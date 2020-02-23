@echo off
setlocal
set PATH=%GitToolPath%;%PATH%

if not "%WsUpdate%"=="" (
  .paket\paket.exe update -g build --no-install
  if errorlevel 1 exit /b %errorlevel%

  .paket\paket.exe restore -g build
  if errorlevel 1 exit /b %errorlevel%
)

cls

if not "%BuildBranch%"=="" (
  packages\build\FAKE\tools\FAKE.exe build.fsx ws-checkout
  if errorlevel 1 (
    exit /b %errorlevel%
  )

  set /p BuildFromRef=<build\buildFromRef
)

if "%MSBUILD%"=="" (
  if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Preview\MSBuild\Current\Bin\" (
    set "MSBUILD=%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Preview\MSBuild\Current\Bin\"
  )
  if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Community\MSBuild\Current\Bin\" (
    set "MSBUILD=%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Community\MSBuild\Current\Bin\"
  )
  if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Professional\MSBuild\Current\Bin\" (
    set "MSBUILD=%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Professional\MSBuild\Current\Bin\"
  )
  if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Enterprise\MSBuild\Current\Bin\" (
    set "MSBUILD=%ProgramFiles(x86)%\Microsoft Visual Studio\2017\Enterprise\MSBuild\Current\Bin\"
  )
  if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Preview\MSBuild\Current\Bin\" (
    set "MSBUILD=%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Preview\MSBuild\Current\Bin\"
  )
  if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\" (
    set "MSBUILD=%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\"
  )
  if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\" (
    set "MSBUILD=%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\"
  )
  if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\" (
    set "MSBUILD=%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\"
  )
)

if "%VisualStudioVersion%"=="" (
  if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2017\" (
    set VisualStudioVersion=15.0
  )
  if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2019\" (
    set VisualStudioVersion=16.0
  )
)

packages\build\FAKE\tools\FAKE.exe build.fsx %*
