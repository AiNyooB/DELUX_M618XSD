using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MouseDriverClient
{
    /// <summary>
    /// 主视图模型：聚合所有配置模型、运行时状态、下拉选项集合与业务命令，
    /// 使 MainWindow.xaml.cs 退化为"只负责绑定与事件转发"的纯视图层。
    /// 所有"发什么报告/什么顺序/间隔"的业务编排逻辑都集中在这里，可脱离 UI 独立测试。
    /// </summary>
    public class MainViewModel : NotifyBase, IDisposable
    {
        #region 设备通信
        private readonly HidComm _hid = new HidComm();
        private readonly System.Windows.Threading.Dispatcher _uiDispatcher
            = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        #endregion

        #region 配置模型（可被 XAML 直接绑定）
        public DpiConfig Dpi { get; } = new DpiConfig();
        public LightConfig Light { get; } = new LightConfig();
        public RateConfig Rate { get; } = new RateConfig();
        public ButtonConfig BtnCfg { get; } = new ButtonConfig();
        public ObservableCollection<DpiRow> DpiRows { get; }
        // 宏工作区：宏 ID(1-6) -> 录制内容。与宏编辑页共享同一份 Store。
        public Dictionary<byte, MacroWorkspace> Macros { get; } = new Dictionary<byte, MacroWorkspace>();
        #endregion

        #region 运行时状态（原本散落在 MainWindow 私有字段）
        private byte[] _appliedDpiReport = new byte[DpiConfig.Length];
        public byte[] AppliedDpiReport
        {
            get => _appliedDpiReport;
            set => Set(ref _appliedDpiReport, value);
        }

        // 当前档位（软件侧记忆，硬件切档上报会同步过来）。0 表示未应用。1-based 1-8。
        private int _currentLevel;
        public int CurrentLevel
        {
            get => _currentLevel;
            set => Set(ref _currentLevel, value);
        }

        // 记录模式：true=录制延迟(Record Delay)，false=默认延迟(Default Delay)
        private bool _recordMode;
        public bool RecordMode
        {
            get => _recordMode;
            set => Set(ref _recordMode, value);
        }

        // 按键应用是否走轻量序列（不写 DPI）。默认 true（与原 MainWindow 行为一致）。
        private bool _buttonLightweight = true;
        public bool ButtonLightweight
        {
            get => _buttonLightweight;
            set => Set(ref _buttonLightweight, value);
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (Set(ref _isConnected, value))
                    OnChanged(nameof(ConnectButtonText));
            }
        }

        // 连接按钮文字：随连接状态切换
        public string ConnectButtonText => _isConnected ? "断开设备" : "连接设备";
        #endregion

        #region 下拉选项集合（类型安全，替代脆弱的 ComboBoxItem.Tag 映射）
        public List<OptionItem<int>> LightModeOptions { get; } = new List<OptionItem<int>>
        {
            new OptionItem<int>("关闭", 0),
            new OptionItem<int>("呼吸DPI", 1),
            new OptionItem<int>("常亮DPI", 2),
            new OptionItem<int>("循环呼吸", 3),
            new OptionItem<int>("霓虹", 4),
        };

        public List<OptionItem<int>> RateOptions { get; } = new List<OptionItem<int>>
        {
            new OptionItem<int>("125Hz", 125),
            new OptionItem<int>("250Hz", 250),
            new OptionItem<int>("500Hz", 500),
            new OptionItem<int>("1000Hz", 1000),
        };

        // 仅暴露本机 M618XSD 的 10 个真实物理键（entry[6..13] 为固件预留、无对应物理键的"未使用"槽位，不暴露给用户）。
        // 顺序沿用官方软件直觉排列；其后的 _buttonEntryMap 给出「UI 序号 → 协议 0x08 entry 索引」的真实映射
        // （来自 HID协议逆向报告.md 3.5 节第 237-241 行实机验证，非推断）：
        //   UI 左键→entry0 右键→entry1 前进→entry2 后退→entry3 中键→entry4 DPI→entry5
        //   左滚→entry14 右滚→entry15 上滚→entry16 下滚→entry17
        private static readonly string[] _buttonNames = new[]
        {
            "左键", "右键", "前进", "后退", "中键", "DPI循环",
            "左滚", "右滚", "上滚", "下滚"
        };
        // UI 下拉序号(0-based) → 协议 0x08 entry 索引。长度须与 _buttonNames 一致。
        private static readonly int[] _buttonEntryMap = new[]
        {
            0,  // 左键
            1,  // 右键
            2,  // 前进
            3,  // 后退
            4,  // 中键
            5,  // DPI循环
            14, // 左滚
            15, // 右滚
            16, // 上滚
            17, // 下滚
        };
        // 供 UI 层（MainWindow.xaml.cs）把下拉序号转换为协议 entry 索引。
        public static int ButtonEntryMap(int uiIndex)
        {
            if (uiIndex < 0 || uiIndex >= _buttonEntryMap.Length) return -1;
            return _buttonEntryMap[uiIndex];
        }
        // UI 只暴露 10 个真实物理键；协议层 BtnCfg.Entries 仍是 18 项（整表覆写，见 ButtonConfig.ToBytes）。
        public List<OptionItem<int>> ButtonOptions { get; } = Enumerable.Range(0, 10)
            .Select(i => new OptionItem<int>(_buttonNames[i], i)).ToList();

        // 仅保留 HID协议逆向报告.md 3.5 节实机验证过的有效功能码（AGENTS.md 2.3：未验证编码禁止下发，会写坏设备）。
        // 0x12=宏（已按实机验证修正，原为误用的 0x11，曾导致 2.4G 断联）。
        public List<OptionItem<byte>> FuncOptions { get; } = new List<OptionItem<byte>>
        {
            new OptionItem<byte>("标准/未使用", 0x01),
            new OptionItem<byte>("左键", 0x02),
            new OptionItem<byte>("右键", 0x03),
            new OptionItem<byte>("中键", 0x04),
            new OptionItem<byte>("后退", 0x05),
            new OptionItem<byte>("前进", 0x06),
            new OptionItem<byte>("上滚", 0x09),
            new OptionItem<byte>("下滚", 0x0A),
            new OptionItem<byte>("左滚", 0x0B),
            new OptionItem<byte>("右滚", 0x0C),
            new OptionItem<byte>("DPI循环", 0x0D),
            new OptionItem<byte>("宏", 0x12),
        };

        // 键-宏选择：值=MacroConfig 的 ID（0=无）。UI 里 0 显示为"无"。
        public List<OptionItem<byte>> KeyMacroOptions { get; } = new List<OptionItem<byte>>
        {
            new OptionItem<byte>("无", 0),
            new OptionItem<byte>("宏 1", 1),
            new OptionItem<byte>("宏 2", 2),
            new OptionItem<byte>("宏 3", 3),
            new OptionItem<byte>("宏 4", 4),
            new OptionItem<byte>("宏 5", 5),
            new OptionItem<byte>("宏 6", 6),
            new OptionItem<byte>("宏 7", 7),
            new OptionItem<byte>("宏 8", 8),
        };

        /// <summary>
        /// 保存宏后刷新『绑定宏 ID』下拉文本：已保存的标记为 ✓（含按键数），
        /// 未保存的标记为（空）。让用户直观看到哪些宏 ID 已可用。
        /// </summary>
        public void RefreshKeyMacroOptions()
        {
            foreach (var opt in KeyMacroOptions)
            {
                if (!(opt.Value is byte mid) || mid == 0) continue;
                if (Macros.TryGetValue(mid, out var ws) && ws.Actions.Count > 0)
                    opt.Text = $"宏 {mid} ✓（{ws.Actions.Count / 2}键）";
                else
                    opt.Text = $"宏 {mid}（空）";
            }
        }

        // 记录模式选项（宏）
        public List<OptionItem<bool>> RecordModeOptions { get; } = new List<OptionItem<bool>>
        {
            new OptionItem<bool>("默认延迟", false),
            new OptionItem<bool>("录制延迟", true),
        };

        // 档位选项（仅 1-5，M618XSD 本机实际只有 5 个可用档位，6-8 官方隐藏不启用）
        public List<OptionItem<int>> LevelOptions { get; } = Enumerable.Range(1, 5)
            .Select(i => new OptionItem<int>($"档位 {i}", i)).ToList();
        #endregion

        #region 日志
        private ObservableCollection<string> _logLines = new ObservableCollection<string>();
        public ObservableCollection<string> LogLines
        {
            get => _logLines;
            set => Set(ref _logLines, value);
        }

        private string _logText = string.Empty;
        public string LogText
        {
            get => _logText;
            set => Set(ref _logText, value);
        }

        public void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            var disp = _uiDispatcher ?? Application.Current?.Dispatcher;
            if (disp != null)
            {
                disp.Invoke(() =>
                {
                    LogLines.Add(line);
                    while (LogLines.Count > 500) LogLines.RemoveAt(0);
                    LogText = LogText + line + Environment.NewLine;
                });
            }
            else
            {
                LogLines.Add(line);
                while (LogLines.Count > 500) LogLines.RemoveAt(0);
                LogText = LogText + line + Environment.NewLine;
            }
        }
        #endregion

        #region 命令
        public RelayCommand ConnectCmd { get; }
        public RelayCommand ApplyAllCmd { get; }
        public RelayCommand ApplyLightCmd { get; }
        public RelayCommand ApplyRateCmd { get; }
        public RelayCommand ApplyDpiCmd { get; }
        public RelayCommand ApplyButtonCmd { get; }
        public RelayCommand SwitchLevelCmd { get; }
        public RelayCommand RecoverLightCmd { get; }
        #endregion

        public MainViewModel()
        {
            // 捕获主线程 Dispatcher，供后台线程（Task.Run 连接逻辑）安全回写日志/属性
            _uiDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

            // 0x04 报告协议层仍为 8 档槽位（固件内存布局），但 M618XSD 本机只有 5 个可用档位
            // （800/1200/1600/2400/4000，官方软件中 6-8 档隐藏不启用，见 AGENTS.md 2.1 节）。
            // 因此 UI 只暴露前 5 档供用户编辑；槽位 6/7/8 固定为 0，不暴露、不可编辑。
            Dpi.Levels = new int[8]; // 协议层 8 槽，后 3 槽由 SyncDpiConfig 固定填 0
            DpiRows = new ObservableCollection<DpiRow>(
                Enumerable.Range(1, 5).Select(n => new DpiRow // 仅 5 档暴露给 UI
                {
                    Label = $"档位 {n}",
                    Value = new[] { 800, 1200, 1600, 2400, 4000 }[n - 1].ToString(),
                    Enabled = true, // 5 档全部启用（官方默认 800/1200/1600/2400/4000）
                }));

            // 订阅设备输入报告：硬件切档上报(ID=3) buf[3]=当前档位
            _hid.DpiLevelChanged += Hid_DpiLevelChanged;

            ConnectCmd = new RelayCommand(_ =>
            {
                _ = Task.Run(() =>
                {
                    try { Connect(); }
                    catch (Exception ex) { Log("连接异常: " + ex.Message); }
                });
            });
            ApplyAllCmd = new RelayCommand(_ => ApplyAll());
            ApplyLightCmd = new RelayCommand(_ => ApplyLight());
            ApplyRateCmd = new RelayCommand(_ => ApplyRate());
            ApplyDpiCmd = new RelayCommand(_ => ApplyDpi());
            ApplyButtonCmd = new RelayCommand(_ => ApplyButton());
            SwitchLevelCmd = new RelayCommand(p => SwitchLevel(System.Convert.ToInt32(p)));
            RecoverLightCmd = new RelayCommand(_ => RecoverLight());
        }

        #region 设备事件
        private void Hid_DpiLevelChanged(byte level)
        {
            // 硬件切档上报理论上范围 1-8（固件内存支持），但本机 UI 只暴露 1-5。
            // 超出 5 的档位（6-8）本机不会出现，作为异常值忽略。
            if (level < 1 || level > 8) return;
            if (level > 5) { Log($"[硬件切档] 收到异常档位 {level}（本机仅 1-5），已忽略"); return; }
            if (level != CurrentLevel)
            {
                CurrentLevel = level;
                Log($"[硬件切档] 档位 → {level}");
            }
            // 同步 DPI 页激活高亮（DpiRow.IsActive 单向绑到 RadioButton.IsChecked）
            for (int i = 0; i < DpiRows.Count; i++)
                DpiRows[i].IsActive = (i == level - 1);
        }
        #endregion

        #region 连接 / 断开
        public void Connect()
        {
            if (IsConnected)
            {
                Dispose();
                IsConnected = false;
                Log("已断开");
                return;
            }

            Log("连接设备...");

            // 先打印枚举结果，便于出问题时定位是哪个集合
            var collections = HidComm.EnumerateCollections();
            Log($"枚举到 {collections.Count} 个匹配 VID/PID 的 HID 集合:");
            for (int i = 0; i < collections.Count; i++)
                Log($"  [{i}] {collections[i]}");

            if (!_hid.Connect())
            {
                Log("✗ 打开设备失败：" + _hid.LastErrorMessage);
                return;
            }
            IsConnected = true;
            Log($"✓ 已连接 — UsagePage=0x0B 特性接口, Feature 长度={_hid.FeatureReportLength}");

            // 档位同步策略（见项目 AGENTS.md）：主动读取不可行，硬件切档上报可读。
            // 连接初期占位高亮第 1 档，待首次硬件上报纠正。
            if (_hid.OpenDataInterface())
            {
                _hid.StartInputListener();
                _hid.BatteryChanged += (cs, pct) =>
                {
                    var disp = _uiDispatcher ?? Application.Current?.Dispatcher;
                    var txt = cs switch
                    {
                        1 => "未充电/满电",
                        2 => "充电中",
                        3 => "充电完成",
                        4 => "插入检测",
                        _ => "未知(" + cs + ")"
                    };
                    if (disp != null)
                        disp.Invoke(() => { BatteryPercent = pct; BatteryChargeText = txt; });
                    else
                        { BatteryPercent = pct; BatteryChargeText = txt; }
                };
                Log("✓ 已启动 Input Report 监听（硬件切档上报 -> 自动同步）");
            }
            else
            {
                Log("⚠ 数据接口打开失败（仅失去硬件切档自动跟随）：" + _hid.LastErrorMessage);
            }

            CurrentLevel = 1; // 占位
            Log($"本地记忆档位={CurrentLevel}（占位；按鼠标 DPI 键可自动同步真实档位）");
            _hid.Wake();
        }
        #endregion

        #region 电池
        private int? _batteryPercent;
        public int? BatteryPercent
        {
            get => _batteryPercent;
            set => Set(ref _batteryPercent, value);
        }

        private string _batteryChargeText = "—";
        public string BatteryChargeText
        {
            get => _batteryChargeText;
            set => Set(ref _batteryChargeText, value);
        }
        #endregion

        #region 业务编排（原 MainWindow 按钮事件里的发送逻辑，集中于此）
        // 唤醒报告（0x0C），Mouse.exe 应用全部流程第一步
        private static readonly byte[] WakeReport =
            { 0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0x00, 0x00, 0x00, 0x00 };

        private void Send(byte[] report)
        {
            // 诊断：打印完整原始字节，便于与已验证的 macro_write_simple.py 逐字节比对
            Log($"  [RAW] {report[0]:X2} {BitConverter.ToString(report, 1).Replace('-', ' ')}");
            bool ok = _hid.WriteFeature(report);
            Log(ok ? $"  → WriteFeature OK (ID=0x{report[0]:X2})"
                   : $"  → WriteFeature 失败 (ID=0x{report[0]:X2}): {_hid.LastErrorMessage}");
        }

        // 把 DpiRows（UI 当前编辑）同步进 DpiConfig，并写入当前活跃档位。
        // UI 只暴露 5 档（DpiRows.Count == 5），同步前 5 槽；槽位 6/7/8 固定为 0（本机不启用）。
        private void SyncDpiConfig()
        {
            byte bitmap = 0;
            for (int i = 0; i < 5; i++) // 仅同步 UI 暴露的 5 档
            {
                Dpi.Levels[i] = int.TryParse(DpiRows[i].Value, out int v) ? v : 0;
                if (DpiRows[i].Enabled) bitmap |= (byte)(1 << i);
            }
            for (int i = 5; i < 8; i++) Dpi.Levels[i] = 0; // 槽位 6/7/8 固定 0，本机不启用
            Dpi.EnabledBitmap = bitmap; // 仅前 5 位有效（0x1F）
            if (CurrentLevel >= 1 && CurrentLevel <= 5) // UI 活跃档位只允许 1..5
                Dpi.ActiveLevel = (byte)CurrentLevel;
        }

        // 应用灯光 + 电源管理（0x05）
        public void ApplyLight()
        {
            // 电源字段(byte5 睡眠 / byte9 一级休眠)须先打开数据设备(0x0A)才生效；
            // 若未打开，设备会忽略电源设置甚至可能断联（见 AGENTS.md §0 前置条件）。
            if (!_hid.IsDataInterfaceOpen && !_hid.OpenDataInterface())
            {
                Log("✗ 应用灯光+电源失败：无法打开数据设备(0x0A)，电源字段(睡眠/一级休眠)将被忽略。");
                return;
            }
            Log("应用灯光+电源管理(0x05)...");
            Send(Light.ToBytes());
        }

        // 恢复灯光：用 light_recovery.py 的已知良好载荷（0x05 灯光/呼吸/速度）
        public void RecoverLight()
        {
            byte[] payload = { 0x05, 0x0F, 0x01, 0x83, 0x05, 0x98, 0x00, 0x00, 0xFF, 0x02, 0x03, 0x00, 0x00, 0x00, 0x00 };
            Log("恢复灯光(0x05)...");
            Send(payload);
        }

        // 应用回报率（0x06），必须与 0x0C 唤醒同发
        public void ApplyRate()
        {
            Log("应用回报率(0x06)...");
            Send(WakeReport);
            Send(Rate.ToBytes());
        }

        // 应用 DPI（全套数值 + 活跃档位），含 0x0C 唤醒 + 校验和
        public void ApplyDpi()
        {
            Log("应用 DPI(0x04)...");
            Send(WakeReport);
            SyncDpiConfig();
            var r = Dpi.ToBytes();
            AppliedDpiReport = r;
            Send(r);
            Log($"  DPI 应用完成，活跃档位={CurrentLevel}。");
        }

        // 仅切档（轻量，不重发全套数值），只改 [24]。UI 只允许 1-5 档，故限制范围。
        public void SwitchLevel(int level)
        {
            if (level < 1 || level > 5) return; // 本机仅 5 档（M618XSD，6-8 不启用）
            CurrentLevel = level;
            Log($"切档 → {level} (仅写 [24])...");
            Send(WakeReport);
            var r = (byte[])AppliedDpiReport.Clone();
            r[0] = 0x04;
            r[24] = (byte)level;
            Dpi.ActiveLevel = (byte)level;
            Send(r);
        }

        // 应用按键映射（0x08），整表覆写（BtnCfg.Entries 已在 UI 侧维护完整）
        public void ApplyButton()
        {
            Log("应用按键映射(0x08)...");
            Send(WakeReport);
            // 已验证路径（macro_write_simple.py）在 0x0C 唤醒后 sleep(0.2) 再发 0x08；
            // 自写程序此前无此间隔，补上与官方一致的稳定时序。
            Thread.Sleep(200);
            // 非轻量序列则先同步当前 DPI 面板值（完整序列）
            if (!ButtonLightweight) ApplyDpi();
            Send(BtnCfg.ToBytes());
            // 宏数据：按键表里标记为「宏」的条目，按各自宏 ID 补发宏内容
            SendMacroDataIfAny();
        }

        /// <summary>
        /// 扫描当前按键表，对每一个标记为「宏」的条目，按 entry[2]=宏ID
        /// 从共享工作区 Macros 取录制内容发 0x09×3。
        /// 该宏 ID 在本程序未录制过内容时，保持设备原有宏内容，不触碰。
        /// </summary>
        public void SendMacroDataIfAny()
        {
            var macroIds = new HashSet<byte>();
            foreach (var ent in BtnCfg.Entries)
                if (ent[0] == ButtonConfig.FuncCode.Macro) macroIds.Add(ent[2]);
            if (macroIds.Count == 0) return;
            foreach (var mid in macroIds)
            {
                if (!Macros.TryGetValue(mid, out var ws) || ws.Actions.Count == 0) continue; // 未录制 -> 不触碰设备原有宏
                byte recordMode = RecordMode ? (byte)0x07 : (byte)0x01;
                var chunks = MacroConfig.BuildMacroChunks(mid, ws.Method, recordMode, ws.Actions, ws.LoopCount);
                Log($"写入 0x09×3（宏 ID={mid}，按键数={ws.Actions.Count / 2}）");
                foreach (var ch in chunks)
                {
                    Send(ch);
                    Thread.Sleep(200);
                }
            }
        }

        // 应用全部：0x0C + 0x04 + 0x05 + 0x06 + 0x08 + 0x09×N
        public void ApplyAll()
        {
            Log("===== 应用全部 =====");
            Send(WakeReport);
            ApplyDpi();
            ApplyLight();
            ApplyRate();
            ApplyButton();
            // 宏数据：按键表里标记为「宏」的条目，按各自宏 ID 补发宏内容
            SendMacroDataIfAny();
            Log("===== 完成 =====");
        }
        #endregion

        public void Dispose()
        {
            _hid.DpiLevelChanged -= Hid_DpiLevelChanged;
            _hid.Dispose();
        }
    }
}
