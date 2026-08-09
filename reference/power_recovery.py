# -*- coding: utf-8 -*-
"""
电源触发判定脚本 — 发送「完整锚定序列」验证电源(一级休眠/睡眠)是否生效。

背景
----
历史结论（MouseDriverClient状态记录.md 第10节）：单独发 0x05 设备**可能不提交**，
只有完整序列 0x0C→0x04→0x05→0x06→0x08 被验证能生效。
本脚本复刻该序列（light_recovery.py 基座），只把 0x05 的电源字段改成要测的值，
用于判定"电源管理全部档不生效"的真因是否 = 单发 0x05 不提交。

用法（32位 Python）:
  python power_recovery.py --standby 1 --sleep 5
  python power_recovery.py --standby 3
  python power_recovery.py --sleep 1

判定方法
--------
1) 彻底退出 Mouse.exe（任务管理器确认无进程残留）
2) 运行本脚本设定目标休眠时间
3) 实际等待对应分钟数，观察鼠标是否自动入睡（灯灭/省电/无法唤醒）

- 若能按设定入睡  => 真因是"单独发 0x05 不提交"，把 power_send.py/上位机
  改为完整锚定序列即可解决
- 若仍不入睡      => 怀疑字段语义或设备对 0x05 的提交机制另有要求
"""
import argparse
import ctypes
import os
import struct
import sys
import time

BASE = r"C:\Users\fresh\Downloads\618XSD\extracted\app"
DLL_PATH = os.path.join(BASE, "hiddriver_ms_4.dll")
VID, PID = 0x1D57, 0xFA60

# 灯光等非电源字段，沿用 light_recovery.py 已验证的抓包默认值
# (byte3=0x03 循环呼吸, byte4=0x03 速度6)
LIGHT_BYTE3 = 0x03
LIGHT_BYTE4 = 0x03
DEBOUNCE_BYTE10 = 0x03   # 去抖 6ms


def sleep_byte(minutes: int) -> int:
    """byte5 睡眠时间编码 (分钟<<4)|0x08，支持1~15分钟。"""
    m = int(minutes)
    if m < 1:
        m = 1
    if m > 15:
        m = 15
    return (m << 4) | 0x08


def standby_byte(minutes: float) -> int:
    """
    byte9 一级休眠编码（2026-08-07 实抓映射表）。
    主公式 = round(分钟×2)；仅 2.5 / 3.0 两档官方存在 +1 特判。
    0.5→1,1→2,1.5→3,2→4,5→10；2.5→6,3→7。
    """
    b = int(round(minutes * 2))
    if minutes in (2.5, 3.0):
        b += 1
    if b < 1:
        b = 1
    if b > 0xFF:
        b = 0xFF
    return b


def build_05(byte5: int, byte9: int) -> bytes:
    data = [LIGHT_BYTE3, LIGHT_BYTE4, byte5, 0x00, 0x00, 0xFF, byte9, DEBOUNCE_BYTE10]
    total = sum(data) & 0xFFFF
    csum = ((total >> 8) & 0xFF, total & 0xFF)
    return bytes([0x05, 0x0F, 0x01] + data + [csum[0], csum[1], 0x00, 0x00])


def send(dll, report: bytes, label: str, delay: float = 0.3) -> bool:
    r = dll.SetFeature(report, len(report))
    print("  %-6s : %s -> %s" % (label, report.hex(" "), r))
    time.sleep(delay)
    return r == 1


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--standby", type=float, default=None, help="一级休眠(分钟，支持0.5)")
    p.add_argument("--sleep", type=float, default=None, help="睡眠(分钟，整数1-15)")
    args = p.parse_args()
    if args.standby is None and args.sleep is None:
        print("[FAIL] 至少给 --standby 或 --sleep")
        return 1

    dll = ctypes.WinDLL(DLL_PATH)
    dll.Set_VIDPID.argtypes = [ctypes.c_uint, ctypes.c_uint]
    dll.Set_VIDPID.restype = ctypes.c_int
    dll.Open_FeatureDevice.restype = ctypes.c_int
    dll.SetFeature.argtypes = [ctypes.c_char_p, ctypes.c_uint]
    dll.SetFeature.restype = ctypes.c_int
    dll.Close_FeatureDevice.restype = ctypes.c_int

    dll.Set_VIDPID(VID, PID)
    if not dll.Open_FeatureDevice():
        print("[FAIL] Open_FeatureDevice 失败（鼠标休眠/未连接？）")
        return 1

    print("=== 完整锚定序列 0x0C→0x04→0x05→0x06→0x08 ===")

    # 1. 0x0C 唤醒
    send(dll, bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0, 0, 0, 0]), "唤醒")

    # 2. 0x04 DPI（light_2 抓包默认值，原样保留）
    dpi = bytes.fromhex(
        "04 38 01 00 00 1f 10 10 20 b0 40 60 a0 00 00 00 "
        "03 04 06 09 0f 00 00 00 02 ff 00 00 00 ff 00 00 00 "
        "ff ff 00 ff ff ff 00 ff 00 ff ff 40 00 ff ff ff 02 0f ab 00 00 00 00"
    )
    send(dll, dpi, "DPI")

    # 3. 0x05 灯光+电源（byte5=睡眠, byte9=一级休眠）
    byte5 = sleep_byte(args.sleep) if args.sleep is not None else 0xA8  # 默认睡眠10分
    byte9 = standby_byte(args.standby) if args.standby is not None else 0x02  # 默认1分
    send(dll, build_05(byte5, byte9), "灯光+电源")

    # 4. 0x06 回报率（250Hz）
    send(dll, bytes([0x06, 0x09, 0x01, 0x04, 0xFB, 0x00, 0x00, 0x00, 0x00]), "回报率")

    # 5. 0x08 按键映射（light_2 抓包默认值，原样保留，不误清键表）
    buttons = bytes.fromhex(
        "08 3b 01 02 00 00 03 00 00 06 00 00 05 00 00 04 00 00 "
        "0d 00 00 01 00 00 01 00 00 01 00 00 01 00 00 01 00 00 "
        "01 00 00 01 00 00 01 00 00 0b 00 00 0c 00 00 09 00 00 0a 00 00 00 53"
    )
    send(dll, buttons, "按键")

    dll.Close_FeatureDevice()
    print("=== 完成 ===")
    if args.standby is not None:
        print("请等待至少 %.1f 分钟，观察鼠标是否自动入睡" % args.standby)
    return 0


if __name__ == "__main__":
    sys.exit(main())