@echo off
setlocal
cd /d "%~dp0"
if exist "node_modules\electron\cli.js" (
  call npm start
  exit /b %errorlevel%
)
echo Electron dependencies are not installed. Opening the browser version instead.
start "CHUNITHM Mobile Skin Studio" "%~dp0index.html"
