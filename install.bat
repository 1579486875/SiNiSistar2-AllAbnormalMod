@echo off
chcp 65001 >nul
title SiNiSistar 2  -  AllAbnormalMod Installer
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
echo.
pause