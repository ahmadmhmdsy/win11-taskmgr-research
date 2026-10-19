
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public class FOC{
 [DllImport("user32.dll")]public static extern bool EnumWindows(EnumProc cb,IntPtr l);
 public delegate bool EnumProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int c);
 [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int cx,int cy,uint f);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetClassNameW(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("kernel32.dll")]public static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")]public static extern bool AttachThreadInput(uint a,uint b,bool f);
 [DllImport("user32.dll")]public static extern IntPtr GetWindow(IntPtr h,uint c);
 public struct RECT{public int L,T,R,B;}
}
"@
$tpid=(Get-Process Taskmgr|Select-Object -First 1).Id
$script:found=[IntPtr]::Zero
$cb=[FOC+EnumProc]{param($h,$l)
 $p=0;[FOC]::GetWindowThreadProcessId($h,[ref]$p)|Out-Null
 if($p -eq $tpid){$c=New-Object System.Text.StringBuilder 256;[FOC]::GetClassNameW($h,$c,256)|Out-Null;if($c.ToString() -eq 'TaskManagerWindow'){$script:found=$h}}
 return $true}
[FOC]::EnumWindows($cb,[IntPtr]::Zero)|Out-Null
$h=$script:found
if($h -eq [IntPtr]::Zero){Write-Output 'NOT FOUND';exit 1}
if([FOC]::IsIconic($h)){[FOC]::ShowWindow($h,9)|Out-Null;Start-Sleep -Milliseconds 500}
[FOC]::ShowWindow($h,9)|Out-Null
[FOC]::SetWindowPos($h,[IntPtr](-1),0,0,0,0,0x0001 -bor 0x0002 -bor 0x0040)|Out-Null
$fg=[FOC]::GetForegroundWindow()
$tidFg=0;[FOC]::GetWindowThreadProcessId($fg,[ref]$tidFg)|Out-Null
$tidSelf=[FOC]::GetCurrentThreadId()
[FOC]::AttachThreadInput($tidSelf,$tidFg,$true)|Out-Null
[FOC]::SetForegroundWindow($h)|Out-Null
[FOC]::AttachThreadInput($tidSelf,$tidFg,$false)|Out-Null
Start-Sleep -Milliseconds 500
Write-Output ("Foreground now: " + [FOC]::GetForegroundWindow() + " target=" + $h)
$r=New-Object FOC+RECT;[FOC]::GetWindowRect($h,[ref]$r)|Out-Null
$w=$r.R-$r.L;$ht=$r.B-$r.T
Write-Output ("rect " + $r.L + "," + $r.T + " " + $w + "x" + $ht)
$names=@{1='processes';2='performance';3='apphistory';4='startup';5='users';6='details';7='services'}
New-Item -ItemType Directory -Force -Path 'E:\rea\taskmanager\recon\shots' | Out-Null
foreach($n in 1..7){
  [System.Windows.Forms.SendKeys]::SendWait('%'+$n)
  Start-Sleep -Milliseconds 1100
  $bmp=New-Object System.Drawing.Bitmap($w,$ht)
  $g=[System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L,$r.T,0,0,$bmp.Size)
  $path='E:\rea\taskmanager\recon\shots\'+$n+'-'+$names[$n]+'.png'
  $bmp.Save($path,[System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose();$bmp.Dispose()
  Write-Output ('saved '+$path)
}
