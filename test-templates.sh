dotnet new sln -o "templatetest"
pushd "templatetest"

dotnet new -i WebSharper.Templates

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

dotnet new websharper-prx -o Fs.Prx -lang f#
dotnet sln add Fs.Prx/Fs.Prx.fsproj

dotnet new websharper-prx -o Cs.Prx -lang c#
dotnet sln add Cs.Prx/Cs.Prx.csproj

dotnet build

dotnet dev-certs https --clean
dotnet dev-certs https --trust

dotnet fsi ../test-templates.fsx