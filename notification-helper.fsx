open System.Net.Http
open System.Text
open System.Text.Json

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

let packages =
    System.IO.Directory.EnumerateFiles("./localnuget")
    |> Seq.map (fun f ->
        let file = System.IO.FileInfo(f)
        let filename = file.Name.Replace(".nupkg", "")
        let splitByDots = filename.Split([|'.'|])
        let len = splitByDots.Length
        let name = splitByDots[0..len-5] |> String.concat "."
        let version = splitByDots[len-4..] |> String.concat "."
        sprintf "- %s %s [[link]] https://github.com/dotnet-websharper/core/pkgs/nuget/%s" name version name
    )
    |> String.concat "\n"

let client = new HttpClient()

let message: Message =
    {
        content = sprintf "## Released to GitHub:\n%s" packages
        username = "IntelliFactory CI"
        avatar_url = "https://raw.githubusercontent.com/dotnet-websharper/core/refs/heads/master/tools/WebSharper.png"
        tts = false
        embeds = [||]
    }

async {
    let serializedMessage = JsonSerializer.Serialize message
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
