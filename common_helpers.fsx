open System

module Package =
    open System.IO
    open System.Text.RegularExpressions

    // Extract out the version and package name from the nupkg name
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
            
