@echo off
setlocal
pushd "%~dp0"
set "PS_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
start "NVDriver Installer - Pipeline Resume" "%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -WindowStyle Normal -File "%~dp0resume-workflow.ps1"
popd
endlocal
exit /b 0
