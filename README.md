> **⚠️ Research use only.** This is an independent, clean-room reconstruction built to study
> interoperability and user-interface behaviour of Windows Task Manager. It is **not affiliated with
> Microsoft**, contains no Microsoft binaries or source code, and is provided **as is** without
> warranty. Read [DISCLAIMER.md](DISCLAIMER.md) before using or sharing it.

# TaskMgrClone

Clean-room reconstruction of the Windows 11 Task Manager (build 26200) as a WPF .NET 9 desktop app.
Reverse-engineering evidence, architecture, feature matrix and limitations: [REPORT.md](REPORT.md).

## Run the published build
```
dist\TaskMgrClone.exe
```
Requires the .NET 9 Desktop Runtime (the development host has it).

## Pages
Processes · Performance · App history · Startup apps · Users · Details · Services

## Build from source
```powershell
cd src\TaskMgrClone
dotnet build -c Release
dotnet publish -c Release -o ..\..\dist
```

## Diagnostics
```powershell
dist\TaskMgrClone.exe --selftest                     # 40 headless logic checks
dist\TaskMgrClone.exe --sampledump                  # dump one live sample to %TEMP%\TaskMgrClone-sampledump.txt
dist\TaskMgrClone.exe --renderpages C:\temp\shots   # render the 7 pages to PNG (no screen capture needed)
```

## Keyboard
| Shortcut | Action |
|---|---|
| Ctrl+Shift+N | Run new task |
| Delete | End task / page primary action |
| F5 | Refresh |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous page |
| Ctrl+1..7 | Jump to page |
| F10 | Settings |
| F1 | About |

## Notes
* Monitoring is read-only and works without elevation; ending elevated processes or controlling services needs an elevated instance.
* App history is accumulated from live sampling (the vendor reads the SRUM ESE database); see REPORT.md §5 for all approximations.