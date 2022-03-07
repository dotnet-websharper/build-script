#r "System.Net.Http"
#r "System.Xml"
#r "System.Xml.Linq"
#r "nuget: Nuget.Packaging"
#r "nuget: Nuget.Protocol"
#r "nuget: Mono.Cecil"

open System
open System.Threading
open System.Threading.Tasks
open System.Collections.Generic
open System.IO
open System.Net.Http
open System.Text
open NuGet.Common
open NuGet.Configuration
open NuGet.Protocol
open NuGet.Protocol.Core.Types
open NuGet.Packaging.Core

let webSharperCDNLogin = Environment.GetEnvironmentVariable("CND_WEBSHARPER_COM_LOGIN")

let repository = 
    let source = PackageSource("https://api.nuget.org/v3/index.json")
    let providers = Repository.Provider.GetCoreV3()
    SourceRepository(source, providers)

module Task =
    let RunSynchronously (task: Task<'T>) = task.Result

let webSharperPackages =
    task {
        let! search = repository.GetResourceAsync<PackageSearchResource>()
        let! response = search.SearchAsync("WebSharper", SearchFilter(true), 0, 1000, NullLogger.Instance, CancellationToken.None)
        return 
            response
            |> Seq.filter (fun m -> m.Identity.Id.StartsWith("WebSharper"))
            |> Seq.toArray
    }
    |> Task.RunSynchronously

let downloader =
    repository.GetResourceAsync<DownloadResource>()
    |> Task.RunSynchronously

let downloadContext =
    PackageDownloadContext(new SourceCacheContext(), "./packages", true)

//webSharperPackages |> Array.map (fun m -> m.Identity.Id)

let IsWebResourceAttribute (fullName: string) =
    fullName = "WebSharper.WebResourceAttribute"

let getVersions (package: IPackageSearchMetadata) =
    task {
        let! versions = package.GetVersionsAsync()
        return
            versions
            |> Seq.filter (fun t -> t.Version.Major >= 5)
            |> Seq.sortByDescending (fun t -> t.Version)
            |> Array.ofSeq
    }
    |> Task.RunSynchronously

let downloadPackage (package: PackageIdentity) =

    downloader.GetDownloadResourceResultAsync(package, downloadContext, "", NullLogger.Instance, CancellationToken.None)
    |> Task.RunSynchronously

let package =
    webSharperPackages |> Array.head 
let version =
    package
    |> getVersions |> Array.head

let packageWithVersion =
    package.WithVersions(Seq.singleton version).Identity

let downloadRes = downloadPackage packageWithVersion



let packagePathResolver = PackagePathResolver(Path.GetFullPath("packages"));

downloadRes.

let tryPackageUploadToCDN = 



for package in webSharperPackages do
    task {
        let! versions = package.GetVersionsAsync()
        
    }



type Settings =
    {
        PackagesFolder : string
        PrivateRepository : string
        PublicRepository : string
        PackageOutputPath : string
        VsixFolder : string
    }

    static member Default =
        let npop =
            match Environment.GetEnvironmentVariable("NuGetPackageOutputPath") with
            | d when Directory.Exists(d) -> d
            | _ -> failwithf "Please set NuGetPackageOutputPath environment variable"
        {
            PackagesFolder = "packages"
            PrivateRepository =
                match Environment.GetEnvironmentVariable("NuGetPublishUrl") with
                | null -> npop
                | url -> url
            PublicRepository = "https://nuget.org/api/v2/"
            PackageOutputPath = npop
            VsixFolder =
                match Environment.GetEnvironmentVariable("VsixPublishFolder") with
                | null -> npop
                | url -> url
        }

type ProjectSet =
    private { ProjectIDs: list<string> }

    static member Create(ps) =
        let packageSourceProvider = PackageSourceProvider(Settings.LoadDefaultSettings(null, null, null))
        let emptyCredentialProvider = { new ICredentialProvider with member this.GetCredentials(_, _, _, _) = null }
        let credentialProvider = SettingsCredentialProvider(emptyCredentialProvider, packageSourceProvider)
        HttpClient.DefaultCredentialProvider <- credentialProvider
        { ProjectIDs = Seq.toList ps }

    member ps.PublishCdn(?settings, ?cdnBase) =
        let settings = defaultArg settings Settings.Default
        let cdnBase = defaultArg cdnBase "http://cdn.websharper.com"
        let privSource = Source.Private(settings)
        let credentials =
            "Basic " +
            (webSharperCDNLogin |> Encoding.ASCII.GetBytes |> Convert.ToBase64String)
        let packages = privSource.R.GetPackages()
            // query {
            //     for p in privSource.R.Search("WebSharper", true) do
            //     let v = p.Version.Version
            //     where (//p.Id.Contains "WebSharper" &&
            //            v.Major >= 3 &&
            //            v.Minor >= 6)
            //     select p
            // }
        let shouldPush (p: IPackage) =
            if p.Id.Contains "WebSharper"
                || p.Id = "IntelliFactory.Reactive"
                || p.Id = "IntelliFactory.OAuth"
                || p.Id = "IntelliFactory.AuthenticationServer"
                || p.Id = "IntelliFactory.AuthenticationServer.Api"
                || p.Id = "IntelliFactory.AppHarbor"
                || p.Id = "IntelliFactory.Markup"
            then
                p.Version.Version >= Version(3, 6) && p.Version.Version <= Version(4, 0)
            else
                (p.Id = "FPish.API" && p.Version.Version >= Version(1, 0, 139))
                || (p.Id = "IntelliFactory.Communications" && p.Version.Version >= Version(0, 10, 215))

        for p in packages do
            if shouldPush p then
                for f in p.GetLibFiles() do
                    let asm =
                        try Some (Mono.Cecil.AssemblyDefinition.ReadAssembly (f.GetStream()))
                        with _ -> None // it's not an assembly, pass.
                    match asm with
                    | None -> ()
                    | Some asm ->
                        let asmName = asm.Name.Name
                        let asmVersion =
                            asm.CustomAttributes
                            |> Seq.tryPick (fun attr ->
                                if attr.AttributeType.FullName = typeof<Reflection.AssemblyFileVersionAttribute>.FullName then
                                    Some (attr.ConstructorArguments.[0].Value :?> string)
                                else None)
                        match asmVersion with
                        | None -> () // old assembly without AssemblyFileVersion, don't CDN it.
                        | Some asmVersion ->
                            let resources =
                                asm.CustomAttributes
                                |> Seq.choose (fun attr ->
                                    if IsWebResourceAttribute attr.AttributeType.FullName then
                                        Some (attr.ConstructorArguments.[0].Value :?> string)
                                    else None)
                                |> Seq.choose (fun r ->
                                    let x =
                                        asm.MainModule.Resources
                                        |> Seq.tryPick (fun res -> if res.Name = r then Some (res, r) else None)
                                    if x.IsNone then eprintfn "Warning: WebResource without EmbeddedResource: %s" r
                                    x)
                                |> Seq.append (
                                    asm.MainModule.Resources
                                    |> Seq.choose (fun r ->
                                        if r.Name = "WebSharper.js" then
                                            Some (r, asmName + ".js")
                                        elif r.Name = "WebSharper.min.js" then
                                            Some (r, asmName + ".min.js")
                                        else None))
                            if Seq.isEmpty resources then () else
                            let isUploaded =
                                use client = new System.Net.Http.HttpClient()
                                let resp = client.GetAsync(cdnBase + "/exists/" + asmName + "/" + asmVersion) 
                                resp.Wait()
                                let resp = resp.Result.Content.ReadAsStringAsync()
                                resp.Wait()
                                resp.Result = "OK"

                            if isUploaded then
                                printfn "Already on the CDN: %s %s" asmName asmVersion
                            else
                                printf "Posting on the CDN: %s %s..." asmName asmVersion
                                stdout.Flush()
                                use client = new System.Net.Http.HttpClient()
                                use formData = new MultipartFormDataContent()
                                for resource, name in resources do
                                    let resource = resource :?> Mono.Cecil.EmbeddedResource
                                    let content = new StreamContent(resource.GetResourceStream())
                                    formData.Add(content, "files", name)
                                use msg =
                                    new HttpRequestMessage(
                                        RequestUri = Uri(cdnBase + "/post/" + asmName + "/" + asmVersion),
                                        Content = formData,
                                        Method = HttpMethod.Post)
                                msg.Headers.Add("Authorization", credentials)
                                let resp = client.SendAsync(msg) 
                                resp.Wait()
                                if resp.Result.IsSuccessStatusCode then
                                    printfn "OK"
                                else
                                    let err = resp.Result.Content.ReadAsStringAsync()
                                    err.Wait()
                                    printfn "KO\n  %s" err.Result
