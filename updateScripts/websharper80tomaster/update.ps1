$dirname = Split-Path -Path (Get-Location) -Leaf

if (($dirname -eq "core") -or ($dirname -eq "ui")) 
{
	exit 0
}

try {
  git branch websharper70
  git checkout websharper70
  git push -u origin websharper70
} catch {
	Write-Output "Creating websharper70 branch has failed"
}

git checkout master
git merge net8upgrade
git push origin master