namespace Inertial.Client

open Sutil
open Sutil.Core
open Inertial.Lib.Types

/// Render helpers for Deferred<'T> values in Sutil components
/// Provides declarative ways to handle loading, loaded, and error states
[<RequireQualifiedAccess>]
module Deferred =

    /// Configuration for rendering a Deferred value
    type RenderConfig<'T, 'El when 'El :> SutilElement> = {
        Loading: unit -> 'El
        Loaded: 'T -> 'El
        Failed: exn -> 'El
    }

    /// Render a Deferred<'T> value with explicit handlers for each state
    let render (config: RenderConfig<'T, 'El>) (deferred: Deferred<'T>) : 'El =
        match deferred with
        | Pending _ -> config.Loading()
        | Loaded value -> config.Loaded value
        | Failed ex -> config.Failed ex

    /// Render a Deferred<'T> with a default loading spinner and error display
    let renderSimple (loaded: 'T -> SutilElement) (deferred: Deferred<'T>) : SutilElement =
        match deferred with
        | Pending _ -> Html.text "Loading..."
        | Loaded value -> loaded value
        | Failed ex -> Html.text $"Error: {ex.Message}"

    /// Render a Deferred<'T> with custom loading element but default error handling
    let renderWithLoading (loading: SutilElement) (loaded: 'T -> SutilElement) (deferred: Deferred<'T>) : SutilElement =
        match deferred with
        | Pending _ -> loading
        | Loaded value -> loaded value
        | Failed ex -> Html.text $"Error: {ex.Message}"

    /// Render a Deferred<'T list> with helpers for empty state
    let renderList
        (loading: unit -> SutilElement)
        (empty: unit -> SutilElement)
        (renderItems: 'T list -> SutilElement)
        (failed: exn -> SutilElement)
        (deferred: DeferredList<'T>) : SutilElement =
        match deferred with
        | Pending _ -> loading()
        | Loaded [] -> empty()
        | Loaded items -> renderItems items
        | Failed ex -> failed ex

    /// Render a Deferred<'T option> with helpers for None state
    let renderOption
        (loading: unit -> SutilElement)
        (none: unit -> SutilElement)
        (some: 'T -> SutilElement)
        (failed: exn -> SutilElement)
        (deferred: DeferredOption<'T>) : SutilElement =
        match deferred with
        | Pending _ -> loading()
        | Loaded None -> none()
        | Loaded (Some value) -> some value
        | Failed ex -> failed ex

    /// Render a Deferred<Result<'T, 'E>> with error case handling
    let renderResult
        (loading: unit -> SutilElement)
        (ok: 'T -> SutilElement)
        (error: 'E -> SutilElement)
        (failed: exn -> SutilElement)
        (deferred: DeferredResult<'T, 'E>) : SutilElement =
        match deferred with
        | Pending _ -> loading()
        | Loaded (Ok value) -> ok value
        | Loaded (Error e) -> error e
        | Failed ex -> failed ex

/// Sutil bindings for reactive Deferred values
[<RequireQualifiedAccess>]
module Bind =

    open Sutil.CoreElements

    /// Bind a Deferred<'T> store to render different content based on state
    let deferred
        (store: IReadOnlyStore<Deferred<'T>>)
        (loading: unit -> SutilElement)
        (loaded: 'T -> SutilElement)
        (failed: exn -> SutilElement) : SutilElement =
        Bind.el(store, fun d ->
            match d with
            | Pending _ -> loading()
            | Loaded value -> loaded value
            | Failed ex -> failed ex)

    /// Simplified deferred binding with default loading/error states
    let deferredSimple
        (store: IReadOnlyStore<Deferred<'T>>)
        (loaded: 'T -> SutilElement) : SutilElement =
        Bind.el(store, fun d ->
            match d with
            | Pending _ -> Html.text "Loading..."
            | Loaded value -> loaded value
            | Failed ex -> Html.text $"Error: {ex.Message}")

    /// Bind a Deferred<'T list> store with empty list handling
    let deferredList
        (store: IReadOnlyStore<DeferredList<'T>>)
        (loading: unit -> SutilElement)
        (empty: unit -> SutilElement)
        (renderItems: 'T list -> SutilElement)
        (failed: exn -> SutilElement) : SutilElement =
        Bind.el(store, fun d ->
            match d with
            | Pending _ -> loading()
            | Loaded [] -> empty()
            | Loaded items -> renderItems items
            | Failed ex -> failed ex)

    /// Bind a Deferred<'T option> store with None handling
    let deferredOption
        (store: IReadOnlyStore<DeferredOption<'T>>)
        (loading: unit -> SutilElement)
        (none: unit -> SutilElement)
        (some: 'T -> SutilElement)
        (failed: exn -> SutilElement) : SutilElement =
        Bind.el(store, fun d ->
            match d with
            | Pending _ -> loading()
            | Loaded None -> none()
            | Loaded (Some value) -> some value
            | Failed ex -> failed ex)

/// Helper functions for working with Deferred values
[<AutoOpen>]
module DeferredExtensions =

    /// Check if a Deferred is in loading state
    let isLoading (deferred: Deferred<'T>) =
        match deferred with
        | Pending _ -> true
        | _ -> false

    /// Check if a Deferred has loaded successfully
    let isLoaded (deferred: Deferred<'T>) =
        match deferred with
        | Loaded _ -> true
        | _ -> false

    /// Check if a Deferred has failed
    let isFailed (deferred: Deferred<'T>) =
        match deferred with
        | Failed _ -> true
        | _ -> false

    /// Get the loaded value or None
    let tryGetLoaded (deferred: Deferred<'T>) : 'T option =
        match deferred with
        | Loaded v -> Some v
        | _ -> None

    /// Get the loaded value or a default
    let getLoadedOr (defaultValue: 'T) (deferred: Deferred<'T>) : 'T =
        match deferred with
        | Loaded v -> v
        | _ -> defaultValue

    /// Get the error from a failed Deferred
    let tryGetError (deferred: Deferred<'T>) : exn option =
        match deferred with
        | Failed ex -> Some ex
        | _ -> None
