@echo off
REM ============================================================
REM   NX 12.0 Sprocket Plugin - build a clean portable copy
REM ------------------------------------------------------------
REM   Creates the folder  "_portable"  next to this .bat,
REM   containing ONLY the files needed on another computer.
REM   Copy (or zip) that folder to the target PC, then follow
REM   the deployment guide (.txt file in this folder).
REM
REM   NOTE: the output folder is deleted and rebuilt on every run.
REM         It is created by this script only.
REM         This file is deliberately pure ASCII (see the launcher
REM         for the reason).
REM ============================================================

setlocal EnableExtensions

set "SRC=%~dp0"
set "DST=%~dp0_portable"

echo.
echo ============================================================
echo   Build portable copy
echo ============================================================
echo   Source : %SRC%
echo   Output : %DST%
echo.

if exist "%DST%" (
    echo   Removing old output folder ...
    rmdir /s /q "%DST%"
)

mkdir "%DST%" 2>nul
mkdir "%DST%\startup" 2>nul
mkdir "%DST%\application" 2>nul

REM --- plugin payload -----------------------------------------
for %%F in ("%SRC%startup\*.*")     do copy /y "%%~fF" "%DST%\startup\"     >nul
for %%F in ("%SRC%application\*.*") do copy /y "%%~fF" "%DST%\application\" >nul

REM --- launcher + docs (skip this script itself) ---------------
for %%F in ("%SRC%*.bat" "%SRC%*.txt" "%SRC%*.md") do if /i not "%%~nxF"=="%~nx0" copy /y "%%~fF" "%DST%\" >nul

echo   Copied:
echo     _portable\startup\       (menu, journal, dialog, icon)
echo     _portable\application\   (dialog + journal copy)
echo     _portable\               (launcher + docs)
echo.
echo   Done. Copy this folder to the target PC:
echo     %DST%
echo.
pause
endlocal
