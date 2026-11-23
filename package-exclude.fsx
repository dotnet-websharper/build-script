open System.IO

/// Packages that are never published to nuget.org, whatever the exclude input says: the private ones, which the
/// build workflow already keeps out of the Packages artifact (repos.yml `private: true`).
let alwaysExcluded = [ "WebSharper.FSharp.Booster.Pro."; "WebSharper.CSharp.Booster.Pro."; "WebSharper.Booster.Pro." ]

let exclude =
    let fromInput =
        if fsi.CommandLineArgs.Length > 1 then
            fsi.CommandLineArgs.[1].Split([|';'|], System.StringSplitOptions.RemoveEmptyEntries) |> List.ofArray
        else []
    alwaysExcluded @ fromInput

Directory.GetFiles("./Packages")
|> Array.iter (fun f ->
    if exclude |> List.exists (Path.GetFileName(f).Contains) then
        File.Delete(f)
        printfn $"Removed package {f} before publishing"
)
