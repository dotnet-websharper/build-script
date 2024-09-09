System.IO.Directory.EnumerateFiles("./localnuget")
|> Seq.map (fun f ->
    let file = System.IO.FileInfo(f)
    let filename = file.Name.Replace(".nupkg", "")
    let splitByDots = filename.Split([|'.'|])
    let len = splitByDots.Length
    let name = splitByDots[0..len-5] |> String.concat "."
    let version = splitByDots[len-4..] |> String.concat "."
    sprintf "| %s | %s |" name version
)
|> String.concat "\n"
|> printfn "%s"