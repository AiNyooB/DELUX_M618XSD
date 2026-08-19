# ADR: 左键改键风险确认弹窗改用独立模态窗口

- **状态**：已采纳（Accepted）
- **日期**：2026-08-17
- **决策人**：用户 + AI 代理（grill-with-docs / domain-modeling 流程产出）
- **相关代码**：`LeftButtonConfirmWindow.xaml` / `.xaml.cs`（新建模态窗口）、`MainWindow.xaml.cs`（`UpdateLeftBtnConfirm` 开合）、`AppViewModel.cs`（`LeftBtnConfirmVisible`/`LeftBtnConfirmText`/`SelectButtonCore`）、`MainWindow.xaml`（移除旧内容内遮罩）

---

## 1. 背景 / 问题

改键设置页对「左键」改键有风险二次确认（改了左键可能无法点击）。初版实现为**主窗口内容树内的遮罩层**（`Grid Grid.RowSpan="2"` + 半透明 `Border` + 居中卡片），实测暴露四个问题：

1. **遮罩盖不住标题栏**：系统标题栏是 OS 非客户区，内容树内的元素永远无法覆盖它——这是结构性限制，不是调 ZIndex 能解决的。
2. **文案不显示**：`LeftBtnConfirmText` 是普通属性，无 `INotifyPropertyChanged`，`TextBlock` 绑定只在启动时求值一次（值为空串），之后赋值永远不更新界面。
3. **卡片半透明 + 描边**：卡片背景用 `CardBackgroundFillColorDefaultBrush`（浅色 70% 白），且带 1px 警告色描边，用户规格要求「不透明、无描边」。
4. **点左键标签即展开右栏**：`SelectButton` 在弹窗前先完成选中逻辑（`SelectedButton` 已赋值 → 右侧分配功能面板可见），弹窗只是盖在上面——用户要求「点「我知道了」才展开右栏」。

### 之前的理解偏差（被本 ADR 纠正）

「遮罩覆盖整个窗口」被理解为「覆盖主窗口内容区即可」；实际上用户期望覆盖**含标题栏的完整窗口**，且弹窗期间不展开任何功能面板。内容内遮罩方案两者都做不到（前一项结构性不可能、后一项需要把选中逻辑推迟到确认后）。

---

## 2. 决策

### 2.1 独立模态窗口替代内容内遮罩

新建 `LeftButtonConfirmWindow`（`WindowStyle="None"` + `ResizeMode="NoResize"` + `ShowInTaskbar="False"`），由 `MainWindow.UpdateLeftBtnConfirm` 监听 `AppViewModel.LeftBtnConfirmVisible` 开合：

- **定位**：`Left/Top/Width/Height` 取主窗口外框（含标题栏）→ 遮罩真正覆盖整个窗口。
- **透明遮罩必须 `AllowsTransparency="True"`**：独立 `Window` 不开它时 alpha 通道被丢弃，半透明黑（RGB 纯黑）会显示成**实心黑**——这是初版「遮罩纯黑无透明度」的根因（WPF 只有同一视觉树内的合成器混合或分层窗口两种透明途径；WPF-UI 走前者，我们因要盖标题栏走后者）。`AllowsTransparency` 要求 `WindowStyle=None` + `ResizeMode=NoResize`（均已满足）。
- **模态**：`ShowDialog()` + `Owner` → 主窗口自动禁用、弹窗置顶，不可能误点底层。
- **键盘**：`IsCancel`/`IsDefault` + 初始焦点给「我知道了」→ Esc=取消、Enter=确认（内容内遮罩完全没有键盘支持）。
- **生命周期**：`Closed` 事件同步 VM 状态，Alt+F4 等外部关闭不会留下 `Visible=true` 残留导致下次打不开。

### 2.2 卡片规格：不透明实底 + 无描边；遮罩保留透明度

- **卡片**背景改用 `SolidBackgroundFillColorQuarternaryBrush`（浅色 #FFFFFF / 深色 #2C2C2C，**不透明**）；去掉 `BorderThickness`/`BorderBrush`（**无描边**），仅保留 `CornerRadius` + 柔和 `DropShadowEffect`（参数对齐 ComboBox 弹层）增强层级。
- **遮罩**新增主题令牌 `ScrimBackgroundBrush` = `#4D000000`（30% 黑，浅深同值，**对齐 WPF-UI `ContentDialogSmokeFill`**），**必须保留透明度**让底层内容（鼠标示意图等）可见；按项目令牌纪律不硬编码色值。

### 2.3 VM 侧：选中逻辑推迟到确认后

- `SelectButton` 拆分出 `SelectButtonCore`：普通键点击 → 立即 `SelectButtonCore`；**左键点击 → 只设弹窗文案 + 置可见，直接 return，不建立选中态**。
- 「我知道了」→ `ConfirmLeftBtnChange` 关闭弹窗后调用 `SelectButtonCore(0)`（此刻右栏才展开）；「取消」→ `CancelLeftBtnChange` 放弃本次切换并**回到无选中态（分配面板关闭）**——用户规格：改右键时点左键→取消，面板应关闭（初版仅左键选中时才清除，改右键场景会残留右键选中态）。
- **无变化守卫**：`ApplyEntryChange` 对「同功能码/同宏 ID」直接返回 false，不置待保存、不触发保存（用户规格：点当前已选功能不应出现「正在保存…已保存」）；DPI `SwitchLevel` 对当前档同样早退。
- `LeftBtnConfirmText` 改为走 `SetProperty` 的可通知属性（修文案不显示）；**正文只保留风险说明**（动作名由弹窗标题「修改左键」承载，用户规格去掉「你正在修改左键功能」句）。

### 2.4 取消路径恢复标签勾选态（`ReassertTagChecks`）

点击左键标签时 `RadioButton` 组互斥会顺带改动其它标签的勾选（OneWay 绑定不回写源），取消后若不恢复会出现「右侧面板还开着、但标签没有高亮」的状态分裂。`CancelLeftBtnChange` 里先置反再置回全部 `Buttons[i].IsSelected`（两次变更通知强制 OneWay 绑定重推目标侧），恢复与 VM 一致。

---

## 3. 替代方案（被否决）

| 方案 | 内容 | 否决理由 |
|---|---|---|
| 保留内容内遮罩，仅调 ZIndex/层级 | 继续用主窗口内容树内的 Grid 遮罩 | **结构性做不到**：系统标题栏不是内容树一部分，任何内容树元素都盖不住它 |
| 主窗口改 `WindowStyle=None` 自定义标题栏 | 让整个主窗口无边框，遮罩即可覆盖全窗 | 改变全站窗口外观（无系统菜单/拖动/最大化按钮），影响面过大，为单个弹窗不值得 |
| 遮罩保持内容内 + 接受标题栏不盖 | 妥协：只盖内容区 | 不满足用户明确规格（遮罩必须覆盖标题栏） |

---

## 4. 影响 / 后续

- **一致性**：弹窗卡片与全站卡片令牌体系一致（实底用 `SolidBackgroundFillColorQuarternaryBrush`，阴影对齐 ComboBox 弹层）。
- **可测性**：`SelectButtonCore` 为纯逻辑（建立选中态），可在无 HID 硬件下补单元测试（建议：`SelectButton(0)` 不置选中 → 确认后选中；取消后各标签 `IsSelected` 与弹窗前一致）。
- **可扩展性**：未来其它危险操作（恢复出厂、电源命令、Profile 删除）可复用同一模态窗口模式（`UpdateLeftBtnConfirm` 的模板：Owner 定位 + Closed 同步），只需把文案/命令换成各自的。
- **注意**：`ShowDialog` 是嵌套消息循环（重入），`LeftBtnConfirmVisible` 置 false 的路径必须走 `Close()` 让 `ShowDialog` 返回；已用 `Closed` 事件兜底外部关闭。

---

## 5. 验证（Windows 实机）

- 点击左键标签 → 弹窗出现，遮罩覆盖**含标题栏**的整个窗口，底部主窗口不可交互。
- 弹窗卡片为不透明实底、无描边；遮罩为半透明（30% 黑，`AllowsTransparency` 生效，非纯黑），底层鼠标示意图透过可见；标题「修改左键」+ 正文「当前只有一个左键，改了后可能无法点击」。
- 弹窗出现时右侧分配功能面板**不展开**；点「我知道了」后面板才出现且左键选中；点「取消」后面板状态与弹窗前一致。
- 按 Esc = 取消、Enter = 我知道了；Alt+F4 关闭后再次点左键标签仍能正常弹出。
- 「取消」按钮为 `SecondaryButtonStyle`（浅填充 + 1px 描边），与「我知道了」`PrimaryButtonStyle` 风格统一（初版用 `TextButtonStyle` 露出 Fluent 默认 chrome，并排割裂，见 DESIGN.md §5.11）。
- 改右键时点左键→取消：右栏面板关闭、无选中态；点击当前已选功能：不出现「正在保存…」；宏 Tab 点击未绑定按钮的宏 ID：正常绑定（不再「点了没反应」）。
