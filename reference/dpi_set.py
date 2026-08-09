# -*- coding: utf-8 -*-
"""设置 DPI（指定 byte 值）并读回报告 0x04 观察状态变化。
用法: python dpi_set.py <byte>   # 候选 8、4、2、1、3、5、10 ...
"""
import ctypes
import sys

APP = r"C:\Users\fresh\Downloads\618XSD\extracted\app"
DLL_PATH = APP + r"\hiddriver_ms_4.dll"
VID, PID = 0x1D57, 0xFA60

dll = ctypes.WinDLL(DLL_PATH)
dll.Set_VIDPID.argtypes = [ctypes.c_uint, ctypes.c_uint]
dll.Set_VIDPID.restype = ctypes.c_int
dll.Open_FeatureDevice.restype = ctypes.c_int
dll.SetFeature.argtypes = [ctypes.c_char_p, ctypes.c_uint]
dll.SetFeature.restype = ctypes.c_int
dll.GetFeature.argtypes = [ctypes.c_char_p, ctypes.c_uint]
dll.GetFeature.restype = ctypes.c_int
dll.Close_FeatureDevice.restype = ctypes.c_int

try:
    b = int(sys.argv[1]) & 0xFF
except (IndexError, ValueError):
    b = 8

dll.Set_VIDPID(VID, PID)
if not dll.Open_FeatureDevice():
    print("Open_FeatureDevice 失败")
    raise SystemExit(1)

init = bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0, 0, 0, 0])
print("0C init       ->", dll.SetFeature(init, len(init)))

cmd = bytes([0x06, 0x09, 0x01, b, (~b) & 0xFF, 0, 0, 0, 0])
print("06 09 byte=%02X -> %d" % (b, dll.SetFeature(cmd, len(cmd))))

rbuf = ctypes.create_string_buffer(bytes([0x04]) + b"\x00" * 0x3F, 0x40)
if dll.GetFeature(rbuf, 0x40):
    raw = rbuf.raw
    print("read 0x04:   " + raw.hex(" "))
    print("  前9字节(命令回显): " + raw[:9].hex(" "))
    print("  [0x0A..0x0E]: " + raw[0x0A:0x0F].hex(" "))
    print("  [0x10..0x14]: " + raw[0x10:0x15].hex(" "))
    print("  [0x18] 当前索引字段: %02X" % raw[0x18])
else:
    print("read 0x04 失败")

dll.Close_FeatureDevice()
