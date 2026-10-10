# Smart RAM Optimizer

A small Windows tray app that keeps the RAM usage of the apps you choose low while they run.

It started as a Discord-only RAM limiter and later gained Brave support. You can now add any application and switch each one on or off.

![Smart RAM Optimizer window](Screenshots/app-preview.png)

## What it does

- Lists the apps it manages, each with its own on/off switch, live RAM usage and how much of its memory is freed right now.
- Says why an app is being left alone: **in use**, **trims in 18 s** (you just switched away from it), **already small**, **paused** or **needs administrator**.
- **Optimize now** trims every switched-on app at once, without waiting for it to sit in the background, and tells you what that freed.
- **Add application** shows what is running right now or lets you browse for an `.exe`. Apps marked **Good to optimize** come first, then the rest by size, then the ones marked **Not recommended**. Point at a row in the main list to show its remove button; a removed app can be brought back with **Undo** for a few seconds.
- On its very first start it looks at what you are running and adds the well-known memory-heavy apps it finds (browsers, chat apps and so on), so the list starts out as yours. If it finds none, it starts with Discord and Brave.
- Shows what it has achieved: memory freed right now, memory freed this session, and overall system memory. The memory bar turns amber at 80% and red at 90%.
- Lives in the system tray. Click the icon to open the window; right-click it for **Optimize now**, **Pause for 30 minutes / 1 hour / until I resume**, **Launch with Windows** and **Quit**. The icon turns grey while optimizing is paused.
- The minimize and close buttons and the Esc key all hide the window. It reopens where you left it.
- Remembers your apps and settings between runs.
- Optional **Launch with Windows**, which starts it hidden in the tray.

It no longer starts Discord by itself, and it no longer exits when your apps close. It sits in the tray and waits for them.

## How it works

Every app in the list is a name plus the process names that belong to it (for example `Discord`, `DiscordCanary`, `DiscordPTB`). A background loop in `RamLimiterService` repeats:

1. Take one snapshot of all running processes and pick out the ones that belong to your apps.
2. For each app that is due, call the Windows API `SetProcessWorkingSetSize(handle, -1, -1)` on its processes. This asks Windows to move the process's pages out of physical RAM.
3. Measure again to see what the trim achieved, and send the numbers to the window.
4. Wait (2 seconds while the window is open, 5 seconds while it is hidden) and repeat.

### Gentle and aggressive mode

**Gentle (default).** An app is trimmed only when it is in the background, has been there for a while, is using more than 100 MB, and was not trimmed a moment ago. The app you are working in is left alone and its row says "in use". How long "a while" and "a moment" are depends on how full your memory is:

| System memory in use | Left alone after you switch away | Time between trims |
| --- | --- | --- |
| Under 60% | 45 seconds | 90 seconds |
| 60% to 80% | 30 seconds | 30 seconds |
| 80% and over | 10 seconds | 15 seconds |

With plenty of memory free there is nothing to gain from hurrying, and when it runs short the memory is wanted back sooner.

**Aggressive** (settings menu). Every app is trimmed every 1.5 seconds, including the one you are using. This is how the app used to behave. It frees more, but the app being trimmed can stutter.

**Optimize now** is a single aggressive pass: it trims every switched-on app once, whatever it is doing, and works even while optimizing is paused.

### What "limiting" really means

Trimming does not make an app need less memory. Windows moves its pages to the standby list or the page file, and the app pulls them back in when it touches them again. The freed RAM is available to other programs, but an app that is trimmed while busy may feel slower for a moment when you switch back to it. Turn off an app's switch if that bothers you.

### What the stats mean

- **Freed** on an app's row: what that app held just before it was trimmed, minus what it has taken back since.
- **Freed right now**: the same figure for all your optimized apps together. Memory an app released by itself is not counted.
- **Freed this session**: everything trimmed since the optimizer started. The same memory can be counted more than once if an app takes it back and it is trimmed again.
- **System memory**: physical RAM in use across the whole PC.

### Its own footprint

A RAM optimizer should not be a memory-heavy app itself, so it is built to stay small:

- The window only exists while it is on screen. Hiding it closes it and hands its memory back; opening it again takes a fraction of a second.
- The window is drawn on the CPU, which keeps the graphics driver's working memory out of the process.
- The tray icon talks to Windows directly instead of loading Windows Forms.
- All processes are read with one system call into a buffer that is freed straight away, with nothing allocated per process.
- .NET's garbage budget is capped at 2 MB (`System.GC.Gen0MaxBudget` in the project file), so garbage is collected before it piles up.
- While hidden it trims its own working set once a minute.

On the development PC that comes to about 2 MB in Task Manager while it sits in the tray and around 20 MB with the window open. The settings menu shows the current figure next to the version.

### Safety

- Windows system processes (`csrss`, `lsass`, `svchost`, `dwm`, anything in the Windows folder, and so on) cannot be added.
- Apps marked **Not recommended** in the picker need a confirmation before they are added, and the message says why. There are three kinds:
  - Games and launchers protected by anti-cheat (Valorant and the Riot Client, Fortnite, Roblox and so on). Touching their memory from another program can get you kicked or flagged.
  - Recording, streaming and audio-mixing apps such as OBS. They work in real time in the background, so trimming them causes dropped frames and glitches.
  - Virtual machines and Android emulators, which slow to a crawl when their memory is pushed to disk.
- Apps marked **Good to optimize** are known memory-heavy apps that cope well with trimming: browsers (Chrome, Edge, Brave, Firefox, Opera, Vivaldi), chat apps (Discord, Slack, Teams, Telegram, WhatsApp, Signal), launcher helpers (Steam WebHelper, Epic Games Launcher, Battle.net, EA Desktop, GOG Galaxy, Ubisoft Connect), Spotify and a few Electron tools.
- Both lists live in `Services/AppRules.cs`. They are hand-picked from how these apps are known to behave, so treat an unmarked app as "probably fine, try it" and a warning as worth heeding.
- Apps running as administrator cannot be trimmed unless the optimizer is also run as administrator. Their row says "needs administrator".

## Settings

Settings are stored in `%AppData%\SmartRamOptimizer\settings.json`. Unexpected errors are written to `error.log` in the same folder. Only one copy of the optimizer runs at a time; starting it again brings up the existing window.

## Support button

A **Buy us a coffee** button at the bottom of the window opens a donation page in the default browser. It ships with a placeholder link, and the button stays hidden until the placeholder is replaced. To switch it on:

1. Create an account at [Buy Me a Coffee](https://www.buymeacoffee.com/) or [Ko-fi](https://ko-fi.com/).
2. Claim your page link, for example `https://buymeacoffee.com/myteam`.
3. Connect a payout method (Stripe or PayPal) in that site's settings so donations can reach you.
4. Replace `YOUR_USERNAME` in `CoffeeUrl` in `SmartRamOptimizer/AppConfig.cs` with your page name (or replace the whole link if you use Ko-fi) and rebuild. The link must start with `https://`.

## Project layout

```text
SmartRamOptimizer.sln
SmartRamOptimizer/
├── Services/
│   ├── RamLimiterService.cs  background measure-and-trim loop
│   ├── ProcessList.cs        every running process in one system call
│   ├── ProcessTools.cs       trim a process, find its exe, read system memory
│   ├── NativeMethods.cs      the Windows API declarations
│   ├── AppRules.cs           protected processes, warnings and recommendations
│   ├── RunningAppFinder.cs   builds the "Add application" list
│   ├── FirstRunSetup.cs      picks a new user's starting list
│   ├── IconService.cs        reads an app's icon from its exe
│   ├── SettingsService.cs    loads and saves settings.json
│   ├── StartupService.cs     "launch with Windows" registry entry
│   ├── SingleInstance.cs     one running copy per user
│   ├── TrayIcon.cs           the tray icon itself
│   └── ErrorLog.cs           error.log
├── Models/
│   ├── AppSettings.cs        what is saved between runs
│   └── MemorySnapshot.cs     the data sent from the loop to the window
├── ViewModels/               state behind the two windows
├── Assets/                   the app's own icons (normal and paused)
├── AppConfig.cs              the support-button link
├── AppIdentity.cs            the name that keeps a development copy apart
├── MainWindow.xaml(.cs)      application list and stats
├── AddAppWindow.xaml(.cs)    "Add application" picker
├── ConfirmDialog.xaml(.cs)   the "not recommended" warning
├── TrayMenu.cs               the tray icon's right-click menu
└── App.xaml(.cs)             startup, tray, shared styles, the --minimized argument
```

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

### A development copy beside the installed one

Only one copy runs at a time, so a fresh build normally just brings up the copy you already have running. Building with `-p:DevInstance=true` makes a copy with its own identity (settings in `%AppData%\SmartRamOptimizer.Dev`, its own startup entry and instance lock) that runs alongside:

```powershell
dotnet build .\SmartRamOptimizer\SmartRamOptimizer.csproj -c Release -p:DevInstance=true -o .\artifacts\dev
```

## License

MIT. See [LICENSE](LICENSE).
