# -*- coding: utf-8 -*-
"""只读探测 M618XSD 特性接口：用 GetFeature 读各 Report ID，打印返回字节。
不写任何东西，用于诊断当前鼠标状态（如 DPI 显示 0 的问题）。"""
import ctypes

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
    print("Open_FeatureDevice 失败，无法读取。")
    raise SystemExit(1)
print("特性接口已打开，开始只读探测（Report ID 0x01..0x14，长度 0x40/0x41/0x83）\n")

for rid in range(0x01, 0x15):
    for ln in (0x40, 0x41, 0x83):
        buf = ctypes.create_string_buffer(bytes([rid]) + b"\x00" * 0x82, 0x83)
        r = dll.GetFeature(buf, ln)
        if r:
            raw = buf.raw[:ln]
            print("Report %02X  len=%03X -> %s" % (rid, ln, raw.hex(" ")))
            break

dll.Close_FeatureDevice()
print("\n探测完成（只读，未写入任何内容）。")
