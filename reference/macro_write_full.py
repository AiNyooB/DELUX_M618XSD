#!/usr/bin/env python3
"""
DELUX M618XSD — 完整序列宏写入（复制官方完整 9 条报告序列）

完全复制官方"应用"流程的 9 条报告，仅替换宏 ID 和按键数据。
历史上写这段的假设是"必须含 0x04/0x05/0x06 报告，否则缺提交步骤不生效"。

⚠️ 注意（2026-08-04 实机证伪）：该假设已作废。轻量序列
`macro_write_simple.py`（仅 0x0C + 0x08 + 0x09，不含 0x04/0x05/0x06）
在 Mouse.exe 完全退出态下已实测可用、不断连、且不碰 DPI（右滚键→P 实机跑通）。
本"完整"脚本如今仅作对照/兜底用，并非必需。断联的真正主因是官方驱动抢占，而非缺前置报告。

用法:
  python macro_write_full.py --key G           # 写 G 键宏到后退键
  python macro_write_full.py --key G --dry-run # 只打印不发
"""
import argparse
import ctypes
import os
import sys
import time

APP = r"C:\Users\fresh\Downloads\618XSD\extracted\app"
DLL_PATH = os.path.join(APP, "hiddriver_ms_4.dll")
VID, PID = 0x1D57, 0xFA60

HID_KEYS = {
    'A': 0x04, 'B': 0x05, 'C': 0x06, 'D': 0x07,
    'E': 0x08, 'F': 0x09, 'G': 0x0A, 'H': 0x0B,
    'I': 0x0C, 'J': 0x0D, 'K': 0x0E, 'L': 0x0F,
    'M': 0x10, 'N': 0x11, 'O': 0x12, 'P': 0x13,
    'Q': 0x14, 'R': 0x15, 'S': 0x16, 'T': 0x17,
    'U': 0x18, 'V': 0x19, 'W': 0x1A, 'X': 0x1B,
    'Y': 0x1C, 'Z': 0x1D,
    'ENTER': 0x28, 'SPACE': 0x2C,
}

# 默认 entry 表（与官方一致）
DEFAULT_ENTRIES = [
    (0x02, 0, 0),  # [0]  左键
    (0x03, 0, 0),  # [1]  右键
    (0x06, 0, 0),  # [2]  前进
    (0x12, 0, 4),  # [3]  后退（宏，默认 ID 4）
    (0x04, 0, 0),  # [4]  中键
    (0x0D, 0, 0),  # [5]  DPI 循环
    (0x01, 0, 0),  # [6-13] 未使用
    (0x01, 0, 0),
    (0x01, 0, 0),
    (0x01, 0, 0),
    (0x01, 0, 0),
    (0x01, 0, 0),
    (0x01, 0, 0),
    (0x01, 0, 0),
    (0x0B, 0, 0),  # [14] 左滚
    (0x0C, 0, 0),  # [15] 右滚
    (0x09, 0, 0),  # [16] 上滚
    (0x0A, 0, 0),  # [17] 下滚
]


def build_sequence(keycode: int, macro_id: int = 1) -> list[bytes]:
    """构建完整 9 条报告序列"""
    seq = []

    # 1. 0x0C 唤醒
    seq.append(bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0, 0, 0, 0]))

    # 2-3. 0x04 DPI 配置 × 2（来自官方抓包）
    dpi = bytes.fromhex(
        "04 38 01 00 00 1f 10 10 20 b0 40 60 a0 00 00 00"
        " 03 04 06 09 0f 00 00 00 02"
        " ff 00 00 00 ff 00 00 00 ff ff 00 ff ff ff 00 ff 00 ff ff 40 00 ff ff ff"
        " 02 0f ab 00 00 00 00"
    )
    seq.append(dpi)
    seq.append(dpi)  # 发两次

    # 4. 0x05 未知配置（来自官方抓包）
    seq.append(bytes.fromhex("05 0f 01 04 03 a8 00 00 ff 01 03 01 b2 00 00"))

    # 5. 0x06 DPI 选择（来自官方抓包）
    seq.append(bytes.fromhex("06 09 01 02 fd 00 00 00 00"))

    # 6. 0x08 按键映射（替换宏 ID）
    seq.append(build_08(macro_id))

    # 7-9. 0x09 宏数据 × 3（替换按键和宏 ID）
    seq.extend(build_09_chunks(macro_id, keycode))

    return seq


def build_08(macro_id: int) -> bytes:
    """构建 0x08 按键映射报告（59 字节）"""
    entries = bytearray(54)
    for i, (func, p1, p2) in enumerate(DEFAULT_ENTRIES):
        entries[i * 3] = func
        entries[i * 3 + 1] = p1
        entries[i * 3 + 2] = p2

    # 设置宏绑定到后退键（entry[3]）
    entries[3 * 3]     = 0x12        # 宏
    entries[3 * 3 + 1] = 0x00        # param1
    entries[3 * 3 + 2] = macro_id & 0xFF  # 宏 ID

    report = bytearray(59)
    report[0] = 0x08
    report[1] = 0x3B
    report[2] = 0x01
    report[3:57] = entries

    cksum = sum(report[3:57]) & 0xFFFF
    report[57] = (cksum >> 8) & 0xFF
    report[58] = cksum & 0xFF
    return bytes(report)


def build_09_chunks(macro_id: int, keycode: int) -> list[bytes]:
    """构建 0x09 宏数据 3 个 chunk"""
    # 内部缓冲（131 字节）
    buf = bytearray(131)
    buf[0] = 0x09
    buf[1] = 0x83
    buf[2] = macro_id & 0xFF
    buf[3] = 0x01        # 播放方式：任意键停止
    buf[7] = 0x01        # modifier（与官方一致）
    buf[28] = 0x02       # 按键对数量（按下+释放）
    buf[29] = 0x01       # 固定字节
    # 按下：[keycode, flag|delay]
    buf[30] = keycode & 0xFF
    buf[31] = 0x81       # press | delay=1
    # 释放：[keycode, flag|delay]
    buf[32] = keycode & 0xFF
    buf[33] = 0x00       # release | delay=0

    # 校验和
    cksum = sum(buf[3:129]) & 0xFFFF
    buf[129] = (cksum >> 8) & 0xFF
    buf[130] = cksum & 0xFF

    # 拆分为 3 个 64 字节 chunk
    c0 = bytearray(64)
    c0[0] = 0x09; c0[1] = 0x40; c0[2] = macro_id & 0xFF; c0[3] = 0
    c0[4:64] = buf[3:0x3F]

    c1 = bytearray(64)
    c1[0] = 0x09; c1[1] = 0x40; c1[2] = macro_id & 0xFF; c1[3] = 1
    c1[4:64] = buf[0x3F:0x7B]

    c2 = bytearray(64)
    c2[0] = 0x09; c2[1] = 0x40; c2[2] = macro_id & 0xFF; c2[3] = 2
    c2[4:12] = buf[0x7B:0x83]

    return [bytes(c0), bytes(c1), bytes(c2)]


def send_sequence(seq: list[bytes]):
    """通过 hiddriver DLL 发送序列"""
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
    for i, data in enumerate(seq):
        r = dll.SetFeature(ctypes.c_char_p(bytes(data)), ctypes.c_int(len(data)))
        lbl = {0x0C: "唤醒", 0x04: "DPI配置", 0x05: "未知配置",
               0x06: "DPI选择", 0x08: "按键映射", 0x09: "宏数据"}.get(data[0], "?")
        ci = f" chunk{data[3]}" if data[0] == 0x09 else ""
        print(f"  [{i}] {lbl}{ci} ({len(data)}B) → 返回={r}")
        if i < len(seq) - 1:
            time.sleep(0.3)

    dll.Close_FeatureDevice()
    print("  ✅ 设备已关闭")


def main():
    parser = argparse.ArgumentParser(description="M618XSD 完整序列宏写入")
    parser.add_argument('--key', '-k', default='G', help='按键 (默认 G)')
    parser.add_argument('--macro-id', '-m', type=int, default=1, help='宏 ID (默认 1)')
    parser.add_argument('--dry-run', '-n', action='store_true', help='只打印不发')
    args = parser.parse_args()

    key = args.key.upper()
    if key not in HID_KEYS:
        print(f"错误: 不支持 '{key}'")
        sys.exit(1)

    keycode = HID_KEYS[key]
    macro_id = args.macro_id & 0xFF

    seq = build_sequence(keycode, macro_id)

    print("=" * 60)
    print(f"  M618XSD 完整序列宏写入")
    print("=" * 60)
    print(f"  按键:   {key} (0x{keycode:02X})")
    print(f"  宏 ID:  {macro_id}")
    print(f"  模式:   {'🖨️ DRY RUN' if args.dry_run else '📡 发送'}")
    print()

    for i, data in enumerate(seq):
        lbl = {0x0C: "唤醒", 0x04: "DPI配置", 0x05: "未知",
               0x06: "DPI选择", 0x08: "按键映射", 0x09: "宏数据"}.get(data[0], "?")
        ci = f" ch{data[3]}" if data[0] == 0x09 else "   "
        print(f"  [{i}] 0x{data[0]:02X} {lbl}{ci}  {data.hex(' ')}")

    # 校验和验证
    print(f"\n  {'─'*50}")
    for data in seq:
        if data[0] == 0x08:
            cs = (data[57] << 8) | data[58]
            calc = sum(data[3:57]) & 0xFFFF
            ok = "✅" if cs == calc else "❌"
            print(f"  0x08 校验: 0x{cs:04X} 计算=0x{calc:04X} {ok}")
        elif data[0] == 0x09 and data[3] == 2:
            cs = (data[10] << 8) | data[11]
            calc = sum(build_09_chunks(macro_id, keycode)[0][3:129]) & 0xFFFF
            # 实际校验和是内部缓冲的，不是 chunk 的
            buf = bytearray(131)
            buf[3] = 0x01; buf[7] = 0x01
            buf[28] = 0x02; buf[29] = 0x01
            buf[30] = keycode & 0xFF; buf[31] = 0x81
            buf[32] = keycode & 0xFF; buf[33] = 0x00
            calc = sum(buf[3:129]) & 0xFFFF
            ok = "✅" if cs == calc else "❌"
            print(f"  0x09 校验: 0x{cs:04X} 计算=0x{calc:04X} {ok}")

    if not args.dry_run:
        print(f"\n  ⚠️  确保 Mouse.exe 已关闭!")
        send_sequence(seq)
        print(f"\n  ✅ 完成！按后退键测试。")

if __name__ == '__main__':
    main()