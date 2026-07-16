@echo off
call "%~dp0tools\ensure_p3r_closed.bat" || (
    echo.
    pause
    exit /b 1
)
echo Exporting UI texture uassets into editable DDS files...
echo.
echo Are you sure? It will rewrite the folder and assets.
choice /c YN /n /m "Continue? [Y/N]: "
if errorlevel 2 (
    echo.
    echo Cancelled.
    echo.
    pause
    exit /b 0
)
echo.
echo Running DDS exporter. This can take a while on first scan...
echo.
"%~dp0tools\UE4-DDS-Tools\python\python.exe" -u -B -E "%~dp0tools\UE4-DDS-Tools\src\p3rtex.py" --batch-export ^
    "%~dp0tools\dds_l10n\source" ^
    "%~dp0dds"

echo.
pause




