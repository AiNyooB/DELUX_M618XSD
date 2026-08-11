using Xunit;

namespace MouseDriverClient.Tests;

/// <summary>
/// ButtonConfig（0x08 报告，59 字节，18×3）编解码测试。
/// 布局依据：reference/HID协议逆向报告.md 3.5 节 + Models.cs InitEntries（官方抓包反推）。
/// </summary>
public class ButtonConfigTests
{
    [Fact]
    public void ToBytes_Default_MatchesHeaderAndLength()
    {
        var cfg = new ButtonConfig();

        var r = cfg.ToBytes();

        Assert.Equal(59, r.Length);
        Assert.Equal(0x08, r[0]);
        Assert.Equal(0x3B, r[1]);
        Assert.Equal(0x01, r[2]);
    }

    [Fact]
    public void ToBytes_DefaultEntries_FromOfficialCapture()
    {
        var r = new ButtonConfig().ToBytes();

        // 默认 entry 表（Models.cs InitEntries，来自官方软件抓包反推）
        Assert.Equal(0x02, r[3 + 0 * 3]);   // entry0  左键
        Assert.Equal(0x03, r[3 + 1 * 3]);   // entry1  右键
        Assert.Equal(0x06, r[3 + 2 * 3]);   // entry2  前进
        Assert.Equal(0x05, r[3 + 3 * 3]);   // entry3  后退
        Assert.Equal(0x04, r[3 + 4 * 3]);   // entry4  中键
        Assert.Equal(0x0D, r[3 + 5 * 3]);   // entry5  DPI循环
        Assert.Equal(0x0B, r[3 + 14 * 3]);  // entry14 左滚
        Assert.Equal(0x0C, r[3 + 15 * 3]);  // entry15 右滚
        Assert.Equal(0x09, r[3 + 16 * 3]);  // entry16 上滚
        Assert.Equal(0x0A, r[3 + 17 * 3]);  // entry17 下滚

        // entry6..13 = 标准/未使用
        for (int i = 6; i <= 13; i++)
            Assert.Equal(0x01, r[3 + i * 3]);
    }

    [Fact]
    public void ToBytes_Checksum_MatchesBigEndianSumOf3To56()
    {
        var cfg = new ButtonConfig();
        // 改一个按钮，确保校验和随内容变化
        cfg.Entries[0] = new byte[] { 0x12, 0x00, 0x04 }; // 左键→宏4

        var r = cfg.ToBytes();

        int sum = 0;
        for (int i = 3; i <= 56; i++) sum += r[i];
        Assert.Equal((sum >> 8) & 0xFF, r[57]);
        Assert.Equal(sum & 0xFF, r[58]);
    }

    [Fact]
    public void FromBytes_RoundTrip_PreservesAllEntries()
    {
        var cfg = new ButtonConfig();
        cfg.Entries[5] = new byte[] { 0x12, 0x00, 0x02 }; // DPI循环→宏2

        var parsed = ButtonConfig.FromBytes(cfg.ToBytes());

        for (int i = 0; i < 18; i++)
            Assert.Equal(cfg.Entries[i], parsed.Entries[i]);
    }

    [Fact]
    public void Default_ReturnsIndependentFreshCopy()
    {
        var a = ButtonConfig.Default();
        var b = ButtonConfig.Default();
        a.Entries[0] = new byte[] { 0x12, 0x00, 0x01 };

        // b 不受 a 修改影响
        Assert.Equal(0x02, b.Entries[0][0]);
        // 两次调用不共享同一数组
        Assert.NotSame(a.Entries, b.Entries);
        Assert.NotSame(a.Entries[0], b.Entries[0]);
    }

    [Fact]
    public void FuncCode_ConstantsMatchProtocol()
    {
        Assert.Equal(0x01, ButtonConfig.FuncCode.Standard);
        Assert.Equal(0x02, ButtonConfig.FuncCode.Left);
        Assert.Equal(0x03, ButtonConfig.FuncCode.Right);
        Assert.Equal(0x04, ButtonConfig.FuncCode.Middle);
        Assert.Equal(0x05, ButtonConfig.FuncCode.Back);
        Assert.Equal(0x06, ButtonConfig.FuncCode.Forward);
        Assert.Equal(0x09, ButtonConfig.FuncCode.ScrollUp);
        Assert.Equal(0x0A, ButtonConfig.FuncCode.ScrollDown);
        Assert.Equal(0x0B, ButtonConfig.FuncCode.ScrollLeft);
        Assert.Equal(0x0C, ButtonConfig.FuncCode.ScrollRight);
        Assert.Equal(0x0D, ButtonConfig.FuncCode.DpiCycle);
        Assert.Equal(0x12, ButtonConfig.FuncCode.Macro);
    }
}
