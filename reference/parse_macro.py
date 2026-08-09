#!/usr/bin/env python3
"""Parse macro_1.pcapng - extract and analyze HID feature reports"""
import subprocess, sys

PCAP = r"C:\Users\fresh\Downloads\618XSD\captures\macro_1.pcapng"
TSHARK = r"C:\Program Files\Wireshark\tshark.exe"

# Extract all control transfer hex data
result = subprocess.run(
    [TSHARK, "-r", PCAP, "-Y", "usb.dst == 1.4.0", "-x"],
    capture_output=True, text=True
)

# Parse the hex dump output
lines = result.stdout.strip().split('\n')
reports = []
current_report = []
in_usb_control = False

for line in lines:
    if 'USB Control' in line:
        if current_report:
            reports.append(bytes(current_report))
            current_report = []
        in_usb_control = True
        continue
    if in_usb_control and line.strip() and not line.startswith('USB'):
        # Parse hex data from the dump line
        # Format: "0000  09 0c 03 02 ..."
        hex_part = line[6:6+49]  # Extract hex bytes portion
        bytes_str = hex_part.strip().split()
        for b in bytes_str:
            try:
                current_report.append(int(b, 16))
            except ValueError:
                pass

if current_report:
    reports.append(bytes(current_report))

print(f"Found {len(reports)} control transfers\n")

for i, report in enumerate(reports):
    rid = report[0] if report else 0
    print(f"{'='*60}")
    print(f"Report {i+1}: Report ID 0x{rid:02X}, {len(report)} bytes")
    print(f"{'='*60}")
    
    # Format hex dump
    hex_str = ' '.join(f'{b:02x}' for b in report)
    print(f"  Raw: {hex_str}")
    
    # Parse by Report ID
    if rid == 0x0C:
        print(f"  >>> 0x0C: 握手/唤醒命令")
        print(f"      数据: {' '.join(f'{b:02x}' for b in report)}")
        
    elif rid == 0x04:
        print(f"  >>> 0x04: DPI 配置")
        dpi_enabled = report[5]
        dpi_vals = []
        for j in range(8):
            low = report[8+j] if 8+j < len(report) else 0
            high = report[16+j] if 16+j < len(report) else 0
            val = low | (high << 8)
            dpi_vals.append(val)
        print(f"      启用位图: 0x{dpi_enabled:02X} ({dpi_enabled:08b})")
        print(f"      档位值: {dpi_vals}")
        print(f"      当前档: 0x{report[24]:02X}")
        
    elif rid == 0x05:
        print(f"  >>> 0x05: 未知配置")
        print(f"      数据: {' '.join(f'{b:02x}' for b in report)}")
        
    elif rid == 0x06:
        print(f"  >>> 0x06: DPI 选择")
        print(f"      档位索引: {report[3]}")
        print(f"      ~idx: 0x{report[4]:02X}")
        
    elif rid == 0x08:
        print(f"  >>> 0x08: 按键映射")
        # Parse entries
        header = report[0:4]
        print(f"      头部: {' '.join(f'{b:02x}' for b in header)}")
        
        # Try 3-byte entries starting from byte 3
        button_codes = {
            0x01: '标准', 0x02: '左键', 0x03: '右键', 0x04: '中键',
            0x05: '后退', 0x06: '前进', 0x09: '上滚', 0x0A: '下滚',
            0x0B: '左滚', 0x0C: '右滚', 0x0D: 'DPI循环', 0x12: '宏'
        }
        
        # Try different entry sizes
        for entry_size in [2, 3, 4]:
            print(f"\n      按{entry_size}字节解析:")
            entries = []
            for j in range(4, len(report)-1, entry_size):
                if j + entry_size > len(report)-1:
                    break
                entry = report[j:j+entry_size]
                fc = entry[0]
                name = button_codes.get(fc, f'未知({fc:02X})')
                extra = ' '.join(f'{b:02x}' for b in entry[1:])
                entries.append(f"{name}[{extra}]")
            print(f"      {' | '.join(entries)}")
        
        # Checksum
        print(f"      最后字节(校验和?): 0x{report[-1]:02X}")
        
    elif rid == 0x09:
        print(f"  >>> 0x09: 宏数据 (chunk {report[3]})")
        subcmd = report[1]
        btn_idx = report[2]
        chunk_idx = report[3]
        payload = report[4:]
        print(f"      subcmd: 0x{subcmd:02X}, 按钮索引: {btn_idx}, chunk: {chunk_idx}")
        print(f"      载荷({len(payload)}字节): {' '.join(f'{b:02x}' for b in payload)}")
        
        # Highlight non-zero bytes
        non_zero = [(j, f'{b:02x}') for j, b in enumerate(payload) if b != 0]
        if non_zero:
            print(f"      非零字节: {non_zero}")
        
        # For chunk 0, reconstruct internal buffer
        if chunk_idx == 0:
            print(f"\n      内部缓冲 cmd[3..62]:")
            cmd3 = payload[0]  # config field
            action = payload[0:3]
            modifier = payload[3] if len(payload) > 3 else 0
            print(f"      cmd[3](config) = 0x{cmd3:02X}")
            print(f"      cmd[4..6](action) = {' '.join(f'{b:02x}' for b in payload[0:3])}")
            print(f"      cmd[7](modifier) = 0x{modifier:02X}")
            
            # Find non-zero key pairs
            print(f"      按键映射对(从cmd[8]=payload[5]):")
            key_data = payload[5:]
            pairs = []
            for j in range(0, len(key_data)-1, 2):
                if key_data[j] != 0 or key_data[j+1] != 0:
                    pairs.append(f"[{j//2}] 0x{key_data[j]:02X} 0x{key_data[j+1]:02X}")
            if pairs:
                print(f"      {'  '.join(pairs)}")
            else:
                print(f"      (全部为零)")
                
    else:
        print(f"  >>> 未知 Report ID 0x{rid:02X}")

print(f"\n{'='*60}")
print("SUMMARY - 发送序列:")
print(f"{'='*60}")

# Re-list reports in order
for i, report in enumerate(reports):
    rid = report[0] if report else 0
    names = {0x0C: "0x0C 唤醒", 0x04: "0x04 DPI", 0x05: "0x05 未知",
             0x06: "0x06 DPI选择", 0x08: "0x08 按键映射", 0x09: "0x09 宏数据"}
    name = names.get(rid, f"0x{rid:02X}")
    extra = ""
    if rid == 0x09:
        extra = f" (chunk {report[3]}, btn={report[2]})"
    print(f"  {i+1}. {name}{extra}  ({len(report)} bytes)")