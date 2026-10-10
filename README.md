# Smart RAM Optimizer

Smart RAM Optimizer is a lightweight Windows desktop application built with C# and WPF that monitors and optimizes memory usage for Electron and Chromium applications like **Discord** and **Brave Browser**.

It uses native Windows working set trimming (`SetProcessWorkingSetSize`) to release unused memory caches from physical RAM, reducing memory usage from ~1-3 GB down to ~30-50 MB per app.

## Features

- **Modern Dark WPF UI:** Sleek dashboard with live RAM metrics.
- **Individual Per-App Tracking:** Dedicated status cards for Discord and Brave showing live RAM usage and active process count.
- **Automated Lifecycle:** Automatically starts monitoring when target apps are launched and automatically closes when all target apps are exited.
- **System Tray Integration:** Minimizes to the Windows system tray; close button (`X`) minimizes to tray to keep memory optimized in the background.
- **Zero Heavy Overhead:** Consumes < 0.1% CPU in the background.

## Supported Processes

```text
Discord
DiscordCanary
DiscordPTB
DiscordDevelopment
brave
```

## Requirements

- **OS:** Windows 10 or Windows 11 (x64)
- **Runtime:** .NET 10 Desktop Runtime (or .NET 10 SDK to build from source)

## Building From Source

```powershell
dotnet build .\DiscordRamLimiter\DiscordRamLimiter.csproj -c Release
```

## License

This project is licensed under the MIT License.
