using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DeluxDriver
{
    /// <summary>
    /// 宏实时录制用低级键盘钩子（WH_KEYBOARD_LL，P/Invoke，零第三方依赖）。
    /// 仅录制期间安装、结束即卸载（AGENTS.md 约定：Hook 不在录制态外常驻）。
    /// 回调线程 = 安装线程的消息循环（WPF Dispatcher），事件在 UI 线程触发。
    /// </summary>
    public sealed class KeyboardRecorder : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId;
        /// <summary>当前按下的虚拟键码（去重用：同键未抬起前的重复 down = 系统 auto-repeat，跳过不录）。
        /// 抬起事件仅当该键曾被收录过 down 才发出（按住状态下开始录制产生的孤儿 up 不录）。</summary>
        private readonly HashSet<int> _pressed = new();

        /// <summary>按键事件（参数：HID Usage ID、是否按下）。</summary>
        public event Action<byte, bool>? KeyEvent;

        public KeyboardRecorder()
        {
            _proc = HookCallback;
        }

        public void Install()
        {
            if (_hookId != IntPtr.Zero) return;
            _pressed.Clear(); // 新会话从零跟踪按键状态
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        }

        public void Uninstall()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        public void Dispose() => Uninstall();

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int vk = (int)k.vkCode;
                bool down = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
                // auto-repeat 去重 + 孤儿 up 过滤（不改变事件链，仅决定是否上报）
                if (down ? !_pressed.Add(vk) : !_pressed.Remove(vk))
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);
                byte? hid = VkToHid(vk);
                if (hid is { } code)
                    KeyEvent?.Invoke(code, down);
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
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
            _ => $"键 0x{hid:X2}",
        };

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
    }
}
