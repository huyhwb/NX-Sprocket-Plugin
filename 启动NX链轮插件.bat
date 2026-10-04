@echo off
REM ============================================================
REM   NX 12.0 Sprocket Plugin - Launcher (portable, auto-detect)
REM ------------------------------------------------------------
REM   Keep this .bat in the plugin folder. It sets UGII_USER_DIR
REM   to its own folder, locates NX 12.0 and starts it.
REM
REM   If NX is not found automatically, do ONE of these:
REM     A) drag NX's ugraf.exe onto this .bat, or run it with the
REM        full path as an argument
REM     B) create nx_path.txt in this folder, first line = full
REM        path of ugraf.exe (no quotes)
REM     C) edit the "set NXEXE=" line below
REM
REM   NOTE: this file is deliberately pure ASCII. cmd.exe on
REM   Chinese Windows reads .bat as GBK/CP936, so UTF-8 Chinese
REM   text inside a .bat gets garbled into errors. A Chinese
REM   FOLDER name is fine, because %~dp0 is resolved at runtime.
REM ============================================================

setlocal EnableExtensions

REM ---- plugin folder = the folder containing this .bat -------
set "UGII_USER_DIR=%~dp0"
if "%UGII_USER_DIR:~-1%"=="\" set "UGII_USER_DIR=%UGII_USER_DIR:~0,-1%"

REM ---- locate NX --------------------------------------------
set "NXEXE="

REM (1) command line argument
if not "%~1"=="" if exist "%~1" set "NXEXE=%~1"

REM (2) nx_path.txt next to this .bat
if not defined NXEXE if exist "%~dp0nx_path.txt" set /p NXEXE=<"%~dp0nx_path.txt"
if defined NXEXE if not exist "%NXEXE%" set "NXEXE="

REM (3) ugraf.exe reachable through PATH
if not defined NXEXE for /f "delims=" %%P in ('where ugraf.exe 2^>nul') do if not defined NXEXE set "NXEXE=%%P"

REM (4) usual install locations, drives C..H
for %%D in (C D E F G H) do if not defined NXEXE call :probe "%%D:\Program Files\Siemens\NX 12.0\NXBIN\ugraf.exe"
for %%D in (C D E F G H) do if not defined NXEXE call :probe "%%D:\Program Files\Siemens\NX 12.0.1\NXBIN\ugraf.exe"
for %%D in (C D E F G H) do if not defined NXEXE call :probe "%%D:\Program Files\Siemens\NX 12.0.2\NXBIN\ugraf.exe"
for %%D in (C D E F G H) do if not defined NXEXE call :probe "%%D:\Program Files\Siemens\NX12.0\NXBIN\ugraf.exe"
for %%D in (C D E F G H) do if not defined NXEXE call :probe "%%D:\Program Files\Siemens\NX 12\NXBIN\ugraf.exe"
for %%D in (C D E F G H) do if not defined NXEXE call :probe "%%D:\Siemens\NX 12.0\NXBIN\ugraf.exe"
for %%D in (C D E F G H) do if not defined NXEXE call :probe "%%D:\Siemens\NX\NXBIN\ugraf.exe"
for %%D in (C D E F G H) do if not defined NXEXE call :probe "%%D:\UGII\NXBIN\ugraf.exe"

echo.
echo ============================================================
echo   NX 12.0 Sprocket Plugin
echo ============================================================
echo   Plugin dir : %UGII_USER_DIR%
echo   Menu       : Sprocket Tool   --^>   Generate Sprocket...
echo.

if not defined NXEXE goto :nonx

echo   NX exe     : %NXEXE%
echo.
echo Starting NX ...
start "" "%NXEXE%"
endlocal
exit /b 0

REM ------------------------------------------------------------
:probe
if not defined NXEXE if exist %1 set "NXEXE=%~1"
exit /b 0

REM ------------------------------------------------------------
:nonx
echo   [ERROR] ugraf.exe not found automatically.
echo.
echo   Tell the launcher where NX is, in any of these ways:
echo     A) drag NX's ugraf.exe onto this .bat, or run:
echo          this-file.bat "D:\Siemens\NX 12.0\NXBIN\ugraf.exe"
echo     B) create nx_path.txt in this folder, first line =
echo          D:\Siemens\NX 12.0\NXBIN\ugraf.exe
echo     C) edit this .bat and set the NXEXE line manually
echo.
pause
endlocal
exit /b 1
