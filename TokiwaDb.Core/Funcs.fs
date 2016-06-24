﻿namespace TokiwaDb.Core

open System
open System.IO
open System.Threading
open FsYaml

module Value =
  let toType =
    function
    | Int _    -> TInt
    | Float _  -> TFloat
    | String _ -> TString
    | Time _   -> TTime

module ValuePointer =
  let ofUntyped type' (p: int64) =
    match type' with
    | TInt      -> PInt p
    | TFloat    -> PFloat (BitConverter.Int64BitsToDouble p)
    | TString   -> PString p
    | TTime     -> PTime (DateTime.FromBinary(p))

  let toUntyped =
    function
    | PInt p    -> p
    | PFloat d  -> BitConverter.DoubleToInt64Bits(d)
    | PString p -> p
    | PTime t   -> t.ToBinary()

  let serialize vp =
    BitConverter.GetBytes(vp |> toUntyped)

  let hash vp =
    vp |> toUntyped

  let serializer =
    FixedLengthUnionSerializer<ValuePointer>
      ([|
        Int64Serializer()
        FloatSerializer()
        Int64Serializer()
        DateTimeSerializer()
      |])

module Record =
  let toType record =
    record |> Array.map Value.toType

module RecordPointer =
  let hash recordPointer =
    recordPointer |> Array.map ValuePointer.hash |> Array.hash |> int64

  let serializer len =
    FixedLengthArraySerializer(ValuePointer.serializer, len)

  let tryId recordPointer =
    match recordPointer |> Array.tryHead with
    | Some (PInt recordId) -> recordId |> Some
    | _ -> None

  let dropId (recordPointer: RecordPointer) =
    recordPointer.[1..]

  let readFromStream fields (stream: Stream) =
    [|
      for Field (_, type') in fields do
        yield stream |> Stream.readInt64 |> ValuePointer.ofUntyped type'
    |]

  let writeToStream (stream: Stream) recordPointer =
    for valuePointer in recordPointer do
      stream |> Stream.writeInt64 (valuePointer |> ValuePointer.toUntyped)

module Field =
  let toType (Field (_, type')) =
    type'

  let int name =
    Field (name, TInt)

  let float name =
    Field (name, TFloat)

  let string name =
    Field (name, TString)

  let time name =
    Field (name, TTime)

module TableSchema =
  let empty name =
    {
      Name              = name
      Fields            = [||]
      Indexes           = [||]
    }

  let toFields (schema: TableSchema) =
    Array.append [| Field ("id", TInt) |] schema.Fields

module Mortal =
  let maxLifeSpan =
    Int64.MaxValue

  let create t value =
    {
      Begin     = t
      End       = maxLifeSpan
      Value     = value
    }

  let isAliveAt t (mortal: Mortal<_>) =
    mortal.Begin <= t && t < mortal.End

  let valueIfAliveAt t (mortal: Mortal<_>) =
    if mortal |> isAliveAt t
    then mortal.Value |> Some
    else None

  let kill t (mortal: Mortal<_>) =
    if mortal |> isAliveAt t
    then { mortal with End = t }
    else mortal

  let map (f: 'x -> 'y) (m: Mortal<'x>): Mortal<'y> =
    {
      Begin     = m.Begin
      End       = m.End
      Value     = f m.Value
    }

  let readFromStream readValue (stream: Stream) =
    let beginRevision   = stream |> Stream.readInt64
    let endRevision     = stream |> Stream.readInt64
    let value           = stream |> readValue
    in
      {
        Begin     = beginRevision
        End       = endRevision
        Value     = value
      }

  let writeToStream writeValue (stream: Stream) (this: Mortal<_>) =
    stream |> Stream.writeInt64 this.Begin
    stream |> Stream.writeInt64 this.End
    stream |> writeValue this.Value
    
  /// Set to `t` the end of lifespan of the mortal value written at the current position.
  /// This doesn't modify the position.
  let killInStream t (stream: Stream) =
    stream.Seek(8L, SeekOrigin.Current) |> ignore
    stream |> Stream.writeInt64 t
    stream.Seek(-16L, SeekOrigin.Current) |> ignore

type MemoryRevisionServer(_id: RevisionId) =
  inherit RevisionServer()
  let mutable _id = _id

  new() =
    MemoryRevisionServer(0L)

  override this.Current =
    _id

  override this.Next =
    _id + 1L

  override this.Increase() =
    Interlocked.Increment(& _id)
