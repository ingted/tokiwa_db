﻿namespace TokiwaDb.Core

open System
open System.IO
open TokiwaDb.Core.FsSerialize.Public

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Database =
  let transact (f: unit -> 'x) (this: BaseDatabase) =
    let () =
      this.Transaction.Begin()
    in
      try
        let x = f ()
        let () = this.Transaction.Commit()
        in x
      with
      | _ ->
        this.Transaction.Rollback()
        reraise ()

type RepositoryDatabaseConfig =
  {
    CurrentRevision: RevisionId
  }

type RepositoryDatabase(_repo: Repository) as this =
  inherit ImplDatabase()

  let _tableRepo =
    _repo.AddSubrepository("tables")

  let _storageSource =
    _repo.Add("storage")

  let _storageHashTableSource =
    _repo.Add("storage.ht_index")

  let _storage =
    StreamSourceStorage(_storageSource, _storageHashTableSource)

  let _configSource =
    _repo.Add("config")

  let _config =
    try
      use stream = _configSource.OpenRead()
      in stream |> Stream.deserialize<RepositoryDatabaseConfig> |> Some
    with | _ -> None

  let _revisionServer =
    let currentRevision =
      match _config with
      | Some config -> config.CurrentRevision
      | None -> 0L
    in
      MemoryRevisionServer(currentRevision)

  let _saveConfig () =
    let config =
      {
        CurrentRevision     = _revisionServer.Current
      }
    in
      _configSource.WriteAll(fun streamSource ->
        use stream          = streamSource.OpenReadWrite()
        in stream |> Stream.serialize<RepositoryDatabaseConfig> config |> ignore
        )

  let _tables: ResizeArray<ImplTable> =
    _tableRepo.AllSubrepositories()
    |> Seq.map (fun repo ->
      RepositoryTable(this, repo.Name |> int64, repo) :> ImplTable
      )
    |> ResizeArray.ofSeq

  let _transaction = MemoryTransaction(this.Perform, _revisionServer) :> ImplTransaction

  let _createTable schema =
    lock _transaction.SyncRoot (fun () ->
      let tableId         = _tables.Count |> int64
      let revisionId      = _revisionServer.Increase()
      let repo            = _tableRepo.AddSubrepository(string tableId)
      let table           = RepositoryTable.Create(this, tableId, repo, schema, revisionId) :> ImplTable
      /// Add table.
      _tables.Add(table)
      /// Return the new table.
      table
      )

  let _perform (operation: Operation) =
    for (KeyValue (tableId, records)) in operation.InsertRecords do
      _tables.[int tableId].PerformInsert(records.ToArray())
    for (KeyValue (tableId, recordIds)) in operation.RemoveRecords do
      _tables.[int tableId].PerformRemove(recordIds.ToArray())
    for tableId in operation.DropTable do
      _tables.[int tableId].PerformDrop()

  let _disposable =
    new RelayDisposable(_saveConfig) :> IDisposable

  override this.Dispose() =
    _disposable.Dispose()

  override this.Name =
    _repo.Name

  override this.CurrentRevisionId =
    _transaction.RevisionServer.Current

  override this.Transaction =
    _transaction :> Transaction

  override this.ImplTransaction =
    _transaction

  override this.Storage =
    _storage :> Storage

  override this.ImplTables =
    _tables :> seq<ImplTable>

  override this.CreateTable(schema: TableSchema) =
    _createTable schema

  override this.Perform(operation) =
    _perform operation

type MemoryDatabase(_name: string) =
  inherit RepositoryDatabase(MemoryRepository(_name))

type DirectoryDatabase(_dir: DirectoryInfo) =
  inherit RepositoryDatabase(FileSystemRepository(_dir))
