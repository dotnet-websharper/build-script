dotnet fsi %~dp0\update.fsx

git add -A 
git commit -m "Build fix"
if errorlevel 1 exit 0

git push