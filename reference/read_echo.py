# -*- coding: utf-8 -*-
"""不唤醒直接读 page0：捕获官方软件最后发送的完整 0x04 报告（前 10 字节回显 + 配置块）。
用法：官方软件点完「应用」后立刻运行。python read_echo.py [标签]"""
import ctypes
import sys

APP = r"C:\Users\fresh\Downloads\618XSD\extracted\app"
DLL_PATH = APP + r"\hiddriver_ms_4.dll"
VID, PID = 0x1D57, 0xFA60

dll = ctypes.WinDLL(DLL_PATH)
dll.Set_VIDPID.argtypes = [ctypes.c_uint, ctypes.c_uint]
dll.Set_VIDPID.restype = ctypes.c_int
dll.Open_FeatureDevice.restype = ctypes.c_int
dll.GetFeature.argtypes = [ctypes.c_char_p, ctypes.c_uint]
dll.GetFeature.restype = ctypes.c_int
dll.Close_FeatureDevice.restype = ctypes.c_int

dll.Set_VIDPID(VID, PID)
if not dll.Open_FeatureDevice():
    print("Open_FeatureDevice 失败")
    raise SystemExit(1)

buf = ctypes.create_string_buffer(bytes([0x04]) + b"\x00" * 0x3F, 0x40)
r = dll.GetFeature(buf, 0x40)
dll.Close_FeatureDevice()

raw = buf.raw
print("read=%d  完整 page0:" % r)
print("  " + raw.hex(" "))
print("  [0..9]  最后命令回显: " + raw[:10].hex(" "))
print("  [10..]  配置块:       " + raw[10:].hex(" "))

if len(sys.argv) > 1:
    out = r"C:\Users\fresh\Downloads\618XSD\snapshots\echo_%s.txt" % sys.argv[1]
    open(out, "w", encoding="utf-8").write(raw.hex(" ") + "\n")
    print("已保存:", out)
