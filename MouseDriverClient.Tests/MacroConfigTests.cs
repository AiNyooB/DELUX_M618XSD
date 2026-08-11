using Xunit;

namespace MouseDriverClient.Tests;

/// <summary>
/// MacroConfig（0x09 报告，131B 内部 → 3×64B 分块）测试。
/// 布局依据：reference/HID协议逆向报告.md 3.6 节 + Models.cs BuildMacroChunksProven（实机验证移植）。
/// </summary>
public class MacroConfigTests
{
    #region EncodeDelay

    [Theory]
    [InlineData(0, 1)]
    [InlineData(50, 1)]
    [InlineData(100, 1)]
    [InlineData(150, 2)]
    [InlineData(200, 2)]
    [InlineData(999, 10)]
    [InlineData(1000, 10)]
    [InlineData(1270, 13)]
    public void EncodeDelay_ShortRange_RoundToCentisecond(int delayMs, int expected)
    {
        Assert.Equal(expected, MacroConfig.EncodeDelay(delayMs));
    }

    [Theory]
    [InlineData(1271, 1)]
    [InlineData(1280, 1)]
    [InlineData(1300, 1)]
    [InlineData(1400, 1)]
    [InlineData(1500, 1)]
    [InlineData(2000, 1)]
    public void EncodeDelay_LongRange_Modulo200(int delayMs, int expected)
    {
        Assert.Equal(expected, MacroConfig.EncodeDelay(delayMs));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(100, 10)]
    [InlineData(150, 10)]
    [InlineData(1000, 50)]
    [InlineData(1270, 65)]
    public void PcInputToActualMs_DeviceDecodeMin10(int pcMs, int expected)
    {
        Assert.Equal(expected, MacroConfig.PcInputToActualMs(pcMs));
    }

    #endregion

    #region MacroAction.ToFlag

    [Fact]
    public void ToFlag_PressNoDelay_Bit7Set()
    {
        var a = new MacroAction { Code = 0x04, Press = true, DelayMs = 0 };
        Assert.Equal(0x80, a.ToFlag());
    }

    [Fact]
    public void ToFlag_ReleaseNoDelay_Zero()
    {
        var a = new MacroAction { Code = 0x04, Press = false, DelayMs = 0 };
        Assert.Equal(0x00, a.ToFlag());
    }

    [Fact]
    public void ToFlag_PressWithDelay_OrsDelayCode()
    {
        var a = new MacroAction { Code = 0x04, Press = true, DelayMs = 1000 };
        // 0x80 | EncodeDelay(1000)=10(0x0A)
        Assert.Equal(0x8A, a.ToFlag());
    }

    [Fact]
    public void ToFlag_ReleaseWithDelay_DelayBitsOnly()
    {
        var a = new MacroAction { Code = 0x04, Press = false, DelayMs = 1000 };
        Assert.Equal(0x0A, a.ToFlag());
    }

    #endregion

    #region BuildMacroChunks

    [Fact]
    public void BuildMacroChunks_ReturnsThree64ByteChunks()
    {
        var actions = new List<MacroAction>
        {
            new() { Code = 0x04, Press = true },
            new() { Code = 0x04, Press = false },
        };

        var chunks = MacroConfig.BuildMacroChunks(1, 0x00, 0x01, actions, loopCount: 3);

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.Equal(64, c.Length));
    }

    [Fact]
    public void BuildMacroChunks_ChunkHeaders_MatchProtocol()
    {
        var actions = new List<MacroAction>
        {
            new() { Code = 0x04, Press = true },
            new() { Code = 0x04, Press = false },
        };

        var chunks = MacroConfig.BuildMacroChunks(1, 0x00, 0x01, actions, loopCount: 3);

        // chunk0/1: [0]=0x09 [1]=0x40 [2]=macroId [3]=块索引
        Assert.Equal(new byte[] { 0x09, 0x40, 0x01, 0x00 }, chunks[0][..4]);
        Assert.Equal(new byte[] { 0x09, 0x40, 0x01, 0x01 }, chunks[1][..4]);
        // chunk2: [1]=0x0C 提交帧标记
        Assert.Equal(new byte[] { 0x09, 0x0C, 0x01, 0x02 }, chunks[2][..4]);
    }

    [Fact]
    public void BuildMacroChunks_PayloadAndCount_PlacedCorrectly()
    {
        // 单个按键 A：按下+释放两条动作
        var actions = new List<MacroAction>
        {
            new() { Code = 0x04, Press = true, DelayMs = 0 },
            new() { Code = 0x04, Press = false, DelayMs = 0 },
        };

        var chunks = MacroConfig.BuildMacroChunks(1, 0x01, 0x01, actions);

        // buf[28]=动作条数=2 → chunk0[4+25]=chunk0[29]
        Assert.Equal(2, chunks[0][29]);
        // buf[29]=recordMode=0x01 → chunk0[30]
        Assert.Equal(0x01, chunks[0][30]);
        // 数据从 buf[30] 起：chunk0[31]=0x04(按下A) chunk0[32]=0x80 chunk0[33]=0x04 chunk0[34]=0x00(释放A)
        Assert.Equal(0x04, chunks[0][31]);
        Assert.Equal(0x80, chunks[0][32]);
        Assert.Equal(0x04, chunks[0][33]);
        Assert.Equal(0x00, chunks[0][34]);
    }

    [Fact]
    public void BuildMacroChunks_PlayMode0_LoopCountWrittenToBuf7()
    {
        var actions = new List<MacroAction> { new() { Code = 0x04, Press = true } };

        // 循环次数 5 → buf[7]=5 → chunk0[4+4]=chunk0[8]
        var chunks = MacroConfig.BuildMacroChunks(1, 0x00, 0x01, actions, loopCount: 5);
        Assert.Equal(5, chunks[0][8]);
    }

    [Fact]
    public void BuildMacroChunks_PlayMode0_LoopZeroClampedToOne()
    {
        var actions = new List<MacroAction> { new() { Code = 0x04, Press = true } };

        var chunks = MacroConfig.BuildMacroChunks(1, 0x00, 0x01, actions, loopCount: 0);
        Assert.Equal(1, chunks[0][8]);
    }

    [Fact]
    public void BuildMacroChunks_PlayMode1_ModifierWrittenToBuf7()
    {
        var actions = new List<MacroAction> { new() { Code = 0x04, Press = true } };

        // 任意键停止播放：buf[7]=modifier(默认0x01) → chunk0[8]
        var chunks = MacroConfig.BuildMacroChunks(1, 0x01, 0x01, actions);
        Assert.Equal(0x01, chunks[0][8]);
    }

    [Fact]
    public void BuildMacroChunks_Checksum_MatchesReassembledBuf()
    {
        var actions = new List<MacroAction>
        {
            new() { Code = 0x04, Press = true, DelayMs = 100 },
            new() { Code = 0x04, Press = false, DelayMs = 100 },
            new() { Code = 0x05, Press = true, DelayMs = 200 },
            new() { Code = 0x05, Press = false, DelayMs = 200 },
        };

        var chunks = MacroConfig.BuildMacroChunks(1, 0x00, 0x01, actions, loopCount: 2);

        // 由分块反组 buf[3..128]：chunk0[4..]=buf[3..], chunk1[4..]=buf[0x3F..], chunk2[4..]=buf[0x7B..]
        int sum = 0;
        for (int i = 4; i < 64; i++) sum += chunks[0][i];
        for (int i = 4; i < 64; i++) sum += chunks[1][i];
        for (int i = 4; i < 10; i++) sum += chunks[2][i]; // buf[0x7B..0x80] 前 6 字节
        sum &= 0xFFFF;

        // 校验和存于 buf[129..130] → chunk2[10..11]
        Assert.Equal((sum >> 8) & 0xFF, chunks[2][10]);
        Assert.Equal(sum & 0xFF, chunks[2][11]);
    }

    [Fact]
    public void BuildMacroChunks_ManyActions_TruncatedAtBuf128()
    {
        // 60 个动作占 120 字节，超过 [30..128] 的 98 字节容量 → 触发截断保护
        var actions = new List<MacroAction>();
        for (int i = 0; i < 60; i++)
            actions.Add(new MacroAction { Code = 0x04, Press = i % 2 == 0 });

        var chunks = MacroConfig.BuildMacroChunks(1, 0x00, 0x01, actions);

        // 不应抛异常，且校验和仍正确
        Assert.Equal(3, chunks.Count);
        int sum = 0;
        for (int i = 4; i < 64; i++) sum += chunks[0][i];
        for (int i = 4; i < 64; i++) sum += chunks[1][i];
        for (int i = 4; i < 10; i++) sum += chunks[2][i];
        sum &= 0xFFFF;
        Assert.Equal((sum >> 8) & 0xFF, chunks[2][10]);
        Assert.Equal(sum & 0xFF, chunks[2][11]);
    }

    #endregion
}
