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

[<Struct>]
[<Proxy(typeof<voption<_>>)>]
[<RequireQualifiedAccess>]
[<Prototype(false)>]
type private ValueOptionProxy<'T> =
    | ValueNone 
    | ValueSome of 'T

    member this.Value =
        match this with 
        | ValueNone -> invalidOp "ValueOption.Value"
        | ValueSome x -> x 

    [<Inline "$0.$ == 1">]
    member this.IsSome = false

    [<Inline "$0.$ == 0">]
    member this.IsNone = false

    [<Inline; Pure>]  
    static member Some(v: 'T) = As<'T voption> (ValueSome v)  
  
    [<Inline>]  
    static member None = As<'T voption> ValueNone

    [<Inline>]
    static member op_Implicit(v: 'T) = As<'T voption> (ValueSome v)  
