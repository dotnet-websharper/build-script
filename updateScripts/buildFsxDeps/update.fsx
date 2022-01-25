open System.IO

let fileHeader =
    File.ReadAllLines (Path.Combine (__SOURCE_DIRECTORY__, "buildHeader.fsx"))

let buildFsx = 
    File.ReadAllLines "build.fsx"

let newBuildFsx =
    Array.append
        fileHeader
        (buildFsx |> Array.skipWhile (System.String.IsNullOrWhiteSpace >> not))

File.WriteAllLines("build.fsx", newBuildFsx)