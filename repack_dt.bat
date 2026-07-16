@echo off
rem REPACK_MODE=changed  - repack only XML files with real changes
rem REPACK_MODE=force    - repack every XML file, useful for debugging
set "REPACK_MODE=changed"
set "REPACK_ARGS="
if /I "%REPACK_MODE%"=="force" set "REPACK_ARGS=--force"
set "OUT_DIR=%~dp0UnrealEssentials\P3R\Content\L10N\en"
if /I not "%OUT_DIR:~-7%"=="L10N\en" (
    echo ERROR: DT output must be L10N\en only.
    pause
    exit /b 1
)

call "%~dp0tools\ensure_p3r_closed.bat" || (
    echo.
    pause
    exit /b 1
)
echo Repacking translated DT XML files into L10N/en uassets... [%REPACK_MODE%]
echo Guard: DT output is locked to L10N/en only.
echo.

"%~dp0tools\dt\P3RDtTool.exe" --batch-import-xml ^
    "%~dp0dt_xml" ^
    "%~dp0tools\dt\source" ^
    "%OUT_DIR%" ^
    %REPACK_ARGS%

echo.
pause
