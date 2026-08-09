# MouseDriverClient 参考脚本说明文档

本目录收录了 `C:\Users\fresh\Downloads\618XSD` 下对上位机 **MouseDriverClient（C#）** 有参考价值的验证 / 对照脚本。
这些脚本是 Python 版"等价复刻""对照基准"或"协议解码器"，用于核对 C# 协议编码（都在 `Models.cs` / `HidComm.cs` / `HidNative.cs` 中）是否正确，以及排障时定位字节级字段。

> 上位机本体（C# 源码）不在此目录，核心文件为：`Models.cs`、`HidComm.cs`、`HidNative.cs`、`MainViewModel.cs`、`MainWindow.*`、`MacroEditor.*`。
> 向设备发配置命令前，务必先退出官方 `Mouse.exe`，电源类（0x05 byte5/9）还需先 `Open_DevMonitor()` 打开数据设备（0x0A）。具体字段的可靠性以各文件内注释及 AGENTS.md 为准。

---

## 零、核心文档（先于脚本阅读）

这两份文档是整个协议与上位机的**权威依据**，排查任何编码问题前应先读。

| 文件 | 作用 | 参考价值 |
|---|---|---|
| `AGENTSK_knowledge.md` | 事实与状态总索引：设备识别、DLL 语义、各 Report 状态分级、前置条件、事故记录、未解问题。 | ⭐⭐⭐ 接手任务先读本文件，再按需看协议细节 |
| `HID协议逆向报告.md` | 字节级协议文档：Report ID `0x04`(DPI) / `0x05`(灯光+电源) / `0x06`(回报率) / `0x08`(按键映射) / `0x09`(宏) 的完整字段布局、校验和算法、宏/按键/灯光/电池协议细节。 | ⭐⭐⭐ 上位机 `Models.cs` / `HidComm.cs` 编解码的权威对照 |

> 两者关系：`AGENTSK_knowledge.md` 是"事实总表 + 状态分级"，`HID协议逆向报告.md` 是"字节级实现细节"。本目录下的 Python 脚本都是对这两份文档协议的**验证/复刻**，文档优先级高于脚本。

---

## 一、直接对照上位机逻辑的验证脚本（⭐ 最高参考价值）

这类脚本是 C# 源码的"Python 等价复刻"或"对照基准"，排查上位机 bug 时最有用。

| 文件 | 对应上位机模块 | 参考价值 |
|---|---|---|
| `verify_hidcomm_logic.py` | `HidComm.cs` | ⭐⭐⭐ 逐函数等价复刻 `EnumerateCollections / Connect / PadReport / WriteFeature / ReadFeature`，专门验证通信层算法正确性 |
| `macro_write_simple.py` | `Models.cs` 的 `BuildMacroChunks` + `MacroEditor` 绑定流程 | ⭐⭐⭐ **之前定位 `buf[7]` 双重用途 bug 的对照基准**；含 `BUTTON_MAP`、`FUNC` 编码、0x09 内部布局 |
| `verify_dpi_source.py` | DPI 模块（`Models.cs` 0x04 报告、`MainViewModel` 档位记忆） | ⭐⭐ 验证 DPI 数据源 / 档位读取逻辑 |

---

## 二、协议 / 字段解码脚本（⭐ 用于核对协议字节，避免 C# 编解码写错）

上位机的协议编码都在 `Models.cs` / `HidComm.cs` 里，这些脚本可用来核对字节级布局。

| 文件 | 用途 | 对应协议 |
|---|---|---|
| `parse_macro.py` ~ `parse_macro6.py` | 解析宏 pcap / 字节，反推 0x09 内部布局 | 宏 0x09（含 `internal[7]` 双重用途、`internal[29]` 延迟模式） |
| `parse_input_reports.py` | 解析 Input Report（电池 / 切档上报） | `03 28 40 XX YY` 电池、`buf[3]=档位` |
| `parse_input_pcap.py` | 解析 Input Report 抓包 | 电池 / 切档 Input Report |
| `read_input_report.py` | 读取并解析 Input Report | 设备→主机上报 |
| `dpi_input_listen.py` | 监听 DPI 切档上报，确认 `buf[3]=档位` | DPI 硬件切档 |
| `dpi_connect_sync_probe.py` | DPI 连接同步探测 | DPI 初始化 / 同步 |
| `read_battery.py` / `read_battery2.py` / `read_battery3.py` | 读电池 Input Report | 电池协议（`byte[3]`=充电状态，`byte[4]`=电量%） |

---

## 三、设备枚举 / HID 底层诊断（⭐ 通信层排障）

上位机改用系统 `hid.dll` P/Invoke（`HidNative.cs`），但枚举 / 打开集合的逻辑思路和这些脚本一致。

| 文件 | 用途 |
|---|---|
| `hid_enum_diag.py` | HID 枚举 + 集合选择（被 `verify_hidcomm_logic.py` 引用，核心） |
| `hid_probe.py` | HID 探测 |
| `hid_len_test.py` | 报告长度测试 |
| `feature_scan.py` | Feature 页扫描（定位字段用） |
| `walk_pages.py` | 设备内存走查快照 |
| `read_page0.py` | 读 page0 |
| `read_echo.py` | 读回显（验证命令回显） |
| `config_snapshot.py` | 配置快照差分（before / after 对比） |

---

## 四、其他功能验证脚本（按功能对应）

| 文件 | 对应上位机功能 | 说明 |
|---|---|---|
| `dpi_set.py` / `dpi_write.py` | DPI 写入 | DPI 验证脚本（发命令 + 读回确认，非盲试）。`dpi_set.py` 发 `0x0C` 唤醒 + `0x06` 回报率命令后 GetFeature 读回 0x04 观察字段；`dpi_write.py` 用官方抓包模板构造 0x04 报告（校验和 `sum(report[3:50])`）写入指定档位/DPI 并读回验证。注：运行时仍须先退出 Mouse.exe |
| `light_set.py` / `light_recovery.py` | 灯光 0x05 | 灯光模式 / 速度 / 恢复 |
| `power_send.py` / `power_test.py` / `power_recovery.py` | 电源管理 0x05 | 含 `byte9 = round(分钟×2)+1` 修正公式，可对照上位机电源逻辑 |
| `macro_generator.py` | 宏数据生成器 | 可对照 C# 的 `BuildMacroChunks` |
| `macro_write_test.py` / `macro_write_full.py` | 宏写入验证 | 不同粒度的宏写入流程 |
| `macro_verify.py` / `macro_test_S.py` | 宏验证 | 宏数据校验 |
| `macro_replay_official.py` / `macro_restore_middle.py` | 宏回放 / 恢复 | 回放官方宏、恢复中键 |
| `pcap_tool.py` | 通用 USBPcap 分析工具 | analyze / compare / diff / list，对照抓包 |

---

## 快速索引（按目的查文件）

- 查 **宏绑定 bug** → `macro_write_simple.py`（基准）+ `parse_macro*.py`（字节布局）
- 查 **通信层算法** → `verify_hidcomm_logic.py` + `hid_enum_diag.py`
- 查 **DPI 切档/记忆** → `verify_dpi_source.py` + `dpi_input_listen.py` + `dpi_connect_sync_probe.py`
- 查 **电池/充电显示** → `read_battery*.py` + `parse_input_reports.py`
- 查 **电源管理字段** → `power_send.py` / `power_recovery.py`
- 查 **灯光字段** → `light_set.py` / `light_recovery.py`
- 抓包分析 → `pcap_tool.py` + `config_snapshot.py`
- 看**协议权威定义 / 字节布局** → `HID协议逆向报告.md`
- 看**事实总表 / 状态分级 / 事故记录** → `AGENTSK_knowledge.md`
