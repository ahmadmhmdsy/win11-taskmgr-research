param([int]$MaxDepth = 7)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$proc = Get-Process Taskmgr | Select-Object -First 1
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond) }
if (-not $win) { Write-Error 'Taskmgr window not found'; exit 1 }
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker

function Walk($el, $depth) {
  if ($null -eq $el -or $depth -gt $MaxDepth) { return }
  $r = $el.Current.BoundingRectangle
  $type = $el.Current.ControlType.ProgrammaticName.Replace('ControlType.','')
  $name = ($el.Current.Name -replace "[`r`n]", ' ')
  $rect = if ([double]::IsNaN($r.X)) { 'none' } else { "$([int]$r.X),$([int]$r.Y),$([int]$r.Width),$([int]$r.Height)" }
  $line = ('  ' * $depth) + "[$type] name='$name' id='$($el.Current.AutomationId)' class='$($el.Current.ClassName)' rect=$rect enabled=$($el.Current.IsEnabled) offscreen=$($el.Current.IsOffscreen)"
  Write-Output $line
  $c = $walker.GetFirstChild($el)
  while ($c) {
    Walk $c ($depth + 1)
    $c = $walker.GetNextSibling($c)
  }
}
Walk $win 0
