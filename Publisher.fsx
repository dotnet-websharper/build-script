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
open NuGet.Packaging
open NuGet.Packaging.Core

let webSharperCDNLogin = Environment.GetEnvironmentVariable("CDN_WEBSHARPER_COM_LOGIN")

let repository = 
    let source = PackageSource("https://api.nuget.org/v3/index.json")
    Repository.Factory.GetCoreV3(source)

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

let findPackageById = 
    repository.GetResourceAsync<FindPackageByIdResource>()
    |> Task.RunSynchronously

let getResources (fileStream: Stream) =
    let asm = Mono.Cecil.AssemblyDefinition.ReadAssembly fileStream
    let asmName = asm.Name.Name
    let asmVersion =
        asm.CustomAttributes
        |> Seq.tryPick (fun attr ->
            if attr.AttributeType.FullName = typeof<Reflection.AssemblyFileVersionAttribute>.FullName then
                Some (attr.ConstructorArguments.[0].Value :?> string)
            else None)
    match asmVersion with
    | None -> None // old assembly without AssemblyFileVersion, don't CDN it.
    | Some asmVersion ->
        let resources =
            asm.CustomAttributes
            |> Seq.choose (fun attr ->
                if IsWebResourceAttribute attr.AttributeType.FullName then
                    Some (attr.ConstructorArguments.[0].Value :?> string)
                else None
            )
            |> Seq.choose (fun resName ->
                let x =
                    asm.MainModule.Resources
                    |> Seq.tryPick (fun r -> 
                        if r.Name = resName || r.Name = asmName + "." + resName then 
                            Some (r :?> Mono.Cecil.EmbeddedResource, resName) 
                        else None
                    )
                if x.IsNone then eprintfn "Warning: WebResource without EmbeddedResource: %s" resName
                x
            )
            |> Seq.append (
                asm.MainModule.Resources
                |> Seq.choose (fun r ->
                    if r.Name = "WebSharper.js" then
                        Some (r :?> Mono.Cecil.EmbeddedResource, asmName + ".js")
                    elif r.Name = "WebSharper.min.js" then
                        Some (r :?> Mono.Cecil.EmbeddedResource, asmName + ".min.js")
                    else None
                )
            )
        Some (resources |> Array.ofSeq, asmName, asmVersion)

let downloadPackage (package: IPackageSearchMetadata) (version: VersionInfo) =
    let packageStream = new MemoryStream()
    
    printfn "Downloading package %s version %O" package.Identity.Id version.Version

    let ok =
        findPackageById.CopyNupkgToStreamAsync(package.Identity.Id, version.Version, packageStream, new SourceCacheContext(), NullLogger.Instance, CancellationToken.None)
        |> Task.RunSynchronously

    if ok then
        let packageReader = new PackageArchiveReader(packageStream)

        packageReader.GetFiles()
        |> Seq.filter (fun p -> 
            p.StartsWith("lib/") && p.EndsWith(".dll")
        )
        |> Seq.choose (fun p -> 
            let fileStream = new MemoryStream()
            packageReader.GetStream(p).CopyTo(fileStream)
            fileStream.Seek(0, SeekOrigin.Begin) |> ignore
            fileStream |> getResources
        )
        |> Array.ofSeq

    else
        eprintfn "NuGet download failed."
        [||]

let cdnBase = "http://cdn.websharper.com"
let credentials =
    "Basic " + (webSharperCDNLogin |> Encoding.ASCII.GetBytes |> Convert.ToBase64String)

let tryUploadToCdn asmName asmVersion (resources: (Mono.Cecil.EmbeddedResource * string)[]) =
    let isUploaded =
        use client = new System.Net.Http.HttpClient()
        let resp =
            client.GetAsync(cdnBase + "/exists/" + asmName + "/" + asmVersion) 
            |> Task.RunSynchronously
        let respContent =
            resp.Content.ReadAsStringAsync()
            |> Task.RunSynchronously
        respContent = "OK"

    if isUploaded then
        printfn "Already on the CDN: %s %s" asmName asmVersion
        true
    else
        printfn "Posting on the CDN: %s %s" asmName asmVersion
        use client = new System.Net.Http.HttpClient()
        use formData = new MultipartFormDataContent()
        for resource, name in resources do
            printfn "Uploading to: %s/%s/%s/%s" cdnBase asmName asmVersion name
            let content = new StreamContent(resource.GetResourceStream())
            formData.Add(content, "files", name)
        use msg =
            new HttpRequestMessage(
                RequestUri = Uri(cdnBase + "/post/" + asmName + "/" + asmVersion),
                Content = formData,
                Method = HttpMethod.Post)
        msg.Headers.Add("Authorization", credentials)
        let resp =
            client.SendAsync(msg) 
            |> Task.RunSynchronously
        if resp.IsSuccessStatusCode then
            printfn "OK"
        else
            let err =
                resp.Content.ReadAsStringAsync()
                |> Task.RunSynchronously
            eprintfn "KO\n  %s" err
        false

for package in webSharperPackages do
    let versions = package |> getVersions

    let mutable i = 0
    while i < versions.Length do
        let packageRes = downloadPackage package versions[i]

        let uploadRes =
            packageRes |> Array.map (fun (resources, asmName, asmVersion) ->
                tryUploadToCdn asmName asmVersion resources   
            )
            |> Array.forall id

        if uploadRes then
            // everything already on CDN, skip lower versions
            i <- versions.Length
        else
            i <- i + 1
