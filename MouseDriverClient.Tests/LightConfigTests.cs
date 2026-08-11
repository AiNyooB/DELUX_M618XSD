using Xunit;

namespace MouseDriverClient.Tests;

/// <summary>
/// LightConfig（0x05 报告，15 字节）编解码测试。
/// 布局依据：reference/HID协议逆向报告.md 3.2 节（已验证字段）+ Models.cs（含 byte9 +1 修正注释）。
/// </summary>
public class LightConfigTests
{
    [Fact]
    public void ToBytes_Default_MatchesVerifiedLayout()
    {
        var cfg = new LightConfig();

        var r = cfg.ToBytes();

        Assert.Equal(15, r.Length);
        Assert.Equal(0x05, r[0]);
        Assert.Equal(0x0F, r[1]);
        Assert.Equal(0x01, r[2]);
        Assert.Equal(0x03, r[3]);   // 模式 3（循环呼吸）+ 移动关灯开启(bit7=0)
        Assert.Equal(0x03, r[4]);   // 呼吸速度 6 → 9-6=3
        Assert.Equal(0xA8, r[5]);   // 睡眠 10 分钟 → (10<<4)|8
        Assert.Equal(0x00, r[6]);
        Assert.Equal(0x00, r[7]);
        Assert.Equal(0xFF, r[8]);
        Assert.Equal(0x02, r[9]);   // 一级休眠 0.5 分 → round(0.5*2)+1 = 2
        Assert.Equal(0x03, r[10]);  // 去抖 6ms → 6/2=3
        Assert.Equal(0x01, r[11]);  // 校验和高字节（0x1B2）
        Assert.Equal(0xB2, r[12]);
        Assert.Equal(0x00, r[13]);
        Assert.Equal(0x00, r[14]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void ToBytes_Mode_StoredInLowNibble(int mode)
    {
        var cfg = new LightConfig();
        cfg.Mode = mode;

        var r = cfg.ToBytes();

        Assert.Equal(mode, r[3] & 0x0F);
    }

    [Fact]
    public void ToBytes_ModeOutOfRange_ClampedTo0To4()
    {
        var cfg = new LightConfig();
        cfg.Mode = -1;
        Assert.Equal(0, cfg.ToBytes()[3] & 0x0F);

        cfg.Mode = 7;
        Assert.Equal(4, cfg.ToBytes()[3] & 0x0F);
    }

    [Fact]
    public void ToBytes_MoveOffTrue_SetsBit7()
    {
        var cfg = new LightConfig();
        cfg.MoveOff = true;

        var r = cfg.ToBytes();

        Assert.Equal(0x80 | 0x03, r[3]);
    }

    [Theory]
    [InlineData(4, 0x05)]
    [InlineData(6, 0x03)]
    [InlineData(8, 0x01)]
    public void ToBytes_BreathSpeed_Encodes9MinusSpeed(int speed, int expectedByte4)
    {
        var cfg = new LightConfig();
        cfg.BreathSpeed = speed;

        var r = cfg.ToBytes();

        Assert.Equal(expectedByte4, r[4]);
    }

    [Theory]
    [InlineData(1, 0x18)]
    [InlineData(10, 0xA8)]
    [InlineData(15, 0xF8)]
    public void ToBytes_SleepMinutes_EncodesShiftOr8(int minutes, int expectedByte5)
    {
        var cfg = new LightConfig();
        cfg.SleepMinutes = minutes;

        var r = cfg.ToBytes();

        Assert.Equal(expectedByte5, r[5]);
    }

    [Theory]
    [InlineData(0.5, 2)]
    [InlineData(1.0, 3)]
    [InlineData(2.0, 5)]
    public void ToBytes_Level1Sleep_EncodesRoundMinutesTimes2Plus1(double minutes, int expectedByte9)
    {
        var cfg = new LightConfig();
        cfg.Level1SleepMinutes = minutes;

        var r = cfg.ToBytes();

        Assert.Equal(expectedByte9, r[9]);
    }

    [Theory]
    [InlineData(6, 3)]
    [InlineData(10, 5)]
    [InlineData(1, 0)]
    public void ToBytes_DebounceMs_EncodesHalf(int debounceMs, int expectedByte10)
    {
        var cfg = new LightConfig();
        cfg.DebounceMs = debounceMs;

        var r = cfg.ToBytes();

        Assert.Equal(expectedByte10, r[10]);
    }

    [Fact]
    public void ToBytes_Checksum_MatchesBigEndianSumOf3To10()
    {
        var cfg = new LightConfig();
        cfg.Mode = 4;
        cfg.BreathSpeed = 8;
        cfg.SleepMinutes = 15;
        cfg.Level1SleepMinutes = 2.0;
        cfg.DebounceMs = 10;

        var r = cfg.ToBytes();

        int sum = 0;
        for (int i = 3; i <= 10; i++) sum += r[i];
        Assert.Equal((sum >> 8) & 0xFF, r[11]);
        Assert.Equal(sum & 0xFF, r[12]);
    }
}
