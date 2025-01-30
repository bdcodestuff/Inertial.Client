namespace Inertial.Client

open System
open Fable.SimpleHttp
open Microsoft.FSharp.Control
open Thoth.Json
open Core

module Inertia =
  
  let forceRefreshUrl response =
    response.responseHeaders
    |> Map.tryFind "x-inertial-location"
  
  let addInertiaHeaders
    cookie
    propsToGet
    currentComponentName
    currentId
    isSSETriggeredResponse
    isReloadAfterMount
    version
    input =
      // add base inertia headers
      let withInertiaHeaders =
        input
        |> Http.header (Headers.create "X-Requested-With" "XMLHttpRequest")
        |> Http.header (Headers.create "X-Inertial" "true")
        |> Http.header (Headers.create "X-Inertial-Id" currentId)
      // add partial, full data headers
      let withPartialDataHeaders =
        match propsToGet with
        | Eager ->
          withInertiaHeaders
          |> Http.header (Headers.create "X-Inertial-Full-Component" currentComponentName)
        | Lazy ->
          withInertiaHeaders
        | EagerOnly props ->
          let propList = props |> Array.reduce (fun x y -> $"{x}, {y}")
          withInertiaHeaders
          |> Http.header (Headers.create "X-Inertial-Partial-Component" currentComponentName)
          |> Http.header (Headers.create "X-Inertial-Partial-Data" propList)     

      // add CSRF token header
      let withCookie =
        match cookie with
          | Some c ->
            withPartialDataHeaders
            |> Http.header (Headers.create "X-XSRF-TOKEN" c)
          | None -> withPartialDataHeaders
      
      let withSSE =
        if isSSETriggeredResponse then
          withCookie |> Http.header (Headers.create "X-Inertial-SSE" "true")
        else
          withCookie |> Http.header (Headers.create "X-Inertial-SSE" "false")
        
      let withReloadAfterMount =
        if isReloadAfterMount then
          withSSE |> Http.header (Headers.create "X-Inertial-Reload" "true")
        else
          withSSE |> Http.header (Headers.create "X-Inertial-Reload" "false")
          
      let withVersion =
        withReloadAfterMount |> Http.header (Headers.create "X-Inertial-Version" version)
        
      withVersion

  let inertiaHttp
    url
    (httpVerb:Method)
    propsToGet
    currentComponentName
    currentId
    isSSETriggeredResponse
    isReloadAfterMount
    version
    propsDecoder
    sharedDecoder

    : Async<String option * PageObj<'Props,'Shared> option> =
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
            |> addInertiaHeaders cookie propsToGet currentComponentName currentId isSSETriggeredResponse isReloadAfterMount version
            |> Http.send

          //printfn $"{response}"
          
          let forceRefreshUrl = forceRefreshUrl response
          
          match forceRefreshUrl, response.statusCode with
          | Some url, _ -> return (Some url, None)
          | None, 200 | None, 404 | None, 403 ->
            match PageObj.fromJson response.responseText propsDecoder sharedDecoder with
            | Ok p -> return None, Some p
            | Error err ->
              printfn $"error parsing JSON reply: {err}"
              return None, None
          | _ ->
            return None, None
      }

