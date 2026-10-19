
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public class CAP{
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
 public struct RECT{public int L,T,R,B;}
}
"@
$tpid=(Get-Process Taskmgr|Select-Object -First 1).Id
$script:found=[IntPtr]::Zero
$cb=[CAP+EnumProc]{param($h,$l)
 $p=0;[CAP]::GetWindowThreadProcessId($h,[ref]$p)|Out-Null
 if($p -eq $tpid){$c=New-Object System.Text.StringBuilder 256;[CAP]::GetClassNameW($h,$c,256)|Out-Null;if($c.ToString() -eq 'TaskManagerWindow'){$script:found=$h}}
 return $true}
[CAP]::EnumWindows($cb,[IntPtr]::Zero)|Out-Null
$h=$script:found
if($h -eq [IntPtr]::Zero){Write-Output 'NOT FOUND';exit 1}
New-Item -ItemType Directory -Force -Path 'E:\rea\taskmanager\recon\shots' | Out-Null
if([CAP]::IsIconic($h)){[CAP]::ShowWindow($h,9)|Out-Null;Start-Sleep -Milliseconds 600}
[CAP]::SetWindowPos($h,[IntPtr](-1),0,0,0,0,0x0001 -bor 0x0002 -bor 0x0040)|Out-Null
[CAP]::SetForegroundWindow($h)|Out-Null
Start-Sleep -Milliseconds 800
$r=New-Object CAP+RECT;[CAP]::GetWindowRect($h,[ref]$r)|Out-Null
$w=$r.R-$r.L;$ht=$r.B-$r.T
$names=@{1='processes';2='performance';3='apphistory';4='startup';5='users';6='details';7='services'}
foreach($n in 1..7){
  [CAP]::SetForegroundWindow($h)|Out-Null
  Start-Sleep -Milliseconds 250
  [System.Windows.Forms.SendKeys]::SendWait('%'+$n)
  Start-Sleep -Milliseconds 900
  $bmp=New-Object System.Drawing.Bitmap($w,$ht)
  $g=[System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L,$r.T,0,0,$bmp.Size)
  $path='E:\rea\taskmanager\recon\shots\'+$n+'-'+$names[$n]+'.png'
  $bmp.Save($path,[System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose();$bmp.Dispose()
  Write-Output ('saved '+$path)
}
