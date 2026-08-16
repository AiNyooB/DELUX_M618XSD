using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DeluxDriver;

/// <summary>
/// 窗口 DWM 背景材质（Mica/Acrylic/Tabbed）+ 明暗标题栏 + 圆角。
/// 参考 MicaWPF（Simnico99）与 .NET 内置 WindowBackdropManager 的实现思路，
/// 自封装 P/Invoke，避免引入第三方 UI 库（AGENTS.md：纯原生 WPF）。
/// 仅 Windows 11 22H2（Build 22621）及更高版本生效；更老系统静默降级为纯色背景。
/// </summary>
public static class DwmBackdrop
{
    /// <summary>背景材质类型（对应 DWM 的 DWMSBT_* 枚举值）。</summary>
    public enum BackdropType
    {
        None = 1,
        Mica = 2,
        Acrylic = 3,
        Tabbed = 4,
    }

    // DWM 窗口属性（dwAttribute 取值，MicaWPF InteropValues + 官方 dwmapi.h）
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    // 圆角偏好
    private const int DWMWCP_ROUND = 2;

    /// <summary>是否 Windows 11 22H2+（Build 22621+），官方 SYSTEMBACKDROP_TYPE 的最低门槛。</summary>
    public static bool IsSupported { get; } = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    /// <summary>
    /// 为窗口应用背景材质，并让材质透出（窗口背景透明化）。
    /// 仅 Windows 11 22H2+ 生效；不支持的系统返回 false，窗口保持调用方设置的纯色背景。
    /// </summary>
    /// <param name="window">目标窗口。</param>
    /// <param name="type">材质类型，默认 Mica。</param>
    /// <param name="isDark">是否深色主题，决定材质着色与标题栏/边框明暗（跟随应用自研主题而非系统）。</param>
    /// <returns>材质是否已成功应用。</returns>
    public static bool Apply(Window window, BackdropType type = BackdropType.Mica, bool isDark = false)
    {
        if (!IsSupported || window is null) return false;

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;

        // 玻璃帧扩展到整个客户区，让 DWM 材质覆盖窗口内容（同内置 WindowBackdropManager.UpdateGlassFrame）。
        var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        // 背景材质：22H2+ 官方属性（MicaWPF / 内置 WindowBackdropManager 同款 DWMWA_SYSTEMBACKDROP_TYPE）。
        // 关键属性，失败则整体放弃，调用方据此降级为纯色背景。
        int backdrop = (int)type;
        if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) != 0)
            return false;

        // 标题栏/边框明暗：跟随应用自研主题（浅/深），保证与自研主题字典一致。
        // 次要属性：失败不影响材质本身，按 best-effort 处理（22621+ 上均支持，正常返回 0）。
        int dark = isDark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
        { /* 标题栏明暗未生效：材质已应用，后续由 WPF 标题栏着色兜底 */ }

        // Win11 默认圆角，显式声明与系统一致（最大化时 DWM 自动直角）。同为次要 best-effort。
        int corner = DWMWCP_ROUND;
        if (DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int)) != 0)
        { /* 圆角未生效：材质已应用，窗口仍可用，不影响主功能 */ }

        // 关键：WPF 合成器背景必须透明，材质才能透出（内置 WindowBackdropManager.RemoveBackground 同款）。
        if (PresentationSource.FromVisual(window) is HwndSource source)
            source.CompositionTarget.BackgroundColor = Colors.Transparent;

        return true;
    }
}
