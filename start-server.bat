@echo off
setlocal
pushd "%~dp0ServerPart" || exit /b 1
where node.exe >nul 2>&1
if errorlevel 1 (
    echo ERROR: Install Node.js with npm and add it to PATH.
    goto :fail
)
if not exist "app.js" (
    echo ERROR: ServerPart\app.js was not found.
    goto :fail
)
if exist "node_modules\" goto :run
where npm.cmd >nul 2>&1
if errorlevel 1 (
    echo ERROR: npm.cmd was not found in PATH.
    goto :fail
)
echo Installing server dependencies...
call npm.cmd ci
if errorlevel 1 goto :fail
:run
echo Starting server at http://localhost:3005
echo Press Ctrl+C to stop.
node.exe app.js
set "serverExit=%errorlevel%"
if not "%serverExit%"=="0" goto :fail
popd
exit /b 0
:fail
echo Server startup failed. See the error above.
popd
pause
exit /b 1
