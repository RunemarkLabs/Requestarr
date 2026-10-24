@echo off
rem Runs docker\dev.ps1 for this one run whatever PowerShell's script policy is, with the same arguments.
rem Use it if PowerShell says running scripts is disabled and you can't change the policy.
rem It acts on the QA stack unless told otherwise, like dev.ps1: docker\dev.cmd -Stack dev check
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev.ps1" %*
exit /b %ERRORLEVEL%
