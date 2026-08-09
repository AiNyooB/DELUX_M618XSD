# -*- coding: utf-8 -*-
"""实机验证: Input Report(buf[3]) 上报 是否 = 鼠标 OLED 实际档位。
用系统 hid.dll + setupapi 直接打开 UsagePage=0x0A 数据接口 (复刻上位机 HidComm.OpenDataInterface)。
同时用官方 hiddriver_ms_4.dll 读 [24] 做对照。
操作: 跑起来后按 DPI 键切档, 看 Input上报 buf[3] 与 OLED 是否一致; [24] 是否乱值。
"""
import ctypes, time

APP = r"C:\Users\fresh\Downloads\618XSD\extracted\app"
DLL_PATH = APP + r"\hiddriver_ms_4.dll"
VID, PID = 0x1D57, 0xFA60

# ---- 特性设备 (读 [24]) ----
dll = ctypes.WinDLL(DLL_PATH)
dll.Set_VIDPID.argtypes = [ctypes.c_uint, ctypes.c_uint]; dll.Set_VIDPID.restype = ctypes.c_int
dll.Open_FeatureDevice.restype = ctypes.c_int
dll.GetFeature.argtypes = [ctypes.c_char_p, ctypes.c_uint]; dll.GetFeature.restype = ctypes.c_int
dll.Close_FeatureDevice.restype = ctypes.c_int
dll.Set_VIDPID(VID, PID)
if not dll.Open_FeatureDevice():
    print("Open_FeatureDevice 失败 (确认官方 Mouse.exe 已退出)"); raise SystemExit(1)

def read_24():
    buf = ctypes.create_string_buffer(bytes([0x04]) + b"\x00"*0x3F, 0x40)
    r = dll.GetFeature(buf, 0x40)
    return buf.raw[24] if r else None

# ---- 数据接口 (Input Report) via 系统 hid.dll ----
kernel = ctypes.WinDLL("kernel32", use_last_error=True)
hid = ctypes.WinDLL("hid.dll")
setupapi = ctypes.WinDLL("setupapi")

GENERIC_READ = 0x80000000
FILE_SHARE_READ = 0x00000001
FILE_SHARE_WRITE = 0x00000002
OPEN_EXISTING = 3
INVALID_HANDLE = ctypes.c_void_p(-1).value  # 0xFFFFFFFF (32位) / 0xFF...FF (64位)
DIGCF_PRESENT = 0x00000002
DIGCF_DEVICEINTERFACE = 0x00000010

# CreateFileW 必须声明返回 c_void_p, 否则 32 位下 INVALID_HANDLE_VALUE(-1) 比较会失败
kernel.CreateFileW.restype = ctypes.c_void_p
kernel.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_ulong, ctypes.c_ulong,
                               ctypes.c_void_p, ctypes.c_ulong, ctypes.c_ulong, ctypes.c_void_p]
kernel.CloseHandle.argtypes = [ctypes.c_void_p]
kernel.ReadFile.restype = ctypes.c_int
kernel.ReadFile.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_ulong), ctypes.c_void_p]
kernel.GetLastError.restype = ctypes.c_ulong

class GUID(ctypes.Structure):
    _fields_ = [("Data1", ctypes.c_ulong), ("Data2", ctypes.c_ushort),
                ("Data3", ctypes.c_ushort), ("Data4", ctypes.c_ubyte*8)]
class SP_DEVICE_INTERFACE_DATA(ctypes.Structure):
    _fields_ = [("cbSize", ctypes.c_ulong), ("InterfaceClassGuid", GUID),
                ("Flags", ctypes.c_ulong), ("Reserved", ctypes.c_ulong)]
class SP_DEVICE_INTERFACE_DETAIL_DATA(ctypes.Structure):
    # Windows 上 cbSize 固定 6 (4字节指针架构下); 用固定 6 避免 sizeof 在 32位下歧义
    _fields_ = [("cbSize", ctypes.c_ulong), ("DevicePath", ctypes.c_char*520)]
# 显式设 cbSize=6（32 位 / 64 位 Win32 SP_DEVICE_INTERFACE_DETAIL_DATA 的固定值）
SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize = 6

guid = GUID()
hid.HidD_GetHidGuid(ctypes.byref(guid))

HidD_GetHidGuid = hid.HidD_GetHidGuid
HidD_GetHidGuid.argtypes = [ctypes.c_void_p]; HidD_GetHidGuid.restype = ctypes.c_int

class HIDD_ATTRS(ctypes.Structure):
    _fields_ = [("Size", ctypes.c_ulong), ("VendorID", ctypes.c_ushort),
                ("ProductID", ctypes.c_ushort), ("VersionNumber", ctypes.c_ushort)]
class HIDP_CAPS(ctypes.Structure):
    _fields_ = [("Usage", ctypes.c_ushort), ("UsagePage", ctypes.c_ushort),
                ("InputReportByteLength", ctypes.c_ushort),
                ("OutputReportByteLength", ctypes.c_ushort),
                ("FeatureReportByteLength", ctypes.c_ushort),
                ("Reserved", ctypes.c_ushort*17),
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

# SetupDi 函数签名
setupapi.SetupDiGetClassDevsW.restype = ctypes.c_void_p
setupapi.SetupDiGetClassDevsW.argtypes = [ctypes.c_void_p, ctypes.c_wchar_p, ctypes.c_void_p, ctypes.c_ulong]
setupapi.SetupDiEnumDeviceInterfaces.restype = ctypes.c_int
setupapi.SetupDiEnumDeviceInterfaces.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_ulong, ctypes.c_void_p]
setupapi.SetupDiGetDeviceInterfaceDetailW.restype = ctypes.c_int
setupapi.SetupDiGetDeviceInterfaceDetailW.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_ulong), ctypes.c_void_p]

hid.HidD_GetAttributes.restype = ctypes.c_int
hid.HidD_GetAttributes.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
hid.HidD_GetPreparsedData.restype = ctypes.c_int
hid.HidD_GetPreparsedData.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p)]
hid.HidP_GetCaps.restype = ctypes.c_int
hid.HidP_GetCaps.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
hid.HidD_FreePreparsedData.restype = ctypes.c_int
hid.HidD_FreePreparsedData.argtypes = [ctypes.c_void_p]

hDevInfo = setupapi.SetupDiGetClassDevsW(ctypes.byref(guid), None, None,
                                        DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)
if hDevInfo == INVALID_HANDLE or hDevInfo is None:
    print("SetupDiGetClassDevs 失败"); raise SystemExit(1)

data_path = None
idx = 0
did = SP_DEVICE_INTERFACE_DATA()
did.cbSize = ctypes.sizeof(SP_DEVICE_INTERFACE_DATA)
candidates = []
enum_count = 0
while True:
    ok = setupapi.SetupDiEnumDeviceInterfaces(hDevInfo, None, ctypes.byref(guid), idx, ctypes.byref(did))
    if not ok:
        break
    idx += 1
    enum_count += 1
    detail = SP_DEVICE_INTERFACE_DETAIL_DATA()
    req = ctypes.c_ulong(0)
    # 第一遍: 取所需长度
    setupapi.SetupDiGetDeviceInterfaceDetailW(hDevInfo, ctypes.byref(did), None, 0, ctypes.byref(req), None)
    if req.value == 0:
        print("[dbg] #%d 取长度失败" % enum_count, flush=True); continue
    detail = SP_DEVICE_INTERFACE_DETAIL_DATA()
    if not setupapi.SetupDiGetDeviceInterfaceDetailW(hDevInfo, ctypes.byref(did),
            ctypes.byref(detail), req.value, ctypes.byref(req), None):
        print("[dbg] #%d 取详情失败 err=%d" % (enum_count, kernel.GetLastError()), flush=True); continue
    try:
        path = detail.DevicePath.decode("utf-8", "ignore")
    except Exception as e:
        print("[dbg] #%d 路径解码失败 %r" % (enum_count, e), flush=True); continue
    if not path:
        print("[dbg] #%d 路径为空" % enum_count, flush=True); continue
    # 用 access=0 打开读属性 (鼠标类接口不允许 RW 独占)
    h = kernel.CreateFileW(path, 0, FILE_SHARE_READ|FILE_SHARE_WRITE, None, OPEN_EXISTING, 0, None)
    if h == INVALID_HANDLE:
        print("[dbg] #%d 打不开 err=%d path=%s" % (enum_count, kernel.GetLastError(), path), flush=True); continue
    try:
        attrs = HIDD_ATTRS(); attrs.Size = ctypes.sizeof(attrs)
        if not hid.HidD_GetAttributes(h, ctypes.byref(attrs)):
            print("[dbg] #%d 无属性 path=%s" % (enum_count, path), flush=True); continue
        vid_ok = (attrs.VendorID == VID)
        pid_ok = (attrs.ProductID == PID)
        print("[dbg] #%d VID=0x%04X PID=0x%04X match=%s path=%s"
              % (enum_count, attrs.VendorID, attrs.ProductID, (vid_ok and pid_ok), path), flush=True)
        if not (vid_ok and pid_ok):
            continue
        pp = ctypes.c_void_p()
        if not hid.HidD_GetPreparsedData(h, ctypes.byref(pp)):
            print("[dbg] 跳过(无preparsed) %s" % path, flush=True); continue
        try:
            caps = HIDP_CAPS()
            if hid.HidP_GetCaps(pp, ctypes.byref(caps)) != 0:
                print("[dbg] 跳过(getcaps失败) %s" % path, flush=True); continue
        finally:
            hid.HidD_FreePreparsedData(pp)
        candidates.append((path, caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.FeatureReportByteLength))
        print("[dbg] 命中 VID/PID 集合: UsagePage=0x%02X Usage=0x%02X InLen=%d FeatLen=%d"
              % (caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.FeatureReportByteLength), flush=True)
        if caps.UsagePage == 0x0A and caps.InputReportByteLength > 0 and not data_path:
            data_path = path
            print("[dbg] -> 选定数据接口 0x0A: %s" % path, flush=True)
    finally:
        kernel.CloseHandle(h)

print("[dbg] 枚举到 %d 个 HID 接口, 其中 VID/PID 匹配 %d 个" % (enum_count, len(candidates)), flush=True)
if not data_path and candidates:
    for (p, up, u, il, fl) in candidates:
        if il > 0:
            data_path = p
            print("[dbg] 退路选定: UsagePage=0x%02X %s" % (up, p), flush=True)
            break

if not data_path:
    print("[warn] 未找到任何可用的数据接口 (0x0A 或任意 Input>0)", flush=True)
    print("[warn] 全部命中集合: %s" % str(candidates), flush=True)
else:
    hData = kernel.CreateFileW(data_path, GENERIC_READ, FILE_SHARE_READ|FILE_SHARE_WRITE, None, OPEN_EXISTING, 0, None)
    if hData == INVALID_HANDLE:
        print("[warn] 打开数据接口失败 err=%d" % kernel.GetLastError(), flush=True)
        data_path = None

print("=" * 60, flush=True)
v24 = read_24()
print("[24] = %s  (档位应是 1-8, 115 之类说明不可信)" % v24, flush=True)
print(">>> 现在用鼠标 DPI 键切几档, 我会打印 Input 上报 buf[3]", flush=True)
print("    对照鼠标 OLED 实际档位; 按 Ctrl+C 结束\n", flush=True)

if data_path:
    buf = ctypes.create_string_buffer(64)
    read = ctypes.c_ulong()
    last = None
    try:
        while True:
            res = kernel.ReadFile(hData, buf, 64, ctypes.byref(read), None)
            n = read.value
            if n > 0:
                rid = buf.raw[0]
                if rid == 0x03 and n >= 4 and buf.raw[1] == 0x28 and buf.raw[2] == 0x10:
                    lvl = buf.raw[3]
                    if lvl != last:
                        last = lvl
                        v24b = read_24()
                        print("  Input上报: buf[3]=%d   | 此刻[24]=%s   (请核对 OLED)" % (lvl, v24b), flush=True)
                else:
                    print("  [其它] rid=%d n=%d raw=%s" % (rid, n, buf.raw[:n].hex(" ")), flush=True)
            time.sleep(0.01)
    except KeyboardInterrupt:
        print("\n结束", flush=True)
    kernel.CloseHandle(hData)

dll.Close_FeatureDevice()
print("done", flush=True)
