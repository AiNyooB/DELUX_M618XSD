# AGENTS.md

本项目为 DELUX M618XSD 鼠标驱动逆向 + 自写上位机。接手任何任务前，**先读本文件**，再按需查阅协议文档。

## 项目定位与阶段

| 阶段 | 状态 | 内容 |
|---|---|---|
| Phase 1 | ✅ 完成 | 协议逆向（抓包 + Python 实测），产物在 `reference/` |
| Phase 2 | 🔄 进行中 | C# WPF 上位机 `MouseDriverClient/` 逐功能实现 |
| Phase 3 | ⏳ 未开始 | 正式客户端（WPF-UI + MVVM + DI），见 `三阶段计划文档.md` |

## 文档索引（权威依据，脚本/代码以文档为准）

| 文档 | 作用 | 何时读 |
|---|---|---|
| `reference/AGENTSK_knowledge.md` | 事实总表：设备识别、DLL 语义、各 Report 状态分级（✅已实测/⚠️推断/❓未知）、前置条件、事故记录 | 接手任务先读 |
| `reference/HID协议逆向报告.md` | 字节级协议：0x04 DPI / 0x05 灯光+电源 / 0x06 回报率 / 0x08 按键 / 0x09 宏 完整字段布局与校验和 | 编写/核对编解码时 |
| `reference/M618XSD驱动功能Wiki.md` | 官方功能总览：9 大模块 UI 行为与参数（按键功能清单、DPI、灯光、回报率、去抖、电池、电源、宏、Profile） | 核对官方功能集、查功能缺口时 |
| `reference/README.md` | 全部 Python 验证脚本索引（按功能/目的查文件） | 需要对照脚本时 |
| `三阶段计划文档.md` | Phase 3 正式客户端实施计划 | 推进 Phase 3 时 |

> `MouseDriverClient状态记录.md` 在 AGENTSK_knowledge.md 中被引用，但未随仓库提供；如需该历史记录请向用户索取。

## 关键协议事实（速查）

- **设备**：DELUX M618XSD（2.4G 无线），VID=0x1D57 PID=0xFA60。唯一可用特性集合为 UsagePage **0x0B**/Usage 0，FeatureReportByteLength=64。
- **通信**：HID Feature Report（`HidD_SetFeature`），buf[0]=Report ID；所有报告补零到 64 字节。
- **校验和**：16 位累加、大端。覆盖区间按报告：0x04→[3..49]，0x05→[3..10]，0x08→[3..57]，0x09→[3..129]。
- **各报告**：0x04=DPI 配置(56B, [24]=活跃档位)，0x05=灯光+电源管理(15B)，0x06=回报率(9B, idx=1000÷Hz)，0x08=按键映射(59B, 18×3B)，0x09=宏数据(131B×3 分块)。
- **0x08 是整表覆写**，无增量更新；设备不支持读按键表 → 上位机本地维护全表副本，改一项后整表写出。
- **宏写入**：0x0C 唤醒 → 0x08 按键映射 → 0x09×3 分块（间隔 ~0.2s）。
- **档位读取**：主动 GetFeature 读不到当前档位（`[24]` 恒 0）；硬件切档靠监听 Input Report（ID=3）`buf[3]`=档位。上位机用"写入 + 本地记忆 + Input 上报同步"。

### 硬性前置条件（违反会断联/损坏配置）

1. **发任何配置命令前必须退出官方 Mouse.exe**。
2. **电源管理（0x05 byte5/9）必须先打开数据设备（0x0A）**，否则被忽略甚至固件断联。
3. 协议字节未确认前禁止盲发配置命令（历史事故见 AGENTSK_knowledge.md 2.3 节）。

## 构建与验证

- .NET SDK 8.0 已装于 `/usr/local/dotnet`（本环境 Linux；代码目标为 Windows x64）。
- 交叉编译 WPF 必须加 `-p:EnableWindowsTargeting=true`。
- 构建/发布命令见 `README.md`「构建」节。发布产物目录 `publish-self/`（自包含）与 `publish-self-fd/`（框架依赖）已被 gitignore。
- WPF 在 Linux 上**只能交叉编译，不能运行/调试**；UI/HID 功能需在 Windows 实机验证（对照 `reference/` 脚本与 `AGENTSK_knowledge.md` 的已实测事实）。

## 开发约定

- 通信层只允许 `HidComm.cs`（P/Invoke hid.dll）与设备交互；业务编排放 `MainViewModel.cs`（可脱离 UI 测试）；`MainWindow.xaml.cs` 退化为纯视图层。
- 协议模型（DpiConfig/ButtonConfig/MacroConfig/LightConfig/RateConfig）集中在 `Models.cs`，字段注释需标注依据来源（AGENTS.md 第几节 / 实机验证日期）。
- 命名空间 `MouseDriverClient`；WPF 项目 net8.0-windows + UseWPF。
- 代码注释用中文；不引入日志库/Serilog；不引入未列入计划文档的第三方 UI 库。
- 写测试：使用 `.agents/skills/code-testing-agent`（微软官方 C# 测试生成技能）生成单元测试。可脱离 HID 硬件的纯逻辑（协议编解码、校验和、宏序列构建）应优先补测。

## 运行时/诊断

- 启动崩溃日志：exe 同目录 `crash.log`（App.xaml.cs 已内置未处理异常钩子）。
- 排查通信问题优先对照 `reference/verify_hidcomm_logic.py`（HidComm 逻辑等价复刻）与 `reference/hid_enum_diag.py`。

## 技能

- `find-skills`（`.agents/skills/find-skills`）：搜索/安装 open agent skills 生态技能。
- `code-testing-agent`（`.agents/skills/code-testing-agent`）：生成/改进单元测试。
