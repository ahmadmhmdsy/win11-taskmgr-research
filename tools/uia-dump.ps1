param([int]$MaxNodes = 2000, [string]$OutFile = "E:\rea\taskmanager\tools\uia-tree.txt")

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class W {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
  public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$tpid = (Get-Process Taskmgr -ErrorAction SilentlyContinue | Select-Object -First 1).Id
if (-not $tpid) { Start-Process "$env:WINDIR\System32\Taskmgr.exe"; Start-Sleep 4; $tpid = (Get-Process Taskmgr | Select-Object -First 1).Id }

$found = [System.Collections.Generic.List[IntPtr]]::new()
$cb = [W+EnumWindowsProc]{ param($h,$l)
  $p = 0; [void][W]::GetWindowThreadProcessId($h, [ref]$p)
  if ($p -eq $tpid) {
    $sb = New-Object System.Text.StringBuilder 512; [void][W]::GetWindowText($h, $sb, 512)
    $cn = New-Object System.Text.StringBuilder 256; [void][W]::GetClassName($h, $cn, 256)
    $r = New-Object W+RECT; [void][W]::GetWindowRect($h, [ref]$r)
    Write-Host ("HWND={0} vis={1} class={2} title='{3}' rect={4},{5},{6},{7}" -f $h, [W]::IsWindowVisible($h), $cn, $sb, $r.Left, $r.Top, $r.Right, $r.Bottom)
    if ($cn.ToString() -eq "TaskManagerWindow") { $found.Add($h) }
  }
  return $true
}
[void][W]::EnumWindows($cb, [IntPtr]::Zero)

if ($found.Count -eq 0) { throw "No TaskManagerWindow found" }
$hwnd = $found[0]
[void][W]::ShowWindow($hwnd, 9)   # SW_RESTORE
[void][W]::MoveWindow($hwnd, 80, 80, 1180, 820, $true)
[void][W]::SetForegroundWindow($hwnd)
Start-Sleep 2

$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$lines = [System.Collections.Generic.List[string]]::new()
$script:count = 0
function Walk($el, $depth) {
  if ($script:count -ge $MaxNodes -or $depth -gt 20) { return }
  $script:count++
  try {
    $c = $el.Current
    $indent = "  " * $depth
    $lines.Add(("{0}{1} | {2} | id={3} | class={4} | off={5}" -f $indent, $c.ControlType.ProgrammaticName.Replace("ControlType.",""), $c.Name, $c.AutomationId, $c.ClassName, $c.IsOffscreen))
  } catch {}
  try {
    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $child = $walker.GetFirstChild($el)
    while ($child -ne $null) {
      Walk $child ($depth + 1)
      $child = $walker.GetNextSibling($child)
    }
  } catch {}
}
Walk $root 0

$r = $root.Current.BoundingRectangle
if ($r.Width -gt 10 -and $r.Height -gt 10) {
  $bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
  $bmp.Save("E:\rea\taskmanager\tools\taskmgr.png", [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  $lines.Add("RECT: $($r.X),$($r.Y),$($r.Width),$($r.Height)")
}
$lines | Set-Content -Path $OutFile -Encoding UTF8
Write-Output ("nodes={0} hwnd={1} saved={2}" -f $script:count, $hwnd, $OutFile)
