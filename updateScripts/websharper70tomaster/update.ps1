$dirname = Split-Path -Path (Get-Location) -Leaf

if (($dirname -eq "core") -or ($dirname -eq "ui")) 
{
	exit 0
}

git branch websharper60
git checkout websharper60
git push -u origin websharper60

echo "${{ secrets.PAT_PR }}" > token.txt
gh auth login --with-token < token.txt

git checkout websharper70
gh pr create -t "WebSharper 7.0" -b "WebSharper 7.0"
