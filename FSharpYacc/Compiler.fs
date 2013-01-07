﻿(*
Copyright (c) 2012, Jack Pappas
All rights reserved.

This code is provided under the terms of the 2-clause ("Simplified") BSD license.
See LICENSE.TXT for licensing details.
*)

//
module FSharpYacc.Compiler

open Graham.Grammar
open Graham.LR
open FSharpYacc.Ast


//
type PrecompilationState<'Nonterminal, 'Terminal
    when 'Nonterminal : comparison
    and 'Terminal : comparison> = {
    /// The nonterminals declared by the specification, and their declared type (if provided).
    Nonterminals : Map<'Nonterminal, DeclaredType option>;
    /// The terminals declared by the specification, and their declared type (if provided).
    Terminals : Map<'Terminal, DeclaredType option>;
    //
    ProductionRules : Map<'Nonterminal, Symbol<'Nonterminal, 'Terminal>[][]>;
    //
    TerminalPrecedence : Map<'Terminal, Associativity * PrecedenceLevel>;
    /// For production rules with a %prec declaration, maps the production rule
    /// to the terminal specified in the declaration.
    ProductionRulePrecedenceOverrides : Map<'Nonterminal * Symbol<'Nonterminal, 'Terminal>[], 'Terminal>;
    //
    StartSymbols : Set<'Nonterminal>;
    /// Validation warning messages.
    ValidationWarnings : string list;
    /// Validation error messages.
    ValidationErrors : string list;
} with
    /// The empty precompilation state.
    static member Empty = {
        Nonterminals = Map.empty;
        Terminals = Map.empty;
        ProductionRules = Map.empty;
        TerminalPrecedence = Map.empty;
        ProductionRulePrecedenceOverrides = Map.empty;
        StartSymbols = Set.empty;
        ValidationWarnings = List.empty;
        ValidationErrors = List.empty; }

    /// Adds an error message to the precompilation state.
    static member internal AddError message (precompilationState : PrecompilationState<'Nonterminal, 'Terminal>) =
        { precompilationState with
            ValidationErrors = message :: precompilationState.ValidationErrors; }

    /// Adds a warning message to the precompilation state.
    static member internal AddWarning message (precompilationState : PrecompilationState<'Nonterminal, 'Terminal>) =
        { precompilationState with
            ValidationWarnings = message :: precompilationState.ValidationWarnings; }

/// Validates the given parser specification, and compiles it into the
/// data structures used by the Graham library.
// TODO : The 'options' parameter may not actually be needed here, but we won't know until
// we really implement this function; however, it may be possible (and useful) to augment
// the "standard" validation checks with backend-specific checks, since some backends may not
// support all features.
let precompile (spec : Specification, options : CompilationOptions)
    : PrecompilationState<NonterminalIdentifier, TerminalIdentifier> =
    (* TODO :   This function must be modified/rewritten ASAP to use the State workflow, or it'll soon be unreadable! *)

    /// The initial (empty) precompilation state.
    let precompilationState = PrecompilationState<_,_>.Empty

    (* NOTE :   In the code below, we fold *backwards* over the lists of declarations because they are all provided
                in reverse order (compared to the way they were ordered in the parser specification file). *)

    // Validation %token declarations of terminals.
    let precompilationState =
        (spec.TerminalDeclarations, precompilationState)
        ||> List.foldBack (fun (declaredType, terminalIds) precompilationState ->
            (terminalIds, precompilationState)
            ||> List.foldBack (fun terminalId precompilationState ->
                // Has this nonterminal been declared already?
                match Map.tryFind terminalId precompilationState.Terminals with
                | None ->
                    // Add the terminal and it's declared type to the table in the precompilation state.
                    { precompilationState with
                        Terminals = Map.add terminalId declaredType precompilationState.Terminals; }
                
                | Some existingDeclaredType ->
                    (* TODO : Also make sure this 'terminalId' hasn't been declared as a nonterminal. *)

                    (*  This terminal has already been declared!
                        If the previous declaration has the same declared type, emit a warning message;
                        if it has a different declared type, emit an error message. *)
                    if existingDeclaredType = declaredType then
                        let msg = sprintf "Duplicate declaration of the terminal '%s'." terminalId

                        // Add the warning message into the precompilation state.
                        PrecompilationState.AddWarning msg precompilationState
                    else
                        let msg =
                            match existingDeclaredType with
                            | None ->
                                sprintf "The terminal '%s' has already been declared without a data type." terminalId
                            | Some existingDeclaredType ->
                                sprintf "The terminal '%s' has already been declared with the type '%s'." terminalId existingDeclaredType

                        // Add the error message into the precompilation state.
                        PrecompilationState.AddError msg precompilationState))

    // Validate the nonterminals "declared" via production rules.
    // NOTE : Only the nonterminals themselves are validated here --
    // the production rules themselves are validated later.
    let precompilationState =
        (spec.Productions, precompilationState)
        ||> List.foldBack (fun (nonterminalId, _) precompilationState ->
            // Has this nonterminal been declared already?
            if Map.containsKey nonterminalId precompilationState.Nonterminals then
                // Add an error message into the compilation state.
                // TODO : Add a better error message.
                let msg = "Production rules can only be declared once for each nonterminal."
                PrecompilationState.AddError msg precompilationState

            elif Map.containsKey nonterminalId precompilationState.Terminals then
                // Can't have production rules for a terminal
                // TODO : Add a better error message.
                let msg = "Production rules cannot be declared for terminals."
                PrecompilationState.AddError msg precompilationState

            else
                // Add this nonterminal to the precompilation state.
                { precompilationState with
                    Nonterminals = Map.add nonterminalId None precompilationState.Nonterminals; })

    // Validate %type declarations of nonterminals.
    let precompilationState =
        (spec.NonterminalDeclarations, precompilationState)
        ||> List.foldBack (fun (declaredType, nonterminalId) precompilationState ->
            // Has this nonterminal been declared?
            match Map.tryFind nonterminalId precompilationState.Nonterminals with
            | None ->
                // To provide a better error message, check if this is a terminal identifier.
                match Map.tryFind nonterminalId precompilationState.Terminals with
                | None ->
                    // Add an error message into the precompilation state.
                    let msg = sprintf "Undeclared nonterminal '%s'. Type declarations can only be made for nonterminals declared via production rules."
                                nonterminalId
                    PrecompilationState.AddError msg precompilationState

                | Some _ ->
                    // Add an error message into the precompilation state.
                    let msg = "Type declarations (%%type) cannot be applied to terminals. The %%token declaration should be used instead."
                    PrecompilationState.AddError msg precompilationState

            | Some None ->
                // Add the nonterminal and it's declared type to the table in the precompilation state.
                { precompilationState with
                    Nonterminals = Map.add nonterminalId (Some declaredType) precompilationState.Nonterminals; }
            
            | Some (Some existingDeclaredType) ->
                (*  If the previous declaration has the same declared type, emit a warning message;
                    if it has a different declared type, emit an error message. *)
                if existingDeclaredType = declaredType then
                    // Add a warning message into the precompilation state.
                    let msg = sprintf "Duplicate %%type declaration for the nonterminal '%s'." nonterminalId
                    PrecompilationState.AddWarning msg precompilationState
                else
                    // Add an error message into the precompilation state.
                    let msg =
                        sprintf "The nonterminal '%s' has already been declared to have the type '%s'."
                            nonterminalId existingDeclaredType

                    PrecompilationState.AddError msg precompilationState)

    // The specification must declare at least one nonterminal as a starting production.
    // All nonterminals declared as starting productions must have type declarations.
    let precompilationState =
        match spec.StartingProductions with
        | [] ->
            let msg = "The specification must declare at least one (1) nonterminal as a starting nonterminal via the %start declaration."
            PrecompilationState.AddError msg precompilationState
        | startingProductions ->
            (startingProductions, precompilationState)
            ||> List.foldBack (fun nonterminalId precompilationState ->
                // Has this nonterminal been declared?
                match Map.tryFind nonterminalId precompilationState.Nonterminals with
                | None ->
                    // Add an error message into the precompilation state.
                    let msg = sprintf "The nonterminal '%s' is not defined." nonterminalId
                    PrecompilationState.AddError msg precompilationState

                | Some None ->
                    // Add an error message into the precompilation state.
                    let msg = sprintf "The type of nonterminal '%s' has not been declared. \
                                        Nonterminals used as starting productions must have their types declared with a %%type declaration."
                                        nonterminalId
                    PrecompilationState.AddError msg precompilationState

                | Some (Some _) ->
                    // Is this a duplicate %start declaration?
                    if Set.contains nonterminalId precompilationState.StartSymbols then
                        // Add a warning message to the precompilation state.
                        let msg = sprintf "Duplicate %%start declaration for the nonterminal '%s'." nonterminalId
                        PrecompilationState.AddWarning msg precompilationState
                    else
                        // Add this nonterminal to the set of start symbols in the precompilation state.
                        { precompilationState with
                            StartSymbols = Set.add nonterminalId precompilationState.StartSymbols; })

    // Validate the production rules.
    // Determine which, if any, of the terminals used in the precedence-override declarations are "dummy" terminals.
    let dummyTerminals, precompilationState =
        (spec.Productions, (Set.empty, precompilationState))
        ||> List.foldBack (fun (nonterminalId, rules) (dummyTerminals, precompilationState) ->
            // Make sure the symbols used in the production rules have all been declared.
            // OPTIMIZE : Both the validation and the conversion to Graham.Grammar.Symbol could be done in a single pass.
            let productionRulesValid, precompilationState =
                (* Fold forward here, because the ordering doesn't matter. *)
                ((true, precompilationState), rules)
                ||> List.fold (fun (productionRulesValid, precompilationState) rule ->
                    ((productionRulesValid, precompilationState), rule.Symbols)
                    ||> List.fold (fun (productionRulesValid, precompilationState) symbolId ->
                    // Is this symbol a defined terminal or nonterminal?
                    if Map.containsKey symbolId precompilationState.Nonterminals ||
                        Map.containsKey symbolId precompilationState.Terminals then
                        productionRulesValid,
                        precompilationState
                    else
                        // Undefined symbol; add an error message to the precompilation state.
                        let msg = sprintf "Undeclared symbol '%s'." symbolId
                        false,
                        PrecompilationState.AddError msg precompilationState))

            // Convert the production rules into the form needed for use with Graham,
            // then add them into the precompilation state.
            if not productionRulesValid then
                // An error was found when validating the production rules, so don't bother processing them.
                dummyTerminals, precompilationState
            else
                /// The production rules for this nonterminal.
                let productionRules =
                    // OPTIMIZE : Use List.revMapIntoArray here to avoid unnecessary intermediate data structures.
                    rules
                    |> List.rev
                    |> List.toArray
                    |> Array.map (fun rule ->
                        rule.Symbols
                        |> List.rev
                        |> List.toArray
                        |> Array.map (fun symbolId ->
                            if Map.containsKey symbolId precompilationState.Nonterminals then
                                Symbol.Nonterminal symbolId
                            else
                                Symbol.Terminal symbolId))

                // Add the converted production rules into the precompilation state.
                let precompilationState =
                    { precompilationState with
                        ProductionRules = Map.add nonterminalId productionRules precompilationState.ProductionRules; }

                // Validate the %prec declarations (if present) for these rules.
                // OPTIMIZE : Combine this with the rule conversion (above) to avoid re-converting the rules.
                ((dummyTerminals, precompilationState), rules)
                ||> List.fold (fun (dummyTerminals, precompilationState) rule ->
                    // Does this rule have a %prec declaration?
                    match rule.ImpersonatedPrecedence with
                    | None ->
                        dummyTerminals, precompilationState
                    | Some impersonatedTerminal ->
                        let ruleSymbols =
                            rule.Symbols
                            |> List.rev
                            |> List.toArray
                            |> Array.map (fun symbolId ->
                                if Map.containsKey symbolId precompilationState.Nonterminals then
                                    Symbol.Nonterminal symbolId
                                else
                                    Symbol.Terminal symbolId)

                        // Make sure the impersonated identifier is not already declared as a nonterminal.
                        if Map.containsKey impersonatedTerminal precompilationState.Nonterminals then
                            // Nonterminals can't be impersonated -- add an error message to the precompilation state.
                            let msg = "Nonterminals cannot be impersonated by %prec declarations."
                            dummyTerminals,
                            PrecompilationState.AddError msg precompilationState
                        else
                            // Is this a declared terminal? If not, it'll become a dummy terminal.
                            let dummyTerminals =
                                if Map.containsKey impersonatedTerminal precompilationState.Terminals then
                                    dummyTerminals
                                else
                                    Set.add impersonatedTerminal dummyTerminals

                            dummyTerminals,
                            { precompilationState with
                                ProductionRulePrecedenceOverrides =
                                    precompilationState.ProductionRulePrecedenceOverrides
                                    |> Map.add (nonterminalId, ruleSymbols) impersonatedTerminal; }))

    // Validate the precedence/associativity declarations.
    let dummyTerminalsWithoutPrecedence, precompilationState =
        (* NOTE :   We REQUIRE the associativity/precedence to be specified for any dummy terminals
                    defined by the %prec declaration of a production rule. *)
        (spec.Associativities, (dummyTerminals, (1<_> : PrecedenceLevel), precompilationState))
        ||> List.foldBack (fun (associativity, terminals) (dummyTerminalsWithoutPrecedence, precedenceLevel, precompilationState) ->
            let terminalSet, precompilationState =
                (terminals, (Set.empty, precompilationState))
                ||> List.foldBack (fun terminal (terminalSet, precompilationState) ->
                    // Has the associativity/precedence already been declared for this terminal?
                    if Map.containsKey terminal precompilationState.TerminalPrecedence then
                        // If the previous declaration was within this precedence "group",
                        // then just emit a warning about the duplicate declaration.
                        // Otherwise, emit an error because we don't know which precedence
                        // the terminal is really supposed to belong to.
                        if Set.contains terminal terminalSet then
                            let msg = sprintf "Duplicate associativity declaration for '%s'." terminal
                            terminalSet,
                            PrecompilationState.AddWarning msg precompilationState
                        else
                            let msg = sprintf "Duplicate associativity declaration for '%s' which conflicts with an earlier declaration." terminal
                            terminalSet,
                            PrecompilationState.AddError msg precompilationState
                    else
                        // Add this terminal to the set of terminals in this precedence "group".
                        let terminalSet = Set.add terminal terminalSet

                        // Add the associativity and precedence for this terminal to the precompilation state.
                        let precompilationState =
                            { precompilationState with
                                TerminalPrecedence =
                                    precompilationState.TerminalPrecedence
                                    |> Map.add terminal (associativity, precedenceLevel); }
                        terminalSet,
                        precompilationState)

            // Remove any terminals in this set from the set of dummy terminals without precedences.
            let dummyTerminalsWithoutPrecedence =
                Set.difference dummyTerminalsWithoutPrecedence terminalSet
            
            dummyTerminalsWithoutPrecedence,
            precedenceLevel + 1<_>,
            precompilationState)
        // Discard the precedence level counter
        |> fun (a, _, c) -> a, c

    // Add error messages to the precompilation state for any dummy terminals which
    // don't have associativity declarations.
    let precompilationState =
        (precompilationState, dummyTerminalsWithoutPrecedence)
        ||> Set.fold (fun precompilationState dummyTerminal ->
            let msg = sprintf "The terminal '%s' does not have an associativity declaration. \
                                'Dummy' terminals are required to have associativity declarations."
                                dummyTerminal
            PrecompilationState.AddError msg precompilationState)

    // Return the final precompilation state.
    precompilationState

/// Creates a PrecedenceSettings record from the precompilation state.
let private precedenceSettings (precompilationState : PrecompilationState<NonterminalIdentifier, TerminalIdentifier>,
                                productionRuleIds : Map<AugmentedNonterminal<_> * AugmentedSymbol<_,_>[], ProductionRuleId>)
    : PrecedenceSettings<TerminalIdentifier> =
    //
    let rulePrecedence =
        (Map.empty, precompilationState.ProductionRules)
        ||> Map.fold (fun rulePrecedence nonterminal rules ->
            (rulePrecedence, rules)
            ||> Array.fold (fun rulePrecedence rule ->
                /// The identifier for this production rule.
                let productionRuleId =
                    //
                    let augmentedKey =
                        AugmentedNonterminal.Nonterminal nonterminal,
                        rule
                        |> Array.map (function
                            | Nonterminal nonterminal ->
                                Nonterminal <| AugmentedNonterminal.Nonterminal nonterminal
                            | Terminal terminal ->
                                Terminal <| AugmentedTerminal.Terminal terminal)

                    Map.find augmentedKey productionRuleIds

                /// The terminal whose associativity and precedence is impersonated by this production rule.
                let precedenceTerminal =
                    // Does this rule have a precedence override declaration?
                    match Map.tryFind (nonterminal, rule) precompilationState.ProductionRulePrecedenceOverrides with
                    | Some impersonatedTerminal ->
                        Some impersonatedTerminal
                    | None ->
                        // The precedence of a rule is the precedence of it's last (right-most) terminal.
                        match System.Array.FindLastIndex (rule, (function Terminal _ -> true | Nonterminal _ -> false)) with
                        //match System.Array.FindIndex (rule, (function Terminal _ -> true | Nonterminal _ -> false)) with
                        | -1 ->
                            // This rule does not contain any terminals, so it is not assigned a precedence.
                            None
                        | lastTerminalIndex ->
                            match rule.[lastTerminalIndex] with
                            | Terminal terminal ->
                                Some terminal
                            | Nonterminal _ ->
                                failwith "Found a nonterminal where a terminal was expected."

                // If this rule can be assigned a precedence, add it to the rule precedence map now.
                match precedenceTerminal with
                | None ->
                    rulePrecedence
                | Some precedenceTerminal ->
                    // The associativity and precedence of the impersonated terminal.
                    match Map.tryFind precedenceTerminal precompilationState.TerminalPrecedence with
                    | None ->
                        // The terminal has no precedence, so the rule has no precedence.
                        rulePrecedence
                    | Some impersonatedTerminalPrecedence ->
                        // Add the precedence to the rule precedence map.
                        Map.add productionRuleId impersonatedTerminalPrecedence rulePrecedence
                    ))

    // Filter any "dummy" terminals out of the terminal precedence map.
    let terminalPrecedence =
        precompilationState.TerminalPrecedence
        |> Map.filter (fun terminal _ ->
            Map.containsKey terminal precompilationState.Terminals)

    // Create and return a PrecedenceSettings record from the constructed precedence maps.
    {   TerminalPrecedence = terminalPrecedence;
        RulePrecedence = rulePrecedence; }    

/// Compiles a parser specification into a deterministic pushdown automaton (DPDA),
/// then invokes a specified backend to generate code implementing the parser automaton.
let compile (spec : Specification, options : CompilationOptions) : Choice<_,_> =
    // Validate/precompile the specification.
    let precompilationResult = precompile (spec, options)

    // Return the list of error messages if it's non-empty.
    match precompilationResult.ValidationErrors with
    | (_ :: _ as errorMessages) ->
        Choice2Of2 errorMessages
    | [] ->
        /// The grammar created from the parser specification.
        let grammar =
            //
            let rawGrammar : Grammar<_,_> = {
                Terminals =
                    (Set.empty, precompilationResult.Terminals)
                    ||> Map.fold (fun terminals terminal _ ->
                        Set.add terminal terminals);
                Nonterminals =
                    (Set.empty, precompilationResult.Nonterminals)
                    ||> Map.fold (fun nonterminals nonterminal _ ->
                        Set.add nonterminal nonterminals);
                Productions = precompilationResult.ProductionRules; }

            // Augment the grammar.
            Grammar.Augment (rawGrammar, precompilationResult.StartSymbols)

        /// The production-rule-id lookup table.
        let productionRuleIds =
            Grammar.ProductionRuleIds grammar

        // Create the precedence settings (precedence and associativity maps)
        // from the precompilation result.
        let precedenceSettings =
            precedenceSettings (precompilationResult, productionRuleIds)

        (*  Create the LR(0) automaton from the grammar; report the number of states and
            the number of S/R and R/R conflicts. If there are any conflicts, apply the
            precedence table to the constructed parser table to (possibly) resolve some
            of them. At this point, if there aren't any remaining conflicts, report that
            the grammar is LR(0) and return. *)
        //
        let lr0Table = Lr0.createTable grammar

        /// The LR(0) parser table, after applying precedence settings.
        let lr0Table' =
            // Apply precedences to resolve conflicts.
            Lr0.applyPrecedence (lr0Table, precedenceSettings)

        (*  Upgrade the LR(0) automaton to SLR(1); report the remaining number of S/R and
            R/R conflicts. If there aren't any remaining conflicts, report that the grammar
            is SLR(1) and return. *)
        //
        let slr1Table = Slr1.upgrade (grammar, lr0Table', productionRuleIds)        

        (*  Upgrade the LR(0)/SLR(1) automaton to LALR(1); report the remaining number of
            S/R and R/R conflicts. If there aren't any remaining conflicts, report that the
            grammar is LALR(1) and return. *)
        //
        let lalrLookaheadSets =
            Lalr1.lookaheadSets (grammar, slr1Table)

        // If we detected that the grammar is not LR(k), stop and return an error message.
        match Lalr1.lookaheadSets (grammar, slr1Table) with
        | Choice2Of2 errorMessage ->
            Choice2Of2 [errorMessage]
        | Choice1Of2 lookaheadSets ->
            //
            let lalr1Table =
                Lalr1.upgrade (grammar, slr1Table, productionRuleIds, lookaheadSets)
            
            (*  If we reach this point, the grammar is not LALR(1), but we can still create a
                parser by taking certain actions to resolve any remaining conflicts.
                Emit a _warning_ message for each of these conflicts, specifying the action
                we've taken to resolve it. *)
            //
            let lalr1Table' =
                Lr0.resolveConflicts lalr1Table

            // Return the compiled parser table.
            Choice1Of2 lalr1Table'
