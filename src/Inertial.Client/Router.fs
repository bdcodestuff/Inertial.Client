namespace Inertial.Client

open System.Threading
open Browser.Types
open Inertial.Client.Core
open Microsoft.FSharp.Control
open FSharp.Control
open Sutil
open Sutil.Core
open Sutil.CoreElements
open Browser
open Fable.Core
open Inertia
open Core
open Inertial.Lib
open Thoth.Json

[<RequireQualifiedAccess>]
module Router =

  /// Configuration record that holds all the functions needed by the router.
  /// This replaces the need for static members on Props and Shared types.
  type RouterConfig<'Props, 'Shared> =
    {
      /// Decoder function that takes a component name and returns a decoder for Props
      propsDecoder: string -> Decoder<'Props>
      /// Decoder for Shared data
      sharedDecoder: Decoder<'Shared>
      /// Extract field values from a Props value for caching
      toMap: option<string array> * 'Props -> array<string * obj>
      /// Get all Deferred/async field names from a Props value
      toFields: 'Props -> string array
      /// Resolve cached values into a Props value
      resolver: Map<string, obj> * 'Props -> 'Props
      /// Get the current user ID from Shared data (for SSE filtering)
      currentUserId: 'Shared option -> string option
    }

  /// Create a RouterConfig with automatic DU-aware implementations for toMap, toFields, and resolver
  /// IMPORTANT: The inline functions must be passed as lambdas at the call site to preserve Fable type info
  let inline createConfig
    (propsDecoder: string -> Decoder<'Props>)
    (sharedDecoder: Decoder<'Shared>)
    (currentUserId: 'Shared option -> string option)
    : RouterConfig<'Props, 'Shared> =
    {
      propsDecoder = propsDecoder
      sharedDecoder = sharedDecoder
      toMap = fun (filterTo, props) -> FableReflection.extractFieldsFromDU filterTo props
      toFields = fun props -> FableReflection.findDeferredFieldsFromDU props
      resolver = fun (cache, props) -> FableReflection.resolveFieldsInDU cache props
      currentUserId = currentUserId
    }

  /// Create a RouterConfig from a decoder map - the simplest API
  /// Usage: createConfigFromMap decoderMap sharedDecoder currentUserId
  let inline createConfigFromMap
    (decoderMap: Map<string, Decoder<'Props>>)
    (sharedDecoder: Decoder<'Shared>)
    (currentUserId: 'Shared option -> string option)
    : RouterConfig<'Props, 'Shared> =
    createConfig (FableReflection.createPropsDecoder decoderMap) sharedDecoder currentUserId

  /// Create a RouterConfig from a decoder map without Shared data.
  /// Use this when you don't need shared data across pages.
  /// Usage: createConfigWithoutShared decoderMap
  let inline createConfigWithoutShared
    (decoderMap: Map<string, Decoder<'Props>>)
    : RouterConfig<'Props, EmptyShared> =
    createConfig
      (FableReflection.createPropsDecoder decoderMap)
      EmptyShared.decoder
      EmptyShared.currentUserId

  type NavigationData<'Props,'Shared> =
    {
      pathStore:Store<RouterLocation<'Props,'Shared>>
      config: RouterConfig<'Props,'Shared>
      method:Method
      url:string
      propsToEval:PropsToEval
      isForwardBack:bool
      doFullReloadOnArrival:bool
      progress:ProgressBar
      scroll:ScrollPosition
      isPartialReload:bool
      isSSEResponse:bool
      cacheStorage: CacheStorage
      cacheRetrieval: CacheRetrieval
    }

  let createNavigation
      (config: RouterConfig<'Props,'Shared>)
      (pathStore:Store<RouterLocation<'Props,'Shared>>)
      (method:Method)
      url
      propsToEval
      isForwardBack
      (doFullReloadOnArrival:bool)
      (progress:ProgressBar)
      (scroll:ScrollPosition)
      (isPartialReload:bool)
      cacheStorage
      cacheRetrieval
      (isSSEResponse:bool)
       =
        {
          pathStore = pathStore
          config = config
          method = method
          url = url
          propsToEval = propsToEval
          isForwardBack = isForwardBack
          doFullReloadOnArrival = doFullReloadOnArrival
          progress = progress
          scroll = scroll
          isPartialReload = isPartialReload
          isSSEResponse = isSSEResponse
          cacheStorage = cacheStorage
          cacheRetrieval = cacheRetrieval
        }
   
  // function to parse browser location url into string parts
  let getCurrentUrl (router: RouterLocation<'Props,'Shared>) =
    let route =
      router.pathname.TrimStart('/').TrimEnd('/')

    if not (router.query = "") then
      splitRoute route @ [ router.query.TrimStart('?') ]
    else
      splitRoute route

  /// triggers a partial page load and reads JSON server response
  let navigateTo (n:NavigationData<'Props,'Shared>) =
      let inner =
        promise {
          match n.progress with
          | ShowProgressBar -> NProgress.start ()
          | HideProgressBar -> ()
          // component name
          let currentComponent =
            match n.pathStore.Value.pageObj with
            | Some o -> o.``component``
            | None -> ""
          // requested component name
          let requestedComponentName =
            match n.pathStore.Value.pageObj with
            | Some p' ->
              p'.urlComponentMap
              |> Array.tryFind (fun (path, _) -> path = n.url)
              |> Option.map snd
            | None -> None

          // connection id
          let currentId =
              match n.pathStore.Value.pageObj with
              | Some o ->o.connectionId
              | None -> None
          // version
          let version =
              match n.pathStore.Value.pageObj with
              | Some o -> o.version
              | None -> ""

          // Create decoder functions from config
          let propsDecoderOpt = fun name -> Helpers.mapDecoderToOpt (n.config.propsDecoder name)
          let sharedDecoderOpt = Helpers.mapDecoderToOpt n.config.sharedDecoder

          let pageObjAsync =
            inertiaHttp
                n.url
                n.method
                n.propsToEval
                n.cacheRetrieval
                n.cacheStorage
                currentComponent
                currentId
                n.isSSEResponse
                n.isPartialReload
                version
                propsDecoderOpt
                sharedDecoderOpt
                n.config.toFields
                n.config.toMap
                n.config.resolver
                requestedComponentName

          // asynchronously converts inbound JSON to domain PageObj record type
          let! forceRefreshUrl, pageObj =
            Async.StartAsPromise(pageObjAsync,n.pathStore.Value.cancellationTokenSource.Token)

          match forceRefreshUrl with
          | Some url ->
            // if force refresh requested trigger it here
            window.location.href <- url
          | None ->

            // inertia can pass in an optional url; if present it should override the browser url
            let inertiaUrl =
              match pageObj with
              | Some p when p.url <> n.url ->
                p.url
              | _ -> n.url

            // update browser history stack
            // but ignore if user pressed forward or back buttons, or if the desired url hasn't changed
            if not n.isForwardBack && $"{window.location.pathname}{window.location.search}" <> inertiaUrl then
              window.history.pushState((), "", inertiaUrl)
              window.history.scrollRestoration <- ScrollRestoration.Manual

            // modify router location store
            n.pathStore
              |> Store.modify (fun a ->
                // cancel any requests still in progress
                a.cancellationTokenSource.Cancel()
                // dispose the token source
                a.cancellationTokenSource.Dispose()

                // save scroll location of outgoing page on each router location change to enable restoring it later if requested
                match pageObj with
                | Some p ->
                  Scroll.save p.``component`` window.pageYOffset
                | None -> ()

                {a with
                  pathname = inertiaUrl
                  query = window.location.search
                  pageObj = pageObj
                  allowPartialReload = not n.isPartialReload // we need this to prevent infinite partial reloads
                  cancellationTokenSource = new CancellationTokenSource()
                } )

            // do a page reload if back/forward navigation and page implements this behavior
            if n.isForwardBack && n.doFullReloadOnArrival then window.location.reload()

        }

      // call the promise
      inner
        // after promise completes deal with post-processing
        .``then``(
          onfulfilled=
            fun () ->
              match n.progress with
              | ShowProgressBar when not n.isForwardBack -> NProgress.``done`` ()
              | _ -> ()
              // restore y-scroll position is it's stored in session storage
              match n.scroll with
              | ResetScroll -> ()
              | KeepVerticalScroll url -> window.scroll(0,Scroll.restore url)
          )
        .catch(
          // catch errors (most commonly this fires because a pending page request was cancelled by the user making a newer, superseding page load request)
          fun err ->

            match n.progress with
            | ShowProgressBar when not n.isForwardBack -> NProgress.``done`` ()
            | _ -> ()

            let toPrint =
              match string err with
              | "Error: The operation was canceled" -> "A new http request was requested before a prior pending request could be completed"
              | err -> err

            printfn $"{toPrint}")
        |> Promise.start

  let doNav
    (config: RouterConfig<'Props,'Shared>)
    method
    (pathStore: Store<RouterLocation<'Props,'Shared>>)
    url
    propsToGet
    progress
    cacheStorage
    cacheRetrieval
     = createNavigation
          config
          pathStore
          method
          url
          propsToGet
          false
          false
          progress
          ResetScroll
          false
          cacheStorage
          cacheRetrieval

  /// Triggers page to reload itself -- server side client interprets this as a partial data request and will send evaluate and send back any asynchronous page props that match in PropsToEval
  let reload
    (config: RouterConfig<'Props,'Shared>)
    (pathStore:Store<RouterLocation<'Props,'Shared>>)
    propsToEval
    progress
    cacheStorage
    cacheRetrieval
    isSSEResponse
     =
      createNavigation
        config
        pathStore
        (Get [])
        $"{window.location.pathname}{window.location.search}"
        propsToEval
        false
        false
        progress
        ResetScroll
        true
        cacheStorage
        cacheRetrieval
        isSSEResponse

      |> navigateTo


  /// Triggers loading of a new page client-side
  let link
    (config: RouterConfig<'Props,'Shared>)
    (defaultApply: seq<SutilElement>) // these are default Sutil elements to include in every link
    (pathStore: Store<RouterLocation<'Props,'Shared>>)
    (method: Method)
    href
    propsToGet
    (scroll:ScrollPosition)
    progress
    cacheStorage
    cacheRetrieval
    (apply: seq<SutilElement>)
     =
      Html.a (defaultApply
              |> Seq.append apply
              |> Seq.append [
                              EngineHelpers.Attr.href href
                              onClick (fun e -> e.preventDefault()
                                                createNavigation
                                                  config
                                                  pathStore
                                                  method
                                                  href
                                                  propsToGet
                                                  false
                                                  false
                                                  progress
                                                  scroll
                                                  false
                                                  cacheStorage
                                                  cacheRetrieval
                                                  false
                                                |> navigateTo
                            ) []])



  /// Triggers client-side POST request
  let post config pathStore url data propsToGet progress cacheStorage cacheRetrieval =
    doNav config (Post data) pathStore url propsToGet progress cacheStorage cacheRetrieval false |> navigateTo

  /// Triggers client-side PUT request
  let put config pathStore url data propsToGet progress cacheStorage cacheRetrieval =
    doNav config (Put data) pathStore url propsToGet progress cacheStorage cacheRetrieval false |> navigateTo

  /// Triggers client-side PATCH request
  let patch config pathStore url data propsToGet progress cacheStorage cacheRetrieval =
    doNav config (Patch data) pathStore url propsToGet progress cacheStorage cacheRetrieval false |> navigateTo

  /// Triggers client-side DELETE request
  let delete config pathStore url propsToGet progress cacheStorage cacheRetrieval =
    doNav config Delete pathStore url propsToGet progress cacheStorage cacheRetrieval false |> navigateTo

  /// Client facing - creates a partially applied Link function with config and router bound
  let Link
    (config: RouterConfig<'Props,'Shared>)
    defaultApply
    router
    : Method -> string -> PropsToEval -> ScrollPosition -> ProgressBar -> CacheStorage -> CacheRetrieval -> SutilElement seq -> SutilElement =
      link config defaultApply router

  /// Client facing - creates a partially applied Reload function with config and router bound
  let Reload
    (config: RouterConfig<'Props,'Shared>)
    router = reload config router

  /// Client facing - creates a partially applied Post function with config and router bound
  let Post
    (config: RouterConfig<'Props,'Shared>)
    router
    : string -> List<string * obj> -> PropsToEval -> ProgressBar -> CacheStorage -> CacheRetrieval -> unit
    = post config router

  let Put config router = put config router

  let Patch config router = patch config router

  let Delete config router = delete config router
  
  /// Instantiate a router using Sutil store to trigger reactive responses on any change
  let createRouterStore<'Props,'Shared>() =
    Store.make
        {
          pathname = window.location.pathname
          query = window.location.search
          pageObj = Some <| PageObj<'Props,'Shared>.emptyObj window.location.pathname
          allowPartialReload = true
          cancellationTokenSource = new CancellationTokenSource()
        }

  // If a full page reload occurs the PageObj must be extracted from the body data-page attribute
  let initialPageObjAttr () = document.getElementById("sutil-app").getAttribute("data-page")

  // Parse PageObj json string using Props and Shared Thoth decoders
  let initialPageObj (config: RouterConfig<'Props,'Shared>) =
    let propsDecoderOpt = fun name -> Helpers.mapDecoderToOpt (config.propsDecoder name)
    let sharedDecoderOpt = Helpers.mapDecoderToOpt config.sharedDecoder

    match PageObj.fromJson (initialPageObjAttr ()) propsDecoderOpt sharedDecoderOpt with
    | Ok p ->
      Some p
    | Error e ->
      printfn $"{e}"
      None

  let renderRouter
    (config: RouterConfig<'Props,'Shared>)
    (router: Store<RouterLocation<'Props,'Shared>>)
    (urlToComponent:
      string list -> // url parts
        'Props -> // Page props
          'Shared ->
            Option<SutilElement -> 'Props -> 'Shared -> SutilElement> -> // optional layout function
              SutilElement)
    (layout: Option<SutilElement -> 'Props -> 'Shared -> SutilElement>)
    (sseEndpointUrl : string option) =

      let signedInUserId = config.currentUserId

      // configure the progress bar library
      NProgress.configure {| showSpinner = false |}

      // get initial pageObj
      let initial = initialPageObj config

      // inertia passes in an url that we use to override the browser url
      let inertiaUrl =
        match initial with
        | Some p when p.url <> $"{window.location.pathname}{window.location.search}" ->
          p.url
        | _ ->
          $"{window.location.pathname}{window.location.search}"

      // add current url to browser history stack
      window.history.pushState((), "", inertiaUrl)

      // add initial page object to router location store
      // this occurs with full page load or refresh
      router
          |> Store.modify (fun a ->

            {a with
              pathname = inertiaUrl
              query = window.location.search
              pageObj = initial} )

      let shouldListenOnSSE =
        match router.Value.pageObj with
        | Some p' -> p'.realTime
        | None -> true

      // determine if this sutil component should listen on the sse endpoint
      if shouldListenOnSSE then
        let url = defaultArg sseEndpointUrl "/sse" // defaults to "/sse" if url not specified
        // define endpoint
        let eventSource = EventSource.Create(url)
        // make an observable stream of SSE values
        // ignore the first event, this represents the last "leftover" event
        // filter to only those meeting the predicate
        // delay the processing of each event by 1s plus the relative time interval between arrival of events
        let observable =
          RxSSE.ofEventSource<Result<InertialSSEEvent,string>> eventSource
          |> AsyncRx.skip 1
          |> AsyncRx.filter (RxSSE.eventPredicates router signedInUserId)
          |> AsyncRx.delay 1000
          |> AsyncRx.distinctUntilChanged

        // define subscription
        let main = async {
          // trigger the ssePartialReload for each stream element being subscribed to
          let! _ = observable.SubscribeAsync
                     (RxSSE.ssePartialReload (reload config) router ShowProgressBar)
          return ()
        }
        // subscribe!
        Async.StartImmediate main

      // function to determine if the component refreshes itself when it is rendered via forward/back request (prevents stale data from showing due to browser caching)
      let shouldRefreshOnBack =
        match router.Value.pageObj with
        | Some p' ->
            if p'.refreshOnBack then
                match p'.shared with
                | Some _ -> true
                | None -> false
            else
                false
        | None -> false

      // add event listener to navigate on back/forward
      window.addEventListener("popstate", fun _ ->
        createNavigation
          config
          router
          (Get [])
          $"{window.location.pathname}{window.location.search}"
          Eager
          true
          shouldRefreshOnBack
          HideProgressBar
          (KeepVerticalScroll $"{window.location.pathname}{window.location.search}")
          false
          CacheStorage.StoreNone
          CacheRetrieval.SkipCache
          false
        |> navigateTo )

      // render matching SutilElement
      fragment [

        Bind.el (router, (fun location ->

            match location.pageObj with
            | Some obj ->
              document.title <- obj.title // set page title here

              // handle reload on first mount here
              // location.allowPartialReload is a boolean flag that flips in response to whether the incoming request is itself a partial or full page request
              // it prevents infinite reload loops
              // the obj.reloadOnMount.shouldReload is a boolean flag set on the server side that specifies if the component is intended to reload on mount or not

              // DEBUG: trace reloadOnMount flow
              Browser.Dom.console.log($"[reloadOnMount] component={obj.``component``}, allowPartialReload={location.allowPartialReload}")
              Browser.Dom.console.log($"[reloadOnMount] propsToEval={obj.reloadOnMount.propsToEval}")
              Browser.Dom.console.log($"[reloadOnMount] cacheRetrieval={obj.reloadOnMount.cacheRetrieval}")

              if location.allowPartialReload then
                match obj.reloadOnMount.propsToEval with
                | Lazy a ->
                  // Check if cache should be consulted before reloading
                  match obj.reloadOnMount.cacheRetrieval with
                  | CheckForAll | CheckForCached _ ->
                    // Check cache for requested fields BEFORE triggering HTTP request
                    let cacheMap = Inertia.getCacheForComponent (Some obj.``component``) a
                    let cachedFields = cacheMap |> Map.toArray |> Array.map fst
                    let missingFields = a |> Array.filter (fun f -> not (cacheMap.ContainsKey f))
                    Browser.Dom.console.log($"[reloadOnMount] Checking cache - cached: {cachedFields}, missing: {missingFields}")

                    if missingFields.Length = 0 then
                      // All requested fields are in cache - apply cached values to props and update store
                      Browser.Dom.console.log("[reloadOnMount] All data found in cache, applying cached values")
                      match obj.props with
                      | Some props ->
                        // Use the resolver to apply cached values to props
                        let resolvedProps = config.resolver (cacheMap, props)
                        // Create updated pageObj with resolved props and clear propsToEval to prevent re-triggering
                        let updatedPageObj =
                          { obj with
                              props = Some resolvedProps
                              reloadOnMount = { obj.reloadOnMount with propsToEval = Eager }
                          }
                        // Update the router store with resolved data
                        router |> Store.modify (fun loc ->
                          { loc with
                              pageObj = Some updatedPageObj
                              allowPartialReload = false  // Prevent infinite reload loops
                          })
                        Browser.Dom.console.log("[reloadOnMount] Cache applied, store updated")
                      | None ->
                        Browser.Dom.console.log("[reloadOnMount] No props to resolve")
                    else
                      // Some fields missing - reload only the missing ones
                      Browser.Dom.console.log($"[reloadOnMount] Triggering reload for missing props: {missingFields}")
                      reload config router (EagerOnly missingFields) HideProgressBar obj.reloadOnMount.cacheStorage obj.reloadOnMount.cacheRetrieval false
                  | SkipCache ->
                    // Don't check cache, always reload
                    Browser.Dom.console.log($"[reloadOnMount] SkipCache - Triggering reload for Lazy props: {a}")
                    reload config router (EagerOnly a) HideProgressBar obj.reloadOnMount.cacheStorage obj.reloadOnMount.cacheRetrieval false
                | _ ->
                  Browser.Dom.console.log("[reloadOnMount] propsToEval is not Lazy, skipping reload")

            | None -> ()

            // parse url and find matching handler with layout if appropriate
            match location.pageObj with
            | Some obj ->
              match obj.props, obj.shared with
              | Some props, Some shared -> urlToComponent (getCurrentUrl location) props shared layout
              | _ -> text "Error loading page data, please refresh"
            | _ -> text "Error loading page data, please refresh"))

        disposeOnUnmount [router] // free resources
      ]

