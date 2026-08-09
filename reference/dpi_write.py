# -*- coding: utf-8 -*-
"""用官方捕获的 0x04 报告模板构造并发送 DPI 配置（自定义驱动写路径验证）。
用法: python dpi_write.py <档位1..8> <DPI值>   # 例: dpi_write.py 1 2000
校验和 = sum(报告[3..49])（16 位大端，已实测锁定）。"""
import ctypes
import sys

APP = r"C:\Users\fresh\Downloads\618XSD\extracted\app"
DLL_PATH = APP + r"\hiddriver_ms_4.dll"
VID, PID = 0x1D57, 0xFA60

# 官方应用最后一次发送的完整报告（L1=2480=0x09B0），从 P00 捕获
TEMPLATE = bytes.fromhex(
    "04 38 01 00 00 1f 11 11 b0 b0 40 60 a0 00 00 00"
    " 09 04 06 09 0f 00 00 00 01"
    " ff 00 00 00 ff 00 00 00 ff ff 00 ff ff ff 00 ff 00 ff ff 40 00 ff ff ff"
    " 02 10 42 00 00 00 00"
)
assert len(TEMPLATE) == 56

try:
    level = int(sys.argv[1])
    dpi = int(sys.argv[2])
except (IndexError, ValueError):
    print("用法: python dpi_write.py <档位1..8> <DPI值>")
    raise SystemExit(2)
if not (1 <= level <= 8 and 40 <= dpi <= 4800):
    print("档位 1-8，DPI 40-4800（建议 40 的倍数）")
    raise SystemExit(2)

report = bytearray(TEMPLATE)
low_idx = 7 + level      # [8..15]
high_idx = 15 + level    # [16..23]
report[low_idx] = dpi & 0xFF
report[high_idx] = (dpi >> 8) & 0xFF
cksum = sum(report[3:50]) & 0xFFFF
report[50] = (cksum >> 8) & 0xFF
report[51] = cksum & 0xFF

print("构造报告（档位 %d = %d = 0x%04X）:" % (level, dpi, dpi))
print("  " + bytes(report).hex(" "))
print("  校验和: %04X" % cksum)

dll = ctypes.WinDLL(DLL_PATH)
dll.Set_VIDPID.argtypes = [ctypes.c_uint, ctypes.c_uint]
dll.Set_VIDPID.restype = ctypes.c_int
dll.Open_FeatureDevice.restype = ctypes.c_int
dll.SetFeature.argtypes = [ctypes.c_char_p, ctypes.c_uint]
dll.SetFeature.restype = ctypes.c_int
dll.GetFeature.argtypes = [ctypes.c_char_p, ctypes.c_uint]
dll.GetFeature.restype = ctypes.c_int
dll.Close_FeatureDevice.restype = ctypes.c_int

dll.Set_VIDPID(VID, PID)
if not dll.Open_FeatureDevice():
    print("Open_FeatureDevice 失败")
    raise SystemExit(1)

wake = bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0, 0, 0, 0])
dll.SetFeature(wake, len(wake))
import time
time.sleep(0.3)
r = dll.SetFeature(bytes(report), 56)
print("SetFeature(0x04 报告) ->", r)
time.sleep(0.3)

# 读回 P00 验证设备接受并存储了我们的报告
buf = ctypes.create_string_buffer(bytes([0x04]) + b"\x00" * 0x3F, 0x40)
dll.GetFeature(buf, 0x40)
dll.Close_FeatureDevice()
print("设备回读: " + buf.raw.hex(" "))
print("  [8..9]  档位1低字节: %02x %02x" % (buf.raw[8], buf.raw[9]))
print("  [16]    档位1高字节: %02x" % buf.raw[16])
