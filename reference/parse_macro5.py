#!/usr/bin/env python3
"""Parse macro_1.pcapng - correctly handle USBPcap header format"""
import struct

PCAP = r"C:\Users\fresh\Downloads\618XSD\captures\macro_1.pcapng"

with open(PCAP, 'rb') as f:
    data = f.read()

# Parse pcap header
magic = struct.unpack_from('<I', data, 0)[0]
assert magic == 0xa1b2c3d4, "Not a pcap file"

# Skip pcap header (24 bytes)
pos = 24
control_xfers = []

while pos < len(data):
    if pos + 16 > len(data):
        break
    
    ts_sec, ts_usec, incl_len, orig_len = struct.unpack_from('<IIII', data, pos)
    pos += 16
    
    if pos + incl_len > len(data) or incl_len < 28:
        pos += incl_len
        continue
    
    pkt = data[pos:pos + incl_len]
    pos += incl_len
    
    # USBPcap pseudoheader: headerLen is first 4 bytes (ULONG)
    header_len = struct.unpack_from('<I', pkt, 0)[0]  # 4 bytes!
    
    if header_len > incl_len or header_len < 24:
        continue
    
    # Parse the rest of the header
    irp_id = struct.unpack_from('<Q', pkt, 4)[0]
    status = struct.unpack_from('<I', pkt, 12)[0]
    urb_function = struct.unpack_from('<H', pkt, 16)[0]
    info = pkt[18]
    bus = pkt[19]
    device = pkt[20]
    endpoint = pkt[21]
    transfer = pkt[22]
    data_len = struct.unpack_from('<I', pkt, 23)[0]
    
    # USB data starts after the header
    usb_data = pkt[header_len:]
    
    # Control transfer: check URB function
    is_control = (urb_function in [0x0008, 0x001B, 0x0001, 0x0002, 0x000C])
    
    if not is_control:
        continue
    
    # Check for USB setup packet
    if len(usb_data) < 8:
        continue
    
    bmReqType = usb_data[0]
    bRequest = usb_data[1]
    wValue = struct.unpack_from('<H', usb_data, 2)[0]
    wIndex = struct.unpack_from('<H', usb_data, 4)[0]
    wLength = struct.unpack_from('<H', usb_data, 6)[0]
    
    # HID SET_REPORT: bmReqType=0x21, bRequest=0x09
    if bmReqType == 0x21 and bRequest == 0x09:
        report_id = wValue & 0xFF
        report_type = (wValue >> 8) & 0xFF
        
        # Feature report data follows the 8-byte setup packet
        report_data = usb_data[8:8 + wLength]
        
        control_xfers.append({
            'ts': ts_sec + ts_usec / 1000000,
            'report_id': report_id,
            'report_type': report_type,
            'wLength': wLength,
            'data': bytes(report_data),
            'urb_function': urb_function,
        })

print(f"Found {len(control_xfers)} HID SET_REPORT transfers\n")

button_codes = {
    0x01: '标准', 0x02: '左键', 0x03: '右键', 0x04: '中键',
    0x05: '后退', 0x06: '前进', 0x09: '上滚', 0x0A: '下滚',
    0x0B: '左滚', 0x0C: '右滚', 0x0D: 'DPI循环', 0x12: '宏'
}

for i, xfer in enumerate(control_xfers):
    d = xfer['data']
    rid = d[0] if len(d) > 0 else 0
    print(f"#{i+1:2d} t={xfer['ts']:.3f}s  ID=0x{rid:02X}  wLen={xfer['wLength']}  data={len(d)}b")
    
    if rid == 0x08:
        print(f"    >>> 0x08: 按键映射")
        for j in range(4, len(d)-1, 3):
            if j + 3 > len(d) - 1:
                break
            e = d[j:j+3]
            fc = e[0]
            name = button_codes.get(fc, f'0x{fc:02X}')
            marker = " <-- 宏!" if fc == 0x12 else ""
            print(f"    entry[{(j-4)//3:2d}]: {name:8s}  {e[0]:02x} {e[1]:02x} {e[2]:02x}{marker}")
        print(f"    最后字节: 0x{d[-1]:02X}")
        
    elif rid == 0x09:
        print(f"    >>> 0x09: 宏数据  chunk={d[3]}  btn={d[2]}")
        payload = d[4:]
        nz = [(j, b) for j, b in enumerate(payload) if b != 0]
        if nz:
            print(f"    非零: {'  '.join(f'[{j}]={b:02x}' for j, b in nz)}")
        if d[3] == 0:
            # Key pairs from cmd[8] = payload[4]
            for k in range(4, len(payload), 2):
                if k+1 < len(payload) and (payload[k] != 0 or payload[k+1] != 0):
                    print(f"    key[{k-4}] = 0x{payload[k]:02X} 0x{payload[k+1]:02X}")
    
    else:
        print(f"    >>> 0x{rid:02X}: {' '.join(f'{b:02x}' for b in d)}")

# Summary
print(f"\n{'='*60}")
print(f"发送序列:")
for i, xfer in enumerate(control_xfers):
    d = xfer['data']
    rid = d[0] if len(d) > 0 else 0
    names = {0x0C: "唤醒", 0x04: "DPI", 0x05: "未知", 0x06: "DPI选择", 0x08: "按键映射", 0x09: "宏数据"}
    name = names.get(rid, f"0x{rid:02X}")
    extra = ""
    if rid == 0x09 and len(d) >= 4:
        extra = f" (chunk {d[3]}, btn={d[2]})"
    print(f"  {i+1}. 0x{rid:02X} {name}{extra}  ({len(d)} bytes)")