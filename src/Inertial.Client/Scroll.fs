namespace Inertial.Client

open Browser.Types
open Browser
open Core
open Thoth.Json

module Scroll =
    // get all scroll regions from dom
    // let regions() = document.querySelectorAll("body,[scroll-region]")

    let save (url:string) (scrollY: float) = sessionStorage.setItem($"scrollPosition_{url}",string scrollY)
    let restore (url:string) = sessionStorage.getItem($"scrollPosition_{url}") |> float

    // let reset =
    //   for i in 0..regions().length-1 do
    //     let region = regions().Item i
    //     region.scrollTop <- 0.0
    //     region.scrollLeft <- 0.0
    //   save
