namespace Inertial.Client

open Fable.SimpleHttp
open Microsoft.FSharp.Control
open Thoth.Json
open Core

module Inertia =
  let addInertiaHeaders
    cookie
    propsToGet
    currentComponentName
    currentId
    isSSEResponse
    version
    input =
      // add base inertia headers
      let withInertiaHeaders =
        input
        |> Http.header (Headers.create "X-Requested-With" "XMLHttpRequest")
        |> Http.header (Headers.create "X-Inertial" "true")
        |> Http.header (Headers.create "X-Inertial-Id" currentId)
      // add partial data headers
      let withPartialDataHeaders =
        let partialProps =
          match propsToGet with
          | EvalAllProps -> None
          | OnlyEvalProps props -> Some <| (props |> Array.reduce (fun x y -> $"{x}, {y}") )
        match partialProps with
        | Some props ->
          withInertiaHeaders
            |> Http.header (Headers.create "X-Inertial-Partial-Component" currentComponentName)
            |> Http.header (Headers.create "X-Inertial-Partial-Data" props)
        | None ->
          withInertiaHeaders

      // add CSRF token header
      let withCookie =
        match cookie with
          | Some c ->
            withPartialDataHeaders
            |> Http.header (Headers.create "X-XSRF-TOKEN" c)
          | None -> withPartialDataHeaders
      
      let withSSE =
        if isSSEResponse then
          withCookie |> Http.header (Headers.create "X-Inertial-SSE-Response" "true")
        else
          withCookie
          
      let withVersion =
        withSSE |> Http.header (Headers.create "X-Inertial-Version" version)
        
      withVersion

  let inertiaHttp
    url
    (httpVerb:Method)
    propsToGet
    currentComponentName
    currentId
    isSSEResponse
    version
    propsDecoder
    sharedDecoder

    : Async<PageObj<'Props,'Shared> option> =
      let cookie = JsCookie.get "XSRF-TOKEN"
      let dataMap = httpVerb.ToDataMap()
      let includeData = not dataMap.IsEmpty && httpVerb <> Delete
      let addData (input:HttpRequest) =
        if includeData then
          input |> Http.content (BodyContent.Text <| Encode.Auto.toString(4,dataMap))
        else
          input

      async {
          let! response =
            Http.request url
            |> Http.method (httpVerb.ToMethodHttp())
            |> addData
            |> addInertiaHeaders cookie propsToGet currentComponentName currentId isSSEResponse version
            |> Http.send

          match response.statusCode with
          | 200 | 404 | 403 ->
            match PageObj.fromJson response.responseText propsDecoder sharedDecoder with
            | Ok p -> return Some p
            | Error err ->
              printfn $"error parsing JSON reply: {err}"
              return None
          | _ ->
            return None
      }

