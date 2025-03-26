open System.IO

if fsi.CommandLineArgs.Length > 1 then

    let exclude = fsi.CommandLineArgs.[1].Split([|';'|])

    System.IO.Directory.GetFiles("./Packages")
    |> Array.iter (fun f ->
        if exclude |> Array.exists (Path.GetFileName(f).Contains) then
            File.Delete(f)
            printfn $"Removed package {f} before publishing"
    )
