dotnet fsi %~dp0\update.fsx

git add -A 
git commit -m "Do not fix FSharp.Core version"
if errorlevel 1 exit 0

git push