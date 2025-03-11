namespace Inertial.Client

open System
open System.Text.RegularExpressions
open Fable.SimpleHttp
open Inertial.Client.Core
open Microsoft.FSharp.Control
open Thoth.Json
open Core
open Browser
open Inertial.Lib.Types
open Inertial.Lib.Helpers

module Inertia =
  
  let forceRefreshUrl response =
    response.responseHeaders
    |> Map.tryFind "x-inertial-location"
  
  let isReload response =
    response.responseHeaders
    |> Map.tryFind "x-inertial-reload"
  
  let addInertiaHeaders
    cookie
    propsToGet
    currentComponentName
    currentId
    isSSETriggeredResponse
    isReloadAfterMount
    (cacheStorage : CacheStorage option)
    (cacheRetrieval : CacheRetrieval option)
    version
    input

    =
      // add base inertia headers
      let withInertiaHeaders =
        input
        |> Http.header (Headers.create "X-Requested-With" "XMLHttpRequest")
        |> Http.header (Headers.create "X-Inertial" "true")
        
      let withCurrentId =
        match currentId with
        | Some currentId ->
          withInertiaHeaders
          |> Http.header (Headers.create "X-Inertial-Id" currentId)
        | None -> withInertiaHeaders
      // add partial, full data headers
      let withPartialDataHeaders =
        match propsToGet with
        | Eager ->
          withCurrentId
          |> Http.header (Headers.create "X-Inertial-Full-Component" currentComponentName)
        | Lazy ->
          withCurrentId
        | EagerOnly props ->
          let propList = props |> Array.reduce (fun x y -> $"{x}, {y}")
          withCurrentId
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
        
      let withCacheStorage =
        match cacheStorage with
        | Some c ->
          withReloadAfterMount |> Http.header (Headers.create "X-Inertial-CacheStorage" (c.ToHeader()))
        | None ->
          withReloadAfterMount
          
      let withCacheRetrieval =
        match cacheRetrieval with
        | Some c ->
          withCacheStorage |> Http.header (Headers.create "X-Inertial-CacheRetrieval" (c.ToHeader()))
        | None ->
          withCacheStorage
          
      let withVersion =
        withCacheRetrieval |> Http.header (Headers.create "X-Inertial-Version" version)
        
      withVersion

  let getCacheForComponent (componentName:string option) (allFieldNames:string array) =
    //printfn $"Debug -- comp: {componentName}, allFields: {allFieldNames}"
    match componentName with
    | Some currentComponentName -> 
      allFieldNames
      |> Array.map (fun item ->
        let stored = sessionStorage.getItem $"cache:{currentComponentName}:{item}"
        if stored <> null && stored <> "" then
          //let decoded = Decode.fromString cacheResultDecoder stored
          let decoded = Decode.fromString asyncDataDecoder stored
          match decoded with
          | Ok d -> Some (item, box d)
          | Error e ->
            printfn $"Error decoding stored object for {item}: {e}"
            None
        else
          //printfn $"No stored data for {item}"
          None) 
      |> Array.choose id
      |> Map.ofArray
    | None ->
      printfn "Could not find component in map"
      Map.empty
  
  let resolveReEvals (cacheMap: Map<string,obj>) (requestedFromCache:string array) (requestedForReEval:string array) =
    // these are requested from cache and present in cache
    let foundInCacheOkTOmitFromReEval = requestedFromCache |> Array.filter cacheMap.ContainsKey
    // remove keys from http request
    requestedForReEval |> Array.except foundInCacheOkTOmitFromReEval
    
  let missingFromCacheMap (cacheMap:Map<string,obj>) (allFieldNames: string array) =
    let cacheMapSet = cacheMap |> Map.toArray |> Array.map fst |> Set.ofArray
    let allFieldsSet = allFieldNames |> Set.ofArray
    Set.difference allFieldsSet cacheMapSet |> Set.toArray
    
  let resolveNextPropsToEval isReloadOnMount cacheMap toGetFromCache (toReEval:PropsToEval) allFieldNames : PropsToEval * bool * bool=
    let missingFromCache = missingFromCacheMap cacheMap allFieldNames
    let toReEvalArr =
      match toReEval with
      | Lazy when missingFromCache |> Array.isEmpty -> [||]
      | Lazy when missingFromCache = allFieldNames -> allFieldNames
      | Lazy -> missingFromCache
      | Eager -> allFieldNames
      | EagerOnly a -> a
    
    let nextReEvals = resolveReEvals cacheMap toGetFromCache toReEvalArr
    
    let reEvalAllFields toReEval =
      let toReEvalSet = Set.ofArray toReEval
      let allFieldNameSet = Set.ofArray allFieldNames
      Set.isSubset toReEvalSet allFieldNameSet && Set.isSuperset toReEvalSet allFieldNameSet
    
    match nextReEvals with
    | [||] -> Lazy, false, false
    | a when reEvalAllFields a -> Eager, false, true
      // if not isReloadOnMount then
      //   Eager, true, false
      // else
      //   Eager, false, true
    | a when not isReloadOnMount -> EagerOnly a, true, false
    | a -> EagerOnly a, false, true
  
  let inertiaHttp
    url
    (httpVerb:Method)
    propsToEval
    (cacheRetrieval: CacheRetrieval)
    (cacheStorage: CacheStorage)
    currentComponentName
    (currentId:string option)
    isSSETriggeredResponse
    isReloadAfterMount
    version
    propsDecoder
    sharedDecoder
    (toFieldNames: string -> string array)
    (toMap: (option<string array> * 'Props) -> array<string*obj>)
    (resolver: Map<string,obj> * 'Props -> 'Props)
    (requestedComponentName:string option)

    : Async<String option * PageObj<'Props,'Shared> option> =
      let cookie = JsCookie.get "XSRF-TOKEN"
      let dataMap = httpVerb.ToDataMap()
      //let fieldNames = toFieldNames requestedComponentName
      let fieldNames =
        match requestedComponentName with
        | Some name -> toFieldNames name
        | None ->
          printfn "Could not find component in map"
          [||]
      //let cacheMap = getCacheForComponent currentComponentName fieldNames
      let cacheMap = getCacheForComponent requestedComponentName fieldNames
      let includeData = not dataMap.IsEmpty && httpVerb <> Delete
      let addData (input:HttpRequest) =
        if includeData then
          input |> Http.content (BodyContent.Text <| Encode.Auto.toString(4,dataMap))
        else
          input       

      let propsToGet, shouldReload, cacheMap, shouldWriteCache =
        match cacheRetrieval with
        // 
        | CheckForCached toGet ->
          let nextProps, shouldReload, shouldWrite = resolveNextPropsToEval isReloadAfterMount cacheMap toGet propsToEval fieldNames
          nextProps, Some shouldReload, cacheMap, shouldWrite
        | CheckForAll ->
          let nextProps, shouldReload, shouldWrite = resolveNextPropsToEval isReloadAfterMount cacheMap fieldNames propsToEval fieldNames
          nextProps, Some shouldReload, cacheMap, shouldWrite
        | SkipCache ->
          propsToEval, None, Map.empty, isReloadAfterMount

      
      //printfn $"Debug -- isReloadAfterMount: {isReloadAfterMount}, propsToEvalIn: {propsToEval}, out: {propsToGet}, cacheMap:{cacheMap}, shouldReload: {shouldReload}, shouldWriteCache: {shouldWriteCache}, store: {cacheStorage}, retrieve: {cacheRetrieval}"

      async {
          let! response =
            Http.request url
            |> Http.method (httpVerb.ToMethodHttp())
            |> addData
            |> addInertiaHeaders cookie propsToGet (defaultArg requestedComponentName currentComponentName) currentId isSSETriggeredResponse isReloadAfterMount (Some cacheStorage) (Some cacheRetrieval) version
            |> Http.send
          
          let forceRefreshUrl = forceRefreshUrl response
          
          match forceRefreshUrl, response.statusCode with
          | Some url, _ -> return (Some url, None)
          | None, 200 | None, 404 | None, 403 ->
            match PageObj.fromJson response.responseText propsDecoder sharedDecoder with
            | Ok pageObj -> 

              //printfn $"propsToGet: {propsToGet}"
              
              let resolvedProps = 
                match pageObj.props with
                | Some props ->
                  // pass props through function to swap in
                  resolver (cacheMap, props)
                | None -> failwith "No props provided"
                            
              let resolvedPageObj = 
                { pageObj with
                    props = Some resolvedProps
                    reloadOnMount =
                      { pageObj.reloadOnMount with
                          propsToEval = Some propsToGet
                          shouldReload = defaultArg shouldReload pageObj.reloadOnMount.shouldReload
                          //cacheStorage = Some cacheStorage
                          //cacheRetrieval = Some cacheRetrieval
                      }
                }
              
              
              // handle caching to session storage here
              match resolvedPageObj.props, cacheStorage with 
              | Some props, StoreAll when shouldWriteCache ->
                let cacheToStore = toMap (Some fieldNames, props)
                //printfn $"cache: {cacheToStore}"
                cacheToStore 
                  |> Array.iter (fun (k,v) ->
                    let stringEncoded = Encode.Auto.toString v
                    let stripped = Regex.Replace(stringEncoded, "[@\[\]]", "")
                    let parts = stripped.Split(',')
                    let isError = parts |> Array.contains "\"Error\""
                    if not isError then
                      sessionStorage.setItem($"cache:{resolvedPageObj.``component``}:{k}", stringEncoded )
                    else
                      printfn $"skipping cache storage of async function \"{k}\" due to error in return value")
              | Some props, StoreToCache toSave when shouldWriteCache -> 
                //printfn $"resolved: {props}"
                //if shouldWriteToCache then printfn $"saving to cache: {toSave}"
                let fieldNameSet = Set.ofArray fieldNames
                let toSaveSet = Set.ofArray toSave
                let intersection = Set.intersect toSaveSet fieldNameSet
                if (intersection |> Set.isEmpty |> not) then
                  let intersectArr = intersection |> Set.toArray
                  let cacheToStore = toMap (Some intersectArr, props)
                  //printfn $"cache: {cacheToStore}"
                  cacheToStore 
                    |> Array.iter (fun (k,v) -> sessionStorage.setItem($"cache:{resolvedPageObj.``component``}:{k}", Encode.Auto.toString v ) )

              | _ -> ()
              
              return None, Some resolvedPageObj
            | Error err ->
              printfn $"error parsing JSON reply: {err}"
              return None, None
          | _ ->
            return None, None
      }

