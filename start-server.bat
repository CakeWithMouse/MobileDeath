@echo off
setlocal
pushd "%~dp0ServerPart" || exit /b 1
where node.exe >nul 2>&1
if errorlevel 1 (
    echo ERROR: Install Node.js 22 or newer with npm and add it to PATH.
    goto :fail
)
where npm.cmd >nul 2>&1
if errorlevel 1 (
    echo ERROR: npm.cmd was not found in PATH.
    goto :fail
)
echo Installing dependencies from package-lock.json...
call npm.cmd ci
if errorlevel 1 goto :fail
echo Starting MobileDeath API using .env or environment variables.
echo Press Ctrl+C to stop.
call npm.cmd start
if errorlevel 1 goto :fail
popd
exit /b 0
:fail
echo Server startup failed. See the error above.
popd
exit /b 1
