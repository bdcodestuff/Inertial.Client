namespace Inertial.Client

open System
open System.Text.RegularExpressions
open Fable.SimpleHttp
open Inertial.Client.Core
open Microsoft.FSharp.Control
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
        | Skip ->
          withCurrentId
        | EagerOnly props when props.Length > 0 ->
          // EagerOnly triggers immediate server-side evaluation
          let propList = props |> Array.reduce (fun x y -> $"{x}, {y}")
          withCurrentId
          |> Http.header (Headers.create "X-Inertial-Partial-Component" currentComponentName)
          |> Http.header (Headers.create "X-Inertial-Partial-Data" propList)
        | Lazy _ | EagerOnly _ ->
          // Lazy should NOT send partial data headers - let server return Pending
          // and let reloadOnMount trigger the actual evaluation after page renders.
          // Empty EagerOnly also treated like Skip.
          withCurrentId     

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

  /// Extract URL path without query parameters for use as cache key
  let private urlPathForCache (url: string) =
    url.Split('?').[0]

  let getCacheForComponent (componentName:string option) (url:string option) (allFieldNames:string array) =
    //printfn $"Debug -- comp: {componentName}, url: {url}, allFields: {allFieldNames}"
    match componentName, url with
    | Some _, Some currentUrl ->
      let urlPath = urlPathForCache currentUrl
      allFieldNames
      |> Array.map (fun item ->
        let stored = sessionStorage.getItem $"cache:{urlPath}:{item}"
        if stored <> null && stored <> "" then
          //let decoded = Decode.fromString cacheResultDecoder stored
          let decoded = decodeCacheFromString stored
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
    | _, None ->
      printfn "Could not find URL for cache lookup"
      Map.empty
    | None, _ ->
      printfn "Could not find component in map"
      Map.empty
  
  let resolveReEvals (cacheMap: Map<string,obj>) (requestedFromCache:string array) (requestedForReEval:string array) =
    // these are requested from cache and present in cache
    let foundInCacheOkToOmitFromReEval = requestedFromCache |> Array.filter cacheMap.ContainsKey
    // remove keys from http request
    requestedForReEval |> Array.except foundInCacheOkToOmitFromReEval
    
  let missingFromCacheMap (cacheMap:Map<string,obj>) (allFieldNames: string array) =
    let cacheMapSet = cacheMap |> Map.toArray |> Array.map fst |> Set.ofArray
    let allFieldsSet = allFieldNames |> Set.ofArray
    Set.difference allFieldsSet cacheMapSet |> Set.toArray
    
  let resolveNextPropsToEval isReloadOnMount cacheMap toGetFromCache (toReEval:PropsToEval) allFieldNames =
    let missingFromCache = missingFromCacheMap cacheMap allFieldNames

    let toReEvalArr, shouldReload =
      match toReEval with
      | Lazy [||] -> allFieldNames, true // empty Lazy means reload ALL deferred fields
      | Lazy a -> a, true // skip initial evaluation, but do a reload for specified fields
      | Skip when missingFromCache |> Array.isEmpty -> [||], false
      | Skip when missingFromCache = allFieldNames -> allFieldNames, false
      | Skip -> missingFromCache, false
      | Eager -> allFieldNames, false
      | EagerOnly a -> a, false
      
    let nextReEvals = resolveReEvals cacheMap toGetFromCache toReEvalArr
    //printfn $"Debug -- next re-evals: {nextReEvals}"

    let reEvalAllFields toReEval =
      let toReEvalSet = Set.ofArray toReEval
      let allFieldNameSet = Set.ofArray allFieldNames
      Set.isSubset toReEvalSet allFieldNameSet && Set.isSuperset toReEvalSet allFieldNameSet

    match nextReEvals, shouldReload with
    | [||], _ -> Skip, false
    | a, true -> Lazy a, false // shouldReload=true means original was Lazy, preserve it for client-side reload
    | a, false when reEvalAllFields a -> Eager, true
    | a, false -> EagerOnly a, true // any other EagerOnly request we store to cache
  
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
    (toFieldNames: 'Props -> string array)
    (toMap: option<string array> * 'Props -> array<string*obj>)
    (resolver: Map<string,obj> * 'Props -> 'Props)
    (requestedComponentName:string option)

    : Async<String option * PageObj<'Props,'Shared> option> =
      let cookie = JsCookie.get "XSRF-TOKEN"
      let dataMap = httpVerb.ToDataMap()
      let includeData = not dataMap.IsEmpty && httpVerb <> Delete
      let addData (input:HttpRequest) =
        if includeData then
          input
          |> Http.content (encodeBodyContent dataMap |> BodyContent.Text)
        else
          input

      // Initial request without cache-aware headers (we'll get field names from decoded props)
      async {
          let! response =
            Http.request url
            |> Http.method (httpVerb.ToMethodHttp())
            |> addData
            |> addInertiaHeaders cookie propsToEval (defaultArg requestedComponentName currentComponentName) currentId isSSETriggeredResponse isReloadAfterMount (Some cacheStorage) (Some cacheRetrieval) version
            |> Http.send

          let forceRefreshUrl = forceRefreshUrl response

          match forceRefreshUrl, response.statusCode with
          | Some url, _ -> return (Some url, None)
          | None, 200 | None, 404 | None, 403 ->
            // Clear any previous error on successful response
            Core.clearHttpError ()
            match PageObj.fromJson response.responseText propsDecoder sharedDecoder with
            | Ok pageObj ->

              // Now that we have props, we can get field names dynamically
              let fieldNames =
                match pageObj.props with
                | Some props -> toFieldNames props
                | None -> [||]

              // Get cache after we know the field names (use URL for cache key)
              let cacheMap = getCacheForComponent requestedComponentName (Some url) fieldNames

              // Determine cache behavior
              let propsToGet, shouldWriteCache, newCache =
                match cacheRetrieval with
                | CheckForCached toGet ->
                  let nextProps, shouldWrite = resolveNextPropsToEval isReloadAfterMount cacheMap toGet propsToEval fieldNames
                  nextProps, shouldWrite, (cacheMap |> Map.filter (fun k v -> toGet |> Array.contains k))
                | CheckForAll ->
                  let nextProps, shouldWrite = resolveNextPropsToEval isReloadAfterMount cacheMap fieldNames propsToEval fieldNames
                  nextProps, shouldWrite, cacheMap
                | SkipCache ->
                  let nextProps, shouldWrite = resolveNextPropsToEval isReloadAfterMount cacheMap [||] propsToEval fieldNames
                  let filteredCache =
                    match nextProps with
                    | Eager | Skip -> Map.empty
                    | EagerOnly toGet | Lazy toGet -> (cacheMap |> Map.filter (fun k v -> toGet |> Array.contains k |> not))
                  nextProps, shouldWrite, filteredCache

              let resolvedProps =
                match pageObj.props with
                | Some props ->
                  // pass props through function to swap in cached values
                  let resolved = resolver (newCache, props)
                  resolved
                | None -> failwith "No props provided"

              // Determine final propsToEval for reloadOnMount:
              // - If server explicitly set Lazy (for deferred loading pattern), preserve it
              // - Otherwise use client's calculated propsToGet
              let finalPropsToEval =
                match pageObj.reloadOnMount.propsToEval with
                | Lazy _ -> pageObj.reloadOnMount.propsToEval  // Preserve server's Lazy setting
                | _ -> propsToGet  // Use client's calculation for other cases

              let resolvedPageObj =
                { pageObj with
                    props = Some resolvedProps
                    reloadOnMount =
                      { pageObj.reloadOnMount with
                          propsToEval = finalPropsToEval
                      }
                }

              // handle caching to session storage here (use URL path for cache key)
              let cacheUrlPath = urlPathForCache resolvedPageObj.url
              match resolvedPageObj.props, cacheStorage with
              | Some props, StoreAll when shouldWriteCache ->
                let cacheToStore = toMap (Some fieldNames, props)
                cacheToStore
                  |> Array.iter (fun (k,v) ->
                    // Use needsCacheReplacement which handles both Deferred and legacy AsyncData
                    let needsReplacement, stringEncoded = needsCacheReplacement v
                    if not needsReplacement then
                      sessionStorage.setItem($"cache:{cacheUrlPath}:{k}", stringEncoded )
                    else
                      printfn $"skipping cache storage of async function \"{k}\" due to pending/error state")
              | Some props, StoreToCache toSave when shouldWriteCache ->
                let fieldNameSet = Set.ofArray fieldNames
                let toSaveSet = Set.ofArray toSave
                let intersection = Set.intersect toSaveSet fieldNameSet
                if (intersection |> Set.isEmpty |> not) then
                  let intersectArr = intersection |> Set.toArray
                  let cacheToStore = toMap (Some intersectArr, props)
                  cacheToStore
                    |> Array.iter (fun (k,v) ->
                      sessionStorage.setItem($"cache:{cacheUrlPath}:{k}", encodeCacheObj v ) )

              | _ -> ()

              return None, Some resolvedPageObj
            | Error err ->
              printfn $"error parsing JSON reply: {err}"
              // Capture JSON parsing error
              Core.setHttpError {
                StatusCode = response.statusCode
                StatusText = "JSON Parse Error"
                ResponseBody = $"Failed to parse response: {err}\n\nRaw response:\n{response.responseText}"
                Url = url
                Method = httpVerb.ToMethodHttp().ToString()
                Timestamp = System.DateTime.Now
              }
              return None, None
          | _ ->
            // Capture HTTP error for non-success status codes (e.g., 500, 502, etc.)
            let methodStr = httpVerb.ToMethodHttp().ToString()
            printfn $"HTTP error {response.statusCode} for {methodStr} {url}"
            Core.setHttpError {
              StatusCode = response.statusCode
              StatusText = $"HTTP {response.statusCode}"
              ResponseBody = response.responseText
              Url = url
              Method = methodStr
              Timestamp = System.DateTime.Now
            }
            return None, None
      }

