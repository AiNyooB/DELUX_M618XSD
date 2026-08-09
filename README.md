# DELUX M618XSD 鼠标客户端

用 C# 自写驱动（MouseDriverClient）替代官方 Mouse.exe，为 DELUX M618XSD（2.4G 无线鼠标，VID=0x1D57 PID=0xFA60）提供配置管理能力：DPI、灯光、回报率、按键映射、宏、电池/电源管理。

## 项目阶段

| 阶段 | 状态 | 说明 | 产物 |
|---|---|---|---|
| Phase 1 | ✅ 完成 | 协议逆向：抓包 + Python 脚本实测验证官方协议 | `reference/` |
| Phase 2 | 🔄 进行中 | C# 驱动（WPF + hid.dll P/Invoke）逐功能实现 | `MouseDriverClient/` |
| Phase 3 | ⏳ 未开始 | 正式客户端（WPF-UI 现代化 UI + 打包发布） | 见 `三阶段计划文档.md` |

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

环境要求：.NET SDK 8.0（Windows 或 Linux 交叉编译均需 `EnableWindowsTargeting`）。

```bash
# 框架依赖发布（体积小，目标机需 .NET 8 Desktop Runtime）
dotnet publish MouseDriverClient -c Release -r win-x64 --self-contained false \
  -p:EnableWindowsTargeting=true -p:PublishSingleFile=true

# 自包含单文件发布（目标机免安装运行时，体积 ~155MB）
dotnet publish MouseDriverClient -c Release -r win-x64 --self-contained true \
  -p:EnableWindowsTargeting=true -p:PublishSingleFile=true
```

## 安全须知（开发/实测必读）

- **向设备发任何配置命令前，必须先退出官方 Mouse.exe**，否则官方驱动抢占会导致命令失效甚至设备异常。
- 电源管理命令（0x05 的 byte5/9）额外要求先打开数据设备（UsagePage 0x0A）。
- 协议字节验证前禁止向设备盲发配置命令（历史事故见 `reference/AGENTSK_knowledge.md` 2.3 节）。
- 更多协议细节与开发约定见 `AGENTS.md`。
