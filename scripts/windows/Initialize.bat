echo "overwrite private key?"
pause
echo "sure?"
pause

pwsh.exe -NoExit -ExecutionPolicy Bypass -File "Initialize-UpdateSigning.ps1"