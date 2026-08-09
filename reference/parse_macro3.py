#!/usr/bin/env python3
"""Analyze macro_1.pcapng - manually extracted data"""

# Reports extracted from pcap (USB Control data portion only)
reports = [
    ("0x0C 唤醒", bytes.fromhex("0c0a01fe01fe00000000")),
    ("0x04 DPI", bytes.fromhex("04380100001f101020b04060a0000000030406090f00000003ff000000ff000000ffff00ffffff00ff00ffff4000ffffff020fac00000000")),
    ("0x05 未知", bytes.fromhex("050f010403a80000ff010301b20000")),
    ("0x06 DPI选择", bytes.fromhex("06090102fd00000000")),
    ("0x08 按键映射", bytes.fromhex("083b010200000300000600001200040400000d00000100000100000100000100000100000100000100000100000b00000c00000900000a00000064")),
    ("0x09 chunk0", bytes.fromhex("0940040000000000030000000000000000000000000000000000000000000000000000000000000000000000020104810400000000000000000000000000000000000000000000000000000000")),
    ("0x09 chunk1", bytes.fromhex("094004010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")),
    ("0x09 chunk2", bytes.fromhex("090c040200000000000000008f00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")),
]

button_codes = {
    0x01: '标准', 0x02: '左键', 0x03: '右键', 0x04: '中键',
    0x05: '后退', 0x06: '前进', 0x09: '上滚', 0x0A: '下滚',
    0x0B: '左滚', 0x0C: '右滚', 0x0D: 'DPI循环', 0x12: '宏'
}

for name, d in reports:
    print(f"\n{'='*60}")
    print(f"  {name}  ({len(d)} bytes)")
    print(f"{'='*60}")
    hex_str = ' '.join(f'{b:02x}' for b in d)
    print(f"  {hex_str}")
    
    if d[0] == 0x08:
        print(f"\n  按键映射解析:")
        # 08 3b 01 = header, byte 3 = ?
        for j in range(4, len(d)-1, 3):
            if j + 3 > len(d) - 1:
                break
            e = d[j:j+3]
            fc = e[0]
            name = button_codes.get(fc, f'0x{fc:02X}')
            print(f"    entry[{(j-4)//3:2d}]: {name:8s}  {e[0]:02x} {e[1]:02x} {e[2]:02x}")
        print(f"    校验和: 0x{d[-1]:02X}")
    
    elif d[0] == 0x09:
        print(f"\n  宏数据解析:")
        btn = d[2]
        chunk = d[3]
        payload = d[4:]
        print(f"    按钮索引: {btn}  (UI按钮 {btn+1}: {button_codes.get(btn, '?')})")
        print(f"    分块: {chunk}")
        
        # Show non-zero bytes in payload
        nz = [(j, b) for j, b in enumerate(payload) if b != 0]
        if nz:
            print(f"    载荷非零字节:")
            for j, b in nz:
                print(f"      [{j:3d}] = 0x{b:02X} ({b})")
        else:
            print(f"    载荷全部为零")
        
        # If chunk 0, interpret as internal buffer
        if chunk == 0:
            print(f"\n    内部缓冲 (cmd[3..]):")
            print(f"      cmd[3]  = 0x{payload[0]:02X} (config field)")
            print(f"      cmd[4..6] = {' '.join(f'{b:02x}' for b in payload[0:3])} (action ×3)")
            print(f"      cmd[7]  = 0x{payload[3]:02X} (modifier)")
            print(f"      cmd[8..] 按键映射对:")
            for k in range(4, min(len(payload), 130), 2):
                if k+1 < len(payload) and (payload[k] != 0 or payload[k+1] != 0):
                    print(f"        [{k-4:3d}] = 0x{payload[k]:02X} 0x{payload[k+1]:02X}")

# Analyze the 0x08 macro entry in detail
print(f"\n{'='*60}")
print(f"  0x08 宏条目分析")
print(f"{'='*60}")
d08 = reports[4][1]
# Find entry with macro code 0x12
for j in range(4, len(d08)-1, 3):
    if j + 3 > len(d08) - 1:
        break
    e = d08[j:j+3]
    if e[0] == 0x12:
        print(f"  宏条目在 entry[{(j-4)//3}]")
        print(f"    函数=0x12(宏)  参数1=0x{e[1]:02X}  参数2=0x{e[2]:02X}")
        print(f"  参数1可能是 播放方式/循环模式?")
        print(f"  参数2可能是 宏索引? (0x04 = 第4个宏)")

# Reconstruct internal buffer from 3 chunks
print(f"\n{'='*60}")
print(f"  0x09 内部缓冲重建 (131 bytes)")
print(f"{'='*60}")

chunk0_payload = reports[5][1][4:]  # 60 bytes
chunk1_payload = reports[6][1][4:]  # 60 bytes
chunk2_payload = reports[7][1][4:]  # 60 bytes (last 8 real + 52 padding)

internal = bytearray(131)
internal[0] = 0x09
internal[1] = 0x83
internal[2] = 0x04  # btn_idx

# Copy payloads
internal[3:63] = chunk0_payload[:60]
internal[63:123] = chunk1_payload[:60]
internal[123:131] = chunk2_payload[:8]

print(f"  完整内部缓冲:")
for i in range(0, 131, 16):
    chunk = internal[i:i+16]
    hex_part = ' '.join(f'{b:02x}' for b in chunk)
    ascii_part = ''.join(chr(b) if 32 <= b < 127 else '.' for b in chunk)
    print(f"  [{i:3d}] {hex_part:48s} {ascii_part}")

# Parse key mapping pairs
print(f"\n  按键映射对解析:")
print(f"  cmd[3]  = 0x{internal[3]:02X} (config field)")
print(f"  cmd[4..6] = {' '.join(f'{b:02x}' for b in internal[4:7])} (action ×3)")
print(f"  cmd[7]  = 0x{internal[7]:02X} (modifier -> 1)")

# Key pairs from cmd[8] = internal[8]
print(f"  cmd[8..127] 按键对 (2字节每对):")
pair_count = 0
for k in range(8, 128, 2):
    if k+1 < len(internal):
        kc = internal[k]
        fl = internal[k+1]
        if kc != 0 or fl != 0:
            pair_count += 1
            print(f"    pair[{pair_count}] keycode=0x{kc:02X} flag=0x{fl:02X}")

# Checksum
cs = (internal[129] << 8) | internal[130]
print(f"  cmd[129-130] 校验和: 0x{cs:04X}")

# HID keycode lookup
print(f"\n  按键码解释:")
print(f"  HID Usage ID 0x04 = Keyboard 'A'")
print(f"  0x81 = Key down flag?   0x00 = Key up flag?")
print(f"  0x02 = 鼠标左键?")
print(f"  所以 'A' 键按下 = [04 81], 释放 = [04 00]")
print(f"  而 02 01 可能是事件分隔符或鼠标事件标记")