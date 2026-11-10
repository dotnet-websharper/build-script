#load "common_helpers.fsx"
open Common_helpers

System.IO.Directory.EnumerateFiles("./localnuget")
|> Seq.map (fun f ->
    let filename = System.IO.FileInfo(f).Name
    match Package.parseNuGetPackageFileName filename with
    | None ->
        failwithf "Failed to parse package version for %s" filename
    | Some (packageName, version) ->
        sprintf "| %s | %s |" packageName version
)
|> String.concat "\n"
|> printfn "%s"