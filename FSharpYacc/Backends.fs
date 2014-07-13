﻿(*

Copyright 2012-2013 Jack Pappas

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

*)

namespace FSharpYacc.Plugin

open System.ComponentModel.Composition


/// Compiler backends.
[<Export>]
type Backends () =
    let mutable fsyaccBackend = None

    /// The fsyacc-compatible backend.
    [<Import>]
    member __.FsyaccBackend
        with get () : IBackend =
            match fsyaccBackend with
            | None ->
                invalidOp "The fsyacc backend has not been set."
            | Some backend ->
                backend
        and set value =
            fsyaccBackend <- Some value



