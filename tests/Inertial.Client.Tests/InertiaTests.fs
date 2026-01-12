module Inertial.Client.Tests.InertiaTests

open Fable.Mocha
open Inertial.Client.Inertia
open Inertial.Lib.Types

let inertiaTests = testList "Inertia" [

    testList "missingFromCacheMap" [
        testCase "returns all fields when cache is empty" <| fun _ ->
            let cacheMap = Map.empty
            let allFields = [| "a"; "b"; "c" |]
            let result = missingFromCacheMap cacheMap allFields
            Expect.equal (Set.ofArray result) (Set.ofArray allFields) "Should return all fields"

        testCase "returns missing fields" <| fun _ ->
            let cacheMap = Map.ofList [("a", box 1); ("c", box 3)]
            let allFields = [| "a"; "b"; "c"; "d" |]
            let result = missingFromCacheMap cacheMap allFields
            Expect.equal (Set.ofArray result) (Set.ofArray [| "b"; "d" |]) "Should return b and d"

        testCase "returns empty when all cached" <| fun _ ->
            let cacheMap = Map.ofList [("a", box 1); ("b", box 2)]
            let allFields = [| "a"; "b" |]
            let result = missingFromCacheMap cacheMap allFields
            Expect.equal result [||] "Should return empty array"
    ]

    testList "resolveReEvals" [
        testCase "removes cached fields from request" <| fun _ ->
            let cacheMap = Map.ofList [("a", box 1); ("c", box 3)]
            let requestedFromCache = [| "a"; "c" |]
            let requestedForReEval = [| "a"; "b"; "c"; "d" |]
            let result = resolveReEvals cacheMap requestedFromCache requestedForReEval
            Expect.equal (Set.ofArray result) (Set.ofArray [| "b"; "d" |]) "Should exclude cached"

        testCase "keeps fields not in cache" <| fun _ ->
            let cacheMap = Map.ofList [("a", box 1)]
            let requestedFromCache = [| "a"; "b" |]  // b requested but not in cache
            let requestedForReEval = [| "a"; "b"; "c" |]
            let result = resolveReEvals cacheMap requestedFromCache requestedForReEval
            // Only "a" is in cache, so "b" and "c" should remain
            Expect.equal (Set.ofArray result) (Set.ofArray [| "b"; "c" |]) "Should keep b and c"

        testCase "returns all when nothing cached" <| fun _ ->
            let cacheMap = Map.empty
            let requestedFromCache = [| "a"; "b" |]
            let requestedForReEval = [| "a"; "b"; "c" |]
            let result = resolveReEvals cacheMap requestedFromCache requestedForReEval
            Expect.equal (Set.ofArray result) (Set.ofArray [| "a"; "b"; "c" |]) "Should return all"
    ]

    testList "resolveNextPropsToEval" [
        testCase "Lazy with empty array returns all fields" <| fun _ ->
            let cacheMap = Map.empty
            let toGetFromCache = [||]
            let toReEval = Lazy [||]
            let allFields = [| "a"; "b"; "c" |]
            let result, _ = resolveNextPropsToEval false cacheMap toGetFromCache toReEval allFields
            match result with
            | Lazy arr -> Expect.equal (Set.ofArray arr) (Set.ofArray allFields) "Should return all fields"
            | _ -> failwith "Should be Lazy"

        testCase "Skip returns Skip when cache has all fields" <| fun _ ->
            let cacheMap = Map.ofList [("a", box 1); ("b", box 2)]
            let toGetFromCache = [||]
            let toReEval = Skip
            let allFields = [| "a"; "b" |]
            let result, shouldRequest = resolveNextPropsToEval false cacheMap toGetFromCache toReEval allFields
            Expect.equal result Skip "Should return Skip"
            Expect.isFalse shouldRequest "Should not request"

        testCase "Eager returns Eager" <| fun _ ->
            let cacheMap = Map.empty
            let toGetFromCache = [||]
            let toReEval = Eager
            let allFields = [| "a"; "b" |]
            let result, shouldRequest = resolveNextPropsToEval false cacheMap toGetFromCache toReEval allFields
            Expect.equal result Eager "Should return Eager"
            Expect.isTrue shouldRequest "Should request"

        testCase "EagerOnly returns EagerOnly for subset" <| fun _ ->
            let cacheMap = Map.empty
            let toGetFromCache = [||]
            let toReEval = EagerOnly [| "a" |]
            let allFields = [| "a"; "b"; "c" |]
            let result, shouldRequest = resolveNextPropsToEval false cacheMap toGetFromCache toReEval allFields
            match result with
            | EagerOnly arr -> Expect.equal (Set.ofArray arr) (Set.ofArray [| "a" |]) "Should return just a"
            | _ -> failwith "Should be EagerOnly"
            Expect.isTrue shouldRequest "Should request"
    ]
]
