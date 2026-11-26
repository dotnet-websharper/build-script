open System.Net.Http
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

type Message =
    {
        content: string
        username: string
        avatar_url: string
        tts: bool
        embeds: MessageEmbed []
    }

and MessageEmbed =
    {
        color: System.Nullable<int>
        title: string
        url: string
        description: string
    }

let parseNuGetPackageFileName (fileName: string) =
    let name = Path.GetFileNameWithoutExtension(fileName)

    let pattern =
        @"^(?<name>.+)\.(?<version>\d+(?:\.\d+){3}(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?)$"

    let m = Regex.Match(name, pattern)

    if not m.Success then
        None
    else
        let packageName = m.Groups.["name"].Value
        let versionString = m.Groups.["version"].Value

        Some (packageName, versionString)

let hookurl = System.Environment.GetEnvironmentVariable "DISCORD_PACKAGE_FEED"

/// Usage: dotnet fsi ./notification-helper.fsx [github|nuget] [packageFolder]
/// `github` (the default, build-stack.yml) lists ./localnuget as released to the GitHub feed;
/// `nuget` (publish-stack.yml) lists ./Packages as released to nuget.org.
let target =
    if fsi.CommandLineArgs.Length > 1 then fsi.CommandLineArgs.[1].ToLowerInvariant() else "github"

let contentHeader, defaultFolder, packageLink =
    match target with
    | "github" ->
        "## Released to GitHub:\n", "./localnuget",
        fun (name: string) (_: string) -> sprintf "https://github.com/dotnet-websharper/core/pkgs/nuget/%s" name
    | "nuget" ->
        "## Released to NuGet:\n", "./Packages",
        fun (name: string) (version: string) -> sprintf "https://www.nuget.org/packages/%s/%s" name version
    | t -> failwithf "Unknown notification target '%s' (expected github or nuget)" t

let folder =
    if fsi.CommandLineArgs.Length > 2 then fsi.CommandLineArgs.[2] else defaultFolder

let packages =
    System.IO.Directory.EnumerateFiles(folder, "*.nupkg")
    |> Seq.sort
    |> Seq.map (fun f ->
        let filename = System.IO.FileInfo(f).Name
        match parseNuGetPackageFileName filename with
        | None ->
            failwithf "Failed to parse package version for %s" filename
        | Some (packageName, version) ->
            sprintf "- %s %s [[ link ]](<%s>)" packageName version (packageLink packageName version)
    )
    // Messages needs to be chunked by 2000 characters, as that's the discord limit
    |> Seq.fold (fun packageChunk packageString ->
        match packageChunk with
        | [] -> [packageString + "\n"]
        | x::xs ->
            if (x + packageString + "\n").Length < 2000 - contentHeader.Length then
                [x + packageString + "\n"] @ xs
            else
                [
                    packageString + "\n"
                    x
                    yield! xs
                ]
    ) []

let client = new HttpClient()

let message (header: string) (packages: string): Message =
    {
        content = header + packages
        username = "IntelliFactory CI"
        avatar_url = "https://raw.githubusercontent.com/dotnet-websharper/core/refs/heads/master/tools/WebSharper.png"
        tts = false
        embeds = [||]
    }

async {
    // Reorder the packages backwards to be true to the alphabetical ordering
    // Only the first message carries the header; the rest continue the list
    for i, pString in (packages |> List.rev |> List.indexed) do
        let header = if i = 0 then contentHeader else ""
        let serializedMessage = JsonSerializer.Serialize (message header pString)
        printfn "%s" serializedMessage
        use content = new StringContent(serializedMessage, Encoding.UTF8, "application/json")
        let! response = client.PostAsync(hookurl, content) |> Async.AwaitTask
        if response.IsSuccessStatusCode then
            ()
        else
            let! res = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            printfn "%A" res
    return ()
} |> Async.RunSynchronously
