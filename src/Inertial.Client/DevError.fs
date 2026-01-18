namespace Inertial.Client

open System
open Browser
open Browser.Types
open Sutil
open Sutil.Core
open Sutil.CoreElements
open Fable.Core
open Fable.Core.JsInterop

/// Development-mode HTTP error display for debugging.
/// Shows a collapsible toast with detailed error information when HTTP requests fail.
/// Only active in dev mode (localhost, local IPs) - silent in production.
[<RequireQualifiedAccess>]
module DevError =

    // ============================================
    // Error Types
    // ============================================

    /// HTTP error information captured from failed requests
    type HttpError = {
        StatusCode: int
        StatusText: string
        ResponseBody: string
        Url: string
        Method: string
        Timestamp: DateTime
    }

    // ============================================
    // Global Error Store (Sutil reactive store)
    // ============================================

    /// Global reactive store for last HTTP error
    let private lastError : IStore<HttpError option> = Store.make None

    /// Clear the last HTTP error
    let private clearLastError () = Store.set lastError None

    // ============================================
    // Fetch Interceptor (patch window.fetch)
    // ============================================

    /// Patch fetch to intercept errors - stores error info in window.__lastHttpError
    [<Emit("(function(){if(!window.__fetchPatched){var o=window.fetch;window.fetch=function(u,p){return o.apply(this,arguments).then(function(r){if(!r.ok&&r.status>=400){return r.clone().text().then(function(b){window.__lastHttpError={statusCode:r.status,statusText:r.statusText||('HTTP '+r.status),responseBody:b,url:typeof u==='string'?u:u.url,method:(p&&p.method)||'GET',timestamp:new Date().toISOString()};return r})}return r})};window.__fetchPatched=true}})()")>]
    let private patchFetch () : unit = jsNative

    /// Read the last error from window.__lastHttpError
    [<Emit("window.__lastHttpError")>]
    let private jsLastHttpError : obj = jsNative

    /// Clear window.__lastHttpError
    [<Emit("window.__lastHttpError = null")>]
    let private clearJsLastHttpError () : unit = jsNative

    /// Convert JS error object to F# record
    let private readJsError () : HttpError option =
        if isNull jsLastHttpError then
            None
        else
            try
                let statusCode : int = jsLastHttpError?statusCode
                let statusText : string = jsLastHttpError?statusText
                let responseBody : string = jsLastHttpError?responseBody
                let url : string = jsLastHttpError?url
                let method : string = jsLastHttpError?method
                let timestampStr : string = jsLastHttpError?timestamp
                Some {
                    StatusCode = statusCode
                    StatusText = statusText
                    ResponseBody = responseBody
                    Url = url
                    Method = method
                    Timestamp = DateTime.Parse(timestampStr)
                }
            with _ -> None

    // ============================================
    // Dev Mode Detection
    // ============================================

    /// Check if we're running in development mode.
    /// Dev mode = localhost, 127.0.0.1, *.local domains, or private IP ranges.
    let isDev () : bool =
        let hostname = window.location.hostname
        hostname = "localhost"
        || hostname = "127.0.0.1"
        || hostname.EndsWith(".local")
        || hostname.StartsWith("192.168.")
        || hostname.StartsWith("10.")

    // ============================================
    // Error Display Stores
    // ============================================

    /// Store for controlling error panel visibility
    let private isErrorPanelOpen : IStore<bool> = Store.make false

    /// Store for the last displayed error timestamp (to detect changes)
    let private lastDisplayedTimestamp : IStore<string> = Store.make ""

    // ============================================
    // Error Toast Component
    // ============================================

    /// Collapsible error panel that shows HTTP error details.
    /// Only renders in dev mode - call this at app level to enable error display.
    ///
    /// Example usage in App.fs:
    /// ```fsharp
    /// Html.div [
    ///     pageContent
    ///     DevError.toast ()  // Add at end of app
    /// ]
    /// ```
    let toast () =
        // Only show in dev mode
        if not (isDev ()) then
            Html.none
        else
            // Patch fetch on component mount
            patchFetch ()

            // Check for new errors periodically and update global store
            let checkForErrors () =
                match readJsError () with
                | Some error ->
                    let ts = error.Timestamp.ToString("o")
                    if ts <> Store.get lastDisplayedTimestamp then
                        Store.set lastError (Some error)
                        Store.set isErrorPanelOpen true
                        Store.set lastDisplayedTimestamp ts
                | None -> ()

            // Start polling interval (every 500ms)
            let _ = window.setInterval((fun () -> checkForErrors()), 500)

            Bind.el (lastError, fun errorOpt ->
                Bind.el (isErrorPanelOpen, fun isOpen ->
                    match errorOpt with
                    | None -> Html.none
                    | Some error ->
                        Html.div [
                            Attr.className "fixed bottom-4 right-4 z-[9999] max-w-lg"

                            // Collapsed state - just shows error badge
                            if not isOpen then
                                Html.button [
                                    Attr.className "flex items-center gap-2 px-4 py-2 rounded-lg bg-red-600 text-white shadow-lg hover:bg-red-700 transition-colors cursor-pointer"
                                    Ev.onClick (fun _ -> Store.set isErrorPanelOpen true)
                                    Html.span [
                                        Attr.className "text-sm font-medium"
                                        text $"HTTP {error.StatusCode} Error"
                                    ]
                                    Html.i [
                                        Attr.custom("data-lucide", "chevron-up")
                                        Attr.className "w-4 h-4"
                                    ]
                                ]
                            else
                                // Expanded state - shows full error details
                                Html.div [
                                    Attr.className "bg-gray-900 border border-red-500 rounded-lg shadow-2xl overflow-hidden"

                                    // Header
                                    Html.div [
                                        Attr.className "flex items-center justify-between px-4 py-2 bg-red-600 text-white"
                                        Html.div [
                                            Attr.className "flex items-center gap-2"
                                            Html.i [
                                                Attr.custom("data-lucide", "alert-triangle")
                                                Attr.className "w-5 h-5"
                                            ]
                                            Html.span [
                                                Attr.className "font-semibold"
                                                text $"HTTP {error.StatusCode} Error"
                                            ]
                                            Html.span [
                                                Attr.className "text-xs bg-red-700 px-2 py-0.5 rounded"
                                                text "DEV MODE"
                                            ]
                                        ]
                                        Html.div [
                                            Attr.className "flex items-center gap-1"
                                            // Collapse button
                                            Html.button [
                                                Attr.className "p-1 hover:bg-red-700 rounded transition-colors cursor-pointer"
                                                Ev.onClick (fun _ -> Store.set isErrorPanelOpen false)
                                                Html.i [
                                                    Attr.custom("data-lucide", "chevron-down")
                                                    Attr.className "w-4 h-4"
                                                ]
                                            ]
                                            // Dismiss button
                                            Html.button [
                                                Attr.className "p-1 hover:bg-red-700 rounded transition-colors cursor-pointer"
                                                Ev.onClick (fun _ ->
                                                    clearLastError()
                                                    clearJsLastHttpError())
                                                Html.i [
                                                    Attr.custom("data-lucide", "x")
                                                    Attr.className "w-4 h-4"
                                                ]
                                            ]
                                        ]
                                    ]

                                    // Error details
                                    Html.div [
                                        Attr.className "p-4 space-y-3 text-sm max-h-96 overflow-y-auto"

                                        // Request info
                                        Html.div [
                                            Html.p [
                                                Attr.className "text-gray-400 text-xs uppercase tracking-wide mb-1"
                                                text "Request"
                                            ]
                                            Html.p [
                                                Attr.className "text-white font-mono text-sm"
                                                text $"{error.Method} {error.Url}"
                                            ]
                                        ]

                                        // Timestamp
                                        Html.div [
                                            Html.p [
                                                Attr.className "text-gray-400 text-xs uppercase tracking-wide mb-1"
                                                text "Time"
                                            ]
                                            Html.p [
                                                Attr.className "text-gray-300 text-sm"
                                                text (error.Timestamp.ToString("HH:mm:ss.fff"))
                                            ]
                                        ]

                                        // Response body (scrollable)
                                        Html.div [
                                            Html.p [
                                                Attr.className "text-gray-400 text-xs uppercase tracking-wide mb-1"
                                                text "Response Body"
                                            ]
                                            Html.pre [
                                                Attr.className "bg-gray-800 p-3 rounded text-xs text-red-300 font-mono whitespace-pre-wrap break-words max-h-48 overflow-y-auto"
                                                text (
                                                    // Truncate very long responses
                                                    if error.ResponseBody.Length > 2000 then
                                                        error.ResponseBody.Substring(0, 2000) + "\n\n... (truncated)"
                                                    else
                                                        error.ResponseBody
                                                )
                                            ]
                                        ]

                                        // Tip
                                        Html.p [
                                            Attr.className "text-gray-500 text-xs italic"
                                            text "This error panel only appears in dev mode (localhost)"
                                        ]
                                    ]
                                ]
                        ]
                )
            )
