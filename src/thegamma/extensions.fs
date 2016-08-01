﻿module Fable.Extensions

open Fable.Core
open Fable.Import.JS
open Fable.Import.Browser

[<Emit("JSON.stringify($0)")>]
let jsonStringify json : string = failwith "JS Only"

[<Emit("JSON.parse($0)")>]
let jsonParse<'R> (str:string) : 'R = failwith "JS Only"

[<Emit("console.log.apply(console, $0)")>]
let consoleLog (args:obj[]) : unit = failwith "JS only"

let enabledCategories = set [ "COMPLETIONS"; "EDITORS"; "TYPECHECKER"; "SERVICE"; "CODEGEN" ]

type Log =
  static member message(level:string, category:string, msg:string, [<System.ParamArray>] args) = 
    let category = category.ToUpper()
    if level = "EXCEPTION" || level = "ERROR" || enabledCategories.Contains category then
      let dt = System.DateTime.Now
      let p2 (s:int) = (string s).PadLeft(2, '0')
      let p4 (s:int) = (string s).PadLeft(4, '0')
      let prefix = sprintf "[%s:%s:%s:%s] %s: " (p2 dt.Hour) (p2 dt.Minute) (p2 dt.Second) (p4 dt.Millisecond) category
      let color = 
        match level with
        | "TRACE" -> "color:#808080"
        | "EXCEPTION" -> "color:#c00000"
        | "ERROR" -> "color:#900000"
        | _ -> ""
      let args = if args = null then [| |] else args
      consoleLog(FSharp.Collections.Array.append [|box ("%c" + prefix + msg); box color|] args)

  static member trace(category:string, msg:string, [<System.ParamArray>] args) = 
    Log.message("TRACE", category, msg, args)

  static member exn(category:string, msg:string, [<System.ParamArray>] args) = 
    Log.message("EXCEPTION", category, msg, args)

  static member error(category:string, msg:string, [<System.ParamArray>] args) = 
    Log.message("ERROR", category, msg, args)

type Http =
  /// Send HTTP request asynchronously
  /// (does not handle errors properly)
  static member Request(meth, url, ?data, ?cookies) =
    Async.FromContinuations(fun (cont, _, _) ->
      let xhr = XMLHttpRequest.Create()
      xhr.``open``(meth, url, true)
      match cookies with 
      | Some cookies when cookies <> "" -> xhr.setRequestHeader("X-Cookie", cookies)
      | _ -> ()
      xhr.onreadystatechange <- fun _ ->
        if xhr.readyState > 3. && xhr.status = 200. then
          cont(xhr.responseText)
        obj()
      xhr.send(defaultArg data "") )

type Future<'T> = 
  abstract Then : ('T -> unit) -> unit

type Microsoft.FSharp.Control.Async with
  static member AwaitFuture (f:Future<'T>) = Async.FromContinuations(fun (cont, _, _) ->
    f.Then(cont))

  static member AsFuture(op, ?start) = 
    let mutable res = Choice1Of3()
    let mutable handlers = []
    let mutable running = false

    let trigger h = 
      match res with
      | Choice1Of3 () -> handlers <- h::handlers 
      | Choice2Of3 v -> h v
      | Choice3Of3 e -> raise e

    let ensureStarted() = 
      if not running then 
        running <- true
        async { try 
                  let! r = op
                  res <- Choice2Of3 r                  
                with e ->
                  res <- Choice3Of3 e
                for h in handlers do trigger h } |> Async.StartImmediate
    if start = Some true then ensureStarted()

    { new Future<_> with
        member x.Then(f) = 
          ensureStarted()
          trigger f }

  static member StartAsFuture(op) = Async.AsFuture(op, true)

module Async = 
  module Array =
    let rec map f (ar:_[]) = async {
      let res = FSharp.Collections.Array.zeroCreate ar.Length
      for i in 0 .. ar.Length-1 do
        let! v = f ar.[i]
        res.[i] <- v
      return res }

  let rec collect f l = async {
    match l with 
    | x::xs -> 
        let! y = f x
        let! ys = collect f xs
        return List.append y ys
    | [] -> return [] }

  let rec choose f l = async {
    match l with 
    | x::xs -> 
        let! y = f x
        let! ys = choose f xs
        return match y with None -> ys | Some y -> y::ys 
    | [] -> return [] }

  let rec map f l = async {
    match l with 
    | x::xs -> 
        let! y = f x
        let! ys = map f xs
        return y::ys
    | [] -> return [] }

  let rec foldMap f st l = async {
    match l with
    | x::xs ->
        let! y, st = f st x
        let! st, ys = foldMap f st xs
        return st, y::ys
    | [] -> return st, [] }

  let rec fold f st l = async {
    match l with
    | x::xs ->
        let! st = f st x
        return! fold f st xs 
    | [] -> return st }
