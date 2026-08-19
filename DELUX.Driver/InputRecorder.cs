using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeluxDriver
{
    /// <summary>
    /// 宏实时录制用低级输入钩子（WH_KEYBOARD_LL + WH_MOUSE_LL，P/Invoke，零第三方依赖）。
    /// 仅录制/插入捕获期间安装、结束即卸载（AGENTS.md 约定：Hook 不在录制态外常驻）。
    /// 回调线程 = 安装线程的消息循环（WPF Dispatcher），事件在 UI 线程触发。
    /// 鼠标钩子会忽略落在自身窗口的点击，避免录制/捕获态下点击本软件按钮被录进去。
    /// </summary>
    public sealed class InputRecorder : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;

        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT pt);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private readonly LowLevelProc _kbdProc;
        private readonly LowLevelProc _mouseProc;
        private IntPtr _kbdHookId;
        private IntPtr _mouseHookId;
        /// <summary>当前按下的输入标识（去重用：同键未抬起前的重复 down = 系统 auto-repeat，跳过不录）。
        /// 抬起事件仅当该输入曾被收录过 down 才发出（按住状态下开始录制产生的孤儿 up 不录）。</summary>
        private readonly HashSet<int> _pressed = new();

        /// <summary>键盘按键事件（参数：HID Usage ID、是否按下）。</summary>
        public event Action<byte, bool>? KeyEvent;

        /// <summary>鼠标按键事件（参数：宏 Code、是否按下）。</summary>
        public event Action<byte, bool>? MouseEvent;

        public InputRecorder()
        {
            _kbdProc = KbdHookCallback;
            _mouseProc = MouseHookCallback;
        }

        public void Install()
        {
            if (_kbdHookId == IntPtr.Zero)
            {
                _pressed.Clear(); // 新会话从零跟踪输入状态
                _kbdHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _kbdProc, GetModuleHandle(null), 0);
            }
            if (_mouseHookId == IntPtr.Zero)
                _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
        }

        public void Uninstall()
        {
            if (_kbdHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_kbdHookId);
                _kbdHookId = IntPtr.Zero;
            }
            if (_mouseHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }
        }

        public void Dispose() => Uninstall();

        private IntPtr KbdHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int vk = (int)k.vkCode;
                bool down = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
                // auto-repeat 去重 + 孤儿 up 过滤（不改变事件链，仅决定是否上报）
                if (down ? !_pressed.Add(vk) : !_pressed.Remove(vk))
                    return CallNextHookEx(_kbdHookId, nCode, wParam, lParam);
                byte? hid = VkToHid(vk);
                if (hid is { } code)
                    KeyEvent?.Invoke(code, down);
            }
            return CallNextHookEx(_kbdHookId, nCode, wParam, lParam);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var m = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                // 忽略落在自身窗口的点击，避免录制/捕获态下点击本软件按钮被误录
                if (IsOwnWindow(m.pt))
                    return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
                int msg = (int)wParam;
                bool down = msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN;
                byte? code = MouseMsgToCode(msg, m.mouseData, out int dedupKey);
                if (code is { } c)
                {
                    // auto-repeat/重复事件去重（不改变事件链，仅决定是否上报）
                    if (down ? !_pressed.Add(dedupKey) : !_pressed.Remove(dedupKey))
                        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
                    MouseEvent?.Invoke(c, down);
                }
            }
            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private static bool IsOwnWindow(POINT p)
        {
            IntPtr hwnd = WindowFromPoint(p);
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            return pid == Process.GetCurrentProcess().Id;
        }

        /// <summary>HID Usage ID → 显示名（与 VkToHid 收录的键一一对应；未知键显示十六进制）。</summary>
        public static string KeyNameOf(byte hid) => hid switch
        {
            >= 4 and <= 29 => ((char)('A' + hid - 4)).ToString(),
            >= 30 and <= 39 => ((char)('0' + hid - 30)).ToString(),
            >= 58 and <= 69 => $"F{hid - 57}",
            40 => "回车",
            41 => "Esc",
            43 => "Tab",
            44 => "空格",
            0xE0 => "Ctrl",
            0xE1 => "Shift",
            0xE2 => "Alt",
            0xE3 => "Win",
            // ⚠️ 鼠标按键显示名：Code 为假设值（见 MouseMsgToCode），抓包确认后此处无需改，仅做展示
            0xF1 => "鼠标左键",
            0xF2 => "鼠标右键",
            0xF3 => "鼠标中键",
            0xF4 => "鼠标后退",
            0xF5 => "鼠标前进",
            _ => $"键 0x{hid:X2}",
        };

        /// <summary>显示名 → HID Usage ID（KeyNameOf 的逆映射，供 UI 编辑键值时回写 Code）。
        /// 未知名返回 null。</summary>
        public static byte? NameToHid(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var n = name.Trim();
            // 字母 A-Z
            if (n.Length == 1 && char.IsAsciiLetterUpper(n[0]))
                return (byte)(n[0] - 'A' + 4);
            // 数字 0-9
            if (n.Length == 1 && char.IsDigit(n[0]))
                return (byte)(n[0] - '0' + 30);
            // F1-F12
            if (n.Length >= 2 && n[0] == 'F' && int.TryParse(n[1..], out int fn) && fn is >= 1 and <= 12)
                return (byte)(fn + 57);
            // 功能键 / 修饰键 / 鼠标键
            return n switch
            {
                "回车" or "Enter" => 40,
                "Esc" or "Escape" => 41,
                "Tab" => 43,
                "空格" or "Space" => 44,
                "Ctrl" or "Control" => 0xE0,
                "Shift" => 0xE1,
                "Alt" => 0xE2,
                "Win" => 0xE3,
                "鼠标左键" or "LeftMouse" => 0xF1,
                "鼠标右键" or "RightMouse" => 0xF2,
                "鼠标中键" or "MiddleMouse" => 0xF3,
                "鼠标后退" or "BackMouse" => 0xF4,
                "鼠标前进" or "ForwardMouse" => 0xF5,
                _ => null,
            };
        }

        /// <summary>Win32 虚拟键码 → HID Usage ID（仅收录可录制的键：字母/数字/F1-F12/常用键/修饰键；\n        /// 未知键返回 null 不录制）。</summary>
        public static byte? VkToHid(int vk) => vk switch
        {
            >= 0x41 and <= 0x5A => (byte)(vk - 0x41 + 4),   // A-Z → HID 4..29
            >= 0x30 and <= 0x39 => (byte)(vk - 0x30 + 30),  // 0-9 → HID 30..39
            >= 0x70 and <= 0x7B => (byte)(vk - 0x70 + 58),  // F1-F12 → HID 58..69
            0x0D => 40,   // 回车
            0x09 => 43,   // Tab
            0x20 => 44,   // 空格
            0x1B => 41,   // Esc
            0x10 => 0xE1, // Shift（左，近似；不区分左右）
            0x11 => 0xE0, // Ctrl（左，近似）
            0x12 => 0xE2, // Alt（左，近似）
            0x5B => 0xE3, // Win（左，近似）
            _ => null,
        };

        /// <summary>鼠标消息 → 宏 Code（按下/释放共用同一 Code）。
        /// ⚠️ 未实机验证：设备宏(0x09)里鼠标键的真实字节未知，先用 0xF1..0xF5（避开键盘 HID 用法区间，\n        /// 保证显示/落盘自洽）。待用官方 Mouse.exe 录含鼠标点击的宏、抓 0x09 字节差分确认后，只改这张表即可。</summary>
        private static byte? MouseMsgToCode(int msg, uint mouseData, out int dedupKey)
        {
            int xb = (int)(mouseData >> 16) & 0xFFFF;
            switch ((msg, xb))
            {
                case (WM_LBUTTONDOWN, _) or (WM_LBUTTONUP, _):
                    dedupKey = 1; return 0xF1;
                case (WM_RBUTTONDOWN, _) or (WM_RBUTTONUP, _):
                    dedupKey = 2; return 0xF2;
                case (WM_MBUTTONDOWN, _) or (WM_MBUTTONUP, _):
                    dedupKey = 3; return 0xF3;
                case (WM_XBUTTONDOWN, 1) or (WM_XBUTTONUP, 1):
                    dedupKey = 4; return 0xF4;
                case (WM_XBUTTONDOWN, 2) or (WM_XBUTTONUP, 2):
                    dedupKey = 5; return 0xF5;
                default:
                    dedupKey = 0; return null;
            }
        }
    }
}
