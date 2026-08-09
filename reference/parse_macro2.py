#!/usr/bin/env python3
"""Parse macro_1.pcapng - extract HID feature reports properly"""
import struct

PCAP = r"C:\Users\fresh\Downloads\618XSD\captures\macro_1.pcapng"

def read_pcap(filepath):
    """Read USBPcap pcap file and extract USB packets"""
    with open(filepath, 'rb') as f:
        data = f.read()
    
    # Parse pcap global header (24 bytes)
    magic = struct.unpack_from('<I', data, 0)[0]
    if magic == 0xa1b2c3d4:
        endian = '<'
    elif magic == 0xd4c3b2a1:
        endian = '>'
    else:
        raise ValueError(f"Not a pcap file: magic=0x{magic:08X}")
    
    # Skip pcap header (24 bytes)
    pos = 24
    packets = []
    
    while pos < len(data):
        if pos + 16 > len(data):
            break
        
        # Packet header (16 bytes)
        ts_sec = struct.unpack_from(endian + 'I', data, pos)[0]
        ts_usec = struct.unpack_from(endian + 'I', data, pos + 4)[0]
        incl_len = struct.unpack_from(endian + 'I', data, pos + 8)[0]
        orig_len = struct.unpack_from(endian + 'I', data, pos + 12)[0]
        
        pos += 16
        
        if pos + incl_len > len(data):
            break
        
        pkt = data[pos:pos + incl_len]
        pos += incl_len
        
        # Parse USBPcap pseudoheader (27 bytes)
        if len(pkt) < 27:
            continue
        
        header_len = pkt[0]  # USBPcap header length (always 27)
        irp_id = struct.unpack_from('<Q', pkt, 1)[0]
        status = struct.unpack_from('<I', pkt, 9)[0]
        urb_function = struct.unpack_from('<H', pkt, 13)[0]
        # 15: IRP info
        bus = pkt[16]
        device = pkt[17]
        endpoint = pkt[18]
        transfer_type = pkt[19]
        data_len = struct.unpack_from('<I', pkt, 20)[0]
        
        # Skip USBPcap header
        usb_data = pkt[27:]
        
        # Check if this is a control transfer (URB_FUNCTION_CONTROL_TRANSFER = 0x0008)
        # or control transfer with descriptor (0x0001, 0x0002, etc.)
        is_control = (urb_function in [0x0008, 0x0009, 0x001B]) and (endpoint & 0x7F) == 0 and device == 4
        
        # Control transfer: the USB data starts with a setup packet (8 bytes)
        if len(usb_data) >= 8:
            bmReqType = usb_data[0]
            bRequest = usb_data[1]
            wValue = struct.unpack_from('<H', usb_data, 2)[0]
            wIndex = struct.unpack_from('<H', usb_data, 4)[0]
            wLength = struct.unpack_from('<H', usb_data, 6)[0]
            
            # HID SET_REPORT (0x21/0x09) or GET_REPORT (0xA1/0x01)
            is_hid_report = (bmReqType == 0x21 and bRequest == 0x09) or (bmReqType == 0xA1 and bRequest == 0x01)
            
            if is_hid_report:
                report_id = wValue & 0xFF
                report_type = (wValue >> 8) & 0xFF
                
                # The actual report data follows the 8-byte setup packet
                report_data = usb_data[8:8 + wLength]
                
                packets.append({
                    'ts': ts_sec + ts_usec / 1e6,
                    'device': device,
                    'endpoint': endpoint,
                    'urb_function': urb_function,
                    'transfer_type': transfer_type,
                    'bmReqType': bmReqType,
                    'bRequest': bRequest,
                    'report_id': report_id,
                    'report_type': report_type,
                    'wLength': wLength,
                    'data': report_data,
                    'direction': 'host->dev' if (bmReqType & 0x80) == 0 else 'dev->host'
                })
    
    return packets

packets = read_pcap(PCAP)

print(f"Found {len(packets)} HID SET/GET_REPORT transfers\n")

# Filter to only SET_REPORT (host->device)
set_reports = [p for p in packets if p['direction'] == 'host->dev']

print(f"SET_REPORT transfers: {len(set_reports)}\n")

for i, p in enumerate(set_reports):
    d = p['data']
    print(f"{'='*60}")
    print(f"#{i+1:2d}  t={p['ts']:.3f}s  Report ID 0x{p['report_id']:02X}  ({len(d)} bytes)")
    print(f"{'='*60}")
    hex_str = ' '.join(f'{b:02x}' for b in d)
    print(f"  Raw: {hex_str}")
    
    rid = d[0] if len(d) > 0 else 0
    
    if rid == 0x0C:
        print(f"  >>> 0x0C: 唤醒/握手")
        
    elif rid == 0x04:
        print(f"  >>> 0x04: DPI 配置")
        if len(d) >= 25:
            enabled = d[5]
            dpi_vals = []
            for j in range(8):
                low = d[8+j] if 8+j < len(d) else 0
                high = d[16+j] if 16+j < len(d) else 0
                val = low | (high << 8)
                dpi_vals.append(f"{val}(0x{val:04X})")
            print(f"      启用: 0x{enabled:02X}  档位: {', '.join(dpi_vals)}")
            print(f"      当前档: 0x{d[24]:02X}")
            # Colors
            colors = []
            for j in range(8):
                start = 25 + j * 3
                if start + 2 < len(d):
                    colors.append(f"#{d[start]:02X}{d[start+1]:02X}{d[start+2]:02X}")
            if colors:
                print(f"      颜色: {', '.join(colors)}")
            # Checksum
            if len(d) >= 52:
                cs = (d[50] << 8) | d[51]
                print(f"      校验和: 0x{cs:04X}")
        
    elif rid == 0x05:
        print(f"  >>> 0x05: 未知配置")
        print(f"      数据: {hex_str}")
        
    elif rid == 0x06:
        print(f"  >>> 0x06: DPI 选择")
        if len(d) >= 4:
            print(f"      档位: {d[3]}")
        
    elif rid == 0x08:
        print(f"  >>> 0x08: 按键映射")
        button_codes = {
            0x01: '标准', 0x02: '左键', 0x03: '右键', 0x04: '中键',
            0x05: '后退', 0x06: '前进', 0x09: '上滚', 0x0A: '下滚',
            0x0B: '左滚', 0x0C: '右滚', 0x0D: 'DPI循环', 0x12: '宏'
        }
        print(f"      头部: {' '.join(f'{b:02x}' for b in d[:4])}")
        
        # 3-byte entries from byte 4
        for j in range(4, len(d)-1, 3):
            if j + 3 > len(d)-1:
                break
            entry = d[j:j+3]
            fc = entry[0]
            name = button_codes.get(fc, f'0x{fc:02X}')
            print(f"      entry[{(j-4)//3:2d}]: {name:8s}  {' '.join(f'{b:02x}' for b in entry)}")
        
        if len(d) > 0:
            print(f"      最后字节: 0x{d[-1]:02X} (校验和?)")
        
    elif rid == 0x09:
        print(f"  >>> 0x09: 宏数据")
        if len(d) >= 4:
            btn = d[2]
            chunk = d[3]
            subcmd = d[1]
            payload = d[4:]
            print(f"      按钮索引: {btn}, 子命令: 0x{subcmd:02X}, 分块: {chunk}")
            print(f"      载荷({len(payload)}b): {' '.join(f'{b:02x}' for b in payload)}")
            
            non_zero = [(j, f'{b:02x}') for j, b in enumerate(payload) if b != 0]
            if non_zero:
                print(f"      非零字节: {non_zero}")
            
            # For chunk 0, interpret internal buffer
            if chunk == 0 and len(payload) >= 5:
                print(f"      cmd[3]=0x{payload[0]:02X} (config field)")
                print(f"      cmd[4..6]={' '.join(f'{b:02x}' for b in payload[0:3])} (action)")
                print(f"      cmd[7]=0x{payload[3]:02X} (modifier)")
                # Key pairs from cmd[8] = payload[4]
                key_pairs = []
                for k in range(4, min(len(payload), 130), 2):
                    if k+1 < len(payload) and (payload[k] != 0 or payload[k+1] != 0):
                        key_pairs.append(f"[{k-4}] 0x{payload[k]:02X} 0x{payload[k+1]:02X}")
                if key_pairs:
                    print(f"      按键对: {'  '.join(key_pairs)}")
                
                # Checksum at end
                if len(payload) >= 128:
                    cs = (payload[126] << 8) | payload[127]
                    print(f"      校验和(offset 126-127): 0x{cs:04X}")
    
    print()

# Print summary
print(f"\n{'='*60}")
print(f"SUMMARY - 发送序列:")
print(f"{'='*60}")
for i, p in enumerate(set_reports):
    d = p['data']
    rid = d[0] if len(d) > 0 else 0
    names = {0x0C: "0x0C 唤醒", 0x04: "0x04 DPI", 0x05: "0x05 未知",
             0x06: "0x06 DPI选择", 0x08: "0x08 按键映射", 0x09: "0x09 宏数据"}
    name = names.get(rid, f"0x{rid:02X}")
    extra = ""
    if rid == 0x09 and len(d) >= 4:
        extra = f" (chunk {d[3]}, btn={d[2]})"
    print(f"  {i+1}. {name}{extra}  ({len(d)} bytes)")