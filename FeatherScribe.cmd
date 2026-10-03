@echo off
rem FeatherScribe launcher: double-click to build the app and the console
rem launcher (incremental, Release), then run the launcher in this console.
rem The launcher asks whether to use LLM formatting. With LLM formatting it
rem starts Ollama when needed, lets you pick an installed model and waits until
rem the model is loaded; then it starts the app and this console closes.
rem Arguments are passed to the launcher, for example "--llm off" (never ask,
rem start without LLM formatting) or "--llm on --model gemma4:e2b".
rem All paths are relative to this file's folder.
rem Test hook: set FEATHERSCRIBE_LAUNCH_DRY_RUN=1 to run every step except
rem starting Ollama and the app; the launcher reads it from the environment and
rem prints the app command line instead.
rem This file must stay ASCII with CRLF line endings. find and timeout are called
rem from System32 so Unix tools of the same name on PATH are not picked up.
setlocal
pushd "%~dp0"

set "FS_PROJECT=src\FeatherScribe.App\FeatherScribe.App.csproj"
set "FS_EXE=src\FeatherScribe.App\bin\Release\net10.0-windows\FeatherScribe.App.exe"
set "FS_LAUNCHER_PROJECT=src\FeatherScribe.Launcher\FeatherScribe.Launcher.csproj"
set "FS_LAUNCHER_EXE=src\FeatherScribe.Launcher\bin\Release\net10.0-windows\FeatherScribe.Launcher.exe"

if not exist "config\appsettings.json" goto no_config

where dotnet >nul 2>nul
if errorlevel 1 goto no_dotnet

tasklist /FI "IMAGENAME eq FeatherScribe.App.exe" /NH | "%SystemRoot%\System32\find.exe" /I "FeatherScribe.App.exe" >nul
if not errorlevel 1 goto already_running

if exist "local\whisper\" goto build
echo whisper.cpp is not set up yet (local\whisper). See README: setup.

:build
dotnet build "%FS_PROJECT%" -c Release --nologo -v minimal
if errorlevel 1 goto build_failed
dotnet build "%FS_LAUNCHER_PROJECT%" -c Release --nologo -v minimal
if errorlevel 1 goto build_failed

if not exist "%FS_EXE%" goto no_exe
if not exist "%FS_LAUNCHER_EXE%" goto no_launcher

"%FS_LAUNCHER_EXE%" --app "%FS_EXE%" %*
set "FS_EXIT=%ERRORLEVEL%"
popd
if not "%FS_EXIT%"=="0" pause
exit /b %FS_EXIT%

:already_running
echo FeatherScribe is already running (see the tray icon).
popd
"%SystemRoot%\System32\timeout.exe" /t 3 >nul 2>nul
exit /b 0

:no_config
echo config\appsettings.json not found. Run this file from the FeatherScribe repository root.
goto fail

:no_dotnet
echo dotnet SDK (.NET 10) was not found. Install it from https://dotnet.microsoft.com/ and try again.
goto fail

:build_failed
echo Build failed. See the messages above.
goto fail

:no_exe
echo FeatherScribe.App.exe was not found after the build.
goto fail

:no_launcher
echo FeatherScribe.Launcher.exe was not found after the build.
goto fail

:fail
popd
pause
exit /b 1
