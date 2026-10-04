@echo off
REM ============================================================
REM   NX 12.0 Sprocket Plugin - commit and push to GitHub
REM ------------------------------------------------------------
REM   Put this file in the plugin folder and double-click it.
REM   It commits ALL local changes and pushes to the remote.
REM
REM   First-time setup (once only):
REM     git remote add origin https://github.com/<user>/<repo>.git
REM     (the first run of this script will then push and link it)
REM
REM   NOTE: this file is deliberately PURE ASCII.
REM         cmd.exe on a Chinese Windows reads .bat as GBK, so
REM         Chinese comments here would become garbage commands.
REM ============================================================

setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

echo.
echo ============================================================
echo   Commit and push to GitHub
echo ============================================================
echo.

where git >nul 2>nul
if errorlevel 1 (
    echo [ERROR] git not found in PATH.
    echo         Install Git for Windows first: https://git-scm.com
    echo.
    pause
    exit /b 1
)

git rev-parse --is-inside-work-tree >nul 2>nul
if errorlevel 1 (
    echo [ERROR] This folder is not a git repository.
    echo         Run this once in this folder:  git init -b main
    echo.
    pause
    exit /b 1
)

for /f "delims=" %%b in ('git rev-parse --abbrev-ref HEAD') do set "BRANCH=%%b"
echo   branch : !BRANCH!

set "REMOTE="
for /f "delims=" %%r in ('git remote') do set "REMOTE=%%r"
if "!REMOTE!"=="" (
    echo   remote : ^(none^)
) else (
    echo   remote : !REMOTE!
)
echo.

echo ------------------------------------------------------------
echo   Changes to be committed
echo ------------------------------------------------------------
git add -A
git status --short
echo.

git diff --cached --quiet
if not errorlevel 1 (
    echo   Working tree clean - nothing new to commit.
    echo.
    goto :PUSH
)

set "MSG="
set /p "MSG=Commit message (press Enter for automatic): "
if "!MSG!"=="" set "MSG=update %DATE% %TIME%"

echo.
echo ------------------------------------------------------------
echo   Commit
echo ------------------------------------------------------------
git commit -m "!MSG!"
if errorlevel 1 (
    echo.
    echo [ERROR] Commit failed - see the message above.
    echo.
    pause
    exit /b 1
)

:PUSH
if "!REMOTE!"=="" goto :NOREMOTE

echo.
echo ------------------------------------------------------------
echo   Push
echo ------------------------------------------------------------
git push -u origin "!BRANCH!"
if errorlevel 1 (
    echo.
    echo [ERROR] Push failed.
    echo   - Not logged in?  GitHub needs a Personal Access Token as
    echo     the password.  Create one at:
    echo         https://github.com/settings/tokens
    echo   - Certificate error (CRYPT_E_NO_REVOCATION_CHECK)?
    echo     See the deployment guide (.txt) for the fix.
    echo.
    pause
    exit /b 1
)

echo.
echo   Done - pushed to !REMOTE!/!BRANCH!
echo.
pause
endlocal
exit /b 0

:NOREMOTE
echo.
echo [ERROR] No remote repository configured yet.
echo         Run this once, then start this script again:
echo.
echo     git remote add origin https://github.com/^<user^>/^<repo^>.git
echo.
pause
endlocal
exit /b 1
