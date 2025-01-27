namespace Inertial.Client

open Fable.React.Props
open Thoth.Json
open Fable.SimpleHttp
open System
open System.Threading

module Core =
  
  let emptyDecoder : Decoder<obj> = Decode.object(fun _ -> null)

  type ScrollPosition =
    | ResetScroll
    | KeepVerticalScroll of string
  type ScrollRegion =
    {
      top : double
      left : double
    }
    static member encoder = Encode.Auto.generateEncoder<ScrollRegion>()
    static member decoder = Decode.Auto.generateDecoder<ScrollRegion>()


  type ProgressBar =
    | ShowProgressBar
    | HideProgressBar



      // static member decoder =
      // let decoder : Decoder<PropsToEval> =
      //
      //     Decode.map2
      //         (fun pred1 pred2 ->
      //             printfn "decoding empty"
      //             match pred1, pred2 with
      //             | Some _, None -> EvalAllProps
      //             | None, Some arr -> OnlyEvalProps arr
      //             | _ -> failwith "Cannot decode PropsToEval"
      //         )
      //         (Decode.optional "evalAllProps" emptyDecoder)
      //         (Decode.optional "onlyEvalProps" (Decode.array Decode.string))
      // decoder

  type PropsToEval =
    | EvalAllProps
    | OnlyEvalProps of string array
    // static member decoder =
    //   Decode.string
    //         |> Decode.andThen
    //         (function
    //           | "EvalAllProps" -> Decode.succeed EvalAllProps
    //           | "OnlyEvalProps" -> Decode.array Decode.string |> Decode.map OnlyEvalProps
    //           | _ -> failwith "Cannot decode PropsToEval")

    static member decoder =
      let decodeEvalAllProps =
          Decode.string
            |> Decode.andThen
            (function
              | "EvalAllProps" -> Decode.succeed EvalAllProps
              | _ -> failwith "Cannot decode PropsToEval")

      let decodeOnlyEvalProps =
          Decode.field "OnlyEvalProps" (Decode.array Decode.string) |> Decode.map OnlyEvalProps

      // Now that we know how to handle each case, we say that
      // at least of the decoder should succeed to be a valid `Query` representation
      Decode.oneOf [ decodeEvalAllProps ; decodeOnlyEvalProps ]


  type Method =
    | Get of List<string*obj>
    | Post of List<string*obj>
    | Put of List<string*obj>
    | Patch of List<string*obj>
    | Delete
    member x.ToMethodHttp() =
      match x with
      | Get _ -> HttpMethod.GET
      | Post _ -> HttpMethod.POST
      | Put _ -> HttpMethod.PUT
      | Patch _ -> HttpMethod.PATCH
      | Delete -> HttpMethod.DELETE
    member x.ToDataMap() =
      let dataList =
        match x with
        | Delete -> []
        | Get data | Post data | Put data | Patch data -> data
      Map.ofList dataList




  let split (splitBy: string) (value: string) =
    value.Split([| splitBy |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.toList

  let splitRoute = split "/"
  let splitQueryParam = split "&"
  let splitKeyValuePair = split "="

  let getKeyValuePair (parts: 'a list) =
    if List.length parts = 2 then
      Some(parts[0], parts[1])
    else
      None

  type RealTimePredicates =
    | ComponentIsOneOf of string array
    | ComponentIsAny
    | ComponentIsAnyExcept of string array
    | UserIdIsOneOf of string array
    static member decoder =
      let decoder : Decoder<RealTimePredicates> =

        let decodeUserIdIsOneOf =
            Decode.field "UserIdIsOneOf" (Decode.array Decode.string) |> Decode.map UserIdIsOneOf
            
        let decodeComponentIsAny =
            Decode.string
              |> Decode.andThen
              (function
              | "ComponentIsAny" -> Decode.succeed ComponentIsAny
              | a -> failwith $"Cannot decode RealtimePredicate: {a}")       
        
        let decodeComponentIsOneOf =
            Decode.field "ComponentIsOneOf" (Decode.array Decode.string) |> Decode.map ComponentIsOneOf

        let decodeComponentIsAnyExcept =
            Decode.field "ComponentIsAnyExcept" (Decode.array Decode.string) |> Decode.map ComponentIsAnyExcept

        // Now that we know how to handle each case, we say that
        // at least of the decoder should succeed to be a valid `Query` representation
        Decode.oneOf [ decodeUserIdIsOneOf ; decodeComponentIsOneOf ; decodeComponentIsAnyExcept ; decodeComponentIsAny ]
      decoder

    type Predicates =
      {
        predicates : RealTimePredicates array
        propsToEval : PropsToEval
      }

    type InertialSSEEvent =
      {
        title : string
        connectionId : string option
        predicates : Predicates
        firedOn : DateTime
      }
      static member decoder =
        Decode.object (fun get ->
          {
            title = get.Required.Field "title" Decode.string
            connectionId =  get.Optional.Field "connectionId" Decode.string
            predicates = get.Required.Field "predicates" (Decode.object (fun get -> { predicates = get.Required.Field "predicates" (Decode.array RealTimePredicates.decoder) ; propsToEval = get.Required.Field "propsToEval" PropsToEval.decoder }) )// (Decode.tuple2 (Decode.array RealTimePredicates.decoder) PropsToEval.decoder)
            firedOn = get.Required.Field "firedOn" Decode.datetimeUtc
          })


  type ReloadOnMount =
    { shouldReload : bool ; propsToEval : PropsToEval option }

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
    static member emptyObj url : PageObj<'Props,'Shared> =
      {
        ``component`` = ""
        connectionId = ""
        version = ""
        url = url
        title = ""
        props = None
        refreshOnBack = false
        reloadOnMount = { shouldReload = false; propsToEval = None }
        realTime = true
        shared = None
    }

    static member fromJson (json:string) propsDecoder sharedDecoder =
      
      let decodeProps (componentName:string) =
        Decode.object (fun get ->
            //get.Required.Field componentName (propsDecoder componentName)
            get.Required.Field componentName (propsDecoder componentName)
          )

      let decoder : Decoder<PageObj<'Props,'Shared>> =
        Decode.object (fun get ->
          let componentName = get.Required.Field "component" Decode.string
          

          //let asyncData = get.Required.Field "asyncData" (Decode.array (Decode.tuple2 Decode.string Decode.string))

          {
            ``component`` = componentName
            version = get.Required.Field "version" Decode.string
            connectionId =  get.Required.Field "connectionId" Decode.string
            url = get.Required.Field "url" Decode.string
            title = get.Required.Field "title" Decode.string
            refreshOnBack = get.Required.Field "refreshOnBack" Decode.bool
            reloadOnMount = get.Required.Field "reloadOnMount" (Decode.object (fun get -> { shouldReload = get.Required.Field "shouldReload" Decode.bool ; propsToEval = get.Optional.Field "propsToEval" PropsToEval.decoder }) ) // Decode.bool PropsToEval.decoder)
            realTime = get.Required.Field "realTime" Decode.bool
            props = get.Required.Field "props" (decodeProps componentName)
            shared = get.Required.Field "shared" sharedDecoder
          }
        )
      Decode.fromString decoder json

      // define the Router location type
  type RouterLocation<'Props,'Shared> =
    {
      pathname: string
      query: string
      pageObj: PageObj<'Props,'Shared> option
      allowPartialReload: bool
      cancellationTokenSource: CancellationTokenSource
    }
