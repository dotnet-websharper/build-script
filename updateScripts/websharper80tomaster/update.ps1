$dirname = Split-Path -Path (Get-Location) -Leaf

if (($dirname -eq "core") -or ($dirname -eq "ui")) 
{
	exit 0
}

git branch websharper70
git checkout websharper70
git push -u origin websharper70
