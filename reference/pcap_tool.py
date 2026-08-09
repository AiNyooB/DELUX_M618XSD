#!/usr/bin/env python3
"""
DELUX M618XSD — USBPcap HID 分析工具

通用 pcapng 解析 + HID 报告解码 + 对比引擎。
核心解析引擎对任何 USBPcap 抓包通用，报告解码器可按需扩展。

用法:
  python pcap_tool.py analyze <file.pcapng> [--filter 0x08,0x09] [--decode] [--json]
  python pcap_tool.py compare <file1.pcapng> <file2.pcapng> [--filter 0x08]
  python pcap_tool.py diff    <file1.pcapng> <file2.pcapng> [--filter 0x08]
  python pcap_tool.py list    <file.pcapng>                # 只列出报告摘要
"""
import argparse
import json
import struct
import sys
from collections import defaultdict
from typing import Any


# ═══════════════════════════════════════════════════════════════
# 核心：pcapng 解析（通用，永远不用改）
# ═══════════════════════════════════════════════════════════════

USBPCAP_HEADER_FMT = '<H'  # headerLen (USHORT)
URB_CTRL = 0x0008  # URB_FUNCTION_CONTROL_TRANSFER
URB_CTRL_EX = 0x001B  # URB_FUNCTION_CONTROL_TRANSFER_EX


class PcapHidTransfer:
    """一条解析后的 HID 控制传输"""
    __slots__ = ('timestamp', 'report_id', 'data', 'raw', 'is_set', 'is_get')

    def __init__(self, timestamp: float, data: bytes, is_set: bool):
        self.timestamp = timestamp
        self.report_id = data[0] if data else 0
        self.data = data          # report payload (不含 USB setup 头)
        self.raw = data           # 完整数据
        self.is_set = is_set      # True=SET_REPORT, False=GET_REPORT

    def __repr__(self) -> str:
        return f"<HidTransfer ID=0x{self.report_id:02X} {'SET' if self.is_set else 'GET'} {len(self.data)}B>"


def parse_pcapng(filepath: str) -> list[PcapHidTransfer]:
    """解析 pcapng 文件，返回所有 HID Feature Report 传输"""
    with open(filepath, 'rb') as f:
        buf = f.read()

    magic = struct.unpack_from('<I', buf, 0)[0]
    if magic == 0xa1b2c3d4:
        return _parse_pcap_legacy(buf)
    elif magic == 0x1A2B3C4D:  # pcapng 格式
        return _parse_pcapng_block(buf)
    else:
        raise ValueError(f"不支持的文件格式 (magic=0x{magic:08X})")


def _parse_pcap_legacy(buf: bytes) -> list[PcapHidTransfer]:
    """解析传统 pcap 格式 (magic 0xa1b2c3d4)"""
    transfers: list[PcapHidTransfer] = []
    pos = 24  # 跳过全局头

    while pos + 16 <= len(buf):
        ts_sec, ts_usec, incl_len, orig_len = struct.unpack_from('<IIII', buf, pos)
        pos += 16
        if pos + incl_len > len(buf) or incl_len < 28:
            pos += incl_len
            continue

        pkt = buf[pos:pos + incl_len]
        pos += incl_len
        t = _parse_usbpcap_packet(pkt, ts_sec + ts_usec / 1e6)
        if t:
            transfers.append(t)

    return transfers


def _parse_pcapng_block(buf: bytes) -> list[PcapHidTransfer]:
    """解析 pcapng 格式"""
    transfers: list[PcapHidTransfer] = []
    pos = 0

    while pos < len(buf):
        if pos + 8 > len(buf):
            break
        block_type = struct.unpack_from('<I', buf, pos)[0]
        block_len = struct.unpack_from('<I', buf, pos + 4)[0]

        if block_type == 0x00000006:  # Enhanced Packet Block
            if pos + 4 + 16 + 4 > len(buf):
                break
            ts_hi = struct.unpack_from('<I', buf, pos + 8)[0]
            ts_lo = struct.unpack_from('<I', buf, pos + 12)[0]
            cap_len = struct.unpack_from('<I', buf, pos + 16)[0]
            pkt_start = pos + 20
            if pkt_start + cap_len > len(buf):
                break
            pkt = buf[pkt_start:pkt_start + cap_len]
            timestamp = ts_hi + ts_lo / 1e6
            t = _parse_usbpcap_packet(pkt, timestamp)
            if t:
                transfers.append(t)

        pos += block_len if block_len > 0 else 4
        if block_len == 0:
            break

    return transfers


def _parse_usbpcap_packet(pkt: bytes, timestamp: float) -> PcapHidTransfer | None:
    """解析一个 USBPcap 包，提取 HID Feature Report"""
    if len(pkt) < 28:
        return None

    header_len = struct.unpack_from('<H', pkt, 0)[0]
    if header_len < 28 or header_len > len(pkt):
        return None

    urb_function = struct.unpack_from('<H', pkt, 14)[0]
    endpoint = pkt[19]
    data_len = struct.unpack_from('<I', pkt, 22)[0]

    # 只处理控制传输
    if urb_function not in (URB_CTRL, URB_CTRL_EX):
        return None

    usb_data = pkt[header_len:header_len + data_len]
    if len(usb_data) < 8:
        return None

    bmReqType = usb_data[0]
    bRequest = usb_data[1]
    wValue = struct.unpack_from('<H', usb_data, 2)[0]
    wLength = struct.unpack_from('<H', usb_data, 6)[0]

    if wLength == 0 or wLength > 4096:
        return None

    report_data = usb_data[8:8 + wLength]

    # HID SET_REPORT (Host→Device): bmReqType=0x21, bRequest=0x09
    if bmReqType == 0x21 and bRequest == 0x09:
        if report_data:
            return PcapHidTransfer(timestamp, bytes(report_data), is_set=True)

    # HID GET_REPORT (Device→Host): bmReqType=0xA1, bRequest=0x01
    if bmReqType == 0xA1 and bRequest == 0x01:
        if report_data:
            return PcapHidTransfer(timestamp, bytes(report_data), is_set=False)

    return None


# ═══════════════════════════════════════════════════════════════
# 报告解码器
# ═══════════════════════════════════════════════════════════════

BUTTON_NAMES = {
    0x01: '标准/未使用', 0x02: '左键', 0x03: '右键', 0x04: '中键',
    0x05: '后退', 0x06: '前进', 0x09: '上滚', 0x0A: '下滚',
    0x0B: '左滚', 0x0C: '右滚', 0x0D: 'DPI循环', 0x12: '宏',
}

REPORT_NAMES = {
    0x0C: '唤醒/初始化', 0x04: 'DPI配置', 0x05: '未知配置',
    0x06: 'DPI选择', 0x08: '按键映射', 0x09: '宏数据',
    0x0A: '未知', 0x0B: '未知', 0x0D: '未知',
}

PLAYBACK_NAMES = {
    0x00: '循环次数播放',
    0x01: '任意键停止播放',
    0x02: '按住播放松开停止',
}

# 按键 flags (来自反汇编：flag|delay 编码)
# bit7 = 0x80 = press, bit0-6 = delay
KEY_FLAG_NAMES = {
    0x81: '按下(delay=1)', 0x00: '释放(delay=0)',
    0x01: '释放(delay=1)', 0x80: '按下(delay=0)',
}


def decode_report(report: PcapHidTransfer) -> dict[str, Any]:
    """解码报告，返回结构化信息"""
    d = report.data
    rid = report.report_id
    info: dict[str, Any] = {
        'report_id': rid,
        'report_name': REPORT_NAMES.get(rid, f'未知(0x{rid:02X})'),
        'length': len(d),
        'hex': d.hex(' '),
        'is_set': report.is_set,
        'timestamp': report.timestamp,
    }

    decoder = {
        0x04: _decode_0x04,
        0x08: _decode_0x08,
        0x09: _decode_0x09,
        0x0C: _decode_0x0C,
    }.get(rid)

    if decoder:
        info['decoded'] = decoder(d)
    return info


def _decode_0x04(d: bytes) -> dict[str, Any]:
    """解码 0x04 DPI 配置报告 (56 bytes)"""
    result: dict[str, Any] = {'type': 'DPI配置'}
    if len(d) < 52:
        result['error'] = f'数据太短: {len(d)} bytes'
        return result

    result['header'] = d[0:5].hex(' ')
    enabled_mask = d[5]
    enabled_slots = [i + 1 for i in range(8) if enabled_mask & (1 << i)]
    result['enabled_mask'] = f'0x{enabled_mask:02X}'
    result['enabled_slots'] = enabled_slots
    result['status_bytes'] = f'{d[6]:02x} {d[7]:02x}'

    dpi_values = []
    for i in range(8):
        lo = d[8 + i]
        hi = d[16 + i]
        val = (hi << 8) | lo
        dpi_values.append(val)
    result['dpi_values'] = dpi_values

    active = d[24]
    result['active_slot'] = active
    if 1 <= active <= 8:
        result['active_dpi'] = dpi_values[active - 1]

    colors = []
    for i in range(8):
        offset = 25 + i * 3
        if offset + 3 <= len(d):
            colors.append(f'#{d[offset]:02x}{d[offset+1]:02x}{d[offset+2]:02x}')
    result['colors'] = colors
    result['unk49'] = f'0x{d[49]:02X}'

    if len(d) >= 52:
        cs = struct.unpack_from('>H', d, 50)[0]
        computed = sum(d[3:50]) & 0xFFFF
        result['checksum'] = f'0x{cs:04X}'
        result['checksum_match'] = (cs == computed)

    return result


def _decode_0x08(d: bytes) -> dict[str, Any]:
    """解码 0x08 按键映射报告 (59 bytes)"""
    result: dict[str, Any] = {'type': '按键映射'}
    if len(d) < 5:
        result['error'] = f'数据太短: {len(d)} bytes'
        return result

    result['header'] = f'{d[0]:02x} {d[1]:02x} {d[2]:02x}'

    entries = []
    macro_entry = None
    for j in range(3, len(d) - 2, 3):
        if j + 3 > len(d) - 2:
            break
        e = d[j:j+3]
        fc = e[0]
        name = BUTTON_NAMES.get(fc, f'0x{fc:02X}')
        entry = {
            'index': (j - 3) // 3,
            'function': fc,
            'function_name': name,
            'param1': e[1],
            'param2': e[2],
        }
        entries.append(entry)
        if fc == 0x12:
            macro_entry = entry

    result['entries'] = entries

    if macro_entry:
        result['macro'] = {
            'entry_index': macro_entry['index'],
            'param1': macro_entry['param1'],
            'param2': macro_entry['param2'],
        }

    # 校验和
    if len(d) >= 2:
        cs = struct.unpack_from('>H', d, len(d) - 2)[0]
        computed = sum(d[3:len(d) - 2]) & 0xFFFF
        result['checksum'] = f'0x{cs:04X}'
        result['checksum_computed'] = f'0x{computed:04X}'
        result['checksum_match'] = (cs == computed)

    return result


def _decode_0x0C(d: bytes) -> dict[str, Any]:
    """解码 0x0C 唤醒/初始化报告"""
    return {'type': '唤醒/初始化', 'data': d.hex(' ')}


def _decode_0x09(d: bytes) -> dict[str, Any]:
    """解码 0x09 宏数据 chunk"""
    result: dict[str, Any] = {'type': '宏数据chunk'}
    if len(d) < 5:
        result['error'] = f'数据太短: {len(d)} bytes'
        return result

    ci = d[3]
    result['chunk_index'] = ci
    result['button_index'] = d[2]
    result['header2'] = f'{d[0]:02x} {d[1]:02x} {d[2]:02x} {d[3]:02x}'

    payload = d[4:]
    non_zero = [(j, b) for j, b in enumerate(payload) if b != 0]
    result['payload_nonzero'] = [f'[{j+4}]={b:02x}' for j, b in non_zero]

    # 如果这是第 2 个 chunk，提取校验和
    if ci == 2 and len(d) >= 12:
        cs = (d[10] << 8) | d[11]
        result['chunk2_checksum'] = f'0x{cs:04X}'

    return result


def reconstruct_macro_from_chunks(chunks: list[bytes]) -> dict[str, Any] | None:
    """
    从 3 个 0x09 线上 chunk 重构 131 字节内部缓冲。
    每个 chunk 是 64 字节的完整线上报告。
    """
    if len(chunks) != 3:
        return None

    # 按 chunk index 排序
    sorted_chunks = sorted(chunks, key=lambda c: c[3] if len(c) > 3 else 0)
    if len(sorted_chunks) != 3:
        return None

    full = bytearray(131)
    for c in sorted_chunks:
        ci = c[3] if len(c) > 3 else 0
        payload = c[4:]  # 60 bytes payload
        if ci == 0:
            full[3:0x3F] = payload[:0x3C]  # 60 bytes
        elif ci == 1:
            full[0x3F:0x7B] = payload[:0x3C]  # 60 bytes
        elif ci == 2:
            full[0x7B:0x83] = payload[:8]  # 8 bytes (含校验和)

    full[0] = 0x09
    full[1] = 0x83
    full[2] = sorted_chunks[0][2]  # button index / macro_id

    result: dict[str, Any] = {
        'button_index': full[2],
        'playback_mode_raw': full[3],
        'playback_mode': PLAYBACK_NAMES.get(full[3], f'未知(0x{full[3]:02X})'),
        'action': [full[4], full[5], full[6]],
        'modifier': full[7],
        'reserved_8to27': bytes(full[8:28]).hex(' '),
        'cmd28': full[28],    # 按键对数量
        'cmd29': full[29],    # 固定字节（0x01）
        'cmd30_hex': bytes(full[30:36]).hex(' '),  # 按键数据原始hex
    }

    # 解析按键序列（从 offset 30 开始，每对 2 字节：[keycode, flag|delay]）
    keys = []
    offset = 30
    while offset + 1 <= 128:
        keycode = full[offset]
        flag_delay = full[offset + 1]
        if keycode == 0 and flag_delay == 0:
            offset += 2
            continue
        press = (flag_delay & 0x80) != 0
        delay = flag_delay & 0x7F
        keys.append({
            'keycode': keycode,
            'key_name': f'0x{keycode:02X}',
            'press': press,
            'delay': delay,
            'flag_delay': f'0x{flag_delay:02X}',
        })
        offset += 2
    result['keys'] = keys
    result['key_count'] = len(keys)

    # 校验和
    if len(full) >= 131:
        cs = struct.unpack_from('>H', full, 129)[0]
        computed = sum(full[3:129]) & 0xFFFF
        result['checksum'] = f'0x{cs:04X}'
        result['checksum_computed'] = f'0x{computed:04X}'
        result['checksum_match'] = (cs == computed)

    return result


# ═══════════════════════════════════════════════════════════════
# 输出格式化
# ═══════════════════════════════════════════════════════════════

def format_transfer(info: dict[str, Any], verbose: bool = True) -> str:
    """格式化一条传输记录"""
    rid = info['report_id']
    name = info['report_name']
    length = info['length']
    direction = 'SET' if info['is_set'] else 'GET'
    lines = [f"  [{direction}] 0x{rid:02X} {name}  ({length} bytes)"]

    if verbose:
        lines.append(f"         hex: {info['hex']}")

    decoded = info.get('decoded')
    if decoded:
        dtype = decoded.get('type', '')
        if dtype == 'DPI配置':
            lines.append(f"         ╔═ DPI配置 ═══════════════════════")
            lines.append(f"         ║ 启用: {decoded['enabled_mask']} → 档位 {decoded['enabled_slots']}")
            lines.append(f"         ║ 状态: {decoded['status_bytes']}")
            dpi_str = ', '.join(f'L{i+1}={v}' for i, v in enumerate(decoded['dpi_values']))
            lines.append(f"         ║ DPI:  {dpi_str}")
            lines.append(f"         ║ 活跃: 档位{decoded['active_slot']} = {decoded.get('active_dpi', '?')} DPI")
            lines.append(f"         ║ 颜色: {', '.join(decoded['colors'])}")
            cs_match = '✅' if decoded.get('checksum_match') else '❌'
            lines.append(f"         ║ 校验: {decoded.get('checksum', '?')} {cs_match}")
            lines.append(f"         ╚═══════════════════════════════════")

        elif dtype == '按键映射':
            lines.append(f"         ╔═ 按键映射 ═══════════════════════")
            for e in decoded['entries']:
                marker = ' <-- 宏!' if e['function'] == 0x12 else ''
                lines.append(f"         ║ entry[{e['index']:2d}]: {e['function_name']:8s}  {e['function']:02x} {e['param1']:02x} {e['param2']:02x}{marker}")
            if decoded.get('macro'):
                m = decoded['macro']
                lines.append(f"         ║ → 宏: param1=0x{m['param1']:02X} param2=0x{m['param2']:02X}")
            cs_match = '✅' if decoded.get('checksum_match') else '❌'
            lines.append(f"         ║ 校验: {decoded.get('checksum', '?')} (计算={decoded.get('checksum_computed', '?')}) {cs_match}")
            lines.append(f"         ╚═══════════════════════════════════")

        elif dtype == '宏数据chunk':
            ci = decoded['chunk_index']
            btn = decoded['button_index']
            nz = decoded.get('payload_nonzero', [])
            lines.append(f"         ╔═ 宏数据 chunk {ci} ════════════════════")
            lines.append(f"         ║ header={decoded['header2']}  button={btn}")
            if nz:
                lines.append(f"         ║ 非零: {'  '.join(nz)}")
            else:
                lines.append(f"         ║ (全部为零)")
            if 'chunk2_checksum' in decoded:
                lines.append(f"         ║ 校验(粗): {decoded['chunk2_checksum']}")
            lines.append(f"         ╚═══════════════════════════════════════")

    return '\n'.join(lines)


def format_macro_reconstruction(macro: dict[str, Any] | None) -> str:
    """格式化宏重构结果"""
    if not macro:
        return "  (无法重构：需要 3 个 0x09 chunk)"

    lines = [
        f"         ╔═ 宏内部缓冲重构 ═══════════════════",
        f"         ║ button={macro['button_index']}  播放方式={macro['playback_mode']} (0x{macro['playback_mode_raw']:02X})",
        f"         ║ action={macro['action']}  mod=0x{macro['modifier']:02X}",
        f"         ║ 按键数={macro['cmd28']}  固定=0x{macro['cmd29']:02X}  原始={macro['cmd30_hex']}",
        f"         ║ reserved[8..27]={macro['reserved_8to27']}",
    ]

    if macro['keys']:
        lines.append(f"         ║ 按键序列 ({macro['key_count']} 对):")
        for k in macro['keys']:
            action = '按下' if k['press'] else '释放'
            lines.append(f"         ║   key=0x{k['keycode']:02X}  {action}  delay={k['delay']}  raw={k['flag_delay']}")

    if 'checksum' in macro:
        cs_match = '✅' if macro.get('checksum_match') else '❌'
        lines.append(f"         ║ 校验: {macro['checksum']} (计算={macro['checksum_computed']}) {cs_match}")
    lines.append(f"         ╚═══════════════════════════════════════")
    return '\n'.join(lines)


# ═══════════════════════════════════════════════════════════════
# 对比引擎
# ═══════════════════════════════════════════════════════════════

def compare_captures(
    file1: str, file2: str,
    filter_ids: set[int] | None = None,
    show_diff_only: bool = False,
) -> str:
    """对比两个抓包，按报告 ID 分组显示"""
    xfers1 = parse_pcapng(file1)
    xfers2 = parse_pcapng(file2)

    info1 = [decode_report(x) for x in xfers1]
    info2 = [decode_report(x) for x in xfers2]

    # 按 report_id 分组
    groups1: dict[int, list[dict]] = defaultdict(list)
    groups2: dict[int, list[dict]] = defaultdict(list)

    for info in info1:
        groups1[info['report_id']].append(info)
    for info in info2:
        groups2[info['report_id']].append(info)

    all_ids = set(groups1.keys()) | set(groups2.keys())
    if filter_ids:
        all_ids &= filter_ids

    lines = [
        f"{'='*70}",
        f"  对比: {file1}  vs  {file2}",
        f"{'='*70}",
    ]

    for rid in sorted(all_ids):
        g1 = groups1.get(rid, [])
        g2 = groups2.get(rid, [])

        name = REPORT_NAMES.get(rid, f'0x{rid:02X}')
        lines.append(f"\n  --- 0x{rid:02X} {name} ---")
        lines.append(f"      {file1}: {len(g1)} 个传输")
        lines.append(f"      {file2}: {len(g2)} 个传输")

        if rid == 0x08:
            lines.extend(_diff_08_reports(g1, g2))
        elif rid == 0x09:
            lines.extend(_diff_09_reports(g1, g2, xfers1, xfers2))
        else:
            data1 = [info['hex'] for info in g1 if info['is_set']]
            data2 = [info['hex'] for info in g2 if info['is_set']]
            if data1 == data2:
                if not show_diff_only:
                    lines.append(f"      ✅ 完全相同")
            else:
                lines.append(f"      ⚠️ 有差异!")
                for i, (h1, h2) in enumerate(zip(data1, data2)):
                    if h1 != h2:
                        lines.append(f"      #{i}:")
                        lines.append(f"        A: {h1}")
                        lines.append(f"        B: {h2}")
                if len(data1) != len(data2):
                    lines.append(f"      (数量不同: {len(data1)} vs {len(data2)})")

    if not all_ids:
        lines.append("  (无匹配的报告)")

    return '\n'.join(lines)


def _diff_08_reports(g1: list[dict], g2: list[dict]) -> list[str]:
    """对比 0x08 按键映射报告"""
    lines: list[str] = []
    for g1_info, g2_info in zip(g1, g2):
        d1 = g1_info.get('decoded', {})
        d2 = g2_info.get('decoded', {})
        entries1 = d1.get('entries', [])
        entries2 = d2.get('entries', [])

        has_diff = False
        for e1, e2 in zip(entries1, entries2):
            if e1['function'] != e2['function'] or e1['param1'] != e2['param1'] or e1['param2'] != e2['param2']:
                lines.append(f"      entry[{e1['index']}] 差异:")
                lines.append(f"        A: {e1['function_name']:8s}  {e1['function']:02x} {e1['param1']:02x} {e1['param2']:02x}")
                lines.append(f"        B: {e2['function_name']:8s}  {e2['function']:02x} {e2['param1']:02x} {e2['param2']:02x}")
                has_diff = True

        if not has_diff:
            lines.append(f"      ✅ 按键映射一致")

        # 校验和对比
        cs1 = g1_info.get('decoded', {}).get('checksum')
        cs2 = g2_info.get('decoded', {}).get('checksum')
        if cs1 and cs2 and cs1 != cs2:
            lines.append(f"      ⚡ 校验和不同: {cs1} vs {cs2}")

    return lines


def _diff_09_reports(g1: list[dict], g2: list[dict],
                     xfers1: list[PcapHidTransfer], xfers2: list[PcapHidTransfer]) -> list[str]:
    """对比 0x09 宏数据报告"""
    lines: list[str] = []

    # 提取原始数据
    raw1 = [x.data for x in xfers1 if x.report_id == 0x09]
    raw2 = [x.data for x in xfers2 if x.report_id == 0x09]

    # 按 chunk index 对比
    for i, (r1, r2) in enumerate(zip(raw1, raw2)):
        ci1 = r1[3] if len(r1) > 3 else i
        ci2 = r2[3] if len(r2) > 3 else i
        if r1 != r2:
            lines.append(f"      chunk {ci1}:")
            lines.append(f"        A: {r1.hex(' ')}")
            lines.append(f"        B: {r2.hex(' ')}")
            # 逐字节对比
            diff_bytes = []
            for j in range(min(len(r1), len(r2))):
                if r1[j] != r2[j]:
                    diff_bytes.append(f"[{j}]={r1[j]:02x}→{r2[j]:02x}")
            if diff_bytes:
                lines.append(f"        差异: {'  '.join(diff_bytes)}")
        else:
            lines.append(f"      chunk {ci1}: ✅ 相同")

    if len(raw1) != len(raw2):
        lines.append(f"      (chunk 数量不同: {len(raw1)} vs {len(raw2)})")

    # 重构宏对比
    if len(raw1) == 3 and len(raw2) == 3:
        macro1 = reconstruct_macro_from_chunks(raw1)
        macro2 = reconstruct_macro_from_chunks(raw2)
        if macro1 and macro2:
            lines.append(f"")
            lines.append(f"      ╔═ 宏重构对比 ═══════════════════════")
            for key in ['button_index', 'playback_mode', 'action', 'modifier', 'key_count']:
                if macro1.get(key) != macro2.get(key):
                    lines.append(f"      ║ {key}: A={macro1.get(key)}  B={macro2.get(key)}  ❌ 不同")
                else:
                    lines.append(f"      ║ {key}: {macro1.get(key)}  ✅")
            lines.append(f"      ╚═══════════════════════════════════════")

    return lines


# ═══════════════════════════════════════════════════════════════
# 命令行接口
# ═══════════════════════════════════════════════════════════════

def cmd_analyze(args: argparse.Namespace) -> None:
    """analyze 命令：分析单个抓包"""
    xfers = parse_pcapng(args.file)
    infos = [decode_report(x) for x in xfers]

    filter_ids = set(args.filter) if args.filter else None

    print(f"{'='*70}")
    print(f"  文件: {args.file}")
    print(f"  传输: {len(xfers)} 条 HID Feature Report")
    print(f"{'='*70}\n")

    # 收集 0x09 chunks 用于重构
    raw_09 = [x.data for x in xfers if x.report_id == 0x09]

    for i, info in enumerate(infos):
        rid = info['report_id']
        if filter_ids and rid not in filter_ids:
            continue
        print(format_transfer(info, verbose=args.verbose))
        print()

    # 宏重构
    if len(raw_09) == 3 and (not filter_ids or 0x09 in filter_ids):
        macro = reconstruct_macro_from_chunks(raw_09)
        if macro:
            print(format_macro_reconstruction(macro))
            print()

    # 统计
    if not filter_ids:
        stats = defaultdict(int)
        for info in infos:
            stats[info['report_name']] += 1
        print(f"{'='*70}")
        print(f"  统计:")
        for name, count in sorted(stats.items(), key=lambda x: -x[1]):
            print(f"    {name}: {count} 次")
        print(f"{'='*70}")


def cmd_compare(args: argparse.Namespace) -> None:
    """compare 命令：对比两个抓包"""
    filter_ids = set(args.filter) if args.filter else None
    result = compare_captures(args.file1, args.file2, filter_ids, show_diff_only=False)
    print(result)


def cmd_diff(args: argparse.Namespace) -> None:
    """diff 命令：只显示差异"""
    filter_ids = set(args.filter) if args.filter else None
    result = compare_captures(args.file1, args.file2, filter_ids, show_diff_only=True)
    print(result)


def cmd_list(args: argparse.Namespace) -> None:
    """list 命令：只列出报告摘要"""
    xfers = parse_pcapng(args.file)
    infos = [decode_report(x) for x in xfers]

    print(f"  文件: {args.file}")
    print(f"  传输: {len(xfers)} 条\n")
    print(f"  {'#':>3s}  {'方向':4s}  {'ID':4s}  {'名称':16s}  {'长度':5s}  {'时间':>10s}")
    print(f"  {'-'*3}  {'-'*4}  {'-'*4}  {'-'*16}  {'-'*5}  {'-'*10}")
    for i, info in enumerate(infos):
        direction = 'SET' if info['is_set'] else 'GET'
        rid = info['report_id']
        name = info['report_name']
        length = info['length']
        ts = f"{info['timestamp']:.3f}"
        print(f"  {i+1:>3d}  {direction:4s}  0x{rid:02X}  {name:16s}  {length:5d}  {ts:>10s}")


def main():
    parser = argparse.ArgumentParser(
        description='DELUX M618XSD — USBPcap HID 分析工具',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
使用示例:
  python pcap_tool.py analyze captures/macro_1.pcapng
  python pcap_tool.py analyze captures/macro_2.pcapng --filter 0x08,0x09
  python pcap_tool.py compare captures/macro_1.pcapng captures/macro_2.pcapng
  python pcap_tool.py diff captures/macro_1.pcapng captures/macro_2.pcapng --filter 0x08
  python pcap_tool.py list captures/macro_1.pcapng
        """
    )
    subparsers = parser.add_subparsers(dest='command', help='子命令')

    # analyze
    ap = subparsers.add_parser('analyze', help='分析单个抓包，解码已知报告')
    ap.add_argument('file', help='pcapng 文件路径')
    ap.add_argument('--filter', '-f', help='只显示指定报告 ID，逗号分隔，如 0x08,0x09')
    ap.add_argument('--verbose', '-v', action='store_true', default=True, help='显示 hex 详情')
    ap.add_argument('--json', '-j', action='store_true', help='输出 JSON 格式')
    ap.set_defaults(func=cmd_analyze)

    # compare
    ap = subparsers.add_parser('compare', help='对比两个抓包，显示所有差异')
    ap.add_argument('file1', help='第一个 pcapng 文件')
    ap.add_argument('file2', help='第二个 pcapng 文件')
    ap.add_argument('--filter', '-f', help='只对比指定报告 ID，逗号分隔')
    ap.set_defaults(func=cmd_compare)

    # diff
    ap = subparsers.add_parser('diff', help='只显示差异部分（同 compare 但更简洁）')
    ap.add_argument('file1', help='第一个 pcapng 文件')
    ap.add_argument('file2', help='第二个 pcapng 文件')
    ap.add_argument('--filter', '-f', help='只对比指定报告 ID')
    ap.set_defaults(func=cmd_diff)

    # list
    ap = subparsers.add_parser('list', help='列出抓包中的报告摘要')
    ap.add_argument('file', help='pcapng 文件路径')
    ap.set_defaults(func=cmd_list)

    args = parser.parse_args()
    if args.command is None:
        parser.print_help()
        sys.exit(1)

    # 解析 filter 参数
    if hasattr(args, 'filter') and args.filter:
        args.filter = {int(x, 16) if x.startswith('0x') else int(x) for x in args.filter.split(',')}

    args.func(args)


if __name__ == '__main__':
    main()