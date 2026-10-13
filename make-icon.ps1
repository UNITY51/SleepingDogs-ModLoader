<#
 Достаёт иконку из exe игры и сохраняет как src\SDModLoader.App\Assets\app.ico
 (в репозиторий иконку игры не кладём — это чужой товарный знак).
 Пример:  powershell -ExecutionPolicy Bypass -File make-icon.ps1 "D:\Steam\steamapps\common\SleepingDogsDefinitiveEdition\SDHDShip.exe"
#>
param([Parameter(Mandatory=$true)][string]$Exe)
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::ExtractAssociatedIcon($Exe)
$out = Join-Path $PSScriptRoot "src\SDModLoader.App\Assets\app.ico"
$fs = [IO.File]::Create($out); $icon.Save($fs); $fs.Close()
Write-Host "Saved $out. Now run publish.cmd"
