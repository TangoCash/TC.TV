# Baut eine Einzeldatei-EXE und prueft das Ergebnis.
#
# Variante SingleFile (Standard):
#   Ergebnis: NUR TC.TV.exe (ca. 0,9 MB), die runtimeconfig.json ist eingebettet.
#   Voraussetzung: .NET-Desktop-Runtime 8.0 ODER neuer (9/10/...). Das erledigt
#   "rollForward: Major" in TCTV.csproj - der Host wertet es auch aus dem Bundle
#   aus (mit COREHOST_TRACE nachgemessen).
#
# Variante Standalone (selbstenthalten):
#   Ergebnis: NUR TC.TV.exe (ca. 72 MB, komprimiert). Enthaelt die .NET-Laufzeit
#   selbst - auf dem Zielrechner muss gar kein .NET installiert sein.
#
# Aufruf:
#   powershell -File tools\publish-singlefile.ps1                       # eine Datei
#   powershell -File tools\publish-singlefile.ps1 -Variant Standalone   # ohne .NET
#   jeweils optional -Architecture x86
#   optional -Version <x.y.z> setzt die Assembly-Version (im CI aus dem Tag).
param(
    [ValidateSet('x64', 'x86')]
    [string]$Architecture = 'x64',

    [ValidateSet('SingleFile', 'Standalone')]
    [string]$Variant = 'SingleFile',

    [string]$Configuration = 'Release',

    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repo    = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'src\TCTV\TCTV.csproj'
$profile = if ($Variant -eq 'Standalone') { "Standalone-$Architecture" } else { "SingleFile-$Architecture" }

Write-Output "Publish $project ($profile, $Variant, $Configuration)"

$profileArgument = '-p:PublishProfile=' + $profile
$arguments = @($project, '-c', $Configuration, $profileArgument, '--nologo')
if ($Version) {
    $arguments += '-p:Version=' + $Version
}

& dotnet publish @arguments
if ($LASTEXITCODE -ne 0) { throw "dotnet publish ist fehlgeschlagen (ExitCode $LASTEXITCODE)." }

# Nur die oberste Ebene: ein Publish-Ordner ist flach, die Unterordner gehoeren
# zu den anderen Architektur-/Varianten-Ausgaben und werden nicht mitgezaehlt.
$dist = Join-Path $repo 'dist'
if ($Variant -eq 'Standalone') { $dist = Join-Path $dist 'standalone' }
if ($Architecture -eq 'x86') { $dist = Join-Path $dist 'x86' }
$files = Get-ChildItem $dist -File

Write-Output ""
Write-Output "Ergebnis in $dist :"
$files | ForEach-Object { Write-Output ("  {0,-24} {1,12:N0} Bytes" -f $_.Name, $_.Length) }

$expected = @('TC.TV.exe')
$missing  = $expected | Where-Object { -not (Test-Path (Join-Path $dist $_)) }

# Ein danebenliegendes .7z (gepackte Weitergabe der EXE) ist kein Fehler und
# wird als Beiwerk genannt, nicht als ueberzaehlig.
$packed = $files | Where-Object { $_.Name -like 'TC.TV.exe.*' -and $_.Name -notin $expected }
$extra  = $files | Where-Object { $_.Name -notin $expected -and $_.Name -notin ($packed | ForEach-Object { $_.Name }) }

if ($missing -or $extra) {
    Write-Warning ("Unerwarteter Inhalt: fehlend=[{0}] ueberzaehlig=[{1}]" -f ($missing -join ','), (($extra | ForEach-Object { $_.Name }) -join ','))
} else {
    Write-Output ""
    if ($Variant -eq 'Standalone') {
        Write-Output "OK: TC.TV.exe (self-contained). Auf dem Zielrechner ist KEIN .NET noetig, keine weiteren Dateien."
    } else {
        Write-Output "OK: genau eine Datei (runtimeconfig.json ist eingebettet)."
        Write-Output "Voraussetzung auf dem Zielrechner: .NET-Desktop-Runtime ($Architecture) 8.0 ODER neuer (9/10/...)."
    }

    if ($packed) {
        Write-Output ("Beiwerk (nicht vom Skript erzeugt): " + (($packed | ForEach-Object { $_.Name }) -join ', '))
    }
}