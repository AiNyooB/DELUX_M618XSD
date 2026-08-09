# -*- coding: utf-8 -*-
"""
电源管理实机测试 — DELUX M618XSD
验证睡眠时间(byte5)和一级休眠(byte9)在设备端是否实际生效。

用法:
  python power_test.py <test> [minutes]

测试:
  sleep     — 测试睡眠时间 (byte5)，默认 1 分钟
  standby   — 测试一级休眠 (byte9)，默认 1 分钟
  both      — 先测一级休眠再测睡眠（需较长时间）
  default   — 恢复默认值（睡眠10min, 一级休眠1min）
"""
import ctypes
import os
import sys
import time

DLL_PATH = os.path.join(
    r"C:\Users\fresh\Downloads\618XSD\extracted\app", "hiddriver_ms_4.dll"
)
VID, PID = 0x1D57, 0xFA60


def calc_checksum(data: list[int]) -> tuple[int, int]:
    """16位累加校验和，返回(高字节, 低字节)"""
    total = sum(data) & 0xFFFF
    return (total >> 8) & 0xFF, total & 0xFF


def build_05_report(byte3, byte4, byte5, byte9, byte10) -> bytes:
    """
    构建完整的 0x05 灯光+电源管理报告
    byte3: 灯光模式(低4位) + 移动关灯(bit7)
    byte4: 呼吸速度编码 (9 - speed)
    byte5: 睡眠时间编码 (分钟×16 + 8)
    byte9: 一级休眠编码 (分钟×2, 01=0.5min)
    byte10: 去抖编码 (去抖值÷2)
    """
    data = [byte3, byte4, byte5, 0x00, 0x00, 0xFF, byte9, byte10]
    csum_h, csum_l = calc_checksum(data)
    report = bytes([0x05, 0x0F, 0x01] + data + [csum_h, csum_l, 0x00, 0x00])
    return report


def send_wakeup(dll) -> bool:
    """发送 0x0C 唤醒报告"""
    wake = bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0, 0, 0, 0])
    r = dll.SetFeature(wake, len(wake))
    return r == 1


def open_device(dll) -> bool:
    dll.Set_VIDPID(VID, PID)
    if not dll.Open_FeatureDevice():
        print("❌ Open_FeatureDevice 失败")
        return False
    return True


def send_report(dll, report: bytes, label: str) -> bool:
    r = dll.SetFeature(report, len(report))
    print(f"  {label}: {report.hex(' ')} → {'✅' if r else '❌'}")
    return r == 1


def wait_with_prompt(minutes: int, label: str):
    """等待并提示用户观察"""
    print(f"\n⏳ 等待 {minutes} 分钟 ({label})...")
    print(f"   请观察鼠标 OLED 屏幕是否熄灭")
    for i in range(minutes, 0, -1):
        print(f"   ⏱ 剩余 {i} 分钟...", end="\r")
        time.sleep(60)
    print("\n✅ 等待结束")
    print("   👀 OLED 是否已熄灭？")
    print("   🖱 移动鼠标，看是否恢复（OLED 亮起、DPI 显示正常）")


def test_sleep(dll, minutes: int):
    """测试睡眠时间 (byte5)"""
    print(f"\n{'='*60}")
    print(f"测试：睡眠时间 = {minutes} 分钟")
    print(f"{'='*60}")

    # 一级休眠设长（60分钟=0x78），避免其先触发
    byte5 = (minutes << 4) | 0x08  # 睡眠时间编码
    byte9 = 0x78  # 一级休眠=60分钟，防止干扰
    report = build_05_report(
        byte3=0x02,    # 常亮DPI模式
        byte4=0x03,    # 速度6 (9-6=3)
        byte5=byte5,
        byte9=byte9,
        byte10=0x03,   # 去抖6ms
    )

    send_report(dll, report, f"睡眠{minutes}min(byte5=0x{byte5:02X})")
    print(f"   一级休眠已设为60分钟，避免干扰")
    time.sleep(0.5)

    # 再发一次唤醒，确保鼠标已接收
    send_wakeup(dll)
    time.sleep(1)

    wait_with_prompt(minutes, "睡眠时间测试")
    input("\n按 Enter 继续...")


def test_standby(dll, minutes: int):
    """测试一级休眠 (byte9)"""
    print(f"\n{'='*60}")
    print(f"测试：一级休眠 = {minutes} 分钟")
    print(f"{'='*60}")

    # 睡眠时间设长（60分钟=0x3E8），避免其先触发
    byte5 = 0x3E8 & 0xFF  # 60分钟编码：60×16+8=0x3E8，但只取低8位... 
    # 等等，60×16+8=968=0x3C8，超过255了。byte5 是一个字节，所以最大值是 0xFF=255
    # 分钟×16+8 ≤ 255 → 分钟 ≤ 15.4，所以最大15分钟
    byte5 = 0xF8  # 15分钟 (15×16+8=248=0xF8)
    byte9 = minutes << 1  # 一级休眠：分钟×2，1min=0x02
    if minutes < 1:
        byte9 = 0x01  # 0.5min

    report = build_05_report(
        byte3=0x02,    # 常亮DPI模式
        byte4=0x03,    # 速度6
        byte5=byte5,   # 睡眠15分钟，避免干扰
        byte9=byte9,
        byte10=0x03,   # 去抖6ms
    )

    send_report(dll, report, f"一级休眠{minutes}min(byte9=0x{byte9:02X})")
    print(f"   睡眠时间已设为15分钟，避免干扰")

    # 等待
    wait_time = max(minutes, 1)
    wait_with_prompt(wait_time, "一级休眠测试")
    input("\n按 Enter 继续...")


def restore_default(dll):
    """恢复默认电源管理设置"""
    print(f"\n{'='*60}")
    print("恢复默认值：睡眠10min, 一级休眠1min")
    print(f"{'='*60}")

    report = build_05_report(
        byte3=0x02,    # 常亮DPI
        byte4=0x03,    # 速度6
        byte5=0xA8,    # 睡眠10分钟 (10×16+8=168=0xA8)
        byte9=0x02,    # 一级休眠1分钟
        byte10=0x03,   # 去抖6ms
    )
    send_report(dll, report, "恢复默认")
    print("✅ 已恢复默认电源管理设置")


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return

    test = sys.argv[1]
    minutes = int(sys.argv[2]) if len(sys.argv) > 2 else 1

    dll = ctypes.WinDLL(DLL_PATH)
    dll.Set_VIDPID.argtypes = [ctypes.c_uint, ctypes.c_uint]
    dll.Set_VIDPID.restype = ctypes.c_int
    dll.Open_FeatureDevice.restype = ctypes.c_int
    dll.SetFeature.argtypes = [ctypes.c_char_p, ctypes.c_uint]
    dll.SetFeature.restype = ctypes.c_int
    dll.Close_FeatureDevice.restype = ctypes.c_int

    print(f"🔌 打开设备...")
    if not open_device(dll):
        return

    # 先唤醒
    send_wakeup(dll)
    time.sleep(0.5)

    if test == "sleep":
        test_sleep(dll, minutes)
    elif test == "standby":
        test_standby(dll, minutes)
    elif test == "both":
        test_standby(dll, minutes)
        print("\n重启设备后继续...")
        input("按 Enter 开始睡眠时间测试...")
        time.sleep(0.5)
        send_wakeup(dll)
        time.sleep(0.5)
        test_sleep(dll, minutes)
    elif test == "default":
        restore_default(dll)
    else:
        print(f"❌ 未知测试: {test}")
        print(__doc__)

    dll.Close_FeatureDevice()
    print("\n✅ 测试完成")


if __name__ == "__main__":
    main()