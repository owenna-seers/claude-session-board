# 세션 위젯 빌드: 윈도우 기본 C# 컴파일러(.NET Framework 4.x, C# 5)로 SessionWidget.exe 를 만든다.
#   .\build.ps1                 → %USERPROFILE%\.claude\session-board\app\SessionWidget.exe
#   .\build.ps1 -Out <경로>     → 그 자리에
#   .\build.ps1 -Stop           → 떠 있는 위젯을 먼저 끈다(exe 가 잠겨 있으면 빌드가 안 됨)
#   .\build.ps1 -Run            → 빌드 뒤 바로 띄운다
param(
    [string]$Out = (Join-Path $env:USERPROFILE '.claude\session-board\app\SessionWidget.exe'),
    [switch]$Stop,
    [switch]$Run
)
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$fw  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path "$fw\csc.exe")) { $fw = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319' }
if (-not (Test-Path "$fw\csc.exe")) { throw "csc.exe 가 없습니다 (.NET Framework 4.x). 윈도우 10/11 에는 기본으로 있습니다: $fw" }
$wpf = Join-Path $fw 'WPF'

if ($Stop) {
    Get-Process -Name 'SessionWidget' -ErrorAction SilentlyContinue |
        Where-Object { try { $_.Path -eq $Out } catch { $false } } |
        ForEach-Object { $_.CloseMainWindow() | Out-Null; if (-not $_.WaitForExit(3000)) { $_.Kill() } }
    Start-Sleep -Milliseconds 300
}

$outDir = Split-Path -Parent $Out
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force $outDir | Out-Null }

$refs = @(
    "$wpf\PresentationFramework.dll", "$wpf\PresentationCore.dll", "$wpf\WindowsBase.dll",
    "$fw\System.Xaml.dll", "$fw\System.Web.Extensions.dll", "$fw\System.Windows.Forms.dll",
    "$fw\System.Drawing.dll", "$fw\Microsoft.VisualBasic.dll", "$fw\System.Core.dll", "$fw\System.dll"
) | ForEach-Object { "/r:$_" }
$icon = Join-Path $src 'app.ico'
if (-not (Test-Path $icon)) { & (Join-Path $src 'make-icon.ps1') -Out $icon | Out-Null }
& "$fw\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 "/out:$Out" "/win32icon:$icon" @refs (Join-Path $src 'SessionWidget.cs')
if ($LASTEXITCODE -ne 0) { throw "빌드 실패 ($LASTEXITCODE)" }
"built: $Out"
if ($Run) { Start-Process -FilePath $Out }
