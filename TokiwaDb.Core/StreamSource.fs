﻿namespace TokiwaDb.Core

open System.IO

type [<AbstractClass>] StreamSource() =
  abstract member OpenReadWrite: unit -> Stream
  abstract member OpenRead: unit -> Stream
  abstract member OpenAppend: unit -> Stream
  abstract member Clear: unit -> unit
  abstract member Length: int64

/// Stream source based on a file.
/// Note: OpenRead doesn't lock the file for reading or writing.
type FileStreamSource(_file: FileInfo) =
  inherit StreamSource()

  override this.OpenReadWrite() =
    _file.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite) :> Stream

  override this.OpenRead() =
    _file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite) :> Stream

  override this.OpenAppend() =
    _file.Open(FileMode.Append, FileAccess.Write, FileShare.Read) :> Stream

  override this.Clear() =
    if _file |> FileInfo.exists then
      _file.Delete()

  override this.Length =
    _file |> FileInfo.length

type MemoryStreamSource(_buffer: array<byte>) =
  inherit StreamSource()

  let mutable _buffer = _buffer

  new() = new MemoryStreamSource([||])

  member private this.Open(index) =
    let stream =
      { new MemoryStream() with
          override this.Close() =
            _buffer <- this.ToArray()
            base.Close()
      }
    let ()    = stream.Write(_buffer, 0, _buffer.Length)
    let _     = stream.Seek(index, SeekOrigin.Begin)
    in stream

  override this.OpenReadWrite() =
    this.Open(index = 0L) :> Stream

  override this.OpenRead() =
    this.OpenReadWrite()

  override this.OpenAppend() =
    this.Open(index = _buffer.LongLength) :> Stream

  override this.Clear() =
    _buffer <- [||]

  override this.Length =
    _buffer.LongLength