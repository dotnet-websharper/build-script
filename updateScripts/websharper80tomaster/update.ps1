$dirname = Split-Path -Path (Get-Location) -Leaf

if (($dirname -eq "core") -or ($dirname -eq "ui")) 
{
	exit 0
}

git branch websharper70
git checkout websharper70
git push -u origin websharper70

gh auth login --with-token ${env:PR_TOKEN}

git checkout net8upgrade
gh pr create -t "WebSharper 8.0" -b "WebSharper 8.0"
