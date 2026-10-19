# REA evidence — Windows Task Manager (build 26200)

## Target identity (observed)
- Path: `C:\Windows\System32\Taskmgr.exe`
- Size: 5,871,912 bytes; SHA-256 `8215ACAB6FB1C747279954FDF55C05E915381FC32F8B9E996C54EECF189F1227`
- Version: `10.0.26100.9549 (WinBuild.160101.0800)`, x86-64 (machine 0x8664), subsystem 2 (GUI), PE32+ (magic 0x20b), 9 sections
- Localized resources: `C:\Windows\System32\{en-US,ar-SA}\Taskmgr.exe.mui` (en-US 82,432 bytes, 193 resources incl. 1,687 string-table entries)

## Observed architecture (from imports + string evidence)
- UI: Win32 + `DirectUI` (`DUI70.dll` 378 imports, `DUser.dll`), XAML islands
  (`Windows.UI.Composition.DesktopWindowContentBridge`), classes `TaskManagerWindow`,
  `TaskManagerMain`, `NativeHWNDHost`, `DirectUIHWND`, chart windows class `CvChartWindow`.
- Counters: `pdh.dll` — `PdhOpenQueryW`, `PdhAddCounterW`, `PdhCollectQueryData`,
  `PdhGetFormattedCounterArrayW`, `PdhGetRawCounterArrayW`, `PdhCloseQuery`.
- Device/adapter info: `SETUPAPI.dll` (`SetupDiGetClassDevsW`, `SetupDiEnumDeviceInfo`,
  `SetupDiGetDevicePropertyW`), `virtdisk.dll`, `WlanFreeMemory`/`WFDFreeMemoryInt` (wlanapi/wfapi).
- Users/sessions: `USERENV.dll`, `WdcUserMonitor::*`, `HandleLogOffOrDisconnectUser`.
- Startup: `WdcStartupMonitor::{LoadPackagedList,EnableItem,DisableItem,_VerifyAndGetSpecialExeFilePath}`,
  `PackagedStartupTask::{Create,SetIsDisabled}`. Registry read/write via `ADVAPI32` `RegOpenKeyExW`/`RegSetValueExW`.
- End task: `TmEndTaskHandler::{QueueKillProcess,QueueKillService,_EndTaskProcess,_InitiateCloseProcessHandles,CloseProcessHandle}`,
  `WdcListView::{EndProcessTree,SetProcessPriority,ToggleProcessVirtualization,CreateDumpFile,ShowPropertiesDialog,StartServiceW,StopService}`.
- Telemetry naming reveals pages/flows: `page_{Processes,Performance,Services,Startup,Users,LowMemory}DurationInMilliseconds`,
  `WdcAppServiceMonitor`, `WdcDiskMonitor::s_DevQueryCallback`.

## Observed UI text / columns (from en-US MUI string table)
- Type strings: `AppContainer`, `NPU`, `Apps`, `Background`, `Windows`, `Details`, `Services`, `Users`, `Startup`, `Performance`, `Processes`.
- Columns: `Name`, `PID`, `Type`, `Status`, `Publisher`, `Process`, `Commit size`, `Paged pool`, `NP pool`,
  `Page faults`, `PF Delta`, `Base priority`, `Handles`, `Threads`, `User objects`, `GDI objects`,
  `I/O read`, `I/O write`, `Dedicated GPU memory`, `Shared GPU memory`, `Power usage`, `Disk %1!d!`.
- States: `Not responding`, `Running`, `Suspended`, `Enabled`, `Disabled`, `Stopped`, `Starting`, `Stopping`, `Paused`.
- Priorities: `Normal`, `Above normal`, `High`, `Realtime`, `N/A` (+ Low/Below normal).
- Performance: `Total processor utilization across all cores`, `Total installed memory`,
  `Memory usage in percentage`, `Available (%1!s! MB)`, `<All Processors>`, `Node %1!d!`, `Disk %1!d! (%2!s!)`,
  `Form factor`, `Health`, `Capacity`, `Resource Monitor`, `Last BIOS time: %1 seconds`.
- Disk/network kinds: `HDD`, `SSD`, `Persistent Memory`, `Removable`, `Ethernet`, `Bluetooth PAN`, `USB`,
  `5G`, `LTE`, `HSPA`, `Wi-Fi`, `Unknown`, `16 bit`, `32 bit`, `64 bit`, `Guard`, `Virtualization:`.
- Menus/dialogs: `&End task`, `&Restart`, `&Switch user`, `Si&gn out`, `Dis&connect`, `Open Services`,
  `E&nable`, `Dis&able`, `End process`, `Change priority`, `Are you sure you want to continue?`,
  `The necessary debug privilege...`, `Unable to obtain a handle to the live kernel dump directory.`

## Unknowns / not established
- No decompilation performed: no Ghidra/IDA engine present, REA MCP not registered, target runs
  elevated so UIA/input from this medium-integrity session is blocked by UIPI (only 3 accessible nodes).
- Exact internal sampling threads and GPU/NPU aggregation formulas are inferred, not observed.
- Live UI screenshots not captured (window parked at -25600 by the elevated instance).
