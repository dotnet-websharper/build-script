open System.IO

File.ReadAllLines "paket.dependencies"
|> Array.map (fun s -> if s.Contains("WebSharper") then s.Replace("prerelease", "") else s)
|> fun ls -> File.WriteAllLines("paket.dependencies", ls)