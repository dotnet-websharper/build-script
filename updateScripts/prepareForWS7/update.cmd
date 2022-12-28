@ECHO OFF

git checkout websharper70
git push -u origin websharper70
git checkout master
dotnet fsi fix60Dependencies.fsx

git add -A 
git commit -m "Fix WebSharper dependencies to 6.x"
if errorlevel 1 exit 0

git push