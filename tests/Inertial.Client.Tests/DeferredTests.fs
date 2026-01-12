module Inertial.Client.Tests.DeferredTests

open Fable.Mocha
open Inertial.Lib.Types
open Inertial.Client

let deferredTests = testList "Deferred" [

    testList "isLoading" [
        testCase "returns true for Pending" <| fun _ ->
            let d = Pending (async { return 42 })
            Expect.isTrue (isLoading d) "Pending should be loading"

        testCase "returns false for Loaded" <| fun _ ->
            let d = Loaded 42
            Expect.isFalse (isLoading d) "Loaded should not be loading"

        testCase "returns false for Failed" <| fun _ ->
            let d = Failed (exn "error")
            Expect.isFalse (isLoading d) "Failed should not be loading"
    ]

    testList "isLoaded" [
        testCase "returns true for Loaded" <| fun _ ->
            let d = Loaded "hello"
            Expect.isTrue (isLoaded d) "Loaded should be loaded"

        testCase "returns false for Pending" <| fun _ ->
            let d = Pending (async { return "hello" })
            Expect.isFalse (isLoaded d) "Pending should not be loaded"

        testCase "returns false for Failed" <| fun _ ->
            let d = Failed (exn "error")
            Expect.isFalse (isLoaded d) "Failed should not be loaded"
    ]

    testList "isFailed" [
        testCase "returns true for Failed" <| fun _ ->
            let d = Failed (exn "error")
            Expect.isTrue (isFailed d) "Failed should be failed"

        testCase "returns false for Pending" <| fun _ ->
            let d = Pending (async { return 1 })
            Expect.isFalse (isFailed d) "Pending should not be failed"

        testCase "returns false for Loaded" <| fun _ ->
            let d = Loaded 1
            Expect.isFalse (isFailed d) "Loaded should not be failed"
    ]

    testList "tryGetLoaded" [
        testCase "returns Some for Loaded" <| fun _ ->
            let d = Loaded 42
            Expect.equal (tryGetLoaded d) (Some 42) "Should return value"

        testCase "returns None for Pending" <| fun _ ->
            let d = Pending (async { return 42 })
            Expect.equal (tryGetLoaded d) None "Should return None"

        testCase "returns None for Failed" <| fun _ ->
            let d = Failed (exn "error")
            Expect.equal (tryGetLoaded d) None "Should return None"
    ]

    testList "getLoadedOr" [
        testCase "returns value for Loaded" <| fun _ ->
            let d = Loaded 42
            Expect.equal (getLoadedOr 0 d) 42 "Should return loaded value"

        testCase "returns default for Pending" <| fun _ ->
            let d = Pending (async { return 42 })
            Expect.equal (getLoadedOr 0 d) 0 "Should return default"

        testCase "returns default for Failed" <| fun _ ->
            let d = Failed (exn "error")
            Expect.equal (getLoadedOr 0 d) 0 "Should return default"
    ]

    testList "tryGetError" [
        testCase "returns Some for Failed" <| fun _ ->
            let ex = exn "test error"
            let d = Failed ex
            match tryGetError d with
            | Some e -> Expect.equal e.Message "test error" "Should return exception"
            | None -> failwith "Should have returned error"

        testCase "returns None for Loaded" <| fun _ ->
            let d = Loaded 42
            Expect.equal (tryGetError d) None "Should return None"

        testCase "returns None for Pending" <| fun _ ->
            let d = Pending (async { return 42 })
            Expect.equal (tryGetError d) None "Should return None"
    ]

    testList "Deferred module functions" [
        testCase "Deferred.loaded creates Loaded" <| fun _ ->
            let d = Deferred.loaded 42
            match d with
            | Loaded x -> Expect.equal x 42 "Should be Loaded 42"
            | _ -> failwith "Should be Loaded"

        testCase "Deferred.failed creates Failed" <| fun _ ->
            let ex = exn "error"
            let d = Deferred.failed ex
            match d with
            | Failed e -> Expect.equal e.Message "error" "Should have message"
            | _ -> failwith "Should be Failed"

        testCase "Deferred.map transforms Loaded" <| fun _ ->
            let d = Loaded 10 |> Deferred.map (fun x -> x * 2)
            match d with
            | Loaded x -> Expect.equal x 20 "Should be 20"
            | _ -> failwith "Should be Loaded"

        testCase "Deferred.map preserves Failed" <| fun _ ->
            let d = Failed (exn "error") |> Deferred.map (fun x -> x * 2)
            match d with
            | Failed _ -> ()
            | _ -> failwith "Should remain Failed"

        testCase "Deferred.defaultValue returns value for Loaded" <| fun _ ->
            let result = Loaded 42 |> Deferred.defaultValue 0
            Expect.equal result 42 "Should return 42"

        testCase "Deferred.defaultValue returns default for Pending" <| fun _ ->
            let result = Pending (async { return 42 }) |> Deferred.defaultValue 0
            Expect.equal result 0 "Should return default"

        testCase "Deferred.ofOption wraps in Loaded" <| fun _ ->
            let d = Deferred.ofOption (Some 42)
            match d with
            | Loaded (Some x) -> Expect.equal x 42 "Should wrap Some"
            | _ -> failwith "Should be Loaded Some"

        testCase "Deferred.ofOption wraps None in Loaded" <| fun _ ->
            let d = Deferred.ofOption None
            match d with
            | Loaded None -> ()
            | _ -> failwith "Should be Loaded None"
    ]
]
