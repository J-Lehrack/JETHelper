@echo off
setlocal

if "%~1"=="" (
    echo Drag one or more JETHelper.dictionary-benchmark.jsonl files onto this file.
    echo.
    echo Console example:
    echo   py -3 analyze_runtime_benchmarks.py --dataset "desktop=C:\Path\JETHelper.dictionary-benchmark.jsonl"
    echo.
    pause
    exit /b 2
)

py -3 "%~dp0analyze_runtime_benchmarks.py" %* --output-dir "%~dp0output"
set EXIT_CODE=%ERRORLEVEL%

echo.
if "%EXIT_CODE%"=="1" echo Reports were created, but one or more runs contain lifecycle or reconciliation findings.
if not "%EXIT_CODE%"=="0" if not "%EXIT_CODE%"=="1" echo Analyzer exit code: %EXIT_CODE%
pause
exit /b %EXIT_CODE%
