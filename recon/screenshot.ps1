Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public class SW{
 [DllImport("user32.dll")]public static extern bool EnumWindows(EnumProc cb,IntPtr l);
 public delegate bool EnumProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int c);
 [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int cx,int cy,uint f);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetClassNameW(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out RECT r);
 public struct RECT{public int L,T,R,B;}
}
"@
$tpid=(Get-Process Taskmgr|Select-Object -First 1).Id
$script:found=[IntPtr]::Zero
$cb=[SW+EnumProc]{param($h,$l)
 $p=0;[SW]::GetWindowThreadProcessId($h,[ref]$p)|Out-Null
 if($p -eq $tpid){$c=New-Object System.Text.StringBuilder 256;[SW]::GetClassNameW($h,$c,256)|Out-Null;if($c.ToString() -eq 'TaskManagerWindow'){$script:found=$h}}
 return $true}
[SW]::EnumWindows($cb,[IntPtr]::Zero)|Out-Null
$h=$script:found
if($h -eq [IntPtr]::Zero){Write-Error 'not found';exit 1}
if([SW]::IsIconic($h)){[SW]::ShowWindow($h,9)|Out-Null;Start-Sleep -Milliseconds 800}
[SW]::SetWindowPos($h,[IntPtr](-1),0,0,0,0,0x0001 -bor 0x0002 -bor 0x0040)|Out-Null
[SW]::SetForegroundWindow($h)|Out-Null
Start-Sleep -Milliseconds 900
$r=New-Object SW+RECT;[SW]::GetWindowRect($h,[ref]$r)|Out-Null
$w=$r.R-$r.L;$ht=$r.B-$r.T
$bmp=New-Object System.Drawing.Bitmap($w,$ht)
$g=[System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L,$r.T,0,0,$bmp.Size)
$bmp.Save('E:\rea\taskmanager\recon\taskmgr.png',[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose();$bmp.Dispose()
Write-Output "saved ${w}x${ht} at $($r.L),$($r.T)"
