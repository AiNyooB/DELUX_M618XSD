# -*- coding: utf-8 -*-
"""
dpi_connect_sync_probe.py — 一次性诊断脚本
目的：验证「连接设备后能否读到鼠标当前 DPI 档位」。
  - 全程不读 0x04 的 [24]（已知恒为 0，2026-08-02 证伪）。
  - 只观察 Input Report（数据接口 UsagePage=0x0A），看 buf[3] 是否跟随真实档位。

结论（2026-08-02 实机）：Input Report 的 buf[3] 不跟随真实档位。连接后自发上报两帧交替：
  - 03 28 40 01 2c  （固定状态帧）
  - 03 28 10 <n> 00  （枚举帧，buf[3] 走自发序列 1->3->4->5，与真实档位无关）
用户按 DPI 键切档（OLED 确认在变）时 buf[3] 完全不变。故当前档位**不可读**，
上位机应采用「0x04 写入目标档位 + 本地记忆」模型，不能依赖读取。

用法（必须 32 位 Python，否则 ctypes 无法加载 32 位 hiddriver_ms_4.dll）：
  C:\tmp\re32\py\python.exe dpi_connect_sync_probe.py
先确保官方 Mouse.exe 已完全退出。运行时可在监听窗口内按 DPI 键观察字节变化。
"""
import ctypes
import ctypes.wintypes as wt
import re
import time

APP = r"C:\Users\fresh\Downloads\618XSD\extracted\app"
DLL_PATH = APP + r"\hiddriver_ms_4.dll"  # 占位，下方会尝试真实文件名
DLL_CANDIDATES = [
    r"C:\Users\fresh\Downloads\618XSD\extracted\app\hiddriver_ms_4.dll",
]
VID, PID = 0x1D57, 0xFA60
DATA_USAGE_PAGE = 0x0A

GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
FILE_SHARE_READ = 0x00000001
FILE_SHARE_WRITE = 0x00000002
OPEN_EXISTING = 3
INVALID_HANDLE = -1 & 0xFFFFFFFF
DIGCF_PRESENT = 0x02
DIGCF_DEVICEINTERFACE = 0x10
HIDP_STATUS_SUCCESS = 0x00110000

kernel32 = ctypes.windll.kernel32
hid = ctypes.WinDLL('hid.dll')
setupapi = ctypes.WinDLL('setupapi.dll')


class GUID(ctypes.Structure):
    _fields_ = [("Data1", ctypes.c_ulong), ("Data2", ctypes.c_ushort),
                ("Data3", ctypes.c_ushort), ("Data4", ctypes.c_ubyte * 8)]


class HIDD_ATTRIBUTES(ctypes.Structure):
    _fields_ = [("Size", ctypes.c_ulong), ("VendorID", ctypes.c_ushort),
                ("ProductID", ctypes.c_ushort), ("VersionNumber", ctypes.c_ushort)]


class SP_DEVICE_INTERFACE_DATA(ctypes.Structure):
    _fields_ = [("cbSize", ctypes.c_ulong), ("InterfaceClassGuid", GUID),
                ("Flags", ctypes.c_ulong), ("Reserved", ctypes.c_void_p)]


class SP_DEVICE_INTERFACE_DETAIL_DATA(ctypes.Structure):
    _fields_ = [("cbSize", ctypes.c_ulong), ("DevicePath", ctypes.c_wchar * 256)]


class HIDP_CAPS(ctypes.Structure):
    _fields_ = [("Usage", ctypes.c_ushort), ("UsagePage", ctypes.c_ushort),
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
                ("NumberFeatureDataIndices", ctypes.c_ushort)]


class OVERLAPPED(ctypes.Structure):
    _fields_ = [("Internal", ctypes.c_void_p), ("InternalHigh", ctypes.c_void_p),
                ("Offset", wt.DWORD), ("OffsetHigh", wt.DWORD),
                ("hEvent", wt.HANDLE)]


kernel32.CreateFileW.argtypes = [wt.LPCWSTR, wt.DWORD, wt.DWORD, ctypes.c_void_p,
                                 wt.DWORD, wt.DWORD, wt.HANDLE]
kernel32.CreateFileW.restype = wt.HANDLE
kernel32.CloseHandle.argtypes = [wt.HANDLE]
kernel32.CloseHandle.restype = wt.BOOL
kernel32.ReadFile.argtypes = [wt.HANDLE, ctypes.c_void_p, wt.DWORD,
                              ctypes.POINTER(wt.DWORD), ctypes.c_void_p]
kernel32.ReadFile.restype = wt.BOOL
kernel32.GetLastError.argtypes = []
kernel32.GetLastError.restype = wt.DWORD
kernel32.CreateEventW.argtypes = [ctypes.c_void_p, wt.BOOL, wt.BOOL, wt.LPCWSTR]
kernel32.CreateEventW.restype = wt.HANDLE
kernel32.WaitForSingleObject.argtypes = [wt.HANDLE, wt.DWORD]
kernel32.WaitForSingleObject.restype = wt.DWORD
kernel32.GetOverlappedResult.argtypes = [wt.HANDLE, ctypes.c_void_p,
                                         ctypes.POINTER(wt.DWORD), wt.BOOL]
kernel32.GetOverlappedResult.restype = wt.BOOL
kernel32.CancelIo.argtypes = [wt.HANDLE]
kernel32.CancelIo.restype = wt.BOOL

hid.HidD_GetHidGuid.argtypes = [ctypes.POINTER(GUID)]
hid.HidD_GetHidGuid.restype = None
hid.HidD_GetAttributes.argtypes = [wt.HANDLE, ctypes.POINTER(HIDD_ATTRIBUTES)]
hid.HidD_GetAttributes.restype = wt.BOOL
hid.HidD_GetPreparsedData.argtypes = [wt.HANDLE, ctypes.POINTER(ctypes.c_void_p)]
hid.HidD_GetPreparsedData.restype = wt.BOOL
hid.HidP_GetCaps.argtypes = [ctypes.c_void_p, ctypes.POINTER(HIDP_CAPS)]
hid.HidP_GetCaps.restype = ctypes.c_long
hid.HidD_FreePreparsedData.argtypes = [ctypes.c_void_p]
hid.HidD_FreePreparsedData.restype = wt.BOOL

setupapi.SetupDiGetClassDevsW.argtypes = [ctypes.POINTER(GUID), ctypes.c_void_p,
                                          ctypes.c_void_p, wt.DWORD]
setupapi.SetupDiGetClassDevsW.restype = wt.HANDLE
setupapi.SetupDiEnumDeviceInterfaces.argtypes = [wt.HANDLE, ctypes.c_void_p,
    ctypes.POINTER(GUID), wt.DWORD, ctypes.POINTER(SP_DEVICE_INTERFACE_DATA)]
setupapi.SetupDiEnumDeviceInterfaces.restype = wt.BOOL
setupapi.SetupDiGetDeviceInterfaceDetailW.argtypes = [wt.HANDLE,
    ctypes.POINTER(SP_DEVICE_INTERFACE_DATA), ctypes.c_void_p, wt.DWORD,
    ctypes.POINTER(wt.DWORD), ctypes.c_void_p]
setupapi.SetupDiGetDeviceInterfaceDetailW.restype = wt.BOOL
setupapi.SetupDiDestroyDeviceInfoList.argtypes = [wt.HANDLE]
setupapi.SetupDiDestroyDeviceInfoList.restype = wt.BOOL


def enumerate_collections():
    """枚举匹配 VID/PID 的所有顶层集合，返回 [(path, usage_page, input_len, feature_len)]。"""
    guid = GUID()
    hid.HidD_GetHidGuid(ctypes.byref(guid))
    dev = setupapi.SetupDiGetClassDevsW(ctypes.byref(guid), None, None,
                                        DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)
    if dev == INVALID_HANDLE:
        return []
    out = []
    idx = 0
    did = SP_DEVICE_INTERFACE_DATA()
    did.cbSize = ctypes.sizeof(SP_DEVICE_INTERFACE_DATA)
    while setupapi.SetupDiEnumDeviceInterfaces(dev, None, ctypes.byref(guid), idx, ctypes.byref(did)):
        idx += 1
        needed = wt.DWORD(0)
        setupapi.SetupDiGetDeviceInterfaceDetailW(dev, ctypes.byref(did), None, 0,
                                                  ctypes.byref(needed), None)
        detail = SP_DEVICE_INTERFACE_DETAIL_DATA()
        detail.cbSize = 8 if ctypes.sizeof(ctypes.c_void_p) == 8 else 6
        if not setupapi.SetupDiGetDeviceInterfaceDetailW(dev, ctypes.byref(did),
                ctypes.byref(detail), ctypes.sizeof(detail), None, None):
            continue
        path = detail.DevicePath
        h = kernel32.CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                 None, OPEN_EXISTING, 0, None)
        if h == INVALID_HANDLE:
            continue
        try:
            attrs = HIDD_ATTRIBUTES()
            attrs.Size = ctypes.sizeof(HIDD_ATTRIBUTES)
            if not hid.HidD_GetAttributes(h, ctypes.byref(attrs)):
                continue
            if attrs.VendorID != VID or attrs.ProductID != PID:
                continue
            up = 0xFFFF
            inlen = 0
            featlen = 0
            pp = ctypes.c_void_p()
            if hid.HidD_GetPreparsedData(h, ctypes.byref(pp)):
                caps = HIDP_CAPS()
                if hid.HidP_GetCaps(pp, ctypes.byref(caps)) == HIDP_STATUS_SUCCESS:
                    up = caps.UsagePage
                    inlen = caps.InputReportByteLength
                    featlen = caps.FeatureReportByteLength
                hid.HidD_FreePreparsedData(pp)
            out.append((path, up, inlen, featlen))
        finally:
            kernel32.CloseHandle(h)
    setupapi.SetupDiDestroyDeviceInfoList(dev)
    return out


def listen_for_seconds(h, seconds, label, last=None):
    """用 OVERLAPPED 阻塞读（带超时，能打断），每条 Input Report 原始 5 字节全 dump。

    last: 传入上一条原始 bytes，用于高亮「哪些字节相对上一条变了」。
    返回 (hits, last_raw)。
    """
    print("\n=== 阶段 %s：监听 %ds（请按 DPI 键试验）===" % (label, seconds))
    buf = ctypes.create_string_buffer(257)
    read = wt.DWORD(0)
    ov = OVERLAPPED()
    ov.hEvent = kernel32.CreateEventW(None, True, False, None)
    t0 = time.time()
    hits = 0
    pending = False
    last_raw = last
    while time.time() - t0 < seconds:
        if not pending:
            read.value = 0
            rc = kernel32.ReadFile(h, buf, 256, ctypes.byref(read), ctypes.byref(ov))
            if rc:
                n = read.value
            else:
                err = kernel32.GetLastError()
                if err == 997:  # ERROR_IO_PENDING
                    pending = True
                else:
                    print("  ReadFile 错误 %d" % err)
                    break
        if pending:
            wait = kernel32.WaitForSingleObject(ov.hEvent, 200)
            if wait == 0:
                if kernel32.GetOverlappedResult(h, ctypes.byref(ov), ctypes.byref(read), False):
                    n = read.value
                else:
                    n = 0
                pending = False
            else:
                continue
        if n > 0:
            raw = bytes(buf[0:n])      # bytes，每个元素已是 int
            rid = raw[0]
            # 全字节 dump，不预设格式
            parts = ["%02x" % b for b in raw]
            mark = ""
            if rid == 0x03 and len(raw) >= 4:
                hits += 1
                if last_raw is not None and raw != last_raw:
                    diff = " ".join(("^%02x" % raw[i]) if (i >= len(last_raw) or raw[i] != last_raw[i]) else "  " for i in range(len(raw)))
                    mark = "  [相对上条变化] %s" % diff
                print("  [Input] ID=0x03  raw=%s   buf[3]=%d buf[4]=%d%s" % (" ".join(parts), raw[3], raw[4] if len(raw) > 4 else -1, mark))
            else:
                print("  [Input] ID=%s raw=%s" % (hex(rid), " ".join(parts)))
            last_raw = raw
    kernel32.CancelIo(h)
    print("=== 阶段 %s 结束，收到 Input 上报 %d 条 ===" % (label, hits))
    return hits, last_raw


def main():
    dll = None
    for c in DLL_CANDIDATES:
        try:
            dll = ctypes.WinDLL(c)
            DLL_PATH = c
            break
        except Exception as e:
            print(f"[*] 加载 {c} 失败: {e}")
    if dll is None:
        print("[!] 无法加载 hiddriver_ms_4.dll，仅做 Input 监听（不发 wake）")
    else:
        dll.Set_VIDPID.argtypes = [ctypes.c_uint, ctypes.c_uint]
        dll.Set_VIDPID.restype = ctypes.c_int
        dll.Open_FeatureDevice.restype = ctypes.c_int
        dll.SetFeature.argtypes = [ctypes.c_char_p, ctypes.c_uint]
        dll.SetFeature.restype = ctypes.c_int
        dll.Close_FeatureDevice.restype = ctypes.c_int

    print(f"枚举 VID={VID:#06x} PID={PID:#06x} 的所有集合 ...")
    cols = enumerate_collections()
    if not cols:
        print("[!] 未找到设备，请确认接收器已插入且 Mouse.exe 已退出")
        return
    for i, (path, up, inlen, featlen) in enumerate(cols):
        print(f"  [{i}] UsagePage=0x{up:02X} InputLen={inlen} FeatureLen={featlen} path=...{path[-36:]}")

    target = next((c for c in cols if c[1] == DATA_USAGE_PAGE and c[2] > 0), None)
    if target is None:
        print("[!] 未找到数据接口(UsagePage=0x0A)。将尝试第一个 InputLen>0 的接口。")
        target = next((c for c in cols if c[2] > 0), None)
    if target is None:
        print("[!] 没有任何可监听的 Input 接口")
        return

    path, up, inlen, featlen = target
    print(f"[+] 监听目标: UsagePage=0x{up:02X} InputLen={inlen} path=...{path[-36:]}")
    h = kernel32.CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                             None, OPEN_EXISTING, 0, None)
    if h == INVALID_HANDLE:
        print(f"[!] 打开数据接口失败，Win32 错误 {kernel32.GetLastError()}")
        return

    feat_ok = False
    if dll is not None:
        dll.Set_VIDPID(VID, PID)
        feat_ok = dll.Open_FeatureDevice()
        print(f"[+] 特性设备 Open_FeatureDevice: {'成功' if feat_ok else '失败'}")

    print("\n>>> 即将进入 30s 监听窗口。请在窗口内按几下 DPI 键（让档位变化），")
    print(">>> 脚本会实时打印每帧并高亮相对上一条变化的字节。")
    try:
        if feat_ok:
            wake = bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0, 0, 0, 0])
            r = dll.SetFeature(wake, len(wake))
            print(f"[+] 已发送 wake(0x0C) -> {r}")
        hits, _ = listen_for_seconds(h, 30, "监听（请按 DPI 键）")
    finally:
        if feat_ok:
            dll.Close_FeatureDevice()
        kernel32.CloseHandle(h)

    print("\n=== 结论提示 ===")
    print("本窗口共收到 %d 条 Input 上报。" % hits)
    print("[*] 查看带 [相对上条变化] 标记的行：")
    print("    - 若按 DPI 键后某个字节（通常是 buf[3]）跟着变 → 那字节就是实时档位字段；")
    print("    - 若按键后 28 10 帧的 buf[3] 停在某一值 → 该值即当前档位序号（1~5）；")
    print("    - 若按键完全无变化 → 此 0x03 帧不含实时档位，连接同步需另想方案。")


if __name__ == "__main__":
    main()
