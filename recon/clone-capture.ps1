
param([string]$outDir='E:\rea\taskmanager\recon\clone')
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;using System.Runtime.InteropServices;
public class CAPX{
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int cx,int cy,uint f);
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int c);
 [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("kernel32.dll")]public static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")]public static extern bool AttachThreadInput(uint a,uint b,bool f);
 [DllImport("user32.dll")]public static extern int GetSystemMetrics(int i);
 [DllImport("user32.dll")]public static extern uint GetDpiForWindow(IntPtr h);
 public struct RECT{public int L,T,R,B;}
}
"@
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$p=Get-Process TaskMgrClone -ErrorAction SilentlyContinue | Select-Object -First 1
if(-not $p){Write-Output 'NO APP';exit 1}
$h=$p.MainWindowHandle
[CAPX]::ShowWindow($h,9)|Out-Null
Start-Sleep -Milliseconds 400
$sw=[CAPX]::GetSystemMetrics(0); $sh=[CAPX]::GetSystemMetrics(1)
$scale=[CAPX]::GetDpiForWindow($h)/96.0
$w=[Math]::Min([int](1180*$scale),$sw-80); $ht=[Math]::Min([int](760*$scale),$sh-80)
Write-Output ("dpi scale " + $scale)
[CAPX]::SetWindowPos($h,[IntPtr](-1),20,20,$w,$ht,0x0040)|Out-Null
$fg=[CAPX]::GetForegroundWindow(); $tidFg=0; [CAPX]::GetWindowThreadProcessId($fg,[ref]$tidFg)|Out-Null
$tidSelf=[CAPX]::GetCurrentThreadId()
[CAPX]::AttachThreadInput($tidSelf,$tidFg,$true)|Out-Null
[CAPX]::SetForegroundWindow($h)|Out-Null
[CAPX]::AttachThreadInput($tidSelf,$tidFg,$false)|Out-Null
Start-Sleep -Milliseconds 1000
$r=New-Object CAPX+RECT; [CAPX]::GetWindowRect($h,[ref]$r)|Out-Null
$W=$r.R-$r.L; $H=$r.B-$r.T
Write-Output ("window " + $W + "x" + $H + " at " + $r.L + "," + $r.T)
$root=[System.Windows.Automation.AutomationElement]::RootElement
$cond=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$p.Id)
$win=$root.FindFirst([System.Windows.Automation.TreeScope]::Children,$cond)
$items=$win.FindAll([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem)))
$names=@('Processes','Performance','App history','Startup apps','Users','Details','Services')
function Shot($file){
  $bmp=New-Object System.Drawing.Bitmap($W,$H)
  $g=[System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L,$r.T,0,0,$bmp.Size)
  $g.Dispose()
  $bmp.Save($file,[System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
$idx=0
foreach($name in $names){
  $found=$null
  foreach($it in $items){
    $texts=$it.FindAll([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Text)))
    foreach($t in $texts){ if($t.Current.Name -eq $name){ $found=$it } }
  }
  if($found){
    $found.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 1300
    $file=Join-Path $outDir (('{0}-{1}.png') -f $idx,($name -replace ' ',''))
    Shot $file
    Write-Output ("shot " + $name + " -> " + $file)
  } else { Write-Output ("nav item not found: " + $name) }
  $idx++
}