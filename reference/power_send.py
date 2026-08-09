# -*- coding: utf-8 -*-
"""
电源管理命令发送端 — 32位 Python。
由 PowerShell GUI 调用，向鼠标发送 0x05 报告。

用法:
  python power_send.py --sleep <分钟> --standby <分钟>
  python power_send.py --standby 1            # 只改一级休眠
  python power_send.py --sleep 1              # 只改睡眠时间
  python power_send.py --restore              # 恢复默认(睡眠10分, 一级休眠0.5分)

说明:
  0x05 报告共 15 字节，包含 灯光(byte3/4) + 睡眠(byte5) + 一级休眠(byte9) + 去抖(byte10)。
  本工具改电源字段时，用固定安全值保留灯光/去抖，不干扰其他设置。

  ⚠️ 注意：电源管理设置（byte5/byte9）通过自定义命令发送时，设备反应不稳定。
  有时生效、有时被忽略、有时触发异常行为（深睡而非浅睡）。
  2026-08-07 实测中还发生过 2.4G 断联（固件崩溃），需拔插接收器恢复。
  建议：电源管理仍通过官方软件（Mouse.exe）操作。
  本工具主要用于实验/验证目的，使用风险自负。
"""
import argparse
import ctypes
import os
import sys
import time

DLL_PATH = os.path.join(
    r"C:\Users\fresh\Downloads\618XSD\extracted\app", "hiddriver_ms_4.dll"
)
VID, PID = 0x1D57, 0xFA60

# 非电源字段的安全默认值（保留灯光/去抖，见 HID协议逆向报告 3.2 节）
# 注意: byte3=0x04(霓虹+移动关灯开启) 是官方软件在设电源时的标准值
# 用其他灯光模式(如0x02常亮DPI)会导致设备不响应休眠设置
LIGHT_BYTE3 = 0x04   # 霓虹模式 + 移动关灯开启
LIGHT_BYTE4 = 0x03   # 呼吸速度 6 (9-6=3)
DEBOUNCE_BYTE10 = 0x03  # 去抖 6ms
FIXED_BYTE5 = 0xA8   # 睡眠 10 分钟（恢复默认用）
FIXED_BYTE9 = 0x01   # 一级休眠 0.5 分钟（官方默认值）


def calc_checksum(data: list[int]) -> tuple[int, int]:
    """16位累加校验和，返回(高字节, 低字节)"""
    total = sum(data) & 0xFFFF
    return (total >> 8) & 0xFF, total & 0xFF


def build_05_report(byte3, byte4, byte5, byte9, byte10) -> bytes:
    """构建完整 0x05 报告"""
    data = [byte3, byte4, byte5, 0x00, 0x00, 0xFF, byte9, byte10]
    csum_h, csum_l = calc_checksum(data)
    return bytes([0x05, 0x0F, 0x01] + data + [csum_h, csum_l, 0x00, 0x00])


def send(dll, report: bytes, label: str) -> bool:
    r = dll.SetFeature(report, len(report))
    ok = r == 1
    print(f"[{'OK' if ok else 'FAIL'}] {label}: {report.hex(' ')}")
    return ok


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--sleep", type=float, default=None, help="睡眠时间(分钟，整数)")
    parser.add_argument("--standby", type=float, default=None, help="一级休眠(分钟，支持0.5)")
    parser.add_argument("--restore", action="store_true", help="恢复默认")
    args = parser.parse_args()

    # 参数校验
    if args.restore:
        byte5, byte9 = FIXED_BYTE5, FIXED_BYTE9
        sleep_min, standby_min = 10, 0.5
    elif args.sleep is None and args.standby is None:
        print("[FAIL] 需要 --sleep 或 --standby 或 --restore")
        return 1
    else:
        # 未指定的字段用安全默认值
        # 睡眠编码 (分钟<<4)|0x08 最大只支持 15 分钟(0xF8)，超出会溢出到 >255
        sleep_min = args.sleep if args.sleep is not None else 10
        standby_min = args.standby if args.standby is not None else 1

        # 检测倒置：一级休眠 >= 睡眠 时设备可能行为异常
        if standby_min >= sleep_min and args.sleep is not None:
            print(f"[WARN] 一级休眠({standby_min}分) >= 睡眠({sleep_min}分)，设备可能异常！")
            # 自动修正：把"其他"字段改成安全值
            if args.standby is not None and args.sleep is None:
                # 只指定了休眠，睡眠是"其他"字段，把睡眠改成大于休眠的值
                sleep_min = max(sleep_min, standby_min + 5)
                print(f"      自动修正: 睡眠改为 {sleep_min}分")
            elif args.sleep is not None:
                # 只指定了睡眠（或两者都指定），休眠是其他字段，改成小于睡眠
                standby_min = 0.5
                print(f"      自动修正: 一级休眠改为 0.5分")

        byte5 = int((int(sleep_min) << 4) | 0x08) if sleep_min >= 1 else 0x18
        if sleep_min < 1:
            byte5 = 0x18
        # 一级休眠 byte9 编码（根因已定位，2026-08-08）：
        #   固件把 byte9 当 1-based 的 0.5 分档计数：实际分钟 = (byte9 - 1) × 0.5
        #   PC 端（含官方 Mouse.exe）编码为 分钟×2，与固件 off-by-one → 每档系统性 -0.5 分
        #   反汇编证据（mouse_analysis3.txt 0x417E60）：byte9 从结构体 [edi+0x95d] 原样透传，
        #   发送端无任何 ×2/-1 运算，偏移纯属固件解码行为。官方软件发相同字节也实测 ≈1.5 分（非 2 分）。
        #   修正：无条件 +1，使 实际 = (byte9-1)×0.5 = (分钟×2+1-1)×0.5 = 分钟。
        byte9 = int(round(standby_min * 2)) + 1
        if byte9 < 1:
            byte9 = 0x01
        # 钳位到合法字节范围，防止溢出
        byte5 = max(0, min(byte5, 0xFF))
        byte9 = max(0, min(byte9, 0xFF))

    report = build_05_report(
        byte3=LIGHT_BYTE3,
        byte4=LIGHT_BYTE4,
        byte5=byte5,
        byte9=byte9,
        byte10=DEBOUNCE_BYTE10,
    )

    dll = ctypes.WinDLL(DLL_PATH)
    dll.Set_VIDPID.argtypes = [ctypes.c_uint, ctypes.c_uint]
    dll.Set_VIDPID.restype = ctypes.c_int
    dll.Open_DevMonitor.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_uint]
    dll.Open_DevMonitor.restype = ctypes.c_int
    dll.Close_DevMonitor.restype = ctypes.c_int
    dll.Open_FeatureDevice.restype = ctypes.c_int
    dll.SetFeature.argtypes = [ctypes.c_char_p, ctypes.c_uint]
    dll.SetFeature.restype = ctypes.c_int
    dll.Close_FeatureDevice.restype = ctypes.c_int

    dll.Set_VIDPID(VID, PID)

    # ⚠️ 电源管理前置条件：必须先打开数据设备(0x0A)，否则电源设置被忽略甚至断联
    # 官方初始化序列: Open_DevMonitor()(开0x0A) -> Open_FeatureDevice()(开0x0B)
    user32 = ctypes.windll.user32
    hwnd = user32.GetDesktopWindow()
    dll.Open_DevMonitor(hwnd, 0, 0)
    time.sleep(1)

    if not dll.Open_FeatureDevice():
        print("[FAIL] Open_FeatureDevice 失败（鼠标休眠/未连接？）")
        dll.Close_DevMonitor()
        return 1

    # 唤醒（复刻官方时序：0x0C×2 间隔500ms，然后等1s才发配置）
    wake = bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0, 0, 0, 0])
    send(dll, wake, "唤醒 #1")
    time.sleep(0.5)
    send(dll, wake, "唤醒 #2")
    time.sleep(1.0)  # 官方必等 ~1s 后发配置，短于这个时间设备可能未就绪

    # 发送 0x05
    desc = f"0x05(睡眠{sleep_min}分/一级休眠{standby_min}分)"
    ok = send(dll, report, desc)

    dll.Close_FeatureDevice()
    dll.Close_DevMonitor()
    time.sleep(0.2)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())