namespace Inertial.Client

open System.Threading
open Browser.Types
open Inertial.Client.Core
open Microsoft.FSharp.Control
open FSharp.Control
open Sutil
open Thoth.Json
open Core

module RxSSE =
    // extend the ofEvent function to handle generation of SSE events from eventsource
    let ofEventSource<'ev> (eventSource:EventSource) : IAsyncObservable<'ev> =
          let cts = new CancellationTokenSource ()

          let subscribe (obv: IAsyncObserver<'ev>) : Async<IAsyncRxDisposable> =
              async {
                  let mb = MailboxProcessor.Start(fun inbox ->
                      let rec messageLoop _ = async {
                          let! ev = inbox.Receive ()
                          do! obv.OnNextAsync ev

                          return! messageLoop ()
                      }
                      messageLoop ()
                  , cts.Token)

                  eventSource.addEventListener_message(
                      fun msg ->
                        let json = msg.data :?> string
                        let event = Decode.fromString InertialSSEEvent.decoder json
                        unbox mb.Post event
                    )

                  let cancel () = async {
                      cts.Cancel ()
                      eventSource.removeEventListener("message", unbox mb.Post)
                  }
                  return AsyncDisposable.Create cancel
              }

          AsyncRx.create subscribe
          
    // check if the inbound sse event matches the predicate
    let eventPredicates (router:Store<RouterLocation<'Props,'Shared>>) signedInUserId event =
      match event, router.Value.pageObj  with
        | Ok ev, Some _ when ev.id = System.Guid.Empty -> false // ignore the initial SSE message
        | Ok ev, Some p ->
          printfn $"event: {ev}"

          let shouldReload =
              ev.predicates.predicates |> Array.map (fun pred ->
              match pred  with
              | ComponentIsOneOf arr ->
                arr
                |> Array.contains p.``component``
              | ComponentIsAny -> true
              | ComponentIsAnyExcept arr ->
                arr
                |> Array.contains p.``component`` |> not
              | UserIdIsOneOf arr ->
                match signedInUserId p.shared with
                | Some u -> arr |> Array.contains u
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

    // reload function to fire if an incoming SSE value satisfies the predicate
    let ssePartialReload
      (reloadFn:
        (string -> string -> JsonValue -> Result<'Props,DecoderError>) -> Decoder<'Shared> -> Store<RouterLocation<'Props,'Shared>> -> PropsToEval -> ProgressBar -> bool -> unit)
      (propsDecoder: string -> Decoder<'Props>)
      (sharedDecoder: Decoder<'Shared>)
      (router:Store<RouterLocation<'Props,'Shared>>)
      progressBar
      (notification:Notification<Result<InertialSSEEvent,string>>) =
        async {
          match notification with
          | OnNext n ->
            match n with
            | Ok ev ->
              return reloadFn propsDecoder sharedDecoder router ev.predicates.propsToEval progressBar true
            | Error err -> return printfn $"{err}"
          | OnError exn -> return printfn $"{exn.Message}"
          | OnCompleted -> return ()
        }

