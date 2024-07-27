let content =
    if System.IO.File.Exists "wsbuild.fsx" then
        System.IO.File.ReadAllText "wsbuild.fsx"
    else
        System.IO.File.ReadAllText "build.fsx"

let structuredlogger, stlReplace =
    "#r \"nuget: Paket.Core, 8.1.0-alpha004\"", "#r \"nuget: Paket.Core, 8.1.0-alpha004\"\n#r \"nuget: MSBuild.StructuredLogger\""

let pattern1 = """let execContext = Context.FakeExecutionContext.Create false "build.fsx" []
Context.setExecutionContext (Context.RuntimeContext.Fake execContext)"""

let newPattern ="""System.Environment.GetCommandLineArgs()
|> Array.skip 2 // skip fsi.exe; build.fsx
|> Array.toList
|> Fake.Core.Context.FakeExecutionContext.Create false __SOURCE_FILE__
|> Fake.Core.Context.RuntimeContext.Fake
|> Fake.Core.Context.setExecutionContext"""