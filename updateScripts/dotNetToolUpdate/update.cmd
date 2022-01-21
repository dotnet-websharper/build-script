dotnet tool update paket
dotnet tool update fake-cli

git add -A 
git commit -m "Update dotnet tools" 
if errorlevel 1 exit 0

git push