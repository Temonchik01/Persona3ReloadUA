@echo off
call "%~dp0tools\ensure_p3r_closed.bat" || (
    echo.
    pause
    exit /b 1
)
setlocal EnableExtensions

echo Exporting BMD uassets into editable XML files...
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
set "ROOT=%~dp0"
set "SRC=%ROOT%tools\dds_l10n\source"
set "OUT=%ROOT%xml"
set "TOOL=%ROOT%tools\bmd\P3RBmdTool.exe"

if not exist "%TOOL%" (
    echo Missing tool: %TOOL%
    echo.
    pause
    exit /b 1
)

if not exist "%SRC%" (
    echo Missing source folder: %SRC%
    echo.
    pause
    exit /b 1
)

echo Running BMD exporter. This can take a while...
echo.
"%TOOL%" --batch-export-xml-uasset "%SRC%" "%OUT%"

echo.
pause
