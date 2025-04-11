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

/// Implements concurrency primitives.
module internal WebSharper.Concurrency

open WebSharper
open WebSharper.JavaScript

type private OCE = System.OperationCanceledException

[<JavaScript; Prototype false>]
type Result<'T> =
    | Ok of 'T
    | No of exn
    | Cc of OCE
  
[<JavaScript; Prototype false>]
type CT =
    { 
        [<Name "c">] mutable IsCancellationRequested : bool 
        [<Name "r">] Registrations : (unit -> unit)[]
    }

[<Inline "$arr.push($item)">]
let private push arr item = X<int>

[<JavaScript>]
let internal noneCT =           
    { 
        IsCancellationRequested = false
        Registrations = [||]
    }

[<JavaScript>]
let internal Register (ct: CT) (callback: unit -> unit) =
    if ct ===. noneCT then
        { new System.IDisposable with
            member this.Dispose() = ()
        }
    else
        let i = push ct.Registrations callback - 1
        { new System.IDisposable with
            member this.Dispose() = ct.Registrations.[i] <- ignore
        }

[<JavaScript; Prototype false>]
type AsyncBody<'T> =
    {
        k  : Result<'T> -> unit
        ct : CT
    }

type Concurrent<'T>     = AsyncBody<'T> -> unit
and private C<'T>       = Concurrent<'T>

type private Queue<'T>  = System.Collections.Generic.Queue<'T>
type Milliseconds       = int

type private Scheduler [<JavaScript>]() =
    let mutable idle    = true
    let robin           = Queue<unit->unit>()

    [<JavaScript>]
    let rec tick () =
        let t = System.DateTime.Now
        let mutable loop = true
        while loop do
            match robin.Count with
            | 0 ->
                idle <- true
                loop <- false
            | _ ->
                robin.Dequeue()()
                if System.DateTime.Now - t > System.TimeSpan.FromMilliseconds 40. then
                    JS.SetTimeout tick 0 |> ignore
                    loop <- false

    [<JavaScript>]
    member this.Fork(action: unit -> unit) =
        robin.Enqueue action
        if idle then
            idle <- false
            JS.SetTimeout tick 0 |> ignore

[<JavaScript>]
let private scheduler = Scheduler()

[<JavaScript>]
let internal defCTS = ref(new System.Threading.CancellationTokenSource())

[<Inline>]
let fork action = scheduler.Fork action

/// Client implementation of the public function in ../WebSharper.StdLib/Concurrency.fs.
[<Inline>]
let Schedule action = fork action

[<JavaScript>]
let private cancel c = c.k (Cc (new OCE(As<System.Threading.CancellationToken> c.ct)))

[<JavaScript>]
let private checkCancel r =
    ()
    fun c -> if c.ct.IsCancellationRequested then cancel c else r c

[<JavaScript; Pure>]
let Return (x: 'T) : C<'T> =
    ()
    fun c -> c.k (Ok x)

[<JavaScript; Pure>]
let Zero =
    Return ()

[<JavaScript; Pure>]
let Bind (r: C<'T>, f: 'T -> C<'R>) =
    checkCancel <| fun c ->
        r { 
            k = function 
                | Ok x -> fork (fun () -> try f x c with e -> c.k (No e))
                | res  -> fork (fun () -> c.k (As res)) // error or cancellation
            ct = c.ct
        }

[<JavaScript; Pure>]
let Combine (a: C<unit>, b: C<'T>) : C<'T> = 
    Bind (a, fun _ -> b)

[<Inline>]
let Ignore (r: C<'T>): C<unit> = As<C<unit>> r

[<JavaScript; Pure>]
let Delay (mk: unit -> C<'T>) : C<'T> =
    ()
    fun c ->
        try mk () c with e -> c.k (No e)

[<JavaScript; Pure>]
let TryFinally (run: C<'T>, f: unit -> unit) : C<'T> =
    ()
    fun c ->
        run {
            k = fun r -> 
                try f ()
                    c.k r 
                with e -> c.k (No e)
            ct = c.ct
        }

[<JavaScript; Pure>]
let TryWith (r: C<'T>, f: exn -> C<'T>) : C<'T> =
    ()
    fun c ->
        r {
            k = function
                | Ok x -> c.k (Ok x)
                | No e as res -> try f e c with e -> c.k (As res)
                | res -> c.k (As res)
            ct = c.ct
        }

[<JavaScript; Pure>]
let Catch (r : C<'T>) : C<Choice<'T, exn>> =
    ()
    fun c ->
        try r {
                k = function 
                    | Ok x -> c.k (Ok (Choice1Of2 x))
                    | No e -> c.k (Ok (Choice2Of2 e))
                    | res  -> c.k (As res)
                ct = c.ct
            }
        with e -> c.k (Ok (Choice2Of2 e))

[<JavaScript; Pure>]
let GetCT : C<CT> =
    ()
    fun c -> c.k (Ok c.ct)

[<JavaScript; Pure>]
let FromContinuations (subscribe: ('T -> unit) * (exn -> unit) * (OCE -> unit) -> unit) : C<'T> =
    ()
    fun c ->
        let continued = ref false
        let once cont : unit =
            if !continued then failwith "A continuation provided by Async.FromContinuations was invoked multiple times" else
            continued := true
            fork cont   
        subscribe (
            fun a -> once (fun () -> c.k (Ok a))
        ,   fun e -> once (fun () -> c.k (No e))
        ,   fun e -> once (fun () -> c.k (Cc e))
        )

[<JavaScript>]
let StartWithContinuations (c: C<'T>, s: 'T -> unit, f: exn -> unit, cc: OCE -> unit, ctOpt) =
    let ct = defaultArg ctOpt (As !defCTS)
    if not ct.IsCancellationRequested then
        c {
            k = function
                | Ok x -> s x
                | No e -> f e
                | Cc e -> cc e
            ct = ct
        }

[<JavaScript>]
let UncaughtAsyncError (e: exn) =
    Console.Log ("WebSharper: Uncaught asynchronous exception", e)

[<JavaScript>]
let Start (c: C<unit>, ctOpt) =
    let ct = defaultArg ctOpt (As !defCTS)
    fork (fun () -> 
        if not ct.IsCancellationRequested then
            c {
                k = function
                    | No e -> UncaughtAsyncError e
                    | _ -> ()
                ct = ct
            }
    )

[<JavaScript>]
let StartImmediate (c: C<unit>, ctOpt) =
    let ct = defaultArg ctOpt (As !defCTS)
    if not ct.IsCancellationRequested then
        c {
            k = function
                | No e -> UncaughtAsyncError e
                | _ -> ()
            ct = ct
        }

//export function StartImmediate(c, ctOpt){
//  const ct=ctOpt==null?d:ctOpt.$0;
//  const d=(defCTS())[0];
//  if(!ct.c)c(New((a) =>  {
//    if(a.$==1)UncaughtAsyncError(a.$0);
//  }, ct));
//}

#nowarn "40"

[<JavaScript; Pure>]
let AwaitEvent (e: IEvent<'T>, ca: option<unit -> unit>) : C<'T> =
    ()
    fun c ->
        let mutable sub = JS.Undefined<System.IDisposable>
        let mutable creg = JS.Undefined<System.IDisposable>
        sub <-
            e.Subscribe (fun x -> 
                sub.Dispose()
                creg.Dispose()
                fork (fun () -> c.k (Ok x))        
            )
        creg <-
            Register c.ct (fun () -> 
                match ca with
                | Some ca ->
                    ca()
                | _ ->
                    sub.Dispose()
                    fork (fun () -> cancel c)    
            ) 

[<JavaScript; Pure>]
let AwaitTask (t: System.Threading.Tasks.Task) : C<unit> =
    FromContinuations (fun (ok, err, cc) ->
        if t.Status = System.Threading.Tasks.TaskStatus.Created then
            t.Start()
        t.ContinueWith(fun t ->
            if t.IsCanceled then
                cc (OCE())
            elif t.IsFaulted then
                err t.Exception
            else
                ok()   
        ) |> ignore
    )

[<JavaScript; Pure>]
let AwaitTask1 (t: System.Threading.Tasks.Task<'T>) : C<'T> =
    FromContinuations (fun (ok, err, cc) ->
        if t.Status = System.Threading.Tasks.TaskStatus.Created then
            t.Start()
        t.ContinueWith(fun (t: System.Threading.Tasks.Task<'T>) ->
            if t.IsCanceled then
                cc (OCE())
            elif t.IsFaulted then
                err t.Exception
            else
                ok t.Result  
        ) |> ignore
    )

[<JavaScript>]
let StartAsTask (c: C<'T>, ctOpt) =
    let tcs = System.Threading.Tasks.TaskCompletionSource<'T>()
    fork (fun () ->
        StartWithContinuations (c, tcs.SetResult, tcs.SetException, (fun _ -> tcs.SetCanceled()), ctOpt)
    )
    tcs.Task

[<JavaScript>]
let StartImmediateAsTask (c: C<'T>, ctOpt) =
    let tcs = System.Threading.Tasks.TaskCompletionSource<'T>()
    StartWithContinuations (c, tcs.SetResult, tcs.SetException, (fun _ -> tcs.SetCanceled()), ctOpt)
    tcs.Task

[<JavaScript; Pure>]
let Sleep (ms: Milliseconds) : C<unit> =
    ()
    fun c ->
        let mutable pending = JS.Undefined<JS.Handle>
        let mutable creg = JS.Undefined<System.IDisposable>
        pending <-
            JS.SetTimeout (fun () -> 
                creg.Dispose()
                fork (fun () -> c.k (Ok ()))
            ) ms
        creg <-
            Register c.ct (fun () -> 
                JS.ClearTimeout pending
                fork (fun () -> cancel c)
            )

[<JavaScript; Pure>]
let Parallel (cs: seq<C<'T>>) : C<'T[]> =
    let cs = Array.ofSeq cs
    if cs.Length = 0 then Return [||] else
    fun c ->
        let n = Array.length cs
        let o = ref n
        let a = As<'T[]>(JavaScript.Array(n))
        let accept i x =
            match !o, x with
            | 0, _ -> ()
            | 1, Ok x -> 
                a.[i] <- x
                o := 0
                c.k (Ok a)
            | oo, Ok x -> 
                a.[i] <- x
                o := oo - 1
            | _, res ->
                o := 0
                c.k (As res)
        for i = 0 to n - 1 do
            fork (fun () -> cs.[i] { k = accept i; ct = c.ct })            

[<JavaScript; Pure>]
let ParallelWithMaxDegree (cs: seq<C<'T>>) (d: int) : C<'T[]> =
    if d <= 0 then
       invalidArg "maxDegreeOfParallelism" ("maxDegreeOfParallelism must be positive, was " + string d)
    let cs = Array.ofSeq cs
    if cs.Length = 0 then Return [||] else
    fun c ->
        let n = Array.length cs
        let o = ref n
        let a = As<'T[]>(JavaScript.Array(n))
        let rec start i =
            fork (fun () -> cs.[i] { k = accept i; ct = c.ct })
        and accept i x =
            match !o, x with
            | 0, _ -> ()
            | 1, Ok x -> 
                a.[i] <- x
                o := 0
                c.k (Ok a)
            | oo, Ok x -> 
                if c.ct.IsCancellationRequested then 
                    o := 0
                    cancel c 
                else
                    a.[i] <- x
                    o := oo - 1
                    let j = n - oo + d
                    if j < n then start j 
            | n, res -> 
                o := 0
                c.k (As res)
        for i = 0 to (min d n) - 1 do 
            start i

[<JavaScript; Pure>]
let Sequential (cs: seq<C<'T>>) : C<'T[]> =
    let cs = Array.ofSeq cs
    if cs.Length = 0 then Return [||] else
    fun c ->
        let n = Array.length cs
        let a = As<'T[]>(JavaScript.Array(n))
        let rec start i =
            fork (fun () -> cs.[i] { k = accept i; ct = c.ct })
        and accept i x =
            match x with
            | Ok x -> 
                a.[i] <- x
                if i = n - 1 then
                    c.k (Ok a)
                elif c.ct.IsCancellationRequested then 
                    cancel c 
                else
                    start (i + 1) 
            | res ->
                c.k (As res)
        start 0     

[<JavaScript; Pure>]
let StartChild (r : C<'T>, t: Milliseconds option) : C<C<'T>> =
    ()
    fun c ->
        let inTime = ref true
        let cached = ref None
        let queue  = Queue()
        let tReg =
            match t with
            | Some timeout ->
                JS.SetTimeout (fun () ->
                    inTime := false
                    let err = No (System.TimeoutException())
                    while queue.Count > 0 do
                        queue.Dequeue() err
                ) timeout |> Some     
            | _ -> None
        fork (fun _ ->
            if not c.ct.IsCancellationRequested then
                r {
                    k = fun res ->
                        if !inTime then
                            cached := Some res
                            match tReg with
                            | Some r -> JS.ClearTimeout r
                            | _ -> ()
                            while queue.Count > 0 do
                                queue.Dequeue() res
                    ct = c.ct
                }
        )
        let r2 c2 =            
            if !inTime then
                match cached.Value with
                | Some x    -> c2.k x
                | None      -> queue.Enqueue c2.k
            else c2.k (No (System.TimeoutException()))
        c.k (Ok r2)

[<JavaScript>]
let StartChildAsTask (r : C<'T>) =
    ()
    fun c ->
        let ch = StartChild(r, None)
        ch {
            k = function
                | Ok r2 -> c.k (Ok (StartImmediateAsTask(r2, Some c.ct)))
                | _ -> ()
            ct = c.ct
        }

[<JavaScript; Pure>]
let OnCancel (action: unit -> unit) : C<System.IDisposable> =
    ()
    fun c -> c.k (Ok (Register c.ct action))

[<JavaScript; Pure>]
let TryCancelled (run: C<'T>, comp: OCE -> unit) : C<'T> =
    ()
    fun c ->
        run {
            k = function
                | Cc e as res ->
                    comp e
                    c.k res
                | res -> c.k res
            ct = c.ct
        }

[<JavaScript; Pure>]
let Using (x: 'U, f: 'U -> C<'T>) =
    TryFinally (f x, fun () -> (x :> System.IDisposable).Dispose())

[<JavaScript; Pure>]
let rec While (g: unit -> bool, c: C<unit>) : C<unit> = 
    if g() then 
        Bind (c, fun () -> While (g, c)) 
    else
        Return ()

[<JavaScript; Pure>]
let rec For (s: seq<'T>, b: 'T -> C<unit>) =
    Using (s.GetEnumerator(), fun ie -> 
        While ((fun () -> ie.MoveNext()), 
            Delay (fun () -> b ie.Current)))
