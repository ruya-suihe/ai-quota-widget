param([ValidateSet('default','10','4')][string]$Variant = 'default', [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$destination = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $PSScriptRoot '../../artifacts/CodexQuota' }
[void][IO.Directory]::CreateDirectory($destination)
$images = @()
foreach ($size in @(16,32,48,256)) {
    $bitmap = [Drawing.Bitmap]::new($size,$size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.Clear([Drawing.Color]::FromArgb(27,33,48))
    $background = [Drawing.Pen]::new([Drawing.Color]::FromArgb(60,73,94), [single]($size * 0.105))
    $accent = [Drawing.Pen]::new([Drawing.Color]::FromArgb(126,226,186), [single]($size * 0.105))
    $accent.StartCap = 'Round'; $accent.EndCap = 'Round'
    $rect = [Drawing.RectangleF]::new([single]($size*0.17),[single]($size*0.17),[single]($size*0.66),[single]($size*0.66))
    $graphics.DrawEllipse($background,$rect)
    $graphics.DrawArc($accent,$rect,-90,270)
    $brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(145,185,255))
    $graphics.FillEllipse($brush,[single]($size*0.405),[single]($size*0.405),[single]($size*0.19),[single]($size*0.19))
    $buffer = [IO.MemoryStream]::new()
    $bitmap.Save($buffer,[Drawing.Imaging.ImageFormat]::Png)
    $images += ,([pscustomobject]@{Size=$size;Bytes=$buffer.ToArray()})
    if ($size -eq 256) { $bitmap.Save((Join-Path $PSScriptRoot 'Icon-preview.png'),[Drawing.Imaging.ImageFormat]::Png) }
    $buffer.Dispose(); $brush.Dispose(); $background.Dispose(); $accent.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$iconPath = Join-Path $PSScriptRoot 'Widget.ico'
$writer = [IO.BinaryWriter]::new([IO.File]::Create($iconPath))
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($item in $images) {
        $dimension = if ($item.Size -eq 256) {0} else {$item.Size}
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$item.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $item.Bytes.Length
    }
    foreach ($item in $images) { $writer.Write([byte[]]$item.Bytes) }
} finally { $writer.Dispose() }
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$name = if ($Variant -eq 'default') { 'UsageQuery.exe' } else { 'UsageQuery_' + $Variant + '.exe' }
$output = Join-Path $destination $name
$arguments = @('/nologo','/target:winexe',"/out:$output", "/win32icon:$iconPath",
    "/resource:$PSScriptRoot/ProjectQr.png,ProjectQr.png",
    "/resource:$iconPath,Widget.ico", "/resource:$PSScriptRoot/Widget.xaml,Widget.xaml",
    "/reference:$framework/WPF/WindowsBase.dll", "/reference:$framework/WPF/PresentationCore.dll",
    "/reference:$framework/WPF/PresentationFramework.dll", '/reference:System.Xaml.dll',
    '/reference:System.Web.Extensions.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', (Join-Path $PSScriptRoot 'AppServices.cs'), (Join-Path $PSScriptRoot 'StartupSettings.cs'), (Join-Path $PSScriptRoot 'SettingsAppearance.cs'), (Join-Path $PSScriptRoot 'RefreshPolicy.cs'), (Join-Path $PSScriptRoot 'Theme.cs'), (Join-Path $PSScriptRoot 'LayoutMetrics.cs'), (Join-Path $PSScriptRoot 'WidgetLayout.cs'), (Join-Path $PSScriptRoot 'LayoutContractChecks.cs'), (Join-Path $PSScriptRoot 'QuotaWidget.cs'))
$arguments += (Join-Path $PSScriptRoot 'AboutSettings.cs')
$arguments += (Join-Path $PSScriptRoot 'QuotaProviders.cs')
$arguments += (Join-Path $PSScriptRoot 'ClaudeConnection.cs')
$arguments += (Join-Path $PSScriptRoot 'ClaudeDesktopAccount.cs')
$arguments += (Join-Path $PSScriptRoot 'ProviderNetwork.cs')
$arguments += (Join-Path $PSScriptRoot 'ClientCredentials.cs')
$arguments += (Join-Path $PSScriptRoot 'ClientSqlite.cs')
$arguments += (Join-Path $PSScriptRoot 'ClientCrypto.cs')
$arguments += '/reference:System.Security.dll'
$arguments += (Join-Path $PSScriptRoot 'ScifiTheme.cs')
$arguments += (Join-Path $PSScriptRoot 'ScifiSequence.cs')
$arguments += (Join-Path $PSScriptRoot 'ScifiGeometry.cs')
$arguments += (Join-Path $PSScriptRoot 'ScifiModels.cs')
$arguments += (Join-Path $PSScriptRoot 'ScifiImpact.cs')
$arguments += (Join-Path $PSScriptRoot 'ScifiPortal.cs')
$arguments += (Join-Path $PSScriptRoot 'TidalScene.cs')
$arguments += (Join-Path $PSScriptRoot 'InkScene.cs')
$arguments += (Join-Path $PSScriptRoot 'SteamScene.cs')
$arguments += (Join-Path $PSScriptRoot 'StarryScene.cs')
$arguments += (Join-Path $PSScriptRoot 'StarryRibbonFlow.cs')
$arguments += (Join-Path $PSScriptRoot 'SpiralFlow.cs')
foreach($asset in @('scifi-chamber.png','model-reference.png','perspectives.png','rider-cannon.png','scout-rear.png','frigate-side.png','tidal-day.png','tidal-night.png','tidal-animals.png','tidal-celestials-v2.png','ink-landscape.png','steam-workshop.png','steam-parts.png','starry-night.png')) {
    $arguments += "/resource:$PSScriptRoot/Assets/Scifi/$asset,Scifi.$asset"
}
if ($Variant -ne 'default') { $arguments += '/define:SEGMENT' + $Variant }
& (Join-Path $framework 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output $output
