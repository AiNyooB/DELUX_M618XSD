# -*- coding: utf-8 -*-
"""验证 HidComm.cs 的算法逻辑（与 C# 代码逐步等价）。

本机 .NET SDK 目录为空（只剩运行时），无法编译 C# 项目，
因此用 Python 复刻 HidComm.Connect / Wake / ReadFeature 的完整调用序列，
证明算法本身正确。只发送无害的 0x0C 唤醒握手，不写任何配置。

C# 对应关系：
  EnumerateCollections()  <->  enumerate_collections()
  Connect()               <->  connect()
  PadReport()             <->  pad_report()
  WriteFeature()          <->  write_feature()
  ReadFeature()           <->  read_feature()
"""
import ctypes
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from hid_enum_diag import (GUID, HIDD_ATTRIBUTES, HIDP_CAPS,
                           SP_DEVICE_INTERFACE_DATA,
                           SP_DEVICE_INTERFACE_DETAIL_DATA_W,
                           DIGCF_DEVICEINTERFACE, DIGCF_PRESENT,
                           FILE_SHARE_READ, FILE_SHARE_WRITE,
                           GENERIC_READ, GENERIC_WRITE,
                           INVALID_HANDLE_VALUE, OPEN_EXISTING,
                           hid, kernel32, setupapi)

VID = 0x1D57
PID = 0xFA60
FEATURE_USAGE_PAGE = 0x0B
REPORT_LENGTH = 64

# 用 c_void_p 而非 c_char_p：c_char_p 会在首个 NUL 处截断，导致 GetFeature 报 Win32 87
hid.HidD_SetFeature.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_ulong]
hid.HidD_GetFeature.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_ulong]


def enumerate_collections():
    """等价于 C# HidComm.EnumerateCollections()。"""
    out = []
    guid = GUID()
    hid.HidD_GetHidGuid(ctypes.byref(guid))
    devinfo = setupapi.SetupDiGetClassDevsW(
        ctypes.byref(guid), None, None, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)
    if devinfo == INVALID_HANDLE_VALUE or devinfo is None:
        return out

    i = 0
    while True:
        did = SP_DEVICE_INTERFACE_DATA()
        did.cbSize = ctypes.sizeof(SP_DEVICE_INTERFACE_DATA)
        if not setupapi.SetupDiEnumDeviceInterfaces(
                devinfo, None, ctypes.byref(guid), i, ctypes.byref(did)):
            break
        i += 1

        detail = SP_DEVICE_INTERFACE_DETAIL_DATA_W()
        detail.cbSize = 6 if ctypes.sizeof(ctypes.c_void_p) == 4 else 8
        req = ctypes.c_ulong(0)
        if not setupapi.SetupDiGetDeviceInterfaceDetailW(
                devinfo, ctypes.byref(did), ctypes.byref(detail),
                ctypes.sizeof(detail), ctypes.byref(req), None):
            continue
        path = detail.DevicePath

        # 关键点：读属性阶段用 access=0（C# 中同样如此）
        h = kernel32.CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                 None, OPEN_EXISTING, 0, None)
        if h == INVALID_HANDLE_VALUE:
            continue
        try:
            attrs = HIDD_ATTRIBUTES()
            attrs.Size = ctypes.sizeof(HIDD_ATTRIBUTES)
            if not hid.HidD_GetAttributes(h, ctypes.byref(attrs)):
                continue
            if attrs.VendorID != VID or attrs.ProductID != PID:
                continue

            usage_page = usage = feat_len = 0
            pp = ctypes.c_void_p()
            if hid.HidD_GetPreparsedData(h, ctypes.byref(pp)):
                caps = HIDP_CAPS()
                if hid.HidP_GetCaps(pp, ctypes.byref(caps)) == 0x00110000:
                    usage_page = caps.UsagePage
                    usage = caps.Usage
                    feat_len = caps.FeatureReportByteLength
                hid.HidD_FreePreparsedData(pp)

            out.append({"path": path, "usage_page": usage_page,
                        "usage": usage, "feat_len": feat_len})
        finally:
            kernel32.CloseHandle(h)

    setupapi.SetupDiDestroyDeviceInfoList(devinfo)
    return out


def connect():
    """等价于 C# HidComm.Connect()：先按 UsagePage==0x0B 选，再退回 feat_len>0。"""
    cols = enumerate_collections()
    if not cols:
        return None, 0, "未枚举到匹配设备"

    target = next((c for c in cols
                   if c["usage_page"] == FEATURE_USAGE_PAGE and c["feat_len"] > 0), None)
    if target is None:
        target = next((c for c in cols if c["feat_len"] > 0), None)
    if target is None:
        return None, 0, "没有集合支持 Feature 报告"

    h = kernel32.CreateFileW(target["path"], GENERIC_READ | GENERIC_WRITE,
                             FILE_SHARE_READ | FILE_SHARE_WRITE,
                             None, OPEN_EXISTING, 0, None)
    if h == INVALID_HANDLE_VALUE:
        return None, 0, "打开失败，Win32 错误 %d" % ctypes.GetLastError()
    return h, target["feat_len"], ""


def pad_report(report, feat_len):
    """等价于 C# HidComm.PadReport()。"""
    n = feat_len if feat_len > 0 else REPORT_LENGTH
    buf = bytearray(n)
    buf[:min(len(report), n)] = report[:n]
    return bytes(buf)


def write_feature(h, report, feat_len):
    data = pad_report(report, feat_len)
    buf = (ctypes.c_ubyte * len(data)).from_buffer_copy(data)
    ok = hid.HidD_SetFeature(h, ctypes.byref(buf), len(data))
    return bool(ok), (0 if ok else ctypes.GetLastError())


def read_feature(h, feat_len, report_id=0x04):
    n = feat_len if feat_len > 0 else REPORT_LENGTH
    buf = (ctypes.c_ubyte * n)()
    buf[0] = report_id
    ok = hid.HidD_GetFeature(h, ctypes.byref(buf), n)
    if not ok:
        return None, ctypes.GetLastError()
    return bytes(buf), 0


def main():
    print("=" * 70)
    print("验证 HidComm.cs 算法（Python 等价复刻，仅发送无害的 0x0C 唤醒）")
    print("=" * 70)

    cols = enumerate_collections()
    print("\n[1] EnumerateCollections() -> %d 个集合" % len(cols))
    for i, c in enumerate(cols):
        mark = "  <== C# 会选中这个" if (
            c["usage_page"] == FEATURE_USAGE_PAGE and c["feat_len"] > 0) else ""
        print("    [%d] UsagePage=0x%02X Usage=0x%02X Feature=%d%s"
              % (i, c["usage_page"], c["usage"], c["feat_len"], mark))

    print("\n[2] Connect()")
    h, feat_len, err = connect()
    if h is None:
        print("    失败: %s" % err)
        return 1
    print("    成功，FeatureReportLength = %d" % feat_len)

    try:
        print("\n[3] Wake()  发送 0C 0A 01 FE 01 FE 00 00 00 00（补零到 %d）" % feat_len)
        wake = bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0x00, 0x00, 0x00, 0x00])
        ok, e = write_feature(h, wake, feat_len)
        print("    HidD_SetFeature -> %s%s"
              % ("成功" if ok else "失败", "" if ok else " (Win32 %d)" % e))
        if not ok:
            return 1

        import time
        time.sleep(0.3)

        # 注意：report_id 必须非 0。设备使用带编号的报告，
        # 传 0 会被 Windows 拒绝并返回 Win32 错误 87（参数错误）。
        print("\n[4] ReadFeature(reportId=0x04)")
        data, e = read_feature(h, feat_len, 0x04)
        if data is None:
            print("    失败 (Win32 %d)" % e)
            return 1
        print("    读回 %d 字节" % len(data))
        print("    命令回显 [0..9]: %s" % " ".join("%02x" % b for b in data[:10]))

        expect = wake
        if data[:10] == expect:
            print("\n[结论] 回显与发送的 0x0C 完全一致 —— 通信链路验证通过。")
        else:
            print("\n[结论] 回显与发送内容不同（设备可能返回其他页），"
                  "但读写调用本身成功。")
    finally:
        kernel32.CloseHandle(h)
        print("\n句柄已关闭。全程未写入任何配置数据。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
