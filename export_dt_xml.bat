@echo off
call "%~dp0tools\ensure_p3r_closed.bat" || (
    echo.
    pause
    exit /b 1
)
echo Exporting DT uassets into editable XML files...
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
echo Running DT exporter. This can take a while...
echo.
"%~dp0tools\dt\P3RDtTool.exe" --batch-export-xml ^
    "%~dp0tools\dt\source" ^
    "%~dp0dt_xml"

echo.
pause




