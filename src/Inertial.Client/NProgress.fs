module NProgress

open Fable.Core.JsInterop

type INprogress =
  abstract start: unit -> unit
  abstract set: value: int -> unit
  abstract inc: unit -> unit
  abstract ``done``: unit -> unit
  abstract configure: obj -> unit

let private imported = importDefault<INprogress> "nprogress"

let start = imported.start
let set v = imported.set v
let inc = imported.inc
let ``done`` = imported.``done``
let configure options = imported.configure options
