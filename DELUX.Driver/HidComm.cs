using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace DeluxDriver
{
    /// <summary>
    /// 枚举到的一个 HID 顶层集合的信息（诊断用）。
    /// </summary>
    public class HidCollectionInfo
    {
        public string DevicePath { get; set; } = "";
        public string ProductName { get; set; } = "";
        public ushort UsagePage { get; set; }
        public ushort Usage { get; set; }
        public int FeatureReportByteLength { get; set; }
        public int InputReportByteLength { get; set; }
        public bool CanOpenReadWrite { get; set; }
        public int OpenError { get; set; }

        public override string ToString() =>
            $"UsagePage=0x{UsagePage:X02}/Usage=0x{Usage:X02} Feature={FeatureReportByteLength} " +
            $"RW={(CanOpenReadWrite ? "OK" : $"失败({OpenError})")}";
    }

    /// <summary>
    /// HID 通信层：替代 hiddriver_ms_4.dll 的薄封装，直接 P/Invoke 系统 hid.dll。
    /// 设备：DELUX M618XSD，VID=0x1D57 PID=0xFA60。
    ///
    /// 关键实测结论（2026-08-01 hid_enum_diag.py）：
    ///  - 该设备暴露 7 个 HID 顶层集合，其中 6 个的 FeatureReportByteLength = 0，
    ///    对它们调用 HidD_SetFeature 必然返回 FALSE。
    ///  - 唯一可用的是 UsagePage=0x0B / Usage=0x00 的集合，FeatureReportByteLength = 64。
    ///  - 鼠标/键盘集合（UsagePage=0x01）Windows 禁止 GENERIC_READ|GENERIC_WRITE 独占打开
    ///    （Win32 错误 5，反按键记录器机制），因此读属性阶段必须用 access=0 打开。
    ///  - 所有报告统一补零到 64 字节；校验和只覆盖约定区间，补零不影响设备解析。
    /// </summary>
    public class HidComm : IDisposable
    {
        public const int VID = 0x1D57;
        public const int PID = 0xFA60;

        /// <summary>特性接口的 UsagePage（官方 DLL 的过滤判据）。</summary>
        public const ushort FEATURE_USAGE_PAGE = 0x0B;

        /// <summary>实测 FeatureReportByteLength = 64，所有报告补零到该长度。</summary>
        public const int REPORT_LENGTH = 64;

        private IntPtr _handle = HidNative.INVALID_HANDLE_VALUE;

        /// <summary>最近一次失败的 Win32 错误码（0 表示无错误）。</summary>
        public int LastError { get; private set; }

        /// <summary>最近一次失败的说明文本。</summary>
        public string LastErrorMessage { get; private set; } = "";

        /// <summary>已连接的特性接口设备路径。</summary>
        public string DevicePath { get; private set; } = "";

        /// <summary>设备报告的 Feature 报告长度（连接后有效）。</summary>
        public int FeatureReportLength { get; private set; }

        public bool IsConnected => _handle != HidNative.INVALID_HANDLE_VALUE;

        /// <summary>
        /// 枚举本机所有匹配 VID/PID 的 HID 顶层集合（只读，不打开写句柄以外的资源）。
        /// 供诊断/日志使用。
        /// </summary>
        public static List<HidCollectionInfo> EnumerateCollections()
        {
            var result = new List<HidCollectionInfo>();

            var guid = new HidNative.GUID { Data4 = new byte[8] };
            HidNative.HidD_GetHidGuid(ref guid);

            IntPtr devInfo = HidNative.SetupDiGetClassDevs(
                ref guid, IntPtr.Zero, IntPtr.Zero,
                HidNative.DIGCF_PRESENT | HidNative.DIGCF_DEVICEINTERFACE);

            if (devInfo == HidNative.INVALID_HANDLE_VALUE) return result;

            try
            {
                for (int index = 0; ; index++)
                {
                    var did = new HidNative.SP_DEVICE_INTERFACE_DATA();
                    did.cbSize = Marshal.SizeOf(did);
                    if (!HidNative.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref guid, index, ref did))
                        break;

                    var detail = new HidNative.SP_DEVICE_INTERFACE_DETAIL_DATA
                    {
                        // 32 位下为 6，64 位下为 8（结构体对齐差异）
                        cbSize = IntPtr.Size == 8 ? 8 : 6,
                        DevicePath = ""
                    };
                    int required = 0;
                    if (!HidNative.SetupDiGetDeviceInterfaceDetail(
                            devInfo, ref did, ref detail, Marshal.SizeOf(detail), ref required, IntPtr.Zero))
                        continue;

                    string path = detail.DevicePath;

                    // 关键：读属性阶段用 access=0 打开。
                    // 鼠标/键盘集合不允许 RW 独占打开，但允许无访问权限打开来查询能力。
                    IntPtr h = HidNative.CreateFile(
                        path, 0, HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
                        IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
                    if (h == HidNative.INVALID_HANDLE_VALUE) continue;

                    try
                    {
                        var attrs = new HidNative.HIDD_ATTRIBUTES();
                        attrs.Size = Marshal.SizeOf(attrs);
                        if (!HidNative.HidD_GetAttributes(h, ref attrs)) continue;
                        if (attrs.VendorID != VID || attrs.ProductID != PID) continue;

                        var info = new HidCollectionInfo { DevicePath = path };

                        IntPtr pp = IntPtr.Zero;
                        if (HidNative.HidD_GetPreparsedData(h, ref pp))
                        {
                            try
                            {
                                var caps = new HidNative.HIDP_CAPS { Reserved = new ushort[17] };
                                if (HidNative.HidP_GetCaps(pp, ref caps) == HidNative.HIDP_STATUS_SUCCESS)
                                {
                                    info.UsagePage = caps.UsagePage;
                                    info.Usage = caps.Usage;
                                    info.FeatureReportByteLength = caps.FeatureReportByteLength;
                                    info.InputReportByteLength = caps.InputReportByteLength;
                                }
                            }
                            finally { HidNative.HidD_FreePreparsedData(pp); }
                        }

                        var nameBuf = new byte[256];
                        if (HidNative.HidD_GetProductString(h, nameBuf, nameBuf.Length))
                            info.ProductName = System.Text.Encoding.Unicode.GetString(nameBuf).TrimEnd('\0');

                        // 探测能否以读写方式打开
                        IntPtr hrw = HidNative.CreateFile(
                            path, HidNative.GENERIC_READ | HidNative.GENERIC_WRITE,
                            HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
                            IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
                        if (hrw != HidNative.INVALID_HANDLE_VALUE)
                        {
                            info.CanOpenReadWrite = true;
                            HidNative.CloseHandle(hrw);
                        }
                        else
                        {
                            info.OpenError = Marshal.GetLastWin32Error();
                        }

                        result.Add(info);
                    }
                    finally
                    {
                        HidNative.CloseHandle(h);
                    }
                }
            }
            finally
            {
                HidNative.SetupDiDestroyDeviceInfoList(devInfo);
            }

            return result;
        }

        /// <summary>
        /// 枚举并打开特性设备接口（等价于 hiddriver 的 Open_FeatureDevice）。
        /// 过滤判据与官方 DLL 一致：VID/PID 匹配 且 UsagePage==0x0B。
        /// 若未命中，退回选择第一个 FeatureReportByteLength &gt; 0 的集合。
        /// </summary>
        public bool Connect()
        {
            Dispose();
            LastError = 0;
            LastErrorMessage = "";

            var collections = EnumerateCollections();
            if (collections.Count == 0)
            {
                LastErrorMessage = "未枚举到匹配 VID=0x1D57 PID=0xFA60 的 HID 集合，请确认接收器已插入";
                return false;
            }

            // 首选官方判据：UsagePage == 0x0B
            HidCollectionInfo? target = collections.Find(
                c => c.UsagePage == FEATURE_USAGE_PAGE && c.FeatureReportByteLength > 0);

            // 退路：任何支持 Feature 报告的集合
            target ??= collections.Find(c => c.FeatureReportByteLength > 0);

            if (target == null)
            {
                LastErrorMessage =
                    $"找到 {collections.Count} 个集合，但没有一个支持 Feature 报告" +
                    "（FeatureReportByteLength 全为 0）";
                return false;
            }

            IntPtr h = HidNative.CreateFile(
                target.DevicePath,
                HidNative.GENERIC_READ | HidNative.GENERIC_WRITE,
                HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
                IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);

            if (h == HidNative.INVALID_HANDLE_VALUE)
            {
                LastError = Marshal.GetLastWin32Error();
                LastErrorMessage = LastError == 5
                    ? "打开设备被拒绝（错误 5）：官方 Mouse.exe 可能仍在运行，请完全退出后重试"
                    : $"打开设备失败，Win32 错误 {LastError}";
                return false;
            }

            _handle = h;
            DevicePath = target.DevicePath;
            FeatureReportLength = target.FeatureReportByteLength;
            return true;
        }

        /// <summary>
        /// 把任意长度的报告补零到设备要求的 Feature 报告长度。
        /// 超长则截断（不应发生）。
        /// </summary>
        private byte[] PadReport(byte[] report)
        {
            int len = FeatureReportLength > 0 ? FeatureReportLength : REPORT_LENGTH;
            if (report.Length == len) return report;
            var buf = new byte[len];
            Array.Copy(report, buf, Math.Min(report.Length, len));
            return buf;
        }

        /// <summary>
        /// 发送 Feature Report，report[0] 即 Report ID（等价于 SetFeature）。
        /// 内部自动补零到 64 字节。
        /// </summary>
        public bool WriteFeature(byte[] report)
        {
            LastError = 0;
            LastErrorMessage = "";
            if (!IsConnected)
            {
                LastErrorMessage = "设备未连接";
                return false;
            }
            if (report == null || report.Length == 0)
            {
                LastErrorMessage = "报告为空";
                return false;
            }

            var buf = PadReport(report);
            bool ok = HidNative.HidD_SetFeature(_handle, buf, buf.Length);
            if (!ok)
            {
                LastError = Marshal.GetLastWin32Error();
                LastErrorMessage = $"HidD_SetFeature 失败，Win32 错误 {LastError}";
            }
            return ok;
        }

        /// <summary>
        /// 读取 Feature Report（按 Report ID 寻址）。
        /// ⚠️ reportId 必须非 0：该设备使用带编号的报告，
        /// 传 0 会被 Windows 拒绝并返回 Win32 错误 87（ERROR_INVALID_PARAMETER）。
        /// </summary>
        public byte[]? ReadFeature(byte reportId = 0x04)
        {
            LastError = 0;
            LastErrorMessage = "";
            if (!IsConnected)
            {
                LastErrorMessage = "设备未连接";
                return null;
            }

            int len = FeatureReportLength > 0 ? FeatureReportLength : REPORT_LENGTH;
            var buf = new byte[len];
            buf[0] = reportId;

            if (!HidNative.HidD_GetFeature(_handle, buf, buf.Length))
            {
                LastError = Marshal.GetLastWin32Error();
                LastErrorMessage = $"HidD_GetFeature 失败，Win32 错误 {LastError}";
                return null;
            }
            return buf;
        }

        /// <summary>
        /// 唤醒设备：发送 0x0C 握手命令（实测可唤醒休眠中的鼠标）。
        /// </summary>
        public bool Wake()
        {
            var wake = new byte[] { 0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0x00, 0x00, 0x00, 0x00 };
            bool ok = WriteFeature(wake);
            if (ok) Thread.Sleep(300); // 与 Python 验证脚本一致，给设备响应时间
            return ok;
        }

        public void Dispose()
        {
            if (_handle != HidNative.INVALID_HANDLE_VALUE)
            {
                HidNative.CloseHandle(_handle);
                _handle = HidNative.INVALID_HANDLE_VALUE;
            }
            DevicePath = "";
            FeatureReportLength = 0;
            GC.SuppressFinalize(this);
        }

        ~HidComm() => Dispose();

        // ===== Input Report 监听（数据接口 UsagePage=0x0A）=====
        // 设备切 DPI 档时主动发 Input Report ID=3，buf[3]=当前档位索引(1-5)。

        /// <summary>数据接口的 UsagePage（身份/输入报告）。</summary>
        public const ushort DATA_USAGE_PAGE = 0x0A;

        private IntPtr _dataHandle = HidNative.INVALID_HANDLE_VALUE;
        private Thread? _listenThread;
        private volatile bool _listening;

        /// <summary>数据接口(0x0A)是否已打开（用于电源命令前置条件检查）。</summary>
        public bool IsDataInterfaceOpen => _dataHandle != HidNative.INVALID_HANDLE_VALUE;

        /// <summary>收到 DPI 档位变化（1-5）时触发。</summary>
        public Action<byte>? DpiLevelChanged;

        /// <summary>收到电池状态变化时触发（chargeState：1=未充/满电 2=充电中 3=充完 4=插入检测；percent：0-100）。</summary>
        public Action<byte, byte>? BatteryChanged;

        /// <summary>是否已收到过任意一条合法 Input Report（鼠标真正在线的判据）。</summary>
        public bool HasInputSignal { get; private set; }

        /// <summary>重置在线信号标记（每次重新连接前调用）。</summary>
        public void ResetInputSignal() => HasInputSignal = false;

        /// <summary>打开数据接口（UsagePage=0x0A）用于监听 Input Report。</summary>
        public bool OpenDataInterface()
        {
            var collections = EnumerateCollections();
            var target = collections.Find(c => c.UsagePage == DATA_USAGE_PAGE && c.InputReportByteLength > 0);
            if (target == null)
            {
                LastErrorMessage = "未找到数据接口（UsagePage=0x0A）";
                return false;
            }
            IntPtr h = HidNative.CreateFile(
                target.DevicePath,
                HidNative.GENERIC_READ,
                HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
                IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == HidNative.INVALID_HANDLE_VALUE)
            {
                LastError = Marshal.GetLastWin32Error();
                LastErrorMessage = $"打开数据接口失败，Win32 错误 {LastError}";
                return false;
            }
            _dataHandle = h;
            return true;
        }

        /// <summary>启动后台线程监听 Input Report，解析 DPI 档位并回调。</summary>
        public void StartInputListener()
        {
            if (_dataHandle == HidNative.INVALID_HANDLE_VALUE) return;
            _listening = true;
            _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "HidInput" };
            _listenThread.Start();
        }

        public void StopInputListener()
        {
            _listening = false;
            if (_dataHandle != HidNative.INVALID_HANDLE_VALUE)
            {
                // ReadFile 由 HidInput 线程发起，CancelIo 只能取消当前线程的 I/O，故用 CancelIoEx 跨线程取消，
                // 否则挂起的 ReadFile 会让 CloseHandle 永久阻塞（窗口关闭后进程残留）。
                HidNative.CancelIoEx(_dataHandle, IntPtr.Zero);
                // 等待监听线程退出后再关句柄，避免对仍在 ReadFile 的句柄做 CloseHandle 阻塞。
                _listenThread?.Join(2000);
                HidNative.CloseHandle(_dataHandle);
                _dataHandle = HidNative.INVALID_HANDLE_VALUE;
                _listenThread = null;
            }
        }

        private void ListenLoop()
        {
            var buf = new byte[64];
            while (_listening)
            {
                uint read = 0;
                bool ok = HidNative.ReadFile(_dataHandle, buf, (uint)buf.Length, out read, IntPtr.Zero);
                if (!ok || read == 0)
                {
                    if (!_listening) break;
                    Thread.Sleep(50);
                    continue;
                }

                // 收到任意一条合法 Input Report（ID=3）即标记鼠标真正在线。
                if (buf[0] == 0x03 && read > 0)
                    HasInputSignal = true;

                // 档位上报过滤（已据实机数据校准）：
                //   仅当 [1]==0x28 且 [2]==0x10 才为真正的 DPI 切档上报，buf[3]=当前档位(1-5)。
                if (buf[0] == 0x03 && read >= 4 && buf[1] == 0x28 && buf[2] == 0x10 && buf[3] >= 1 && buf[3] <= 5)
                {
                    DpiLevelChanged?.Invoke(buf[3]);
                }
                // 电池 / 充电状态包：03 28 40 XX YY
                else if (buf[0] == 0x03 && read >= 5 && buf[1] == 0x28 && buf[2] == 0x40)
                {
                    BatteryChanged?.Invoke(buf[3], buf[4]);
                }
            }
        }
    }
}
