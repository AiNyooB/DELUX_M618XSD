using System;
using System.Collections.Generic;

namespace DeluxDriver
{
    /// <summary>设备身份（用于硬件握手前缀）。</summary>
    public enum DeviceId : byte
    {
        Unknown = 0,
        /// <summary>2.4G 无线设备（M618XSD 本体）。</summary>
        Wireless2_4G = 1,
        /// <summary>蓝牙设备。</summary>
        Bluetooth = 2,
    }

    /// <summary>
    /// DPI 配置（0x04 报告，56 字节）。布局与编解码 1:1 来自 MouseDriverClient/Models.cs 实机验证版
    /// （HID协议逆向报告.md 3.4 节：r[5]=启用位图、r[8..23]=8×16 位小端值、r[24]=活跃档位、
    /// r[50..51]=校验和 sum[3..49]）。
    /// </summary>
    public class DpiConfig
    {
        public const int ReportId = 0x04;
        public const int Length = 56;

        // 8 个档位槽位（固件 0x04 报告内存布局固定为 8 槽）。
        // M618XSD 本机实际只有 5 个可用档位（800/1200/1600/2400/4000），官方软件 6-8 档隐藏不启用，
        // 故：UI 只暴露前 5 档，槽位 6/7/8 固定为 0，保持协议字节布局正确。
        public int[] Levels { get; set; } = new int[8];

        /// <summary>档位启用位图：位 0..7 = 档位 1..8，1 表示启用。默认仅前 5 档启用（0x1F）。</summary>
        public byte EnabledBitmap { get; set; } = 0x1F;

        /// <summary>当前活跃档位索引（1..8，1=800 等）。UI 只允许在 1..5 内选择。</summary>
        public byte ActiveLevel { get; set; } = 1;

        /// <summary>解析读回的 0x04 报告（主动读档位不可行，见 AGENTSK 2.4；仅用于回显自检）。</summary>
        public static DpiConfig FromBytes(byte[] r)
        {
            var cfg = new DpiConfig();
            cfg.EnabledBitmap = r[5];
            cfg.ActiveLevel = r[24];
            for (int i = 0; i < 8; i++)
                cfg.Levels[i] = r[8 + i] | (r[16 + i] << 8);
            return cfg;
        }

        /// <summary>序列化为 56 字节 0x04 报告（含校验和 r[50..51] = sum(报告[3..49])）。</summary>
        public byte[] ToBytes()
        {
            var r = new byte[Length];
            r[0] = ReportId;
            r[1] = 0x38;
            r[2] = 0x01;
            r[3] = 0x00;
            r[4] = 0x00;
            r[5] = EnabledBitmap;
            r[6] = 0x10; // 未知状态字节（实测常见 10 10 / 11 11）
            r[7] = 0x10;
            // 写入 8 个槽位（固件内存布局）。槽位 6/7/8 由 UI 逻辑固定为 0（本机不启用）。
            for (int i = 0; i < 8; i++)
            {
                int v = Math.Clamp(Levels[i], 0, 0xFFFF);
                r[8 + i] = (byte)(v & 0xFF);
                r[16 + i] = (byte)((v >> 8) & 0xFF);
            }
            r[24] = ActiveLevel;
            r[49] = 0x02;
            int sum = 0;
            for (int i = 3; i <= 49; i++) sum += r[i];
            r[50] = (byte)((sum >> 8) & 0xFF);
            r[51] = (byte)(sum & 0xFF);
            return r;
        }
    }

    /// <summary>灯光（呼吸/常亮/关闭/流光）+ 电源管理 + 去抖（依据：HID协议逆向报告.md 0x05 报告）。</summary>
    public class LightConfig
    {
        /// <summary>灯光模式：1=呼吸 2=常亮 3=关闭 4=流光（枚举值来自 Wiki）。</summary>
        public byte Mode { get; set; } = 1;
        public bool MoveOff { get; set; } = false;
        /// <summary>呼吸速度档位（1..8，UI 直接展示档位）。</summary>
        public int BreathSpeed { get; set; } = 4;
        /// <summary>睡眠时间（分钟，1..60）。</summary>
        public int SleepMinutes { get; set; } = 10;
        /// <summary>一级休眠（分钟，0.5..60）。</summary>
        public int Level1SleepMinutes { get; set; } = 5;
        /// <summary>去抖时间（毫秒，1..25）。</summary>
        public int DebounceMs { get; set; } = 8;
    }

    public class RateConfig
    {
        /// <summary>回报率（Hz）：125 / 250 / 500 / 1000。</summary>
        public int Hz { get; set; } = 500;
    }

    /// <summary>
    /// 按键映射（0x08 报告，59 字节）。布局与编解码 1:1 来自 MouseDriverClient/Models.cs 实机验证版
    /// （HID协议逆向报告.md 3.5 节：`08 3B 01` + 18×3 条目 r[3..56] + 校验和 r[57..58]=sum[3..56]）。
    /// 0x08 为整表覆写、无增量、不可读 → 本地维护全表副本，改一项后整表写出。
    /// </summary>
    public class ButtonConfig
    {
        public const int ReportId = 0x08;
        public const int Length = 59;

        // 18 个按钮条目，每条 3 字节（[0]=功能编码 [1]=参数1 [2]=参数2，宏时参数2=宏 ID）。
        public byte[][] Entries { get; set; } = InitEntries();

        /// <summary>
        /// 默认 entry 表（来自官方软件抓包反推，非推断）：
        /// entry[0]=左键 02  entry[1]=右键 03  entry[2]=前进 06  entry[3]=后退 05（真实出厂默认，非宏）
        /// entry[4]=中键 04  entry[5]=DPI循环 0d  entry[6..13]=标准/未使用 01
        /// entry[14]=左滚 0b entry[15]=右滚 0c entry[16]=上滚 09 entry[17]=下滚 0a
        /// </summary>
        public static byte[][] InitEntries()
        {
            return new byte[18][]
            {
                new byte[]{0x02,0x00,0x00}, // 0  左键
                new byte[]{0x03,0x00,0x00}, // 1  右键
                new byte[]{0x06,0x00,0x00}, // 2  前进
                new byte[]{0x05,0x00,0x00}, // 3  后退（真实出厂默认，非宏）
                new byte[]{0x04,0x00,0x00}, // 4  中键
                new byte[]{0x0d,0x00,0x00}, // 5  DPI循环
                new byte[]{0x01,0x00,0x00}, // 6
                new byte[]{0x01,0x00,0x00}, // 7
                new byte[]{0x01,0x00,0x00}, // 8
                new byte[]{0x01,0x00,0x00}, // 9
                new byte[]{0x01,0x00,0x00}, // 10
                new byte[]{0x01,0x00,0x00}, // 11
                new byte[]{0x01,0x00,0x00}, // 12
                new byte[]{0x01,0x00,0x00}, // 13
                new byte[]{0x0b,0x00,0x00}, // 14 左滚
                new byte[]{0x0c,0x00,0x00}, // 15 右滚
                new byte[]{0x09,0x00,0x00}, // 16 上滚
                new byte[]{0x0a,0x00,0x00}, // 17 下滚
            };
        }

        /// <summary>解析读回的 0x08 报告（设备不支持读按键表，仅用于回显自检）。</summary>
        public static ButtonConfig FromBytes(byte[] r)
        {
            var cfg = new ButtonConfig();
            for (int i = 0; i < 18; i++)
                cfg.Entries[i] = new byte[] { r[3 + i * 3], r[4 + i * 3], r[5 + i * 3] };
            return cfg;
        }

        /// <summary>序列化为 59 字节 0x08 报告（含校验和 r[57..58] = sum(报告[3..56])）。</summary>
        public byte[] ToBytes()
        {
            var r = new byte[Length];
            r[0] = ReportId;
            r[1] = 0x3B;
            r[2] = 0x01;
            for (int i = 0; i < 18; i++)
            {
                r[3 + i * 3] = Entries[i][0];
                r[4 + i * 3] = Entries[i][1];
                r[5 + i * 3] = Entries[i][2];
            }
            int sum = 0;
            for (int i = 3; i <= 56; i++) sum += r[i];
            sum &= 0xFFFF;
            r[57] = (byte)((sum >> 8) & 0xFF);
            r[58] = (byte)(sum & 0xFF);
            return r;
        }

        /// <summary>按钮功能编码（HID协议逆向报告.md 3.5 节，✅ 已实机验证的 12 个）。</summary>
        public static class FuncCode
        {
            public const byte Standard = 0x01;
            public const byte Left = 0x02;
            public const byte Right = 0x03;
            public const byte Middle = 0x04;
            public const byte Back = 0x05;
            public const byte Forward = 0x06;
            public const byte ScrollUp = 0x09;
            public const byte ScrollDown = 0x0A;
            public const byte ScrollLeft = 0x0B;
            public const byte ScrollRight = 0x0C;
            public const byte DpiCycle = 0x0D;
            public const byte Macro = 0x12;
        }
    }

    /// <summary>
    /// 宏单步动作：一个按键事件（按下或抬起）。
    /// code = HID Usage ID（键盘键码；鼠标键码在宏中未逆向验证，UI 不提供，见 AGENTSK 6 节）。
    /// DelayMs = **设备实际生效延迟**（ms，5 的倍数，10..635 有效；0 = 无延迟）。
    /// </summary>
    /// <remarks>
    /// 延迟语义（ADR macro-delay-semantics）：直接以「设备实际生效」为输入，
    /// 编码 byte = round(ms/5)（1..127），与设备端解码 max(10ms, byte×5ms)（2026-08-09 实测）严格互逆。
    /// 不用 Phase 2 的 PC 输入语义（EncodeDelay）——其 &gt;1270ms 分支为退化公式（只产出 byte 1..2），
    /// 实际只能表达 ≤65ms，无法承载真实宏的 100ms+ 延迟。
    /// </remarks>
    public class MacroAction
    {
        public byte Code { get; set; }
        public bool Press { get; set; } = true;
        public int DelayMs { get; set; }

        /// <summary>构造 flag|delay 字节：bit7=按下/抬起，bits0-6=延迟编码 byte（1..127）。</summary>
        public byte ToFlag()
        {
            byte flag = (byte)(Press ? 0x80 : 0x00);
            if (DelayMs > 0)
                flag |= (byte)MacroConfig.ActualDelayToByte(DelayMs);
            return flag;
        }
    }

    /// <summary>
    /// 宏配置（0x09 协议，HID协议逆向报告.md 3.6 节）。
    /// 编码 1:1 移植 MouseDriverClient 实机验证版（BuildMacroChunksProven）：
    /// 131 字节命令缓冲 → 线上 3×64 分块（chunk0/1 头 0x40、chunk2 头 0x0C 提交帧，0.2s 间隔发送）。
    /// </summary>
    public class MacroConfig
    {
        /// <summary>设备宏槽位 1..6（与 0x08 按键映射 entry[2] 的宏 ID 对应）。
        /// 0 = 尚未分配槽位（新建宏未保存到设备前；保存时映射到空闲槽）。</summary>
        public int Id { get; set; } = 0;

        /// <summary>宏名称（上位机可读性；设备只认槽位 Id，名称随本地持久化保存）。</summary>
        public string Name { get; set; } = "";

        /// <summary>播放方式：0x00=循环次数 0x01=任意键停止 0x02=按住循环（HID协议逆向报告.md 3.6 节，实机验证）。</summary>
        public int Method { get; set; }

        /// <summary>循环次数（仅 Method=0x00 生效；0 被固件 clamp 为 1）。</summary>
        public int LoopCount { get; set; } = 1;

        public List<MacroAction> Actions { get; set; } = new();

        /// <summary>设备实际延迟(ms) → 延迟编码 byte：clamp(round(ms/5), 1, 127)。</summary>
        public static int ActualDelayToByte(int ms)
        {
            int b = (int)Math.Round(ms / 5.0, MidpointRounding.AwayFromZero);
            return Math.Clamp(b, 1, 127);
        }

        /// <summary>PC 端输入延迟 → 设备实际生效延迟（Phase 2 语义，保留作参考/换算：
        /// byte = EncodeDelay(pcMs)，设备实际 = max(10, byte×5)。本产品 UI 用直接字节编码，不再走此路径）。</summary>
        public static int PcInputToActualMs(int pcMs)
        {
            int b = EncodeDelay(pcMs);
            return Math.Max(10, b * 5);
        }

        /// <summary>PC 端延迟编码（反汇编 0x004182B0 确认，1:1 移植 macro_generator.py）：
        /// ≤1270ms: round(ms/100)；&gt;1270ms: round((ms%200)/100)——后者为退化分支，仅产出 1..2。</summary>
        public static int EncodeDelay(int delayMs)
        {
            int encoded = delayMs <= 1270
                ? (int)(delayMs / 100.0 + 0.5)
                : (int)((delayMs % 200) / 100.0 + 0.5);
            return Math.Max(1, encoded);
        }

        /// <summary>组装 131 字节命令缓冲（内部布局：buf[2]=宏ID buf[3]=播放方式 buf[7]=循环次数/修饰键
        /// buf[28]=动作条数 buf[29]=延迟模式 buf[30..128]=[keycode, flag|delay]×N 校验和 buf[129..130]）。</summary>
        public byte[] BuildCommandBuffer(byte recordMode = 0x01)
        {
            var buf = new byte[131];
            buf[0] = 0x09;
            buf[2] = (byte)(Id & 0xFF);
            buf[3] = (byte)(Method & 0xFF);
            // internal[7] 双重用途：循环次数播放 → 次数（0 clamp 1）；任意键停止/按住循环 → 修饰键（官方 0x01）
            buf[7] = (byte)(Method == 0x00 ? Math.Clamp(LoopCount, 1, 255) : 0x01);
            buf[29] = recordMode; // 0x01=Default Delay（未改默认值） 0x07=Record Delay
            int offset = 30;
            foreach (var a in Actions)
            {
                if (offset + 1 >= 129) break; // 容量保护：数据区 [30..128]
                buf[offset] = a.Code;
                buf[offset + 1] = a.ToFlag();
                offset += 2;
            }
            // 动作条数 = 实际写入的对数（而非 Actions.Count）：超 49 步时若写原始条数，
            // 固件按 count 读取会越过数据区读到校验和/越界（UI 已封顶 49，此处防御性修正）
            buf[28] = (byte)((offset - 30) / 2);
            int sum = 0;
            for (int i = 3; i <= 128; i++) sum += buf[i];
            buf[129] = (byte)((sum >> 8) & 0xFF);
            buf[130] = (byte)(sum & 0xFF);
            return buf;
        }

        /// <summary>拆分为线上 3×64 分块（每块头 [0]=0x09 [1]=0x40/0x40/0x0C [2]=宏ID [3]=块索引）。</summary>
        public List<byte[]> BuildMacroChunks(byte recordMode = 0x01)
        {
            var buf = BuildCommandBuffer(recordMode);
            var ch = new byte[3][];
            for (int c = 0; c < 3; c++) ch[c] = new byte[64];
            ch[0][0] = 0x09; ch[0][1] = 0x40; ch[0][2] = (byte)Id; ch[0][3] = 0;
            for (int i = 0; i < 60; i++) ch[0][4 + i] = buf[3 + i];
            ch[1][0] = 0x09; ch[1][1] = 0x40; ch[1][2] = (byte)Id; ch[1][3] = 1;
            for (int i = 0; i < 60; i++) ch[1][4 + i] = buf[0x3F + i];
            ch[2][0] = 0x09; ch[2][1] = 0x0C; ch[2][2] = (byte)Id; ch[2][3] = 2;
            for (int i = 0; i < 8; i++) ch[2][4 + i] = buf[0x7B + i];
            return new List<byte[]> { ch[0], ch[1], ch[2] };
        }
    }

    public class BatteryInfo
    {
        public int Percent { get; set; } = -1;
        public bool Charging { get; set; }
        public bool Valid => Percent >= 0;
    }

    #region 协议常量与编解码

    /// <summary>协议相关常量（来自 HID协议逆向报告.md）。</summary>
    public static class ProtocolConsts
    {
        public const int VID = 0x1D57;
        public const int PID = 0xFA60;
        public const int REPORT_LEN = 64;

        // 各报告的校验和覆盖区间 [from, to]（闭合区间，字节下标）
        public const int DPI_CHECKSUM_FROM = 3;
        public const int DPI_CHECKSUM_TO = 49;
        public const int LIGHT_CHECKSUM_FROM = 3;
        public const int LIGHT_CHECKSUM_TO = 10;
        public const int BUTTON_CHECKSUM_FROM = 3;
        public const int BUTTON_CHECKSUM_TO = 57;
        public const int MACRO_CHECKSUM_FROM = 3;
        public const int MACRO_CHECKSUM_TO = 129; // 仅用于 0x09 长报告

        // 命令码
        public const byte CMD_DPI = 0x04;
        public const byte CMD_LIGHT = 0x05;
        public const byte CMD_RATE = 0x06;
        public const byte CMD_BUTTON = 0x08;
        public const byte CMD_MACRO = 0x09;
        public const byte CMD_WAKE = 0x0C;
        public const byte CMD_DATA_OPEN = 0x0A;

        // 设备身份约定（硬件握手前缀）
        public const byte DEVICE_TYPE = (byte)DeviceId.Wireless2_4G; // 0x01
        public const byte DEVICE_SEQ = 0x00;
    }

    /// <summary>
    /// 校验和：16 位累加、大端。
    /// 覆盖区间 sum = Σ buf[from..to]，校验和写在 buf[1..2]（或报告指定位置）。
    /// </summary>
    public static class Checksum
    {
        /// <summary>计算 [from, to] 区间的 16 位累加和（大端写入 buf[1], buf[2]）。</summary>
        public static void Fill(byte[] buf, int from, int to)
        {
            int sum = 0;
            for (int i = from; i <= to && i < buf.Length; i++)
                sum += buf[i];
            buf[1] = (byte)((sum >> 8) & 0xFF);
            buf[2] = (byte)(sum & 0xFF);
        }

        /// <summary>校验 buf[1..2] 是否等于 [from..to] 累加和（用于读取时自检）。</summary>
        public static bool Verify(byte[] buf, int from, int to)
        {
            int sum = 0;
            for (int i = from; i <= to && i < buf.Length; i++)
                sum += buf[i];
            int cs = (buf[1] << 8) | buf[2];
            return cs == (sum & 0xFFFF);
        }
    }

    /// <summary>编解码工具（主键映射 / 宏序列）。</summary>
    public static class Codecs
    {
        // ---------- DPI（0x04） ----------
        // 0x04 编解码见上方 DpiConfig.ToBytes()/FromBytes()（实机验证版布局，勿用旧式子命令假设）。

        // ---------- 灯光 + 电源（0x05） ----------
        /// <summary>编码灯光/电源/去抖为 0x05 报告（整报告，一次写全）。</summary>
        public static byte[] EncodeLight(LightConfig cfg)
        {
            var buf = new byte[ProtocolConsts.REPORT_LEN];
            buf[0] = ProtocolConsts.CMD_LIGHT;
            buf[3] = 0x01; // 子命令：灯光设置
            buf[4] = ProtocolConsts.DEVICE_TYPE;
            buf[5] = ProtocolConsts.DEVICE_SEQ;
            buf[7] = cfg.Mode;             // 灯光模式
            buf[8] = cfg.MoveOff ? (byte)1 : (byte)0; // 移动时关灯
            buf[9] = (byte)cfg.BreathSpeed; // 呼吸速度
            // 电源管理（byte5/byte9 在报告尾部，按 Wiki：电源区字节）
            buf[36] = (byte)cfg.SleepMinutes;           // 睡眠（分钟）
            buf[40] = (byte)(cfg.Level1SleepMinutes * 2); // 一级休眠（半分钟单位）
            // 去抖（byte3 电源区的去抖字段，按 Wiki：去抖 1..25ms）
            buf[43] = (byte)cfg.DebounceMs;
            Checksum.Fill(buf, ProtocolConsts.LIGHT_CHECKSUM_FROM, ProtocolConsts.LIGHT_CHECKSUM_TO);
            return buf;
        }

        // ---------- 回报率（0x06） ----------
        /// <summary>编码回报率为 0x06 报告（idx = 1000/Hz）。</summary>
        public static byte[] EncodeRate(int hz)
        {
            var buf = new byte[ProtocolConsts.REPORT_LEN];
            buf[0] = ProtocolConsts.CMD_RATE;
            buf[3] = 0x01;
            buf[4] = ProtocolConsts.DEVICE_TYPE;
            buf[5] = ProtocolConsts.DEVICE_SEQ;
            int idx = 1000 / hz; // 125→8, 250→4, 500→2, 1000→1
            buf[6] = (byte)idx;
            Checksum.Fill(buf, 3, 9);
            return buf;
        }

        // ---------- 按键映射（0x08，整表覆写） ----------
        // 0x08 编解码见上方 ButtonConfig.ToBytes()/FromBytes()（实机验证版布局，勿用旧式子命令假设）。

        /// <summary>0x0A 打开数据设备（电源管理前置）。</summary>
        public static byte[] EncodeDataOpen()
        {
            var buf = new byte[ProtocolConsts.REPORT_LEN];
            buf[0] = ProtocolConsts.CMD_DATA_OPEN;
            buf[3] = 0x01;
            buf[4] = ProtocolConsts.DEVICE_TYPE;
            buf[5] = ProtocolConsts.DEVICE_SEQ;
            Checksum.Fill(buf, 3, 49);
            return buf;
        }

        /// <summary>唤醒序列（0x0C）。</summary>
        public static byte[] EncodeWake()
        {
            var buf = new byte[ProtocolConsts.REPORT_LEN];
            buf[0] = ProtocolConsts.CMD_WAKE;
            buf[3] = 0x01;
            buf[4] = ProtocolConsts.DEVICE_TYPE;
            buf[5] = ProtocolConsts.DEVICE_SEQ;
            Checksum.Fill(buf, 3, 49);
            return buf;
        }

        // ---------- 宏（0x09，分块） ----------
        // 0x09 编解码见上方 MacroConfig.BuildCommandBuffer / BuildMacroChunks（1:1 移植 MouseDriverClient 实机验证版）。
        // ⚠️ 旧版 Codecs.EncodeMacro（4 字节记录 + 0xFE 结束标记）未实机验证，已移除。
    }

    #endregion
}

// 独立的轻量 IO 选项容器（HidComm 需要）
namespace DeluxDriver
{
    /// <summary>通信层可调选项（默认值来自 AGENTSK 实测经验）。</summary>
    public class HidOptions
    {
        /// <summary>每个命令之间的间隔（毫秒）。</summary>
        public int CommandIntervalMs { get; set; } = 200;
    }
}
