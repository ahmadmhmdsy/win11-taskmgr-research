param([long]$Hwnd, [string]$Out = "shot.png")
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public class PW {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
}
'@
$h = [IntPtr]$Hwnd
if ([PW]::IsIconic($h)) { [PW]::ShowWindow($h, 9) | Out-Null; Start-Sleep -Milliseconds 800 }
$r = New-Object PW+RECT
[PW]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $hh = $r.B - $r.T
if ($w -le 0 -or $w -gt 4000) { $w = 1400; $hh = 900; [PW]::SetWindowPos($h, [IntPtr]::Zero, 60, 60, $w, $hh, 0x0040) | Out-Null; Start-Sleep -Milliseconds 800; [PW]::GetWindowRect($h, [ref]$r) | Out-Null; $w = $r.R - $r.L; $hh = $r.B - $r.T }
$bmp = New-Object System.Drawing.Bitmap($w, $hh)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [PW]::PrintWindow($h, $hdc, 2)
$g.ReleaseHdc($hdc)
$bmp.Save((Resolve-Path .).Path + "\" + $Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "hwnd=$Hwnd rect=$($r.L),$($r.T),$w x $hh printwindow=$ok"
