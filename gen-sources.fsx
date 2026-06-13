open System
open System.IO
open System.Xml.Linq

let root =
    fsi.CommandLineArgs
    |> Array.skip 1
    |> Array.tryHead
    |> Option.defaultValue "."
    |> Path.GetFullPath

let generatedFile = "GeneratedPerf.fs"
let macroFile = "GeneratedMacro.fs"
let xname n = XName.Get n

/// Ensure the given files are <Compile>-included, in order, exactly once (idempotent).
let ensureCompileIncludes (projectDir: string) (files: string list) =
    let fsproj = Directory.GetFiles(projectDir, "*.fsproj") |> Array.exactlyOne
    let doc = XDocument.Load fsproj
    let project = doc.Root
    let included (name: string) =
        project.Descendants(xname "Compile")
        |> Seq.exists (fun e ->
            let a = e.Attribute(xname "Include")
            not (isNull a) && a.Value = name)
    let toAdd = files |> List.filter (included >> not)
    if not (List.isEmpty toAdd) then
        let itemGroup = XElement(xname "ItemGroup")
        for f in toAdd do
            itemGroup.Add(XElement(xname "Compile", XAttribute(xname "Include", f)))
        project.Add(itemGroup)
        doc.Save fsproj

// A WebSharper macro defined in, and used by, the same project. This forces the compiler to
// load the macro type from the project's own freshly built output dll via reflection — the
// WebSharper #1587 path that Idea 1 (parallel F# compilation) is about. Keeping it in the
// scenario ensures the parallel-compile experiment has a real own-output-reflection case to
// measure rather than an artificially reflection-free build.
let macroSource =
    """namespace PerfMacro

open WebSharper
open WebSharper.JavaScript
open WebSharper.Core
open WebSharper.Core.AST

[<Sealed>]
type PerfConstMacro() =
    inherit Macro()
    override this.TranslateCall(c) = MacroOk (!~ (Int 42))
"""

let writeProject projectName nsName withMacro extra =
    let projectDir = Path.Combine(root, projectName)
    Directory.CreateDirectory projectDir |> ignore
    if withMacro then
        File.WriteAllText(Path.Combine(projectDir, macroFile), macroSource)
        ensureCompileIncludes projectDir [ macroFile; generatedFile ]
    else
        ensureCompileIncludes projectDir [ generatedFile ]
    let functions =
        [ for i in 1 .. 120 ->
            sprintf "    let value%d seed = (seed + %d) * %d - seed / %d" i i (i % 13 + 2) (i % 7 + 1) ]
        |> String.concat Environment.NewLine
    let models =
        [ for i in 1 .. 32 ->
            sprintf "    type Model%d = { Id: int; Name: string; Score: int }" i ]
        |> String.concat Environment.NewLine
    let source =
        $"""namespace {nsName}

open WebSharper

[<JavaScript>]
module GeneratedPerf =
{models}

    [<Inline "$x + $y">]
    let inlineAdd (x: int) (y: int) = x + y

{functions}

    let all seed =
        [| {String.concat "; " [ for i in 1 .. 120 -> sprintf "value%d seed" i ]} |]
        |> Array.sum

{extra}
"""
    File.WriteAllText(Path.Combine(projectDir, generatedFile), source)

writeProject "Core.Domain" "Core.Domain" true """
    [<Macro(typeof<PerfMacro.PerfConstMacro>)>]
    let macroConst () = 0

    let domainSummary seed =
        inlineAdd (all seed) seed + macroConst ()
"""

writeProject "Core.Shared" "Core.Shared" false """
    let sharedSummary seed =
        Core.Domain.GeneratedPerf.domainSummary seed + all seed
"""

writeProject "Client.Components" "Client.Components" false """
    let renderNumber seed =
        Core.Shared.GeneratedPerf.sharedSummary seed + all seed
"""

writeProject "Client.Components2" "Client.Components2" false """
    let renderSibling seed =
        Core.Shared.GeneratedPerf.sharedSummary seed - all seed
"""

writeProject "Server.Api" "Server.Api" false """
    [<Rpc>]
    let serverValue seed =
        async { return Core.Shared.GeneratedPerf.sharedSummary seed + all seed }
"""
