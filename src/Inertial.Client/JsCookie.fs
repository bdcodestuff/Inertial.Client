module JsCookie

open Fable.Core.JsInterop

type IJSCookie =
  abstract remove: name: string -> unit
  abstract get: name: string -> (string option)
  abstract set: name: string -> value: string -> obj option // Returns null, must be represented with option

let private imported = importDefault<IJSCookie> "js-cookie"

let get = imported.get
let set = imported.set
let remove = imported.remove
