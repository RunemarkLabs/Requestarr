@echo off
rem Runs docker\dev.ps1 for this one run whatever PowerShell's script policy is, with the same arguments.
rem Use it if PowerShell says running scripts is disabled and you can't change the policy.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev.ps1" %*
exit /b %ERRORLEVEL%
