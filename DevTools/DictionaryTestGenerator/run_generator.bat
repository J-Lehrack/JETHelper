@echo off
cd /d "%~dp0"
py main.py
if errorlevel 1 (
    echo.
    echo The generator failed. Ensure Python 3 is installed.
    pause
)
