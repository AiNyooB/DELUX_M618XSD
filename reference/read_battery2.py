# -*- coding: utf-8 -*-
"""尝试通过 hiddriver DLL 的 Hid_Read 读取 Input Report 获取电池数据"""
import ctypes
import os
import time
import threading
from ctypes import wintypes

DLL_PATH = os.path.join(r"C:\Users\fresh\Downloads\618XSD\extracted\app", "hiddriver_ms_4.dll")
VID, PID = 0x1D57, 0xFA60

dll = ctypes.WinDLL(DLL_PATH)

# 设置函数签名
dll.Set_VIDPID.argtypes = [ctypes.c_uint, ctypes.c_uint]
dll.Set_VIDPID.restype = ctypes.c_int
dll.Open_FeatureDevice.restype = ctypes.c_int
dll.SetFeature.argtypes = [ctypes.c_char_p, ctypes.c_uint]
dll.SetFeature.restype = ctypes.c_int
dll.Close_FeatureDevice.restype = ctypes.c_int

# 先打开 Feature Device 发送唤醒
dll.Set_VIDPID(VID, PID)
if not dll.Open_FeatureDevice():
    print("❌ Open_FeatureDevice 失败")
    exit(1)

dll.SetFeature(bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0, 0, 0, 0]), 10)
time.sleep(0.5)
dll.Close_FeatureDevice()

# 现在尝试用 Open_DevMonitor 打开 Data Device
# 需要创建一个隐藏窗口来接收消息
# 先用线程方式

# 尝试使用 hidapi 的 hid_open 打开设备，然后用 hid_read
# 但之前尝试过，hid_open 打开的是第一个设备（鼠标），不是 Data Device
# 不过我们可以枚举所有设备，找到正确的那个

# 换个思路：使用 SetupAPI 直接打开 Data Device
import uuid

# 定义 HID GUID
class GUID(ctypes.Structure):
    _fields_ = [
        ("Data1", ctypes.c_ulong),
        ("Data2", ctypes.c_ushort),
        ("Data3", ctypes.c_ushort),
        ("Data4", ctypes.c_ubyte * 8),
    ]

setupapi = ctypes.windll.setupapi
kernel32 = ctypes.windll.kernel32

hid_guid = GUID()
hid_guid.Data1 = 0x4d1e55b2
hid_guid.Data2 = 0xf16f
hid_guid.Data3 = 0x11cf
hid_guid.Data4 = (ctypes.c_ubyte * 8)(0x88, 0xcb, 0x00, 0x11, 0x11, 0x00, 0x00, 0x30)

# 获取设备信息集
hDevInfo = setupapi.SetupDiGetClassDevsW(
    ctypes.byref(hid_guid),
    None, None,
    0x10 | 0x2  # DIGCF_PRESENT | DIGCF_DEVICEINTERFACE
)

if hDevInfo == ctypes.c_void_p(-1).value or hDevInfo is None:
    print("❌ SetupDiGetClassDevsW 失败")
    exit(1)

print(f"设备信息集: {hDevInfo}")

# 枚举设备接口
dev_info_data = (ctypes.c_byte * 0x1C)()  # SP_DEVINFO_DATA
ctypes.memset(dev_info_data, 0, len(dev_info_data))
ctypes.memmove(dev_info_data, b"\x1c\x00\x00\x00", 4)  # cbSize

for i in range(20):
    if not setupapi.SetupDiEnumDeviceInfo(hDevInfo, i, ctypes.byref(dev_info_data)):
        break
    
    # 获取设备实例 ID
    buf = ctypes.create_unicode_buffer(256)
    bufsize = wintypes.DWORD(256)
    if setupapi.SetupDiGetDeviceInstanceIdW(hDevInfo, ctypes.byref(dev_info_data), buf, 256, ctypes.byref(bufsize)):
        inst_id = buf.value
        if "1D57" in inst_id.upper():
            print(f"  设备: {inst_id}")

    # 获取设备接口详情
    detail_size = 0x220  # SP_DEVICE_INTERFACE_DETAIL_DATA_W
    detail = (ctypes.c_byte * detail_size)()
    ctypes.memset(detail, 0, detail_size)
    ctypes.memmove(detail, ctypes.c_ushort(detail_size), 2)  # cbSize
    
    iface_data = (ctypes.c_byte * 0x20)()  # SP_DEVICE_INTERFACE_DATA
    ctypes.memset(iface_data, 0, len(iface_data))
    ctypes.memmove(iface_data, b"\x20\x00\x00\x00", 4)  # cbSize
    
    if setupapi.SetupDiEnumDeviceInterfaces(hDevInfo, None, ctypes.byref(hid_guid), i, ctypes.byref(iface_data)):
        required = wintypes.DWORD(0)
        setupapi.SetupDiGetDeviceInterfaceDetailW(hDevInfo, ctypes.byref(iface_data), None, 0, ctypes.byref(required), None)
        if required.value > 0 and required.value <= detail_size:
            ctypes.memset(detail, 0, detail_size)
            ctypes.memmove(detail, ctypes.c_ushort(detail_size), 2)
            if setupapi.SetupDiGetDeviceInterfaceDetailW(hDevInfo, ctypes.byref(iface_data), ctypes.byref(detail), detail_size, None, ctypes.byref(dev_info_data)):
                # 路径在 detail 结构的偏移 4 处（宽字符串）
                path_ptr = ctypes.cast(ctypes.pointer(detail, 4), ctypes.c_wchar_p)
                path = path_ptr.value
                if path and "1D57" in path.upper():
                    print(f"  路径: {path}")

setupapi.SetupDiDestroyDeviceInfoList(hDevInfo)
print("Done")