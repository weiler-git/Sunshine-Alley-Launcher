[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Artifacts = Join-Path $ProjectRoot 'artifacts'
$Solution = Join-Path $ProjectRoot 'Sunshine Alley Launcher.sln'
$AppProject = Join-Path $ProjectRoot 'src/SunshineAlley.App/SunshineAlley.App.csproj'
$CliProject = Join-Path $ProjectRoot 'src/SunshineAlley.Cli/SunshineAlley.Cli.csproj'
$SmokeProject = Join-Path $ProjectRoot 'tests/SunshineAlley.SmokeTests/SunshineAlley.SmokeTests.csproj'

New-Item -ItemType Directory -Force (Join-Path $Artifacts 'publish') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $Artifacts 'packages') | Out-Null

dotnet restore $Solution
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }
dotnet run --project $SmokeProject -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'smoke tests failed' }

foreach ($Rid in @('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')) {
    $PublishRoot = Join-Path $Artifacts "publish/$Rid"
    $AppOutput = Join-Path $PublishRoot 'app'
    $ToolsOutput = Join-Path $PublishRoot 'tools'
    New-Item -ItemType Directory -Force $AppOutput | Out-Null
    New-Item -ItemType Directory -Force $ToolsOutput | Out-Null

    dotnet publish $AppProject -c Release -r $Rid --self-contained true --no-restore `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -o $AppOutput
    if ($LASTEXITCODE -ne 0) { throw "app publish failed for $Rid" }

    dotnet publish $CliProject -c Release -r $Rid --self-contained true --no-restore `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -o $ToolsOutput
    if ($LASTEXITCODE -ne 0) { throw "CLI publish failed for $Rid" }

    if ($Rid.StartsWith('linux-')) {
        $LinuxExecutable = Join-Path $AppOutput 'SunshineAlleyLauncher'
        $Package = Join-Path $Artifacts "packages/SunshineAlleyLauncher-$Rid"
        Copy-Item $LinuxExecutable $Package -Force
        continue
    }
    elseif ($Rid.StartsWith('osx-')) {
        $Bundle = Join-Path $PublishRoot 'Sunshine Alley Launcher.app'
        $MacOs = Join-Path $Bundle 'Contents/MacOS'
        New-Item -ItemType Directory -Force $MacOs | Out-Null
        New-Item -ItemType Directory -Force (Join-Path $Bundle 'Contents/Resources') | Out-Null
        Copy-Item (Join-Path $AppOutput '*') $MacOs -Recurse -Force
        Copy-Item (Join-Path $ProjectRoot 'build/packaging/macos/Info.plist') (Join-Path $Bundle 'Contents/Info.plist')
    }

    $Package = Join-Path $Artifacts "packages/SunshineAlleyLauncher-$Rid.zip"
    if (Test-Path $Package) { Remove-Item $Package -Force }
    Compress-Archive -Path (Join-Path $PublishRoot '*') -DestinationPath $Package
}

Write-Host "Packages and directly downloadable Linux launchers written to $(Join-Path $Artifacts 'packages')"
