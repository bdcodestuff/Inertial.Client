module Inertial.Client.Tests.CoreTests

open Fable.Mocha
open Inertial.Client.Core

let coreTests = testList "Core" [

    testList "split" [
        testCase "splits by delimiter" <| fun _ ->
            let result = split "/" "a/b/c"
            Expect.equal result ["a"; "b"; "c"] "Should split into 3 parts"

        testCase "removes empty entries" <| fun _ ->
            let result = split "/" "/a//b/"
            Expect.equal result ["a"; "b"] "Should remove empty entries"

        testCase "returns empty list for empty string" <| fun _ ->
            let result = split "/" ""
            Expect.equal result [] "Should return empty list"

        testCase "returns single item if no delimiter" <| fun _ ->
            let result = split "/" "hello"
            Expect.equal result ["hello"] "Should return single item"
    ]

    testList "splitRoute" [
        testCase "splits URL path" <| fun _ ->
            let result = splitRoute "/users/123/profile"
            Expect.equal result ["users"; "123"; "profile"] "Should split path"

        testCase "handles root path" <| fun _ ->
            let result = splitRoute "/"
            Expect.equal result [] "Root should be empty list"
    ]

    testList "splitQueryParam" [
        testCase "splits query params" <| fun _ ->
            let result = splitQueryParam "foo=bar&baz=qux"
            Expect.equal result ["foo=bar"; "baz=qux"] "Should split params"
    ]

    testList "splitKeyValuePair" [
        testCase "splits key=value" <| fun _ ->
            let result = splitKeyValuePair "foo=bar"
            Expect.equal result ["foo"; "bar"] "Should split key and value"

        testCase "handles value with equals" <| fun _ ->
            // Note: this will only get first two parts due to split behavior
            let result = splitKeyValuePair "foo=bar=baz"
            Expect.equal result ["foo"; "bar"; "baz"] "Should split all parts"
    ]

    testList "getKeyValuePair" [
        testCase "returns Some for valid pair" <| fun _ ->
            let result = getKeyValuePair ["foo"; "bar"]
            Expect.equal result (Some ("foo", "bar")) "Should return pair"

        testCase "returns None for single item" <| fun _ ->
            let result = getKeyValuePair ["foo"]
            Expect.equal result None "Should return None"

        testCase "returns None for more than two items" <| fun _ ->
            let result = getKeyValuePair ["a"; "b"; "c"]
            Expect.equal result None "Should return None for 3 items"

        testCase "returns None for empty list" <| fun _ ->
            let result = getKeyValuePair []
            Expect.equal result None "Should return None for empty"
    ]

    testList "Method" [
        testCase "Get ToDataMap returns map" <| fun _ ->
            let method = Get [("key", box "value")]
            let result = method.ToDataMap()
            Expect.equal (result.["key"] :?> string) "value" "Should have key"

        testCase "Delete ToDataMap returns empty" <| fun _ ->
            let method = Delete
            let result = method.ToDataMap()
            Expect.isTrue result.IsEmpty "Delete should have empty map"

        testCase "Post ToDataMap returns map" <| fun _ ->
            let method = Post [("a", box 1); ("b", box 2)]
            let result = method.ToDataMap()
            Expect.equal result.Count 2 "Should have 2 items"
    ]
]
