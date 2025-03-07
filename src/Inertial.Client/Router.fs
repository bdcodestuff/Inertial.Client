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
open Inertial.Lib.Types

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
      toMap: option<string array> * 'Props -> array<string * obj>
      toFields: string -> string array
      resolver: Map<string,obj> * 'Props -> 'Props
      cacheStorage: CacheStorage
      cacheRetrieval: CacheRetrieval
    }

  let inline createNavigation<'Props,'Shared 
    when 'Props: (static member decoder: string -> Decoder<'Props>) 
    and 'Props: (static member toMap: (array<string> option * 'Props) -> array<string*obj>)
    and 'Props: (static member toFields: string -> string array)
    and 'Props: (static member resolver: (Map<string,obj>  * 'Props) -> 'Props) 
    and 'Shared: (static member decoder: Decoder<'Shared>) 
    and 'Shared: (static member currentUserId: 'Shared option -> string option)>
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

    // create a new navigation data record
    // this is used to pass data to the navigateTo function
        let propsDecoderOpt = fun name -> 'Props.decoder name |> Decode.map Some
        let sharedDecoderOpt = 'Shared.decoder |> Decode.map Some
        {
          pathStore = pathStore
          method = method
          url = url
          propsToEval = propsToEval
          isForwardBack = isForwardBack
          propsDecoder = propsDecoderOpt
          sharedDecoder = sharedDecoderOpt
          doFullReloadOnArrival = doFullReloadOnArrival
          progress = progress
          scroll = scroll
          isPartialReload = isPartialReload
          isSSEResponse = isSSEResponse
          //cacheMap = cacheMap
          toMap = 'Props.toMap
          toFields = 'Props.toFields 
          resolver = 'Props.resolver
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

  //when 'Props: (static member decoder: string -> Decoder<'Props>) and 'Props: (static member toMap: option<string array> * 'Props -> Map<string,obj>) and 'Props: (static member resolver: Map<string,obj> * 'Props -> 'Props) and 'Shared: (static member decoder: Decoder<'Shared>)

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
                n.propsDecoder
                n.sharedDecoder
                n.toFields
                //n.cacheMap
                n.toMap
                n.resolver
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

  let inline doNav<
    'Props ,'Shared 
      when 'Props: (static member decoder: string -> Decoder<'Props>) 
      and 'Props: (static member toMap: (array<string> option * 'Props) -> array<string*obj>)
      and 'Props: (static member toFields: string -> string array )
      and 'Props: (static member resolver: (Map<string,obj>  * 'Props) -> 'Props) 
      and 'Shared: (static member decoder: Decoder<'Shared>) 
      and 'Shared: (static member currentUserId: 'Shared option -> string option)
  > method =
    fun
      (pathStore: Store<RouterLocation<'Props,'Shared>>)
      url
      propsToGet
      progress
      cacheStorage
      cacheRetrieval
       -> createNavigation
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
  
  
  
  let inline reload<
    'Props ,'Shared 
      when 'Props: (static member decoder: string -> Decoder<'Props>) 
      and 'Props: (static member toMap: (array<string> option * 'Props) -> array<string*obj>)
      and 'Props: (static member toFields: string -> string array )
      and 'Props: (static member resolver: (Map<string,obj>  * 'Props) -> 'Props) 
      and 'Shared: (static member decoder: Decoder<'Shared>) 
      and 'Shared: (static member currentUserId: 'Shared option -> string option)
  >
    (pathStore:Store<RouterLocation<'Props,'Shared>>)
    propsToEval
    progress
    cacheStorage
    cacheRetrieval
    isSSEResponse 
     =
      createNavigation
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
  let inline link<
    'Props ,'Shared 
      when 'Props: (static member decoder: string -> Decoder<'Props>) 
      and 'Props: (static member toMap: (array<string> option * 'Props) -> array<string*obj>)
      and 'Props: (static member toFields: string -> string array)
      and 'Props: (static member resolver: (Map<string,obj>  * 'Props) -> 'Props) 
      and 'Shared: (static member decoder: Decoder<'Shared>) 
      and 'Shared: (static member currentUserId: 'Shared option -> string option)
  >
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
  let inline post pathStore url data propsToGet progress cacheStorage cacheRetrieval =
    doNav (Post data) pathStore url propsToGet progress cacheStorage cacheRetrieval false |> navigateTo
  
  /// Triggers client-side PUT request
  let inline put pathStore url data propsToGet progress cacheStorage cacheRetrieval = doNav (Put data) pathStore url propsToGet progress cacheStorage cacheRetrieval false |> navigateTo
  
  /// Triggers client-side PATCH request
  let inline patch pathStore url data propsToGet progress cacheStorage cacheRetrieval = doNav (Patch data) pathStore url propsToGet progress cacheStorage cacheRetrieval false |> navigateTo
  
  /// Triggers client-side DELETE request
  let inline delete pathStore url propsToGet progress cacheStorage cacheRetrieval = doNav Delete pathStore url propsToGet progress cacheStorage cacheRetrieval false |> navigateTo

  /// Client facing
  let inline Link
    defaultApply
    router
    : Method -> string -> PropsToEval -> ScrollPosition -> ProgressBar -> CacheStorage -> CacheRetrieval -> SutilElement seq -> SutilElement =
      link defaultApply router

  let inline Reload
    router = reload router 

  let inline Post
    router
    : string -> List<string * obj> -> PropsToEval -> ProgressBar -> CacheStorage -> CacheRetrieval -> unit
    = post router
    
  let inline Put
    router = put router
    
  let inline Patch
    router = patch router
    
  let inline Delete
    router = delete router
  
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
  let inline initialPageObj<'Props,'Shared 
    when 'Props: (static member decoder: string -> Decoder<'Props>)
    and 'Props: (static member toMap: (array<string> option * 'Props) -> array<string*obj>)
    and 'Props: (static member toFields: string -> string array)
    and 'Props: (static member resolver: (Map<string,obj>  * 'Props) -> 'Props) 
    and 'Shared: (static member decoder: Decoder<'Shared>) 
    and 'Shared: (static member currentUserId: 'Shared option -> string option)> () =
    
    let propsDecoderOpt = fun name -> 'Props.decoder name |> Decode.map Some
    let sharedDecoderOpt = 'Shared.decoder |> Decode.map Some
    match PageObj.fromJson (initialPageObjAttr ()) propsDecoderOpt sharedDecoderOpt with
    | Ok p ->       
      Some p
    | Error e ->
      printfn $"{e}"
      None
  
  let inline 
    renderRouter<'Props ,'Shared 
      when 'Props: (static member decoder: string -> Decoder<'Props>)
      and 'Props: (static member toMap: (array<string> option * 'Props) -> array<string*obj>)
      and 'Props: (static member toFields: string -> string array)
      and 'Props: (static member resolver: (Map<string,obj>  * 'Props) -> 'Props) 
      and 'Shared: (static member decoder: Decoder<'Shared>) 
      and 'Shared: (static member currentUserId: 'Shared option -> string option)
    >
    (router: Store<RouterLocation<'Props,'Shared>>)
    (urlToComponent:
      string list -> // url parts
        'Props -> // Page props
          'Shared ->
            Option<SutilElement -> 'Props -> 'Shared -> SutilElement> -> // optional layout function
              SutilElement)
    (layout: Option<SutilElement -> 'Props -> 'Shared -> SutilElement>)
    (sseEndpointUrl : string option) =
      
      // let propsDecoder = 'Props.decoder
      // let sharedDecoder = 'Shared.decoder
      let signedInUserId = 'Shared.currentUserId
      
      
      // configure the progress bar library
      NProgress.configure {| showSpinner = false |}
      
      
      // get initial pageObj
      let initial = initialPageObj()

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
                     (RxSSE.ssePartialReload reload router ShowProgressBar)
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
              if location.allowPartialReload && obj.reloadOnMount.shouldReload then
                match obj.reloadOnMount.propsToEval, obj.reloadOnMount.cacheStorage, obj.reloadOnMount.cacheRetrieval with
                | Some withProps, Some cacheStorage, Some cacheRetrieval ->
                  reload router withProps HideProgressBar cacheStorage cacheRetrieval false 
                | _ -> ()
              
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

