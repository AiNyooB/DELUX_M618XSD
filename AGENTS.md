# AGENTS.md

本项目为 **DELUX M618XSD 鼠标客户端**——一款面向普通用户的 C 端配置软件（替代官方 Mouse.exe）。
接手任何任务前，**先读本文件**，再按需查阅协议文档（`reference/`）。

> 产品定位：**给不懂 DPI/回报率/HID 的普通用户用的配置工具**，不是调试器、不是驱动、不是开发者工具。
> 因此：**用户体验 / UX / UI 与协议正确性同等重要**，甚至更优先——协议是手段，用户能轻松完成配置才是目的。

---

## 一、项目定位与阶段

| 阶段 | 状态 | 内容 |
|---|---|---|
| Phase 1 | ✅ 完成 | 协议逆向（抓包 + Python 实测），产物在 `reference/` |
| Phase 2 | ✅ 完成 | C# WPF 上位机 `MouseDriverClient/` 逐功能实现（协议层全部打通，含单元测试） |
| Phase 3 | 🔄 **进行中（最终阶段）** | 正式客户端 `DELUX.Driver/`（**纯原生 WPF** + MVVM + DI，未引入任何第三方 UI 库），重点打磨 UX/UI，见 `三阶段计划文档.md` |

> Phase 2 的 `MouseDriverClient/` 是**协议验证载体**：编解码逻辑（`Models.cs`）与通信层（`HidComm.cs`）已实机验证，
> 是 Phase 3 复用的基础。Phase 3 的职责是**把已验证的协议能力包装成普通用户看得懂、用得了的界面**。

---

## 二、文档索引（权威依据，脚本/代码以文档为准）

| 文档 | 作用 | 何时读 |
|---|---|---|
| `三阶段计划文档.md` | **Phase 3 实施计划 + UX 目标 + 验收标准**（含每页 UX/UI 要求、风险表 UX 侧） | 推进 Phase 3 时，先读 |
| `M618XSD客户端原型描述.md` | **UI 原型规格**：参考 ATK HUB 自研的页面布局/组件/交互/文案（9 个页面 + 页面清单 + 待确认事项），是 Phase 3 各页 UI 实现的视觉与交互蓝本 | 设计/实现任一页面 UI 时对照；与计划文档互为补充（计划写"做什么"，原型写"长什么样"） |
| `reference/AGENTSK_knowledge.md` | 事实总表：设备识别、DLL 语义、各 Report 状态分级（✅已实测/⚠️推断/❓未知）、前置条件、事故记录 | 接手任务先读 |
| `reference/HID协议逆向报告.md` | 字节级协议：0x04 DPI / 0x05 灯光+电源 / 0x06 回报率 / 0x08 按键 / 0x09 宏 完整字段布局与校验和 | 编写/核对编解码时 |
| `reference/M618XSD驱动功能Wiki.md` | 官方功能总览：9 大模块 UI 行为与参数（按键功能清单、DPI、灯光、回报率、去抖、电池、电源、宏、Profile） | 核对官方功能集、查功能缺口时 |
| `reference/README.md` | 全部 Python 验证脚本索引（按功能/目的查文件） | 需要对照脚本时 |
| `DESIGN.md` | 设计系统参考：设计令牌、色板、排版标度、组件样式、主题切换机制 | 实现/修改 UI 组件时对照 |

> `MouseDriverClient状态记录.md` 在 AGENTSK_knowledge.md 中被引用，但未随仓库提供；如需该历史记录请向用户索取。

---

## 三、UX/UI 设计准则（Phase 3 硬性要求，逐条验收）

> 面向普通用户，全部从用户视角出发。**宁可少一个功能，不可多一分困惑。**

### 3.1 页面骨架（所有页统一）
页面标题（页名 + 一句话说明）→ 配置区（卡片化）→ **自动保存**（修改后停止操作约 1.5 秒自动写入，防抖合并；无操作区按钮）。

### 3.2 语言与文案
- 用**用户语言**，禁止暴露协议术语：不出现「0x08」「entry」「校验和」「Feature Report」「HID」。
  例外：进阶用户可展开的「原始字节」区（默认折叠）。
- 错误提示给**怎么办**，不给报错码：❌「写入失败(0x57)」 ✅「写入失败：请先退出官方驱动 Mouse.exe 再重试」。
- 同一操作全站同一动词：统一「保存」「恢复出厂」，不混用「应用/加载/提交/写入」等词。

### 3.3 反馈
- 每次向设备写配置：**保存前**有「待保存」角标（修改未保存高亮），保存中显示「正在保存…」，**保存后**有成功 Toast。
- 所有异步操作显示进度或禁用态，禁止「点了没反应」。
- 连接状态常驻顶部状态条：绿=已连接 / 灰=未连接 / 黄=连接中，旁带连接按钮。

### 3.4 防呆与可逆
- **危险操作**（0x08 整表覆写、电源命令、重置、删除 Profile）必须二次确认。
- 改错可撤销：未保存编辑可放弃（断连/切页前确认），不丢用户输入（断连时保留未保存编辑，重连后自动保存）；「恢复出厂」仅在**其他设置页**提供（该页规格见 `M618XSD客户端原型描述.md` 第九节：电池 / 外观 / 维护 / 官方驱动 / 关于），一键恢复默认。
- 检测到官方 Mouse.exe 运行 → 顶部黄色警告条 + 拦截发送 + 给「关闭官方软件」引导（AGENTSK 0 节前置）。

### 3.5 一致性
- 全站同一控件样式、同一间距、同一图标语义；详情见 `DESIGN.md`。
- 深色/浅色主题（自研 `Themes/LightTheme.xaml` + `DarkTheme.xaml` 资源字典，通过 `App.ApplyTheme` 切换），默认跟随系统。

### 3.6 协议限制的 UX 转译（关键映射）
| 协议事实 | UX 呈现 |
|---|---|
| 0x08 整表覆写、无增量、不可读（AGENTSK 6 节） | 自动保存前确认弹窗；本地维护全表副本；UI 不展示「读取按键表」 |
| 当前档位不可主动读，靠 Input Report 同步（AGENTSK 2.4） | 不提供「读取档位」按钮；靠监听 Input 自动更新高亮 |
| 宏延迟 UI 值 ≠ 设备实际值（`PcInputToActualMs`，AGENTSK 6 节） | 延迟输入按「设备实际生效延迟」标注，换算已封装 |
| 电源命令不稳定、需数据设备 0x0A 前置（AGENTSK 2.3c） | 电源页红色风险条 + 二次确认 + 默认建议官方软件 |
| 未逆向功能码（媒体/快捷键等，AGENTSK 6 节） | 置灰 + 「等待协议补齐」，绝不进 UI 下拉 |

---

## 四、关键协议事实（速查）

- **设备**：DELUX M618XSD（2.4G 无线），VID=0x1D57 PID=0xFA60。唯一可用特性集合为 UsagePage **0x0B**/Usage 0，FeatureReportByteLength=64。
- **通信**：HID Feature Report（`HidD_SetFeature`），buf[0]=Report ID；所有报告补零到 64 字节。
- **校验和**：16 位累加、大端。覆盖区间按报告：0x04→[3..49]，0x05→[3..10]，0x08→[3..57]，0x09→[3..129]。
- **各报告**：0x04=DPI 配置(56B, [24]=活跃档位)，0x05=灯光+电源管理(15B)，0x06=回报率(9B, idx=1000÷Hz)，0x08=按键映射(59B, 18×3B)，0x09=宏数据(131B×3 分块)。
- **0x08 是整表覆写**，无增量更新；设备不支持读按键表 → 上位机本地维护全表副本，改一项后整表写出。
- **宏写入**：0x0C 唤醒 → 0x08 按键映射 → 0x09×3 分块，**每步间隔 0.2s（含 0x08→0x09，AGENTSK 2.3d）**。
- **档位读取**：主动 GetFeature 读不到当前档位（`[24]` 恒 0）；硬件切档靠监听 Input Report（ID=3）`buf[3]`=档位。上位机用"写入 + 本地记忆 + Input 上报同步"。

### 硬性前置条件（违反会断联/损坏配置）

1. **发任何配置命令前必须退出官方 Mouse.exe**。
2. **电源管理（0x05 byte5/9）必须先打开数据设备（0x0A）**，否则被忽略甚至固件断联。
3. 协议字节未确认前禁止盲发配置命令（历史事故见 AGENTSK_knowledge.md 2.3 节）。

---

## 五、构建与验证

- .NET SDK 10.0（本环境 Linux；代码目标为 Windows x64）。
- 交叉编译 WPF 必须加 `-p:EnableWindowsTargeting=true`。
- 构建/发布命令见 `README.md`「构建」节。发布产物目录 `publish-self/`（自包含）与 `publish-self-fd/`（框架依赖）已被 gitignore。
- WPF 在 Linux 上**只能交叉编译，不能运行/调试**；UI/HID 功能需在 Windows 实机验证（对照 `reference/` 脚本与 `AGENTSK_knowledge.md` 的已实测事实）。
- 单元测试：`MouseDriverClient.Tests/`（xUnit，协议编解码 golden 断言）。Linux 只能编译，**`dotnet test` 必须在 Windows 实机跑**：
  ```
  dotnet test MouseDriverClient.Tests\MouseDriverClient.Tests.csproj -c Release
  ```

---

## 六、开发约定

- **分层**：通信层只允许 `HidComm.cs`（P/Invoke hid.dll）与设备交互；业务编排在 `MainViewModel.cs`；`MainWindow.xaml.cs` 退化为纯视图层。
- **协议模型**（DpiConfig/ButtonConfig/MacroConfig/LightConfig/RateConfig）集中在 `Models.cs`，字段注释需标注依据来源（文档节号 / 实机验证日期）。
- **命名空间** `MouseDriverClient`；WPF 项目 net10.0-windows + UseWPF。
- **代码注释用中文**；不引入日志库/Serilog；不引入未列入计划文档的第三方 UI 库。
- **写测试**：使用 `.agents/skills/code-testing-agent` 生成单元测试。可脱离 HID 硬件的纯逻辑（协议编解码、校验和、宏序列构建）应优先补测。
- **UX 实现**：凡新增 UI 功能，先对照 `三阶段计划文档.md` 对应 Phase 的 UX/UI 清单，逐条落实后再交付；文案禁止出现协议术语。
- **设计系统**：UI 实现优先使用 `DESIGN.md` 中定义的设计令牌和组件样式；新样式须基于现有令牌体系扩展，避免硬编码色值/字号/间距。

---

## 七、运行时/诊断

- 启动崩溃日志：exe 同目录 `crash.log`（App.xaml.cs 已内置未处理异常钩子）。
- 排查通信问题优先对照 `reference/verify_hidcomm_logic.py`（HidComm 逻辑等价复刻）与 `reference/hid_enum_diag.py`。

---

## 八、技能

- `find-skills`（`.agents/skills/find-skills`）：搜索/安装 open agent skills 生态技能。
- `code-testing-agent`（`.agents/skills/code-testing-agent`）：生成/改进单元测试。
