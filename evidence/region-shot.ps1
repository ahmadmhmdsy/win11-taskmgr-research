param([int]$X,[int]$Y,[int]$W,[int]$H,[string]$Out="region.png")
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap($W,$H)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($X,$Y,0,0,(New-Object System.Drawing.Size($W,$H)))
$bmp.Save((Resolve-Path .).Path + "\" + $Out,[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "saved $Out $W x $H"
