# Usage: test-templates.sh <dotnet version> [<templates version>] [<booster: true|false>]
# booster=true installs the WebSharper.FSharp.Booster and WebSharper.CSharp.Booster tools in a tool manifest,
# and checks that the build used them; otherwise it checks that the build used the portable compilers.
set -e
set -o pipefail

booster="${3:-false}"

dotnet --list-sdks

dotnet new sln -o "templatetest"
pushd "templatetest"

export WebSharperBuildService="True"

if [[ $1 =~ ^6.* ]]; then
  if [ -z "$2" ]; then
    dotnet new -i WebSharper.Templates
  else
    dotnet new -i WebSharper.Templates::$2
  fi
else
  if [ -z "$2" ]; then
    dotnet new install WebSharper.Templates
  else
    dotnet new install WebSharper.Templates::$2
  fi
fi

dotnet new websharper-lib -o Fs.Lib -lang f#
dotnet sln add Fs.Lib/Fs.Lib.fsproj

dotnet new websharper-lib -o Cs.Lib -lang c#
dotnet sln add Cs.Lib/Cs.Lib.csproj

dotnet new websharper-ext -o Fs.Ext -lang f#
dotnet sln add Fs.Ext/Fs.Ext.fsproj

dotnet new websharper-web -o Fs.Web -lang f#
dotnet sln add Fs.Web/Fs.Web.fsproj

dotnet add Fs.Web/Fs.Web.fsproj reference Fs.Lib/Fs.Lib.fsproj
dotnet add Fs.Web/Fs.Web.fsproj reference Fs.Ext/Fs.Ext.fsproj
dotnet add Fs.Web/Fs.Web.fsproj reference Cs.Lib/Cs.Lib.csproj

dotnet new websharper-web -o Cs.Web -lang c#
dotnet sln add Cs.Web/Cs.Web.csproj

dotnet new websharper-spa -o Fs.Spa -lang f#
dotnet sln add Fs.Spa/Fs.Spa.fsproj

dotnet new websharper-spa -o Cs.Spa -lang c#
dotnet sln add Cs.Spa/Cs.Spa.csproj

dotnet new websharper-html -o Fs.Html -lang f#
dotnet sln add Fs.Html/Fs.Html.fsproj

dotnet new websharper-html -o Cs.Html -lang c#
dotnet sln add Cs.Html/Cs.Html.csproj

dotnet new websharper-min -o Fs.Min -lang f#
dotnet sln add Fs.Min/Fs.Min.fsproj

dotnet new websharper-min -o Cs.Min -lang c#
dotnet sln add Cs.Min/Cs.Min.csproj

dotnet new websharper-svc -o Fs.Svc -lang f#
dotnet sln add Fs.Svc/Fs.Svc.fsproj

dotnet new websharper-svc -o Cs.Svc -lang c#
dotnet sln add Cs.Svc/Cs.Svc.csproj

dotnet new websharper-prx -o Fs.Prx -lang f#
dotnet sln add Fs.Prx/Fs.Prx.fsproj

dotnet new websharper-prx -o Cs.Prx -lang c#
dotnet sln add Cs.Prx/Cs.Prx.csproj

if [ "$booster" = "true" ]; then
  # the Booster tools must have exactly the version of the WebSharper packages the templates reference
  dotnet restore
  wsversion=$(dotnet msbuild Fs.Lib/Fs.Lib.fsproj -getProperty:WebSharperCompilerVersion)
  if [ -z "$wsversion" ]; then
    echo "WebSharperCompilerVersion is not set by the WebSharper.FSharp package"
    exit 1
  fi
  echo "Installing the Booster tools $wsversion"
  dotnet new tool-manifest
  dotnet tool install WebSharper.FSharp.Booster --version "$wsversion"
  dotnet tool install WebSharper.CSharp.Booster --version "$wsversion"
fi

dotnet build -v d 2>&1 | tee build.log

# the WebSharper task logs which compiler it runs
if [ "$booster" = "true" ]; then
  expected=("Using the WebSharper.FSharp.Booster" "Using the WebSharper.CSharp.Booster")
else
  expected=("Using the portable WebSharper compiler: no tool manifest lists WebSharper.FSharp.Booster" "Using the portable WebSharper compiler: no tool manifest lists WebSharper.CSharp.Booster")
fi
for e in "${expected[@]}"; do
  if ! grep -q "$e" build.log; then
    echo "Expected in the build log: $e"
    exit 1
  fi
done
if grep -q "WSB910[12]" build.log; then
  echo "Booster tool warnings in the build log:"
  grep "WSB910[12]" build.log
  exit 1
fi
echo "Build used the expected compilers (booster=$booster)"

dotnet fsi ../test-templates.fsx
