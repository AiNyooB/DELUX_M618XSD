using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseDriverClient
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
    ///  - 所有报告统一补零到 64 字节；0x04 报告 [52..55] 本就是填充零，
    ///    校验和只覆盖 [3..49]，补零不影响设备解析。
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
        /// 实测：传入具体 Report ID（如 0x04）时，设备返回对应 ID 的那一页，
        /// 而非"顺序内存页"——config_snapshot.py 即遍历 RID 0x01..0x20 各读一页，
        /// dpi_write.py 也用 GetFeature(0x04) 回读刚写入的 0x04 报告验证成功。
        /// 因此可安全地反复 GetFeature(0x04) 读取，不会因调用次数错位。
        /// ⚠️ 但 0x04 的 [24] 已被 2026-08-02 实机证伪：恒为 0，不跟随硬件档位，
        /// 不能当作"当前 DPI 档位"。当前档位请以 Input Report（ID=3, buf[3]）为准。
        ///
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
        /// ❌ 已废弃（2026-08-02 实机证伪）：GetFeature(0x04) 读出的 [24] 恒为 0，
        /// 不跟随硬件 DPI 键档位。详见 AGENTS.md 2.4 节。
        /// 当前档位一律以 Input Report（ID=3, buf[3]）为准，见 ListenLoop / OnDpiLevelChanged。
        /// 本方法仅保留作诊断，不应再用于 UI 同步。
        /// </summary>
        [Obsolete("DPI 档位以 Input Report 为准，不要再调用本方法")]
        public byte ReadActiveDpiLevel()
        {
            var buf = ReadFeature(0x04);
            if (buf == null) return 0;
            byte level = buf[24];
            return (level >= 1 && level <= 8) ? level : (byte)0;
        }

        /// <summary>
        /// 唤醒设备：发送 0x0C 握手命令（实测可唤醒休眠中的鼠标）。
        /// 官方软件的操作序列第一步即为此命令。
        /// </summary>
        public bool Wake()
        {
            // 0C 0A 01 FE 01 FE 00 00 00 00 （来源：AGENTS.md 1.4 实测）
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
        // 设备切 DPI 档时主动发 Input Report ID=3，buf[3]=当前档位索引(1-5；本驱动仅启用默认 5 档，6-8 档不处理)。
        // 据此实时刷新 UI（OnDpiLevelChanged），并同步本地记忆；主动 GetFeature 读取 [24] 不可行。

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

        /// <summary>打开数据接口（UsagePage=0x0A）用于监听 Input Report。
        /// 鼠标类接口不允许 RW 独占打开，使用 GENERIC_READ 即可读 Input Report。</summary>
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
                HidNative.CancelIo(_dataHandle);
                HidNative.CloseHandle(_dataHandle);
                _dataHandle = HidNative.INVALID_HANDLE_VALUE;
            }
        }

        private void ListenLoop()
        {
            // Input Report：首字节为 Report ID，其余为数据。HID 栈会加 1 字节 ID 前缀。
            var buf = new byte[64];
            while (_listening)
            {
                uint read = 0;
                bool ok = HidNative.ReadFile(_dataHandle, buf, (uint)buf.Length, out read, IntPtr.Zero);
                if (!ok || read == 0)
                {
                    // 被 CancelIo 打断或设备移除
                    if (!_listening) break;
                    Thread.Sleep(50);
                    continue;
                }
                // [诊断] 全量记录每条 Input Report（无论是否匹配），始终写入 dpi_diag.log，
                // 便于实机按 DPI 键后核对设备真实上报格式（[1][2] 是否恒为 28 10 待验证）。
                {
                    string hex = BitConverter.ToString(buf, 0, (int)read);
                    string allLine = $"[Input原始] rid={buf[0]} n={read} raw={hex}";
                    System.Diagnostics.Debug.WriteLine(allLine);
                    try { System.IO.File.AppendAllText(
                        System.IO.Path.Combine(System.AppContext.BaseDirectory, "dpi_diag.log"),
                        DateTime.Now.ToString("HH:mm:ss") + " " + allLine + "\n"); } catch { }
                }
                // 档位上报过滤（已据 dpi_diag.log 实机数据校准，2026-08-03）：
                //  设备周期性发送两类 Input Report (rid=3)：
                //   · [1]=0x28 [2]=0x10  → 真正的 DPI 切档上报，buf[3]=当前档位(1-5；默认仅启用前 5 档)。
                //   · [1]=0x28 [2]=0x40  → 心跳/状态包（约每 4 秒一次），buf[3] 恒为 1，
                //     若误当作档位会把 UI 反复拉回第 1 档（"亮一下又回第一档"的根因）。
                //  故必须同时判定 [1]==0x28 且 [2]==0x10，仅此类触发档位同步。
                if (buf[0] == 0x03 && read >= 4 && buf[1] == 0x28 && buf[2] == 0x10 && buf[3] >= 1 && buf[3] <= 5)
                {
                    byte level = buf[3];
                    {
                        string line = $"[DPI诊断] Input上报 buf[3]={level} (raw[1..2]={buf[1]:X2} {buf[2]:X2})";
                        System.Diagnostics.Debug.WriteLine(line);
                        try { System.IO.File.AppendAllText(
                            System.IO.Path.Combine(System.AppContext.BaseDirectory, "dpi_diag.log"),
                            DateTime.Now.ToString("HH:mm:ss") + " " + line + "\n"); } catch { }
                    }
                    DpiLevelChanged?.Invoke(level);
                }
                // 电池 / 充电状态包（AGENTS.md §6 已实机确认，2026-08-07）：
                //   Input Report (rid=3)：03 28 40 XX YY
                //   buf[3] = 充电状态：01=未充电/满电 02=充电中 03=充电完成 04=插入检测
                //   buf[4] = 电池电量百分比（0-100 十进制直读）
                else if (buf[0] == 0x03 && read >= 5 && buf[1] == 0x28 && buf[2] == 0x40)
                {
                    byte chargeState = buf[3];
                    byte percent = buf[4];
                    BatteryChanged?.Invoke(chargeState, percent);
                }
            }
        }
    }
}
