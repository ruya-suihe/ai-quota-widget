# AI Quota Widget

[中文](README.md) · **English**

A Windows quota widget for AI coding tools, with Codex support and experimental Claude/Cursor CLI and Desktop connections.

[Download EXE](https://github.com/ruya-suihe/ai-quota-widget/releases/latest) · [User guide](guide.en.md) · [Report an issue](https://github.com/ruya-suihe/ai-quota-widget/issues)

![Starry night theme](images/theme-starry.png)

- Remaining quotas, reset countdowns and reference markers.
- Expanded, collapsed and minimal layouts; dragging, wheel scaling and adjustable opacity.
- Standard/custom palettes and five animated themes: ink, sci-fi, tides, steam and starry night.
- Original artwork, 16:9 or manual bar heights; optional Windows startup and update checks.

**Start:** download `UsageQuery.exe`, sign in to the source client, and select your provider in Settings → Update / Provider. Normal mode is the default; Minimal mode remains optional in toolbar settings.

Claude Desktop Code was verified with a real Windows account; other sources need confirmation in their respective account environments. Client sign-ins are read only, authentication material is excluded from quota caches, and updates are downloaded manually. Screenshots use sample data.

Source and required artwork are in [tools/CodexQuota](tools/CodexQuota). Build on Windows with the included .NET Framework compiler; no NuGet packages are needed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\CodexQuota\Build.ps1
```

Output: `artifacts/CodexQuota/UsageQuery.exe`.

Developed with assistance from ChatGPT/Codex. Maintained by [儒雅随和](https://github.com/ruya-suihe).
