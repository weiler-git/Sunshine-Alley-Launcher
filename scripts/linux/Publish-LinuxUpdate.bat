@echo off
setlocal

set "publishScript=%~dp0Publish-LinuxUpdate.ps1"
set "repositoryRoot=%~dp0..\.."

if not exist "%publishScript%" (
  echo ERROR: Publish-LinuxUpdate.ps1 was not found beside this helper.
  echo Place both files in the repository's scripts\linux folder.
  pause
  exit /b 1
)

where pwsh.exe >nul 2>&1
if errorlevel 1 (
  echo ERROR: pwsh.exe was not found. Install PowerShell 7 and try again.
  pause
  exit /b 1
)

set "version="
set /p "version=Version (e.g. 3.1.0): "
if not defined version (
  echo ERROR: Version is required.
  pause
  exit /b 1
)

set "releaseId="
set /p "releaseId=Release ID (e.g. 43): "
if not defined releaseId (
  echo ERROR: Release ID is required.
  pause
  exit /b 1
)

pushd "%repositoryRoot%" >nul
if errorlevel 1 (
  echo ERROR: Could not open the repository root.
  pause
  exit /b 1
)

pwsh.exe -NoExit -ExecutionPolicy Bypass -File "%publishScript%" ^
  -Version "%version%" ^
  -ReleaseId "%releaseId%" ^
  -Channel stable ^
  -RuntimeIdentifier linux-x64 ^
  -MinimumVersion 3.0.0 ^
  -MinimumSupportedVersion 3.0.0 ^
  -PackageBaseUrl "https://sunshinealley.games/launcher/releases"

set "publishExitCode=%errorlevel%"
popd
endlocal & exit /b %publishExitCode%
