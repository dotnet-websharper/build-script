@ECHO OFF

git branch -b websharper70
git checkout websharper70
git push -u origin websharper70

git checkout master
dotnet fsi %~dp0\fix60Dependencies.fsx
git add -A 
git commit -m "Fix WebSharper dependencies to 6.x"

git push

exit 0