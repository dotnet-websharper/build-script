/// Helpers for FAKE scripts for the standard WebSharper extensions.
/// Check the sources for usage.
(**
Uses the following environment variables (some of which are overridden by WSTargets options):
  * BUILD_NUMBER: the build number to use (last component of the version).
    Automatically set by Jenkins and used by FAKE.
  * BuildBranch: the name of the branch to switch to before building, and to push to on success.
    Default: current branch
  * PushRemote: the name of the git or hg remote to push to
    Default: origin
  * NuGetPublishUrl: the URL of the NuGet server
    Default: https://nuget.intellifactory.com/nuget
  * NUGET_KEY: the API key of the NuGet server.
    Automatically used by Paket.

Versioning policy (as implemented in ComputeVersion):
  * Major, minor and pre are taken from baseVersion.
  * Patch is set based on the latest tag:
    * If the tag's major/minor is different from baseVersion, then patch = 0.
    * If the major/minor is the same and the latest tag is on HEAD, then this is a rebuild
      of the same code: use the tag's patch number (they will be differenciated by ther Build).
    * Otherwise, increment from the tag's patch number.
  * Build is set to $(BUILD_NUMBER) if that is defined, baseVersion.Build + 1 otherwise.
*)

module WebSharper.Fake

#nowarn "20"  // Ignore string result of ==>

#if INTERACTIVE
#r "nuget: FAKE.Core"
#r "nuget: Fake.Core.Target"
#r "nuget: Fake.IO.FileSystem"
#r "nuget: Fake.Tools.Git"
#r "nuget: Fake.DotNet.Cli"
#r "nuget: Fake.DotNet.AssemblyInfoFile"
#r "nuget: Fake.DotNet.Paket"
#r "nuget: Paket.Core"
#else
#r "paket:
nuget FAKE.Core
nuget Fake.Core.Target
nuget Fake.IO.FileSystem
nuget Fake.Tools.Git
nuget Fake.DotNet.Cli
nuget Fake.DotNet.AssemblyInfoFile
nuget Fake.DotNet.Paket
nuget Paket.Core //"
#endif

#load "UpdateLicense.fsx"

open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions
open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Tools

let usage = """
usage: build [FAKE options] [--] [options]

options:
 -v, --verbose      Verbose build output
 -d, --debug        Build in Debug configuration
"""

let opts =
    let parsed = ref None
    fun (o: TargetParameter) ->
        match !parsed with
        | Some p -> p
        | None ->
            try
                let value = Docopt(usage).Parse(Array.ofSeq o.Context.Arguments)
                parsed := Some value
                value
            with DocoptException _ ->
                Trace.traceError usage
                reraise()

let verbose = opts >> DocoptResult.hasFlag "-v"
   
let isDebug = opts >> DocoptResult.hasFlag "-d"

let private mainGroupName = Paket.Domain.GroupName "Main"

let GetSemVerOf pkgName =
    let lockFile = Paket.LockFile.LoadFrom "./paket.lock"
    lockFile
        .GetGroup(mainGroupName)
        .GetPackage(Paket.Domain.PackageName pkgName)
        .Version
    |> Some

let shell program cmd =
    Printf.kprintf (fun cmd ->
        Shell.Exec(program, cmd, ".")
        |> function
            | 0 -> ()
            | n -> failwithf "%s %s failed with code %i" program cmd n
    ) cmd

let shellOut program cmd =
    Printf.kprintf (fun cmd ->
        let psi =
            System.Diagnostics.ProcessStartInfo(
                FileName = program,
                Arguments = cmd,
                RedirectStandardOutput = true,
                UseShellExecute = false
            )
        let proc = System.Diagnostics.Process.Start(psi)
        let out = proc.StandardOutput.ReadToEnd()
        proc.WaitForExit()
        match proc.ExitCode with
        | 0 -> out
        | n -> failwithf "%s %s failed with code %i" program cmd n
    ) cmd

let git cmd =
    Printf.kprintf (fun s ->
        Trace.logfn "> git %s" s
        Git.CommandHelper.directRunGitCommandAndFail "." s
    ) cmd

let private splitLines (s: string) =
    s.Split([| "\r\n"; "\n" |], StringSplitOptions.None)

let gitOut cmd =
    Printf.kprintf (fun s ->
        Trace.logfn "> git %s" s
        Git.CommandHelper.getGitResult "." s 
    ) cmd

let gitSilentNoFail cmd =
    Printf.kprintf (fun s ->
        Git.CommandHelper.directRunGitCommand "." s
    ) cmd

/// Generate a file at the given location, but leave it unchanged
/// if the generated contents are identical to the existing file.
/// `generate` receives the actual filename it should write to,
/// which may be a temp file.
let unchangedIfIdentical filename generate =
    if File.Exists(filename) then
        let tempFilename = Path.GetTempFileName()
        generate tempFilename
        if not (Shell.compareFiles true filename tempFilename) then
            File.Delete(filename)
            File.Move(tempFilename, filename)
    else
        generate filename

module Git =
    let getCurrentBranch() =
        match Git.Information.getBranchName "." with
        | "NoBranch" ->
            // Jenkins runs git in "detached head" and sets this env instead
            Environment.environVar "GIT_BRANCH"
        | s -> s

module VC =
    let getTags() =
        let _ok, out, _err = Git.CommandHelper.runGitCommand "." "tag --merged"
        Array.ofList out

    let getCurrentCommitId() =
        Git.Information.getCurrentSHA1 "."

    let commitIdForTag tag =
        Git.Branches.getSHA1 "." ("refs/tags/" + tag)

let ComputeVersion (baseVersion: option<Paket.SemVerInfo>) =
    let lastVersion, tag =
        try
            let tags = VC.getTags()
            let re = Regex("""^(?:[a-zA-Z]+[-.]?)?([0-9]+(?:\.[0-9]+){0,3})(?:-.*)?$""")
            match tags |> Array.choose (fun tag ->
                try
                    let m = re.Match(tag)
                    if m.Success then
                        let v = m.Groups.[1].Value |> Paket.SemVer.Parse
                        Some (v, Some tag)
                    else None
                with _ -> None) with
            | [||] ->
                Trace.traceImportant "Warning: no latest tag found"
                Paket.SemVer.Zero, None
            | a ->
                let v, t = Array.maxBy fst a
                v, t
        with e ->
            Trace.traceImportant (sprintf "Warning: getting version from latest tag: %s" e.Message)
            Paket.SemVer.Zero, None
    let baseVersion = defaultArg baseVersion lastVersion
    let patch =
        try
            if lastVersion.Major <> baseVersion.Major || lastVersion.Minor <> baseVersion.Minor then
                0u
            else
                let head = VC.getCurrentCommitId()
                let tagged =
                    match tag with
                    | Some tag -> VC.commitIdForTag tag
                    | None -> head
                if head = tagged then
                    lastVersion.Patch
                else
                    lastVersion.Patch + 1u
        with e ->
            Trace.traceImportant (sprintf "Warning: computing patch version: %s" e.Message)
            0u
    let build =
        match Environment.environVarOrNone "BUILD_NUMBER" with
        | None -> string (int64 lastVersion.Build + 1L)
        | Some b -> b
    Printf.ksprintf (fun v ->
        Trace.tracefn "==== Building project v%s ===="  v
        Paket.SemVer.Parse v
    ) "%i.%i.%i.%s%s" baseVersion.Major baseVersion.Minor patch build
        (match baseVersion.PreRelease with Some r -> "-" + r.Origin | None -> "")

let LazyVersionFrom packageName =
    fun () -> GetSemVerOf packageName |> ComputeVersion

type BuildMode =
    | Debug
    | Release

    override this.ToString() =
        match this with
        | Debug -> "Debug"
        | Release -> "Release"

    member this.AsDotNet =
        match this with
        | Debug -> DotNet.BuildConfiguration.Debug
        | Release -> DotNet.BuildConfiguration.Release

[<NoComparison; NoEquality>]
type BuildAction =
    | Projects of seq<string>
    | Custom of (BuildMode -> unit)
    | Multiple of seq<BuildAction>

    static member Solution s =
        BuildAction.Projects (!!s)

[<NoComparison; NoEquality>]
type Args =
    {
        GetVersion : unit -> Paket.SemVerInfo
        BuildAction : BuildAction
        Attributes : seq<AssemblyInfo.Attribute>
        WorkBranch : option<string>
        PushRemote : string
        HasDefaultBuild : bool
    }

type WSTargets =
    {
        BuildDebug : string
        Publish : string
    }

    member this.AddPrebuild s =
        "WS-GenAssemblyInfo" ==> s 
        s ==> "WS-BuildDebug"
        s ==> "WS-BuildRelease"
        ()

let msbuildVerbosity = 
    verbose >> function
    | true -> MSBuildVerbosity.Detailed
    | false -> MSBuildVerbosity.Minimal

let buildModeFromFlag =
    isDebug >> function
    | true -> BuildMode.Debug
    | false -> BuildMode.Release

let build o (mode: BuildMode) action =
    let rec buildRec action =
        match action with
        | BuildAction.Projects files ->
            let build = DotNet.build <| fun p ->
                { p with
                    Configuration = mode.AsDotNet
                    MSBuildParams = 
                        match Environment.environVarOrNone "OS" with
                        | Some "Windows_NT" ->
                            { p.MSBuildParams with
                                Verbosity = Some (msbuildVerbosity o)
                                Properties = ["Configuration", string mode]
                                DisableInternalBinLog = true // workaround for https://github.com/fsharp/FAKE/issues/2515
                            }
                        | _ ->
                            p.MSBuildParams
                }
            Seq.iter build files
        | Custom f -> f mode
        | Multiple actions -> Seq.iter buildRec actions
    buildRec action

let MakeTargets (args: Args) =

    Target.create "WS-Stop" <| fun _ ->
        try
            Process.GetProcessesByName("wsfscservice")
            |> Array.iter (fun x -> x.Kill())
            |> ignore
        with
        | _ -> ()
    
    let dirtyDirs =
        !! "**/bin/Debug"
        ++ "**/bin/Release"
        ++ "**/obj/Debug"
        ++ "**/obj/Release"
        ++ "**/Scripts/WebSharper"
        ++ "**/Content/WebSharper"
        ++ "build"

    Target.create "WS-Clean" <| fun _ ->
        Seq.iter Directory.delete dirtyDirs

    Target.create "WS-Update" <| fun _ ->
        let depsFile = Paket.DependenciesFile.ReadFromFile "./paket.dependencies"
        let mainGroup = depsFile.GetGroup mainGroupName
        let needsUpdate =
            mainGroup.Packages
            |> Seq.exists (fun { Name = pkg } ->
                pkg.Name.Contains "WebSharper")
        if needsUpdate then
            let res =
                DotNet.exec id "paket"
                    (sprintf "update -g %s" mainGroup.Name.Name)
            if not res.OK then failwith "dotnet paket update failed"
        for g, _ in depsFile.Groups |> Map.toSeq do
            if g.Name.ToLower().StartsWith("test") then
                let res =
                    DotNet.exec id "paket"
                        (sprintf "update -g %s" g.Name)
                if not res.OK then failwith "dotnet paket update failed"

    Target.create "WS-Restore" <| fun o ->
        DotNet.exec id "paket" "restore"
        if not (Environment.environVarAsBoolOrDefault "NOT_DOTNET" false) then
            let slns = (Environment.environVarOrDefault "DOTNETSOLUTION" "").Trim('"').Split(';')
            let restore proj =
                DotNet.restore (fun p -> 
                    { p with 
                        DisableParallel = true
                        MSBuildParams = 
                            { p.MSBuildParams with
                                Verbosity = Some (msbuildVerbosity o)
                                DisableInternalBinLog = true // workaround for https://github.com/fsharp/FAKE/issues/2515
                            }
                    }
                ) proj
            if slns |> Array.isEmpty then
                restore ""
            else
                for sln in slns do
                    restore sln

    /// DO NOT force this lazy value in or before WS-Update.
    let version =
        lazy
        let version = args.GetVersion()
        let addVersionSuffix = Environment.environVarAsBoolOrDefault "AddVersionSuffix" false
        let version =
            if addVersionSuffix then
                match args.WorkBranch, version.PreRelease with
                | None, _ -> version
                | Some b, Some p when b = p.Origin -> version
                | Some b, _ -> Paket.SemVer.Parse (version.AsString + "-" + b)
            else version
        printfn "Computed version: %s" version.AsString
        version

    Target.create "WS-GenAssemblyInfo" <| fun _ ->
        unchangedIfIdentical ("build" </> "AssemblyInfo.fs") <| fun file ->
            AssemblyInfoFile.createFSharp file [
                yield AssemblyInfo.Version (sprintf "%i.%i.0.0" version.Value.Major version.Value.Minor)
                yield AssemblyInfo.FileVersion (sprintf "%i.%i.%i.%A" version.Value.Major version.Value.Minor version.Value.Patch version.Value.Build)
                yield! args.Attributes
            ]

    Target.create "WS-BuildDebug" <| fun o ->
        build o BuildMode.Debug args.BuildAction

    Target.create "WS-BuildRelease" <| fun o ->
        build o BuildMode.Release args.BuildAction

    if args.HasDefaultBuild then
        Target.create "Build" ignore

    Target.create "WS-Package" <| fun _ ->      
        let outputPath = Environment.environVarOrNone "WSPackageFolder" |> Option.defaultValue "build"
        Paket.pack <| fun p ->
            { p with
                ToolType = ToolType.CreateLocalTool()
                OutputPath = outputPath
                Version = version.Value.AsString
            }
        let versionsFilePath = Environment.environVarOrNone "WSVersionsFile" |> Option.defaultValue "build/versions.txt"
        let repoName = Directory.GetCurrentDirectory() |> Path.GetFileName
        if not (File.exists versionsFilePath) then
            File.writeNew versionsFilePath [ repoName + " " + version.Value.AsString ]
        else
            File.write true versionsFilePath [ repoName + " " + version.Value.AsString ]

    Target.create "WS-Checkout" <| fun _ ->
        match args.WorkBranch with
        | None -> ()
        | Some branch ->
            try git "checkout -f %s" branch
            with e ->
                try git "checkout -f -b %s" branch
                with _ -> raise e

    Target.create "CI-Release" ignore

    Target.create "UpdateLicense" <| fun _ ->
        UpdateLicense.updateAllLicenses ()

    Target.create "AddMissingLicense" <| fun _ ->
        UpdateLicense.interactiveAddMissingCopyright ()

    "WS-Clean"
        ==> "WS-Checkout"

    "WS-Clean"
        ==> "WS-Update"
        ?=> "WS-Restore"

    "WS-Restore"
        ==> "WS-BuildRelease"
        ==> "WS-Package"
        ==> "CI-Release"

    "WS-Clean"
        ?=> "WS-GenAssemblyInfo"
        ==> "WS-BuildDebug"
        =?> ("Build", args.HasDefaultBuild)

    "WS-GenAssemblyInfo"
        ==> "WS-BuildRelease"

    "WS-Update"
        ==> "CI-Release"

    "WS-Stop"
        ?=> "WS-Clean"

    "WS-Stop"
        ==> "WS-Update"
        ==> "CI-Commit"

    {
        BuildDebug = "Build"
        Publish = "CI-Release"
    }

Target.create "CI-Commit" <| fun _ ->
    let versionsFilePath = Environment.environVarOrNone "WSVersionsFile" |> Option.defaultValue "build/versions.txt"
    let repoName = Directory.GetCurrentDirectory() |> Path.GetFileName
    if File.exists versionsFilePath then
        let versions = File.ReadAllLines versionsFilePath
        let version =
            versions |> Array.tryPick (fun l ->
                if l.StartsWith(repoName + " ") then
                    Some (l.[repoName.Length + 1 ..])
                else None
            )
        match version with
        | None ->
            failwith "version not found in versions.txt"
        | Some v ->
            git "add -A"
            git "commit -m \"Version %s\" --allow-empty" v
            git "push"
    else
        failwith "versions.txt not found"

Target.create "CI-Tag" <| fun _ ->
    let lastCICommitLog =
        gitOut "log --author=\"ci@intellifactory.com\" "
    // prints something like:
        //commit 39a5220f342488162bc5625fd2db3f9c13048626 (HEAD -> websharper50, origin/websharper50)
        //Author: IntelliFactory CI <ci@intellifactory.com>
        //Date:   Tue Aug 3 16:42:05 2021 +0000
        //
        //    Version 5.0.0.60-preview1

    Trace.log "git log returned:"
    lastCICommitLog |> List.iter Trace.log

    let commitSHA = 
        lastCICommitLog |> Seq.pick (fun l ->
            let l = l.Trim()
            if l.StartsWith("commit ") then
                Some (l.Split(' ').[1])
            else
                None
        )

    let tagName =
        lastCICommitLog |> Seq.pick (fun l ->
            let l = l.Trim()
            if l.StartsWith "Version " then
                Some (l.Split(' ').[1])
            else
                None
        )
     
    try
        git "tag %s %s" tagName commitSHA
        git "push origin %s" tagName
    with _ ->
        Trace.log "Tagging failed, ignored."

let RunTargets (targets: WSTargets) =
    Target.runOrDefaultWithArguments targets.BuildDebug

type WSTargets with

    static member Default getVersion =
        let buildBranch = Environment.environVarOrNone "BuildBranch"
        {
            GetVersion = getVersion
            BuildAction = BuildAction.Solution "*.sln"
            Attributes = Seq.empty
            WorkBranch = buildBranch
            PushRemote = Environment.environVarOrDefault "PushRemote" "origin"
            HasDefaultBuild = true
        }

    static member Default () =
        WSTargets.Default (fun () -> ComputeVersion None)
