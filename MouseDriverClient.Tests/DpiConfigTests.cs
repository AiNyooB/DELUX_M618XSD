using Xunit;

namespace MouseDriverClient.Tests;

/// <summary>
/// DpiConfig（0x04 报告，56 字节）编解码测试。
/// 布局依据：reference/HID协议逆向报告.md 3.4 节 + Models.cs 注释（已实测验证）。
/// </summary>
public class DpiConfigTests
{
    [Fact]
    public void ToBytes_Default_MatchesHeaderAndFixedFields()
    {
        var cfg = new DpiConfig();

        var r = cfg.ToBytes();

        Assert.Equal(56, r.Length);
        Assert.Equal(0x04, r[0]);   // Report ID
        Assert.Equal(0x38, r[1]);
        Assert.Equal(0x01, r[2]);
        Assert.Equal(0x00, r[3]);
        Assert.Equal(0x00, r[4]);
        Assert.Equal(0x1F, r[5]);   // 前 5 档启用位图（默认）
        Assert.Equal(0x10, r[6]);
        Assert.Equal(0x10, r[7]);
        Assert.Equal(1, r[24]);     // 默认活跃档位
        Assert.Equal(0x02, r[49]);
    }

    [Theory]
    [InlineData(800, 0x20, 0x03)]   // 800 = 0x0320
    [InlineData(1200, 0xB0, 0x04)]  // 1200 = 0x04B0
    [InlineData(2480, 0xB0, 0x09)]  // 2480 = 0x09B0（官方模板 L1）
    [InlineData(1, 0x01, 0x00)]     // 最小值
    [InlineData(0xFFFF, 0xFF, 0xFF)]// 最大值
    public void ToBytes_Levels_StoredLittleEndian(int dpi, int expectedLo, int expectedHi)
    {
        var cfg = new DpiConfig();
        cfg.Levels[0] = dpi;

        var r = cfg.ToBytes();

        Assert.Equal(expectedLo, r[8]);
        Assert.Equal(expectedHi, r[16]);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(8)]
    public void ToBytes_ActiveLevel_WrittenToByte24(int level)
    {
        var cfg = new DpiConfig();
        cfg.ActiveLevel = (byte)level;

        var r = cfg.ToBytes();

        Assert.Equal(level, r[24]);
    }

    [Fact]
    public void ToBytes_LevelOverflow_ClampedToUshort()
    {
        var cfg = new DpiConfig();
        cfg.Levels[0] = 0x10000;

        var r = cfg.ToBytes();

        Assert.Equal(0xFF, r[8]);
        Assert.Equal(0xFF, r[16]);
    }

    [Fact]
    public void ToBytes_Checksum_MatchesBigEndianSumOf3To49()
    {
        var cfg = new DpiConfig();
        cfg.Levels[0] = 800;
        cfg.Levels[1] = 1600;
        cfg.ActiveLevel = 3;

        var r = cfg.ToBytes();

        int sum = 0;
        for (int i = 3; i <= 49; i++) sum += r[i];
        Assert.Equal((sum >> 8) & 0xFF, r[50]);
        Assert.Equal(sum & 0xFF, r[51]);
    }

    [Fact]
    public void FromBytes_RoundTrip_PreservesLevelsAndFlags()
    {
        var cfg = new DpiConfig();
        cfg.Levels[0] = 800;
        cfg.Levels[1] = 1200;
        cfg.Levels[4] = 4000;
        cfg.ActiveLevel = 4;
        cfg.EnabledBitmap = 0x1F;

        var parsed = DpiConfig.FromBytes(cfg.ToBytes());

        Assert.Equal(cfg.EnabledBitmap, parsed.EnabledBitmap);
        Assert.Equal(cfg.ActiveLevel, parsed.ActiveLevel);
        for (int i = 0; i < 8; i++)
            Assert.Equal(cfg.Levels[i], parsed.Levels[i]);
    }

    [Fact]
    public void FromBytes_ReadsLittleEndianLevels()
    {
        // 构造：档位1=0x0320(800)，档位2=0x04B0(1200)，其余 0
        var r = new byte[56];
        r[0] = 0x04;
        r[8] = 0x20; r[16] = 0x03;
        r[9] = 0xB0; r[17] = 0x04;
        r[24] = 2;
        r[5] = 0x1F;

        var parsed = DpiConfig.FromBytes(r);

        Assert.Equal(800, parsed.Levels[0]);
        Assert.Equal(1200, parsed.Levels[1]);
        Assert.Equal(0, parsed.Levels[7]);
        Assert.Equal(2, parsed.ActiveLevel);
        Assert.Equal(0x1F, parsed.EnabledBitmap);
    }
}
