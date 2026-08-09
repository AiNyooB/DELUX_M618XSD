using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace MouseDriverClient
{
    /// <summary>
    /// 宏录制编辑器（UserControl），按键页与宏管理页共用。
    /// 通过 <see cref="Store"/> 指向共享的宏工作区（key=宏ID），
    /// 通过 <see cref="Logger"/> 把录制动作输出到主窗口日志。
    /// </summary>
    public partial class MacroEditor : UserControl
    {
        /// <summary>共享宏工作区：key = 宏 ID(1..6)，按键页与宏页共用同一份。</summary>
        public Dictionary<byte, MacroWorkspace> Store { get; set; } = new();

        /// <summary>日志回调（由主窗口注入）。</summary>
        public Action<string>? Logger { get; set; }

        /// <summary>切换宏 ID 时回调（由主窗口注入），用于同步循环次数等外部输入控件。</summary>
        public Action<byte>? MacroIdChanged { get; set; }

        private bool _showMacroIdSelector = true;
        /// <summary>是否在控件内显示「宏 ID」下拉（按键页有自己的宏 ID 下拉时设为 false）。</summary>
        public bool ShowMacroIdSelector
        {
            get => _showMacroIdSelector;
            set
            {
                _showMacroIdSelector = value;
                if (RowMacroId != null)
                    RowMacroId.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public MacroEditor()
        {
            InitializeComponent();
            CmbMacroMethod.SelectionChanged += CmbMacroMethod_SelectionChanged;
            CmbMacroId.SelectionChanged += CmbMacroId_SelectionChanged;
            Loaded += (_, _) =>
            {
                if (!_showMacroIdSelector) RowMacroId.Visibility = Visibility.Collapsed;
                // 构造期 Store / Logger 可能尚未注入（MainWindow 在 InitializeComponent 之后才注入），
                // 故把首次 EnsureCurrent / Refresh 放到 Loaded，避免访问空 Store / Logger。
                EnsureCurrent();
                Refresh();
                MacroIdChanged?.Invoke(CurrentId);
            };
        }

        /// <summary>当前选中的宏 ID（1..6）。</summary>
        public byte CurrentId => (byte)(CmbMacroId.SelectedIndex + 1);

        /// <summary>当前宏工作区（按需创建）。</summary>
        public MacroWorkspace Current => EnsureCurrent();

        /// <summary>外部联动：把编辑器切到指定宏 ID（按键页的宏 ID 下拉驱动此方法）。</summary>
        public void SetMacroId(byte id)
        {
            if (id >= 1 && id <= 6) { CmbMacroId.SelectedIndex = id - 1; Refresh(); }
        }

        private MacroWorkspace EnsureCurrent()
        {
            byte id = CurrentId;
            if (!Store.ContainsKey(id)) Store[id] = new MacroWorkspace();
            return Store[id];
        }

        private void CmbMacroId_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EnsureCurrent();
            Refresh();
            MacroIdChanged?.Invoke(CurrentId);
        }

        private void CmbMacroMethod_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EnsureCurrent().Method = GetMethod();
        }

        private byte GetMethod()
        {
            // Tag 在 XAML 中可能为 int 也可能为 string，统一解析为字节再映射。
            // 协议语义（HID协议逆向报告.md 3.6 节）：0x00=循环次数、0x01=任意键停止、0x02=按住循环。
            if (CmbMacroMethod.SelectedItem is ComboBoxItem ci)
            {
                byte v = TagToByte(ci.Tag);
                if (v == 0x00 || v == 0x01 || v == 0x02) return v;
            }
            return 0x00; // 默认：循环次数
        }

        private static byte TagToByte(object tag)
        {
            if (tag is int i) return (byte)i;
            if (tag is string s && byte.TryParse(s, out byte b)) return b;
            return 0xFF;
        }

        /// <summary>读取「每步延迟(ms)」全局框；无效时返回 0（该条不默认带延迟）。</summary>
        private int GetGlobalDelayMs()
        {
            if (int.TryParse(TxtDelayMs.Text, out int ms) && ms >= 1) return ms;
            return 0;
        }

        private void BtnMacroKey_Click(object sender, RoutedEventArgs e)
        {
            var input = UiHelper.InputBox(Window.GetWindow(this),
                "输入按键名（A-Z / 0-9 / F1-F12 / LCLK,RCLK,MCLK；可空格分隔多个）", "录制宏按键", "A");
            if (string.IsNullOrWhiteSpace(input)) return;
            var ws = EnsureCurrent();
            int defDelay = GetGlobalDelayMs();
            foreach (var part in input.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                byte code = UiHelper.KeyNameToCode(part);
                if (code == 0)
                {
                    Logger?.Invoke($"⚠ 未知按键 '{part}'，已跳过");
                    continue;
                }
                ws.Actions.Add(new MacroAction { Code = code, Press = true, DelayMs = defDelay }); // 按下
                ws.Actions.Add(new MacroAction { Code = code, Press = false, DelayMs = defDelay }); // 释放
            }
            Refresh();
        }

        /// <summary>编辑选中动作的延迟：弹框输入该动作后的延迟(ms)，留空/0 表示清除延迟。</summary>
        private void BtnEditDelay_Click(object sender, RoutedEventArgs e)
        {
            var ws = EnsureCurrent();
            int idx = LstMacroActions.SelectedIndex;
            if (idx < 0 || idx >= ws.Actions.Count)
            {
                Logger?.Invoke("⚠ 请先在列表中选择要编辑延迟的动作");
                return;
            }
            var a = ws.Actions[idx];
            string cur = a.DelayMs > 0 ? a.DelayMs.ToString() : "";
            var input = UiHelper.InputBox(Window.GetWindow(this),
                "输入该动作后的延迟(ms)，0 表示无延迟", "编辑延迟", cur);
            if (input == null) return; // 取消
            int newMs;
            if (string.IsNullOrWhiteSpace(input.Trim()))
            {
                Logger?.Invoke("⚠ 延迟值无效：请输入正整数毫秒或 0");
                return;
            }
            else if (!int.TryParse(input.Trim(), out newMs) || newMs < 0)
            {
                Logger?.Invoke("⚠ 延迟值无效：请输入非负整数毫秒");
                return;
            }
            a.DelayMs = newMs;
            Refresh();
            Logger?.Invoke($"✓ 已更新第 {idx + 1} 条延迟为 {newMs}ms");
        }

        private void BtnMacroDel_Click(object sender, RoutedEventArgs e)
        {
            var ws = EnsureCurrent();
            int idx = LstMacroActions.SelectedIndex;
            if (idx < 0 || idx >= ws.Actions.Count)
            {
                Logger?.Invoke("⚠ 请先选择要删除的动作");
                return;
            }
            ws.Actions.RemoveAt(idx);
            if (idx < ws.Actions.Count) LstMacroActions.SelectedIndex = idx; // 保持选中下一条
            else if (ws.Actions.Count > 0) LstMacroActions.SelectedIndex = ws.Actions.Count - 1;
            Refresh();
        }

        private void BtnMacroClear_Click(object sender, RoutedEventArgs e)
        {
            var ws = EnsureCurrent();
            ws.Actions.Clear();
            Refresh();
        }

        private void BtnMacroDelay_Click(object sender, RoutedEventArgs e)
        {
            var ws = EnsureCurrent();
            if (!int.TryParse(TxtDelayMs.Text, out int ms) || ms < 1)
            {
                Logger?.Invoke("⚠ 延迟值无效：请输入正整数毫秒（1-65535）");
                return;
            }
            // 延迟作为一个虚拟动作插入：Code=0, Press=false, DelayMs=ms
            // 生成器遇到 Code=0 时只写 flag 的延迟位、keycode 填 0（设备按空操作+延迟处理）。
            ws.Actions.Add(new MacroAction { Code = 0x00, Press = false, DelayMs = ms });
            Refresh();
            Logger?.Invoke($"✓ 已插入延迟 {ms}ms");
        }

        /// <summary>外部联动：把 VM 的 RecordMode 同步到本控件下拉（默认延迟/录制延迟）。</summary>
        public void SyncRecordMode(bool recordMode)
        {
            CmbRecordMode.SelectedIndex = recordMode ? 1 : 0;
        }

        /// <summary>读取本控件当前选择的延迟录制模式（true=录制延迟）。</summary>
        public bool GetRecordMode()
        {
            if (CmbRecordMode.SelectedItem is ComboBoxItem ci)
            {
                byte v = TagToByte(ci.Tag);
                if (v != 0xFF) return v == 1; // Tag=0 默认延迟, Tag=1 录制延迟
            }
            return false;
        }

        /// <summary>延迟模式变更回调（由主窗口注入，写回 VM.RecordMode）。</summary>
        public Action<bool>? RecordModeChanged { get; set; }

        private void CmbRecordMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RecordModeChanged?.Invoke(GetRecordMode());
        }

        private void Refresh()
        {
            var ws = EnsureCurrent();
            LstMacroActions.Items.Clear();
            int i = 1;
            foreach (var a in ws.Actions)
            {
                string kind = a.Press ? "按下" : "释放";
                string delay = $" 延迟={a.DelayMs}ms";
                LstMacroActions.Items.Add($"{i++}. code=0x{a.Code:X2} {kind}{delay}");
            }
        }
    }
}
