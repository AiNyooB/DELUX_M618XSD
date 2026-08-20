# 本地配置（重写 Phase 7 Profile）— 方案

> 状态：已实现并编译通过（`dotnet build DELUX.Driver/DELUX.Driver.csproj -c Debug -p:EnableWindowsTargeting=true`，0 错误）。
> 决策来源：grill-me 访谈（Q1–Q6），用户确认后执行。

## 0. 用户决策确认

| # | 用户回答 | 落地 |
|---|---|---|
| Q1 | A：重写 Profile | 「配置管理」→「本地配置」，Phase 7 重做。复用 `ProfileData`/`ProfileEntry`。 |
| Q2 | (a)：设备全套快照 | 槽位内容 = 9 模块设备配置快照，存 PC。 |
| Q3 | 固定 4 槽为排版；新建=第 5 个 | 槽位**动态**（可 >4），2 列网格可滚动。新建=追加；删除=移除（槽 1 不可删）。 |
| Q4 | (ii)：立即写设备 | 切换激活槽 = 全量写 8 条报告 → 危险操作：二次确认 + 进度 + 回滚。 |
| Q5 | 右栏空白，不编辑；卡片做大只读 | 本页纯选择/管理。卡片显示：名称/DPI档/回报率/灯光/最后修改时间。 |
| Q6 | 其他页修改后自动保存到启用的槽位 | 全局一个激活槽位；各配置页自动保存时写设备 **并** 同步到激活槽位快照。 |

** adopting 的假设（实施中若不对请纠正）：**
- (a) 槽位数量是**动态**的（可建第 5、6…个，网格滚动），而非硬上限 4。
- (b) 右栏保持空白（不用单列全宽）。1080 宽度下右侧留白偏多，如视觉不好可后续改为全宽单列。
- (c) 槽 1 无特殊语义 = 默认激活的普通槽位；启动时若设备已连接，把当前设备状态抓取写入槽 1，否则槽 1 = 出厂默认。

---

## 1. 你指出的错误（已修正）

原方案写「启动时若设备已连接，抓取设备当前 9 模块状态写入槽 1」——**错，已删除**。

**根因（AGENTS.md 四 / AGENTS_knowledge 2.4）**：设备**不支持读回配置**。当前档位主动 GetFeature 恒为 0、按键表（0x08）不可读、灯光/电源/去抖无读回通道；只有电池能靠 Input Report 上报。所以「抓取设备当前状态」这个动作不存在。

**这条错误同源污染了切换回滚的 preSnapshot**，两处一并修正（见第 5 节）。

**「自动记录当前本地的配置」的正确含义**：这是**持续行为**（各配置页修改后自动保存到激活槽位），**不是启动时的一次性抓取**。槽 1 仅因默认激活而最先被记录。

---

## 2. 核心数据模型

- **复用** `ProfileData`（`Models.cs:518`，9 模块全套快照 + `Clone()`）作为槽位内容。
- **新增** `SlotEntry`（`Models.cs`，继承 `ProfileEntry` 复用 Name/Data）：`Id`、`Name`、`Content` (ProfileData)、`LastModified`、`IsActive`，外加 `CardSummary` / `LastModifiedText` / `RefreshDisplay()`。
- **持久化** `%LOCALAPPDATA%\DELUX.Driver\profiles.json`：`{ slots: SlotEntry[], activeSlotId }`。首次运行自动创建槽 1（出厂默认）。

---

## 3. 启动流程（**已修正**）

1. 加载 `profiles.json`（若存在）→ `slots` + `activeSlotId`。
2. **首次运行（文件不存在）**：创建槽 1，名称「配置 1」，`Content` = **出厂默认值**（与「恢复出厂」同一套默认），`activeSlotId = 1`，落盘。
3. 激活 `activeSlotId` 对应槽位高亮。
4. **全程不读设备。** 「自动记录当前本地的配置」= 用户在各配置页编辑后自动保存到激活槽位的**持续行为**，槽 1 仅因默认激活而最先被记录。

> 局限（如实保留）：首次运行时槽 1 = 出厂默认，**不等于**用户鼠标上可能存在的历史配置（因为读不回来）。用户需在配置页手动改一次，才会写进槽 1。这是设备不可读导致的固有局限，非本方案引入。

---

## 4. 跨页面耦合

- `ActiveSlotId` 由 `AppViewModel` 持有（唯一能调 `HidComm` 的层，AGENTS 六）。
- 各配置页自动保存（`SaveDpi`/`SaveLight`/`SaveRate`/`SaveButtons`/`SaveDebounce`）：写设备后**同步合并进激活槽位 `ProfileData` 快照** + 更新 `LastModified` + 落盘 profiles.json。复用现有防抖（`AutoSaveDelayMs`）与同值不触发，避免每次编辑都重写快照。
- 切换流程见第 5 节。

---

## 5. 切换流程（**preSnapshot 已修正**）

选中槽位 → **二次确认** → **备份 preSnapshot（= 上位机本地已知状态，非设备读取）** → 逐步写入 `0x0C→0x04→0x05→0x06→0x08→0x09×3`（每步 0.2s）→ 进度条「正在应用第 3/8 步…」→

- **preSnapshot 取自本地**：内存中当前各模块值 + 已持久化副本（`buttons.json` 按键表、`dpi-level.json`、`rate.json`、`macros.json`，以及内存中的灯光/去抖/电源）。**不调用 GetFeature 读设备。**
- **成功**：更新 `ActiveSlotId` + 成功 Toast「已切换到 XX 配置」。
- **失败**：立即中止，用 preSnapshot（本地已知状态）全量写回设备（回滚也走进度「正在恢复…」）；回滚再失败 → 顶部红色错误条「配置恢复失败，设备可能处于不一致状态，建议重连后手动校对」，不静默吞错。
- **电源风险**：目标槽含 0x05 电源字节时，0x05 步前确认数据设备 0x0A 已开；未开则单独二次确认，失败优先回滚。
- **官方驱动占用**：切换前检测，运行中则拦截 + 黄色警告条 +「请完全退出 Mouse.exe」（复用全局机制）。
- **本地一致性**：`ActiveSlotId` 仅在 8 步全部成功后更新。
- **回滚局限（如实）**：preSnapshot 是「上位机最后写入的已知状态」，不是设备实时状态。若用户曾用官方 Mouse.exe 改过配置且未退出，二者可能不一致。但 AGENTS 要求写入前必须退出官方驱动，该边界受控。

---

## 6. 页面布局（`LocalConfigPage.xaml`）

**2026-08-20 精简修订（用户确认）**：实测 1080 宽度下原「左栏 300px / 右栏 `*`」右半边空白失衡，且槽位卡片信息（档位/回报率/灯光/时间）在窄列内拥挤、冗余——改为**标题整宽置顶 + 单列窄列表（约 340px，左对齐）**，删除按钮不再常驻。

- **标题行（整宽 Grid）**：左 = 「本地配置」(PageTitleTextStyle) + 副标题「保存多套设置，一键切换」(PageSubtitleTextStyle)；右 = ＋新建配置（SecondaryButtonStyle）。
- **下方 ScrollViewer → ItemsControl** 绑定 `Slots`，`UniformGrid Columns="1"` 单列窄列表（Width=340，左对齐）：
  - 整行可点 = 切换：`Border.InputBindings` + `MouseBinding`（非嵌套 Button，避免与行内删除按钮事件冲突），`Command="{Binding SwitchSlotCmd}" CommandParameter="{Binding .}"`。
  - Border(CornerRadius=NavigationItemCornerRadius) + DataTrigger on `IsActive`：激活槽 = **描边强调色 1.5px + 背景 SubtleFillColorTertiaryBrush**（对齐 DESIGN §5.15 ButtonTagStyle 选中态）；悬停背景 = ControlFillColorSecondaryBrush（§5.15）。
  - 每行只显示：**配置名称（粗体）+ 「当前」徽章**（AccentFillColorDefaultBrush 底 + TextOnAccent 字，仅激活槽显示）。
  - **删除按钮每行常驻占位**：所有行都有同一个按钮（`MinWidth=28, MinHeight=24` 参与布局 → 行高天然一致，新增可删槽不会撑高前 4 行），仅 `IsDeletable`（`Id >= 5`，新增于 Models.cs，前 4 槽为排版基准不可删）为 true 时可见可点；前 4 行用 **`Opacity=0 + IsEnabled=False + IsHitTestVisible=False`** 实现「占位不显示」（**不能用 Visibility=Collapsed**，否则不占布局、MinHeight 失效、行高又会被 UniformGrid 撑开）。点击 = `DeleteSlotCmd` + `CommandParameter="{Binding .}"`（VM 内二次确认 + IsDeletable 守卫）。
- **空态**：无（槽 1 内置始终存在，`Slots.Count >= 1`）。
- **底部状态行**：`SwitchProgress`，反映切换写入生命周期 `待确认 → 正在写入 1/8…8/8 → 已保存 ✓`；成功 Toast，失败给「发生了什么 + 怎么办」文案（AGENTS 3.2）。

---

## 7. 文件变更

| 文件 | 变更 |
|---|---|
| 新建 `DELUX.Driver/Views/LocalConfigPage.xaml` + `.cs` | 替换原 `ProfilePage`；实现第 6 节布局 |
| `DELUX.Driver/Models.cs` | 新增 `SlotEntry`（继承 `ProfileEntry`）+ `RefreshDisplay()`；`ProfileEntry` 注释标注已被取代 |
| `DELUX.Driver/AppViewModel.cs` | `ActiveSlotId`/`ActiveSlotName`/`Slots`/`CanManageSlots`/`CanDeleteSlot`/`CanSwitchSlot`/`IsSwitching`/`SwitchProgress`、`NewSlotCmd`/`DeleteSlotCmd`/`SwitchSlotCmd`、`profiles.json` 读写、`InitSlots`/`BuildSnapshot`/`ApplyProfileToState`/`SyncMacrosFrom`/`SyncDpiConfigFrom`/`SyncActiveSlot`/`WriteProfileToDevice`/`WriteMacrosToDevice`/`SwitchSlot`、各 `Save*` 同步到激活槽位 |
| `DELUX.Driver/MainWindow.xaml` | NavItem「配置管理」→「本地配置」，Tag `Profile`→`LocalConfig` |
| `DELUX.Driver/MainWindow.xaml.cs` | `Pages`：`"LocalConfig" => () => new LocalConfigPage()` |
| 删除 `DELUX.Driver/Views/ProfilePage.xaml` + `.cs` | 已无引用 |
| `AGENTS.md` | 「Profile 切换/全量切换/删除 Profile」→「本地配置切换/全量切换/删除槽位」 |
| `三阶段计划文档.md` | Phase 7「配置管理（Profile）」→「本地配置」，标注已实现 + preSnapshot 修正说明 |

**VM 层决策（对批准方案的小简化）**：未新建 `LocalConfigViewModel`，直接绑定 `AppViewModel`——与全站其他页一致（所有页的 `DataContext` 由 `MainWindow.Navigate` 统一注入 `_vm`），避免再起一个 VM。AGENTS 六要求业务编排在 `MainViewModel`，切换流程必调 `HidComm`，本来就该在 `AppViewModel`。

---

## 8. UX 规则对照（AGENTS）

- 3.1 骨架：标题 → 配置区 → 底部状态行。本页无属性修改，故无防抖自动保存；状态行反映切换写入生命周期。
- 3.2 文案：用户语言，禁协议术语；失败 = 「发生了什么 + 怎么办」。
- 3.3 反馈：切换 = 危险操作 → 进度 + 禁用态（`IsSwitching`）+ 成功 Toast；失败 Toast/红色条。新建/删除 = 立即生效 + Toast，不防抖。
- 3.4 防呆：切换覆写设备 → 二次确认；官方驱动运行时拦截（全局机制）；删除二次确认；槽 1 不可删。
- 3.5 一致性：复用 `CardStyle`/`SecondaryButtonStyle`/`PageTitleTextStyle`/`PageSubtitleTextStyle`/`NavigationItemCornerRadius`/`Space*`，不新增 DesignToken。
- 3.6 协议限制：电源步 0x0A 前置确认；宏延迟换算封装沿用；未逆向功能码不进 UI。

---

## 9. 验收口径

- 点击槽位 → 二次确认 → 进度条推进 → 设备实际值跟随切换（Windows 实机对照 `reference/` 脚本验证）。
- 启动不读设备；首次运行槽 1 = 出厂默认；各配置页修改后自动落到激活槽位；profiles.json 随变更落盘。
- 新建/删除即时生效；槽 1 不可删。
- 官方 Mouse.exe 运行时切换被拦截 + 黄色警告条。
- 全站无协议术语文案；深/浅主题正常。

---

## 10. 风险

- **跨页面自动保存到激活槽位**：改动 5 个 `Save*` 方法 + profiles.json，须与现有防抖/同值不触发兼容。→ 合并进现有防抖回调，仅写成功后调用 `SyncActiveSlot`，切换期间由 `_switching` 守卫跳过。
- **切换写 8 条报告**：技术不确定性（原子性/回滚）仍在，需 Windows 实机验证；且回滚基于本地已知状态而非设备实时状态（第 5 节已如实标注局限）。
- **右栏空白**：1080 宽度下偏空，验收不佳可改全宽单列（不改语义）。
- **槽 1 首次为出厂默认**：与用户鼠标历史配置可能不一致，需用户手动改一次才同步（设备不可读的固有局限）。
- **WPF 交叉编译**：Linux 只能 `dotnet build`，UI/HID 功能须在 Windows 实机验证（对照 `reference/` 脚本与 `AGENTSK_knowledge.md` 已实测事实）。