# -*- coding: utf-8 -*-
"""验证 HidD_SetFeature 的 bufferLength 要求（只发无害的 0x0C 唤醒命令）。

对特性接口(UsagePage=0x0B)分别用 10 / 56 / 59 / 64 字节长度发送同一条 0x0C 唤醒握手，
观察哪些长度成功、哪些失败及 Win32 错误码，从而确认：
  - bufferLength 是否必须 == FeatureReportByteLength(64)
  - 官方 DLL 用原始长度(10)能成功，是否因为 Python ctypes 缓冲区实际更大

只发送 0x0C 唤醒命令（AGENTS.md 记录为官方流程第一步、无副作用），
不发送任何 DPI/按键/灯光配置报告。
用法: python hid_len_test.py
"""
import ctypes
import ctypes.wintypes as wt
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from hid_enum_diag import (GUID, SP_DEVICE_INTERFACE_DATA,
                           SP_DEVICE_INTERFACE_DETAIL_DATA_W, HIDD_ATTRIBUTES,
                           HIDP_CAPS, setupapi, hid, kernel32,
                           INVALID_HANDLE_VALUE, DIGCF_PRESENT,
                           DIGCF_DEVICEINTERFACE, GENERIC_READ, GENERIC_WRITE,
                           FILE_SHARE_READ, FILE_SHARE_WRITE, OPEN_EXISTING,
                           TARGET_VID, TARGET_PID)

hid.HidD_SetFeature.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_ulong]
hid.HidD_SetFeature.restype = ctypes.c_bool
hid.HidD_GetFeature.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_ulong]
hid.HidD_GetFeature.restype = ctypes.c_bool

WAKE = bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0, 0, 0, 0])


def find_feature_device():
    guid = GUID()
    hid.HidD_GetHidGuid(ctypes.byref(guid))
    hdev = setupapi.SetupDiGetClassDevsW(ctypes.byref(guid), None, None,
                                         DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)
    idx = 0
    found = None
    while True:
        did = SP_DEVICE_INTERFACE_DATA()
        did.cbSize = ctypes.sizeof(SP_DEVICE_INTERFACE_DATA)
        if not setupapi.SetupDiEnumDeviceInterfaces(hdev, None, ctypes.byref(guid),
                                                    idx, ctypes.byref(did)):
            break
        idx += 1
        detail = SP_DEVICE_INTERFACE_DETAIL_DATA_W()
        detail.cbSize = 6 if ctypes.sizeof(ctypes.c_void_p) == 4 else 8
        req = wt.DWORD(0)
        if not setupapi.SetupDiGetDeviceInterfaceDetailW(
                hdev, ctypes.byref(did), ctypes.byref(detail),
                ctypes.sizeof(detail), ctypes.byref(req), None):
            continue
        path = detail.DevicePath
        h = kernel32.CreateFileW(path, GENERIC_READ | GENERIC_WRITE,
                                 FILE_SHARE_READ | FILE_SHARE_WRITE,
                                 None, OPEN_EXISTING, 0, None)
        if h == INVALID_HANDLE_VALUE:
            continue
        attrs = HIDD_ATTRIBUTES()
        attrs.Size = ctypes.sizeof(HIDD_ATTRIBUTES)
        if not hid.HidD_GetAttributes(h, ctypes.byref(attrs)) or \
                attrs.VendorID != TARGET_VID or attrs.ProductID != TARGET_PID:
            kernel32.CloseHandle(h)
            continue
        pp = ctypes.c_void_p()
        caps = HIDP_CAPS()
        is_feat = False
        if hid.HidD_GetPreparsedData(h, ctypes.byref(pp)):
            if hid.HidP_GetCaps(pp, ctypes.byref(caps)) == 0x00110000:
                is_feat = (caps.UsagePage == 0x0B and caps.Usage == 0)
            hid.HidD_FreePreparsedData(pp)
        if is_feat:
            found = (h, path, caps.FeatureReportByteLength)
            break
        kernel32.CloseHandle(h)
    setupapi.SetupDiDestroyDeviceInfoList(hdev)
    return found


def main():
    r = find_feature_device()
    if not r:
        print("未找到 UsagePage=0x0B 的特性接口")
        return
    h, path, flen = r
    print("特性接口已打开")
    print("  路径: %s" % path)
    print("  FeatureReportByteLength = %d\n" % flen)

    print("测试不同 bufferLength 发送同一条 0x0C 唤醒命令：")
    print("(仅发送唤醒握手，不含任何配置数据)\n")

    for ln in (10, 56, 59, 64):
        buf = ctypes.create_string_buffer(bytes(WAKE) + b"\x00" * (ln - len(WAKE)), ln)
        ctypes.set_last_error(0)
        ok = hid.HidD_SetFeature(h, buf, ln)
        err = ctypes.get_last_error()
        print("  len=%2d -> %s%s" % (ln, "成功" if ok else "失败",
                                     "" if ok else "  (Win32 错误 %d)" % err))
        time.sleep(0.2)

    print("\n测试 GetFeature 不同长度：")
    for ln in (56, 64):
        buf = ctypes.create_string_buffer(bytes([0x04]) + b"\x00" * (ln - 1), ln)
        ctypes.set_last_error(0)
        ok = hid.HidD_GetFeature(h, buf, ln)
        err = ctypes.get_last_error()
        print("  len=%2d -> %s%s" % (ln, "成功" if ok else "失败",
                                     "" if ok else "  (Win32 错误 %d)" % err))
        if ok:
            print("        %s" % buf.raw[:32].hex(" "))
        time.sleep(0.2)

    kernel32.CloseHandle(h)
    print("\n测试完成（只发送了唤醒握手，未写入任何配置）。")


if __name__ == "__main__":
    main()
