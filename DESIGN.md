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
| `Space2` | `Double` | 2 | 极紧凑间距 |
| `Space4` | `Double` | 4 | 紧凑间距 |
| `Space8` | `Double` | 8 | 小间距 |
| `Space12` | `Double` | 12 | 中等间距 |
| `Space16` | `Double` | 16 | 标准间距 |
| `Space24` | `Double` | 24 | 大间距 |
| `Space32` | `Double` | 32 | 较大间距 |
| `Space48` | `Double` | 48 | 超大间距 |

### 2.6 动效

| Key | 类型 | 值 | 用途 |
|---|---|---|---|
| `ControlFasterAnimationDuration` | `Duration` | 00:00:00.083 (83ms) | 极快动效 |
| `ControlFastAnimationDuration` | `Duration` | 00:00:00.167 (167ms) | 快速动效 |
| `ControlNormalAnimationDuration` | `Duration` | 00:00:00.250 (250ms) | 标准动效 |
| `ControlSlowAnimationDuration` | `Duration` | 00:00:00.500 (500ms) | 慢速动效 |
| `ControlFastOutSlowInKeySpline` | `String` | `0,0,0,1` | 标准加速曲线（快进慢出） |

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

### 8.5 添加新设计令牌

1. 主题无关的令牌（如新的间距值、圆角）添加到 `DesignTokens.xaml`
2. 主题相关的颜色值添加到 `LightTheme.xaml` 和 `DarkTheme.xaml`（两边同步添加）
3. 遵循 Fluent/WinUI 命名规范，使用 `PascalCase` 键名

### 8.6 主题切换注意事项

- 自定义控件模板中引用的颜色必须使用 `DynamicResource`，否则切换主题后不会更新
- 代码后台（C#）中获取颜色应通过 `Application.Current.Resources["KeyName"]` 动态查找
- 切换主题时，`DynamicResource` 绑定的属性会自动更新，无需手动刷新控件