@echo off
setlocal

if not "%WsUpdate%"=="" (
  dotnet paket update -g wsbuild --no-install
  if errorlevel 1 exit /b %errorlevel%
)