
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public class MOU{
 [DllImport("user32.dll")]public static extern bool EnumWindows(EnumProc cb,IntPtr l);
 public delegate bool EnumProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")]public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]public static extern void mouse_event(uint f,uint dx,uint dy,uint d,IntPtr e);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetClassNameW(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int c);
 public struct RECT{public int L,T,R,B;}
}
"@
$tpid=(Get-Process Taskmgr|Select-Object -First 1).Id
$script:found=[IntPtr]::Zero
$cb=[MOU+EnumProc]{param($h,$l)
 $p=0;[MOU]::GetWindowThreadProcessId($h,[ref]$p)|Out-Null
 if($p -eq $tpid){$c=New-Object System.Text.StringBuilder 256;[MOU]::GetClassNameW($h,$c,256)|Out-Null;if($c.ToString() -eq 'TaskManagerWindow'){$script:found=$h}}
 return $true}
[MOU]::EnumWindows($cb,[IntPtr]::Zero)|Out-Null
$h=$script:found
if($h -eq [IntPtr]::Zero){Write-Output 'NOT FOUND';exit 1}
[MOU]::ShowWindow($h,9)|Out-Null
Start-Sleep -Milliseconds 400
$r=New-Object MOU+RECT;[MOU]::GetWindowRect($h,[ref]$r)|Out-Null
$w=$r.R-$r.L;$ht=$r.B-$r.T
Write-Output ("rect " + $r.L + "," + $r.T + " " + $w + "x" + $ht)
$names=@('processes','performance','apphistory','startup','users','details','services')
$baseY=130
for($i=0;$i -lt 7;$i++){
  $cy=$r.T + $baseY + 39*$i
  [MOU]::SetCursorPos($r.L+24,$cy)|Out-Null
  Start-Sleep -Milliseconds 150
  [MOU]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero)
  [MOU]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 900
  $bmp=New-Object System.Drawing.Bitmap($w,$ht)
  $g=[System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L,$r.T,0,0,$bmp.Size)
  $path='E:\rea\taskmanager\recon\shots\nav'+$i+'-'+$names[$i]+'.png'
  $bmp.Save($path,[System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose();$bmp.Dispose()
  Write-Output ('clicked y='+$cy+' -> '+$path)
}
