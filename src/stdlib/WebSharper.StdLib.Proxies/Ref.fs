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

[<Proxy(typeof<ref<_>>)>]
[<Name "WebSharper.Ref">]
type private RefProxy<'T> =
    {
        [<Name 0>]
        mutable contents : 'T    
    } 
    member this.Value
        with    [<Inline "$this[0]">]
                get () = X<'T>
        and     [<Inline "void ($this[0] = $x)">]
                set (x: 'T) = X<unit>

[<JavaScript; Name "">]
type private ByRef<'T> =
     abstract get: unit -> 'T
     abstract set: 'T -> unit

[<JavaScript; Name "">]
type private OutRef<'T> =
     abstract set: 'T -> unit

[<JavaScript; Name "">]
type private InRef<'T> =
     abstract get: unit -> 'T