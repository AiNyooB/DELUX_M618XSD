# -*- coding: utf-8 -*-
"""只读 HID 枚举诊断（零写入、零风险）。

复刻 Windows HID 枚举流程，列出本机所有 HID 顶层集合（Top-Level Collection），
重点标出 VID=0x1D57 PID=0xFA60 的集合，并对每个集合报告：
  - DevicePath
  - UsagePage / Usage
  - FeatureReportByteLength / InputReportByteLength / OutputReportByteLength
  - CreateFile 以 GENERIC_READ|GENERIC_WRITE 打开是否成功（失败给出 Win32 错误码）
  - CreateFile 以 0（无访问权限）打开是否成功

不发送任何 Feature Report，不修改设备任何配置。
用法: python hid_enum_diag.py
"""
import ctypes
import ctypes.wintypes as wt

TARGET_VID = 0x1D57
TARGET_PID = 0xFA60

setupapi = ctypes.WinDLL("setupapi")
hid = ctypes.WinDLL("hid")
kernel32 = ctypes.WinDLL("kernel32")

INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value
DIGCF_PRESENT = 0x02
DIGCF_DEVICEINTERFACE = 0x10
GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
FILE_SHARE_READ = 0x01
FILE_SHARE_WRITE = 0x02
OPEN_EXISTING = 3


class GUID(ctypes.Structure):
    _fields_ = [("Data1", ctypes.c_ulong),
                ("Data2", ctypes.c_ushort),
                ("Data3", ctypes.c_ushort),
                ("Data4", ctypes.c_ubyte * 8)]


class SP_DEVICE_INTERFACE_DATA(ctypes.Structure):
    _fields_ = [("cbSize", wt.DWORD),
                ("InterfaceClassGuid", GUID),
                ("Flags", wt.DWORD),
                ("Reserved", ctypes.POINTER(ctypes.c_ulong))]


class SP_DEVICE_INTERFACE_DETAIL_DATA_W(ctypes.Structure):
    _fields_ = [("cbSize", wt.DWORD),
                ("DevicePath", ctypes.c_wchar * 512)]


class HIDD_ATTRIBUTES(ctypes.Structure):
    _fields_ = [("Size", ctypes.c_ulong),
                ("VendorID", ctypes.c_ushort),
                ("ProductID", ctypes.c_ushort),
                ("VersionNumber", ctypes.c_ushort)]


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


setupapi.SetupDiGetClassDevsW.restype = ctypes.c_void_p
setupapi.SetupDiGetClassDevsW.argtypes = [ctypes.POINTER(GUID), ctypes.c_wchar_p,
                                          ctypes.c_void_p, wt.DWORD]
setupapi.SetupDiEnumDeviceInterfaces.argtypes = [
    ctypes.c_void_p, ctypes.c_void_p, ctypes.POINTER(GUID), wt.DWORD,
    ctypes.POINTER(SP_DEVICE_INTERFACE_DATA)]
setupapi.SetupDiGetDeviceInterfaceDetailW.argtypes = [
    ctypes.c_void_p, ctypes.POINTER(SP_DEVICE_INTERFACE_DATA),
    ctypes.POINTER(SP_DEVICE_INTERFACE_DETAIL_DATA_W), wt.DWORD,
    ctypes.POINTER(wt.DWORD), ctypes.c_void_p]
setupapi.SetupDiDestroyDeviceInfoList.argtypes = [ctypes.c_void_p]

hid.HidD_GetHidGuid.argtypes = [ctypes.POINTER(GUID)]
hid.HidD_GetAttributes.argtypes = [ctypes.c_void_p, ctypes.POINTER(HIDD_ATTRIBUTES)]
hid.HidD_GetPreparsedData.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p)]
hid.HidP_GetCaps.argtypes = [ctypes.c_void_p, ctypes.POINTER(HIDP_CAPS)]
hid.HidD_FreePreparsedData.argtypes = [ctypes.c_void_p]
hid.HidD_GetProductString.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_ulong]

kernel32.CreateFileW.restype = ctypes.c_void_p
kernel32.CreateFileW.argtypes = [ctypes.c_wchar_p, wt.DWORD, wt.DWORD,
                                 ctypes.c_void_p, wt.DWORD, wt.DWORD, ctypes.c_void_p]
kernel32.CloseHandle.argtypes = [ctypes.c_void_p]


def try_open(path, access):
    h = kernel32.CreateFileW(path, access,
                             FILE_SHARE_READ | FILE_SHARE_WRITE,
                             None, OPEN_EXISTING, 0, None)
    if h == INVALID_HANDLE_VALUE:
        return None, ctypes.get_last_error() or kernel32.GetLastError()
    return h, 0


def main():
    guid = GUID()
    hid.HidD_GetHidGuid(ctypes.byref(guid))

    hdev = setupapi.SetupDiGetClassDevsW(ctypes.byref(guid), None, None,
                                         DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)
    if hdev == INVALID_HANDLE_VALUE or hdev is None:
        print("SetupDiGetClassDevs 失败")
        return

    print("=" * 78)
    print("HID 顶层集合枚举（只读诊断，未向设备写入任何数据）")
    print("目标设备: VID=0x%04X PID=0x%04X" % (TARGET_VID, TARGET_PID))
    print("=" * 78)

    idx = 0
    total = 0
    matched = []
    while True:
        did = SP_DEVICE_INTERFACE_DATA()
        did.cbSize = ctypes.sizeof(SP_DEVICE_INTERFACE_DATA)
        if not setupapi.SetupDiEnumDeviceInterfaces(hdev, None, ctypes.byref(guid),
                                                    idx, ctypes.byref(did)):
            break
        idx += 1
        total += 1

        detail = SP_DEVICE_INTERFACE_DETAIL_DATA_W()
        # 32 位下 cbSize=6，64 位下 cbSize=8
        detail.cbSize = 6 if ctypes.sizeof(ctypes.c_void_p) == 4 else 8
        req = wt.DWORD(0)
        if not setupapi.SetupDiGetDeviceInterfaceDetailW(
                hdev, ctypes.byref(did), ctypes.byref(detail),
                ctypes.sizeof(detail), ctypes.byref(req), None):
            continue
        path = detail.DevicePath

        # 先用无访问权限打开来读属性（最不容易被独占拒绝）
        h, err = try_open(path, 0)
        if h is None:
            continue
        attrs = HIDD_ATTRIBUTES()
        attrs.Size = ctypes.sizeof(HIDD_ATTRIBUTES)
        ok = hid.HidD_GetAttributes(h, ctypes.byref(attrs))
        if not ok or attrs.VendorID != TARGET_VID or attrs.ProductID != TARGET_PID:
            kernel32.CloseHandle(h)
            continue

        pp = ctypes.c_void_p()
        caps = HIDP_CAPS()
        has_caps = False
        if hid.HidD_GetPreparsedData(h, ctypes.byref(pp)):
            if hid.HidP_GetCaps(pp, ctypes.byref(caps)) == 0x00110000:  # HIDP_STATUS_SUCCESS
                has_caps = True
            hid.HidD_FreePreparsedData(pp)

        namebuf = ctypes.create_unicode_buffer(256)
        prod = ""
        if hid.HidD_GetProductString(h, namebuf, 512):
            prod = namebuf.value
        kernel32.CloseHandle(h)

        # 测试两种访问权限
        hrw, err_rw = try_open(path, GENERIC_READ | GENERIC_WRITE)
        if hrw is not None:
            kernel32.CloseHandle(hrw)
        h0, err_0 = try_open(path, 0)
        if h0 is not None:
            kernel32.CloseHandle(h0)

        rec = {
            "path": path, "prod": prod, "caps": caps if has_caps else None,
            "rw": (hrw is not None, err_rw), "noaccess": (h0 is not None, err_0),
        }
        matched.append(rec)

    setupapi.SetupDiDestroyDeviceInfoList(hdev)

    print("\n本机 HID 接口总数: %d，匹配目标 VID/PID 的集合数: %d\n" % (total, len(matched)))

    for i, r in enumerate(matched):
        c = r["caps"]
        print("-" * 78)
        print("[集合 %d] %s" % (i, r["prod"] or "(无产品名)"))
        print("  DevicePath: %s" % r["path"])
        if c:
            print("  UsagePage = 0x%02X   Usage = 0x%02X" % (c.UsagePage, c.Usage))
            print("  ReportByteLength:  Input=%d  Output=%d  Feature=%d"
                  % (c.InputReportByteLength, c.OutputReportByteLength,
                     c.FeatureReportByteLength))
            print("  FeatureValueCaps=%d  FeatureButtonCaps=%d"
                  % (c.NumberFeatureValueCaps, c.NumberFeatureButtonCaps))
        else:
            print("  !! HidP_GetCaps 失败")
        ok_rw, e_rw = r["rw"]
        ok_0, e_0 = r["noaccess"]
        print("  CreateFile(GENERIC_READ|GENERIC_WRITE): %s%s"
              % ("成功" if ok_rw else "失败", "" if ok_rw else "  (Win32 错误 %d)" % e_rw))
        print("  CreateFile(0 无访问权限):               %s%s"
              % ("成功" if ok_0 else "失败", "" if ok_0 else "  (Win32 错误 %d)" % e_0))
        if c and c.UsagePage == 0x0B:
            print("  >>> 这是官方 DLL 选中的特性接口（UsagePage=0x0B）")

    print("-" * 78)
    print("\n【关键结论速览】")
    feat = [r for r in matched if r["caps"] and r["caps"].UsagePage == 0x0B]
    if feat:
        c = feat[0]["caps"]
        print("  特性接口(0x0B) FeatureReportByteLength = %d" % c.FeatureReportByteLength)
        print("  → HidD_SetFeature 的 bufferLength 必须正好等于该值")
        print("  → 而我们的报告是 10 / 56 / 59 字节，%s"
              % ("需要补零到 %d" % c.FeatureReportByteLength
                 if c.FeatureReportByteLength not in (10, 56, 59) else "长度已匹配"))
    else:
        print("  !! 未找到 UsagePage=0x0B 的集合")
    print("\n诊断完成（全程只读，未向设备写入任何配置）。")


if __name__ == "__main__":
    main()
