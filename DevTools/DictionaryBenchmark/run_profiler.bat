@echo off
setlocal

if "%~1"=="" (
    echo Drag a Yomitan dictionary ZIP, a container ZIP, or a dictionary folder onto this file.
    echo.
    pause
    exit /b 2
)

py -3 "%~dp0profile_dictionaries.py" --profile "manual=%~1" --output-dir "%~dp0output"
set EXIT_CODE=%ERRORLEVEL%

echo.
if not "%EXIT_CODE%"=="0" echo Profiler exit code: %EXIT_CODE%
pause
exit /b %EXIT_CODE%
