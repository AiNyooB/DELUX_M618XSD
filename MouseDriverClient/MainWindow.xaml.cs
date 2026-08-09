using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MouseDriverClient
{
    public partial class MainWindow : Window
    {
        // 视图模型：所有配置状态与发送命令都在这里，本类只做绑定与事件转发
        private readonly MainViewModel VM;

        public MainWindow()
        {
            InitializeComponent();
            VM = new MainViewModel();
            DataContext = VM;
            MacroEditor.RecordModeChanged = (rec) => VM.RecordMode = rec; // 宏编辑页切换延迟模式即同步 VM
            // 宏内容（动作/延迟）编辑后即时同步进 VM.Macros，编辑即生效，无需手动点「保存宏」
            MacroEditor.OnMacroChanged = (id) =>
            {
                VM.Macros[id] = MacroEditor.Store[id];
                VM.RefreshKeyMacroOptions();
            };
            // 切换宏 ID 时把该宏已保存的循环次数回填到循环次数输入框
            MacroEditor.MacroIdChanged = (id) =>
            {
                byte loop = 1;
                if (VM.Macros.TryGetValue(id, out var ws)) loop = ws.LoopCount;
                TxtLoop.Text = loop.ToString();
            };
            ShowPage("dpi");
            // 绑定下拉/列表数据源（否则 DPI 档位与按键下拉为空）
            DpiList.ItemsSource = VM.DpiRows;
            CmbButton.DisplayMemberPath = "Text"; CmbButton.SelectedValuePath = "Value"; CmbButton.ItemsSource = VM.ButtonOptions;
            CmbFunc.DisplayMemberPath = "Text"; CmbFunc.SelectedValuePath = "Value"; CmbFunc.ItemsSource = VM.FuncOptions;
            CmbKeyMacroId.DisplayMemberPath = "Text"; CmbKeyMacroId.SelectedValuePath = "Value"; CmbKeyMacroId.ItemsSource = VM.KeyMacroOptions;
            if (CmbButton.Items.Count > 0) CmbButton.SelectedIndex = 0;

        }

        #region 日志（转发到 VM，ListBox 绑定 LogLines）
        private void AppendLog(string msg) => VM.Log(msg);
        #endregion

        #region 页面导航
        private void ShowPage(string name)
        {
            foreach (var p in new[] { PageDpi, PageKey, PageMacro, PageLightPower, PageRate, PageBattery })
                p.Visibility = Visibility.Collapsed;
            switch (name)
            {
                case "dpi": PageDpi.Visibility = Visibility.Visible; break;
                case "key": PageKey.Visibility = Visibility.Visible; break;
                case "macro": PageMacro.Visibility = Visibility.Visible; break;
                case "lightpower": PageLightPower.Visibility = Visibility.Visible; break;
                case "rate": PageRate.Visibility = Visibility.Visible; break;
                case "battery": PageBattery.Visibility = Visibility.Visible; break;
            }
        }
        private void NavDpi_Click(object s, RoutedEventArgs e) => ShowPage("dpi");
        private void NavKey_Click(object s, RoutedEventArgs e) => ShowPage("key");
        private void NavMacro_Click(object s, RoutedEventArgs e) => ShowPage("macro");
        private void NavLightPower_Click(object s, RoutedEventArgs e) => ShowPage("lightpower");
        private void NavRate_Click(object s, RoutedEventArgs e) => ShowPage("rate");
        private void NavBattery_Click(object s, RoutedEventArgs e) => ShowPage("battery");
        private void NavAll_Click(object s, RoutedEventArgs e) => VM.ApplyAll();
        #endregion

        #region DPI 页
        private void DpiActive_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.DataContext is DpiRow row)
                VM.CurrentLevel = VM.DpiRows.IndexOf(row) + 1; // 1-based 档位号
        }
        #endregion

        #region 按键映射页
        // UI 下拉序号(0-based) → 协议 0x08 entry 索引（与 MainViewModel._buttonEntryMap 对应）
        private static int UiButtonToEntry(int uiIndex) => MainViewModel.ButtonEntryMap(uiIndex);

        private void CmbButton_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbButton.SelectedValue is int idx)
            {
                var ent = VM.BtnCfg.Entries[UiButtonToEntry(idx)];
                CmbFunc.SelectedValue = ent[0];
                CmbKeyMacroId.SelectedValue = ent[2];
                UpdateBtnRaw();
            }
        }

        private void CmbFunc_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbButton.SelectedValue is int idx && CmbFunc.SelectedValue is byte func)
            {
                int entry = UiButtonToEntry(idx);
                VM.BtnCfg.Entries[entry][0] = func;
                if (func == ButtonConfig.FuncCode.Macro)
                {
                    GrpKeyMacro.Visibility = Visibility.Visible;
                    CmbKeyMacroId.IsEnabled = true;
                }
                else
                {
                    GrpKeyMacro.Visibility = Visibility.Collapsed;
                    CmbKeyMacroId.IsEnabled = false;
                    VM.BtnCfg.Entries[entry][2] = 0;
                }
                UpdateBtnRaw();
            }
        }

        private void CmbKeyMacroId_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbButton.SelectedValue is int idx && CmbKeyMacroId.SelectedValue is byte mid)
            {
                VM.BtnCfg.Entries[UiButtonToEntry(idx)][2] = mid;
                UpdateBtnRaw();
            }
        }

        private void UpdateBtnRaw()
        {
            if (CmbButton.SelectedValue is int idx)
            {
                var e3 = VM.BtnCfg.Entries[UiButtonToEntry(idx)];
                TxtBtnRaw.Text = $"原始 3 字节: {e3[0]:X2} {e3[1]:X2} {e3[2]:X2}";
            }
        }

        private void BtnGoMacro_Click(object sender, RoutedEventArgs e) => ShowPage("macro");
        #endregion

        #region 宏管理页
        private void BtnSaveMacro_Click(object sender, RoutedEventArgs e)
        {
            byte id = MacroEditor.CurrentId;
            var ws = MacroEditor.Current;
            if (ws.Actions.Count == 0)
            {
                VM.Log($"⚠ 宏 {id} 内容为空：请先用「+ 键盘」录制动作再保存");
                return;
            }
            VM.Macros[id] = ws;
            // 读取循环次数（最小为 1；<1 或解析失败统一 clamp 为 1，固件实测 0 会被当作 1）
            if (int.TryParse(TxtLoop.Text, out int loop) && loop >= 1)
                ws.LoopCount = (byte)Math.Min(loop, 255);
            else
                ws.LoopCount = 1;
            VM.RecordMode = MacroEditor.GetRecordMode(); // 同步延迟录制模式到 VM（发送时用）
            int n = ws.Actions.Count / 2;
            VM.RefreshKeyMacroOptions();
            // 重新绑定下拉，确保新保存标记（✓）立即可见
            CmbKeyMacroId.ItemsSource = VM.KeyMacroOptions;
            CmbKeyMacroId.SelectedValue = id;
            VM.Log($"✓ 宏 {id} 已保存（{n} 个按键，循环次数={ws.LoopCount}（≥1 次），延迟模式：{(VM.RecordMode ? "录制延迟" : "默认延迟")}）。切到「按键映射」页，把某键「功能」设为「宏」，『绑定宏 ID』下拉中现在会以 ✓ 标记本宏，选中它即可完成绑键。");
        }
        #endregion

        #region 配置管理（复位 / 导出 / 导入）
        private void BtnFactory_Click(object sender, RoutedEventArgs e)
        {
            var def = new AppConfig();
            SaveConfig(def, "app_config.json");
            VM.Log("✓ 已复位本程序配置副本（默认值为软件推断，未经抓包证实，未必等于设备真出厂值）。");
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { FileName = "app_config.json", Filter = "JSON|*.json" };
            if (dlg.ShowDialog() != true) return;
            SaveConfig(new AppConfig(), dlg.FileName);
            VM.Log($"✓ 已导出配置到 {dlg.FileName}");
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
            if (dlg.ShowDialog() != true) return;
            if (!File.Exists(dlg.FileName)) { VM.Log("✗ 文件不存在"); return; }
            try
            {
                var json = File.ReadAllText(dlg.FileName);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg != null)
                {
                    VM.Light.Mode = cfg.LightMode;
                    VM.Light.MoveOff = cfg.MoveOff;
                    VM.Light.BreathSpeed = cfg.BreathSpeed;
                    VM.Light.SleepMinutes = cfg.SleepMinutes;
                    VM.Light.Level1SleepMinutes = cfg.Level1SleepMinutes;
                    VM.Light.DebounceMs = cfg.DebounceMs;
                    VM.Rate.Hz = cfg.RateHz;
                    // 仅加载 UI 暴露的 5 档（DpiRows.Count == 5），避免索引越界
                    for (int i = 0; i < 5 && i < cfg.DpiEnabled.Length; i++)
                    {
                        VM.DpiRows[i].Enabled = cfg.DpiEnabled[i];
                        VM.DpiRows[i].Value = cfg.DpiValues[i].ToString();
                    }
                    if (cfg.ActiveLevel >= 1 && cfg.ActiveLevel <= 5)
                        VM.CurrentLevel = cfg.ActiveLevel;
                    VM.Log($"✓ 已导入配置 {dlg.FileName}");
                }
            }
            catch (Exception ex) { VM.Log("✗ 导入失败: " + ex.Message); }
        }

        private void SaveConfig(AppConfig cfg, string path)
        {
            cfg.LightMode = VM.Light.Mode;
            cfg.MoveOff = VM.Light.MoveOff;
            cfg.BreathSpeed = VM.Light.BreathSpeed;
            cfg.SleepMinutes = VM.Light.SleepMinutes;
            cfg.Level1SleepMinutes = VM.Light.Level1SleepMinutes;
            cfg.DebounceMs = VM.Light.DebounceMs;
            cfg.RateHz = VM.Rate.Hz;
            cfg.DpiEnabled = VM.DpiRows.Select(r => r.Enabled).ToArray();
            cfg.DpiValues = VM.DpiRows.Select(r => int.TryParse(r.Value, out int v) ? v : 0).ToArray();
            cfg.ActiveLevel = (byte)VM.CurrentLevel;
            File.WriteAllText(path, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
        }

        [System.Serializable]
        private class AppConfig
        {
            public int LightMode { get; set; } = 3;
            public bool MoveOff { get; set; } = false;
            public int BreathSpeed { get; set; } = 6;
            public int SleepMinutes { get; set; } = 10;
            public double Level1SleepMinutes { get; set; } = 0.5;
            public int DebounceMs { get; set; } = 6;
            public int RateHz { get; set; } = 500;
            public bool[] DpiEnabled { get; set; } = Enumerable.Range(0, 8).Select(i => i < 5).ToArray();
            public int[] DpiValues { get; set; } = { 800, 1200, 1600, 2400, 4000, 0, 0, 0 };
            public byte ActiveLevel { get; set; } = 1;
        }
        #endregion

        protected override void OnClosed(EventArgs e)
        {
            VM.Dispose();
            base.OnClosed(e);
        }
    }

    // DPI 行视图模型（承载单行 UI 状态，属性通知供 XAML 单向/双向绑定）
    public class DpiRow : INotifyPropertyChanged
    {
        private bool _enabled, _isActive;
        private string _value = "0";
        public string Label { get; set; } = "";
        public string Value { get => _value; set { _value = value; OnChanged(nameof(Value)); } }
        public bool Enabled { get => _enabled; set { _enabled = value; OnChanged(nameof(Enabled)); } }
        public bool IsActive { get => _isActive; set { _isActive = value; OnChanged(nameof(IsActive)); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
