using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MouseDriverClient
{
    /// <summary>
    /// 通用 UI 辅助：轻量模态输入框 + 按键名→HID 码映射。
    /// 从 MainWindow 提取至此，供 MacroEditor 与 MainWindow 共用。
    /// </summary>
    internal static class UiHelper
    {
        /// <summary>轻量模态输入对话框（避免依赖 Microsoft.VisualBasic）。</summary>
        public static string? InputBox(Window? owner, string prompt, string title, string defaultValue)
        {
            var win = new Window
            {
                Title = title,
                Width = 360, Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White
            };
            var tb = new TextBox { Text = defaultValue, Margin = new Thickness(12), VerticalContentAlignment = VerticalAlignment.Center };
            var ok = new Button { Content = "确定", Width = 80, Height = 28, Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Right };
            var tb2 = new TextBlock { Text = prompt, Margin = new Thickness(12, 12, 12, 0), TextWrapping = TextWrapping.Wrap };
            var sp = new StackPanel();
            sp.Children.Add(tb2);
            sp.Children.Add(tb);
            sp.Children.Add(ok);
            win.Content = sp;
            string? result = null;
            ok.Click += (s, e) => { result = tb.Text; win.DialogResult = true; };
            tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) { result = tb.Text; win.DialogResult = true; } };
            return win.ShowDialog() == true ? result : null;
        }

        /// <summary>
        /// 按键名 → HID Usage ID（仅含本驱动常用的键；未知键返回 0）。
        /// 调用方需先 Trim().ToUpper()。
        /// </summary>
        public static byte KeyNameToCode(string k)
        {
            var map = new Dictionary<string, byte>
            {
                {"A",4},{"B",5},{"C",6},{"D",7},{"E",8},{"F",9},{"G",10},{"H",11},{"I",12},{"J",13},
                {"K",14},{"L",15},{"M",16},{"N",17},{"O",18},{"P",19},{"Q",20},{"R",21},{"S",22},{"T",23},
                {"U",24},{"V",25},{"W",26},{"X",27},{"Y",28},{"Z",29},
                {"1",30},{"2",31},{"3",32},{"4",33},{"5",34},{"6",35},{"7",36},{"8",37},{"9",38},{"0",39},
                {"ENTER",40},{"ESC",41},{"SPACE",44},{"F1",58},{"F2",59},{"F3",60},{"F4",61},{"F5",62},
                {"F6",63},{"F7",64},{"F8",65},{"F9",66},{"F10",67},{"F11",68},{"F12",69},
                {"LCLK",0xEB},{"RCLK",0xEC},{"MCLK",0xED}
            };
            return map.TryGetValue(k, out var v) ? v : (byte)0;
        }
    }
}
