open System.Net.Http
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

let hookurl = System.Environment.GetEnvironmentVariable "DISCORD_PACKAGE_FEED"
let contentHeader = "## Released to GitHub:\n"

type NuGetVersion = 
    {
        Major: int
        Minor: int
        Patch: int
        Revision: int
        Prerelease: string option
    }

    override this.ToString() =
        match this.Prerelease with
        | None -> $"{this.Major}.{this.Minor}.{this.Patch}.{this.Revision}"
        | Some prerelease -> $"{this.Major}.{this.Minor}.{this.Patch}.{this.Revision}-{prerelease}"

let parseNuGetVersion (input: string) =
    let pattern =
        @"^[vV]?(0|[1-9]\d*)(?:\.(0|[1-9]\d*))?(?:\.(0|[1-9]\d*))?(?:\.(0|[1-9]\d*))?(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$"

    let m = Regex.Match(input, pattern)

    let number (group: int) =
        if m.Groups.[group].Success then
            match System.Int32.TryParse(m.Groups.[group].Value) with
            | true, n -> Some n
            | _ -> None
        else None

    if not m.Success then None
    else
        match number 1, number 2, number 3, number 4 with
        | Some major, Some minor, Some patch, Some revision ->
            Some {
                Major = major
                Minor = minor
                Patch = patch
                Revision = revision
                Prerelease =
                    if m.Groups.[5].Success then
                        Some m.Groups.[5].Value
                    else None
            }
        | _ -> None

let packages =
    System.IO.Directory.EnumerateFiles("./localnuget")
    |> Seq.map (fun f ->
        let file = System.IO.FileInfo(f)
        let filename = file.Name.Replace(".nupkg", "")
        let splitByDots = filename.Split([|'.'|])
        let packageName = splitByDots[0]
        let version = splitByDots[1..] |> String.concat "." |> parseNuGetVersion
        match version with
        | None ->
            failwithf "Failed to parse package version for %s" packageName
        | Some version ->
            sprintf "- %s %s [[ link ]](<https://github.com/dotnet-websharper/core/pkgs/nuget/%s>)" packageName (version.ToString()) packageName
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

let message (packages: string): Message =
    {
        content = sprintf "%s%s" contentHeader packages
        username = "IntelliFactory CI"
        avatar_url = "https://raw.githubusercontent.com/dotnet-websharper/core/refs/heads/master/tools/WebSharper.png"
        tts = false
        embeds = [||]
    }

async {
    // Reorder the packages backwards to be true to the alphabetical ordering
    for pString in (packages |> List.rev) do
        let serializedMessage = JsonSerializer.Serialize (message pString)
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
