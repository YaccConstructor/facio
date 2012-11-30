﻿(*
Copyright (c) 2012, Jack Pappas
All rights reserved.

This code is provided under the terms of the 2-clause ("Simplified") BSD license.
See LICENSE.TXT for licensing details.
*)

//
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FSharpLex.SpecializedCollections


/// Character set implementation based on a Discrete Interval Encoding Tree.
/// This is faster and more efficient than the built-in F# Set<'T>,
/// especially for dense sets.
type CharSet =
    | Empty
    | Node of char * char * CharSet * CharSet

/// Functional programming operators related to the CharSet type.
[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module CharSet =
    /// The empty set.
    let empty = CharSet.Empty

    /// Returns 'true' if the set is empty.
    [<CompiledName("IsEmpty")>]
    let isEmpty tree =
        match tree with
        | Empty -> true
        | Node (_,_,_,_) -> false

    /// The set containing the given element.
    let singleton c =
        Node (c, c, Empty, Empty)

    //
    let rec private splitMaxImpl tree cont =
        match tree with
        | Empty ->
            invalidArg "tree" "Cannot split an empty tree."
        | Node (x, y, l, Empty) ->
            cont (x, y, l)
        | Node (x, y, l, r) ->
            splitMaxImpl r <| fun (u, v, r') ->
                cont (u, v, Node (x, y, l, r'))

    //
    let private splitMax tree =
        splitMaxImpl tree id

    //
    let rec private splitMinImpl tree cont =
        match tree with
        | Empty ->
            invalidArg "tree" "Cannot split an empty tree."
        | Node (x, y, Empty, r) ->
            cont (x, y, r)
        | Node (x, y, l, r) ->
            splitMinImpl l <| fun (u, v, l') ->
                cont (u, v, Node (x, y, l', r))

    //
    let private splitMin tree =
        splitMinImpl tree id

    //
    let private joinLeft = function
        | Empty ->
            invalidArg "tree" "Cannot join an empty tree."
        | Node (x, y, Empty, r) as t -> t
        | Node (x, y, l, r) ->
            let x', y', l' = splitMax l
            if y' + (char 1) = x then
                Node (x', y, l', r)
            else
                Node (x, y, l, r)

    //
    let private joinRight = function
        | Empty ->
            invalidArg "tree" "Cannot join an empty tree."
        | Node (x, y, l, Empty) as t -> t
        | Node (x, y, l, r) ->
            let x', y', r' = splitMin r
            if y + (char 1) = x' then
                Node (x, y', l, r')
            else
                Node (x, y, l, r)

    //
    let rec private addImpl value tree cont =
        match tree with
        | Empty ->
            Node (value, value, Empty, Empty)
            |> cont
        | Node (x, y, l, r) as t ->
            if value < x then
                if value + (char 1) = x then
                    Node (value, y, l, r)
                    |> joinLeft
                    |> cont
                else
                    addImpl value l <| fun l ->
                        Node (x, y, l, r)
                        |> cont
            elif value > y then
                if value = y + (char 1) then
                    Node (x, value, l, r)
                    |> joinRight
                    |> cont
                else
                    addImpl value r <| fun r ->
                        Node (x, y, l, r)
                        |> cont
            else
                cont t (* value in [x, y] *)

    /// Returns a new set with an element added to the set.
    /// No exception is raised if the set already contains the given element.
    [<CompiledName("Add")>]
    let add value tree =
        addImpl value tree id

    //
    let private merge = function
        | l, Empty -> l
        | Empty, r -> r
        | l, r ->
            let x, y, l' = splitMax l
            Node (x, y, l', r)

    //
    [<CompiledName("AddRange")>]
    let addRange lower upper tree =
        // If the range is "inverted", we consider the range
        // to be empty and simply return the original tree.
        if lower > upper then tree
        else
            // If the input set is empty, optimize by immediately
            // creating a new set from the specified range.
            match tree with
            | Empty ->
                Node (lower, upper, Empty, Empty)
            | Node (_,_,_,_) as tree ->
                // OPTIMIZE : Implement some function which adds the values into
                // the tree symbolically -- it'll be *way* faster.
                (tree, seq { lower .. upper })
                ||> Seq.fold (fun tree el ->
                    add el tree)

    //
    let rec private removeImpl value tree cont =
        match tree with
        | Empty ->
            cont Empty
        | Node (x, y, l, r) ->
            if value < x then
                removeImpl value l <| fun l ->
                    Node (x, y, l, r)
                    |> cont
            elif value > y then
                removeImpl value r <| fun r ->
                    Node (x, y, l, r)
                    |> cont
            elif value = x then
                if x = y then
                    // node must be removed
                    merge (l, r)
                    |> cont
                else
                    // interval can be simply shrunk
                    Node (x + (char 1), y, l, r)
                    |> cont
            elif value = y then
                // since value<>x => y>x
                Node (x, (char (int y - 1)), l, r)
                |> cont
            else
                // split interval
                Node (x, (char (int value - 1)), l, Node (value + (char 1), y, Empty, r))
                |> cont

    /// Returns a new set with the given element removed.
    /// No exception is raised if the set doesn't contain the given element.
    [<CompiledName("Remove")>]
    let remove value tree =
        removeImpl value tree id

    /// Evaluates to 'true' if the given element is in the given set.
    [<CompiledName("Contains")>]
    let rec contains value tree =
        match tree with
        | Empty ->
            false
        | Node (y, z, l, r) ->
            if value >= y && value <= z then true
            elif value < y then
                contains value l
            else
                // value > z
                contains value r

    //
    let rec private foldImpl (folder : 'State -> char -> 'State) (state : 'State) tree cont =
        match tree with
        | Empty ->
            cont state
        | Node (lowerBound, upperBound, left, right) ->
            // Fold over the left subtree
            foldImpl folder state left <| fun state ->
                // Fold over the values in this interval.
                let mutable state = state
                let lowerBound = int lowerBound
                let upperBound = int upperBound
                for i = lowerBound to upperBound do
                    state <- folder state (char i)

                // Fold over the right subtree
                foldImpl folder state right cont

    /// Applies the given accumulating function to all the elements of the set.
    [<CompiledName("Fold")>]
    let fold (folder : 'State -> char -> 'State) (state : 'State) tree =
        foldImpl folder state tree id

    //
    let rec private foldBackImpl (folder : char -> 'State -> 'State) tree (state : 'State) cont =
        match tree with
        | Empty ->
            cont state
        | Node (lowerBound, upperBound, left, right) ->
            // Fold backward over the right subtree
            foldBackImpl folder right state <| fun state ->

            // Fold downward over the values in this interval
            let mutable state = state
            let upperBound = int upperBound
            let lowerBound = int lowerBound
            for i = upperBound downto lowerBound do
                state <- folder (char i) state

            // Fold backward over the left subtree
            foldBackImpl folder left state cont

    /// Applies the given accumulating function to all the elements of the set.
    [<CompiledName("FoldBack")>]
    let foldBack (folder : char -> 'State -> 'State) tree (state : 'State) =
        foldBackImpl folder tree state id

    /// Returns a new set containing the results of applying the given function to each element of the input set.
    [<CompiledName("Map")>]
    let map (mapping : char -> char) tree =
        (empty, tree)
        ||> fold (fun set el ->
            add (mapping el) set)

    /// Returns a new set containing only the elements of the collection for which the given predicate returns true.
    [<CompiledName("Filter")>]
    let filter (predicate : char -> bool) tree =
        (empty, tree)
        ||> fold (fun set el ->
            if predicate el then
                add el set
            else set)

    //
    let rec private iterImpl (action : char -> unit) tree cont =
        match tree with
        | Empty ->
            cont ()
        | Node (lowerBound, upperBound, left, right) ->
            // Iterate over the left subtree
            iterImpl action left <| fun () ->

            // Iterate over the values in this interval
            let lowerBound = int lowerBound
            let upperBound = int upperBound
            for i = lowerBound to upperBound do
                action (char i)

            // Iterate over the right subtree
            iterImpl action right cont

    /// Applies the given function to each element of the set, in order from least to greatest.
    [<CompiledName("Iterate")>]
    let iter (action : char -> unit) tree =
        iterImpl action tree id

    //
    let rec private countImpl tree cont =
        match tree with
        | Empty ->
            cont 0
        | Node (lowerBound, upperBound, left, right) ->
            // Count the left and right subtrees.
            countImpl left <| fun leftCount ->
            countImpl right <| fun rightCount ->
                /// The number of values in this interval.
                let thisCount = int upperBound - int lowerBound + 1

                // Return the combined count for this node and it's children.
                thisCount + leftCount + rightCount
                |> cont

    /// Returns the number of elements in the set.
    [<CompiledName("Count")>]
    let count tree =
        countImpl tree id

    //
    let rec private existsImpl (predicate : char -> bool) tree cont =
        match tree with
        | Empty ->
            cont false
        | Node (lowerBound, upperBound, left, right) ->
            // Check the left subtree
            existsImpl predicate left <| fun leftResult ->
                if leftResult then
                    cont true
                else
                    // Check the current interval
                    let mutable result = false
                    let mutable i = lowerBound
                    while i <= upperBound && not result do
                        result <- predicate i
                        i <- i + (char 1)

                    if result then
                        cont true
                    else
                        // Check the right subtree
                        existsImpl predicate right cont

    /// Tests if any element of the collection satisfies the given predicate.
    /// If the input function is <c>predicate</c> and the elements are <c>i0...iN</c>,
    /// then this function computes predicate <c>i0 or ... or predicate iN</c>.
    [<CompiledName("Exists")>]
    let exists (predicate : char -> bool) tree =
        existsImpl predicate tree id

    //
    let rec private forallImpl (predicate : char -> bool) tree cont =
        match tree with
        | Empty ->
            cont true
        | Node (lowerBound, upperBound, left, right) ->
            // Check the left subtree
            forallImpl predicate left <| fun leftResult ->
                if not leftResult then
                    cont false
                else
                    // Check the current interval
                    let mutable result = true
                    let mutable i = lowerBound
                    while i <= upperBound && result do
                        result <- predicate i
                        i <- i + (char 1)

                    if not result then
                        cont false
                    else
                        // Check the right subtree
                        forallImpl predicate right cont

    /// Tests if all elements of the collection satisfy the given predicate.
    /// If the input function is <c>p</c> and the elements are <c>i0...iN</c>,
    /// then this function computes <c>p i0 && ... && p iN</c>.
    [<CompiledName("Forall")>]
    let forall (predicate : char -> bool) tree =
        forallImpl predicate tree id

    /// Creates a list that contains the elements of the set in order.
    [<CompiledName("ToList")>]
    let toList tree =
        // Fold backwards so we don't need to reverse the list.
        (tree, [])
        ||> foldBack (fun i lst ->
            i :: lst)

    /// Creates a set that contains the same elements as the given list.
    [<CompiledName("OfList")>]
    let ofList list =
        (empty, list)
        ||> List.fold (fun tree el ->
            add el tree)

    //
    [<CompiledName("ToSet")>]
    let toSet tree =
        (Set.empty, tree)
        ||> fold (fun set el ->
            Set.add el set)

    //
    [<CompiledName("OfSet")>]
    let ofSet set =
        (empty, set)
        ||> Set.fold (fun tree el ->
            add el tree)

    /// Creates an array that contains the elements of the set in order.
    [<CompiledName("ToArray")>]
    let toArray tree =
        let resizeArr = ResizeArray<_> ()
        iter resizeArr.Add tree
        resizeArr.ToArray ()

    /// Creates a set that contains the same elements as the given array.
    [<CompiledName("OfArray")>]
    let ofArray array =
        (empty, array)
        ||> Array.fold (fun tree el ->
            add el tree)

    /// Returns an ordered view of the set as an enumerable object.
    [<CompiledName("ToSeq")>]
    let rec toSeq tree =
        seq {
        match tree with
        | Empty -> ()
        | Node (lowerBound, upperBound, left, right) ->
            // Produce the sequence for the left subtree.
            yield! toSeq left

            // Produce the sequence of values in this interval.
            yield! { lowerBound .. upperBound }

            // Produce the sequence for the right subtree.
            yield! toSeq right }

    /// Creates a new set from the given enumerable object.
    [<CompiledName("OfSeq")>]
    let ofSeq seq =
        (empty, seq)
        ||> Seq.fold (fun tree el ->
            add el tree)

    /// Returns the highest (greatest) value in the set.
    [<CompiledName("MaxElement")>]
    let rec maxElement tree =
        match tree with
        | Empty ->
            invalidArg "tree" "Cannot retrieve the maximum element of an empty set."
        | Node (_,_,_,(Node (_,_,_,_) as right)) ->
            maxElement right
        | Node (_,maxEl,_,_) ->
            maxEl

    /// Returns the lowest (least) value in the set. 
    [<CompiledName("MinElement")>]
    let rec minElement tree =
        match tree with
        | Empty ->
            invalidArg "tree" "Cannot retrieve the minimum element of an empty set."
        | Node (_,_,(Node (_,_,_,_) as left),_) ->
            minElement left
        | Node (minEl,_,_,_) ->
            minEl

    /// Splits the set into two sets containing the elements for which
    /// the given predicate returns true and false respectively.
    [<CompiledName("Partition")>]
    let partition predicate set =
        ((empty, empty), set)
        ||> fold (fun (trueSet, falseSet) el ->
            if predicate el then
                add el trueSet,
                falseSet
            else
                trueSet,
                add el falseSet)

    /// Returns a new set with the elements of the second set removed from the first.
    [<CompiledName("Difference")>]
    let difference set1 set2 =
        // OPTIMIZE : This needs to be re-implemented in a
        // much more efficient way for it to be useful!

        // TEMP : This works, but it's **slow**.
        (empty, set1)
        ||> fold (fun set1' el ->
            if contains el set2 then
                set1'
            else
                add el set1')

    /// Computes the intersection of the two sets.
    [<CompiledName("Intersect")>]
    let intersect set1 set2 =
        // OPTIMIZE : This needs to be re-implemented in a
        // much more efficient way for it to be useful!

        // TEMP : This works, but it's **slow**.
        let smaller, larger =
            if count set1 < count set2 then
                set1, set2
            else set2, set1

        (empty, smaller)
        ||> fold (fun intersection el ->
            if contains el larger then
                add el intersection
            else
                intersection)    

    /// Computes the union of the two sets.
    [<CompiledName("Union")>]
    let union set1 set2 =
        // OPTIMIZE : This needs to be re-implemented in a
        // much more efficient way for it to be useful!

        // TEMP : This works, but it's **slow**.
        let smaller, larger =
            if count set1 < count set2 then
                set1, set2
            else set2, set1

        (larger, smaller)
        ||> fold (fun union el ->
            add el union)

    /// Returns a sequence of tuples <c>(lower, upper)</c>
    /// containing the intervals of values in the set.
    [<CompiledName("Intervals")>]
    let rec intervals tree =
        seq {
        match tree with
        | Empty -> ()
        | Node (lowerBound, upperBound, left, right) ->
            // Produce the sequence for the left subtree.
            yield! intervals left

            // Return the current interval.
            yield lowerBound, upperBound

            // Produce the sequence for the right subtree.
            yield! intervals right }
