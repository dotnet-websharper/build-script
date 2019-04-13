open System
open System.IO
open System.Text

let thisDir = __SOURCE_DIRECTORY__
let ( ++ ) a b = Path.Combine(a, b)
let rootDir = thisDir ++ "../../../../.."

/// Replaces the copyright notice in a given file.
let replaceCopyright file (newCopyright: string []) =
    let lines =
        File.ReadAllText(file).Split '\n'
    if lines.Length > 2 then
        let lines =
            if String.IsNullOrWhiteSpace lines.[lines.Length - 1] then
                lines.[..lines.Length - 2]
            else lines
        let endsWithR (l: string) = l.Length > 0 && l.[l.Length-1] = '\r'
        if endsWithR lines.[lines.Length - 2] && not (endsWithR lines.[lines.Length - 1]) then
            lines.[lines.Length - 1] <- lines.[lines.Length - 1] + "\r"
        let endsWith (s: string) (l: string) =
            l.EndsWith(s) || l.EndsWith(s + "\r")
        let mutable hasChanges = false
        let text =
            use output = new StringWriter()
            output.NewLine <- "\n"
            let mutable skip = false
            for line in lines do
                if line |> endsWith "$begin{copyright}" then
                    skip <- true
                    hasChanges <- true
                    for c in newCopyright do
                        output.WriteLine(c)
                if not skip then
                    output.WriteLine line
                if line |> endsWith "$end{copyright}" then
                    skip <- false
            output.ToString()
        if hasChanges then
            File.WriteAllText(file, text)

/// Adds the copyright notice in a given file.
let addCopyright file (newCopyright: string []) =
    let oldText = File.ReadAllText(file)
    use f = File.OpenWrite(file)
    use w = new StreamWriter(f, NewLine = "\n")
    for l in newCopyright do
        w.WriteLine(l)
    w.Write(oldText)

let readLicenseFile license =
    File.ReadAllLines(license)
    |> Array.map (fun line -> line.TrimEnd())

/// Updates copyright notices in all F# files in a given folder.
let updateLicense license =
    let copyright = readLicenseFile license
    let findFiles pattern =
        Directory.GetFiles(rootDir, pattern, SearchOption.AllDirectories)
    Array.concat [|
        findFiles "*.fs"
        findFiles "*.fsi"
        findFiles "*.fsx"
        findFiles "*.cs"
        findFiles "*.js"
    |]
    |> Array.iter (fun f ->
        stdout.WriteLine("Replacing: {0}", f)
        replaceCopyright f copyright
    )

let excludedFilenames = [|
    "build.fsx"
    "runtests.fsx"
    "WebSharper.Fake.fsx"
    "UpdateLicense.fsx"
    "UpdateVersion.fsx"
    "FsEye.fsx"
|]

let findSourceFilesWithoutCopyright () =
    let findFiles pattern =
        Directory.GetFiles(rootDir, pattern, SearchOption.AllDirectories)
    let hasCopyright f =
        File.ReadAllLines(f)
        |> Array.exists (fun l ->
            l.Contains "$begin{copyright}" || l.Contains "$nocopyright")
    Array.concat [|
        findFiles "*.fs"
        findFiles "*.fsi"
        findFiles "*.fsx"
        findFiles "*.cs"
    |]
    |> Array.filter (fun f ->
        let fname = Path.GetFileName f
        not (Array.contains fname excludedFilenames)
        && not (fname.EndsWith ".g.cs")
        && not (fname.EndsWith ".g.fs")
        && not (hasCopyright f))

/// Update all copyright notices in all F# files in WebSharper.
let updateAllLicenses () =
    let ( ++ ) a b = Path.Combine(a, b)
    updateLicense (thisDir ++ "LICENSE.txt")

/// Add copyright to files that don't, asking confirmation for each.
let interactiveAddMissingCopyright () =
    let license = readLicenseFile (thisDir ++ "LICENSE.txt")
    for f in findSourceFilesWithoutCopyright () do
        printf "Add copyright to '%s'? (y/n) " f
        stdout.Flush()
        match stdin.ReadLine().Trim().ToLowerInvariant() with
        | "y" | "yes" -> addCopyright f license
        | _ -> ()
