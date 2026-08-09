# -*- coding: utf-8 -*-
"""
灯光模块实机测试 — DELUX M618XSD
用法:
  python light_set.py <mode> [speed] [move_off] [sleep]

mode: 0=关闭, 1=呼吸, 2=常亮, 3=循环呼吸, 4=霓虹
speed: 4-8 (默认6)
move_off: 0=开启(默认), 1=关闭
sleep: 一级休眠分钟数 1=0.5min, 2=1min, 3=2min... (默认1)

示例:
  python light_set.py 1          # 呼吸模式, 默认速度6, 移动关灯开启
  python light_set.py 4 8        # 霓虹, 速度最快
  python light_set.py 0 4 1      # 关闭灯光, 速度4, 移动关灯关闭
  python light_set.py 2 6 0 3    # 常亮, 速度6, 移动关灯开启, 休眠=2min
"""
import ctypes
import os
import sys
import time

DLL_PATH = os.path.join(
    r"C:\Users\fresh\Downloads\618XSD\extracted\app", "hiddriver_ms_4.dll"
)
VID, PID = 0x1D57, 0xFA60

MODE_NAMES = ["关闭", "呼吸DPI", "常亮DPI", "循环呼吸", "霓虹"]


def calc_checksum(data: list[int]) -> tuple[int, int]:
    """16位累加校验和，返回(高字节, 低字节)"""
    total = sum(data) & 0xFFFF
    return (total >> 8) & 0xFF, total & 0xFF


def build_05_report(mode: int, speed: int, move_off: bool, sleep: int) -> bytes:
    """
    构建 0x05 灯光+电源管理报告
    mode: 0-4
    speed: 4-8
    move_off: True=关闭移动关灯, False=开启
    sleep: 一级休眠编码
    """
    byte3 = mode & 0x0F
    if move_off:
        byte3 |= 0x80  # bit7 = 关闭移动关灯

    byte4 = 9 - speed  # 速度编码: byte4 = 9 - speed

    # bytes 5-10: 固定值 + 休眠
    data = [byte3, byte4, 0xA8, 0x00, 0x00, 0xFF, sleep, 0x03]
    csum_h, csum_l = calc_checksum(data)

    report = bytes([0x05, 0x0F, 0x01] + data + [csum_h, csum_l, 0x00, 0x00])
    return report


def send_light(mode: int, speed: int = 6, move_off: bool = False, sleep: int = 1):
    """发送灯光配置到鼠标"""
    if mode < 0 or mode > 4:
        print(f"❌ 无效模式: {mode}，可选 0-4")
        return False
    if speed < 4 or speed > 8:
        print(f"❌ 无效速度: {speed}，可选 4-8")
        return False

    print(f"灯光模式: {MODE_NAMES[mode]} ({mode})")
    print(f"呼吸速度: {speed} (byte4=0x{9-speed:02X})")
    print(f"移动关灯: {'关闭' if move_off else '开启'}")
    print(f"一级休眠: {sleep}")

    dll = ctypes.WinDLL(DLL_PATH)
    dll.Set_VIDPID.argtypes = [ctypes.c_uint, ctypes.c_uint]
    dll.Set_VIDPID.restype = ctypes.c_int
    dll.Open_FeatureDevice.restype = ctypes.c_int
    dll.SetFeature.argtypes = [ctypes.c_char_p, ctypes.c_uint]
    dll.SetFeature.restype = ctypes.c_int
    dll.Close_FeatureDevice.restype = ctypes.c_int

    dll.Set_VIDPID(VID, PID)
    if not dll.Open_FeatureDevice():
        print("❌ Open_FeatureDevice 失败（鼠标可能休眠/未连接）")
        return False

    # 1. 发送 0x0C 唤醒
    wake = bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0, 0, 0, 0])
    print(f"\n[1/2] 唤醒: {wake.hex(' ')}")
    r = dll.SetFeature(wake, len(wake))
    print(f"      结果: {r}")
    time.sleep(0.3)

    # 2. 发送 0x05 灯光配置
    report = build_05_report(mode, speed, move_off, sleep)
    print(f"[2/2] 灯光: {report.hex(' ')}")
    r = dll.SetFeature(report, len(report))
    print(f"      结果: {r}")

    dll.Close_FeatureDevice()
    return r == 1


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return

    mode = int(sys.argv[1])
    speed = int(sys.argv[2]) if len(sys.argv) > 2 else 6
    move_off = len(sys.argv) > 3 and sys.argv[3] == "1"
    sleep = int(sys.argv[4]) if len(sys.argv) > 4 else 1

    send_light(mode, speed, move_off, sleep)


if __name__ == "__main__":
    main()