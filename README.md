# AI Quota Widget

**中文** · [English](README.en.md)

Windows AI 额度悬浮窗：支持 Codex，提供 Claude／Cursor CLI 与桌面端的试验接入。

[下载 EXE](https://github.com/ruya-suihe/ai-quota-widget/releases/latest) · [使用指南](guide.zh-CN.md) · [反馈问题](https://github.com/ruya-suihe/ai-quota-widget/issues)

![星空主题](images/theme-starry.png)

- 剩余额度、重置倒计时与参考三角。
- 展开、收起、极简模式；拖动定位、滚轮缩放、透明度可调。
- 普通色卡、自定义配色，以及山水、科幻、潮汐、蒸汽和星空五种动态主题。
- 原图比例、16:9 或自由条高；可选开机启动与更新检查。

**使用**：下载 `UsageQuery.exe`，在对应客户端登录后启动；进入“设置 → 更新／平台”选择平台。想看数值时关闭极简模式。

Claude Desktop Code 已通过 Windows 本机账户查询验证；其他来源仍需在对应账户环境确认。程序只读客户端登录态，认证信息不写入额度缓存；不自动下载安装更新。截图使用示例数据。

源码与必需素材位于 [tools/CodexQuota](tools/CodexQuota)。Windows 自带 .NET Framework 编译器即可编译，无需 NuGet：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\CodexQuota\Build.ps1
```

输出：`artifacts/CodexQuota/UsageQuery.exe`。

本项目在 ChatGPT／Codex 协助下开发，由[儒雅随和](https://github.com/ruya-suihe)维护。
