# DELUX M618XSD 鼠标客户端

用 C# 自写驱动（MouseDriverClient）替代官方 Mouse.exe，为 DELUX M618XSD（2.4G 无线鼠标，VID=0x1D57 PID=0xFA60）提供配置管理能力：DPI、灯光、回报率、按键映射、宏、电池/电源管理。

## 项目阶段

| 阶段 | 状态 | 说明 | 产物 |
|---|---|---|---|
| Phase 1 | ✅ 完成 | 协议逆向：抓包 + Python 脚本实测验证官方协议 | `reference/` |
| Phase 2 | 🔄 进行中 | C# 驱动（WPF + hid.dll P/Invoke）逐功能实现 | `MouseDriverClient/` |
| Phase 3 | 🔄 进行中 | 正式客户端（**纯原生 WPF** 现代化 UI + 打包发布） | 见 `三阶段计划文档.md` |

## 目录结构

```
.
├── reference/                 # Phase 1 产物：协议文档 + Python 验证/对照脚本
│   ├── AGENTSK_knowledge.md   # 事实总索引（状态分级、前置条件、事故记录）
│   ├── HID协议逆向报告.md      # 字节级协议文档（各 Report 完整字段布局）
│   └── *.py                   # 各功能验证脚本（README.md 内有详细索引）
├── MouseDriverClient/         # Phase 2：C# WPF 上位机源码
│   ├── HidNative.cs           # hid.dll P/Invoke 声明
│   ├── HidComm.cs             # HID 通信层（枚举/连接/Feature Report 读写）
│   ├── Models.cs              # DpiConfig/ButtonConfig/MacroConfig 等协议模型
│   ├── MainViewModel.cs       # 聚合视图模型（业务编排，可脱离 UI 测试）
│   ├── MainWindow.xaml(.cs)   # 主窗口
│   └── MacroEditor.xaml(.cs)  # 宏编辑器
├── 三阶段计划文档.md            # Phase 3 正式客户端实施计划
└── .agents/skills/            # 项目技能（find-skills / code-testing-agent）
```

## 构建

环境要求：.NET SDK 10.0（Windows 或 Linux 交叉编译均需 `EnableWindowsTargeting`）。

> Phase 3 正式客户端 `DELUX.Driver/` 目标框架为 **net10.0-windows**，使用 WPF 内置 Fluent 主题。构建脚本：`DELUX.Driver/publish-fd.bat`（框架依赖，推荐）与 `DELUX.Driver/publish-self.bat`（自包含）。

```bash
# 框架依赖单文件发布（exe 仅 ~0.8MB，目标机需 .NET 10 Desktop Runtime，一次性）—— 推荐
dotnet publish DELUX.Driver -c Release -r win-x64 --self-contained false \
  -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -o "DELUX.Driver/publish-self-fd"

# 自包含单文件发布（目标机免安装运行时，体积 ~150MB）
dotnet publish DELUX.Driver -c Release -r win-x64 --self-contained true \
  -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -o "DELUX.Driver/publish-self"
```

> **冷启动注意**：Windows Defender 会对「新建/刚发布的 exe」做一次全量实时扫描，首次启动可能多花 3-4s（与文件大小无关）。实测框架依赖版重建后首次启动 ~4s、后续启动 ~0.5s。要消除首次扫描延迟，可将发布目录加入 Defender 排除项（见下方命令，需管理员权限）。
>
> ```powershell
> Add-MpPreference -ExclusionPath "D:\DELUX_M618XSD\DELUX.Driver\publish-fd"   # 按实际发布目录调整
> Add-MpPreference -ExclusionPath "D:\DELUX_M618XSD\DELUX.Driver\publish-self-fd"
> Add-MpPreference -ExclusionPath "D:\DELUX_M618XSD\DELUX.Driver\publish-self"
> ```

## 安全须知（开发/实测必读）

- **向设备发任何配置命令前，必须先退出官方 Mouse.exe**，否则官方驱动抢占会导致命令失效甚至设备异常。
- 电源管理命令（0x05 的 byte5/9）额外要求先打开数据设备（UsagePage 0x0A）。
- 协议字节验证前禁止向设备盲发配置命令（历史事故见 `reference/AGENTSK_knowledge.md` 2.3 节）。
- 更多协议细节与开发约定见 `AGENTS.md`。
