# Falco User Guide

> Falco is a "maintenance tool" for Windows PCs: clean up junk files, manage startup items, and monitor system health.
> Its most important principle: **every change is backed up, and every deletion can be undone.** Follow this guide with confidence.

***

## 1. Three things to know before first use

### 1. Falco has two launch modes

| Mode | What it can do | How to launch |
| ---- | -------------- | ------------- |
| Standard mode | View only: monitor system status, analyze disks | Double-click `Falco.ps1`, or right-click → "Run with PowerShell" |
| **Administrator mode** | Everything: optimize, clean, uninstall | Right-click `Falco.ps1` → **Run as administrator**; or click the "Restart as administrator" button on the top bar in the app |

> **Recommendation**: use administrator mode for cleaning and optimizing. When the blue "User Account Control" prompt appears, click "Yes" — Windows is asking "allow it to modify the system?"

### 2. Clicking ✕ in the top-right corner does NOT quit!

When you close the window, Falco **minimizes to the system tray in the bottom-right corner and keeps working** (a balloon tip will tell you the first time).
To actually quit: **right-click the falcon icon in the tray → Exit Falco**.

### 3. Everything is backed up before it changes

Before every optimization or settings change, Falco backs up the original settings to `C:\ProgramData\Falco\Backups` and generates a one-click restore script. **You can always recover if something breaks** (see section 5).

***

## 2. Tour of the main window

The top of the window has 5 navigation buttons: **Clean · Software · Optimize · Analyze · Status**.
Here is what each page does and what is safe to click.

### 📊 Status page (default page after launch — look around freely, absolutely safe)

This page is your PC's "health report", refreshed in real time:

- **Health**: a 0–100 total score. Green (85+) means good shape; yellow means pay attention; red (below 50) means it needs attention. The score is alive — when the PC gets busy (compiling, gaming) it drops, and recovers when idle. **Don't obsess over the number, just watch the color**
- **CPU / GPU / Memory**: current utilization. Occasional spikes are normal; only sustained maxing-out deserves attention
- **Temperature**: some desktops can't report temperature (shown as "—") — a hardware limitation, not a bug
- **Disk / Network / Power plan**: free space and network speed at a glance
- **Process list**: the top 15 CPU-consuming programs, read-only
- **Logo menu**: click the falcon logo in the navigation bar for **Settings**, **Run diagnostics** (a read-only 5-dimension check of privacy / performance / updates / cleanup / backup, reported in a popup), **Check for updates**, and **About Falco**

### ⚡ Optimize page (the most powerful — and the one to be most careful with)

The top has three "one-click optimize" presets:

| Preset | What it does | Advice for beginners |
| ------ | ------------ | -------------------- |
| 🟢 Safe | Only near-zero-side-effect actions (disable useless uploads, clean temp files, etc.) | **This is all a beginner needs** |
| 🟡 Performance | More aggressive reduction of background usage | Read every listed item before trying |
| 🔴 Developer | For programmers; keeps search indexing and other compile-related features | Skip it if you don't write code |

Below are **advanced options**: each tweak is one row showing its current state ("Optimized" or "Default"), with **Apply** / **Restore** buttons:

- **Apply** = enable the tweak (high-risk items ask for confirmation first)
- **Restore** = return to Windows defaults
- **Practice on a "low-risk" item first**: Apply → watch the state change → Restore → confirm it recovered. Now you know the flow
- If a row shows "⚠ Drifted", Windows quietly reverted the setting — click **Apply** to re-enable it

### 🧹 Clean page (scan first, then decide)

Every category follows "**scan → review results → check items → clean**":

| Category | What it is | What happens if deleted |
| -------- | ---------- | ----------------------- |
| Temp files | Caches the system throws away | Safe; programs rebuild them |
| Windows Update cache | Leftovers from system updates | Safe |
| Recycle Bin | Files you deleted yourself | **Emptied permanently — check it first** |
| Thumbnail cache | Image preview cache | Safe; previews just rebuild a bit slower |
| Build artifacts | Rebuildable folders from developer projects (node_modules etc.) | Regenerated on demand; deleted to the Recycle Bin, so reversible |
| App & dev caches | Browser caches, npm/pip and other dev caches | Safe; running programs are skipped automatically |
| Installers | .exe/.msi installers in your Downloads folder | Older than 30 days are pre-checked; deleted to the Recycle Bin |

> **Beginner advice**: touching only "Temp files" and "Thumbnail cache" already helps; review scan results carefully before touching other categories.

### 📦 Software page (look a lot, touch a little)

- **Startup items**: lists all auto-start programs. Falco only **displays** them and never removes anything — to disable one, use Windows Task Manager (Ctrl+Shift+Esc → Startup apps)
- **Installed apps**: sorted by disk usage; select and click "Uninstall" to launch the standard Windows uninstall flow
- **Leftover cleanup**: after uninstalling, click "Scan leftovers" to find folders and broken shortcuts the uninstaller missed; deletions go to the Recycle Bin and registry deletions are backed up first. Items marked "possibly shared" are unchecked by default — **leave them alone unless you know**

### 🔍 Analyze page (purely read-only, click away)

- **System info / key services**: the basics of your machine
- **Folder usage**: click "Scan user folders" to see which folders eat your disk; double-click to drill down level by level
- **Large files**: finds files over 200 MB; results can be exported as JSON
- **Backup & logs**: open the backup folder and the run log

***

## 3. Everyday routine (lazy edition)

1. Once a week: open Falco → Clean page → scan "Temp files" and "Thumbnail cache" → clean
2. When the PC feels slow, check the Status page first: what color is Health? Is it CPU, memory, or disk?
3. Only optimize when the PC actually feels slow — **don't optimize for optimization's sake**
4. The rest of the time, let it sit in the tray; it barely uses any resources

***

## 4. Tray icon (the little falcon in the corner)

- **Hover**: shows current CPU and memory usage
- **Double-click**: opens the main window
- **Right-click menu**:
  - Open main window
  - Pause / resume monitoring
  - Close button minimizes to tray (checked = ✕ hides to tray; unchecked = ✕ really closes)
  - Exit Falco
- If a balloon warns "**some settings were reverted by the system**": Windows silently restores certain tweaks — click the balloon to jump over and re-apply them in one click

***

## 5. When something goes wrong

### Case 1: a feature misbehaves after optimizing

Optimize page → advanced options → find the row → click **Restore**. Every tweak can be reverted individually.

### Case 2: undo everything

Optimize page → backup & restore → click "Generate/update restore script" (generated automatically anyway) → click "Run restore script".
Or run `C:\ProgramData\Falco\Restore-Falco.ps1` manually. Restart the PC afterwards.

### Case 3: deleted something important

- Build artifacts and installers went to the **Recycle Bin** — restore them from there
- Other cleanup (temp files, caches) only removes rebuildable junk; no recovery needed

### Case 4: what has Falco actually done?

Analyze page → backup & logs → "Open log". Everything is recorded in `C:\ProgramData\Falco\Falco.log`.

***

## 6. FAQ

**Q: Why are some buttons grayed out?**
A: You're not running as administrator. Standard mode only allows monitoring and analysis. Click the elevate button on the top bar, or right-click and re-launch as administrator.

**Q: Temperature shows "—" — is it broken?**
A: No. Many desktop motherboards don't expose temperature data to the OS — a hardware limitation. Cross-check with tools like HWiNFO.

**Q: Why does Health stay at 100 / suddenly drop to 90?**
A: The score follows current load: busy CPU, tight memory, or a nearly full disk all deduct points. It recovers when idle. Watch the color, not the number.

**Q: Will cleaning remove my installed software?**
A: No. The Clean page only targets junk and rebuildable content; uninstalling software belongs on the Software page and uses the official Windows flow.

**Q: Should I touch the "high-risk" tweaks (disable SysMain / Windows Search)?**
A: You can, but most users gain little. SysMain and Windows Search handle prefetching and file search — disabling them can make some scenarios slower. When in doubt, leave them at "Default".

**Q: What is the whitelist?**
A: If there's a folder you **never want Falco to touch**, right-click it in the cache list on the Clean page → add to whitelist. It will then show "protected" and stay unchecked during scans. The whitelist lives at `~\.config\falco\whitelist.txt`.

***

*Final reminder: although every Falco action has a backup and a way back, creating a Windows System Restore point before major changes is always good hygiene (search "Create a restore point").*
