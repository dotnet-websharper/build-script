// $begin{copyright}
//
// This file is part of WebSharper
//
// Copyright (c) 2008-2018 IntelliFactory
//
// Licensed under the Apache License, Version 2.0 (the "License"); you
// may not use this file except in compliance with the License.  You may
// obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
// implied.  See the License for the specific language governing
// permissions and limitations under the License.
//
// $end{copyright}

namespace WebSharper.Core.DependencyGraph

open System.Collections.Generic
open System.Collections.Concurrent

open WebSharper.Core

open WebSharper.Core.AST
open WebSharper.Core.Metadata

module R = Resources 

/// The compilation-time mutable representation of a code dependency graph
type Graph =
    {
        Nodes : ResizeArray<Node>
        Edges : ResizeArray<HashSet<int>>
        /// Nested dictionary lookup order: TypeNode -> AbstractMethodNode -> ImplementationNode 
        Overrides : IDictionary<int, IDictionary<int, int>>
        Lookup : IDictionary<Node, int>
        Resources : ConcurrentDictionary<int, Resources.IResource>
//        NewNodes : ResizeArray<int>
    }

    /// Create a mutable graph from parsed immutable graph data
    static member FromData (data: GraphData) =
        let lookup = Dictionary()
        data.Nodes |> Seq.iteri (fun i n -> lookup.Add(n, i))
        let overrides = Dictionary()
        for typ, ms in data.Overrides do
            let o = Dictionary()
            for td, d in ms do o.Add(td, d)
            overrides.Add(typ, o :> IDictionary<_,_>)
        {
            Nodes = ResizeArray(data.Nodes)
            Edges = ResizeArray(data.Edges |> Array.map (fun e -> HashSet(e)))
            Overrides = overrides
            Lookup = lookup
            Resources = ConcurrentDictionary()
//            NewNodes = ResizeArray()
        }

    static member New() =            
        let nodes = ResizeArray()
        let edges = ResizeArray() 
        let lookup = Dictionary()
        
        {
            Nodes = nodes
            Edges = edges
            Overrides = Dictionary()
            Lookup = lookup
            Resources = ConcurrentDictionary()
        }

    /// Create a mutable graph from a sequence of parsed immutable graph data
    static member FromData (data: seq<GraphData>) =
        let graphs = Array.ofSeq data
            
        let nodes = ResizeArray()
        let lookup = Dictionary()
        let edges = ResizeArray() 
        let overrides = Dictionary()
        for i = 0 to graphs.Length - 1 do
            let g = graphs.[i]
            let r = Dictionary() // redirects
            for j = 0 to g.Nodes.Length - 1 do
                let jn = g.Nodes.[j]
                let jr =
                    match lookup.TryFind jn with 
                    | Some jr -> jr
                    | _ ->
                        let jr = nodes.Count
                        nodes.Add jn 
                        edges.Add(HashSet())
                        lookup.Add(jn, jr)
                        jr
                r.Add(j, jr)
            
            for j = 0 to g.Edges.Length - 1 do
                let ejr = edges.[r.[j]]
                for d in g.Edges.[j] do
                    ejr.Add r.[d] |> ignore
            
            for typ, ors in g.Overrides do
                let o = Dictionary() :> IDictionary<_,_>
                overrides.Add(r.[typ], o)
                for a, n in ors do
                    o.Add(r.[a], r.[n])                 
        {
            Nodes = nodes
            Edges = edges
            Overrides = overrides
            Lookup = lookup
            Resources = ConcurrentDictionary()
//            NewNodes = ResizeArray()
        }

    member this.GetData() =
        {
            GraphData.Nodes = this.Nodes.ToArray()
            Edges = this.Edges.ToArray() |> Array.map Array.ofSeq
            Overrides = 
                this.Overrides 
                |> Seq.map (fun (KeyValue (typ, ms)) -> 
                    typ,
                    ms |> Seq.map (fun (KeyValue(td, d)) -> td, d) |> Array.ofSeq
                ) 
                |> Array.ofSeq
        } 
    
    /// Get all code dependencies unordered
    member this.GetDependencies (nodes : seq<Node>) =
        let allNodes = HashSet()
        let newNodes = HashSet()
        let allTypesWithOverrides = HashSet()
        let newTypesWithOverrides = HashSet()
        let allAbstractMembers = HashSet()
        let newAbstractMembers = HashSet()

        let addTypeWithOverride i =
            if this.Overrides.ContainsKey i then newTypesWithOverrides.Add i |> ignore  
        
        let addNode n i =
            if allNodes.Add i then
                newNodes.Add i |> ignore
                match n with
                | AbstractMethodNode (td, m) -> this.Lookup.TryFind (AbstractMethodNode (td, m)) |> Option.iter (newAbstractMembers.Add >> ignore) 
                | TypeNode td -> this.Lookup.TryFind (TypeNode td) |> Option.iter addTypeWithOverride
                | _ -> ()
        
        nodes |> Seq.iter (fun n -> this.Lookup.TryFind n |> Option.iter (addNode n))

        while newNodes.Count > 0 do
            let currentNodes = Array.ofSeq newNodes
            newNodes.Clear()
            
            for n in currentNodes do
                this.Edges.[n] |> Seq.iter (fun i -> addNode this.Nodes.[i] i)
            
            if newTypesWithOverrides.Count > 0 || newAbstractMembers.Count > 0 then 
                let currentTypes = Set.ofSeq newTypesWithOverrides
                allTypesWithOverrides.UnionWith(newTypesWithOverrides)
                newTypesWithOverrides.Clear()
            
                let currentAbstractMembers = Array.ofSeq newAbstractMembers
                allAbstractMembers.UnionWith(newAbstractMembers)
                newAbstractMembers.Clear()
            
                for typ in allTypesWithOverrides do
                    let ors = this.Overrides.[typ] 
                    let currentOrAllAbstractMembers =
                        if currentTypes.Contains typ then 
                            allAbstractMembers :> _ seq
                        else currentAbstractMembers :> _ seq   
                    for mem in currentOrAllAbstractMembers do
                        ors.TryFind mem |> Option.iter (fun i -> addNode this.Nodes.[i] i)
        
        allNodes |> Seq.map (fun n -> this.Nodes.[n]) |> List.ofSeq

    /// Get all resource nodes used by a graph node.
    member private this.GetRequires (metadata: Info) (nodes : seq<Node>) =
        let allNodes = HashSet()
        let newNodes = HashSet()
        let allTypesWithOverrides = HashSet()
        let newTypesWithOverrides = HashSet()
        let allAbstractMembers = HashSet()
        let newAbstractMembers = HashSet()
        let resNodes = HashSet()

        let addTypeWithOverride i =
            if this.Overrides.ContainsKey i then newTypesWithOverrides.Add i |> ignore  
        
        let addNode n i =
            if allNodes.Add i then
                match n with
                | ResourceNode _
                | AssemblyNode _ ->
                    resNodes.Add i |> ignore
                | TypeNode typ ->
                    match metadata.Classes.TryGetValue(typ) with
                    | true, (addr, _, Some cls) when cls.BaseClass.IsSome ->
                        let rec isWebControl (def: TypeDefinition) =
                            def.Value.FullName = "WebSharper.Web.Control" || (
                                match metadata.Classes.TryGetValue(def) with
                                | true, (_, _, Some cls) when cls.BaseClass.IsSome ->
                                    isWebControl cls.BaseClass.Value.Entity
                                | _ ->
                                    false
                            )
                        if isWebControl cls.BaseClass.Value.Entity then
                            match addr.Module with
                            | JavaScriptModule m -> resNodes.Add i |> ignore
                            | _ -> ()
                    | _ -> ()
                | _ -> ()
                newNodes.Add i |> ignore
                match n with
                | AbstractMethodNode (td, m) -> this.Lookup.TryFind (AbstractMethodNode (td, m)) |> Option.iter (newAbstractMembers.Add >> ignore) 
                | TypeNode td -> this.Lookup.TryFind (TypeNode td) |> Option.iter addTypeWithOverride
                | _ -> ()

        nodes |> Seq.iter (fun n -> this.Lookup.TryFind n |> Option.iter (addNode n))

        while newNodes.Count > 0 do
            let currentNodes = Array.ofSeq newNodes
            newNodes.Clear()
            
            for n in currentNodes do
                this.Edges.[n] |> Seq.iter (fun i -> addNode this.Nodes.[i] i)
            
            if newTypesWithOverrides.Count > 0 || newAbstractMembers.Count > 0 then 
                let currentTypes = Set.ofSeq newTypesWithOverrides
                allTypesWithOverrides.UnionWith(newTypesWithOverrides)
                newTypesWithOverrides.Clear()
            
                let currentAbstractMembers = Array.ofSeq newAbstractMembers
                allAbstractMembers.UnionWith(newAbstractMembers)
                newAbstractMembers.Clear()
            
                for typ in allTypesWithOverrides do
                    let ors = this.Overrides.[typ] 
                    let currentOrAllAbstractMembers =
                        if currentTypes.Contains typ then 
                            allAbstractMembers :> _ seq
                        else currentAbstractMembers :> _ seq   
                    for mem in currentOrAllAbstractMembers do
                        ors.TryFind mem |> Option.iter (fun i -> addNode this.Nodes.[i] i)
        
        resNodes |> Array.ofSeq

    /// Gets all resource class instances for a set of graph nodes.
    /// Resource nodes are ordered by graph edges, assembly nodes by assembly dependencies.
    member this.GetResources (metadata: Info) (nodes : seq<Node>) =
        let activate i n =
            this.Resources.GetOrAdd(i, fun _ ->
                match n with
                | ResourceNode (t, p) ->
                    try
                        match p with
                        | None ->
                            let td = Reflection.LoadTypeDefinition t
                            match td.GetConstructor([||]) with
                            | null ->
                                R.EmptyResource
                            | _ ->
                                td
                                |> System.Activator.CreateInstance
                                |> unbox
                        | Some p ->
                            System.Activator.CreateInstance(
                                Reflection.LoadTypeDefinition t, 
                                args =
                                    match p with
                                    | ParameterObject.Array _ ->
                                        ParameterObject.ToObj p :?> obj[]
                                    | _ ->
                                        [| ParameterObject.ToObj p |]
                            ) |> unbox
                    with e ->
                        {
                            new R.IResource with
                                member this.Render ctx =
                                    fun writer ->
                                        let writer = writer R.Scripts
                                        writer.Write("<!-- ")
                                        writer.Write("Failed to load: {0}; because of: {1}", t, e.Message)
                                        writer.WriteLine(" -->")
                        }
                | _ -> failwith "not a resource node"
            )

        let resNodes =
            nodes 
            |> this.GetRequires metadata
            |> Seq.distinct
            |> Seq.choose (fun i ->
                let n = this.Nodes.[i]
                match n with
                | ResourceNode _ ->
                    Some (false, (i, n))
                | _ -> None
            )    
            |> List.ofSeq

        let resNodesOrdered = ResizeArray()
        let registeredResNodes = HashSet()

        let rec registerResNode i n =
            if registeredResNodes.Add i then
                for d in this.Edges.[i] do
                    registerResNode d this.Nodes.[d]    
                resNodesOrdered.Add (i, n)    

        for _, (i, n) in resNodes do registerResNode i n

        [
            yield Resources.Runtime.Instance
            for i, n in resNodesOrdered -> activate i n
        ]
   
     /// Gets the resource nodes of a set of already explored graph nodes.
     /// Used for minimal bundling.
     member this.GetResourcesOf (nodes : seq<Node>) =
        nodes |> Seq.filter (fun n ->
            match n with
            | ResourceNode _ ->
                true
            | _ -> false
        )
        |> Seq.distinct
        |> this.GetResources Info.Empty

    member this.AddOrLookupNode n =
        match this.Lookup.TryFind n with
        | Some i -> i
        | _ ->
            let i = this.Nodes.Count
            this.Nodes.Add n
            this.Edges.Add(HashSet())
            this.Lookup.Add(n, i)
            match n with
            | ResourceNode (r, Some _) ->
                let b = this.AddOrLookupNode (ResourceNode(r, None))
                this.Edges.[i].Add(b) |> ignore
            | _ -> ()   
            i  

    member this.AddOrLookupImplementation (typ, btyp, meth) =
        if btyp <> typ then
            ImplementationNode(typ, btyp, meth)
        else
            MethodNode(typ, meth)
        |> this.AddOrLookupNode

    member this.GetNodeDeps n =
        this.Edges.[this.AddOrLookupNode n]    

    member this.AddEdge (n, d) = this.Edges.[n].Add(d) |> ignore
    member this.AddEdge (n, d) = this.Edges.[n].Add(this.AddOrLookupNode d) |> ignore
    member this.AddEdge (n, d) = this.Edges.[this.AddOrLookupNode n].Add(d) |> ignore
    member this.AddEdge (n, d) = this.Edges.[this.AddOrLookupNode n].Add(this.AddOrLookupNode d) |> ignore
    
    member this.AddOverride (typ, btyp, meth) =
        let typeNode = this.AddOrLookupNode (TypeNode typ)
        let abstractMethodNode = this.AddOrLookupNode (AbstractMethodNode (btyp, meth))
        let methodNode = this.AddOrLookupNode (MethodNode (typ, meth))
        let ovr =
            match this.Overrides.TryFind typeNode with
            | None ->
                let o = new Dictionary<_,_>() :> IDictionary<_,_>
                this.Overrides.Add(typeNode, o)
                o
            | Some o -> o
        ovr.Add(abstractMethodNode, methodNode)

    member this.AddImplementation (typ, intf, meth) =
        let typeNode = this.AddOrLookupNode (TypeNode typ)
        let abstractMethodNode = this.AddOrLookupNode (AbstractMethodNode (intf, meth))
        let implementationNode = this.AddOrLookupNode (ImplementationNode (typ, intf, meth))
        let ovr =
            match this.Overrides.TryFind typeNode with
            | None ->
                let o = new Dictionary<_,_>() :> IDictionary<_,_>
                this.Overrides.Add(typeNode, o)
                o
            | Some o -> o
        ovr.Add(abstractMethodNode, implementationNode)

    static member Empty : Graph =
        {
            Nodes = ResizeArray()
            Edges = ResizeArray()
            Overrides = Dictionary()
            Lookup = Dictionary()
            Resources = ConcurrentDictionary()
        }
