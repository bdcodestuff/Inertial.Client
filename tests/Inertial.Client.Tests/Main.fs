module Inertial.Client.Tests.Main

open Fable.Mocha

let allTests = testList "Inertial.Client" [
    RouteTests.routeTests
    CoreTests.coreTests
    DeferredTests.deferredTests
    InertiaTests.inertiaTests
]

[<EntryPoint>]
let main args =
    Mocha.runTests allTests
