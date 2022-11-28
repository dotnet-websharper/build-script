open System.IO

File.ReadAllLines "paket.dependencies"
|> Array.filter (fun s -> s.TrimEnd() <> "nuget FSharp.Core")
|> fun ls -> File.WriteAllLines("paket.dependencies", ls)