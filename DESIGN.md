# DELUX M618XSD 客户端 · 设计系统

> 本文档记录 DELUX.Driver 的设计系统（Design System），涵盖设计令牌、色板、排版标度、组件样式、主题切换机制及使用指南。
> 所有 UI 实现应优先使用此处定义的令牌与样式，避免硬编码色值/字号/间距。

---

## 目录

1. [设计系统架构](#1-设计系统架构)
2. [设计令牌（DesignTokens.xaml）](#2-设计令牌designtokensxaml)
3. [排版标度（PageStyles.xaml）](#3-排版标度pagestylesxaml)
4. [颜色系统](#4-颜色系统)
   - [4.1 浅色主题色板](#41-浅色主题色板)
   - [4.2 深色主题色板](#42-深色主题色板)
   - [4.3 品牌色](#43-品牌色)
   - [4.4 旧键兼容别名](#44-旧键兼容别名)
5. [组件样式库（Styles.xaml）](#5-组件样式库stylesxaml)
6. [主题切换机制](#6-主题切换机制)
7. [设计系统文件清单](#7-设计系统文件清单)
8. [开发者使用指南](#8-开发者使用指南)

---

## 1. 设计系统架构

设计系统采用**三层架构**，层级分明、职责分离：

```
┌─────────────────────────────────────────────────────┐
│  ③ 组件样式层  Styles.xaml                          │
│    消费 DesignTokens + 主题色板，定义 Card/Button/…  │
├─────────────────────────────────────────────────────┤
│  ② 主题色板层  LightTheme.xaml / DarkTheme.xaml     │
│    语义颜色令牌（Fluent/WinUI 命名），随主题切换     │
├─────────────────────────────────────────────────────┤
│  ① 设计令牌层  DesignTokens.xaml                    │
│    字体/圆角/间距/动效，不随主题变化                 │
├─────────────────────────────────────────────────────┤
│  ╟ 排版标度层  PageStyles.xaml                       │
│    字号层级 + TextBlock 样式（Caption→Display）      │
├─────────────────────────────────────────────────────┤
│  ╟ 基础主题层  PresentationFramework.Fluent          │
│    WPF 内置 Fluent 主题，提供 ControlCornerRadius 等 │
└─────────────────────────────────────────────────────┘
```

- **第①层**：主题无关的设计令牌，任何主题下值不变。
- **第②层**：定义浅色/深色各自的语义颜色，通过 `App.ApplyTheme()` 运行时切换。
- **第③层**：组件样式通过 `DynamicResource` 引用第①、②层的令牌，实现自动主题跟随。
- **排版标度**：独立于三层之外，提供字号系统和 TextBlock 样式。
- **基础主题层**：.NET WPF 内置 `PresentationFramework.Fluent`，提供 Fluent Design 基础控件样式（如 `ControlCornerRadius`=4、`OverlayCornerRadius`=8）。

---

## 2. 设计令牌（DesignTokens.xaml）

**文件**：`Themes/DesignTokens.xaml`

对齐 Windows 11 Fluent 规范与 WPF Gallery（WinUI 语义命名）。本文件只定义**主题无关**的令牌，不包含任何颜色。

> ⛔ **令牌集已冻结：不允许新增 DesignToken。** 新页面 / 新组件一律复用现有令牌（间距用 `Space*`/`Margin*`/`Padding*`、圆角用 `CardCornerRadius` 等，见下方各表）；间距/圆角/字号不够用时，用**现有令牌组合**或页面级样式解决，不新建全局令牌。确需调整现有令牌的值（如修正错误值），改前先与用户确认，改后同步更新本文档。

### 2.1 字体

| Key | 类型 | 值 | 用途 |
|---|---|---|---|
| `AppFontFamily` | `FontFamily` | `Segoe UI Variable, Segoe UI` | 全站默认字体族 |

### 2.2 字号补充

| Key | 类型 | 值 | 说明 |
|---|---|---|---|
| `BodyLargeTextBlockFontSize` | `Double` | 18 | 位于 Body(14) 与 Subtitle(20) 之间的层级 |

> 完整字号标度见[第 3 节](#3-排版标度pagestylesxaml)。

### 2.3 圆角

| Key | 类型 | 值 | 用途 |
|---|---|---|---|
| `CardCornerRadius` | `CornerRadius` | 8 | 卡片容器圆角 |
| `ImageCornerRadius` | `CornerRadius` | 6 | 图片容器圆角 |
| `NavigationItemCornerRadius` | `CornerRadius` | 6 | 侧边导航项圆角 |

> 控件/弹层圆角由 `PresentationFramework.Fluent` 提供：`ControlCornerRadius`=4、`OverlayCornerRadius`=8、`PopupCornerRadius`=8。

### 2.4 边框宽度

| Key | 类型 | 值 | 用途 |
|---|---|---|---|
| `ControlStrokeThickness` | `Thickness` | 1 | 控件/卡片默认边框 |
| `FocusStrokeThickness` | `Thickness` | 2 | 焦点边框 |
| `SelectionStrokeThickness` | `Thickness` | 2.5 | 选中态边框 |

### 2.5 间距

基于 4px 基数的 Fluent 间距标度：

| Key | 类型 | 值 | 用途示例 |
|---|---|---|---|
| `PagePadding` | `Thickness` | 24 | 页面级内边距 |
| `CardPadding` | `Thickness` | 16 | 卡片内边距 |
| `Space2` | `Thickness` | 2 | 均匀间距（Margin/Padding 直接引用） |
| `Space4` | `Thickness` | 4 | 均匀间距（Margin/Padding 直接引用） |
| `Space8` | `Thickness` | 8 | 均匀间距（Margin/Padding 直接引用） |
| `Space12` | `Thickness` | 12 | 均匀间距（Margin/Padding 直接引用） |
| `Space16` | `Thickness` | 16 | 均匀间距（Margin/Padding 直接引用） |
| `Space24` | `Thickness` | 24 | 均匀间距（Margin/Padding 直接引用） |
| `Space32` | `Thickness` | 32 | 均匀间距（Margin/Padding 直接引用） |
| `Space48` | `Thickness` | 48 | 均匀间距（Margin/Padding 直接引用） |

> ⚠️ `Space*` 必须保持 `Thickness` 类型（历史事故：曾定义为 `sys:Double` 并被用于 `Margin`，布局抛 `“12”不是属性“Margin”的有效值`，窗口启动即崩）。类型约定见 [§2.7](#27-令牌类型约定防止布局崩溃)。

### 2.6 动效

| Key | 类型 | 值 | 用途 |
|---|---|---|---|
| `ControlFasterAnimationDuration` | `Duration` | 00:00:00.083 (83ms) | 极快动效 |
| `ControlFastAnimationDuration` | `Duration` | 00:00:00.167 (167ms) | 快速动效 |
| `ControlNormalAnimationDuration` | `Duration` | 00:00:00.250 (250ms) | 标准动效 |
| `ControlSlowAnimationDuration` | `Duration` | 00:00:00.500 (500ms) | 慢速动效 |
| `ControlFastOutSlowInKeySpline` | `String` | `0,0,0,1` | 标准加速曲线（快进慢出） |

### 2.7 令牌类型约定（防止布局崩溃）

WPF 属性对令牌的类型有**硬性要求**，类型不匹配在**布局时**才暴露：`Double` 令牌赋给 `Margin`（`Thickness`）时，首次 Measure 抛 `InvalidOperationException: “12”不是属性“Margin”的有效值`，`Window.Show()` 同步崩、窗口不显示（历史事故：`Space*` 曾定义为 `sys:Double` 且被用于 `Margin`，2026-08-18 启动即崩，见上）。

**硬性规则：**

1. **属性类型与令牌类型一一对应**：

   | 属性 | 令牌类型 |
   |---|---|
   | `Margin` / `Padding` / `BorderThickness` | `Thickness` |
   | `CornerRadius` | `CornerRadius` |
   | `FontSize` / `Width` / `Height` / `MinWidth` / `MaxWidth` / `MinHeight` / `MaxHeight` / `Opacity` / `StrokeThickness` | `Double`（`sys:Double`） |
   | `Duration` | `Duration` |

2. **间距令牌（`Space*`）一律定义为均匀 `Thickness`**（供 `Margin`/`Padding` 直接引用），**禁止**定义成 `sys:Double`。确需纯数字间距（如 `Width`/`Height`）时另建 `Double` 令牌，命名区分用途（如 `SpaceWidth*`），**禁止一个令牌同时给两种属性用**。

3. **引用前确认 `x:Key` 已定义**：`DynamicResource` 对缺失键**静默失败**（属性保持未设置、不报错），会留下隐形布局缺陷（margin 变 0）。引用前先在 `DesignTokens.xaml` 核对；**令牌集已冻结，缺失时不得新增令牌**，改用现有令牌（如用 `MarginTop14`/`Space12` 替代曾缺失的 `MarginTop12`）或页面级样式。

4. **令牌集已冻结（见 §2 头注）：不允许新增 DesignToken。** 确需调整现有令牌的值时，须保持本规则的类型约定，改前与用户确认，改后同步更新本文档 §2 各表，避免类型漂移。

5. **排查布局崩溃**：`Window.Show()` 抛 `“XX”不是属性“X”的有效值` → 在启动路径可见元素上找类型不匹配的令牌引用（典型：`Double` 上 `Margin`、`Thickness` 上 `FontSize`）。

---

## 3. 排版标度（PageStyles.xaml）

**文件**：`Themes/WPFGallery/PageStyles.xaml`

### 3.1 字号层级

| Key | 类型 | 值 | 对应样式 |
|---|---|---|---|
| `CaptionTextBlockFontSize` | `Double` | 12 | `CaptionTextBlockStyle` |
| `BodyTextBlockFontSize` | `Double` | 14 | `BodyTextBlockStyle` |
| `BodyLargeTextBlockFontSize` | `Double` | 18 | —（DesignTokens 补充） |
| `SubtitleTextBlockFontSize` | `Double` | 20 | `SubtitleTextBlockStyle` |
| `TitleTextBlockFontSize` | `Double` | 28 | `TitleTextBlockStyle` |
| `TitleLargeTextBlockFontSize` | `Double` | 40 | `TitleLargeTextBlockStyle` |
| `DisplayTextBlockFontSize` | `Double` | 68 | `DisplayTextBlockStyle` |

### 3.2 其他令牌

| Key | 类型 | 值 | 用途 |
|---|---|---|---|
| `DeemphasizedTextOpacity` | `Double` | 0.7 | 弱化文字透明度 |

### 3.3 TextBlock 样式

所有样式继承自 `BaseTextBlockStyle`（字体族=AppFontFamily、字号=14、字重=SemiBold、自动换行），各样式仅覆写 `FontSize` 与 `FontWeight`：

| 样式 Key | 字号 | 字重 | 用途 |
|---|---|---|---|
| `CaptionTextBlockStyle` | 12 | Normal | 辅助说明、标签 |
| `BodyTextBlockStyle` | 14 | Normal | 正文 |
| `BodyStrongTextBlockStyle` | 14 | SemiBold | 强调正文 |
| `SubtitleTextBlockStyle` | 20 | SemiBold | 副标题 |
| `TitleTextBlockStyle` | 28 | SemiBold | 标题 |
| `TitleLargeTextBlockStyle` | 40 | SemiBold | 大标题 |
| `DisplayTextBlockStyle` | 68 | SemiBold | 展示文字 |

---

## 4. 颜色系统

### 4.1 浅色主题色板

**文件**：`Themes/LightTheme.xaml`

品牌色：**DELUX 蓝** `#0067C0`

#### 文字填充（TextFill）

| 语义键 | 色值 | 用途 |
|---|---|---|
| `TextFillColorPrimaryBrush` | `#E4000000` | 主文字 |
| `TextFillColorSecondaryBrush` | `#9E000000` | 次要文字 |
| `TextFillColorTertiaryBrush` | `#72000000` | 第三级文字 |
| `TextFillColorDisabledBrush` | `#5C000000` | 禁用文字 |
| `TextFillColorInverseBrush` | `#FFFFFF` | 反色文字 |

#### 强调色文字（AccentTextFill / TextOnAccent）

| 语义键 | 色值 | 用途 |
|---|---|---|
| `AccentTextFillColorPrimaryBrush` | `#0067C0` | 强调色主文字 |
| `AccentTextFillColorSecondaryBrush` | `#004D94` | 强调色次要文字 |
| `AccentTextFillColorTertiaryBrush` | `#4C8CC9` | 强调色第三级文字 |
| `TextOnAccentFillColorPrimaryBrush` | `#FFFFFF` | 强调色背景上的主文字 |
| `TextOnAccentFillColorSecondaryBrush` | `#B3FFFFFF` | 强调色背景上的次要文字 |
| `TextOnAccentFillColorSelectedTextBrush` | `#FFFFFF` | 选中文字 |
| `TextOnAccentFillColorDisabledBrush` | `#FFFFFF` | 禁用态文字 |

#### 背景（SolidBackground）

| 语义键 | 色值 | 用途 |
|---|---|---|
| `ApplicationBackgroundColorBrush` | `#FAFAFA` | 应用背景 |
| `SolidBackgroundFillColorBaseBrush` | `#F3F3F3` | 基础填充 |
| `SolidBackgroundFillColorSecondaryBrush` | `#EEEEEE` | 次要填充 |
| `SolidBackgroundFillColorTertiaryBrush` | `#F9F9F9` | 第三级填充 |
| `SolidBackgroundFillColorQuarternaryBrush` | `#FFFFFF` | 第四级填充 |
| `SolidBackgroundFillColorBaseAltBrush` | `#DADADA` | 基础交替填充 |

#### 卡片/层背景（Card / Layer）

| 语义键 | 色值 | 用途 |
|---|---|---|
| `CardBackgroundFillColorDefaultBrush` | `#B3FFFFFF` | 卡片默认背景 |
| `CardBackgroundFillColorSecondaryBrush` | `#80F6F6F6` | 卡片次要背景 |
| `CardBackgroundFillColorDimmedBrush` | `#E8E8E8` | 卡片变灰背景（连接页图片卡失败态；深色 `#3A3A3A`） |
| `LayerFillColorDefaultBrush` | `#80FFFFFF` | 层默认背景 |
| `LayerFillColorAltBrush` | `#FFFFFF` | 层交替背景 |

#### 控件填充（ControlFill / SubtleFill / ControlAltFill）

| 语义键 | 色值 | 用途 |
|---|---|---|
| `ControlFillColorDefaultBrush` | `#B3FFFFFF` | 控件默认填充 |
| `ControlFillColorSecondaryBrush` | `#80F9F9F9` | 控件次要填充 |
| `ControlFillColorTertiaryBrush` | `#4DF9F9F9` | 控件第三级填充 |
| `ControlFillColorDisabledBrush` | `#4DF9F9F9` | 控件禁用填充 |
| `ControlFillColorTransparentBrush` | `#00FFFFFF` | 控件透明填充 |
| `ControlFillColorInputActiveBrush` | `#FFFFFF` | 输入框活跃态填充 |
| `ControlStrongFillColorDefaultBrush` | `#72000000` | 强控件默认填充 |
| `ControlStrongFillColorDisabledBrush` | `#51000000` | 强控件禁用填充 |
| `ControlSolidFillColorDefaultBrush` | `#FFFFFF` | 实心控件默认填充 |
| `SubtleFillColorTransparentBrush` | `#00FFFFFF` | 细微透明填充 |
| `SubtleFillColorSecondaryBrush` | `#09000000` | 细微次要填充 |
| `SubtleFillColorTertiaryBrush` | `#06000000` | 细微第三级填充 |
| `SubtleFillColorDisabledBrush` | `#00FFFFFF` | 细微禁用填充 |
| `ControlAltFillColorTransparentBrush` | `#00FFFFFF` | 交替透明填充 |
| `ControlAltFillColorSecondaryBrush` | `#06000000` | 交替次要填充 |
| `ControlAltFillColorTertiaryBrush` | `#0F000000` | 交替第三级填充 |
| `ControlAltFillColorQuarternaryBrush` | `#18000000` | 交替第四级填充 |
| `ControlAltFillColorDisabledBrush` | `#00FFFFFF` | 交替禁用填充 |

#### 强调色（Accent）

| 语义键 | 色值 | 用途 |
|---|---|---|
| `AccentFillColorDefaultBrush` | `#0067C0` | 强调色默认（品牌色） |
| `AccentFillColorSecondaryBrush` | `#2E7ACC` | 强调色次要 |
| `AccentFillColorTertiaryBrush` | `#337CC6` | 强调色第三级 |
| `AccentFillColorDisabledBrush` | `#37000000` | 强调色禁用 |
| `AccentFillColorSelectedTextBackgroundBrush` | `#0067C0` | 选中文字背景 |

#### 描边（Stroke）

| 语义键 | 色值 | 用途 |
|---|---|---|
| `ControlStrokeColorDefaultBrush` | `#0F000000` | 控件默认描边 |
| `ControlStrokeColorSecondaryBrush` | `#29000000` | 控件次要描边 |
| `ControlStrongStrokeColorDefaultBrush` | `#72000000` | 强控件默认描边 |
| `ControlStrongStrokeColorDisabledBrush` | `#37000000` | 强控件禁用描边 |
| `CardStrokeColorDefaultBrush` | `#0F000000` | 卡片默认描边 |
| `CardStrokeColorDefaultSolidBrush` | `#EBEBEB` | 卡片实心描边 |
| `DividerStrokeColorDefaultBrush` | `#0F000000` | 分割线描边 |
| `SurfaceStrokeColorDefaultBrush` | `#66757575` | 表面描边 |
| `FocusStrokeColorOuterBrush` | `#E4000000` | 焦点外描边 |
| `FocusStrokeColorInnerBrush` | `#B3FFFFFF` | 焦点内描边 |

#### 系统状态色（SystemFill）

| 语义键 | 色值 | 用途 |
|---|---|---|
| `SystemFillColorSuccessBrush` | `#0F7B0F` | 成功文字/图标 |
| `SystemFillColorCautionBrush` | `#9D5D00` | 警告文字/图标 |
| `SystemFillColorCriticalBrush` | `#C42B1C` | 错误文字/图标 |
| `SystemFillColorNeutralBrush` | `#72000000` | 中性文字/图标 |
| `SystemFillColorSuccessBackgroundBrush` | `#DFF6DD` | 成功背景 |
| `SystemFillColorCautionBackgroundBrush` | `#FFF4CE` | 警告背景 |
| `SystemFillColorCriticalBackgroundBrush` | `#FDE7E9` | 错误背景 |
| `SystemFillColorNeutralBackgroundBrush` | `#06000000` | 中性背景 |

#### 键盘焦点边框

| 语义键 | 色值 |
|---|---|
| `KeyboardFocusBorderColorBrush` | `#BE000000` |

#### 遮罩（Scrim）

| 语义键 | 色值 | 用途 |
|---|---|---|
| `ScrimBackgroundBrush` | `#4D000000`（30% 黑，对齐 WPF-UI `ContentDialogSmokeFill`） | 模态弹窗背景遮罩，**保留透明度**让底层内容可见 |

---

### 4.2 深色主题色板

**文件**：`Themes/DarkTheme.xaml`

品牌色：**DELUX 蓝** `#4CC2FF`

结构与浅色主题完全一致，仅色值不同。以下列出关键差异：

| 类别 | 浅色主值 | 深色主值 |
|---|---|---|
| 应用背景 `ApplicationBackgroundColorBrush` | `#FAFAFA` | `#202020` |
| 主文字 `TextFillColorPrimaryBrush` | `#E4000000` | `#FFFFFF` |
| 次要文字 `TextFillColorSecondaryBrush` | `#9E000000` | `#C5FFFFFF` |
| 品牌色 `AccentFillColorDefaultBrush` | `#0067C0` | `#4CC2FF` |
| 成功 `SystemFillColorSuccessBrush` | `#0F7B0F` | `#6CCB5F` |
| 警告 `SystemFillColorCautionBrush` | `#9D5D00` | `#FCE100` |
| 错误 `SystemFillColorCriticalBrush` | `#C42B1C` | `#FF99A4` |
| 卡片背景 `CardBackgroundFillColorDefaultBrush` | `#B3FFFFFF` | `#0DFFFFFF` |
| 控件默认填充 `ControlFillColorDefaultBrush` | `#B3FFFFFF` | `#0FFFFFFF` |
| 遮罩 `ScrimBackgroundBrush` | `#4D000000`（30% 黑，对齐 WPF-UI `ContentDialogSmokeFill`） | `#4D000000`（30% 黑，浅深同值） |

> 完整色值见 `Themes/DarkTheme.xaml` 文件，结构与浅色主题一一对应。

### 4.3 品牌色

| 模式 | 色值 | 用途 |
|---|---|---|
| 浅色 | `#0067C0` | 强调色默认、选中态、主操作按钮 |
| 深色 | `#4CC2FF` | 同上（深色背景上的高对比度版本） |

`AccentFillColorDefaultBrush` 为品牌色主色，`AccentFillColorSecondaryBrush`/`TertiaryBrush` 为 Hover/Pressed 态递进。

### 4.4 旧键兼容别名

两个主题文件末尾均保留了旧键别名，用于渐进迁移期兼容。**新代码一律使用上方 Fluent 语义键**，勿使用别名。

| 旧键 | 映射到 Fluent 语义键 |
|---|---|
| `AppBackgroundBrush` | `SolidBackgroundFillColorBaseBrush` |
| `WindowBackgroundBrush` | `SolidBackgroundFillColorQuarternaryBrush` |
| `CardBackgroundBrush` | `CardBackgroundFillColorDefaultBrush` |
| `ForegroundBrush` | `TextFillColorPrimaryBrush` |
| `SubForegroundBrush` | `TextFillColorSecondaryBrush` |
| `BorderBrush` | `ControlStrokeColorDefaultBrush` |
| `CardBorderBrush` | `CardStrokeColorDefaultBrush` |
| `AccentBrush` | `AccentFillColorDefaultBrush` |
| `AccentTextBrush` | `TextOnAccentFillColorPrimaryBrush` |
| `NavActiveBackgroundBrush` | 导航选中态 |
| `NavHoverBackgroundBrush` | 导航悬停态 |

---

## 5. 组件样式库（Styles.xaml）

**文件**：`Themes/Styles.xaml`

所有样式通过 `DynamicResource` 引用 DesignTokens 令牌和主题色板，实现自动跟随主题切换。

### 5.1 CardStyle（卡片容器）

| 属性 | 值 |
|---|---|
| TargetType | `Border` |
| 背景 | `{DynamicResource CardBackgroundFillColorDefaultBrush}` |
| 描边 | `{DynamicResource CardStrokeColorDefaultBrush}` |
| 边框厚度 | `{DynamicResource ControlStrokeThickness}` (1) |
| 圆角 | `{DynamicResource CardCornerRadius}` (8) |
| 内边距 | `{DynamicResource CardPadding}` (16) |

**用途**：页面配置区的卡片容器。

### 5.2 IconStyle（图标）

| 属性 | 值 |
|---|---|
| TargetType | `TextBlock` |
| 字体族 | `{DynamicResource SymbolThemeFontFamily}`（Segoe MDL2） |
| 字号 | `{DynamicResource DefaultIconFontSize}` |
| 前景色 | `{DynamicResource AccentFillColorDefaultBrush}`（品牌色） |
| 垂直对齐 | Center |

**用途**：Segoe MDL2 / Fluent 图标文本块。

### 5.3 NavItemStyle（侧边导航项）

| 属性 | 值 |
|---|---|
| TargetType | `ListBoxItem` |
| Padding | 10,8 |
| Margin | 0,2 |
| Cursor | Hand |
| 前景色 | `{DynamicResource TextFillColorPrimaryBrush}` |
| 圆角 | `{DynamicResource NavigationItemCornerRadius}` (6) |

**状态**：
- 悬停：背景 `ControlAltFillColorSecondaryBrush`
- 选中：背景 `SubtleFillColorSecondaryBrush`

### 5.4 PageTitleTextStyle（页面标题）

| 属性 | 值 |
|---|---|
| TargetType | `TextBlock` |
| 字体族 | `{DynamicResource AppFontFamily}` |
| 字号 | `{DynamicResource TitleTextBlockFontSize}` (28) |
| 字重 | SemiBold |
| 前景色 | `{DynamicResource TextFillColorPrimaryBrush}` |

### 5.5 PageSubtitleTextStyle（页面副标题）

| 属性 | 值 |
|---|---|
| TargetType | `TextBlock` |
| 字体族 | `{DynamicResource AppFontFamily}` |
| 字号 | `{DynamicResource BodyTextBlockFontSize}` (14) |
| 前景色 | `{DynamicResource TextFillColorSecondaryBrush}` |
| 自动换行 | Wrap |

### 5.6 CardTitleTextStyle（卡片内标题）

| 属性 | 值 |
|---|---|
| TargetType | `TextBlock` |
| 字体族 | `{DynamicResource AppFontFamily}` |
| 字号 | `{DynamicResource BodyLargeTextBlockFontSize}` (18) |
| 字重 | SemiBold |
| 前景色 | `{DynamicResource TextFillColorPrimaryBrush}` |

### 5.7 CardDescriptionTextStyle（卡片内说明）

| 属性 | 值 |
|---|---|
| TargetType | `TextBlock` |
| 字体族 | `{DynamicResource AppFontFamily}` |
| 字号 | `{DynamicResource BodyTextBlockFontSize}` (14) |
| 前景色 | `{DynamicResource TextFillColorSecondaryBrush}` |
| 自动换行 | Wrap |

### 5.8 InfoBannerStyle（信息提示条基类）

| 属性 | 值 |
|---|---|
| TargetType | `Border` |
| 圆角 | `{DynamicResource OverlayCornerRadius}` (8) |
| 内边距 | 12,10 |

派生样式：
- `ErrorBannerStyle`：背景 `SystemFillColorCriticalBackgroundBrush`
- `CautionBannerStyle`：背景 `SystemFillColorCautionBackgroundBrush`

配套文本样式：
- `ErrorBannerTextStyle`：前景 `SystemFillColorCriticalBrush`、字号 14、字重 SemiBold
- `CautionBannerTextStyle`：前景 `SystemFillColorCautionBrush`、字号 14、字重 SemiBold

### 5.9 TextButtonStyle（文字按钮）

| 属性 | 值 |
|---|---|
| TargetType | `Button` |
| 字体族 | `{DynamicResource AppFontFamily}` |
| 字号 | `{DynamicResource BodyTextBlockFontSize}` (14) |
| Padding | 12,6 |
| Cursor | Hand |

### 5.10 PrimaryButtonStyle（主要操作按钮）

| 属性 | 值 |
|---|---|
| TargetType | `Button` |
| 背景 | `{DynamicResource AccentFillColorDefaultBrush}`（品牌色） |
| 前景色 | `{DynamicResource TextOnAccentFillColorPrimaryBrush}`（白色） |
| 字体族 | `{DynamicResource AppFontFamily}` |
| 字号 | 14 |
| Padding | 16,8 |
| 边框厚度 | 0 |
| 圆角 | `{DynamicResource ControlCornerRadius}` (4) |

**状态**：
- 悬停：背景 `AccentFillColorSecondaryBrush`
- 按下：背景 `AccentFillColorTertiaryBrush`
- 禁用：背景 `AccentFillColorDisabledBrush`、前景 `TextFillColorDisabledBrush`

### 5.11 SecondaryButtonStyle（次要操作按钮）

| 属性 | 值 |
|---|---|
| TargetType | `Button` |
| 背景 | `{DynamicResource ControlFillColorDefaultBrush}`（浅填充） |
| 前景色 | `{DynamicResource TextFillColorPrimaryBrush}` |
| 描边 | `{DynamicResource ControlStrokeColorDefaultBrush}`（1px） |
| 字体族 | `{DynamicResource AppFontFamily}` |
| 字号 | 14 |
| Padding | 16,8（与 PrimaryButtonStyle 同尺寸） |
| 圆角 | `{DynamicResource ControlCornerRadius}` (4) |

**状态**：
- 悬停：背景 `ControlFillColorSecondaryBrush`、描边 `ControlStrokeColorSecondaryBrush`
- 按下：背景 `ControlFillColorTertiaryBrush`
- 禁用：背景 `ControlFillColorDisabledBrush`、前景 `TextFillColorDisabledBrush`

> 用于弹窗「取消」等次级动作。**注意**：`TextButtonStyle` 无自定义模板（露出 Fluent 默认 chrome），与自研 `PrimaryButtonStyle` 并排时风格割裂，弹窗次级按钮一律用本样式。

### 5.12 ComboBoxStyle（下拉选择框，隐式覆盖 Fluent 默认）

> **隐式样式**（`TargetType="ComboBox"` 无 `x:Key`）：合并进 App.xaml 后自动覆盖 WPF 内置 Fluent 主题的默认样式，全站所有 ComboBox 生效，无需逐处引用。

**背景**：WPF 内置 Fluent 主题的 ComboBox 有两个已知视觉缺陷——弹层背景用 Acrylic 半透明色（深色主题下透出桌面，即「透明层」），且弹层 1.5px 深色描边 + item 间 3px 缝隙导致 item 周围显「黑边」。本样式按官方模板结构（`wpf-main/.../Fluent.Light.xaml`）重写，仅替换视觉层，功能部件全部保留。

| 属性 | 值 |
|---|---|
| TargetType | `ComboBox`（隐式） |
| 背景 | `{DynamicResource ControlFillColorDefaultBrush}` |
| 描边 | `{DynamicResource ControlStrokeColorDefaultBrush}`（1px） |
| 圆角 | `{DynamicResource ControlCornerRadius}` (4) |
| 弹层背景 | `{DynamicResource SolidBackgroundFillColorQuarternaryBrush}`（**不透明**，修复「透明层」） |
| 弹层描边 | `{DynamicResource CardStrokeColorDefaultBrush}`（1px，修复「黑边」） |
| 弹层圆角 | `{DynamicResource PopupCornerRadius}` (8) |
| 弹层取整 | `UseLayoutRounding="True"`（替代官方 `SnapsToDevicePixels`：圆角在非整数边界（如 125% 缩放下 105.7×1.25=132.125px）上会掉角成直角，`UseLayoutRounding` 是 WPF 推荐的布局取整机制） |
| 弹层边距 | `Margin="30,0,30,30"`（左/右/下 30px 给阴影留窗口空间，顶部 0 保持弹层贴紧组合框；Margin 计入弹层窗口尺寸，等效 WPF-UI 的 `EffectThicknessDecorator` 机制） |
| 弹层宽度 | `MinWidth="{TemplateBinding ActualWidth}"` 在 **DropDownBorder** 上（不在 Popup 上）：窗口宽度=Border+边距，Border 本体宽度=组合框宽，弹层与下拉框宽度一致；若 MinWidth 在 Popup 上会被 60px 边距瓜分，弹层窄 60px（对照 WPF-UI ComboBox.xaml） |
| 弹层阴影 | `DropShadowEffect`：`BlurRadius=20 Direction=270 Opacity=0.135 ShadowDepth=10 Color=#202020`（对照 WPF-UI 成熟实现，官方 Fluent 的 0.25/6 偏生硬） |
| Padding | 12,5,0,7（右 0 为箭头列预留） |

**模板部件（对照官方必须保留）**：
- `PART_ContentPresenter`：显示选中项（`SelectionBoxItem` 绑定）
- `ToggleButton`：全区域开合热区（`IsChecked` TwoWay 绑 `IsDropDownOpen`）
- `PART_Popup` + `ScrollViewer`（`MaxDropDownHeight`）+ `ItemsPresenter`：下拉容器
- `PART_EditableTextBox`：仅编辑态模板，`IsEditable=True` 时通过 `Style.Triggers` 切换到编辑模板

**状态**：悬停 `ControlFillColorSecondaryBrush`、按下 `ControlFillColorTertiaryBrush`、禁用 `ControlFillColorDisabledBrush` + 前景 `TextFillColorDisabledBrush`；弹层带 `DropShadowEffect` 阴影与 167ms 展开动画。

### 5.13 ComboBoxItemStyle（下拉项，隐式覆盖 Fluent 默认）

> 同样为**隐式样式**，与 ComboBoxStyle 成对出现；两者必须一起覆盖，否则 item 仍走 Fluent 默认模板。

| 属性 | 值 |
|---|---|
| TargetType | `ComboBoxItem`（隐式） |
| Margin | 0（**去掉官方 3,2,3,0 缝隙**，让 hover 高亮铺满弹层宽度） |
| Padding | 10,8 |
| 圆角 | `{DynamicResource ControlCornerRadius}` (4) |
| 前景色 | `{DynamicResource TextFillColorPrimaryBrush}` |

**状态**：
- 悬停：背景 `SubtleFillColorSecondaryBrush`
- 选中：背景同悬停 + 左侧 3px 强调色竖条（`AccentFillColorDefaultBrush`）
- 禁用：前景 `TextFillColorDisabledBrush`

### 5.14 DpiCardRadioStyle（DPI 档位卡片，整卡可点）

> Keyed 样式（`x:Key="DpiCardRadioStyle"`），TargetType `RadioButton`，用于 DPI 页 5 档卡片横排（UniformGrid）。**整卡即单选按钮**：点卡片空白处 = 设为当前；卡片内的 TextBox/CheckBox 各自拦截点击，不误触切档。

| 属性 | 值 |
|---|---|
| Cursor | Hand |
| 模板 | `Border`（卡片：`CardBackgroundFillColorDefaultBrush` 底 + `CardStrokeColorDefaultBrush` 描边 + `CardCornerRadius`）+ `ContentPresenter` |

**状态触发器**（卡片 Border 上，数据源 = 卡片绑定的档位项）：
- 当前档（`IsChecked=True`）：描边 `AccentFillColorDefaultBrush`、1.5px
- 数值非法（`HasError=True`，优先级高）：描边 `SystemFillColorCriticalBrush` 1.5px + tooltip「请输入 40-4800 且为 40 的倍数」

**卡片内容约定**（数据模板内，三行结构）：
- 第一行：色点 10px（`IndicatorBrush`，固定色模拟鼠标 DPI 指示灯：红/绿/蓝/紫/黄 = DPI 1..5，不随主题、**不随选中态**——仅作该档颜色标识）+ 档位名；右侧启用勾选（**hover/焦点才显示**，Opacity 0↔1 保留占位）；**当前档位指示 = 卡片描边**（选中态强调色 1.5px，色点不参与指示）
- 第二行：数值大字（26px，绑定 `Value`，仅展示）+ 数值输入框（104px）
- 第三行：`Slider` Min 40 / Max 4800、TickFrequency 400（视觉刻度）、`IsMoveToPointEnabled`，TwoWay 绑定 `SliderValue`（VM 辅助属性：get=最近合法值、set=40 倍数取整后写回 `Value`，保证滑块输出恒合法，校验/保存逻辑不动）

### 5.15 ButtonTagStyle（改键页按钮标签，整块可点）

> Keyed 样式（`x:Key="ButtonTagStyle"`），TargetType `RadioButton`，用于改键设置页 10 个可编程按钮标签（WrapPanel 两行 5+5）。整块可点 = 选中按钮。

| 属性 | 值 |
|---|---|
| Cursor | Hand |
| 模板 | `Border`（`SubtleFillColorSecondaryBrush` 底 + `CardStrokeColorDefaultBrush` 描边 + `ControlCornerRadius` 圆角）+ `ContentPresenter` |

**状态触发器**（Border 上，颜色全在 Style Setter——本地值会压触发器）：
- 悬停：背景 `ControlFillColorSecondaryBrush`
- 选中：描边 `AccentFillColorDefaultBrush` 1.5px + 背景 `SubtleFillColorTertiaryBrush`

**内容约定**（数据模板内）：按钮名（13px SemiBold）+ 当前功能名（11px 次要色）。

---

## 6. 主题切换机制

### 6.1 资源字典加载顺序（App.xaml）

```xml
<ResourceDictionary.MergedDictionaries>
    <!-- ① WPF 内置 Fluent 主题 -->
    <ResourceDictionary Source="PresentationFramework.Fluent;component/Themes/Fluent.xaml" />
    <!-- ② 设计令牌（主题无关） -->
    <ResourceDictionary Source="Themes/DesignTokens.xaml" />
    <!-- ③ 浅色主题色板（启动时默认） -->
    <ResourceDictionary Source="Themes/LightTheme.xaml" />
    <!-- ④ 组件样式 -->
    <ResourceDictionary Source="Themes/Styles.xaml" />
    <!-- ⑤ 排版标度 -->
    <ResourceDictionary Source="Themes/WPFGallery/PageStyles.xaml" />
</ResourceDictionary.MergedDictionaries>
```

> **注意**：`DarkTheme.xaml` 不在 App.xaml 中静态引用，而是通过 `App.ApplyTheme()` 在运行时动态替换。

### 6.2 运行时切换

`App.ApplyTheme(bool isDark)` 方法：

1. 创建一个新的 `ResourceDictionary`，Source 指向 `LightTheme.xaml` 或 `DarkTheme.xaml`
2. 遍历 `Application.Current.Resources.MergedDictionaries`，移除旧的浅/深主题字典
3. 将新字典插入到 `MergedDictionaries[0]`

```csharp
public static void ApplyTheme(bool isDark)
{
    const string light = "Themes/LightTheme.xaml";
    const string dark = "Themes/DarkTheme.xaml";
    var dict = new ResourceDictionary
    {
        Source = new Uri(isDark ? dark : light, UriKind.Relative)
    };
    for (int i = Current.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
    {
        var src = Current.Resources.MergedDictionaries[i].Source?.ToString();
        if (src == light || src == dark)
            Current.Resources.MergedDictionaries.RemoveAt(i);
    }
    Current.Resources.MergedDictionaries.Insert(0, dict);
}
```

### 6.3 调用方式

`AppViewModel.ThemeCmd` 绑定到 UI 的主题切换按钮，接收 `"light"` / `"dark"` 参数：

```csharp
public ICommand ThemeCmd { get; }
private void SetTheme(string? mode)
{
    bool dark = string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase);
    if (dark == IsDark) return;
    IsDark = dark;
    App.ApplyTheme(IsDark);
}
```

### 6.4 关键设计决策

- 使用 `DynamicResource` 而非 `StaticResource` 引用主题色板令牌，确保切换后自动更新。
- `DesignTokens.xaml` 使用 `StaticResource` 引用（因为不随主题变化），但 `PageStyles.xaml` 中 `BaseTextBlockStyle` 引用 `AppFontFamily` 也使用 `StaticResource`。
- 主题切换时只替换色板字典，不替换 DesignTokens 和 Styles，避免不必要的样式重建。

---

## 7. 设计系统文件清单

| 文件 | 用途 | 加载方式 |
|---|---|---|
| `Themes/DesignTokens.xaml` | 设计令牌：字体、圆角、间距、动效 | App.xaml 静态合并 |
| `Themes/LightTheme.xaml` | 浅色主题语义色板 | App.xaml 静态合并（初始）+ `ApplyTheme()` 动态切换 |
| `Themes/DarkTheme.xaml` | 深色主题语义色板 | 仅 `ApplyTheme()` 动态加载 |
| `Themes/Styles.xaml` | 组件样式：Card、NavItem、Button、Banner 等 | App.xaml 静态合并 |
| `Themes/WPFGallery/PageStyles.xaml` | 排版标度与 TextBlock 样式 | App.xaml 静态合并 |
| `Themes/WPFGallery/Templates.xaml` | WrapPanel 模板、导航卡片模板 | 按需引用 |

---

## 8. 开发者使用指南

### 8.1 在 XAML 中引用设计令牌

```xml
<!-- 引用主题色（自动跟随主题切换） -->
<Border Background="{DynamicResource CardBackgroundFillColorDefaultBrush}"
        CornerRadius="{DynamicResource CardCornerRadius}" />

<!-- 引用间距令牌 -->
<StackPanel Margin="{DynamicResource PagePadding}" />

<!-- 引用排版字号 -->
<TextBlock Style="{StaticResource PageTitleTextStyle}" Text="DPI 设置" />
```

### 8.2 引用组件样式

```xml
<!-- 卡片容器 -->
<Border Style="{StaticResource CardStyle}">
    <!-- 卡片内容 -->
</Border>

<!-- 主要操作按钮 -->
<Button Style="{StaticResource PrimaryButtonStyle}" Content="连接设备" />

<!-- 警告提示条 -->
<Border Style="{StaticResource CautionBannerStyle}">
    <TextBlock Style="{StaticResource CautionBannerTextStyle}" Text="检测到官方驱动运行中" />
</Border>
```

### 8.3 DynamicResource vs StaticResource

| 场景 | 使用 | 原因 |
|---|---|---|
| 引用主题色板令牌（TextFill、AccentFill、CardBackground 等） | `DynamicResource` | 切换主题时自动更新 |
| 引用 DesignTokens 中不变的值（圆角、间距、动效） | `StaticResource` 或 `DynamicResource` | 值不变，两者均可 |
| 引用组件样式（CardStyle、PrimaryButtonStyle 等） | `StaticResource` | 样式本身不切换 |
| 引用排版字号（TitleTextBlockFontSize 等） | `StaticResource` | 字号的 `DynamicResource` 在 WPF 中部分场景不支持 |

**经验法则**：颜色相关的令牌用 `DynamicResource`，其余用 `StaticResource`。

### 8.4 添加新样式

1. 如果新样式需要引用主题色，在 `Styles.xaml` 中添加，使用 `DynamicResource` 引用颜色令牌
2. 如果新样式只需要 DesignTokens 中的值，可在 `Styles.xaml` 或页面级资源中添加
3. 命名规范：`{用途}Style`，如 `ComboBoxStyle`、`ToggleSwitchStyle`
4. 避免在页面 XAML 中硬编码色值/字号/间距

### 8.5 使用现有设计令牌（令牌集已冻结，禁止新增）

1. **`DesignTokens.xaml` 不允许新增令牌**（令牌集已冻结，见 §2 头注）：新页面 / 新组件一律复用现有令牌——间距用 `Space*`（均匀）或 `Margin*`/`Padding*`（单边/组合），圆角用 `CardCornerRadius`，字号用 `*TextBlockFontSize`，动效用 `*AnimationDuration`
2. 间距/圆角/字号不够用时，优先用**现有令牌组合**（如嵌套 Margin）或**页面级样式**（页面 `<Page.Resources>` 内定义，如 `MacroPage` 的 `ActionRowStyle`），不新建全局令牌
3. 确需调整现有令牌的值（如修正错误值）时，改前先与用户确认，改后同步更新本文档 §2 对应表
4. **遵守令牌类型约定（§2.7）**：`Margin`/`Padding`/`BorderThickness` 用 `Thickness` 令牌，`FontSize`/`Width`/`Height` 用 `Double` 令牌，禁止类型混用；`Space*` 间距令牌必须是 `Thickness`

### 8.6 主题切换注意事项

- 自定义控件模板中引用的颜色必须使用 `DynamicResource`，否则切换主题后不会更新
- 代码后台（C#）中获取颜色应通过 `Application.Current.Resources["KeyName"]` 动态查找
- 切换主题时，`DynamicResource` 绑定的属性会自动更新，无需手动刷新控件