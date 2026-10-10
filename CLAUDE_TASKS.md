# Instructions for Claude: Smart RAM Optimizer Enhancements

> [!IMPORTANT]
> **Mandatory Directive for Claude:**  
> When you start, you **MUST immediately inform your user** that a new specification file (`CLAUDE_TASKS.md`) was created by other collaborators outlining suggested features and architectural improvements.
> **DO NOT delete this file right away.**
> Instead, explain these proposed features and design options to your user, get their input on how they want them implemented, and decide on the implementation plan together before making code changes.

---

## 1. Project Context & Architecture

* **Solution File:** `SmartRamOptimizer.sln`
* **Main Project Path:** `SmartRamOptimizer/SmartRamOptimizer.csproj`
* **Target Framework:** `net10.0-windows` (WPF + Windows Forms for `NotifyIcon`)
* **Output Binary:** `SmartRamOptimizer.exe`
* **Core Optimization Mechanism:** Native Windows Working Set Trimming via `kernel32.dll`:
  ```csharp
  [DllImport("kernel32.dll")]
  private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);
  // Trimming is invoked with: SetProcessWorkingSetSize(proc.Handle, -1, -1);
  ```
* **Existing Target Structure:**
  * `SmartRamOptimizer/Targets/AppTarget.cs`: Represents an app target with its process names, process enumerator, and launch information.
  * `SmartRamOptimizer/Targets/AppTargets.cs`: List of active/monitored `AppTarget` instances.
  * `SmartRamOptimizer/Targets/DiscordTarget.cs` and `BraveTarget.cs`: Concrete target implementations.
  * `SmartRamOptimizer/Services/RamLimiterService.cs`: Background monitor loop running every 1500ms to calculate working sets and trim memory.
  * `SmartRamOptimizer/MainWindow.xaml` and `MainWindow.xaml.cs`: Dark modern WPF interface.

---

## 2. Tasks to Implement

### Task 1: "Add More Apps" & High-RAM Presets for Gamers and Power Users

#### A. Target Identification
Gamers and heavy multitaskers frequently suffer from background clients and browsers hoarding physical RAM while running games. The following applications are the biggest offenders that should be supported as presets:

1. **Browsers (Chromium/Gecko):**
   * **Google Chrome:** `chrome` (often consumes 1–3 GB+ with multiple tabs)
   * **Microsoft Edge:** `msedge` (frequently holds 500MB–1.5GB across background WebView2 hosts)
   * **Opera GX:** `opera`, `opera_gx` (popular gaming browser; can hold 1GB+ without strict memory limits)
2. **Game Launchers & Clients (Electron/Chromium WebHelpers):**
   * **Steam WebHelper:** `steamwebhelper` (Steam spawns 6–10 Chromium renderer processes that easily hoard 600MB–1.5GB of RAM while playing games)
   * **Epic Games Launcher:** `EpicGamesLauncher` (CEF-based launcher, consumes 500MB–1GB in the background)
   * **Battle.net:** `Battle.net`, `Battle.net.exe` (Blizzard launcher and auxiliary helpers, 400MB–800MB)
   * **Riot Client:** `RiotClientServices` (League of Legends / Valorant client)
   * **EA Desktop:** `EADesktop` (Electronic Arts client)
3. **Media & Streaming:**
   * **Spotify:** `Spotify` (Electron desktop player; hoards 500MB–1GB RAM while playing in the background during games)

#### B. Decouple Application Lifecycle (No Auto-Launch / Auto-Exit with Discord or Brave)

In earlier iterations, the utility was tightly coupled to Discord and Brave:
* `EnsureTargetLaunched()` automatically launched Discord or Brave if neither was running.
* `AllTargetProcessesExited` automatically closed the optimizer when target processes exited.
* Previous concepts contemplated launching the optimizer whenever Discord or Brave opened.

**Must be removed completely:**
1. **No Auto-Launching Target Apps:** Remove `EnsureTargetLaunched()` from `RamLimiterService.cs` (and remove `TryLaunch()` / `_getLaunchInfo` from `AppTarget.cs`). The optimizer must never launch external applications on its own.
2. **No External Launch Coupling:** Ensure there are no background triggers, registry hooks, or wrappers configured to automatically launch the optimizer when Discord or Brave is opened. The optimizer is a standalone utility that the user starts manually or via the Windows Startup checkbox.
3. **No Auto-Exit When Apps Close:** Remove `AllTargetProcessesExited` event and any logic in `MainWindow.xaml.cs` that shuts down the optimizer when targets terminate. The optimizer should stay running quietly in the system tray, ready to trim memory whenever monitored apps are opened.

#### C. Implementation Requirements for "Add Apps"
1. **Modular Targets or Configurable Target List:**
   * Implement target definitions for these high-RAM applications in `SmartRamOptimizer/Targets/` (e.g. `SteamTarget.cs`, `ChromeTarget.cs`, `SpotifyTarget.cs`, `EdgeTarget.cs`, `OperaGxTarget.cs`).
   * Support an active/enabled toggle per app, or a settings/selection modal so users can choose which applications they want actively monitored and limited.
   * Provide a clean way to add custom process names (e.g. entering an executable name like `obs64` or `vlc` to monitor custom apps).
2. **UI Integration in `MainWindow.xaml`:**
   * Update the dashboard to support displaying active apps dynamically (e.g., using an `ItemsControl` or scrollable list of app cards) rather than a fixed 2-column layout.
   * Each app card should show:
     * App icon/logo
     * App name
     * Live current RAM usage (formatted via `FormatBytes`)
     * Active sub-process count
     * Individual enable/disable toggle or remove button.
   * Provide an **"+ Add App"** button that lets users pick from the gamer/browser preset list or type a custom process name.

---

### Task 2: High-Resolution Unified App Icon Replacement

#### Problem to Resolve
Previous iterations suffered from icon inconsistencies:
* Some versions had a white square background artifact around the circular icon in the Windows taskbar and system tray.
* In some resolutions, the icon was blurry or distorted.

#### Requirements
1. **Design a Unified, Professional Icon:**
   * Use a sleek, modern icon that represents universal memory optimization / speed boosting (e.g., futuristic RAM chip, neon gauge, or speed booster).
   * Ensure it does not look tied strictly to Discord alone, since the app now optimizes browsers, gaming launchers, and music players.
2. **Ensure True 32-bit ARGB Transparency (No White Boxes):**
   * Generate a multi-resolution Windows `.ico` file containing standard sizes: **16x16, 32x32, 48x48, 64x64, 128x128, and 256x256**.
   * Use PNG-compressed icon frames or properly masked 32-bit ARGB bitmaps so the Windows taskbar and system tray render anti-aliased transparency without solid white/black backgrounds.
3. **Replace Across All Touchpoints:**
   * **Project Executable:** `<ApplicationIcon>Assets\app_icon.ico</ApplicationIcon>` in `SmartRamOptimizer.csproj`.
   * **Window Header & Taskbar:** Set `Icon="pack://application:,,,/Assets/app_icon.png"` or load from `app_icon.ico` in `MainWindow.xaml` / `MainWindow.xaml.cs`.
   * **System Tray:** Set `_trayIcon.Icon` in `MainWindow.xaml.cs` to the newly generated `app_icon.ico`.
   * **In-App Dashboard:** Ensure the top header graphic in `MainWindow.xaml` displays the clean `app_icon.png` centered without clipping.

---

### Task 3: "Buy Me a Coffee" / "Give Us a Coffee" Feature

#### UI & Code Implementation
1. **Add a Donation / Support Button:**
   * Add a tasteful "☕ Buy Us a Coffee" or "☕ Support" button/pill in `MainWindow.xaml` (e.g., in the bottom footer next to the startup checkbox, or in a header settings bar).
   * Style it to match the dark modern palette (e.g., accent color `#FFDD00` or subtle glowing pill button with a coffee cup icon).
2. **Click Handler:**
   * When clicked, open the user's default browser to the donation URL safely using:
     ```csharp
     private void CoffeeButton_Click(object sender, RoutedEventArgs e)
     {
         var url = AppConfig.CoffeeUrl; // or ConfigurationManager / constant
         if (!string.IsNullOrWhiteSpace(url))
         {
             try
             {
                 Process.Start(new ProcessStartInfo
                 {
                     FileName = url,
                     UseShellExecute = true
                 });
             }
             catch (Exception ex)
             {
                 Debug.WriteLine($"Failed to open coffee link: {ex.Message}");
             }
         }
     }
     ```
3. **Configurability:**
   * Put the URL in a dedicated configuration file or constant (e.g., `AppConfig.cs` or `appsettings.json`), with a placeholder fallback:
     ```csharp
     public static class AppConfig
     {
         public const string CoffeeUrl = "https://buymeacoffee.com/YOUR_USERNAME";
     }
     ```

#### Developer/User Onboarding Guide (What Needs to Be Done on Their Side):
Document in the project README or comments what the owner must set up:
1. **Create an Account:**
   * Sign up at [Buy Me a Coffee](https://www.buymeacoffee.com/) or [Ko-fi](https://ko-fi.com/).
2. **Claim Username / Page:**
   * Set up their profile and claim their page link (e.g., `https://buymeacoffee.com/myteam`).
3. **Connect Payout Method:**
   * Link Stripe or PayPal in the Buy Me a Coffee settings to receive donations.
4. **Update the URL in Code:**
   * Replace `https://buymeacoffee.com/YOUR_USERNAME` in `AppConfig.cs` with their real link.

---

## 3. Build & Verification Steps

1. **Build the Project:**
   ```powershell
   dotnet build .\SmartRamOptimizer\SmartRamOptimizer.csproj -c Release
   ```
2. **Publish Release:**
   ```powershell
   dotnet publish .\SmartRamOptimizer\SmartRamOptimizer.csproj -c Release -o .\publish
   ```
3. **Verify:**
   * Launch `publish\SmartRamOptimizer.exe`.
   * Check that the system tray icon has zero white border artifacts.
   * Check that new gamer apps (Steam, Chrome, Spotify, etc.) can be monitored and their working sets trimmed.
   * Verify the "Buy Us a Coffee" button opens the configured URL.

---

## 4. Collaboration & Retention Guidance for Claude

> [!IMPORTANT]
> **DO NOT DELETE THIS FILE PREMATURELY:**
> 1. Mention to your user at the very beginning that this `CLAUDE_TASKS.md` file was drafted with suggestions from other collaborators.
> 2. Present and explain the suggested features, presets, and decoupling cleanup items to your user.
> 3. Agree on the scope and design together with your user before writing any code.
> 4. Keep this file intact throughout the implementation process as your master reference.
> 5. Only remove or archive this file once everything is built, tested, and your user explicitly confirms they no longer need it.

