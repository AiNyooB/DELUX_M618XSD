# -*- coding: utf-8 -*-
"""读取 M618XSD 电池电量 - 直接打开 Data Device 读 Input Report"""
import ctypes
import os
import time
from ctypes import wintypes

HIDAPI = os.path.join(r"C:\Users\fresh\Downloads\618XSD\extracted\app", "hidapi.dll")
hidapi = ctypes.WinDLL(HIDAPI)
hidapi.hid_init()

# 枚举找到 Data Device (UsagePage 0x0A)
hidapi.hid_enumerate.argtypes = [wintypes.USHORT, wintypes.USHORT]
hidapi.hid_enumerate.restype = ctypes.c_void_p

enum = hidapi.hid_enumerate(0x1D57, 0xFA60)
p = enum
data_path = None
while p:
    path_ptr = ctypes.c_void_p.from_address(p)
    path = ctypes.c_char_p(path_ptr.value)
    usage_page = ctypes.c_ushort.from_address(p + 24).value
    if path.value and usage_page == 0x0A:
        data_path = path.value.decode("utf-8", errors="replace")
        print(f"Data Device: {data_path}")
        break
    next_ptr = ctypes.c_void_p.from_address(p + 32)
    p = next_ptr.value

if not data_path:
    print("No Data Device found")
    # Try to open the first device with hid_open
    print("Falling back to hid_open...")
    hidapi.hid_open.argtypes = [wintypes.USHORT, wintypes.USHORT, ctypes.c_wchar_p]
    hidapi.hid_open.restype = ctypes.c_void_p
    dev = hidapi.hid_open(0x1D57, 0xFA60, None)
    if not dev:
        print("Still failed")
        hidapi.hid_exit()
        exit(1)
    print(f"Opened: {dev}")
else:
    # Try hid_open_path
    hidapi.hid_open_path.argtypes = [ctypes.c_wchar_p]
    hidapi.hid_open_path.restype = ctypes.c_void_p
    dev = hidapi.hid_open_path(data_path)
    if not dev:
        print("hid_open_path failed, trying hid_open...")
        hidapi.hid_open.argtypes = [wintypes.USHORT, wintypes.USHORT, ctypes.c_wchar_p]
        hidapi.hid_open.restype = ctypes.c_void_p
        dev = hidapi.hid_open(0x1D57, 0xFA60, None)
        if not dev:
            print("Still failed")
            hidapi.hid_exit()
            exit(1)
    print(f"Opened: {dev}")

# Set non-blocking
hidapi.hid_set_nonblocking.argtypes = [ctypes.c_void_p, ctypes.c_int]
hidapi.hid_set_nonblocking(dev, 0)  # blocking

buf = ctypes.create_string_buffer(64)
hidapi.hid_read.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
hidapi.hid_read.restype = ctypes.c_int

print("Move the mouse and click buttons...")
for i in range(100):
    ctypes.memset(buf, 0, 64)
    r = hidapi.hid_read(dev, buf, 64)
    if r > 0:
        data = bytes(buf.raw[:r])
        print(f"Got {r}B: {data.hex(' ')}")
        for j in range(1, r):
            if 0 < data[j] <= 100:
                print(f"  byte[{j}] = {data[j]} (possible battery)")
    time.sleep(0.1)

hidapi.hid_close(dev)
hidapi.hid_exit()
print("Done")