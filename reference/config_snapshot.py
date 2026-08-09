# -*- coding: utf-8 -*-
"""只读快照：读取设备所有 feature report 页保存到文件，用于前后差分定位字段。
用法: python config_snapshot.py <标签>   # 例: before / after
"""
import ctypes
import os
import sys
import time

APP = r"C:\Users\fresh\Downloads\618XSD\extracted\app"
DLL_PATH = APP + r"\hiddriver_ms_4.dll"
OUTDIR = r"C:\Users\fresh\Downloads\618XSD\snapshots"
VID, PID = 0x1D57, 0xFA60

dll = ctypes.WinDLL(DLL_PATH)
dll.Set_VIDPID.argtypes = [ctypes.c_uint, ctypes.c_uint]
dll.Set_VIDPID.restype = ctypes.c_int
dll.Open_FeatureDevice.restype = ctypes.c_int
dll.GetFeature.argtypes = [ctypes.c_char_p, ctypes.c_uint]
dll.GetFeature.restype = ctypes.c_int
dll.Close_FeatureDevice.restype = ctypes.c_int

label = sys.argv[1] if len(sys.argv) > 1 else "snap"
os.makedirs(OUTDIR, exist_ok=True)

dll.Set_VIDPID(VID, PID)
if not dll.Open_FeatureDevice():
    print("Open_FeatureDevice 失败")
    raise SystemExit(1)

lines = ["# M618XSD config snapshot: %s  @ %s" % (label, time.strftime("%Y-%m-%d %H:%M:%S"))]
for rid in range(0x01, 0x21):
    buf = ctypes.create_string_buffer(bytes([rid]) + b"\x00" * 0x3F, 0x40)
    r = dll.GetFeature(buf, 0x40)
    lines.append("R%02X %d %s" % (rid, r, buf.raw.hex(" ")))

dll.Close_FeatureDevice()

out = os.path.join(OUTDIR, "config_%s.txt" % label)
open(out, "w", encoding="utf-8").write("\n".join(lines) + "\n")
print("快照已保存:", out)
