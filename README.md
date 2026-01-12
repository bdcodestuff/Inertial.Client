# Inertial.Client Module Reference

This document describes each module in the Inertial.Client library.

## Overview

Inertial.Client is a Fable/F# client library that works with Inertial (server) to provide
InertiaJS-style single page application behavior. It uses Sutil for reactive UI rendering.

## Modules

### JsCookie.fs

**Purpose**: JavaScript interop for the `js-cookie` library.

**Dependencies**: `js-cookie` npm package

**Exports**:
- `get : string -> string option` - Get a cookie by name
- `set : string -> string -> obj option` - Set a cookie
- `remove : string -> unit` - Remove a cookie

**Usage**:
```fsharp
let token = JsCookie.get "XSRF-TOKEN"
JsCookie.set "session" "abc123"
JsCookie.remove "old-cookie"
```

---

### NProgress.fs

**Purpose**: JavaScript interop for the `nprogress` progress bar library.

**Dependencies**: `nprogress` npm package

**Exports**:
- `start : unit -> unit` - Start the progress bar
- `set : int -> unit` - Set progress to specific value
- `inc : unit -> unit` - Increment progress
- `done : unit -> unit` - Complete the progress bar
- `configure : obj -> unit` - Configure NProgress options

**Usage**:
```fsharp
NProgress.start()
// ... loading ...
NProgress.``done``()
```

---

### Common.fs (Core module)

**Purpose**: Core types and utility functions used throughout the library.

**Types**:
- `Method` - HTTP method union (Get, Post, Put, Patch, Delete) with data
- `RouterLocation<'Props,'Shared>` - Current router state including pathname, query, pageObj

**Functions**:
- `split : string -> string -> string list` - Split string by delimiter
- `splitRoute : string -> string list` - Split URL path by "/"
- `splitQueryParam : string -> string list` - Split query string by "&"
- `splitKeyValuePair : string -> string list` - Split by "="
- `getKeyValuePair : 'a list -> ('a * 'a) option` - Extract key-value pair from list

**Usage**:
```fsharp
let segments = splitRoute "/users/123/profile"
// ["users"; "123"; "profile"]

let method = Post [("name", box "John"); ("age", box 30)]
let dataMap = method.ToDataMap()
```

---

### Route.fs

**Purpose**: Active patterns for parsing URL segments. Inspired by Feliz.Router.

**Active Patterns**:
- `(|Int|_|)` - Parse integer
- `(|Int64|_|)` - Parse 64-bit integer
- `(|Guid|_|)` - Parse GUID
- `(|Number|_|)` - Parse floating point number
- `(|Decimal|_|)` - Parse decimal
- `(|Bool|_|)` - Parse boolean ("true", "false", "1", "0", "")
- `(|Query|_|)` - Parse query string into key-value pairs

**Usage**:
```fsharp
match segment with
| Int id -> loadUser id
| Guid uuid -> loadByGuid uuid
| _ -> notFound()

match queryString with
| Query params ->
    params |> List.tryFind (fst >> (=) "page")
| _ -> None
```

---

### Scroll.fs

**Purpose**: Scroll position persistence using sessionStorage.

**Functions**:
- `save : string -> float -> unit` - Save scroll position for a component
- `restore : string -> float` - Restore scroll position (returns 0 if not found)

**Storage Key Format**: `scrollPosition:{componentName}`

**Usage**:
```fsharp
// Save current scroll position
Scroll.save "UserList" window.pageYOffset

// Restore scroll position
let y = Scroll.restore "UserList"
window.scroll(0, y)
```

---

### Inertia.fs

**Purpose**: HTTP request handling with Inertial protocol headers and caching logic.

**Key Functions**:
- `addInertiaHeaders` - Add all required Inertial headers to HTTP request
- `getCacheForComponent` - Retrieve cached prop values from sessionStorage
- `resolveReEvals` - Determine which props need re-evaluation vs cache
- `missingFromCacheMap` - Find props not in cache
- `resolveNextPropsToEval` - Determine final PropsToEval based on cache state
- `inertiaHttp` - Main HTTP function for Inertial requests

**Caching**:
The module handles client-side prop caching:
- Props can be stored in sessionStorage per component
- Cache storage key: `cache:{componentName}:{fieldName}`
- Supports `StoreAll`, `StoreToCache`, `StoreNone` strategies
- Supports `CheckForAll`, `CheckForCached`, `SkipCache` retrieval

**Headers Set**:
- `X-Inertial: true`
- `X-Inertial-Version: {version}`
- `X-Inertial-Id: {connectionId}`
- `X-Inertial-Partial-Component` / `X-Inertial-Full-Component`
- `X-Inertial-Partial-Data`
- `X-Inertial-SSE`, `X-Inertial-Reload`
- `X-Inertial-CacheStorage`, `X-Inertial-CacheRetrieval`
- `X-XSRF-TOKEN`

---

### RxSSE.fs

**Purpose**: Server-Sent Events (SSE) integration with FSharp.Control reactive extensions.

**Functions**:
- `ofEventSource<'ev>` - Convert EventSource to IAsyncObservable
- `eventPredicates` - Check if SSE event matches predicates for current page
- `ssePartialReload` - Trigger partial reload when SSE event is received

**Predicates Supported**:
- `ComponentIsOneOf` - Event applies to specific components
- `ComponentIsAny` - Event applies to all components
- `ComponentIsAnyExcept` - Event applies to all except specified
- `UserIdIsOneOf` - Event applies to specific users

**Usage**:
```fsharp
// SSE events trigger automatic partial reloads when predicates match
let eventSource = EventSource("/sse")
let observable = RxSSE.ofEventSource eventSource
```

---

### Deferred.fs

**Purpose**: Sutil render helpers for `Deferred<'T>` values.

**Modules**:

#### `Deferred` (render functions)
- `render` - Render with explicit handlers for Loading/Loaded/Failed
- `renderSimple` - Render with default loading text and error display
- `renderWithLoading` - Render with custom loading element
- `renderList` - Render Deferred list with empty state handling
- `renderOption` - Render Deferred option with None handling
- `renderResult` - Render Deferred Result with Ok/Error handling

#### `Bind` (reactive bindings)
- `deferred` - Bind Deferred store with all handlers
- `deferredSimple` - Bind with default loading/error
- `deferredList` - Bind Deferred list store
- `deferredOption` - Bind Deferred option store

#### `DeferredExtensions` (helper functions)
- `isLoading` - Check if Pending
- `isLoaded` - Check if Loaded
- `isFailed` - Check if Failed
- `tryGetLoaded` - Get value or None
- `getLoadedOr` - Get value or default
- `tryGetError` - Get exception or None

**Usage**:
```fsharp
// Simple rendering
Deferred.renderSimple (fun items ->
    Html.ul [ for item in items -> Html.li [ Html.text item.Name ] ]
) props.Items

// Reactive binding
Bind.deferred itemsStore
    (fun () -> Html.text "Loading...")
    (fun items -> renderItems items)
    (fun ex -> Html.text $"Error: {ex.Message}")
```

---

### Router.fs

**Purpose**: Main router component handling navigation, page loading, and SSE integration.

**Key Responsibilities**:
1. Initialize router from `data-page` attribute on mount
2. Handle link clicks for SPA navigation
3. Manage browser history (pushState/popState)
4. Trigger partial reloads for `reloadOnMount` props
5. Connect to SSE for real-time updates
6. Manage scroll position restoration
7. Show/hide progress bar during navigation

**Router State** (`RouterLocation<'Props,'Shared>`):
- `pathname` - Current URL path
- `query` - Query string
- `pageObj` - Current page object with props and metadata
- `allowPartialReload` - Whether partial reloads are allowed
- `cancellationTokenSource` - For cancelling in-flight requests

**Page Configuration via PageObj**:
- `component` - Component name
- `title` - Page title
- `props` - Page props (typed)
- `shared` - Shared data across pages
- `reloadOnMount` - Deferred field loading configuration
- `refreshOnBack` - Whether to reload on back navigation
- `preserveScroll` - Whether to restore scroll position
- `realTime` - Whether SSE updates are enabled
- `urlComponentMap` - URL to component mapping for prefetching

**Usage**:
```fsharp
// In your app entry point
Router.router
    propsDecoder
    sharedDecoder
    toFieldNames
    toMap
    resolver
    currentUserId
    renderPage
```
