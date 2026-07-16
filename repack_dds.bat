@echo off
call "%~dp0tools\ensure_p3r_closed.bat" || (
    echo.
    pause
    exit /b 1
)
set PYTHONIOENCODING=utf-8
echo Repacking DDS textures into base Content and L10N/en only. Other L10N cultures are locked.
echo.
"%~dp0tools\UE4-DDS-Tools\python\python.exe" -B -X utf8 -E "%~dp0tools\dds_l10n\repack_dds_all_l10n.py"
echo.
pause
