﻿module FaultsTests

open NUnit.Framework
open FSharp.Control
open System
open System.Threading
open Kafunk

[<Test>]
let ``should return timeout result and cancel when past timeout`` () =
  
  let serviceTime = TimeSpan.FromMilliseconds 50.0

  for timeout in [true;false] do

    let sleepTime = 
      if timeout then int serviceTime.TotalMilliseconds * 2
      else 0

    let cancelled = ref false

    let sleepEcho () = async {
      use! _cnc = Async.OnCancel (fun () -> cancelled := true)
      do! Async.Sleep sleepTime
      return () }

    let sleepEcho =
      sleepEcho
      |> AsyncFunc.timeoutResult serviceTime
      |> AsyncFunc.mapOut (snd >> Result.mapError ignore)

    let expected = 
      if timeout then Failure ()
      else Success ()

    let actual = sleepEcho () |> Async.RunSynchronously
  
    shouldEqual expected actual None
    shouldEqual timeout !cancelled None

  
[<Test>]
let ``should retry with reevaluation`` () =

  let time = TimeSpan.FromMilliseconds 50.0

  for attempts in [1..5] do

    let mutable i = 0

    let sleepEcho () = 
      if Interlocked.Increment &i > attempts then
        async.Return (Success ())
      else
        async.Return (Failure ())

    let backoff = RetryPolicy.constant 10 |> RetryPolicy.maxAttempts attempts

    let sleepEcho =
      sleepEcho 
      |> Faults.AsyncFunc.retryResultList backoff
  
    sleepEcho () |> Async.RunSynchronously |> ignore

    let expected = attempts + 1
    let actual = i

    shouldEqual expected actual None



let partitionByCount 
  (count:int) 
  (before:'a -> Async<'b>)
  (after:'a -> Async<'b>) : 'a -> Async<'b> =
  let mutable i = 0
  fun a -> async {
    if Interlocked.Increment &i > count then
      return! after a
    else
      return! before a }

[<Test>]
let ``should retry timeout with backoff and succeed`` () = 

  let time = TimeSpan.FromMilliseconds 50.0

  for attempts in [1..5] do

    for fail in [true;false] do

      let policy = RetryPolicy.constant 10 |> RetryPolicy.maxAttempts attempts

      let sleepEcho =
        let mutable i = 0
        let attempts = if fail then attempts + 1 else attempts
        fun () -> async {
          if Interlocked.Increment &i > attempts then
            return ()
          else
            do! Async.Sleep (int time.TotalMilliseconds * 2)
            return () }

      let sleepEcho =
        sleepEcho
        |> AsyncFunc.timeoutResult time
        |> AsyncFunc.mapOut (snd >> Result.mapError ignore)
        |> Faults.AsyncFunc.retryResultList policy

      let expected = 
        if fail then Failure (List.init (attempts + 1) ignore)
        else Success ()

      let actual = sleepEcho () |> Async.RunSynchronously

      shouldEqual expected actual (Some (sprintf "[fail=%A attempts=%i]" fail attempts))

//[<Test>]
let ``should retry with condition and retry policy``() =
  
  let shouldRetry (a,b) = false
  let policy = RetryPolicy.constant 10 |> RetryPolicy.maxAttempts 10

  let service (x:int) = async {
    
    return x }

  let serviceWithRetry =
    service
    |> Faults.AsyncFunc.retry shouldRetry policy

  let actual = serviceWithRetry 1 |> Async.RunSynchronously
  let expected = None

  shouldEqual expected actual None

