@echo off
setlocal

if not "%WsUpdate%"=="" (
  .paket\paket.exe update -g wsbuild --no-install
  if errorlevel 1 exit /b %errorlevel%
)