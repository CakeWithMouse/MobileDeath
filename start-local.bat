@echo off
setlocal
pushd "%~dp0ServerPart" || exit /b 1
call npm.cmd run db:start
if errorlevel 1 goto :fail
call npm.cmd start
if errorlevel 1 goto :fail
popd
exit /b 0
:fail
echo Command failed. See the error above. Install Node.js 22+ and run setup-local.bat first.
popd
exit /b 1
