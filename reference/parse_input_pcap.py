#!/usr/bin/env python3
"""解析 USBPcap 经典 pcap (magic 0xa1b2c3d4)，提取所有 设备->主机(IN) 的
中断/批量输入数据，定位 DPI 切档时的 Input Report 字节变化。

USBPcap 每包结构:
  pcap 包头 16B -> payload = USBPcap_HEADER + usb data
  USBPcap_HEADER (27B): header_len(u8), irp_id(u64), ... , data_length(u32) @ offset 23
  实际 USB 传输数据 = payload[header_len : header_len + data_length]
"""
import struct
import sys

def main():
    path = sys.argv[1]
    with open(path, 'rb') as f:
        buf = f.read()
    magic = struct.unpack_from('<I', buf, 0)[0]
    assert magic in (0xa1b2c3d4, 0xd4c3b2a1), "需要 classic pcap"
    swap = (magic == 0xd4c3b2a1)
    def u(fmt, off):
        v = struct.unpack_from(fmt, buf, off)
        if swap and fmt in ('<I','<H','<Q'):
            pass
        return v
    # 全局头
    ghdr = struct.unpack_from('<IHHiIII', buf, 0)
    # ghdr: magic, ver_maj, ver_min, thiszone, sigfigs, snaplen, network
    linktype = ghdr[6]
    print(f"linktype={linktype} snaplen={ghdr[5]}")
    off = 24
    n = len(buf)
    packets = []
    while off + 16 <= n:
        ts_sec, ts_usec, incl_len, orig_len = struct.unpack_from('<IIII', buf, off)
        off += 16
        data = buf[off:off+incl_len]
        off += incl_len
        if len(data) < 27:
            continue
        header_len = data[0]
        # data_length 在 USBPcap_HEADER 偏移 23 (u32)
        if header_len < 27:
            continue
        data_len = struct.unpack_from('<I', data, 23)[0]
        usb_data = data[header_len:header_len+data_len]
        # 判断方向: USBPcap_HEADER 偏移 8 是 irp_direction (0=in,1=out)? 实际字段:
        # 偏移 0: header_len
        # 偏移 1: irp_id_lo? 看 USBPcap 定义
        # irp_direction 在 offset 1? 我们用长度判断: IN 包通常有负载且是键盘/鼠标报告
        packets.append((ts_sec, ts_usec, usb_data))
    print(f"总包: {len(packets)}")
    # 过滤出有 USB 描述符特征(以 0x.. 开头) 且长度>0 的
    in_pkts = [p for p in packets if len(p[2]) > 0]
    print(f"非空包: {len(in_pkts)}")
    # 按长度分组，找出出现变化的短包 (Input Report 通常 <=64B)
    prev = None
    seq = 0
    for ts_sec, ts_usec, d in in_pkts:
        if d == prev:
            continue
        prev = d
        # 只关心 <=64 字节的（Input Report 范围）
        if len(d) > 64:
            continue
        print(f"[{seq}] +{ts_usec//1000:>5}ms len={len(d):2d} {d.hex(' ')}")
        seq += 1
        if seq > 80:
            print("...截断")
            break
        if seq > 80:
            print("...截断")
            break

if __name__ == '__main__':
    main()
