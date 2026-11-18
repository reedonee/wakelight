# Builds Wakelight with the C# compiler that ships with Windows (.NET Framework 4.8).
# No SDK or downloads needed.  Output: dist\Wakelight.exe and dist\WakelightSetup.exe
param([switch]$AppOnly)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw   = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf  = "$fw\WPF"
$csc  = "$fw\csc.exe"
$dist = Join-Path $root 'dist'
$obj  = Join-Path $root 'obj'
New-Item -ItemType Directory -Force $dist, $obj | Out-Null

if (-not (Test-Path "$root\assets\wakelight.ico")) { & "$root\tools\make-icon.ps1" }

$refs = @("$wpf\PresentationFramework.dll", "$wpf\PresentationCore.dll", "$wpf\WindowsBase.dll", "$fw\System.Xaml.dll",
          'System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Runtime.Serialization.dll', 'System.Xml.dll', 'System.Core.dll', 'System.Management.dll') |
        ForEach-Object { "/r:$_" }

function Invoke-Csc([string]$out, [string[]]$sources, [string[]]$resources, [string]$manifest) {
    $res = $resources | ForEach-Object { "/resource:$_" }
    & $csc /nologo /target:winexe /optimize+ /platform:anycpu /out:$out "/win32icon:$root\assets\wakelight.ico" `
        "/win32manifest:$manifest" @refs @res @sources
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $out" }
    Write-Host "Built $out"
}

# --- app
$app = Join-Path $dist 'Wakelight.exe'
Invoke-Csc $app (Get-ChildItem "$root\src\Wakelight\*.cs").FullName @(
    "$root\src\Shared\Theme.xaml,Theme.xaml",
    "$root\src\Wakelight\MainWindow.xaml,MainWindow.xaml",
    "$root\assets\wakelight.png,wakelight.png",
    "$root\assets\wakelight.ico,wakelight.ico") "$root\src\Wakelight\app.manifest"
if ($AppOnly) { return }

# --- installer (embeds the app)
$setup = Join-Path $dist 'WakelightSetup.exe'
Invoke-Csc $setup (Get-ChildItem "$root\src\Setup\*.cs").FullName @(
    "$root\src\Shared\Theme.xaml,Theme.xaml",
    "$root\src\Setup\Setup.xaml,Setup.xaml",
    "$root\assets\wakelight.png,wakelight.png",
    "$app,payload.Wakelight.exe") "$root\src\Setup\setup.manifest"

