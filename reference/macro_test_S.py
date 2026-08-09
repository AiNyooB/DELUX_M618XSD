#!/usr/bin/env python3
"""测试：写 S 键宏到前进键，任意键停止播放"""
import ctypes, os, time

APP = r"C:\Users\fresh\Downloads\618XSD\extracted\app"
DLL_PATH = os.path.join(APP, "hiddriver_ms_4.dll")
VID, PID = 0x1D57, 0xFA60

# S 键 = 0x16
KEYCODE = 0x16
MACRO_ID = 2
BTN_ENTRY = 4  # 中键

# 0x08 报告（改 entry[2] = 宏 ID 2，其余保持默认）
entries = bytearray(54)
default = [
    (0x02,0,0),(0x03,0,0),(0x06,0,0),(0x12,0,4),(0x04,0,0),(0x0D,0,0),
    (0x01,0,0),(0x01,0,0),(0x01,0,0),(0x01,0,0),(0x01,0,0),(0x01,0,0),
    (0x01,0,0),(0x01,0,0),(0x0B,0,0),(0x0C,0,0),(0x09,0,0),(0x0A,0,0),
]
for i,(f,p1,p2) in enumerate(default):
    entries[i*3]=f; entries[i*3+1]=p1; entries[i*3+2]=p2
entries[BTN_ENTRY*3]=0x12; entries[BTN_ENTRY*3+1]=0; entries[BTN_ENTRY*3+2]=MACRO_ID

r08 = bytearray(59)
r08[0]=0x08; r08[1]=0x3B; r08[2]=0x01
r08[3:57]=entries
cs=sum(r08[3:57])&0xFFFF; r08[57]=(cs>>8)&0xFF; r08[58]=cs&0xFF

# 0x09 内部缓冲
buf = bytearray(131)
buf[0]=0x09; buf[1]=0x83; buf[2]=MACRO_ID
buf[3]=0x01   # 任意键停止
buf[7]=0x01   # modifier
buf[28]=0x02  # 按键对数量
buf[29]=0x01  # 固定
buf[30]=KEYCODE; buf[31]=0x81  # S 按下
buf[32]=KEYCODE; buf[33]=0x00  # S 释放
cs=sum(buf[3:129])&0xFFFF; buf[129]=(cs>>8)&0xFF; buf[130]=cs&0xFF

# 3 个 chunk
def mkchunk(idx, payload):
    c=bytearray(64); c[0]=0x09; c[1]=0x40 if idx<2 else 0x0C
    c[2]=MACRO_ID; c[3]=idx; c[4:64]=payload; return bytes(c)
c0 = mkchunk(0, buf[3:0x3F])
c1 = mkchunk(1, buf[0x3F:0x7B])
c2 = mkchunk(2, buf[0x7B:0x83] + b'\x00'*52)

wake = bytes([0x0C,0x0A,0x01,0xFE,0x01,0xFE,0,0,0,0])

print("="*50)
print("  测试：前进键 → S 键（任意键停止）")
print("="*50)
print(f"\n  S 键码: 0x{KEYCODE:02X}")
print(f"  宏 ID:  {MACRO_ID}")
print(f"  前进键: entry[{BTN_ENTRY}]")
print(f"\n  0x08: {r08.hex(' ')}")
print(f"  0x09 ch0: {c0.hex(' ')}")
print(f"  0x09 ch1: {c1.hex(' ')}")
print(f"  0x09 ch2: {c2.hex(' ')}")

print(f"\n  ⚠️ 确保 Mouse.exe 已关闭!")
dll = ctypes.WinDLL(DLL_PATH)
dll.Set_VIDPID(ctypes.c_ushort(VID), ctypes.c_ushort(PID))
dll.Open_FeatureDevice()
for name, data in [("唤醒",wake),("按键映射",r08),("宏ch0",c0),("宏ch1",c1),("宏ch2",c2)]:
    r = dll.SetFeature(ctypes.c_char_p(bytes(data)), ctypes.c_int(len(data)))
    print(f"  {name} → 返回={r}")
    time.sleep(0.2)
dll.Close_FeatureDevice()
print(f"\n  ✅ 完成！按前进键测试，按鼠标键停止。")