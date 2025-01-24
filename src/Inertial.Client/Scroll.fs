namespace Inertial.Client

open Browser
open Core

module Scroll =
    let save (url:string) (scrollY: float) = sessionStorage.setItem($"scrollPosition_{url}",string scrollY)
    let restore (url:string) = sessionStorage.getItem($"scrollPosition_{url}") |> float