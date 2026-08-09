#!/usr/bin/env python3
"""解析 USBPcap pcapng，提取所有 Interrupt IN（设备->主机）的 Input Report。
用于定位 DPI 循环键切档时设备上报的字节格式。
"""
import struct
import sys

def parse_pcapng_interrupt_in(filepath):
    with open(filepath, 'rb') as f:
        buf = f.read()
    magic = struct.unpack_from('<I', buf, 0)[0]
    if magic != 0x1A2B3C4D:
        print("不是 pcapng 格式"); return []
    # 解析 block
    off = 8  # 跳过 section header
    results = []
    n = len(buf)
    while off + 8 <= n:
        block_type, block_total_len = struct.unpack_from('<II', buf, off)
        if block_total_len < 12 or off + block_total_len > n:
            break
        block_body = buf[off+8 : off+block_total_len-4]
        # Enhanced Packet Block = 0x00000006, Packet Block = 0x00000003
        if block_type in (0x00000006, 0x00000003):
            # EPB: interface_id(u32), timestamp(u64), captured_len(u32), packet_len(u32)
            if block_type == 0x00000006:
                if len(block_body) < 20:
                    off += block_total_len; continue
                interface_id, ts_hi, ts_lo, cap_len, orig_len = struct.unpack_from('<IIIII', block_body, 0)
                pkt = block_body[20:20+cap_len]
            else:
                # Packet Block (old): interface_id(u16 pad u16), ts(u32), ts(u32), cap(u32), orig(u32)
                if len(block_body) < 20:
                    off += block_total_len; continue
                interface_id = struct.unpack_from('<H', block_body, 0)[0]
                ts_hi, ts_lo, cap_len, orig_len = struct.unpack_from('<IIII', block_body, 4)
                pkt = block_body[20:20+cap_len]
            ts = (ts_hi << 32) | ts_lo
            results.append((interface_id, ts, pkt))
        off += block_total_len
    return results

def main():
    path = sys.argv[1]
    pkts = parse_pcapng_interrupt_in(path)
    print(f"总包数: {len(pkts)}")
    # 按接口分组，找出带数据的 IN 包
    by_iface = {}
    for iface, ts, pkt in pkts:
        by_iface.setdefault(iface, []).append((ts, pkt))
    for iface, lst in sorted(by_iface.items()):
        print(f"\n=== 接口 {iface} : {len(lst)} 包 ===")
        # 只打印有实际载荷的非空包，且去重相邻相同
        prev = None
        shown = 0
        for ts, pkt in lst:
            if len(pkt) < 1:
                continue
            # USBPcap 包头: 通常指 header (27 bytes IRP) + 数据
            # 这里尝试剥离常见 USBPcap 头部：前若干字节是伪头
            # 简单策略：显示整包后 16 字节 + 完整长度
            if pkt == prev:
                continue
            prev = pkt
            # 尝试剥离 USBPcap 的 transfer 头: 第一个字节 0x01 通常表示... 直接显示尾部
            tail = pkt[-16:] if len(pkt) >= 16 else pkt
            print(f"  len={len(pkt):3d}  tail={tail.hex(' ')}  full={pkt.hex(' ')}")
            shown += 1
            if shown > 60:
                print("  ... (截断)")
                break

if __name__ == '__main__':
    main()
