# Windows Task Manager — reverse engineering & clean-room reconstruction

## 1. Target identity (observed, not inferred)

| Property | Value |
|---|---|
| Image | `C:\Windows\System32\Taskmgr.exe` |
| Size | 5,871,912 bytes |
| SHA-256 | `8215ACAB6FB1C747279954FDF55C05E915381FC32F8B9E996C54EECF189F1227` |
| File version | `10.0.26100.9549 (WinBuild.160101.0800)` |
| OS | Windows 11 25H2, build 26200 (host) |
| PE | PE32+ (0x20b), machine x86-64 (0x8664), subsystem 2 (GUI), 9 sections |
| Localized resources | `System32\en-US\Taskmgr.exe.mui` (82,432 bytes, 193 resources incl. 1,687 string entries), `ar-SA\Taskmgr.exe.mui` |

Evidence files: `REA-EVIDENCE.md`, `evidence/pe-resources.json`, `evidence/binary-layout.json`, `recon/mui-strings.txt`, `recon/taskmgr-ui.txt`, `recon/shots/*.png`, `tools/mui-ui-strings.txt`.

## 2. How the evidence was obtained

Static (no decompiler was available on this host — no Ghidra/IDA engine and the REA MCP server is not registered for this client; the REA CLI fallback was used for the PE/resource work available in this environment):

* PE header/section/import parsing of `Taskmgr.exe` (sections, subsystem, imports with counts).
* MUI resource extraction and string-table decoding (`recon/mui-strings.txt`, `tools/mui-ui-strings.txt`), which yields the complete user-visible vocabulary: tab names, column headers, status words, priority classes, dialog captions, connection types, GPU engine names, memory composition labels.
* Icon/resource groups (`evidence/pe-resources.json`).

Runtime (live observation of the installed build):

* UI Automation enumeration of the running window: top-level class `TaskManagerWindow`, `Windows.UI.Composition.DesktopWindowContentBridge` content islands, `NativeHWNDHost` / `TaskManagerMain`, `DirectUIHWND` list host, one `CvChartWindow` per graph. This confirms Win32 + DirectUI + XAML-Islands composition rather than WinUI 3.
* Window screenshots of the live app (`recon/shots/*.png`, `tm-processes.png`) captured through GDI screen copy; the app's content is DWM-composited, so `PrintWindow` returns blank — recorded as a limitation.
* Input injection into the live vendor instance is blocked by UIPI when the running Task Manager is elevated, so automated tab-by-tab capture of the vendor UI was only partially possible; the MUI vocabulary plus the captured GPU page are the authoritative source for labels and layout.

Import evidence behind the implementation:

* `pdh.dll` — `PdhOpenQueryW`, `PdhAddCounterW`, `PdhCollectQueryData`, `PdhGetFormattedCounterArrayW` ⇒ disk/network/GPU numbers are PDH counters; the reconstruction consumes the same counters and instance names.
* `DUI70.dll` (378 imports) / `DUser.dll` — DirectUI; `dwmapi`/composition for XAML islands.
* `SETUPAPI`, `virtdisk`, `wlanapi`/`wfapi` — device, disk and wireless adapter metadata.
* `USERENV`, `WdcUserMonitor::*`, `HandleLogOffOrDisconnectUser` — the Users page session model.
* `WdcStartupMonitor::{LoadPackagedList,EnableItem,DisableItem,_VerifyAndGetSpecialExeFilePath}`, `PackagedStartupTask::{Create,SetIsDisabled}`, `ADVAPI32 RegOpenKeyExW/RegSetValueExW` ⇒ Startup apps are registry `Run` + `StartupApproved` + packaged startup tasks.
* `TmEndTaskHandler::{QueueKillProcess,QueueKillService,_EndTaskProcess,_InitiateCloseProcessHandles,CloseProcessHandle}`, `WdcListView::{EndProcessTree,SetProcessPriority,ToggleProcessVirtualization,CreateDumpFile,ShowPropertiesDialog,StartServiceW,StopService}` ⇒ the exact command surface replicated in the clone.
* Telemetry names `page_{Processes,Performance,Services,Startup,Users,LowMemory}DurationInMilliseconds`, `WdcAppServiceMonitor`, `WdcDiskMonitor::s_DevQueryCallback` — page inventory and the existence of a low-memory page variant.

## 3. Reconstructed application

`src/TaskMgrClone` — WPF .NET 9 (WinExe, x64, PerMonitorV2 DPI, dark + light theme, no external NuGet packages). Published build: `dist/TaskMgrClone.exe`.

### Architecture

```
src/TaskMgrClone
├─ MainWindow.xaml(.cs)     shell: nav pane, command bar, search, settings, tray, shortcuts
├─ App.xaml(.cs)            theme, settings, --selftest / --sampledump / --renderpages entry points
├─ RunNewTaskWindow, SettingsWindow, PropertiesWindow, AffinityWindow
├─ Pages/                   7 navigable pages (Processes, Performance, App history,
│                           Startup apps, Users, Details, Services)
├─ Controls/Charts.cs       MiniGraph, AreaGraph, CpuHeatmap, CompositionBar, PercentBar, Format
├─ Core/
│   ├─ Native.cs            P/Invoke surface (process/token/service/WTS/PDH/minidump)
│   ├─ Pdh.cs               PDH query wrapper incl. wildcard instance arrays
│   ├─ Sampler.cs           1 Hz system + per-process sampler (CPU, memory, IO, GPU, disks, net)
│   ├─ Models.cs            observable row models shared with the UI
│   ├─ Win32Ext.cs          window ownership, packaged apps, sessions, ProcessActions
│   ├─ Services.cs          SCM enumeration + start/stop/restart
│   ├─ Sessions.cs          WTS session enumeration and per-session aggregation
│   ├─ StartupApps.cs       Run keys, StartupApproved blobs, startup folders
│   ├─ AppHistory.cs        live per-app CPU/network accumulator (persisted JSON)
│   ├─ AppSettings.cs       settings persistence
│   └─ SelfTest.cs          40 headless checks for pure logic
├─ UI/                      IDataPage/PageBase, converters, icons, list reconciliation, theme
└─ Themes/                  dark+light palette, DataGrid/nav/menu styles
```

### Feature parity matrix

Legend: ✅ implemented from observed behaviour, 🟡 approximated (documented below).

| Page | Content | Actions | Status |
|---|---|---|---|
| Processes | Groups Apps / Background processes / Windows processes; Name+icon, Status, CPU, Memory, Disk, Network, GPU; collapsible group headers | End task (graceful WM_CLOSE then terminate, critical-process guards), Efficiency mode, Restart, Create dump file, Open file location, Search online, Properties, double-click Properties, sorting, search | ✅ |
| Performance | Resource tiles with live mini-graphs (CPU, Memory, each disk with its media type such as `SSD (NVMe)` read from the storage descriptors, each adapter, each GPU); CPU graph + per-core heatmap (Change graph), memory composition chart, disk (Type/Active time/Response/Read/Write/Capacity/Free), network and GPU details, Open Resource Monitor | Change graph (overall/logical processors), Open Resource Monitor | ✅ |
| App history | Name, CPU time, Network, Tile updates | Delete usage history, per-row delete, search | 🟡 history is accumulated from live sampling (the vendor reads SRUM) |
| Startup apps | Name, Publisher, Status, Startup impact, "Last BIOS time" footer | Enable/Disable, open location, search online, double-click toggle | ✅ |
| Users | Session cards: user, status/session, process count, CPU/Memory/Disk/Network; selection | Disconnect, Sign out | ✅ |
| Details | Name, PID, Status, User name, CPU, Memory, Commit size, Paged pool, NP pool, Page faults, PF Delta, Base priority, Handles, Threads, User objects, GDI objects, I/O read/write/other, Disk, GPU, GPU memory, Power usage (+trend) | End task/tree, Set priority, Set affinity, Create dump, Restart, Open location, Search online, Properties | ✅ (Power usage is estimated) |
| Services | Name, PID, Description, Status, Group | Start, Stop, Restart, Open Services | ✅ |

Shell: collapsible navigation pane, command bar (Run new task, page primary command, Efficiency mode, search, overflow menu), Always on top, Minimize on use, Hide when minimized (tray icon), Default start page, Real-time update speed, light/dark theme, dark title bar, adaptive toolbar, keyboard shortcuts (Ctrl+Shift+N, Delete, F5, F10, F1, Ctrl+Tab, Ctrl+1..7).

## 4. Implementation choices and their evidence

| Decision | Why |
|---|---|
| PDH counters for CPU (`Processor Information`), memory, physical disks, network interfaces, GPU Engine/Adapter Memory | The vendor binary imports `pdh.dll` and calls `PdhGetFormattedCounterArrayW`; instance strings match the `Disk %1 (%2!s!)`, `<All Processors>`, `Node %1` vocabulary decoded from MUI. |
| `GetProcessMemoryInfo`/`GetProcessIoCounters`/`GetProcessHandleCount`/`GetGuiResources` per process | Matches the Details column set decoded from the MUI table (commit, paged/nonpaged pool, page faults, I/O, handles, GDI/user objects). |
| SCM enumeration via `EnumServicesStatusEx` + `QueryServiceConfig2` | Services columns and `StartServiceW`/`StopService` handlers observed in imports. |
| WTS (`WTSEnumerateSessions`, `WTSQuerySessionInformation`) for Users | `USERENV`/`WdcUserMonitor` evidence; the original signs out with `WTSLogoffSession`. |
| Startup enable/disable by writing `StartupApproved` binary blobs (per-user override read first) | `WdcStartupMonitor::EnableItem/DisableItem` + `RegSetValueExW`; blob semantics reproduced in `StartupApps.IsEnabledBlob`. |
| Per-process GPU via `\GPU Engine(pid_*)` PDH instances | Instance naming (`pid_<pid>_luid_..._engtype_3D`) consumed verbatim by the sampler. |
| End task = WM_CLOSE + wait + terminate | Mirrors `_EndTaskProcess` / `_InitiateCloseProcessHandles`; critical processes are refused like the original. |
| Efficiency mode via `PROCESS_POWER_THROTTLING` (`Set/GetProcessInformation`) | EcoQoS is the mechanism behind the vendor's Efficiency mode. |
| Minidump via `MiniDumpWriteDump` (data segments, full memory, handle data) | Matches the original's "Create dump file"; dump path is `%TEMP%\<name>.DMP`. |
| App history in `%LOCALAPPDATA%\TaskMgrClone\apphistory.json` | SRUM is an ESE database; without an ESE reader the same fields are accumulated from live samples instead. |

## 5. Known gaps and approximations

1. **Per-process network** — the original uses ETW network accounting not exposed per process through PDH; the clone reports 0 for the per-process Network column (system-level network is real and live).
2. **App history** — SRUM is not parsed; history, network and tile counters accumulate only while this app has been running (Network and Tile updates stay 0/-).
3. **Power usage** — derived from measured CPU load into the same five buckets ("Very low"…"Very high") plus a trend; not the vendor's platform energy estimator.
4. **Startup impact** — "Not measured" until a boot trace exists (as in the original).
5. **Suspend process / analyze wait chain** — not implemented; status shows Running / Not responding / Efficiency mode.
6. **Compressed memory** — approximated from the modified page list; Windows exposes no stable exact counter.
7. **Memory speed/form factor/slots** — read from SMBIOS by the Performance page's own parser (values shown on this host). Disk media type (`SSD (NVMe)`) is read with `IOCTL_STORAGE_QUERY_PROPERTY` (seek penalty + bus type), matching the vendor tile subtitle.
8. **GPU adapter↔LUID mapping** — adapters enumerated from the display class registry key and matched to PDH LUIDs by enumeration order (correct on this host, documented as an inference).
9. **"Last BIOS time"** — the registry value is absent on this machine; the honest fallback is `N/A` (no other public source is used).
10. **No decompilation** — no Ghidra/IDA engine was present, so conclusions rest on PE metadata, imports, resources/MUI, live UI observation and behavioural comparison. This is a clean-room reconstruction, not a copy.
11. **Visual chrome** — standard WPF chrome with immersive dark title bar; spacing/type/colours are matched from captures, not pixel-identical.
12. **Elevation** — read-only monitoring works unelevated; ending elevated processes, controlling services and changing other users' sessions report access-denied instead of crashing.

## 6. Verification (all run on this host)

```text
dotnet build                -> 0 warnings / 0 errors
dotnet publish -c Release   -> dist/TaskMgrClone.exe (+ dll, deps, runtimeconfig), embedded Task Manager icon
TaskMgrClone.exe --selftest -> RESULT OK (40/40 checks)
TaskMgrClone.exe --sampledump
  cpu=24.3% speed=4.37GHz base=2.71 cores=6/12 procs=528 threads=6958 handles=222265
  mem total=47.6GB avail=20.4GB inuse=27.2GB cached=1.0GB ...
  disks=2 nets=1 gpus=2 perCore=13 rows=527
TaskMgrClone.exe --renderpages <dir> -> 7 PNGs, one per page (deterministic RenderTargetBitmap)
```

Live UI Automation smoke test on the running app:
* switched all 7 navigation pages (7/7) without exceptions;
* `Run new task` dialog opened and closed;
* `Settings` dialog opened, theme switched Dark → Light → OK: persisted `"Theme": "Light"`, palette updated live, then switched back to Dark;
* `WM_CLOSE` ends the app cleanly; sampler, app-history store and tray resources are disposed.

Screenshots: `recon/render/*.png` (deterministic renders of all seven pages), `recon/render-release/*.png` (Release build), `recon/clone/light-theme.png`, `recon/shots/*.png` (vendor app, reference).

## 7. Build & run

```powershell
cd E:\rea\taskmanager\src\TaskMgrClone
dotnet build -c Release

# published build (no SDK required, .NET 9 Desktop Runtime needed):
E:\rea\taskmanager\dist\TaskMgrClone.exe

# headless diagnostics:
dist\TaskMgrClone.exe --selftest
dist\TaskMgrClone.exe --sampledump
dist\TaskMgrClone.exe --renderpages C:\temp\shots
```