# Construit l'installateur : publie ModPublisher (autonome, .NET inclus) puis compile ModPublisher.iss.
# Résultat : installer\Output\ModPublisher-Setup-<version>.exe
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\ModPublisher\ModPublisher.csproj'
$publish = Join-Path $root 'artifacts\publish'

# Version lue dans le .csproj
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 introuvable (winget install JRSoftware.InnoSetup)" }

& $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot 'ModPublisher.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC a échoué" }
Write-Host "Installateur : $(Join-Path $PSScriptRoot "Output\ModPublisher-Setup-$version.exe")"
