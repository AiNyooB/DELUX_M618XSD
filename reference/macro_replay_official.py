#!/usr/bin/env python3
"""
回放官方抓包中的 0x08 + 0x09 报告（仅 0x0C+0x08+0x09，不含断连报告）。

从官方抓包（其他_2.pcapng）提取的 0x08 和 0x09 报告，按官方时序发送。
用于验证：是数据问题还是上下文问题（是否需要全量报告）。

用法:
  python macro_replay_official.py
  python macro_replay_official.py --dry-run
"""
import argparse
import ctypes
import os
import sys
import time

APP = r"C:\Users\fresh\Downloads\618XSD\extracted\app"
DLL_PATH = os.path.join(APP, "hiddriver_ms_4.dll")
VID, PID = 0x1D57, 0xFA60

# 来自官方抓包（其他_2.pcapng）的 0x08 报告
# 后退键 → 宏 ID 4
OFFICIAL_08 = bytes.fromhex(
    "08 3b 01 02 00 00 03 00 00 06 00 00 12 00 04 04 00 00 0d 00 00"
    " 01 00 00 01 00 00 01 00 00 01 00 00 01 00 00 01 00 00 01 00 00"
    " 01 00 00 0b 00 00 0c 00 00 09 00 00 0a 00 00 00 64"
)

# 来自官方抓包的 0x09 宏数据（G 键，宏 ID 4）
OFFICIAL_09_CHUNKS = [
    bytes.fromhex(
        "09 40 04 00 01 00 00 00 01 00 00 00 00 00 00 00"
        " 00 00 00 00 00 00 00 00 00 00 00 00 00 02 01 0a"
        " 81 0a 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
        " 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
    ),
    bytes.fromhex(
        "09 40 04 01 00 00 00 00 00 00 00 00 00 00 00 00"
        " 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
        " 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
        " 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
    ),
    bytes.fromhex(
        "09 0c 04 02 00 00 00 00 00 00 00 9a 00 00 00 00"
        " 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
        " 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
        " 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
    ),
]

WAKEUP = bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0, 0, 0, 0])


def send_sequence(dry_run: bool = False):
    """按官方时序发送 0x0C + 0x08 + 0x09×3"""
    if not dry_run:
        dll = ctypes.WinDLL(DLL_PATH)
        dll.Set_VIDPID.argtypes = [ctypes.c_ushort, ctypes.c_ushort]
        dll.Set_VIDPID.restype = ctypes.c_int
        dll.Open_FeatureDevice.restype = ctypes.c_int
        dll.SetFeature.argtypes = [ctypes.c_char_p, ctypes.c_int]
        dll.SetFeature.restype = ctypes.c_int
        dll.Close_FeatureDevice.restype = ctypes.c_int

        dll.Set_VIDPID(VID, PID)
        if not dll.Open_FeatureDevice():
            print("  ❌ Open_FeatureDevice 失败")
            sys.exit(1)
        print("  ✅ 设备已打开")

    # 按官方时序：每条报告之间 1 秒
    seq = [("唤醒", WAKEUP, 0)]
    seq.append(("按键映射", OFFICIAL_08, 1.0))
    for i, chunk in enumerate(OFFICIAL_09_CHUNKS):
        seq.append((f"宏数据 ch{i}", chunk, 1.0))

    for name, data, delay in seq:
        print(f"  {name} ({len(data)}B): {data.hex(' ')}")
        if not dry_run:
            r = dll.SetFeature(ctypes.c_char_p(bytes(data)), ctypes.c_int(len(data)))
            print(f"    → 返回={r}")
            if delay > 0:
                time.sleep(delay)

    if not dry_run:
        dll.Close_FeatureDevice()
        print("  ✅ 设备已关闭")


def main():
    parser = argparse.ArgumentParser(description="回放官方宏抓包")
    parser.add_argument('--dry-run', '-n', action='store_true', help='只打印不发')
    args = parser.parse_args()

    print("=" * 60)
    print("  M618XSD 回放官方宏抓包")
    print("=" * 60)
    print(f"  模式: {'🖨️ DRY RUN' if args.dry_run else '📡 发送到设备'}")
    print(f"  宏:   G 键, 宏 ID 4, 后退键触发")
    print(f"  数据: 来自官方抓包（其他_2.pcapng）")
    print()

    send_sequence(dry_run=args.dry_run)

    if not args.dry_run:
        print(f"\n  ✅ 完成！按后退键测试。")
        print(f"  💡 如果这也不行，说明问题不在数据格式，而在")
        print(f"      设备需要完整的窗口/消息循环上下文。")


if __name__ == '__main__':
    main()