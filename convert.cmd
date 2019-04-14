:: Run this script from the root directory of the project to convert.
@echo off

if not exist %~dp0\.paket\fake.exe dotnet tool install fake-cli --tool-path %~dp0\.paket

%~dp0\.paket\fake.exe run %~dp0\convert.fsx