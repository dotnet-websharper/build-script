#!/usr/bin/env bash
set -euo pipefail

ROOT="${1:-Perf}"
DOTNET_VERSION="${2:-}"
TEMPLATE_VERSION="${3:-}"

rm -rf "$ROOT"
dotnet new sln -n Perf -o "$ROOT"
pushd "$ROOT" >/dev/null

export WebSharperBuildService="True"

# Templates are expected to be installed already by the caller. In CI the workflow installs
# the locally built WebSharper.Templates.*.nupkg explicitly, so DO NOT run an unversioned
# `dotnet new install WebSharper.Templates --force` here: feed resolution can pull a higher
# PUBLIC version over the locally built one, which would make scaffolded projects reference
# the published compiler and silently benchmark the wrong thing.
# Only (re)install when an explicit version is requested (e.g. local manual runs).
if [[ -n "$TEMPLATE_VERSION" ]]; then
  dotnet new install "WebSharper.Templates::$TEMPLATE_VERSION" --force
fi

dotnet new websharper-lib -o Core.Domain -lang f#
dotnet new websharper-lib -o Core.Shared -lang f#
dotnet new websharper-lib -o Client.Components -lang f#
dotnet new websharper-lib -o Client.Components2 -lang f#
dotnet new websharper-lib -o Server.Api -lang f#
dotnet new websharper-spa -o App.Spa -lang f#
dotnet new websharper-web -o App.Web -lang f#

dotnet sln add Core.Domain/Core.Domain.fsproj
dotnet sln add Core.Shared/Core.Shared.fsproj
dotnet sln add Client.Components/Client.Components.fsproj
dotnet sln add Client.Components2/Client.Components2.fsproj
dotnet sln add Server.Api/Server.Api.fsproj
dotnet sln add App.Spa/App.Spa.fsproj
dotnet sln add App.Web/App.Web.fsproj

dotnet add Core.Shared/Core.Shared.fsproj reference Core.Domain/Core.Domain.fsproj
dotnet add Client.Components/Client.Components.fsproj reference Core.Shared/Core.Shared.fsproj
dotnet add Client.Components2/Client.Components2.fsproj reference Core.Shared/Core.Shared.fsproj
dotnet add Server.Api/Server.Api.fsproj reference Core.Shared/Core.Shared.fsproj
dotnet add App.Spa/App.Spa.fsproj reference Client.Components/Client.Components.fsproj
dotnet add App.Spa/App.Spa.fsproj reference Client.Components2/Client.Components2.fsproj
dotnet add App.Spa/App.Spa.fsproj reference Core.Shared/Core.Shared.fsproj
dotnet add App.Web/App.Web.fsproj reference Core.Domain/Core.Domain.fsproj
dotnet add App.Web/App.Web.fsproj reference Core.Shared/Core.Shared.fsproj
dotnet add App.Web/App.Web.fsproj reference Client.Components/Client.Components.fsproj
dotnet add App.Web/App.Web.fsproj reference Client.Components2/Client.Components2.fsproj
dotnet add App.Web/App.Web.fsproj reference Server.Api/Server.Api.fsproj

cat > Directory.Build.props <<'EOF'
<Project>
  <PropertyGroup>
    <WebSharperBuildService>True</WebSharperBuildService>
    <WebSharperLogImportance>High</WebSharperLogImportance>
  </PropertyGroup>
</Project>
EOF

popd >/dev/null
dotnet fsi "$(dirname "$0")/gen-sources.fsx" "$ROOT"
