dotnet fsi %~dp0\update.fsx

git add -A
git commit -m "Add websharper.log in .gitignore"

if errorlevel 1 exit 0

git push