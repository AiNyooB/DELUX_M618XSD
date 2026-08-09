# -*- coding: utf-8 -*-
"""读取 M618XSD 鼠标的 Input Report（数据设备）"""
import ctypes
import os
import time
from ctypes import wintypes

HIDAPI = os.path.join(r"C:\Users\fresh\Downloads\618XSD\extracted\app", "hidapi.dll")
hidapi = ctypes.WinDLL(HIDAPI)
hidapi.hid_init()

# Data Device 路径 (UsagePage 0x0A)
path = r"\??\hid#vid_1d57&pid_fa60&mi_02&col03#8&317ab2dd&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}"

hidapi.hid_open_path.argtypes = [ctypes.c_wchar_p]
hidapi.hid_open_path.restype = ctypes.c_void_p

dev = hidapi.hid_open_path(path)
if not dev:
    print("Failed to open Data Device")
    hidapi.hid_exit()
    exit(1)

print(f"Data Device opened: {dev}")

hidapi.hid_set_nonblocking.argtypes = [ctypes.c_void_p, ctypes.c_int]
hidapi.hid_set_nonblocking(dev, 0)  # blocking

buf = ctypes.create_string_buffer(64)
hidapi.hid_read.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
hidapi.hid_read.restype = ctypes.c_int

print("Reading Input Reports...")
print("(Move mouse, click buttons, press DPI key)")

for i in range(100):
    ctypes.memset(buf, 0, 64)
    r = hidapi.hid_read(dev, buf, 64)
    if r > 0:
        data = bytes(buf.raw[:r])
        print(f"Got {r}B: {data.hex(' ')}")
        # Check for battery-like values
        for j in range(r):
            if 0 < data[j] <= 100:
                print(f"  byte[{j}] = {data[j]} (possible battery)")
    time.sleep(0.1)

hidapi.hid_close(dev)
hidapi.hid_exit()
print("Done")