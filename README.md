# Inertial.Client

## What is this?

You're looking at an attempt (by an FSharp novice) to bring [InertiaJS](https://inertiajs.com/) to the FSharp ecosystem with a few additional bells and whistles.

Essentially, this is a Fable library that provides client-side router functionality for a [Sutil](https://sutil.dev/) application.  It's forked from [Sutil.Router.Path](https://github.com/davidon-top/Sutil.Router.Path), which already figured out how to parse the browser url location to summon the matching Sutil components.  My fork hijacks links and page reloads so that they make client-side XML http requests with added headers that follow the InertiaJS [protocol](https://inertiajs.com/the-protocol).

To get the InertiaJS-like experience, you need to have a way to communicate from the client app to a back-end server.  For that, there is a complementary [Giraffe server plugin](https://github.com/bdcodestuff/Inertial.Giraffe) that reads the Http headers of incoming requests and determines how to respond accordingly.

Together the two libraries allow you to create Giraffe HttpHandlers that return Sutil front-end components (with the necessary page data already included, or pre-baked with instructions to pull in data asynchronously) just like you would call a JSON or HTML server response.  The upside is that you get a SPA-like end result without having to mess with an API using server-side methods that are simple to understand (server-side auth, db calls, etc...).  The trade-off is that you've now coupled the front-end to the backend so you can't have multiple front-end targets (i.e. a mobile app and web app served by the same data).  

The client also listens to server sent events (SSE) coming from the server and will (optionally) trigger client-side responses depending on predicates set server-side.

## In Detail

To make this work you need to do the following:

1. Create a Giraffe AspCoreNet server-side app with the Inertial.Giraffe nuget package. [See here](https://github.com/bdcodestuff/Inertial.Giraffe) for those instructions.
2. Create a Netstandard2.0 library project in the same solution with the server-side app that sits "above" both the Server app and the Client app in the same solution.  Make sure both server and client reference this project.  In this library (let's call it the "Common" library for demonstration purposes) you define the app domain with top-level "Props" and "Shared" types that the server and client will both reference.  Props are types describing data available for a given "page".  Shared is a type describing data that gets made available to all components all of the time -- think details of the signed-in user or flash messages.  The top-level types also need functions (or as below static methods) that decode themselves from JSON.  I chose to use Thoth.Json for this task because it has reliable "auto" decoders and for more complex scenarios allows for very intuitive composition.  The decoder function for the top-level Props DU should take in a string name that is pattern matched to determine which child decoder is needed (see below for an example implementation):
   ```fsharp
   open Thoth.Json
   
   // some helper functions for our decoders
   module Helpers =
        let emptyDecoder : Decoder<obj> =
            Decode.object( fun _ -> null )     
      
        let resultDecoder<'T> (decoder: Decoder<'T>) =
        
            let decoder: Decoder<Result<'T,string>> =                
                    let decodeOK =
                        Decode.field "Ok" decoder |> Decode.map Ok
                    let decodeError =
                        Decode.field "Error" (Decode.string) |> Decode.map Error

                    Decode.oneOf [ decodeOK ; decodeError ]
                
            decoder
         
        let asyncChoice2Decoder<'T> (placeholder : Async<Result<'T,string>>) (decoder: Decoder<'T>) =
            let decoder =                
                let decodeChoice1 =
                    Decode.field "Choice1Of2" (emptyDecoder |> Decode.andThen (fun _ ->  Decode.succeed (Choice1Of2 placeholder) ))
            
                let decodeChoice2 =
                    Decode.field "Choice2Of2" (resultDecoder decoder) |> Decode.map Choice2Of2

                Decode.oneOf [ decodeChoice1 ; decodeChoice2 ]
            
            decoder
            
   
   type Widget {
        name: string
        description: string
   }
   static member decoder = Decode.Auto.generateDecoder<Widget>()
    
   type User = {
        email : string
        username : string
   }
   static member decoder = Decode.Auto.generateDecoder<User option>()
   static member encoder userOpt = Encode.Auto.generateEncoder<User option>()
   
   type IndexPage = {
        widgets : Widget list
        asyncWidgets : Choice<Async<Result<Widget list,string>>,Result<Widget list,string>>>
   }
   static member decoder =
        Decode.object (fun get ->
            {
                widgets = get.Required.Field "widgets" (Decode.list Widget.decoder)
                asyncWidgets =
                    get.Required.Field
                        "asyncWidgets"
                        (Helpers.asyncChoice2Decoder
                             (async { return Ok <| [] }) // this is a placeholder that has the same type signature
                             Widget.decoder)
                        
            })
   
   type Props =
        | Index of IndexPage
        static member index = nameof Props.Index
   
        static member decoder (name: string) : Decoder<Props> = 
        // note that this decoder is a function that takes a string matching the component name 
        // and returns a decoder that has been mapped back to Option<Props>
            match name with
            | "Index" = Props.index ->
                IndexPage.decoder 
                |> Decode.map Index
            | notFound -> 
                 failwith 
                     $"Could not find matching decoder for component named: {notFound}"
   
   type Shared = 
        {
            user : User option
            flashMessage = string option
        }
        let extra =
            Extra.empty
            |> Extra.withCustom User.encoder User.decoder
        
        Decode.Auto.generateDecoder<Shared option>(extra=extra)
    
   ```
3. Next create a new Sutil app in the same solution as the Server app and Common library.  Add the Inertial.Client nuget package.  The preferred way of doing this is by installing the package with Femto so that the npm packages (JsCookie, ViteJS and NProgress) get auto installed.  If you prefer to do it manually just add them using npm install.  be sure to also reference the "Common" library that you create with the domain types.
4. In the Sutil App you will need to reference the following packages:
```fsharp
   open Sutil
   open Sutil.Core
   open Inertial.Client.Core
   open Inertial.Client
   open Fable.Core
   open Common // the "shared" domain library you create with Props, Shared definitions
```
5. The client library parses JSON data into a PageObj<Props,Shared> record type from the server (it starts either embedded in the html data-page body attribute on full page load or as JSON on a partial page response) with the type signature:
```fsharp
    type PageObj<'Props,'Shared> =
        {
            ``component`` : string
            connectionId : string
            version : string
            url : string
            title : string
            props : 'Props option
            refreshOnBack : bool
            reloadOnMount : ReloadOnMount
            realTime : bool
            shared : 'Shared option
        }
        and ReloadOnMount = { shouldReload : bool ; propsToEval : PropsToEval option }
        and PropsToEval =
            | EvalAllProps
            | OnlyEvalProps of string array
```
6. The Router.renderRouter function takes in an instance of the PageObj<Props,Shared> type and creates a SutilElement that drives the client side experience.
7. The Router.renderRouter function has the following signature:
```fsharp   
   router: Store<RouterLocation<'Props,'Shared>> -> 
   signedInUserId: 'Shared option -> string option -> 
   urlToElement: (string list -> 'Props -> 'Shared -> Option<(SutilElement -> 'Props -> 'Shared -> SutilElement)> -> SutilElement) -> 
   propsDecoder: (string -> Thoth.Json.Decoder<'Props>) -> 
   sharedDecoder: Thoth.Json.Decoder<'Shared option> -> 
   layout: (Option<(SutilElement -> 'Props -> 'Shared -> SutilElement)>) -> 
   SutilElement
```
   8. The router argument is implemented from the Router.createRouter function in the library as a Sutil "store".
```fsharp

   let router = Router.createRouter<Props,Shared>() // Concrete implementations of Props, Shared from our "Common" library are used here to replace the generic 'Props and 'Shared types in the Inertial.Client library
```
9. The signedInUser argument is a function that takes in an optional Shared record and returns an optional string representing the signed-in user's id -- the primary use case is to trigger a client-side event for a specific user with matching id.
```fsharp
    // if the shared record looks like:
    type Shared = 
        {
            user: User option
        }
        and User = {
            id : string
        }
        
    // then the signedInUser function would be:
    let signedInUser (sharedOpt: Shared option) =
        sharedOpt |> Option.map (fun s -> s.user |> Option.map (_.id))
        
    // if you have no use for this then:
    let signedInUser = fun _ -> None
```
10. The urlToElement argument describes a function that takes in the browser url parsed into a string list with the optional page object as well as an optional layout function that wraps the page inside another Sutil component. 
```fsharp
    let getElementFromUrl
        (url: string list)
        (props:Props)
        (shared:Shared)
        (layoutOpt: Option<SutilElement -> Props -> Shared -> SutilElement>)
        =
            let nextEl =
                match props, url with
                | Index p, [] -> Index.indexHandler p
                | Dashboard p, ["dashboard"] -> Dashboard.dashboardHandler p
                | SignIn p, [ "sign-in" ] -> SignIn.signInHandler p
                | Register p, [ "register" ] -> Register.registerHandler p
                | NotFoundError p, _ -> Error.handleError p
                | _, notfound ->
                    let notfoundMsg =
                        match notfound with
                        | [] -> "/"
                        | u -> u |> List.reduce (fun x y -> x + "/" + y) 
                    text $"Could not find requested path: {notfoundMsg}"
        
        layoutOpt |> function
            | Some layout ->
                layout nextEl props shared // if layout builder function is provided, wrap the nextEl arg here
            | None -> nextEl

```
11. We get the propsDecoder and sharedDecoder from the "Common" library.  I made these static methods on the Props and Shared types but they could be independent functions as well:
```fsharp
   let propsDecoder = Props.decoder
   let sharedDecoder = Shared.decoder
```
12. We finally pass in the layout builder function, which takes (1) an inner sutil element that the layout will wrap as well as (2) the current Props instance and (3) Shared instances.  It then returns a new SutilElement.  The provided example shows how a menu bar can be built that reacts to the signed in user in the shared data and reacts accordingly:
```fsharp
    open Inertial.Client
    
    // helper function for link creation
    let Link = Router.link [Attr.className "text-md font-semibold hover:underline"] Props.decoder Shared.decoder router 
    
    let layout (inner:SutilElement) (props: Props) (shared:Shared) =
        let sharedStore = Store.make p.shared
        
        Html.div [
            class' "flex flex-col"
            Html.div [
                class' "w-full justify-center flex flex-row py-4 border-b border-gray-400"
                Link (Get []) "/" EvalAllProps ResetScroll HideProgressBar [text "Home" ; addClass "px-4"]
                Link (Get []) "/" (OnlyEvalProps [| "username"; "username2" |]) ResetScroll ShowProgressBar [text "Test link" ; addClass "px-4"]

                Bind.el (sharedStore,
                        (fun shared' ->

                            match shared'.user with
                            | Some user ->
                                fragment [
                                    Link (Get []) "/dashboard" EvalAllProps ResetScroll ShowProgressBar [text "Dashboard" ; addClass "px-4"]
                                    Link (Method.Post []) "/sign-out" EvalAllProps ResetScroll HideProgressBar [text "Sign-out" ; addClass "px-4"]
                                ]
                            | _ ->
                                Link (Get []) "/sign-in" EvalAllProps (KeepVerticalScroll "/sign-in") ShowProgressBar [text "Sign-in" ; addClass "px-4"]
                            ))
            
            
            
            ]
            Bind.el (sharedStore,
                        (fun shared' ->
                            match shared'.flash with
                            | Some flash -> Flash.render flash
                            | _ -> nothing
                            ))
            inner
            disposeOnUnmount [ sharedStore ]
    ]
```
13. Finally, we put it altogether in the renderRouter function and mount the app
```fsharp
   let app() =
      Router.renderRouter
          router
          Shared.currentUserId
          getElementFromUrl
          Props.decoder
          Shared.decoder
          (Some layout)
    
    app() |> Program.mount
    
```
14. Cheers!