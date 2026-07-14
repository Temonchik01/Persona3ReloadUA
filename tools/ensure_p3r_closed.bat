@echo off
setlocal EnableExtensions

tasklist /FI "IMAGENAME eq P3R.exe" /NH | find /I "P3R.exe" >nul
if not errorlevel 1 (
    echo.
    echo P3R.exe is still running.
    echo Close the game before exporting or repacking mod files.
    echo No files were changed.
    exit /b 1
)

exit /b 0
