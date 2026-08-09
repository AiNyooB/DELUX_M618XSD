#!/usr/bin/env python3
"""
DELUX M618XSD — 自写宏（仅 0x0C+0x08+0x09，不含 0x04/0x05/0x06）

用途：
  - 发送一个简单宏（"按 A 键"）到后退键
  - 仅含唤醒+按键映射+宏数据，不会触发断连
  - 配合 USBPcap 抓包，与官方软件抓包对比

用法：
  python macro_write_simple.py                  # 发送到设备
  python macro_write_simple.py --dry-run        # 只打印不发
  python macro_write_simple.py --key B          # 改按 B 键
  python macro_write_simple.py --macro-id 1     # 改宏 ID
  python macro_write_simple.py --btn 3          # 改按钮索引
"""
import argparse
import ctypes
import os
import sys
import time

# ── 路径 ──
APP = r"C:\Users\fresh\Downloads\618XSD\extracted\app"
DLL_PATH = os.path.join(APP, "hiddriver_ms_4.dll")
VID, PID = 0x1D57, 0xFA60

# ── HID 键盘 Usage ID ──
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

# ── 按钮 entry 索引（UI 编号 → 协议索引） ──
BUTTON_MAP = {
    1: 0,   # 左键
    2: 1,   # 右键
    3: 4,   # 中键
    4: 2,   # 前进
    5: 3,   # 后退
    6: 5,   # DPI 循环
    7: 14,  # 左滚
    8: 15,  # 右滚
    9: 16,  # 上滚
    10: 17, # 下滚
}

# ── 按钮功能编码 ──
FUNC = {
    'NONE':    0x01,
    'LEFT':    0x02,
    'RIGHT':   0x03,
    'MIDDLE':  0x04,
    'BACK':    0x05,
    'FORWARD': 0x06,
    'SCRL_UP': 0x09,
    'SCRL_DN': 0x0A,
    'SCRL_LF': 0x0B,
    'SCRL_RT': 0x0C,
    'DPI':     0x0D,
    'MACRO':   0x12,
}

# ═══════════════════════════════════════════════════════════════
# 报告构建
# ═══════════════════════════════════════════════════════════════

def build_wakeup() -> bytes:
    """0x0C 唤醒/初始化报告（10 字节）"""
    return bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0, 0, 0, 0])

def build_08_report(btn_entry: int, macro_id: int) -> bytes:
    """
    构建 0x08 按键映射报告（59 字节）。

    btn_entry: 按钮在协议中的 entry 索引 (0..17)
    macro_id:  宏 ID (1..255)
    """
    # 默认 entry 表（与官方软件一致）
    entries = bytearray(54)  # 18 × 3
    default = [
        (0x02, 0, 0),  # [0]  左键
        (0x03, 0, 0),  # [1]  右键
        (0x06, 0, 0),  # [2]  前进
        (0x12, 0, 4),  # [3]  后退（宏 ID 4，官方默认）
        (0x04, 0, 0),  # [4]  中键
        (0x0D, 0, 0),  # [5]  DPI 循环
        (0x01, 0, 0),  # [6]  未使用
        (0x01, 0, 0),  # [7]
        (0x01, 0, 0),  # [8]
        (0x01, 0, 0),  # [9]
        (0x01, 0, 0),  # [10]
        (0x01, 0, 0),  # [11]
        (0x01, 0, 0),  # [12]
        (0x01, 0, 0),  # [13]
        (0x0B, 0, 0),  # [14] 左滚
        (0x0C, 0, 0),  # [15] 右滚
        (0x09, 0, 0),  # [16] 上滚
        (0x0A, 0, 0),  # [17] 下滚
    ]
    for i, (func, p1, p2) in enumerate(default):
        entries[i * 3] = func
        entries[i * 3 + 1] = p1
        entries[i * 3 + 2] = p2

    # 覆盖目标按钮为宏绑定
    entries[btn_entry * 3]     = 0x12        # 功能码 = 宏
    entries[btn_entry * 3 + 1] = 0x00        # param1 = 0x00
    entries[btn_entry * 3 + 2] = macro_id & 0xFF  # param2 = 宏 ID

    report = bytearray(59)
    report[0] = 0x08
    report[1] = 0x3B
    report[2] = 0x01
    report[3:57] = entries

    # 校验和：sum(report[3:57]) 大端
    cksum = sum(report[3:57]) & 0xFFFF
    report[57] = (cksum >> 8) & 0xFF
    report[58] = cksum & 0xFF

    return bytes(report)

def build_09_internal(macro_id: int, play_mode: int,
                      keycodes: list[int], modifier: int = 0x01) -> bytearray:
    """
    构建 0x09 内部缓冲（131 字节）。
    每个按键产生 2 字节输出：[keycode, flag|delay]（实测顺序，见官方抓包验证）。

    keycodes: 按键码列表，每项产生一个按下+释放对
    play_mode: 0x00=循环次数播放, 0x01=任意键停止
    modifier: 修饰键字节（默认 0x01，与官方一致）
    """
    buf = bytearray(131)

    buf[0] = 0x09
    buf[1] = 0x83          # 发送时被覆盖为 0x40
    buf[2] = macro_id & 0xFF
    buf[3] = play_mode & 0xFF
    buf[4] = 0x00          # action[0]
    buf[5] = 0x00          # action[1]
    buf[6] = 0x00          # action[2]

    m = modifier & 0xFF
    if m == 0:
        m = 0x01           # 官方用 0x01
    buf[7] = m

    # cmd[8..27] = 20 字节保留（全零）
    # cmd[28] = 按键对数量, cmd[29] = 固定 0x01, cmd[30+] = 按键数据
    buf[28] = len(keycodes) * 2  # 按下+释放 = 2 对
    buf[29] = 0x01               # 固定字节（与官方一致）

    # 按键对从 offset 30 开始
    # 每对 2 字节：[keycode, flag|delay]（实测顺序！）
    offset = 30
    for kc in keycodes:
        if offset + 1 >= 129:
            break
        # 按下：keycode, flag=0x81(press) | delay=0x01(min)
        buf[offset] = kc & 0xFF       # keycode 在前
        buf[offset + 1] = 0x81        # flag|delay 在后
        offset += 2
        # 释放：keycode, flag=0x00(release) | delay=0x00
        if offset + 1 >= 129:
            break
        buf[offset] = kc & 0xFF       # keycode 在前
        buf[offset + 1] = 0x00        # flag|delay 在后
        offset += 2

    # 校验和：sum(buf[3:129]) 大端
    cksum = sum(buf[3:129]) & 0xFFFF
    buf[129] = (cksum >> 8) & 0xFF
    buf[130] = cksum & 0xFF

    return buf

def build_09_chunks(internal: bytearray) -> list[bytes]:
    """
    将 131 字节内部缓冲拆分为 3 个 64 字节线上报告。
    """
    # chunk0: cmd[3..0x3E] 共 60 字节
    c0 = bytearray(64)
    c0[0] = 0x09
    c0[1] = 0x40
    c0[2] = internal[2]   # macro_id
    c0[3] = 0              # chunk index
    c0[4:64] = internal[3:0x3F]
    # chunk1: cmd[0x3F..0x7A] 共 60 字节
    c1 = bytearray(64)
    c1[0] = 0x09
    c1[1] = 0x40
    c1[2] = internal[2]
    c1[3] = 1
    c1[4:64] = internal[0x3F:0x7B]
    # chunk2: cmd[0x7B..0x82] 共 8 字节 + 52 字节 00 填充
    # 官方用 0x0C 替代 0x40（可能是"提交"标记）
    c2 = bytearray(64)
    c2[0] = 0x09
    c2[1] = 0x0C
    c2[2] = internal[2]
    c2[3] = 2
    c2[4:12] = internal[0x7B:0x83]
    return [bytes(c0), bytes(c1), bytes(c2)]

def build_full_sequence(btn_entry: int, macro_id: int,
                        keycodes: list[int], play_mode: int = 0x01) -> list[bytes]:
    """构建完整写入序列（唤醒 + 按键映射 + 宏数据 × 3）"""
    wake = build_wakeup()
    r08 = build_08_report(btn_entry, macro_id)
    internal = build_09_internal(macro_id, play_mode, keycodes)
    chunks = build_09_chunks(internal)
    return [wake, r08] + chunks

# ═══════════════════════════════════════════════════════════════
# 发送
# ═══════════════════════════════════════════════════════════════

def send_sequence(seq: list[bytes], delay: float = 0.2):
    """通过 hiddriver DLL 发送报告序列"""
    if not os.path.exists(DLL_PATH):
        print(f"  DLL 不存在: {DLL_PATH}")
        sys.exit(1)

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
        print(f"  [{i}] ({len(data)}B) Report ID=0x{data[0]:02X} → 返回={r}")
        if delay > 0 and i < len(seq) - 1:
            time.sleep(delay)

    dll.Close_FeatureDevice()
    print("  ✅ 设备已关闭")

# ═══════════════════════════════════════════════════════════════
# 命令行
# ═══════════════════════════════════════════════════════════════

def main():
    parser = argparse.ArgumentParser(
        description="M618XSD 自写宏（仅 0x0C+0x08+0x09，不含 0x04/0x05/0x06）")
    parser.add_argument('--key', '-k', default='A',
                        help='按键 (默认 A)')
    parser.add_argument('--btn', '-b', type=int, default=4,
                        help='UI 按钮编号 (默认 4=前进键)')
    parser.add_argument('--macro-id', '-m', type=int, default=1,
                        help='宏 ID (默认 1)')
    parser.add_argument('--playback', '-p', type=lambda x: int(x, 16), default=0x01,
                        help='播放方式: 0x00=循环, 0x01=任意键停止 (默认 0x01)')
    parser.add_argument('--dry-run', '-n', action='store_true',
                        help='只打印不发')
    args = parser.parse_args()

    key = args.key.upper()
    if key not in HID_KEYS:
        print(f"错误: 不支持的按键 '{key}'，支持: {', '.join(HID_KEYS.keys())}")
        sys.exit(1)

    if args.btn not in BUTTON_MAP:
        print(f"错误: 不支持的按钮编号 {args.btn}，支持: {list(BUTTON_MAP.keys())}")
        sys.exit(1)

    btn_entry = BUTTON_MAP[args.btn]
    keycode = HID_KEYS[key]
    macro_id = args.macro_id & 0xFF

    print("=" * 60)
    print(f"  M618XSD 自写宏")
    print("=" * 60)
    print(f"  按键:     {key} (0x{keycode:02X})")
    print(f"  按钮:     UI#{args.btn} → entry[{btn_entry}]")
    print(f"  宏 ID:    {macro_id}")
    print(f"  播放方式: 0x{args.playback:02X}")
    print(f"  模式:     {'🖨️ DRY RUN (只打印)' if args.dry_run else '📡 发送到设备'}")

    seq = build_full_sequence(btn_entry, macro_id, [keycode], args.playback)

    print(f"\n  {'─'*58}")
    print(f"  完整序列 ({len(seq)} 条报告)")
    print(f"  {'─'*58}")
    for i, data in enumerate(seq):
        lbl = {0x0C: "唤醒", 0x08: "按键映射", 0x09: "宏数据"}.get(data[0], "?")
        ci = data[3] if data[0] == 0x09 else -1
        ci_str = f" chunk{ci}" if ci >= 0 else ""
        print(f"  [{i}] {lbl}{ci_str} ({len(data)}B): {data.hex(' ')}")

    # 校验和验证
    print(f"\n  {'─'*58}")
    print(f"  校验和验证")
    print(f"  {'─'*58}")
    for data in seq:
        if data[0] == 0x08:
            cs = (data[57] << 8) | data[58]
            calc = sum(data[3:57]) & 0xFFFF
            ok = "✅" if cs == calc else "❌"
            print(f"  0x08: 报告=0x{cs:04X}  计算=0x{calc:04X}  {ok}")
        elif data[0] == 0x09 and data[3] == 2:  # chunk2 有校验和
            cs = (data[10] << 8) | data[11]
            # 校验和覆盖 chunk0~2 的完整内部缓冲
            # 重新计算内部缓冲
            kcs = [HID_KEYS[key.upper()]]
            internal = build_09_internal(macro_id, args.playback, kcs)
            calc = sum(internal[3:129]) & 0xFFFF
            ok = "✅" if cs == calc else "❌"
            print(f"  0x09(chunk2): 报告=0x{cs:04X}  计算=0x{calc:04X}  {ok}")

    if not args.dry_run:
        print(f"\n  {'─'*58}")
        print(f"  ⚠️  确保 Mouse.exe 已关闭！")
        print(f"  {'─'*58}")
        send_sequence(seq)
        print(f"\n  ✅ 发送完成！按后退键测试。")
        print(f"  💡 如果不好使，用官方软件恢复。")
    else:
        print(f"\n  💡 去掉 --dry-run 发送到设备。")

if __name__ == '__main__':
    main()