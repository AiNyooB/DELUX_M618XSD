#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
监听 DELUX M618XSD 的 Input Report，验证 DPI 切档上报格式。

设备：VID=0x1D57 PID=0xFA60，数据接口 UsagePage=0x0A / Usage=0。
目标：确认切 DPI 键时收到 Input Report ID=3，且 buf[3]=当前档位索引(1-5)。

用法（必须 32 位 Python，否则无法加载 32 位 hid.dll 调用）：
  C:\tmp\re32\py\python.exe dpi_input_listen.py

按 Ctrl+C 退出。先确保官方 Mouse.exe 已退出。
"""
import ctypes
from ctypes import wintypes
import time

VID = 0x1D57
PID = 0xFA60
DATA_USAGE_PAGE = 0x0A  # 数据设备（身份/输入报告）

# --- Windows API 常量 ---
GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
FILE_SHARE_READ = 0x00000001
FILE_SHARE_WRITE = 0x00000002
OPEN_EXISTING = 3
INVALID_HANDLE_VALUE = -1 & 0xFFFFFFFF
DIGCF_PRESENT = 0x00000002
DIGCF_DEVICEINTERFACE = 0x00000010

kernel32 = ctypes.windll.kernel32
hid = ctypes.windll.hid
setupapi = ctypes.windll.setupapi

class GUID(ctypes.Structure):
    _fields_ = [
        ("Data1", ctypes.c_ulong),
        ("Data2", ctypes.c_ushort),
        ("Data3", ctypes.c_ushort),
        ("Data4", ctypes.c_ubyte * 8),
    ]

class SP_DEVICE_INTERFACE_DATA(ctypes.Structure):
    _fields_ = [
        ("cbSize", ctypes.c_ulong),
        ("InterfaceClassGuid", GUID),
        ("Flags", ctypes.c_ulong),
        ("Reserved", ctypes.POINTER(ctypes.c_ulong)),
    ]

class SP_DEVICE_INTERFACE_DETAIL_DATA(ctypes.Structure):
    _fields_ = [
        ("cbSize", ctypes.c_ulong),
        ("DevicePath", ctypes.c_wchar * 256),
    ]

# 设置关键 API 的参数/返回类型，避免 ctypes 默认 int 转型出错
kernel32.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                                 ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
kernel32.CreateFileW.restype = wintypes.HANDLE
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.CloseHandle.restype = wintypes.BOOL
kernel32.ReadFile.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD,
                             ctypes.POINTER(wintypes.DWORD), ctypes.c_void_p]
kernel32.ReadFile.restype = wintypes.BOOL
kernel32.GetLastError.argtypes = []
kernel32.GetLastError.restype = wintypes.DWORD

hid.HidD_GetHidGuid.argtypes = [ctypes.POINTER(GUID)]
hid.HidD_GetHidGuid.restype = None
hid.HidD_GetAttributes.argtypes = [wintypes.HANDLE, ctypes.c_void_p]
hid.HidD_GetAttributes.restype = wintypes.BOOL
hid.HidD_GetPreparsedData.argtypes = [wintypes.HANDLE, ctypes.POINTER(ctypes.c_void_p)]
hid.HidD_GetPreparsedData.restype = wintypes.BOOL
hid.HidD_FreePreparsedData.argtypes = [ctypes.c_void_p]
hid.HidD_FreePreparsedData.restype = wintypes.BOOL
hid.HidP_GetCaps.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
hid.HidP_GetCaps.restype = ctypes.c_ulong

setupapi.SetupDiGetClassDevsW.argtypes = [ctypes.POINTER(GUID), ctypes.c_void_p,
                                          ctypes.c_void_p, wintypes.DWORD]
setupapi.SetupDiGetClassDevsW.restype = wintypes.HANDLE
setupapi.SetupDiEnumDeviceInterfaces.argtypes = [wintypes.HANDLE, ctypes.c_void_p,
                                                ctypes.POINTER(GUID), wintypes.DWORD,
                                                ctypes.POINTER(SP_DEVICE_INTERFACE_DATA)]
setupapi.SetupDiEnumDeviceInterfaces.restype = wintypes.BOOL
setupapi.SetupDiGetDeviceInterfaceDetailW.argtypes = [wintypes.HANDLE,
                                                     ctypes.POINTER(SP_DEVICE_INTERFACE_DATA),
                                                     ctypes.c_void_p, wintypes.DWORD,
                                                     ctypes.POINTER(wintypes.DWORD),
                                                     ctypes.c_void_p]
setupapi.SetupDiGetDeviceInterfaceDetailW.restype = wintypes.BOOL
setupapi.SetupDiDestroyDeviceInfoList.argtypes = [wintypes.HANDLE]
setupapi.SetupDiDestroyDeviceInfoList.restype = wintypes.BOOL

def get_hid_guid():
    guid = GUID()
    hid.HidD_GetHidGuid(ctypes.byref(guid))
    return guid

def enumerate_paths():
    """枚举匹配 VID/PID 的所有集合路径，返回 [(path, usage_page, usage, input_len)]"""
    guid = get_hid_guid()
    dev_info = setupapi.SetupDiGetClassDevsW(
        ctypes.byref(guid), None, None, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)
    if dev_info == INVALID_HANDLE_VALUE:
        return []
    results = []
    idx = 0
    did = SP_DEVICE_INTERFACE_DATA()
    did.cbSize = ctypes.sizeof(SP_DEVICE_INTERFACE_DATA)
    while setupapi.SetupDiEnumDeviceInterfaces(
            dev_info, None, ctypes.byref(guid), idx, ctypes.byref(did)):
        idx += 1
        # 先取 detail 大小
        needed = ctypes.c_ulong(0)
        setupapi.SetupDiGetDeviceInterfaceDetailW(
            dev_info, ctypes.byref(did), None, 0, ctypes.byref(needed), None)
        detail = SP_DEVICE_INTERFACE_DETAIL_DATA()
        detail.cbSize = ctypes.sizeof(ctypes.c_ulong) + 2  # 32/64 兼容最小大小
        if not setupapi.SetupDiGetDeviceInterfaceDetailW(
                dev_info, ctypes.byref(did), ctypes.byref(detail),
                ctypes.sizeof(detail), None, None):
            continue
        path = detail.DevicePath
        # 用路径字符串里的 vid_xxxx&pid_xxxx 过滤（绕开 HIDD_ATTRIBUTES 对齐坑）
        import re
        m = re.search(r'vid_([0-9a-fA-F]{4})&pid_([0-9a-fA-F]{4})', path)
        if not m:
            continue
        vid = int(m.group(1), 16)
        pid = int(m.group(2), 16)
        if vid != VID or pid != PID:
            continue
        # 用 access=0 打开以读属性
        h = kernel32.CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                 None, OPEN_EXISTING, 0, None)
        if h == INVALID_HANDLE_VALUE:
            print(f"    [dbg] CreateFile 失败 path={path[-20:]}")
            continue
        try:
            # preparsed data -> caps
            pp = ctypes.c_void_p()
            if not hid.HidD_GetPreparsedData(h, ctypes.byref(pp)):
                print(f"    [dbg] GetPreparsedData 失败 path={path[-20:]}")
                continue
            try:
                class HIDP_CAPS(ctypes.Structure):
                    _fields_ = [
                        ("Usage", ctypes.c_ushort),
                        ("UsagePage", ctypes.c_ushort),
                        ("InputReportByteLength", ctypes.c_ushort),
                        ("OutputReportByteLength", ctypes.c_ushort),
                        ("FeatureReportByteLength", ctypes.c_ushort),
                        ("Reserved", ctypes.c_ushort * 17),
                        ("NumberLinkCollectionNodes", ctypes.c_ushort),
                        ("NumberInputButtonCaps", ctypes.c_ushort),
                        ("NumberInputValueCaps", ctypes.c_ushort),
                        ("NumberInputDataIndices", ctypes.c_ushort),
                        ("NumberOutputButtonCaps", ctypes.c_ushort),
                        ("NumberOutputValueCaps", ctypes.c_ushort),
                        ("NumberOutputDataIndices", ctypes.c_ushort),
                        ("NumberFeatureButtonCaps", ctypes.c_ushort),
                        ("NumberFeatureValueCaps", ctypes.c_ushort),
                        ("NumberFeatureDataIndices", ctypes.c_ushort),
                    ]
                caps = HIDP_CAPS()
                st = hid.HidP_GetCaps(pp, ctypes.byref(caps))
                if st == 0:
                    print(f"    [dbg] 集合 path={path[-18:]} UP=0x{caps.UsagePage:02X} "
                          f"U=0x{caps.Usage:02X} InLen={caps.InputReportByteLength}")
                    results.append((path, caps.UsagePage, caps.Usage,
                                    caps.InputReportByteLength))
                else:
                    print(f"    [dbg] GetCaps 失败 st={st} path={path[-18:]}")
            finally:
                hid.HidD_FreePreparsedData(pp)
        finally:
            kernel32.CloseHandle(h)
    setupapi.SetupDiDestroyDeviceInfoList(dev_info)
    return results

def read_loop(path, input_len):
    # 用 GENERIC_READ 打开数据接口（鼠标类不允许 RW 独占，但可读）
    h = kernel32.CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                             None, OPEN_EXISTING, 0, None)
    if h == INVALID_HANDLE_VALUE:
        err = kernel32.GetLastError()
        print(f"[!] 打开数据接口失败，Win32 错误 {err}")
        return
    print(f"[+] 已打开数据接口，开始监听（Input 报告长度={input_len}）...")
    print("    按 DPI 循环键观察档位字节，Ctrl+C 退出\n")
    import sys
    deadline = time.time() + (int(sys.argv[1]) if len(sys.argv) > 1 else 15)
    buf = ctypes.create_string_buffer(input_len + 1)  # +1 给 Report ID 前缀
    last = None
    try:
        while True:
            n = kernel32.ReadFile(h, buf, input_len + 1, None, None)
            # ReadFile 同步返回，但 HID 是重叠的；这里用简单轮询
            # 实际 HID 输入用 ReadFile 阻塞，需 OVERLAPPED，简化用非阻塞尝试
            got = bytes(buf[:input_len + 1])
            if got != last:
                last = got
                rid = got[0]
                body = got[1:]
                if rid == 0x03:
                    lvl = body[2] if len(body) > 2 else '?'
                    print(f"    ReportID=0x03 len={len(body)} {body.hex(' ')}  -> 档位={lvl}")
                else:
                    print(f"    ReportID={rid:#04x} len={len(body)} {body.hex(' ')}")
            time.sleep(0.02)
            if time.time() > deadline:
                print("\n[+] 超时退出")
                break
    except KeyboardInterrupt:
        print("\n[+] 退出")
    finally:
        kernel32.CloseHandle(h)

def main():
    print(f"枚举 VID={VID:#06x} PID={PID:#06x} 的 HID 集合...")
    cols = enumerate_paths()
    if not cols:
        print("[!] 未找到设备，确认接收器已插入且官方 Mouse.exe 已退出")
        return
    print(f"找到 {len(cols)} 个集合：")
    data = None
    for i, (path, up, usage, ilen) in enumerate(cols):
        tag = ""
        if up == DATA_USAGE_PAGE:
            tag = "  <== 数据接口(Input Report)"
            data = (path, ilen)
        print(f"  [{i}] UsagePage=0x{up:02X} Usage=0x{usage:02X} "
              f"InputLen={ilen}{tag}")
    if data is None:
        print("[!] 未找到 UsagePage=0x0A 的数据接口")
        return
    read_loop(data[0], data[1])

if __name__ == "__main__":
    main()
