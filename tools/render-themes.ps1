# Rendert alle Fenster für jedes Theme nach <OutRoot>\<name>.
# Arbeitet mit einem eigenen Datenordner (--datadir), damit die echten
# Benutzerdaten in %APPDATA%\TC.TV unangetastet bleiben.
# Aufruf: powershell -File tools\render-themes.ps1
param(
    [string]$Exe = 'src\TCTV\bin\Debug\net8.0-windows\TC.TV.exe',
    [string]$OutRoot = 'C:\tctv-render',
    [string]$DataRoot = (Join-Path $env:TEMP 'tctv-render-data')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$exe  = (Resolve-Path $Exe).Path
$work = Join-Path $DataRoot 'TC.TV_Workdir'
$ini  = Join-Path $work 'TC.TV.ini'

$themes = [ordered]@{
    '3' = 'dark-orange'   # Standarddesign (neutrino-images)
    '2' = 'dark-gruen'
    '4' = 'hell'
    '1' = 'overlay'
}

foreach ($key in $themes.Keys) {
    if (Test-Path $DataRoot) { Remove-Item $DataRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $work -Force | Out-Null

    # Minimal-INI; fehlende Schlüssel ergänzt die Anwendung mit Standardwerten.
    $text = "[Benutzerdaten]`r`nSprache=deutsch`r`nTheme=$key`r`n"
    [System.IO.File]::WriteAllText($ini, $text, [System.Text.UTF8Encoding]::new($false))

    $dir = Join-Path $OutRoot $themes[$key]
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }

    $arguments = @('--datadir', $DataRoot, '--render', $dir)
    $process   = Start-Process $exe -ArgumentList $arguments -PassThru -Wait
    $count     = (Get-ChildItem $dir -Filter *.png -ErrorAction SilentlyContinue).Count
    $showed    = (Get-Content $ini | Select-String '^Theme=').Line
    Write-Output ("Theme $key ({0}): ExitCode={1} PNGs={2}  INI:{3}" -f $themes[$key], $process.ExitCode, $count, $showed)
}