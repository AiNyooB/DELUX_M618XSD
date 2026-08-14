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

    /// <summary>DPI 档位模型（依据：HID协议逆向报告.md 0x04 报告）。</summary>
    public class DpiLevel
    {
        public bool Enabled { get; set; }
        public int Value { get; set; }
        public int Index { get; set; }

        public string Label => $"档位 {Index + 1}";
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

    /// <summary>按键映射模型：一个物理键 → 一个功能码（0x08）。</summary>
    public class ButtonConfig
    {
        /// <summary>物理按键 HID Usage ID（0=无操作；其余见 Wiki 按键功能清单）。</summary>
        public byte PhysicalButton { get; set; }
        /// <summary>功能码（0=默认/无；其余见 Wiki 按键功能清单）。</summary>
        public byte Function { get; set; }
        /// <summary>参数（功能相关；例如宏 ID）。</summary>
        public byte Param { get; set; }
    }

    /// <summary>宏单步动作（来自 HID协议逆向报告.md 0x09 报告）。</summary>
    public class MacroStep
    {
        public byte Event { get; set; } // 按下=1, 抬起=2
        public byte Code { get; set; }
        public int DelayMs { get; set; }
    }

    public class MacroConfig
    {
        public int Id { get; set; }
        public int Loop { get; set; } = 0;   // 循环次数（0 表示不循环，固件当 1 处理）
        public List<MacroStep> Steps { get; set; } = new();
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
        /// <summary>把 7 档 DPI 编码为 0x04 报告（56 字节体，补齐 64）。</summary>
        public static byte[] EncodeDpi(List<DpiLevel> levels, int activeIndex)
        {
            var buf = new byte[ProtocolConsts.REPORT_LEN];
            buf[0] = ProtocolConsts.CMD_DPI;
            buf[3] = 0x09; // 子命令：设置 DPI
            buf[4] = ProtocolConsts.DEVICE_TYPE;
            buf[5] = ProtocolConsts.DEVICE_SEQ;
            // 7 档，每档 3 字节：高、低、使能
            for (int i = 0; i < 7; i++)
            {
                var lv = levels[i];
                int v = lv.Value;
                buf[6 + i * 2] = (byte)(v >> 8);     // 高字节
                buf[7 + i * 2] = (byte)(v & 0xFF);   // 低字节
                buf[20 + i] = lv.Enabled ? (byte)0x01 : (byte)0x00;
            }
            buf[24] = (byte)activeIndex; // 活跃档位
            Checksum.Fill(buf, ProtocolConsts.DPI_CHECKSUM_FROM, ProtocolConsts.DPI_CHECKSUM_TO);
            return buf;
        }

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
        /// <summary>编码全部 18 个按键为 0x08 报告（整表覆写）。</summary>
        public static byte[] EncodeButtons(List<ButtonConfig> buttons)
        {
            var buf = new byte[ProtocolConsts.REPORT_LEN];
            buf[0] = ProtocolConsts.CMD_BUTTON;
            buf[3] = 0x01;
            buf[4] = ProtocolConsts.DEVICE_TYPE;
            buf[5] = ProtocolConsts.DEVICE_SEQ;
            // 18 个键，每个 3 字节：物理键、功能、参数
            for (int i = 0; i < buttons.Count && i < 18; i++)
            {
                var b = buttons[i];
                buf[6 + i * 3] = b.PhysicalButton;
                buf[7 + i * 3] = b.Function;
                buf[8 + i * 3] = b.Param;
            }
            Checksum.Fill(buf, ProtocolConsts.BUTTON_CHECKSUM_FROM, ProtocolConsts.BUTTON_CHECKSUM_TO);
            return buf;
        }

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
        /// <summary>把宏拆分为多个 0x09 分块（每块 ≤ 数据区）。</summary>
        public static List<byte[]> EncodeMacro(MacroConfig macro)
        {
            // 每条记录 4 字节：事件、键码、延迟高、延迟低
            var records = new List<byte>();
            foreach (var s in macro.Steps)
            {
                records.Add(s.Event);
                records.Add(s.Code);
                records.Add((byte)(s.DelayMs >> 8));
                records.Add((byte)(s.DelayMs & 0xFF));
            }
            // 每分块放 15 条记录（60 字节），最后一条记录后加结束标记 0xFE
            var chunks = new List<byte[]>();
            int i = 0;
            while (i < records.Count)
            {
                var buf = new byte[ProtocolConsts.REPORT_LEN];
                buf[0] = ProtocolConsts.CMD_MACRO;
                buf[3] = 0x09; // 子命令：宏数据
                buf[4] = ProtocolConsts.DEVICE_TYPE;
                buf[5] = ProtocolConsts.DEVICE_SEQ;
                buf[6] = (byte)macro.Id;
                buf[7] = (byte)macro.Loop;
                int count = Math.Min(15, records.Count - i);
                for (int k = 0; k < count; k++)
                    buf[8 + k] = records[i + k];
                if (i + count >= records.Count)
                    buf[8 + count] = 0xFE; // 结束标记
                Checksum.Fill(buf, ProtocolConsts.MACRO_CHECKSUM_FROM, ProtocolConsts.MACRO_CHECKSUM_TO);
                chunks.Add(buf);
                i += count;
            }
            return chunks;
        }
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
