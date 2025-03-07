namespace Inertial.Client

open Browser
open Core

module Scroll =
    let save (componentName:string) (scrollY: float) = sessionStorage.setItem($"scrollPosition:{componentName}",string scrollY)
    let restore (componentName:string) =
        match sessionStorage.getItem($"scrollPosition:{componentName}") with
        | null | "" -> 0.
        | a -> a |> float