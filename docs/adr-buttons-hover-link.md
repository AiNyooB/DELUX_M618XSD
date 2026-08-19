# ADR: 按键标签 hover → 图标联动高亮

- **状态**：已采纳（Accepted）
- **日期**：2026-08-17
- **决策人**：用户 + AI 代理（grill-with-docs / domain-modeling 流程产出）
- **相关代码**：`AppViewModel.cs`（`IconMarker.IsHovered`、`HoverButtonCmd`/`UnhoverButtonCmd`、`SetButtonHover`）、`Views/ButtonsPage.xaml`（`IconMarkerTemplate` 触发器）、`Views/ButtonsPage.xaml.cs`（`OnTagsHostHover`/`OnTagsHostLeave`）

---

## 1. 背景 / 问题

改键设置页（`ButtonsPage`）是「鼠标示意图（含位置图标）+ 10 个可编程按键标签」的叠加布局：

- **位置图标**（`IconMarker`）：画布上叠加在鼠标示意图上的小标记（`Ellipse`/`Path`），由 `IconsHost`（`ItemsControl`）渲染，数据来自 `IconMarkers` 集合，已通过 `IconKey` 与按键标签一一对应。
- **按键标签**（`RadioButton`）：10 个可编程按键的名称标签，由 `TagsHost`（`ItemsControl`）渲染，数据来自 `Buttons` 集合，点击选中后在右侧面板配置功能。

用户期望的交互：**鼠标悬停在某个按键标签上时，画布上与之对应的那个图标也变蓝加粗；移开恢复；若该标签已被选中，则即使移开鼠标图标仍保持蓝粗。**

### 之前的理解偏差（被本 ADR 纠正）

初版实现把 hover 触发器直接挂在 `Ellipse`/`Path` 的 `IsMouseOver` 上——即只有鼠标**直接悬停在 14×14 像素的小图标本身**时才会高亮，这不符合「悬停按键标签 → 对应图标高亮」的诉求。本决策明确：触发源是**按键标签**，而非图标自身；图标只是被联动的目标。

---

## 2. 决策

### 2.1 关联键复用已有字段，不新增配对结构

`ButtonItem` 与 `IconMarker` 已通过 `IconKey` 建立对应关系（注释：把中键图标与滚轮这类同物理键、异命名的项关联起来）。**本决策复用 `IconKey` 作为 hover 联动的关联键**，不引入新的索引或配对表。

> 判定逻辑：`SetButtonHover(buttonIndex, hovered)` 先按 `Index` 找到 `ButtonItem`，再遍历 `IconMarkers` 置位所有 `IconKey` 匹配的图标的 `IsHovered`。

### 2.2 在 ViewModel 新增 `IconMarker.IsHovered` 状态

- `IconMarker` 新增 `IsHovered`（bool，`ObservableObject` 属性，支持 `INotifyPropertyChanged`）。
- 图标的视觉高亮由**数据**驱动（`DataTrigger` 绑定 `IsHovered`/`IsSelected`），而非由图标自身的鼠标命中驱动。

### 2.3 高亮优先级：选中态 > hover 态

图标蓝粗的判定 = `IsSelected || IsHovered`。

- 选中态 `IsSelected` 由 `SelectButton` 逻辑置位，优先级天然高于 `IsHovered`：选中后即使鼠标移开，`IsSelected` 仍为 true，图标保持蓝粗。
- 在 XAML 中 `IsHovered` 的 `DataTrigger` 写在 `IsSelected` 之前；两者同优先级且视觉一致（同为蓝+加粗），无冲突。

### 2.4 标签侧 hover 探测：code-behind 命中测试（零依赖）

不在 XAML 中用 `EventTrigger`/`InvokeCommandAction`（需引入 `System.Windows.Interactivity` 第三方库，违反「不引入未列入计划文档的第三方 UI 库」约定）。改为在 `ButtonsPage.xaml.cs` 中为 `TagsHost` 挂载：

- `PreviewMouseMove`（`OnTagsHostHover`）：用 `ContainerFromElement` 命中测试当前鼠标下的 `RadioButton` 项容器，取出 `ButtonItem.Index`；与上次记录的 `_hoverIndex` 比较，**仅在变化时才发命令**，避免每帧重复置位。
- `MouseLeave`（`OnTagsHostLeave`）：鼠标离开标签区时复位上一个 hover 索引。

命令通过 `HoverButtonCmd` / `UnhoverButtonCmd` 下达到 ViewModel，保持「视图层只发命令、状态在 ViewModel」的分层。

---

## 3. 替代方案（被否决）

| 方案 | 内容 | 否决理由 |
|---|---|---|
| B：XAML `Tag`/`RelativeSource` 跨 ItemsControl 联动 | 用共享 `Tag`（按钮 Index）跨 `TagsHost` 与 `IconsHost` 做视觉联动 | WPF 跨独立 `ItemsControl` 做视觉联动非常脆弱，且无法表达「选中态优先级」的复合判定 |
| 图标自身 `IsMouseOver` 触发 | 初版实现：悬停图标本身才高亮 | 不符合「悬停标签 → 图标高亮」诉求；小图标命中区域仅 14×14px，普通用户几乎无法触发 |

---

## 4. 影响 / 后续

- **一致性**：标签按钮自身的 hover 视觉仍由现有 `ButtonTagStyle` 处理，本决策只动图标高亮，二者互不干扰。
- **可测性**：`SetButtonHover` 为纯逻辑（按 `IconKey` 置位），可在无 HID 硬件下补单元测试（建议：hover 按钮 i → 仅对应 `IconKey` 的图标 `IsHovered=true`；hover 非选中按钮、再移开 → 复位）。
- **可扩展性**：若未来需要「hover 标签时右侧面板预览」，复用同一 `_hoverIndex`/`IsHovered` 即可，无需新增关联结构。

---

## 5. 验证（Windows 实机）

- 鼠标移到某按键标签（不点击）→ 对应图标变蓝加粗；移开 → 恢复灰。
- 点击选中某标签 → 图标蓝粗；鼠标移开仍保持蓝粗（选中态覆盖 hover 态）。
- 悬停标签 A 时，图标 B/C 不受影响（只联动对应 `IconKey`）。
