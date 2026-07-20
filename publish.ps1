<#
.SYNOPSIS
  Builds, tests, and publishes every distributable variant of Word Bomb Tool
  into .\dist\, using the PublishProfiles under each project's Properties
  folder. Run from the repo root (where WordBombTool.sln lives).

.PARAMETER SkipTests
  Skip `dotnet test` before publishing (tests are fast; only skip for a quick
  iteration loop).

.PARAMETER Variant
  Which variant(s) to publish: SelfContainedSingleFile, SelfContainedR2R,
  FrameworkDependent, or All (default).
#>
param(
    [switch]$SkipTests,
    [ValidateSet('SelfContainedSingleFile', 'SelfContainedR2R', 'FrameworkDependent', 'All')]
    [string]$Variant = 'All'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Invoke-Checked {
    param([string]$Description, [scriptblock]$Command)
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE"
    }
}

# Stop any running instances so publish doesn't hit a file-in-use lock.
Get-Process WordBombGUI, WordBombCLI -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Invoke-Checked "Restore & build (Release)" {
    dotnet build "$root\WordBombTool.sln" -c Release
}

if (-not $SkipTests) {
    Invoke-Checked "Run unit tests" {
        dotnet test "$root\src\WordBombTool.Tests\WordBombTool.Tests.csproj" -c Release --logger "console;verbosity=minimal"
    }
}

$profiles = if ($Variant -eq 'All') {
    @('SelfContainedSingleFile', 'SelfContainedR2R', 'FrameworkDependent')
} else {
    @($Variant)
}

foreach ($p in $profiles) {
    Invoke-Checked "Publish GUI ($p)" {
        dotnet publish "$root\src\WordBombGui\WordBombGui.csproj" -c Release -p:PublishProfile=$p
    }
    Invoke-Checked "Publish CLI ($p)" {
        dotnet publish "$root\src\WordBombCli\WordBombCli.csproj" -c Release -p:PublishProfile=$p
    }
}

Write-Host "`nAll requested variants published under $root\dist\" -ForegroundColor Green
Get-ChildItem "$root\dist" -Recurse -Include *.exe | ForEach-Object {
    "{0,10:N1} MB  {1}" -f ($_.Length / 1MB), $_.FullName
}
