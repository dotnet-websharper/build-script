dotnet fsi %~dp0\update.fsx

git add -A 
git commit -m "Do not explicitly reference FSharp.Core in paket"
if errorlevel 1 exit 0

git push