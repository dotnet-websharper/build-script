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

/// Client-side (JavaScript) code.
module WebSharper.Tests.Website.Client

open WebSharper
open WebSharper.JavaScript
open WebSharper.Testing
open WebSharper.Sitelets.Tests.Client

[<JavaScript>]
let ClientSideTupleTest (x, y) =
    Runner.RunTests [|
        TestCategory "ClientSide" {
            Test "Passing tuple" {
                equal x 1
                equal y 2
            }
        }
    |]

[<JavaScript>]
type SimpleUnion =
    | SimpleUnionA of int
    | SimpleUnionB of string

    override this.ToString() =
        match this with
        | SimpleUnionA i -> sprintf "SimpleUnionA(%d)" i
        | SimpleUnionB s -> sprintf "SimpleUnionB(%s)" s

[<JavaScript>]
let ClientSideUnionTest x =
    Runner.RunTests [|
        TestCategory "ClientSide" {
            Test "Passing union" {
                equal x (SimpleUnionB "one")
                equal (x.ToString()) "SimpleUnionB(one)"
            }
        }
    |]

[<JavaScript>]
let ClientSideListTest x =
    Runner.RunTests [|
        TestCategory "ClientSide" {
            Test "Passing list" {
                equal (x |> List.toArray) [| 1 |]
            }
        }
    |]

[<JavaScript>]
type SimpleRecord =
    { A: int }

    override this.ToString() =
        "Hello" + string this.A

[<JavaScript>]
let ClientSideRecordTest x =
    Runner.RunTests [|
        TestCategory "ClientSide" {
            Test "Passing record" {
                equal (x.ToString()) "Hello1"
            }
        }
    |]

[<JavaScript>]
let InitSPA(where: string) =
    Console.Log($"Hello world from {where}!")
