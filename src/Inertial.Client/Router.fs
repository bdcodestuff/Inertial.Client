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

  // function to parse url into string parts
  let getCurrentUrl (router: RouterLocation<'Props,'Shared>) =
    let route =
      router.pathname.TrimStart('/').TrimEnd('/')

    if not (router.query = "") then
      splitRoute route @ [ router.query.TrimStart('?') ]
    else
      splitRoute route

  /// triggers a partial page load using JSON returned from server
  let navigate
    (pathStore: Store<RouterLocation<'Props,'Shared>>)
    (method:Method)
    url
    propsToEval
    isForwardBack
    propsDecoder
    sharedDecoder
    (doFullReloadOnArrival: bool)
    (progress:ProgressBar)
    (scroll:ScrollPosition)
    (isPartialReload:bool) =
      let inner =
        promise {
          match progress with
          | ShowProgressBar -> NProgress.start ()
          | HideProgressBar -> ()

          let currentComponent =
              match pathStore.Value.pageObj with
              | Some o -> o.``component``
              | None -> ""
          let currentId =
              match pathStore.Value.pageObj with
              | Some o -> o.connectionId
              | None -> ""
          let pageObjAsync =
            inertiaHttp
                url
                method
                propsToEval
                currentComponent
                currentId
                propsDecoder
                sharedDecoder
          // get the page obj returned by the server as JSON from the ajax call
          let! pageObj =
            Async.StartAsPromise(pageObjAsync,pathStore.Value.cancellationTokenSource.Token)

          // inertia passes in an url that we use to override the browser url
          let inertiaUrl =
            match pageObj with
            | Some p when p.url <> url ->
              p.url
            | _ -> url

          // don't add to history stack if user pressed forward or back buttons, or if the desired url hasn't changed
          if not isForwardBack && $"{window.location.pathname}{window.location.search}" <> inertiaUrl then
            window.history.pushState((), "", inertiaUrl)
            window.history.scrollRestoration <- ScrollRestoration.Manual

          // modify router location store
          pathStore
            |> Store.modify (fun a ->
              // cancel any requests still in progress
              a.cancellationTokenSource.Cancel()
              // dispose the token source
              a.cancellationTokenSource.Dispose()

              // save scroll location of outgoing page on each router location change
              // so we can restore later if requested
              Scroll.save a.pathname window.pageYOffset;

              {a with
                pathname = inertiaUrl
                query = window.location.search
                pageObj = pageObj
                allowPartialReload = not isPartialReload // we need this to prevent infinite partial reloads
                cancellationTokenSource = new CancellationTokenSource()
              } )

          // do a page reload if back/forward navigation and page implements this behavior
          if isForwardBack && doFullReloadOnArrival then window.location.reload()

        }

      // call the promise
      inner
        // after promise completes deal with post-processing
        .``then``(
          onfulfilled=(
            fun () ->
              match progress with
              | ShowProgressBar when not isForwardBack -> NProgress.``done`` ()
              | _ -> ()
              // restore y-scroll position is it's stored in session storage
              match scroll with
              | ResetScroll -> ()
              | KeepVerticalScroll url -> window.scroll(0,(Scroll.restore url))
            )
          )
        .catch(
          // catch errors (usually cancelled request errors)
          fun err ->

            match progress with
            | ShowProgressBar when not isForwardBack -> NProgress.``done`` ()
            | _ -> ()

            let toPrint =
              match string err with
              | "Error: The operation was canceled" -> "A new http request was requested before a prior pending request could be completed"
              | err -> err

            printfn $"{toPrint}")


  let reload
    propsDecoder
    sharedDecoder
    (pathStore: Store<RouterLocation<'Props,'Shared>>)
    propsToEval
    progress =
      navigate
        pathStore
        (Get [])
        $"{window.location.pathname}{window.location.search}"
        propsToEval
        false
        propsDecoder
        sharedDecoder
        false
        progress
        ResetScroll
        true
        |> Promise.start

  let link
    (defaultApply: seq<SutilElement>)
    propsDecoder
    sharedDecoder
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
              |> Seq.append [EngineHelpers.Attr.href href; onClick (fun e ->
        e.preventDefault()

        navigate
          pathStore
          method
          href
          propsToGet
          false
          propsDecoder
          sharedDecoder
          false
          progress
          scroll
          false
          |> Promise.start
      ) []])

  let post
    propsDecoder
    sharedDecoder
    (pathStore: Store<RouterLocation<'Props,'Shared>>)
    url
    data
    propsToGet
    progress
     =
      navigate
        pathStore
        (Post data)
        url
        propsToGet
        false
        propsDecoder
        sharedDecoder
        false
        progress
        ResetScroll
        false
        |> Promise.start

  let put
    propsDecoder
    sharedDecoder
    (pathStore: Store<RouterLocation<'Props,'Shared>>)
    url
    data
    propsToGet
    progress
     =
      navigate
        pathStore
        (Put data)
        url
        propsToGet
        false
        propsDecoder
        sharedDecoder
        false
        progress
        ResetScroll
        false
        |> Promise.start

  let patch
    propsDecoder
    sharedDecoder
    (pathStore: Store<RouterLocation<'Props,'Shared>>)
    url
    data
    propsToGet
    progress
     =
      navigate
        pathStore
        (Patch data)
        url
        propsToGet
        false
        propsDecoder
        sharedDecoder
        false
        progress
        ResetScroll
        false
        |> Promise.start

  let createRouter<'Props,'Shared>() =
    Store.make(
        {
          pathname = window.location.pathname
          query = window.location.search
          pageObj = Some <| PageObj<'Props,'Shared>.emptyObj window.location.pathname
          allowPartialReload = true
          cancellationTokenSource = new CancellationTokenSource()
        })

  let initialPageObjAttr () = document.getElementById("sutil-app").getAttribute("data-page")

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
    (urlToElement:
      string list -> // url parts
        'Props -> // Page props
          'Shared ->
            Option<SutilElement -> 'Props -> 'Shared -> SutilElement> -> // optional layout function
              SutilElement)
    (propsDecoder: string -> Decoder<'Props option>)
    (sharedDecoder: Decoder<'Shared option>)
    (layout: Option<SutilElement -> 'Props -> 'Shared -> SutilElement>) =
      // configure the progress bar library
      NProgress.configure {| showSpinner = false |}

      // get initial pageObj
      let initial = initialPageObj propsDecoder sharedDecoder

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

      // check if the inbound sse event matches the predicate
      let eventPredicates event page =
          match event, page  with
            | Ok ev, Some p ->
              printfn $"event: {ev}"

              let shouldReload =
                  ev.predicates.predicates |> Array.map (fun pred ->
                  match pred, router.Value.pageObj  with
                  | ComponentIsOneOf arr, Some p ->
                    arr
                    |> Array.contains p.``component`` ||
                    arr
                    |> Array.contains "*"
                  | ComponentIsAnyExcept arr, Some p ->
                    arr
                    |> Array.contains p.``component`` |> not
                  | UserIdIsOneOf arr, Some p ->
                    match signedInUserId p.shared with
                    | Some u -> arr |> Array.contains u
                    | _ -> false
                    //sharedUserPredicateCheck p.shared arr
                  | _ -> false)
                |> Array.contains false
                |> not

              let notEmptyAndNotSameUser =
                match ev.connectionId with
                | Some evId ->
                  evId <> "" && p.connectionId <> evId
                | None -> false

              shouldReload && notEmptyAndNotSameUser
            | Error e, _ -> failwith $"{e}"
            | _ -> failwith "Couldn't decode page data"

      // reload function to fire if an SSE value satisfies the predicate
      let ssePartialReload (notification:Notification<Result<InertialSSEEvent,string>>) =
        async {
          match notification with
          | OnNext n ->
            match n with
            | Ok ev ->
              return
                reload
                  propsDecoder
                  sharedDecoder
                  router
                  ev.predicates.propsToEval
                  ProgressBar.ShowProgressBar
            | Error err -> return printfn $"{err}"
          | OnError exn -> return printfn $"{exn.Message}"
          | OnCompleted -> return ()
        }

      let shouldListenOnSSE = // reloadOnSSE router.Value.pageObj
        match router.Value.pageObj with
        | Some p' -> p'.realTime
        | None -> true
      
      
      // determine if this sutil component should listen on the sse endpoint
      if shouldListenOnSSE then

        let eventSource = EventSource.Create("/sse")
        // make an observable stream of SSE values
        // ignore the first event, this represents the last "leftover" event
        // filter to only those meeting the predicate
        // delay the processing of each event by 1s plus the relative time interval between arrival of events
        let observable =
          RxSSE.ofEventSource<Result<InertialSSEEvent,string>> eventSource
          |> AsyncRx.skip 1
          |> AsyncRx.filter(fun event -> eventPredicates event router.Value.pageObj)
          |> AsyncRx.delay 1000
          //|> AsyncRx.distinctUntilChanged

        // start the subscription
        let main = async {
          // trigger the ssePartialReload for each stream element being subscribed to
          let! _ = observable.SubscribeAsync ssePartialReload
          return ()
        }
        Async.StartImmediate main

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
        promise {
          do!
            navigate
              router
              (Get [])
              $"{window.location.pathname}{window.location.search}"
              EvalAllProps
              true
              propsDecoder
              sharedDecoder
              shouldRefreshOnBack
              HideProgressBar
              (KeepVerticalScroll $"{window.location.pathname}{window.location.search}")
              false
        }
        |> Promise.start)

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
                    reload propsDecoder sharedDecoder router withProps HideProgressBar
                | None -> ()
              
            | None -> ()

            
            
            // parse url and find matching handler with layout if appropriate
            //(urlToElement (getCurrentUrl location) location.pageObj (layout location.allowPartialReload) )))
            match location.pageObj with
            | Some obj ->
              match obj.props, obj.shared with
              | Some props, Some shared -> 
                (urlToElement (getCurrentUrl location) props shared layout )
              | _ -> text "Error loading page data, please refresh"
            | _ -> text "Error loading page data, please refresh"))
        
        disposeOnUnmount [router]
      ]

