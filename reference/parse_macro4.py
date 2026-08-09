#!/usr/bin/env python3
"""Parse macro_1.pcapng - correctly extract HID feature reports from USBPcap format"""
import struct

PCAP = r"C:\Users\fresh\Downloads\618XSD\captures\macro_1.pcapng"

with open(PCAP, 'rb') as f:
    data = f.read()

# Parse pcap header
magic = struct.unpack_from('<I', data, 0)[0]
endian = '<'  # USBPcap is always LE

# Skip pcap header (24 bytes) and find all control transfers
pos = 24
control_xfers = []

while pos < len(data):
    if pos + 16 > len(data):
        break
    
    ts_sec, ts_usec, incl_len, orig_len = struct.unpack_from(endian + 'IIII', data, pos)
    pos += 16
    
    if pos + incl_len > len(data) or incl_len < 27:
        pos += incl_len
        continue
    
    pkt = data[pos:pos + incl_len]
    pos += incl_len
    
    # USBPcap pseudoheader (27 bytes)
    header_len = pkt[0]  # should be 27
    if header_len != 27:
        continue
    
    urb_function = struct.unpack_from('<H', pkt, 13)[0]
    device = pkt[17]
    endpoint = pkt[18]
    
    # Control transfer: URB_FUNCTION_CONTROL_TRANSFER = 0x0008
    # URB_FUNCTION_CONTROL_TRANSFER_EX = 0x001B
    if urb_function not in [0x0008, 0x001B]:
        continue
    
    usb_data = pkt[27:]
    
    # USB setup packet (8 bytes)
    # bmRequestType, bRequest, wValue, wIndex, wLength
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
        report_type = (wValue >> 8) & 0xFF  # 0x03 = Feature
        
        # The actual feature report data starts after the 8-byte setup packet
        report_data = usb_data[8:8 + wLength]
        
        control_xfers.append({
            'ts': ts_sec + ts_usec / 1000000,
            'report_id': report_id,
            'report_type': report_type,
            'wLength': wLength,
            'data': bytes(report_data),
            'device': device,
            'endpoint': endpoint
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
    print(f"#{i+1:2d} t={xfer['ts']:.3f}s  ID=0x{rid:02X}  ({len(d)} bytes)")
    
    if rid == 0x0C:
        print(f"    >>> 0x0C: 唤醒/握手")
        print(f"    {' '.join(f'{b:02x}' for b in d)}")
        
    elif rid == 0x04:
        print(f"    >>> 0x04: DPI 配置")
        enabled = d[5]
        dpi_vals = []
        for j in range(8):
            low = d[8+j] if 8+j < len(d) else 0
            high = d[16+j] if 16+j < len(d) else 0
            val = low | (high << 8)
            dpi_vals.append(f"{val}(0x{val:04X})")
        print(f"    启用: 0x{enabled:02X}  档位: {', '.join(dpi_vals)}")
        if len(d) >= 52:
            cs = (d[50] << 8) | d[51]
            print(f"    校验和: 0x{cs:04X}")
        
    elif rid == 0x05:
        print(f"    >>> 0x05: 未知配置")
        print(f"    {' '.join(f'{b:02x}' for b in d)}")
        
    elif rid == 0x06:
        print(f"    >>> 0x06: DPI 选择  档位={d[3]}")
        
    elif rid == 0x08:
        print(f"    >>> 0x08: 按键映射")
        # 3-byte entries from byte 4
        for j in range(4, len(d)-1, 3):
            if j + 3 > len(d) - 1:
                break
            e = d[j:j+3]
            fc = e[0]
            name = button_codes.get(fc, f'0x{fc:02X}')
            if fc == 0x12:
                print(f"    entry[{(j-4)//3:2d}]: {name:8s}  {e[0]:02x} {e[1]:02x} {e[2]:02x}  <-- 宏!")
            else:
                print(f"    entry[{(j-4)//3:2d}]: {name:8s}  {e[0]:02x} {e[1]:02x} {e[2]:02x}")
        print(f"    校验和: 0x{d[-1]:02X}")
        
    elif rid == 0x09:
        print(f"    >>> 0x09: 宏数据  chunk={d[3]}  btn={d[2]}")
        payload = d[4:]
        nz = [(j, b) for j, b in enumerate(payload) if b != 0]
        if nz:
            print(f"    非零字节: {' '.join(f'[{j}]={b:02x}' for j, b in nz)}")
        
        if d[3] == 0:  # chunk 0
            print(f"    cmd[3]=0x{payload[0]:02X} cmd[4..6]={' '.join(f'{b:02x}' for b in payload[0:3])} cmd[7]=0x{payload[3]:02X}")
            for k in range(4, len(payload), 2):
                if k+1 < len(payload) and (payload[k] != 0 or payload[k+1] != 0):
                    print(f"    key_pair[{k-4}] = 0x{payload[k]:02X} 0x{payload[k+1]:02X}")

# Print summary
print(f"\n{'='*60}")
print(f"发送序列:")
print(f"{'='*60}")
for i, xfer in enumerate(control_xfers):
    d = xfer['data']
    rid = d[0] if len(d) > 0 else 0
    names = {0x0C: "0x0C 唤醒", 0x04: "0x04 DPI", 0x05: "0x05 未知",
             0x06: "0x06 DPI选择", 0x08: "0x08 按键映射", 0x09: "0x09 宏数据"}
    name = names.get(rid, f"0x{rid:02X}")
    extra = ""
    if rid == 0x09 and len(d) >= 4:
        extra = f" (chunk {d[3]}, btn={d[2]})"
    print(f"  {i+1}. {name}{extra}  ({len(d)} bytes)")

# Analyze the 0x09 macro data
print(f"\n{'='*60}")
print(f"0x09 宏数据关键分析")
print(f"{'='*60}")

# Find the 3 chunks
chunks = {}
for xfer in control_xfers:
    d = xfer['data']
    if d[0] == 0x09:
        chunks[d[3]] = d

if 0 in chunks and 1 in chunks and 2 in chunks:
    # Reconstruct internal buffer
    c0 = chunks[0][4:]
    c1 = chunks[1][4:]
    c2 = chunks[2][4:]
    
    internal = bytearray(131)
    internal[0] = 0x09
    internal[1] = 0x83
    internal[2] = chunks[0][2]  # btn idx
    internal[3:63] = c0[:60]
    internal[63:123] = c1[:60]
    internal[123:131] = c2[:8]
    
    # HID keycodes
    hid_keys = {
        0x04: 'A', 0x05: 'B', 0x06: 'C', 0x07: 'D', 0x08: 'E',
        0x09: 'F', 0x0A: 'G', 0x0B: 'H', 0x0C: 'I', 0x0D: 'J',
        0x0E: 'K', 0x0F: 'L', 0x10: 'M', 0x11: 'N', 0x12: 'O',
        0x13: 'P', 0x14: 'Q', 0x15: 'R', 0x16: 'S', 0x17: 'T',
        0x18: 'U', 0x19: 'V', 0x1A: 'W', 0x1B: 'X', 0x1C: 'Y',
        0x1D: 'Z', 0x1E: '1!', 0x1F: '2@', 0x20: '3#', 0x21: '4$',
        0x22: '5%', 0x23: '6^', 0x24: '7&', 0x25: '8*', 0x26: '9(',
        0x27: '0)', 0x28: 'ENTER', 0x29: 'ESC', 0x2A: 'BACKSPACE',
        0x2B: 'TAB', 0x2C: 'SPACE', 0x2D: '-_', 0x2E: '=+',
        0x2F: '[{', 0x30: ']}', 0x31: '\\|', 0x33: ';:',
        0x34: '\'"', 0x35: '`~', 0x36: ',<', 0x37: '.>',
        0x38: '/?', 0x39: 'CAPS', 0x4B: 'PAGEUP', 0x4C: 'PAGEDOWN',
        0x4F: 'END', 0x50: 'HOME', 0x51: 'LEFT', 0x52: 'UP',
        0x53: 'RIGHT', 0x54: 'DOWN', 0xE0: 'CTRL', 0xE1: 'SHIFT',
        0xE2: 'ALT', 0xE3: 'GUI',
    }
    
    print(f"按钮索引: {internal[2]} (UI按钮 {internal[2]+1})")
    print(f"cmd[3]  = 0x{internal[3]:02X} (配置字段 - 可能含播放方式?)")
    print(f"cmd[4..6] = {' '.join(f'{b:02x}' for b in internal[4:7])} (动作值×3)")
    print(f"cmd[7]  = 0x{internal[7]:02X} (修饰键)")
    
    print(f"")
    print(f"按键序列解析:")
    # Parse key pairs from cmd[8]
    for k in range(8, 128, 2):
        kc = internal[k]
        fl = internal[k+1]
        if kc == 0 and fl == 0:
            continue
        key_name = hid_keys.get(kc, f'0x{kc:02X}')
        if fl == 0x81:
            action = "按下"
        elif fl == 0x00:
            action = "释放"
        elif fl == 0x01:
            action = "鼠标按下?"
        else:
            action = f"标记=0x{fl:02X}"
        print(f"  0x{kc:02X}({key_name}) → {action}")
    
    cs = (internal[129] << 8) | internal[130]
    print(f"校验和: 0x{cs:04X}")
    
    print(f"")
    print(f"结论:")
    print(f"  - 'A'键编码 = HID Usage 0x04")
    print(f"  - 按下标志 = 0x81, 释放标志 = 0x00")
    print(f"  - 02 01 可能是鼠标事件前缀或下一个事件标记")
    print(f"  - 播放方式/循环次数 不在0x09宏数据中 (全为零)")
    print(f"  → 播放方式很可能在0x08按键映射的宏条目参数中!")
    print(f"  → 0x08 entry[2] = 12 00 04 中, 00=播放方式? 04=宏索引?")