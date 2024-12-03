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



