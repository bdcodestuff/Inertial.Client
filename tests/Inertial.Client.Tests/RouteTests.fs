module Inertial.Client.Tests.RouteTests

open Fable.Mocha
open Inertial.Client.Route

let routeTests = testList "Route" [

    testList "Int active pattern" [
        testCase "parses valid integer" <| fun _ ->
            match "42" with
            | Int x -> Expect.equal x 42 "Should parse 42"
            | _ -> failwith "Should have matched Int"

        testCase "parses negative integer" <| fun _ ->
            match "-123" with
            | Int x -> Expect.equal x -123 "Should parse -123"
            | _ -> failwith "Should have matched Int"

        testCase "returns None for non-integer" <| fun _ ->
            match "hello" with
            | Int _ -> failwith "Should not match"
            | _ -> ()

        testCase "returns None for float string" <| fun _ ->
            match "3.14" with
            | Int _ -> failwith "Should not match"
            | _ -> ()
    ]

    testList "Int64 active pattern" [
        testCase "parses valid int64" <| fun _ ->
            match "9223372036854775807" with
            | Int64 x -> Expect.equal x 9223372036854775807L "Should parse max int64"
            | _ -> failwith "Should have matched Int64"

        testCase "returns None for overflow" <| fun _ ->
            match "99999999999999999999999" with
            | Int64 _ -> failwith "Should not match overflow"
            | _ -> ()
    ]

    testList "Guid active pattern" [
        testCase "parses valid GUID" <| fun _ ->
            let guidStr = "550e8400-e29b-41d4-a716-446655440000"
            match guidStr with
            | Guid g -> Expect.equal (g.ToString()) guidStr "Should parse GUID"
            | _ -> failwith "Should have matched Guid"

        testCase "returns None for invalid GUID" <| fun _ ->
            match "not-a-guid" with
            | Guid _ -> failwith "Should not match"
            | _ -> ()
    ]

    testList "Number active pattern" [
        testCase "parses integer as double" <| fun _ ->
            match "42" with
            | Number x -> Expect.equal x 42.0 "Should parse as double"
            | _ -> failwith "Should have matched Number"

        testCase "parses float" <| fun _ ->
            match "3.14159" with
            | Number x -> Expect.isTrue (abs(x - 3.14159) < 0.0001) "Should parse float"
            | _ -> failwith "Should have matched Number"

        testCase "parses negative float" <| fun _ ->
            match "-2.5" with
            | Number x -> Expect.equal x -2.5 "Should parse negative"
            | _ -> failwith "Should have matched Number"
    ]

    testList "Decimal active pattern" [
        testCase "parses valid decimal" <| fun _ ->
            match "123.45" with
            | Decimal x -> Expect.equal x 123.45m "Should parse decimal"
            | _ -> failwith "Should have matched Decimal"
    ]

    testList "Bool active pattern" [
        testCase "parses 'true'" <| fun _ ->
            match "true" with
            | Bool x -> Expect.isTrue x "Should be true"
            | _ -> failwith "Should have matched Bool"

        testCase "parses 'false'" <| fun _ ->
            match "false" with
            | Bool x -> Expect.isFalse x "Should be false"
            | _ -> failwith "Should have matched Bool"

        testCase "parses '1' as true" <| fun _ ->
            match "1" with
            | Bool x -> Expect.isTrue x "Should be true"
            | _ -> failwith "Should have matched Bool"

        testCase "parses '0' as false" <| fun _ ->
            match "0" with
            | Bool x -> Expect.isFalse x "Should be false"
            | _ -> failwith "Should have matched Bool"

        testCase "parses 'TRUE' (case insensitive)" <| fun _ ->
            match "TRUE" with
            | Bool x -> Expect.isTrue x "Should be true"
            | _ -> failwith "Should have matched Bool"

        testCase "parses empty string as true" <| fun _ ->
            match "" with
            | Bool x -> Expect.isTrue x "Empty should be true"
            | _ -> failwith "Should have matched Bool"

        testCase "returns None for invalid bool" <| fun _ ->
            match "yes" with
            | Bool _ -> failwith "Should not match"
            | _ -> ()
    ]

    testList "Query active pattern" [
        testCase "parses single query param" <| fun _ ->
            match "foo=bar" with
            | Query kvps ->
                Expect.equal kvps [("foo", "bar")] "Should parse single param"
            | _ -> failwith "Should have matched Query"

        testCase "parses multiple query params" <| fun _ ->
            match "foo=bar&baz=qux" with
            | Query kvps ->
                Expect.equal kvps [("foo", "bar"); ("baz", "qux")] "Should parse multiple"
            | _ -> failwith "Should have matched Query"

        testCase "returns None for empty string" <| fun _ ->
            match "" with
            | Query _ -> failwith "Should not match empty"
            | _ -> ()

        testCase "skips malformed params" <| fun _ ->
            match "foo=bar&invalid&baz=qux" with
            | Query kvps ->
                Expect.equal kvps [("foo", "bar"); ("baz", "qux")] "Should skip invalid"
            | _ -> failwith "Should have matched Query"
    ]
]
