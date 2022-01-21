copy ..\build-script\updateScripts\globalJson\global.json .

git add -A 
git commit -m "Use global.json for .NET 6.0.x"
if errorlevel 1 exit 0

git push