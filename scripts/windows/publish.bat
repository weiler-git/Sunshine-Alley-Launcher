@echo off

set /p version=Version (e.g. 3.1.0): 
set /p releaseId=Release ID (e.g. 42): 

pwsh.exe -NoExit -ExecutionPolicy Bypass -File "Publish-WindowsUpdate.ps1" ^
  -Version "%version%" ^
  -ReleaseId "%releaseId%" ^
  -Channel stable ^
  -MinimumVersion 3.0.0 ^
  -PackageBaseUrl "https://sunshinealley.games/launcher/releases" ^
  -PublisherSubject "Sunshine Alley" ^
  -CodeSigningCertificateThumbprint "8BAC2119463F067837614CE999B8E5B43F34592C"