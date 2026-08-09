# -*- coding: utf-8 -*-
"""读取电池：用 SetupAPI 打开 Data Device 读 Input Report"""
import ctypes
import os
import time
from ctypes import wintypes

class GUID(ctypes.Structure):
    _fields_ = [("Data1", ctypes.c_ulong), ("Data2", ctypes.c_ushort),
                ("Data3", ctypes.c_ushort), ("Data4", ctypes.c_ubyte * 8)]

setupapi = ctypes.windll.setupapi
kernel32 = ctypes.windll.kernel32

# HID GUID
hid_guid = GUID(0x4d1e55b2, 0xf16f, 0x11cf, (ctypes.c_ubyte * 8)(0x88, 0xcb, 0x00, 0x11, 0x11, 0x00, 0x00, 0x30))

hDevInfo = setupapi.SetupDiGetClassDevsW(ctypes.byref(hid_guid), None, None, 0x12)
if not hDevInfo or hDevInfo == ctypes.c_void_p(-1).value:
    print("SetupDiGetClassDevsW failed"); exit(1)

# 枚举所有 HID 设备接口
dev_info = (ctypes.c_byte * 0x1C)()
ctypes.memset(dev_info, 0, 28)
ctypes.memmove(dev_info, b'\x1c\x00\x00\x00', 4)

data_device_path = None
for i in range(30):
    iface_data = (ctypes.c_byte * 0x20)()
    ctypes.memset(iface_data, 0, 32)
    ctypes.memmove(iface_data, b'\x20\x00\x00\x00', 4)
    
    if not setupapi.SetupDiEnumDeviceInterfaces(hDevInfo, None, ctypes.byref(hid_guid), i, ctypes.byref(iface_data)):
        break
    
    required = wintypes.DWORD(0)
    setupapi.SetupDiGetDeviceInterfaceDetailW(hDevInfo, ctypes.byref(iface_data), None, 0, ctypes.byref(required), None)
    
    if required.value > 0 and required.value <= 1024:
        detail = (ctypes.c_byte * required.value)()
        ctypes.memset(detail, 0, required.value)
        ctypes.memmove(detail, ctypes.c_ushort(required.value), 2)
        
        if setupapi.SetupDiGetDeviceInterfaceDetailW(hDevInfo, ctypes.byref(iface_data), ctypes.byref(detail), required.value, None, ctypes.byref(dev_info)):
            path = ctypes.c_wchar_p.from_buffer(detail, 4).value
            if path and "1D57" in path.upper():
                # 判断是否 Data Device (col03)
                if "col03" in path:
                    data_device_path = path
                    print(f"Data Device: {path}")
                else:
                    print(f"  Other: {path}")

setupapi.SetupDiDestroyDeviceInfoList(hDevInfo)

if not data_device_path:
    print("No Data Device found")
    exit(1)

# 尝试打开
handle = kernel32.CreateFileW(
    data_device_path,
    0x80000000 | 0x40000000,  # GENERIC_READ | GENERIC_WRITE
    3,  # FILE_SHARE_READ | FILE_SHARE_WRITE
    None,
    3,  # OPEN_EXISTING
    0,  # No overlapped
    None
)

if handle == ctypes.c_void_p(-1).value:
    err = ctypes.GetLastError()
    print(f"CreateFileW failed: {err}")
    if err == 32:  # ERROR_SHARING_VIOLATION
        print("  Sharing violation - hiddriver DLL might have it open")
    exit(1)

print(f"Opened: {handle}")

# 读取 Input Report
buf = ctypes.create_string_buffer(64)
bytes_read = wintypes.DWORD(0)

print("Reading Input Report (move mouse)...")
for _ in range(5):
    ctypes.memset(buf, 0, 64)
    r = kernel32.ReadFile(handle, buf, 64, ctypes.byref(bytes_read), None)
    if r:
        data = bytes(buf.raw[:bytes_read.value])
        print(f"Read {bytes_read.value}B: {data.hex(' ')}")
        # 检查是否有 0-100 的值
        for j in range(1, bytes_read.value):
            if 0 < data[j] <= 100:
                print(f"  byte[{j}] = {data[j]} (battery?)")
        break
    else:
        err = ctypes.GetLastError()
        print(f"ReadFile failed: {err}")
        if err == 6:  # ERROR_INVALID_HANDLE
            break
    time.sleep(0.5)

kernel32.CloseHandle(handle)
print("Done")