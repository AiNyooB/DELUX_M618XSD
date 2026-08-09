#!/usr/bin/env python3
"""
DELUX M618XSD — 宏数据生成器

生成 0x08 按键映射报告和 0x09 宏数据报告（3×64 分块），
可直接通过 hiddriver DLL 或 HIDAPI 写入鼠标。

用法:
  # 命令行生成宏
  python macro_generator.py --key A --playback 0x00 --btn 4

  # 作为模块导入
  from macro_generator import MacroBuilder

  builder = MacroBuilder(button_index=4)
  builder.set_keys([(0x04, 0x81), (0x04, 0x00)])  # A键按下+释放
  builder.set_playback_mode(0x00)  # 循环次数播放
  report_08 = builder.build_08_report()
  chunks_09 = builder.build_09_chunks()
"""
import argparse
import struct
from typing import Optional


# ═══════════════════════════════════════════════════════════════
# HID 键盘按键码 (Usage ID)
# ═══════════════════════════════════════════════════════════════

HID_KEYBOARD = {
    # 字母
    'A': 0x04, 'B': 0x05, 'C': 0x06, 'D': 0x07, 'E': 0x08,
    'F': 0x09, 'G': 0x0A, 'H': 0x0B, 'I': 0x0C, 'J': 0x0D,
    'K': 0x0E, 'L': 0x0F, 'M': 0x10, 'N': 0x11, 'O': 0x12,
    'P': 0x13, 'Q': 0x14, 'R': 0x15, 'S': 0x16, 'T': 0x17,
    'U': 0x18, 'V': 0x19, 'W': 0x1A, 'X': 0x1B, 'Y': 0x1C,
    'Z': 0x1D,
    # 数字
    '1': 0x1E, '2': 0x1F, '3': 0x20, '4': 0x21, '5': 0x22,
    '6': 0x23, '7': 0x24, '8': 0x25, '9': 0x26, '0': 0x27,
    # 功能键
    'ENTER': 0x28, 'ESC': 0x29, 'BACKSPACE': 0x2A, 'TAB': 0x2B,
    'SPACE': 0x2C, 'MINUS': 0x2D, 'EQUAL': 0x2E,
    'LBRACKET': 0x2F, 'RBRACKET': 0x30, 'BACKSLASH': 0x31,
    'SEMICOLON': 0x33, 'QUOTE': 0x34, 'GRAVE': 0x35,
    'COMMA': 0x36, 'DOT': 0x37, 'SLASH': 0x38,
    'CAPSLOCK': 0x39,
    'F1': 0x3A, 'F2': 0x3B, 'F3': 0x3C, 'F4': 0x3D,
    'F5': 0x3E, 'F6': 0x3F, 'F7': 0x40, 'F8': 0x41,
    'F9': 0x42, 'F10': 0x43, 'F11': 0x44, 'F12': 0x45,
    'DELETE': 0x4C,
    # 修饰键 (用于 modifier 字节)
    'LCTRL': 0x01, 'LSHIFT': 0x02, 'LALT': 0x04, 'LGUI': 0x08,
    'RCTRL': 0x10, 'RSHIFT': 0x20, 'RALT': 0x40, 'RGUI': 0x80,
}

# 按键标志
# flag|delay 字节: bit7=0x80按下/0x00释放, bits0-6=延迟编码值
# 编码公式（反汇编 0x004182B0 确认）:
#   delay ≤ 1270ms: encoded = (int)(delay_ms / 100.0 + 0.5), 最小 1
#   delay > 1270ms: encoded = (int)((delay_ms % 200) / 100.0 + 0.5), 最小 1
# 注意: 所有 < 150ms 的延迟(0-149ms)都被编码为 1（最小值），无法区分 10ms/30ms/100ms 等
# ⚠️ 设备端实际解码为 max(10ms, byte×5ms)（2026-08-09 实测），非 byte×100ms
FLAG_PRESS = 0x81  # 按下, 延迟编码=1（对应 < 50ms 或 50-99ms 的延迟）
FLAG_RELEASE = 0x00  # 释放, 延迟编码=0

# 播放方式
PLAYBACK_LOOP = 0x00     # 循环次数播放
PLAYBACK_STOP_KEY = 0x01 # 任意键停止播放
PLAYBACK_HOLD = 0x02     # 按住播放松开停止


class MacroBuilder:
    """构建宏报告的内部缓冲"""

    # 0x08 报告默认值（全 18 个按钮，除 button_index 外均为标准）
    DEFAULT_08_ENTRIES = [0x01] * 18  # 全部为"标准/未使用"

    # 已知按钮功能映射（UI 按钮编号 → 协议 entry 索引）
    BUTTON_MAP = {
        1: 0,    # 左键
        2: 1,    # 右键
        3: 4,    # 中键
        4: 2,    # 前进
        5: 3,    # 后退
        6: 5,    # DPI 循环
        7: 14,   # 左滚
        8: 15,   # 右滚
        9: 16,   # 上滚
        10: 17,  # 下滚
    }

    # 按钮功能编码
    FUNC_LEFT = 0x02
    FUNC_RIGHT = 0x03
    FUNC_MIDDLE = 0x04
    FUNC_BACK = 0x05
    FUNC_FORWARD = 0x06
    FUNC_DPI = 0x0D
    FUNC_MACRO = 0x12
    FUNC_SCROLL_UP = 0x09
    FUNC_SCROLL_DOWN = 0x0A
    FUNC_SCROLL_LEFT = 0x0B
    FUNC_SCROLL_RIGHT = 0x0C
    FUNC_NONE = 0x01

    def __init__(self, button_index: int = 3, macro_id: int = 1):
        """
        button_index: 宏要绑定的按钮在协议中的 entry 索引 (0..17)
                      后退键 = 3, 前进键 = 2
        macro_id: 宏的内部 ID (1..255)，用于关联 0x08 和 0x09 报告
        """
        self.button_index = button_index
        self.macro_id = macro_id & 0xFF
        self.playback_mode = PLAYBACK_LOOP
        self.loop_count = 1  # 循环次数（仅播放方式 0x00 有效，1-255，默认 1）
        self.key_pairs: list[tuple[int, int]] = []  # [(keycode, flag), ...]
        self.modifier = 0x03  # 修饰键（仅播放方式 0x01/0x02 有效）

    def set_playback_mode(self, mode: int) -> None:
        """设置播放方式: 0x00=循环次数, 0x01=任意键停止, 0x02=按住"""
        self.playback_mode = mode & 0xFF

    def set_loop_count(self, count: int) -> None:
        """
        设置循环次数（仅播放方式 0x00 有效）。
        count: 1-255，0 会被 clamp 为 1。
        """
        self.loop_count = max(1, count & 0xFF)

    @staticmethod
    def encode_delay(delay_ms: int) -> int:
        """
        将延迟毫秒值编码为 flag|delay 字节中的 bits 0-6。
        
        编码公式（反汇编 0x004182B0 确认）:
          delay ≤ 1270ms: encoded = (int)(delay_ms / 100.0 + 0.5), 最小 1
          delay > 1270ms: encoded = (int)((delay_ms % 200) / 100.0 + 0.5), 最小 1
        
        注意: < 50ms 的延迟全部编码为 1（无法区分 10ms 和 30ms）。
        """
        if delay_ms <= 1270:
            encoded = int(delay_ms / 100.0 + 0.5)
        else:
            encoded = int((delay_ms % 200) / 100.0 + 0.5)
        return max(1, encoded)

    def add_key(self, keycode: int, press: bool = True, delay_ms: int = None) -> None:
        """
        添加一个按键事件。
        
        delay_ms: 自定义延迟（毫秒）。为 None 时使用默认 FLAG_PRESS/FLAG_RELEASE。
        注意: < 50ms 的延迟都会被编码为 1，与不指定延迟效果相同。
        """
        if delay_ms is not None:
            encoded = self.encode_delay(delay_ms)
            flag = (0x80 if press else 0x00) | encoded
        else:
            flag = FLAG_PRESS if press else FLAG_RELEASE
        self.key_pairs.append((keycode & 0xFF, flag))

    def add_key_stroke(self, keycode: int, press_delay_ms: int = None, release_delay_ms: int = None) -> None:
        """
        添加一次完整的按键（按下+释放），支持自定义延迟。
        
        press_delay_ms:   按下后的延迟（毫秒）
        release_delay_ms: 释放后的延迟（毫秒）
        """
        self.add_key(keycode, press=True, delay_ms=press_delay_ms)
        self.add_key(keycode, press=False, delay_ms=release_delay_ms)

    def set_keys(self, pairs: list[tuple[int, int]]) -> None:
        """直接设置按键对列表"""
        self.key_pairs = [(k & 0xFF, f & 0xFF) for k, f in pairs]

    def set_modifier(self, mod: int) -> None:
        """设置修饰键 (bit0=LCtrl, bit1=LShift, bit2=LAlt, bit3=LGui)"""
        self.modifier = mod & 0xFF
        if self.modifier == 0:
            self.modifier = 0x03  # 默认值

    # ── 构建内部缓冲 ──

    def _build_internal_buffer(self) -> bytearray:
        """构建 131 字节内部命令缓冲"""
        buf = bytearray(131)

        buf[0] = 0x09
        buf[1] = 0x83  # 发送时被覆盖为 0x40
        buf[2] = self.macro_id  # 宏 ID（与 0x08 param2 对应）
        buf[3] = self.playback_mode
        buf[4] = 0x00  # action[0]
        buf[5] = 0x00  # action[1]
        buf[6] = 0x00  # action[2]
        # internal[7] 双重用途:
        #   播放方式 0x00 → 循环次数值
        #   播放方式 0x01/0x02 → 修饰键
        if self.playback_mode == PLAYBACK_LOOP:
            buf[7] = self.loop_count
        else:
            buf[7] = self.modifier

        # cmd[8..27] = 20 字节保留/填充（实测为全零）
        buf[28] = len(self.key_pairs) & 0xFF  # 按键对数量
        buf[29] = 0x01  # 延迟模式标志（Default Delay 未改）

        # 实际按键对从 offset 30 开始
        offset = 30
        for keycode, flag in self.key_pairs:
            if offset + 1 >= 129:
                break
            buf[offset] = keycode
            buf[offset + 1] = flag
            offset += 2

        # 校验和：sum(buf[3:129]) 大端
        cksum = sum(buf[3:129]) & 0xFFFF
        buf[129] = (cksum >> 8) & 0xFF
        buf[130] = cksum & 0xFF

        return buf

    def build_09_chunks(self) -> list[bytes]:
        """
        构建 3 个 64 字节的 0x09 线上报告。
        返回 [chunk0, chunk1, chunk2] 列表。
        """
        internal = self._build_internal_buffer()

        chunks = []

        # chunk0: cmd[3..0x3E] 共 60 字节
        chunk0 = bytearray(64)
        chunk0[0] = 0x09
        chunk0[1] = 0x40  # 硬编码，覆盖 cmd[1]
        chunk0[2] = self.macro_id
        chunk0[3] = 0  # chunk index
        chunk0[4:64] = internal[3:0x3F]  # 60 bytes
        chunks.append(bytes(chunk0))

        # chunk1: cmd[0x3F..0x7A] 共 60 字节
        chunk1 = bytearray(64)
        chunk1[0] = 0x09
        chunk1[1] = 0x40
        chunk1[2] = self.macro_id
        chunk1[3] = 1  # chunk index
        chunk1[4:64] = internal[0x3F:0x7B]  # 60 bytes
        chunks.append(bytes(chunk1))

        # chunk2: cmd[0x7B..0x82] 共 8 字节 + 52 字节 00 填充
        chunk2 = bytearray(64)
        chunk2[0] = 0x09
        chunk2[1] = 0x0C  # 官方用 0x0C（不是 0x40！0x40 会导致不生效）
        chunk2[2] = self.macro_id
        chunk2[3] = 2  # chunk index
        chunk2[4:12] = internal[0x7B:0x83]  # 8 bytes (含校验和)
        # 剩余 52 字节保持 0x00
        chunks.append(bytes(chunk2))

        return chunks

    def build_08_report(self, button_func_map: Optional[dict[int, int]] = None) -> bytes:
        """
        构建 0x08 按键映射报告 (59 bytes)。

        button_func_map: {entry_index: function_code} 的映射。
        如果为 None，只设置宏绑定的那个 entry 为 0x12。
        """
        # 官方默认 entry 表（18 个按钮）
        DEFAULT = [
            (0x02,0,0),(0x03,0,0),(0x06,0,0),(0x12,0,4),(0x04,0,0),(0x0D,0,0),
            (0x01,0,0),(0x01,0,0),(0x01,0,0),(0x01,0,0),(0x01,0,0),(0x01,0,0),
            (0x01,0,0),(0x01,0,0),(0x0B,0,0),(0x0C,0,0),(0x09,0,0),(0x0A,0,0),
        ]
        entries = bytearray(54)
        for i, (func, p1, p2) in enumerate(DEFAULT):
            entries[i * 3] = func
            entries[i * 3 + 1] = p1
            entries[i * 3 + 2] = p2

        # 应用自定义映射（覆盖对应 entry）
        if button_func_map:
            for idx, func in button_func_map.items():
                if 0 <= idx < 18:
                    entries[idx * 3] = func & 0xFF
                    entries[idx * 3 + 1] = 0x00
                    entries[idx * 3 + 2] = 0x00

        # 设置宏绑定
        if 0 <= self.button_index < 18:
            entries[self.button_index * 3] = self.FUNC_MACRO  # 0x12
            entries[self.button_index * 3 + 1] = 0x00  # param1 (未知)
            entries[self.button_index * 3 + 2] = self.macro_id  # param2 = 宏 ID

        # 构建完整报告
        report = bytearray(59)
        report[0] = 0x08
        report[1] = 0x3B
        report[2] = 0x01
        report[3:57] = entries  # 54 bytes

        # 校验和: sum(report[3:57]) 大端
        cksum = sum(report[3:57]) & 0xFFFF
        report[57] = (cksum >> 8) & 0xFF
        report[58] = cksum & 0xFF

        return bytes(report)

    def build_wakeup(self) -> bytes:
        """构建 0x0C 唤醒/初始化报告 (10 bytes)"""
        return bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0x00, 0x00, 0x00, 0x00])

    def build_full_sequence(self) -> list[bytes]:
        """
        构建完整写入序列:
        1. 0x0C 唤醒
        2. 0x08 按键映射
        3. 0x09 chunk 0..2
        """
        seq = [self.build_wakeup(), self.build_08_report()]
        seq.extend(self.build_09_chunks())
        return seq


# ── 命令行接口 ──

def parse_key_string(key_str: str) -> list[tuple[int, int]]:
    """
    解析按键字符串为按键对列表。
    支持:
      - 单个字母: A, B, C...
      - 修饰键+字母: LCTRL+A, LSHIFT+Tab
      - 组合: A,B,C  (依次按下再释放)
      - 详细: A:down,A:up
    """
    pairs = []

    # 逗号分隔
    parts = [p.strip() for p in key_str.split(',')]

    for part in parts:
        if ':' in part:
            # 显式: key:down 或 key:up
            k, action = part.split(':', 1)
            k = k.strip().upper()
            action = action.strip().lower()
            keycode = HID_KEYBOARD.get(k)
            if keycode is None:
                raise ValueError(f"未知按键: {k}")
            if action == 'down' or action == 'press':
                pairs.append((keycode, FLAG_PRESS))
            elif action == 'up' or action == 'release':
                pairs.append((keycode, FLAG_RELEASE))
            else:
                raise ValueError(f"未知动作: {action} (应为 down/up)")
        else:
            # 单个按键 → 按下+释放
            key_str_upper = part.upper()

            # 检查是否带修饰键
            mod_keys = ['LCTRL', 'LSHIFT', 'LALT', 'LGUI', 'RCTRL', 'RSHIFT', 'RALT', 'RGUI']
            mod_part = None
            key_part = key_str_upper

            for mk in mod_keys:
                if key_str_upper.startswith(mk + '+'):
                    mod_part = mk
                    key_part = key_str_upper[len(mk) + 1:]
                    break

            keycode = HID_KEYBOARD.get(key_part)
            if keycode is None:
                raise ValueError(f"未知按键: {key_part} (完整: {key_str_upper})")

            if mod_part:
                mod_code = HID_KEYBOARD.get(mod_part, 0)
                # 修饰键按下 + 主键按下 + 主键释放 + 修饰键释放
                pairs.append((mod_code, FLAG_PRESS))
                pairs.append((keycode, FLAG_PRESS))
                pairs.append((keycode, FLAG_RELEASE))
                pairs.append((mod_code, FLAG_RELEASE))
            else:
                pairs.append((keycode, FLAG_PRESS))
                pairs.append((keycode, FLAG_RELEASE))

    return pairs


def format_hex(data: bytes, label: str = "") -> str:
    """格式化十六进制输出"""
    h = data.hex(' ')
    if label:
        return f"  {label} ({len(data)} bytes):\n  {h}"
    return f"  {h}"


def main():
    parser = argparse.ArgumentParser(
        description='DELUX M618XSD 宏数据生成器',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
示例:
  生成 A 键一次:
    python macro_generator.py --key A

  生成 Ctrl+C:
    python macro_generator.py --key LCTRL+C

  生成多个按键, ABC:
    python macro_generator.py --key A,B,C

  自定义播放方式 (0x00=循环, 0x01=任意键停止, 0x02=按住):
    python macro_generator.py --key A --playback 0x01

  绑定到按钮 5 (后退键):
    python macro_generator.py --key A --btn 3

  完整 16 进制输出:
    python macro_generator.py --key A --hex
        """
    )
    parser.add_argument('--key', '-k', default='A',
                        help='按键序列，如 A 或 LCTRL+C 或 A,B,C')
    parser.add_argument('--playback', '-p', type=lambda x: int(x, 16), default=0x00,
                        help='播放方式: 0x00=循环, 0x01=任意键停止, 0x02=按住 (默认 0x00)')
    parser.add_argument('--btn', '-b', type=int, default=3,
                        help='按钮 entry 索引 (默认 3=后退键)')
    parser.add_argument('--macro-id', '-m', type=int, default=1,
                        help='宏 ID (默认 1，与 0x08 param2 对应)')
    parser.add_argument('--hex', '-x', action='store_true',
                        help='显示完整 16 进制输出')
    parser.add_argument('--modifier', '-M', type=lambda x: int(x, 16), default=None,
                        help='修饰键字节 (默认自动)')

    args = parser.parse_args()

    # 解析按键
    try:
        key_pairs = parse_key_string(args.key)
    except ValueError as e:
        print(f"错误: {e}")
        sys.exit(1)

    # 构建宏
    builder = MacroBuilder(button_index=args.btn, macro_id=args.macro_id)
    builder.set_keys(key_pairs)
    builder.set_playback_mode(args.playback)
    if args.modifier is not None:
        builder.set_modifier(args.modifier)

    # 生成报告
    wakeup = builder.build_wakeup()
    report_08 = builder.build_08_report()
    chunks_09 = builder.build_09_chunks()

    # 输出
    print(f"{'='*60}")
    print(f"  DELUX M618XSD 宏数据生成")
    print(f"{'='*60}")
    print(f"\n  参数:")
    print(f"    按键序列:   {args.key}")
    print(f"    按键对:     {len(key_pairs)} 对")
    for k, f in key_pairs:
        fn = "按下" if f == 0x81 else "释放" if f == 0x00 else f"0x{f:02X}"
        print(f"                key=0x{k:02X} ({fn})")
    print(f"    播放方式:   0x{args.playback:02X} ({['循环次数','任意键停止','按住'][args.playback] if args.playback <= 2 else '?'})")
    print(f"    按钮索引:   {args.btn}")

    print(f"\n  {'='*60}")
    print(f"  完整写入序列 ({len(builder.build_full_sequence())} 条报告)")
    print(f"  {'='*60}")

    print(f"\n  1. 唤醒 (0x0C):")
    print(f"     {wakeup.hex(' ')}")

    print(f"\n  2. 按键映射 (0x08):")
    print(f"     {report_08.hex(' ')}")
    # 显示宏 entry
    entry_off = args.btn * 3
    entry = report_08[3 + entry_off:3 + entry_off + 3]
    print(f"     entry[{args.btn}]: {entry.hex(' ')} (宏)")

    print(f"\n  3. 宏数据 (0x09, 3 chunks):")
    for i, chunk in enumerate(chunks_09):
        print(f"     chunk {i}: {chunk.hex(' ')}")

    # 校验和验证
    print(f"\n  {'='*60}")
    print(f"  校验和验证")
    print(f"  {'='*60}")
    cs_08 = struct.unpack_from('>H', report_08, 57)[0]
    calc_08 = sum(report_08[3:57]) & 0xFFFF
    print(f"  0x08: 报告=0x{cs_08:04X}  计算=0x{calc_08:04X}  {'✅' if cs_08 == calc_08 else '❌'}")

    buf = builder._build_internal_buffer()
    cs_09 = struct.unpack_from('>H', buf, 129)[0]
    calc_09 = sum(buf[3:129]) & 0xFFFF
    print(f"  0x09: 报告=0x{cs_09:04X}  计算=0x{calc_09:04X}  {'✅' if cs_09 == calc_09 else '❌'}")

    # Python 代码片段
    print(f"\n  {'='*60}")
    print(f"  Python 发送代码片段")
    print(f"  {'='*60}")
    print(f"""
    from ctypes import WinDLL
    dll = WinDLL(r'extracted\\\\app\\\\hiddriver_ms_4.dll')
    dll.Set_VIDPID(0x1D57, 0xFA60)
    dll.Open_FeatureDevice()

    # 1. 唤醒
    dll.SetFeature(bytes({list(wakeup)}), {len(wakeup)})

    # 2. 按键映射
    dll.SetFeature(bytes({list(report_08)}), {len(report_08)})

    # 3. 宏数据 (3 chunks, 200ms 间隔)
    for chunk in {[list(c) for c in chunks_09]}:
        dll.SetFeature(bytes(chunk), {len(chunks_09[0])})
        import time; time.sleep(0.2)

    dll.Close_FeatureDevice()
    """)

    if not args.hex:
        print(f"\n  提示: 用 --hex 显示完整 16 进制输出")


if __name__ == '__main__':
    import sys
    main()