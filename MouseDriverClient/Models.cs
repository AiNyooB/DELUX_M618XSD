using System;

namespace MouseDriverClient
{
    /// <summary>
    /// DPI 配置（0x04 报告，56 字节）。布局来自 AGENTS.md 2.4 节（已实测验证）。
    /// </summary>
    public class DpiConfig
    {
        public const int ReportId = 0x04;
        public const int Length = 56;

        // 8 个档位槽位（固件 0x04 报告内存布局固定为 8 槽，AGENTS.md 2.2 节）。
        // 注意：M618XSD 本机实际只有 5 个可用档位（800/1200/1600/2400/4000），
        // 官方软件中 6-8 档隐藏不启用（AGENTS.md 2.1 节）。因此：
        //   - UI 只暴露前 5 档给用户编辑（见 MainViewModel.DpiRows，仅生成 5 行）；
        //   - 槽位 6/7/8 在此固定为 0，不参与 UI 编辑，保持协议字节布局正确。
        public int[] Levels { get; set; } = new int[8];
        // 档位启用位图：位 0..7 = 档位 1..8，1 表示启用。默认仅前 5 档启用（0x1F）。
        public byte EnabledBitmap { get; set; } = 0x1F; // 默认前 5 档启用（UI 仅暴露 5 档）
        // 当前活跃档位索引（1..8，1=800 等）。UI 只允许在 1..5 内选择（见 LevelOptions）。
        public byte ActiveLevel { get; set; } = 1;

        /// <summary>
        /// 由字节数组解析（读回设备配置时用）。
        /// </summary>
        public static DpiConfig FromBytes(byte[] r)
        {
            var cfg = new DpiConfig();
            cfg.EnabledBitmap = r[5];
            cfg.ActiveLevel = r[24];
            // 读取 8 个槽位（固件内存布局）。槽位 6/7/8 本机不启用，读到的值应恒为 0。
            for (int i = 0; i < 8; i++)
            {
                int lo = r[8 + i];
                int hi = r[16 + i];
                cfg.Levels[i] = lo | (hi << 8);
            }
            return cfg;
        }

        /// <summary>
        /// 序列化为 56 字节 0x04 报告（含校验和）。
        /// </summary>
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
            // 写入 8 个槽位（固件内存布局）。槽位 6/7/8 由 UI 逻辑固定为 0（本机不启用，见 Levels 注释）。
            for (int i = 0; i < 8; i++)
            {
                int v = Math.Clamp(Levels[i], 0, 0xFFFF);
                r[8 + i] = (byte)(v & 0xFF);
                r[16 + i] = (byte)((v >> 8) & 0xFF);
            }
            r[24] = ActiveLevel;
            r[49] = 0x02;
            // 校验和（大端）= sum(报告[3..49])
            int sum = 0;
            for (int i = 3; i <= 49; i++) sum += r[i];
            r[50] = (byte)((sum >> 8) & 0xFF);
            r[51] = (byte)(sum & 0xFF);
            return r;
        }
    }

    /// <summary>
    /// 按键配置（0x08 报告，59 字节）。布局来自 AGENTS.md 6 节（已打通）。
    /// </summary>
    public class ButtonConfig
    {
        public const int ReportId = 0x08;
        public const int Length = 59;

        // 18 个按钮条目，每条 3 字节（第一字节为功能编码）
        public byte[][] Entries { get; set; } = InitEntries();

        private static byte[][] InitEntries()
        {
            // ⚠️ 默认值来自官方软件抓包反推（button_1/2/4/5），不是推断：
            // entry[0]=左键 02  entry[1]=右键 03  entry[2]=前进 06
            // entry[3]=后退 05  entry[4]=中键 04  entry[5]=DPI循环 0d
            // entry[6..13]=标准/未使用 01
            // entry[14]=左滚 0b entry[15]=右滚 0c entry[16]=上滚 09 entry[17]=下滚 0a
            byte[][] defaults = new byte[18][]
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
            return defaults;
        }

        public static ButtonConfig FromBytes(byte[] r)
        {
            var cfg = new ButtonConfig();
            for (int i = 0; i < 18; i++)
            {
                cfg.Entries[i] = new byte[] { r[3 + i * 3], r[4 + i * 3], r[5 + i * 3] };
            }
            return cfg;
        }

        /// <summary>
        /// 返回一份全新的默认表副本（用于 0x08 整表重写时垫底，只改目标按钮）。
        /// 绝不能用全 0x01 垫底——那会把整张按键表清空（2.3b 事故教训）。
        /// ⚠️ 此默认表为软件层推断，未经 USBPcap 抓包证实：entry[0..5]/[14..17] 取自官方软件
        /// 抓包反推，entry[6..13] 等未抓包按钮直接填 0x01（标准/未使用），不等于设备真出厂值。
        /// 本方法只生成内存副本，不向设备发送任何命令，更不会触发设备掉电/硬件复位。
        /// </summary>
        public static ButtonConfig Default()
        {
            var cfg = new ButtonConfig();
            cfg.Entries = InitEntries(); // 已是 18×3 新数组
            return cfg;
        }

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
            // 校验和 = sum(r[3..56])，大端存入 [57][58]
            // 与实测脚本 sum(btn08[3:57]) 完全一致（Python 切片不含索引 57）。
            int sum = 0;
            for (int i = 3; i <= 56; i++) sum += r[i];
            sum &= 0xFFFF;
            r[57] = (byte)((sum >> 8) & 0xFF);
            r[58] = (byte)(sum & 0xFF);
            return r;
        }

        // 按钮功能编码（来自 AGENTS.md 6 节）
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
    /// 单个宏 ID 的工作区（按宏 ID 索引，按键页/宏页共用同一份 Store）。
    /// Actions 每个元素为 {code, flag} 对：当前仅支持键盘按键，flag 仅 bit0（按下置位）有效。
    /// ⚠️ 宏编辑功能尚不完善：延时、选中删除、鼠标键录制等未实现，请勿依赖。
    /// </summary>
    public class MacroWorkspace
    {
        public byte Method = 0x00;        // 播放方式：0x00=循环次数 0x01=任意键停止 0x02=按住循环（HID协议逆向报告.md 3.6 节，实机验证）
        public byte LoopCount = 1;        // 循环次数（仅播放方式=循环次数/0x00 时生效；最小为 1，本机固件实测 0 会被 clamp 为 1）
        public List<MacroAction> Actions = new();
    }

    /// <summary>
    /// 宏动作：一个按键事件（按下或释放）。
    /// code    = HID Usage ID（键盘按键码）
    /// press   = true 表示按下、false 表示释放
    /// delayMs = 该事件后的延迟（毫秒）；0 表示不显式指定（走默认编码）
    /// </summary>
    public class MacroAction
    {
        public byte Code { get; set; }
        public bool Press { get; set; } = true;
        public int DelayMs { get; set; } = 0;

        /// <summary>构造 flag|delay 字节：bit7=按下标志，bits0-6=延迟编码（delayMs>0 时由 EncodeDelay 计算）。</summary>
        public byte ToFlag()
        {
            byte flag = (byte)(Press ? 0x80 : 0x00);
            if (DelayMs > 0)
                flag |= (byte)(MacroConfig.EncodeDelay(DelayMs) & 0x7F);
            return flag;
        }
    }

    // ===== 宏管理（0x09 协议，2026-08-02 实机打通）=====
    // 严格 1:1 移植 macro_generator.py 的 MacroBuilder，字节布局一致。
    // 每个动作含按键码 + 按下/释放 + 延迟（ms），延迟编码复用已实机验证的公式。
    public class MacroConfig
    {
        public byte MacroId = 0x01;            // 宏编号 1..6
        public byte Method = 0x00;             // 播放方式：0x00=循环次数 0x01=任意键停止 0x02=按住循环（HID协议逆向报告.md 3.6 节，实机验证）
        public byte RecordMode = 0x01;         // 延迟录制模式：0x01=默认延迟 0x07=录制延迟
        public List<MacroAction> Actions = new();

        /// <summary>延迟编码（反汇编 0x004182B0 确认，1:1 移植 macro_generator.py）：
        /// delay <= 1270ms: encoded = round(delay_ms/100)，最小 1
        /// delay &gt; 1270ms: encoded = round((delay_ms%200)/100)，最小 1
        /// 注意：&lt;50ms 的延迟全部编码为 1（无法区分 10ms/30ms）。</summary>
        public static int EncodeDelay(int delayMs)
        {
            int encoded = delayMs <= 1270
                ? (int)(delayMs / 100.0 + 0.5)
                : (int)((delayMs % 200) / 100.0 + 0.5);
            return Math.Max(1, encoded);
        }

        /// <summary>由动作序列生成 0x09×3 三块，复用已实机验证的编码。</summary>
        public static List<byte[]> BuildMacroChunks(byte macroId, byte method, byte recordMode, List<MacroAction> actions, byte loopCount = 1, byte modifier = 0x01)
        {
            var keycodes = new List<(byte code, byte flag)>();
            foreach (var a in actions)
                keycodes.Add(((byte)(a.Code & 0xFF), a.ToFlag()));
            return BuildMacroChunksProven(macroId, method, recordMode, keycodes, loopCount, modifier);
        }

        // 复刻 macro_write_simple.py 的 build_09_internal + build_09_chunks（用户实机验证通过的编码）
        private static List<byte[]> BuildMacroChunksProven(byte macroId, byte playMode, byte recordMode, List<(byte code, byte flag)> keycodes, byte loopCount = 1, byte modifier = 0x01)
        {
            var buf = new byte[131];
            buf[0] = 0x09;
            buf[1] = 0x83; // 发送时被 chunk header 覆盖
            buf[2] = (byte)(macroId & 0xFF);
            buf[3] = (byte)(playMode & 0xFF);
            buf[4] = 0; buf[5] = 0; buf[6] = 0;
            // internal[7] 为双重用途字节（HID协议逆向报告.md 3.6 节）：
            //   playMode=0x00（循环次数播放）→ 写循环次数值（0/无效 clamp 为 1）；
            //   playMode=0x01/0x02（任意键停止/按住循环）→ 写修饰键（官方用 0x01，误用 0x03 会导致不生效）。
            int loop = loopCount;
            if (loop < 1) loop = 1;
            if (loop > 255) loop = 255;
            buf[7] = (playMode == 0x00) ? (byte)loop : modifier;
            buf[28] = (byte)(keycodes.Count & 0xFF); // 事件总条数（按下/释放/延迟虚拟动作各算一条）
            buf[29] = (byte)(recordMode & 0xFF);
            int offset = 30;
            foreach (var (kc, flag) in keycodes)
            {
                if (offset + 1 >= 129) break;
                buf[offset] = kc; buf[offset + 1] = flag; offset += 2;
            }
            int cksum = 0;
            for (int i = 3; i <= 128; i++) cksum += buf[i];
            cksum &= 0xFFFF;
            buf[129] = (byte)((cksum >> 8) & 0xFF);
            buf[130] = (byte)(cksum & 0xFF);

            var chunks = new List<byte[]>();
            byte[][] ch = new byte[3][];
            for (int c = 0; c < 3; c++) ch[c] = new byte[64];
            ch[0][0] = 0x09; ch[0][1] = 0x40; ch[0][2] = (byte)(macroId & 0xFF); ch[0][3] = 0;
            for (int i = 0; i < 60; i++) ch[0][4 + i] = buf[3 + i];
            ch[1][0] = 0x09; ch[1][1] = 0x40; ch[1][2] = (byte)(macroId & 0xFF); ch[1][3] = 1;
            for (int i = 0; i < 60; i++) ch[1][4 + i] = buf[0x3F + i];
            ch[2][0] = 0x09; ch[2][1] = 0x0C; ch[2][2] = (byte)(macroId & 0xFF); ch[2][3] = 2;
            for (int i = 0; i < 8; i++) ch[2][4 + i] = buf[0x7B + i];
            chunks.Add(ch[0]); chunks.Add(ch[1]); chunks.Add(ch[2]);
            return chunks;
        }
    }

    // ===== 灯光 + 电源管理（0x05 协议，已 USBPcap 验证，见 HID协议逆向报告.md 3.2 节）=====
    // 默认值取自官方软件抓包实值，故 ToBytes() 默认输出与已验证的 Report05 完全一致。
    public class LightConfig
    {
        public const int ReportId = 0x05;
        public const int Length = 15;

        // 灯光模式：0 关闭 / 1 呼吸DPI / 2 常亮DPI / 3 循环呼吸 / 4 霓虹
        public int Mode { get; set; } = 3;
        // 移动关灯：底层语义 = 该功能是否"关闭"。bit7=1 关闭功能、bit7=0 开启功能（与 light_set.py 对齐）。
        // UI 复选框"移动时关灯"勾选 = 用户想要功能开启 = bit7=0 = MoveOff=false（见 MainWindow.xaml.cs SyncAuxFromUI 取反）。
        public bool MoveOff { get; set; } = false;
        // 呼吸速度：4(最慢) ~ 8(最快)，编码 byte4 = 9 - 速度
        public int BreathSpeed { get; set; } = 6;
        // 睡眠时间(分钟)：1 ~ 60，编码 byte5 = (分 << 4) | 0x08
        public int SleepMinutes { get; set; } = 10;
        // 一级休眠时间(分钟)：0.5 ~ 60，编码 byte9 = 分 × 2（0.5 分 → 1）
        public double Level1SleepMinutes { get; set; } = 0.5;
        // 按键响应 / 去抖(ms)：1 ~ 25，编码 byte10 = ms ÷ 2
        public int DebounceMs { get; set; } = 6;

        public byte[] ToBytes()
        {
            var r = new byte[Length];
            r[0] = ReportId;
            r[1] = 0x0F;
            r[2] = 0x01;
            int mode = Math.Clamp(Mode, 0, 4);
            r[3] = (byte)(mode | (MoveOff ? 0x80 : 0));
            int sp = Math.Clamp(BreathSpeed, 4, 8);
            r[4] = (byte)(9 - sp);
            int sl = Math.Clamp(SleepMinutes, 1, 60);
            r[5] = (byte)(((sl << 4) | 0x08) & 0xFF);
            r[6] = 0x00;
            r[7] = 0x00;
            r[8] = 0xFF;
            // 一级休眠 byte9（根因已定位，2026-08-08 反汇编确认）：
            //   固件把 byte9 当 1-based 的 0.5 分档计数：实际分钟 = (byte9 - 1) × 0.5
            //   PC 端编码为 分钟×2，与固件 off-by-one → 每档系统性 -0.5 分（官方软件也如此）。
            //   反汇编证据（mouse_analysis3.txt 0x417E60）：byte9 从 [edi+0x95d] 原样透传，无 ×2/-1 运算。
            //   修正：无条件 +1，使 实际 = (byte9-1)×0.5 = (分钟×2+1-1)×0.5 = 分钟（已实机验证 2 分档≈2分）。
            int l1 = (int)Math.Clamp(Math.Round(Level1SleepMinutes * 2), 1, 120) + 1;
            r[9] = (byte)l1;
            int db = Math.Clamp(DebounceMs, 1, 25);
            r[10] = (byte)(db / 2);
            int sum = 0;
            for (int i = 3; i <= 10; i++) sum += r[i];
            r[11] = (byte)((sum >> 8) & 0xFF);
            r[12] = (byte)(sum & 0xFF);
            r[13] = 0x00;
            r[14] = 0x00;
            return r;
        }
    }

    // ===== 回报率（0x06 协议，已 USBPcap 验证，见 HID协议逆向报告.md 3.3 节）=====
    // ⚠️ 必须与 0x0C 唤醒同发，单发会破坏设备状态（历史事故）。无校验和。
    public class RateConfig
    {
        public const int ReportId = 0x06;
        public const int Length = 9;

        // 回报率(Hz)：125 / 250 / 500 / 1000，编码 idx = 1000 / Hz、~idx = 0xFF - idx
        public int Hz { get; set; } = 500;

        public byte[] ToBytes()
        {
            var r = new byte[Length];
            r[0] = ReportId;
            r[1] = 0x09;
            r[2] = 0x01;
            int idx = 1000 / Hz;   // 125→8, 250→4, 500→2, 1000→1
            r[3] = (byte)idx;
            r[4] = (byte)(0xFF - idx);
            // r[5..8] 固定 0x00
            return r;
        }
    }
}
