@echo off
chcp 65001 >nul
cd /d "%~dp0..\..\.."
py -3 -c "import tkinter; import PIL" >nul 2>&1
if errorlevel 1 (
    echo Потрібні Python 3 з Tkinter та Pillow.
    echo Запустіть install_gui_dependencies.bat після встановлення Python.
    pause
    exit /b 1
)
py -3 dev\gui\app.py
if errorlevel 1 pause
