dotnet tool uninstall fake-cli
dotnet tool install fake-cli --version 5.20.4

git add -A 
git commit -m "Revert to FAKE 5.20.4" 
if errorlevel 1 exit 0

git push