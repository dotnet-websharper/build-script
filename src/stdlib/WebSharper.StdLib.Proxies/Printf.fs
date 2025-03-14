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

namespace WebSharper

open WebSharper.JavaScript

module M = WebSharper.Core.Macros

[<Proxy(typeof<PrintfFormat<_,_,_,_,_>>)>]
type internal PrintfFormat = 
    [<Macro(typeof<M.PrintF>)>]
    new (value: string) = {}

    [<Macro(typeof<M.PrintF>)>]
    new (value: string, args: obj[], types: System.Type[]) = {}

[<Name "Printf">]
[<Proxy "Microsoft.FSharp.Core.PrintfModule, FSharp.Core">]
module private PrintfProxy =
    [<Inline "$f($k)">]
    let PrintFormatThen (k: string -> 'R) (f: Printf.StringFormat<'T, 'R>) = X<'T>

    [<Inline "$f($k)">]
    let PrintFormatToStringThen (k: string -> 'R) (f: Printf.StringFormat<'T, 'R>) = X<'T>

    [<JavaScript; Inline>]
    let PrintFormatLine (f: Printf.TextWriterFormat<'T>) =  As<'T>(PrintFormatToStringThen (fun s -> Console.Log(s)) (As f))

    [<JavaScript; Inline>]
    let PrintFormatToStringThenFail (f: Printf.StringFormat<'T, 'R>) = PrintFormatToStringThen failwith f
