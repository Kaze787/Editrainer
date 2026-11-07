@echo off
title Editrainer - Download images
echo.
echo   Editrainer - Download images
echo.
echo   This downloads about 5,400 Pokemon images (360 MB) and can take several minutes.
echo   You can close this window at any time and run it again later: it continues where it stopped.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-tools\Download-Images.ps1"
echo.
pause
