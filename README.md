# Smart RAM Optimizer

A small Windows tray app that keeps the RAM usage of **Discord** and **Brave Browser** low while they run.

It started as a Discord-only RAM limiter (hence the repository name) and later gained Brave support. Both live in one app, but the code for each is kept in its own file so it is easy to see which part does what.

![Smart RAM Optimizer window](Screenshots/app-preview.png)

## What it does

- Watches every Discord and Brave process and shows live RAM usage and process count for each app on its own card.
- While the limiter is **ON**, trims the memory of those processes every 1.5 seconds.
- Starts Discord (or Brave, if Discord is not installed) when neither is running, and closes itself once both have exited.
- Lives in the system tray: the minimize and close buttons both hide the window. Use the tray icon's **Exit** to quit.
- Optional **Launch with Windows in tray** setting.

## The two limiters

| | Discord | Brave |
|---|---|---|
| Code | [`Targets/DiscordTarget.cs`](SmartRamOptimizer/Targets/DiscordTarget.cs) | [`Targets/BraveTarget.cs`](SmartRamOptimizer/Targets/BraveTarget.cs) |
| Processes trimmed | `Discord`, `DiscordCanary`, `DiscordPTB`, `DiscordDevelopment` | `brave` |
| How it is launched | `%LocalAppData%\Discord\Update.exe --processStart Discord.exe` | `C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe` |
| Logo | [`Assets/Discord/`](SmartRamOptimizer/Assets/Discord) | [`Assets/Brave/`](SmartRamOptimizer/Assets/Brave) |
| Dashboard card | `Discord Card` in [`MainWindow.xaml`](SmartRamOptimizer/MainWindow.xaml) | `Brave Card` in [`MainWindow.xaml`](SmartRamOptimizer/MainWindow.xaml) |

Everything else is shared and does not care which app it is working on.

## How it works

Each app is described by an `AppTarget`: a name, a list of process names, and how to launch it. `RamLimiterService` takes the list of targets and loops:

1. Measure the working set (physical RAM in use) of every process of every target.
2. If the limiter is on, call the Windows API `SetProcessWorkingSetSize(handle, -1, -1)` on each process. This asks Windows to move the process's pages out of physical RAM.
3. Measure again and send a snapshot to the window, which updates the two cards.
4. Wait 1.5 seconds and repeat.

### What "limiting" really means

Trimming does not make Discord or Brave need less memory. Windows moves their pages to the standby list or the page file, and the apps pull them back in when they touch them again. The number in Task Manager drops sharply, and the freed RAM is available to other programs, but an app that is being trimmed constantly may feel slower when you switch back to it. Turn the limiter off with the big toggle if that bothers you.

## Project layout

```text
SmartRamOptimizer.sln
SmartRamOptimizer/
├── Targets/
│   ├── AppTarget.cs          what a "target app" is (shared)
│   ├── DiscordTarget.cs      Discord only
│   ├── BraveTarget.cs        Brave only
│   └── AppTargets.cs         the list of apps the optimizer manages
├── Services/
│   ├── RamLimiterService.cs  measure-and-trim loop (shared)
│   └── StartupService.cs     "launch with Windows" registry entry
├── Models/
│   └── MemorySnapshot.cs     the data sent from the service to the window
├── Assets/
│   ├── Discord/              Discord logo
│   ├── Brave/                Brave logos
│   └── app_icon.*            the app's own icon
├── MainWindow.xaml(.cs)      dashboard window and tray icon
└── App.xaml(.cs)             startup, handles the --minimized argument
```

### Adding another app

1. Copy `BraveTarget.cs`, rename it, and fill in the process names and install path.
2. Add it to the list in `AppTargets.cs`. The limiter picks it up from there.
3. Add a card for it in `MainWindow.xaml` and fill it in `LimiterService_SnapshotUpdated` in `MainWindow.xaml.cs`.

## Requirements

- Windows 10 or 11 (x64)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) to run, or the .NET 10 SDK to build

## Build and run

```powershell
dotnet build .\SmartRamOptimizer.sln -c Release
```

```powershell
.\SmartRamOptimizer\bin\Release\net10.0-windows\SmartRamOptimizer.exe
```

Add `--minimized` to start hidden in the tray, which is what the "Launch with Windows" option does.

## License

MIT. See [LICENSE](LICENSE).
