# -*- coding: utf-8 -*-
"""稳定读取配置页 0：重新打开设备（指针复位）→ 0x0C 唤醒 → 读一次 → 关闭。
每次运行得到一份干净的 page0 快照，用于前后差分。
用法: python read_page0.py [标签]"""
import ctypes
import sys
import time

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

dll.Set_VIDPID(VID, PID)
if not dll.Open_FeatureDevice():
    print("Open_FeatureDevice 失败（鼠标可能休眠/未连接）")
    raise SystemExit(1)

# 唤醒握手（官方流程第一步，无副作用）
wake = bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0, 0, 0, 0])
dll.SetFeature(wake, len(wake))
time.sleep(0.3)

# 读一次 page0（首次读取 = 第 0 页：前 10 字节为最后命令回显，其后为设备配置）
buf = ctypes.create_string_buffer(bytes([0x04]) + b"\x00" * 0x3F, 0x40)
r = dll.GetFeature(buf, 0x40)
dll.Close_FeatureDevice()

raw = buf.raw.hex(" ")
print("read=%d  page0: %s" % (r, raw))
print("  [0..9]  命令回显: " + buf.raw[:10].hex(" "))
print("  [10..]  设备配置: " + buf.raw[10:].hex(" "))

if len(sys.argv) > 1:
    out = r"C:\Users\fresh\Downloads\618XSD\snapshots\page0_%s.txt" % sys.argv[1]
    open(out, "w", encoding="utf-8").write(raw + "\n")
    print("已保存:", out)
