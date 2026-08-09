#!/usr/bin/env python3
"""
DELUX M618XSD — 宏播放模式实机验证

验证两项未实机测试的功能：
  1. 循环次数播放（0x00）：按一次鼠标键，宏自动循环 N 次
  2. 按住循环（0x02）：按住鼠标键持续循环，松开停止

延迟编码已嵌入宏的按键对中，无需单独测试。

用法：
  python macro_verify.py loop     # 测试循环次数播放（循环次数=5）
  python macro_verify.py hold     # 测试按住循环
  python macro_verify.py loop --dry-run   # 只打印不发
"""
import argparse
import ctypes
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from macro_generator import MacroBuilder, HID_KEYBOARD, PLAYBACK_LOOP, PLAYBACK_HOLD

APP = r"C:\Users\fresh\Downloads\618XSD\extracted\app"
DLL_PATH = os.path.join(APP, "hiddriver_ms_4.dll")
VID, PID = 0x1D57, 0xFA60

# ═══════════════════════════════════════════════════════════════
# 官方默认 entry 表（18 个按钮，与官方软件一致）
# 避免 build_08_report() 默认全 0x01 清空其他键
# ═══════════════════════════════════════════════════════════════
DEFAULT_ENTRIES = [
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


def build_08_report(btn_entry: int, macro_id: int) -> bytes:
    """构建 0x08 按键映射报告（59 字节），使用官方默认 entry 表"""
    entries = bytearray(54)
    for i, (func, p1, p2) in enumerate(DEFAULT_ENTRIES):
        entries[i * 3] = func
        entries[i * 3 + 1] = p1
        entries[i * 3 + 2] = p2
    # 只覆盖目标按钮为宏绑定
    entries[btn_entry * 3] = 0x12
    entries[btn_entry * 3 + 1] = 0x00
    entries[btn_entry * 3 + 2] = macro_id & 0xFF

    report = bytearray(59)
    report[0] = 0x08
    report[1] = 0x3B
    report[2] = 0x01
    report[3:57] = entries
    cksum = sum(report[3:57]) & 0xFFFF
    report[57] = (cksum >> 8) & 0xFF
    report[58] = cksum & 0xFF
    return bytes(report)


def build_wakeup() -> bytes:
    """0x0C 唤醒报告（10 字节）"""
    return bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0, 0, 0, 0])


def send_sequence(seq: list[bytes], delay: float = 0.2):
    """通过 hiddriver DLL 发送报告序列"""
    if not os.path.exists(DLL_PATH):
        print(f"  ❌ DLL 不存在: {DLL_PATH}")
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
        status = "✅" if r else "❌"
        print(f"  [{i}] ({len(data)}B) Report ID=0x{data[0]:02X} → {status}")
        if delay > 0 and i < len(seq) - 1:
            time.sleep(delay)
    dll.Close_FeatureDevice()
    print("  ✅ 设备已关闭")


def run_test(test_type: str, key: str, btn_ui: int, macro_id: int,
             dry_run: bool, loop_count: int = 5,
             press_delay: int = 500, release_delay: int = 10):
    """构建并发送测试宏"""
    btn_entry = MacroBuilder.BUTTON_MAP[btn_ui]
    keycode = HID_KEYBOARD[key]

    builder = MacroBuilder(button_index=btn_entry, macro_id=macro_id)

    # ── 配置测试参数 ──
    if test_type == 'loop':
        builder.set_playback_mode(PLAYBACK_LOOP)
        builder.set_loop_count(loop_count)
        note = f"按前进键一次 → 应输出 {loop_count} 个 {key}（循环次数={loop_count}）"
        desc = "循环次数播放 (0x00)"
    else:  # hold
        builder.set_playback_mode(PLAYBACK_HOLD)
        builder.set_modifier(0x01)
        note = f"按住前进键 → 持续输出 {key}\n           松开前进键 → 停止输出"
        desc = "按住循环 (0x02)"

    # 一次按键
    builder.add_key_stroke(keycode, press_delay_ms=press_delay, release_delay_ms=release_delay)

    # ── 构建序列 ──
    wake = build_wakeup()
    r08 = build_08_report(btn_entry, macro_id)
    chunks = builder.build_09_chunks()
    seq = [wake, r08] + chunks

    print(f"\n  {'─'*58}")
    print(f"  📋 发送序列 ({len(seq)} 条)")
    print(f"  {'─'*58}")
    for i, data in enumerate(seq):
        lbl = {0x0C: "唤醒", 0x08: "按键映射", 0x09: "宏数据"}.get(data[0], "?")
        ci = data[3] if data[0] == 0x09 else -1
        ci_str = f" chunk{ci}" if ci >= 0 else ""
        print(f"  [{i}] {lbl}{ci_str} ({len(data)}B): {data.hex(' ')}")

    # ── 校验和验证 ──
    print(f"\n  {'─'*58}")
    print(f"  校验和验证")
    print(f"  {'─'*58}")
    for data in seq:
        if data[0] == 0x08:
            cs = (data[57] << 8) | data[58]
            calc = sum(data[3:57]) & 0xFFFF
            ok = "✅" if cs == calc else "❌"
            print(f"  0x08: 报告=0x{cs:04X}  计算=0x{calc:04X}  {ok}")
        elif data[0] == 0x09 and data[3] == 2:  # chunk2 = 校验和
            cs = (data[10] << 8) | data[11]
            internal = builder._build_internal_buffer()
            calc = sum(internal[3:129]) & 0xFFFF
            ok = "✅" if cs == calc else "❌"
            print(f"  0x09: 报告=0x{cs:04X}  计算=0x{calc:04X}  {ok}")

    if dry_run:
        print(f"\n  💡 去掉 --dry-run 发送到设备。")
        return

    # ── 发送 ──
    print(f"\n  {'─'*58}")
    print(f"  ⚠️  确保 Mouse.exe 已关闭！")
    print(f"  {'─'*58}\n")

    send_sequence(seq)

    # ── 验证指引 ──
    print(f"\n  {'─'*58}")
    print(f"  ✅ {desc} 已发送到设备！")
    print(f"  {'─'*58}")
    print(f"  📌 {note}")
    print(f"  💡 如果不好使，用官方软件恢复。\n")


def main():
    parser = argparse.ArgumentParser(description="M618XSD 宏播放模式实机验证")
    parser.add_argument('test', nargs='?', default='loop',
                        choices=['loop', 'hold'],
                        help='测试类型: loop=循环次数, hold=按住循环')
    parser.add_argument('--key', '-k', default='6',
                        help='按键 (默认 6)')
    parser.add_argument('--btn', '-b', type=int, default=4,
                        help='UI 按钮编号 (默认 4=前进键)')
    parser.add_argument('--macro-id', '-m', type=int, default=9,
                        help='宏 ID (默认 9，避免与官方冲突)')
    parser.add_argument('--loop-count', '-c', type=int, default=5,
                        help='循环次数 (默认 5，仅 loop 模式有效)')
    parser.add_argument('--press-delay', type=int, default=500,
                        help='按下延迟 ms (默认 500)')
    parser.add_argument('--release-delay', type=int, default=10,
                        help='释放延迟 ms (默认 10)')
    parser.add_argument('--dry-run', '-n', action='store_true',
                        help='只打印不发')
    args = parser.parse_args()

    key = args.key.upper()
    if key not in HID_KEYBOARD:
        print(f"错误: 不支持的按键 '{key}'")
        sys.exit(1)
    if args.btn not in MacroBuilder.BUTTON_MAP:
        print(f"错误: 不支持的按钮编号 {args.btn}")
        sys.exit(1)

    test_name = {'loop': '循环次数播放 (0x00)', 'hold': '按住循环 (0x02)'}
    print("=" * 60)
    print(f"  M618XSD 宏播放模式实机验证")
    print("=" * 60)
    print(f"  测试:     {test_name[args.test]}")
    print(f"  按键:     {key} (0x{HID_KEYBOARD[key]:02X})")
    print(f"  按钮:     UI#{args.btn} → entry[{MacroBuilder.BUTTON_MAP[args.btn]}]")
    print(f"  宏 ID:    {args.macro_id}")
    if args.test == 'loop':
        print(f"  循环次数: {args.loop_count}")
    print(f"  按下延迟: {args.press_delay}ms")
    print(f"  释放延迟: {args.release_delay}ms")
    print(f"  模式:     {'🖨️ DRY RUN' if args.dry_run else '📡 发送到设备'}")

    run_test(args.test, key, args.btn, args.macro_id, args.dry_run,
             loop_count=args.loop_count,
             press_delay=args.press_delay,
             release_delay=args.release_delay)


if __name__ == '__main__':
    main()