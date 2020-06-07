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
#load "../../../../../.fake/build.fsx/intellisense.fsx"
#if INTERACTIVE
#r "C:/.nuget/packages/netstandard.library/2.0.3/build/netstandard2.0/ref/netstandard.dll"
#endif
#load "UpdateLicense.fsx"
#nowarn "49"

open System
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

let gitSilentNoFail cmd =
    Printf.kprintf (fun s ->
        Git.CommandHelper.directRunGitCommand "." s
    ) cmd

let hg cmd = shell "hg" cmd
let hg' cmd = shellOut "hg" cmd

let attempt n f =
    let rec tryRun attempt =
        try
            f()
        with _ ->
            if attempt < n then
                printfn "attempt %i failed, retrying" attempt
                tryRun (attempt + 1)
            else
                reraise()
    tryRun 1

let private splitLines (s: string) =
    s.Split([| "\r\n"; "\n" |], StringSplitOptions.None)

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

module Hg =
    let getBookmarks() =
        hg' """bookmark -T "{bookmark} {node|short}\n" """
        |> splitLines
        |> Array.map (fun s -> s.Split(' ').[0])

    let getCurrentCommitId() =
        (hg' "id -i").Trim()

    let getCurrentBranch() =
        (hg' "branch").Trim()

    let commitIdForBookmark (b: string) =
        hg' """bookmark -T "{bookmark} {node|short}\n" """
        |> splitLines
        |> Array.pick (fun s ->
            let a = s.Split(' ')
            if a.[0] = b then Some a.[1] else None
        )

    let branchExists (b: string) =
        hg' """branches -T "{branch}\n" """
        |> splitLines
        |> Array.contains b

    let isAncestorOfCurrent (ref: string) =
        let o = hg' """log --rev "ancestors(.) and %s" """ ref
        not (String.IsNullOrWhiteSpace o)

module VC =
    let isGit = Directory.Exists ".git"

    let getTags() =
        if isGit then
            let _ok, out, _err = Git.CommandHelper.runGitCommand "." "tag --merged"
            Array.ofList out
        else
            Hg.getBookmarks()

    let getCurrentCommitId() =
        if isGit then
            Git.Information.getCurrentSHA1 "."
        else
            Hg.getCurrentCommitId()

    let commitIdForTag tag =
        if isGit then
            Git.Branches.getSHA1 "." ("refs/tags/" + tag)
        else Hg.commitIdForBookmark tag

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
    }

type WSTargets =
    {
        BuildDebug : string
        Publish : string
    }

    member this.AddPrebuild s =
        "WS-GenAssemblyInfo" ?=> s ==> "WS-BuildDebug"
        "WS-GenAssemblyInfo" ?=> s ==> "WS-BuildRelease"
        ()

let msbuildVerbosity = verbose >> function
    | true -> MSBuildVerbosity.Normal
    | false -> MSBuildVerbosity.Minimal

let MakeTargets (args: Args) =

    let dirtyDirs =
        !! "./**/bin"
        ++ "**/obj/Debug"
        ++ "**/obj/Release"
        ++ "build"

    Target.create "WS-Clean" <| fun _ ->
        Seq.iter Directory.delete dirtyDirs

    Target.create "WS-Update" <| fun _ ->
        let depsFile = Paket.DependenciesFile.ReadFromFile "./paket.dependencies"
        let mainGroup = depsFile.GetGroup mainGroupName
        let needsUpdate =
            mainGroup.Packages
            |> Seq.exists (fun { Name = pkg } ->
                pkg.Name.Contains "WebSharper" || pkg.Name.Contains "Zafir")
        if needsUpdate then
            attempt 3 <| fun () ->
                shell ".paket/paket.exe" "update -g %s %s"
                    mainGroup.Name.Name
                    (if Environment.environVarAsBoolOrDefault "PAKET_REDIRECTS" false
                     then "--redirects"
                     else "")

    Target.create "WS-Restore" <| fun _ ->
        if not (Environment.environVarAsBoolOrDefault "NOT_DOTNET" false) then
            let slns = (Environment.environVarOrDefault "DOTNETSOLUTION" "").Trim('"').Split(';')
            for sln in slns do
                attempt 3 <| fun () -> shell "dotnet" "restore %s --disable-parallel" sln

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

    let rec build o mode action =
        match action with
        | BuildAction.Projects files ->
            let build = DotNet.msbuild <| fun p ->
                p.WithMSBuildParams <| fun p ->
                    { p with
                        Verbosity = Some (msbuildVerbosity o)
                        Properties = ["Configuration", string mode]
                        DisableInternalBinLog = true // workaround for https://github.com/fsharp/FAKE/issues/2515
                    }
            Seq.iter build files
        | Custom f -> f mode
        | Multiple actions -> Seq.iter (build o mode) actions

    let build o mode =
        build o mode args.BuildAction

    Target.create "WS-BuildDebug" <| fun o ->
        build o BuildMode.Debug

    Target.create "WS-BuildRelease" <| fun o ->
        build o BuildMode.Release

    Target.create "Build" ignore

    Target.create "WS-Package" <| fun _ ->
        let re = Regex(@"^(\s*(\S+)\s*~>)\s*LOCKEDVERSION/([1-3])")
        let lock = Paket.LockFile.LoadFrom "paket.lock"
        let g = lock.Groups.[Paket.Domain.GroupName "main"]
        if Directory.Exists("nuget") then
            for f in Directory.EnumerateFiles("nuget", "*.paket.template.in") do
                let s =
                    File.ReadAllLines(f)
                    |> Array.map (fun l ->
                        re.Replace(l, fun m ->
                            let init = m.Groups.[1].Value
                            let pkg = m.Groups.[2].Value
                            let prefixLen = int m.Groups.[3].Value
                            let v = g.GetPackage(Paket.Domain.PackageName pkg).Resolved.Version
                            let pre =
                                match v.PreRelease with
                                | None -> ""
                                | Some x -> "-" + x.Name
                            match prefixLen with
                            | 1 -> sprintf "%s %i%s" init v.Major pre
                            | 2 -> sprintf "%s %i.%i%s" init v.Major v.Minor pre
                            | 3 -> sprintf "%s %i.%i.%i%s" init v.Major v.Minor v.Patch pre
                            | _ -> failwith "Impossible"))
                let outName = f.[..f.Length-4]
                printfn "Writing %s" outName
                File.WriteAllLines(outName, s)
        Paket.pack <| fun p ->
            { p with
                ToolPath =
                    if Environment.isWindows
                    then @".paket\paket.exe"
                    else @".paket/paket"
                OutputPath = "build"
                Version = version.Value.AsString
            }

    Target.create "WS-Checkout" <| fun _ ->
        match args.WorkBranch with
        | None -> ()
        | Some branch ->
            if VC.isGit then
                try git "checkout -f %s" branch
                with e ->
                    try git "checkout -f -b %s" branch
                    with _ -> raise e
            else
                if Hg.branchExists branch
                then hg "update -C %s" branch
                else hg "branch %s" branch

    Target.create "WS-Publish" <| fun _ ->
        match Environment.environVarOrNone "NugetPublishUrl" with
        | Some nugetPublishUrl ->
            Trace.tracefn "[NUGET] Publishing to %s" nugetPublishUrl
            Paket.push <| fun p ->
                { p with
                    ToolPath =
                        if Environment.isWindows
                        then @".paket\paket.exe"
                        else @".paket/paket"
                    PublishUrl = nugetPublishUrl
                    WorkingDir = "build"
                }
        | _ -> Trace.traceError "[NUGET] Not publishing: NugetPublishUrl not set"

    Target.create "CI-Release" ignore

    Target.create "UpdateLicense" <| fun _ ->
        UpdateLicense.updateAllLicenses ()

    Target.create "AddMissingLicense" <| fun _ ->
        UpdateLicense.interactiveAddMissingCopyright ()

    "WS-Clean"
        ==> "WS-Update"
        ==> "WS-Restore"
        =?> ("WS-GenAssemblyInfo", Environment.environVarAsBoolOrDefault "BuildBranch" false)

    "WS-GenAssemblyInfo"
        ==> "WS-BuildDebug"
        ==> "Build"
    "WS-GenAssemblyInfo"
        ==> "WS-BuildRelease"
        ==> "WS-Package"
        ==> "WS-Publish"
        ==> "CI-Release"

    "WS-Restore"
        ==> "WS-Publish"

    "WS-Restore"
        ?=> "WS-GenAssemblyInfo"

    "WS-Clean"
        ==> "WS-Checkout"

    {
        BuildDebug = "Build"
        Publish = "CI-Release"
    }

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
            PushRemote =
                Environment.environVarOrDefault "PushRemote"
                    (if VC.isGit then "origin" else "default")
        }

    static member Default () =
        WSTargets.Default (fun () -> ComputeVersion None)
