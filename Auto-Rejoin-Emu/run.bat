@echo off
chcp 65001 >nul
set PYTHONIOENCODING=utf-8
title == Xenon Style Emu Manager == ^| cook45 for clack
cd /d "%~dp0"

if exist watchdog_updater.py (
    echo [UPDATE] Checking for watchdog updates...
    python watchdog_updater.py
)

python watchdog.py %*

if errorlevel 1 (
    echo.
    echo [ERROR] Watchdog exited with an error.
    pause
)
