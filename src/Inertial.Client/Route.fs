namespace Inertial.Client

open System
open Core

// Credits to Feliz.Router for these amazing active pattern definitions.
// https://github.com/Zaid-Ajaj/Feliz.Router/blob/master/src/Router.fs#L1430
module Route =

  let (|Int|_|) (value: string) =
    match Int32.TryParse value with
    | true, value -> Some value
    | _ -> None

  let (|Int64|_|) (input: string) =
    match Int64.TryParse input with
    | true, value -> Some value
    | _ -> None

  let (|Guid|_|) (input: string) =
    match Guid.TryParse input with
    | true, value -> Some value
    | _ -> None

  let (|Number|_|) (input: string) =
    match Double.TryParse input with
    | true, value -> Some value
    | _ -> None

  let (|Decimal|_|) (input: string) =
    match Decimal.TryParse input with
    | true, value -> Some value
    | _ -> None

  let (|Bool|_|) (input: string) =
    match input.ToLower() with
    | ("1" | "true") -> Some true
    | ("0" | "false") -> Some false
    | "" -> Some true
    | _ -> None

  let (|Query|_|) (input: string) =
    match splitQueryParam input with
    | [] -> None
    | queryParams -> queryParams |> List.choose (splitKeyValuePair >> getKeyValuePair) |> Some
