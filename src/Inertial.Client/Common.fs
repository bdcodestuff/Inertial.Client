namespace Inertial.Client

open Thoth.Json
open Fable.SimpleHttp
open System
open System.Threading
open Inertial.Lib.Types

module Core =
  type Method =
    | Get of List<string*obj>
    | Post of List<string*obj>
    | Put of List<string*obj>
    | Patch of List<string*obj>
    | Delete
    member x.ToMethodHttp() =
      match x with
      | Get _ -> HttpMethod.GET
      | Post _ -> HttpMethod.POST
      | Put _ -> HttpMethod.PUT
      | Patch _ -> HttpMethod.PATCH
      | Delete -> HttpMethod.DELETE
    member x.ToDataMap() =
      let dataList =
        match x with
        | Delete -> []
        | Get data | Post data | Put data | Patch data -> data
      Map.ofList dataList


  let split (splitBy: string) (value: string) =
    value.Split([| splitBy |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.toList

  let splitRoute = split "/"
  let splitQueryParam = split "&"
  let splitKeyValuePair = split "="

  let getKeyValuePair (parts: 'a list) =
    if List.length parts = 2 then
      Some(parts[0], parts[1])
    else
      None

      // define the Router location type
  type RouterLocation<'Props,'Shared> =
    {
      pathname: string
      query: string
      pageObj: PageObj<'Props,'Shared> option
      allowPartialReload: bool
      cancellationTokenSource: CancellationTokenSource
    }
