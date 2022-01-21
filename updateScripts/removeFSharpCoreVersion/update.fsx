open System.IO

File.ReadAllLines "paket.dependencies"
|> Array.map (fun s -> s.Replace("nuget FSharp.Core 5.0.0", "nuget FSharp.Core"))
|> fun ls -> File.WriteAllLines("paket.dependencies", ls)