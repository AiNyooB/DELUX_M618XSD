using Xunit;

namespace MouseDriverClient.Tests;

/// <summary>
/// RateConfig（0x06 报告，9 字节）编解码测试。
/// 布局依据：reference/HID协议逆向报告.md 3.3 节（已验证映射表）+ Models.cs。
/// </summary>
public class RateConfigTests
{
    [Fact]
    public void ToBytes_Default_MatchesVerifiedLayout()
    {
        var cfg = new RateConfig();

        var r = cfg.ToBytes();

        Assert.Equal(9, r.Length);
        Assert.Equal(0x06, r[0]);
        Assert.Equal(0x09, r[1]);
        Assert.Equal(0x01, r[2]);
        Assert.Equal(0x02, r[3]);   // 500Hz → idx=2
        Assert.Equal(0xFD, r[4]);   // ~idx = 0xFF-2
        Assert.Equal(0x00, r[5]);
        Assert.Equal(0x00, r[6]);
        Assert.Equal(0x00, r[7]);
        Assert.Equal(0x00, r[8]);
    }

    [Theory]
    [InlineData(125, 0x08, 0xF7)]
    [InlineData(250, 0x04, 0xFB)]
    [InlineData(500, 0x02, 0xFD)]
    [InlineData(1000, 0x01, 0xFE)]
    public void ToBytes_Hz_EncodesIdxAndComplement(int hz, int expectedIdx, int expectedComplement)
    {
        var cfg = new RateConfig();
        cfg.Hz = hz;

        var r = cfg.ToBytes();

        Assert.Equal(expectedIdx, r[3]);
        Assert.Equal(expectedComplement, r[4]);
    }
}
