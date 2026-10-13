@echo off
cd /d "%~dp0..\..\.."
py -3 -m pip install -r dev\gui\requirements.txt
set "RESULT=%ERRORLEVEL%"
pause
exit /b %RESULT%
