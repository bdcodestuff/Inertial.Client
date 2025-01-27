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
open Thoth.Json
open Inertia
open Core

[<RequireQualifiedAccess>]
module Router =
  type NavigationData<'Props,'Shared> =
    {
      pathStore:Store<RouterLocation<'Props,'Shared>>
      method:Method
      url:string
      propsToEval:PropsToEval
      isForwardBack:bool
      propsDecoder: string -> string -> JsonValue -> Result<'Props option,DecoderError>
      sharedDecoder: string -> JsonValue -> Result<'Shared option,DecoderError>
      doFullReloadOnArrival:bool
      progress:ProgressBar
      scroll:ScrollPosition
      isPartialReload:bool
      isSSEResponse:bool
    }

  let createNavigation
      (pathStore:Store<RouterLocation<'Props,'Shared>>)
      (method:Method)
      url
      propsToEval
      isForwardBack
      propsDecoder
      sharedDecoder
      (doFullReloadOnArrival:bool)
      (progress:ProgressBar)
      (scroll:ScrollPosition)
      (isPartialReload:bool)
      (isSSEResponse:bool) =
        {
          pathStore = pathStore
          method = method
          url = url
          propsToEval = propsToEval
          isForwardBack = isForwardBack
          propsDecoder = propsDecoder
          sharedDecoder = sharedDecoder
          doFullReloadOnArrival = doFullReloadOnArrival
          progress = progress
          scroll = scroll
          isPartialReload = isPartialReload
          isSSEResponse = isSSEResponse 
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
          // connection id
          let currentId =
              match n.pathStore.Value.pageObj with
              | Some o -> o.connectionId
              | None -> ""
          // version
          let version =
              match n.pathStore.Value.pageObj with
              | Some o -> o.version
              | None -> ""
          
          let pageObjAsync =
            inertiaHttp
                n.url
                n.method
                n.propsToEval
                currentComponent
                currentId
                n.isSSEResponse
                n.isPartialReload
                version
                n.propsDecoder
                n.sharedDecoder
                
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
                Scroll.save a.pathname window.pageYOffset;

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
          onfulfilled=(
            fun () ->
              match n.progress with
              | ShowProgressBar when not n.isForwardBack -> NProgress.``done`` ()
              | _ -> ()
              // restore y-scroll position is it's stored in session storage
              match n.scroll with
              | ResetScroll -> ()
              | KeepVerticalScroll url -> window.scroll(0,(Scroll.restore url))
            )
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

  let doNav method =
    fun
      (pathStore: Store<RouterLocation<'Props,'Shared>>)
      url
      propsToGet
      propsDecoder
      sharedDecoder
      progress
       -> createNavigation
            pathStore
            method
            url
            propsToGet
            false
            propsDecoder
            sharedDecoder
            false
            progress
            ResetScroll
            false
  
  /// Triggers page to reload itself -- server side client interprets this as a partial data request and will send evaluate and send back any asynchronous page props that match in PropsToEval 
  
  
  
  let reload
    (propsDecoder : string -> Decoder<'Props>)
    (sharedDecoder : Decoder<'Shared>)
    (pathStore:Store<RouterLocation<'Props,'Shared>>)
    propsToEval
    progress
    isSSEResponse =
      createNavigation
        pathStore
        (Get [])
        $"{window.location.pathname}{window.location.search}"
        propsToEval
        false
        (fun name -> propsDecoder name |> Decode.map Some) // map to option
        (sharedDecoder |> Decode.map Some) // map to option
        false
        progress
        ResetScroll
        true
        isSSEResponse
      |> navigateTo
      

  /// Triggers loading of a new page client-side
  let link
    (defaultApply: seq<SutilElement>) // these are default Sutil elements to include in every link
    (propsDecoder: string -> Decoder<'Props>)
    (sharedDecoder: Decoder<'Shared>)
    (pathStore: Store<RouterLocation<'Props,'Shared>>)
    (method: Method)
    href
    propsToGet
    (scroll:ScrollPosition)
    progress
    (apply: seq<SutilElement>)
     =
      Html.a (defaultApply
              |> Seq.append apply
              |> Seq.append [
                              EngineHelpers.Attr.href href
                              onClick (fun e -> e.preventDefault()
                                                createNavigation
                                                  pathStore
                                                  method
                                                  href
                                                  propsToGet
                                                  false
                                                  (fun name -> propsDecoder name |> Decode.map Some)
                                                  (sharedDecoder |> Decode.map Some)
                                                  false
                                                  progress
                                                  scroll
                                                  false
                                                  false
                                                |> navigateTo
                            ) []])
  

  
  /// Triggers client-side POST request
  let post pathStore propsDecoder sharedDecoder url data propsToGet progress =
    doNav (Post data) pathStore url propsToGet (fun name -> propsDecoder name |> Decode.map Some) (sharedDecoder |> Decode.map Some) progress false |> navigateTo
  
  /// Triggers client-side PUT request
  let put pathStore propsDecoder sharedDecoder url data propsToGet progress = doNav (Put data) pathStore url propsToGet (fun name -> propsDecoder name |> Decode.map Some) (sharedDecoder |> Decode.map Some) progress false |> navigateTo
  
  /// Triggers client-side PATCH request
  let patch pathStore propsDecoder sharedDecoder url data propsToGet progress = doNav (Patch data) pathStore url propsToGet (fun name -> propsDecoder name |> Decode.map Some) (sharedDecoder |> Decode.map Some) progress false |> navigateTo
  
  /// Triggers client-side DELETE request
  let delete pathStore propsDecoder sharedDecoder url propsToGet progress = doNav Delete pathStore url propsToGet (fun name -> propsDecoder name |> Decode.map Some) (sharedDecoder |> Decode.map Some) progress false |> navigateTo

  /// Instantiate a router using Sutil store to trigger reactive responses on any change
  let createRouterStore<'Props,'Shared>() =
    Store.make(
        {
          pathname = window.location.pathname
          query = window.location.search
          pageObj = Some <| PageObj<'Props,'Shared>.emptyObj window.location.pathname
          allowPartialReload = true
          cancellationTokenSource = new CancellationTokenSource()
        })

  // If a full page reload occurs the PageObj must be extracted from the body data-page attribute
  let initialPageObjAttr () = document.getElementById("sutil-app").getAttribute("data-page")
  // Parse PageObj json string using Props and Shared Thoth decoders
  let initialPageObj propsDecoder sharedDecoder =
    match PageObj.fromJson (initialPageObjAttr ()) propsDecoder sharedDecoder with
    | Ok p ->
      Some p
    | Error e ->
      printfn $"{e}"
      None

  let renderRouter<'Props,'Shared>
    (router: Store<RouterLocation<'Props,'Shared>>)
    //(sharedUserPredicateCheck: 'Shared option -> string array -> bool)
    (signedInUserId: 'Shared option -> string option)
    (urlToComponent:
      string list -> // url parts
        'Props -> // Page props
          'Shared ->
            Option<SutilElement -> 'Props -> 'Shared -> SutilElement> -> // optional layout function
              SutilElement)
    (propsDecoder: string -> Decoder<'Props>)
    (sharedDecoder: Decoder<'Shared>)
    (layout: Option<SutilElement -> 'Props -> 'Shared -> SutilElement>)
    (sseEndpointUrl : string option) =
      // configure the progress bar library
      NProgress.configure {| showSpinner = false |}
      
      let propsDecoderOpt = fun name -> propsDecoder name |> Decode.map Some
      let sharedDecoderOpt = sharedDecoder |> Decode.map Some
      
      // get initial pageObj
      let initial = initialPageObj propsDecoderOpt sharedDecoderOpt

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

      let shouldListenOnSSE = // reloadOnSSE router.Value.pageObj
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
                     (RxSSE.ssePartialReload reload propsDecoder sharedDecoder router ProgressBar.ShowProgressBar)
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
          router
          (Get [])
          $"{window.location.pathname}{window.location.search}"
          EvalAllProps
          true
          propsDecoderOpt
          sharedDecoderOpt
          shouldRefreshOnBack
          HideProgressBar
          (KeepVerticalScroll $"{window.location.pathname}{window.location.search}")
          false
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
              if (location.allowPartialReload && obj.reloadOnMount.shouldReload) then
                match obj.reloadOnMount.propsToEval with
                | Some withProps -> 
                    reload propsDecoder sharedDecoder router withProps HideProgressBar false
                | _ -> ()
              
            | None -> ()
            
            // parse url and find matching handler with layout if appropriate
            match location.pageObj with
            | Some obj ->
              match obj.props, obj.shared with
              | Some props, Some shared -> 
                (urlToComponent (getCurrentUrl location) props shared layout )
              | _ -> text "Error loading page data, please refresh"
            | _ -> text "Error loading page data, please refresh"))
        
        disposeOnUnmount [router] // free resources
      ]

