param([int]$TargetPid = 3656, [string]$OutFile = "uia-tree.json")
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $TargetPid)
$wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
$nodes = New-Object System.Collections.ArrayList
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$script:count = 0
function Walk($el, $depth) {
    if ($null -eq $el -or $depth -gt 30 -or $script:count -gt 6000) { return }
    $script:count++
    try {
        $r = $el.Current.BoundingRectangle
        [void]$nodes.Add([pscustomobject]@{
            depth = $depth
            name = $el.Current.Name
            type = ($el.Current.ControlType.ProgrammaticName -replace 'ControlType\.','')
            id = $el.Current.AutomationId
            cls = $el.Current.ClassName
            enabled = $el.Current.IsEnabled
            offscreen = $el.Current.IsOffscreen
            x = [int]$r.X; y = [int]$r.Y; w = [int]$r.Width; h = [int]$r.Height
        })
    } catch {}
    $child = $walker.GetFirstChild($el)
    while ($null -ne $child) {
        Walk $child ($depth + 1)
        $child = $walker.GetNextSibling($child)
    }
}
foreach ($w in $wins) { Walk $w 0 }
$nodes | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 $OutFile
Write-Output "windows=$($wins.Count) nodes=$($nodes.Count)"
