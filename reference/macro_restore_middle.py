#!/usr/bin/env python3
"""恢复中键为默认功能（中键点击），不下发任何宏数据"""
import ctypes, os, time

APP = r"C:\Users\fresh\Downloads\618XSD\extracted\app"
DLL_PATH = os.path.join(APP, "hiddriver_ms_4.dll")
VID, PID = 0x1D57, 0xFA60

# 默认 entry 表，entry[4] = 中键
entries = bytearray(54)
default = [
    (0x02,0,0),(0x03,0,0),(0x06,0,0),(0x12,0,4),(0x04,0,0),(0x0D,0,0),
    (0x01,0,0),(0x01,0,0),(0x01,0,0),(0x01,0,0),(0x01,0,0),(0x01,0,0),
    (0x01,0,0),(0x01,0,0),(0x0B,0,0),(0x0C,0,0),(0x09,0,0),(0x0A,0,0),
]
for i,(f,p1,p2) in enumerate(default):
    entries[i*3]=f; entries[i*3+1]=p1; entries[i*3+2]=p2

r08 = bytearray(59)
r08[0]=0x08; r08[1]=0x3B; r08[2]=0x01
r08[3:57]=entries
cs=sum(r08[3:57])&0xFFFF; r08[57]=(cs>>8)&0xFF; r08[58]=cs&0xFF

wake = bytes([0x0C,0x0A,0x01,0xFE,0x01,0xFE,0,0,0,0])

print("恢复中键为默认功能...")
print(f"0x08: {r08.hex(' ')}")

dll = ctypes.WinDLL(DLL_PATH)
dll.Set_VIDPID(ctypes.c_ushort(VID), ctypes.c_ushort(PID))
dll.Open_FeatureDevice()
for name, data in [("唤醒",wake),("按键映射",r08)]:
    r = dll.SetFeature(ctypes.c_char_p(bytes(data)), ctypes.c_int(len(data)))
    print(f"  {name} → {r}")
    time.sleep(0.2)
dll.Close_FeatureDevice()
print("✅ 完成，按中键测试。")