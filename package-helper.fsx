open System
open System.IO
open System.Text.RegularExpressions

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

System.IO.Directory.EnumerateFiles("./localnuget")
|> Seq.map (fun f ->
    let filename = System.IO.FileInfo(f).Name
    match parseNuGetPackageFileName filename with
    | None ->
        failwithf "Failed to parse package version for %s" filename
    | Some (packageName, version) ->
        sprintf "| %s | %s |" packageName version
)
|> String.concat "\n"
|> printfn "%s"