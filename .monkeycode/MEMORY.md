# User Instruction Memory

This file records user instructions, preferences, and teachings for reference in future interactions.

## Format

### User Instruction Entry
User instruction entries should follow this format:

[User Instruction Summary]
- Date: [YYYY-MM-DD]
- Context: [Mentioned scenario or time]
- Instructions:
  - [Content of user teaching or instruction, described line by line]

### Project Knowledge Entry
Entries discovered by the Agent during task execution should follow this format:

[Project Knowledge Summary]
- Date: [YYYY-MM-DD]
- Context: Discovered by Agent while performing [specific task description]
- Category: [Operations & Deployment|Build Methods|Testing Methods|Troubleshooting & Debugging|Workflow & Collaboration|Environment Configuration]
- Instructions:
  - [Specific knowledge points, described line by line]

## Deduplication Strategy
- Before adding a new entry, check for similar or identical instructions.
- If a duplicate is found, skip the new entry or merge it with the existing one.
- When merging, update the context or date information.
- This helps avoid redundant entries and keeps the memory file tidy.

## Entries

[Project Knowledge Summary]
- Date: 2026-08-09
- Context: Discovered by Agent while publishing v0.1.2 GitHub Release for DELUX_M618XSD
- Category: Operations & Deployment
- Instructions:
  - 本仓库的 git credential helper（`/app/agent/bin/agent git-credential-helper`）对 GitHub 返回 500，无法自动取凭据；历史 fine-grained token 也已失效。
  - 每次需要 push / 创建 tag / 创建 release 时，由用户现场提供新的 fine-grained token（Contents: read/write 权限），通过 `GH_TOKEN=<token>` 环境变量或 `https://x-access-token:<token>@github.com/...` URL 注入使用。
  - token 用完即弃，绝不写入 `.netrc` 或任何项目文件，也不在对话中回显真实值。
  - 发布流程：`git push origin master` → `git tag v0.1.x` → `git push origin v0.1.x` → `gh release create v0.1.x --title "..." --notes "..."`。
  - Release 只发布源码（无二进制资产），用户下载 Source code (zip) 后在 Windows 上双击 `MouseDriverClient\publish-self.bat` 自编译。
  - 启动日志首行已打印程序版本与 exe 构建时间戳（csproj `<Version>`），用户实测前先看日志首行确认非旧产物。

[User Instruction Summary]
- Date: 2026-08-10
- Context: Phase 2 收尾审计时处理死绑定
- Instructions:
  - UI 入口须与官方 Mouse.exe 对齐：官方没有「唤醒」「应用全部」「恢复灯光」按钮，上位机也不得添加这些功能入口（已移除 BtnWake 唤醒按钮、ApplyAllCmd/RecoverLightCmd/NavAll_Click）。
