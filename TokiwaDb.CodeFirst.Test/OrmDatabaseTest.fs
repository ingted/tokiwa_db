namespace TokiwaDb.CodeFirst.Test

open Persimmon
open Persimmon.Syntax.UseTestNameByReflection
open TokiwaDb.Core
open TokiwaDb.CodeFirst
open TokiwaDb.CodeFirst.Detail

module OrmDatabaseTest =
  let schemas =
    [
      (typeof<Person>, TableSchema.ofModel typeof<Person>)
      (typeof<Song>, TableSchema.ofModel typeof<Song>)
    ]

  let testDb () = new OrmDatabase(new MemoryDatabase("test_db"), schemas)

  let createTest =
    test {
      let db = testDb ()
      do! db.Name |> assertEquals "test_db"
    }

  let tableTest =
    test {
      let db = testDb ()
      do! (db.Table<Person> ()).Name |> assertEquals "Person"
      let! _ = trap { it (db.Table<IModel>()) }
      return ()
    }

  let reopenTest =
    test {
      let implDb = new MemoryDatabase("test_db")
      do! test {
        use db = new OrmDatabase(implDb, schemas)
        db.Table<Person>().Insert(Person(Name = "Miku", Age = 16L))
        return ()
      }
      // We can reopen the database with the same models.
      do! test {
        use db = new OrmDatabase(implDb, schemas)
        do! (db.Table<Person>()).CountAllRecords |> assertEquals 1L
      }
      // Opening with different models, all tables are dropped.
      let anotherSchemas = [schemas |> List.head]
      use db = new OrmDatabase(implDb, anotherSchemas)
      do! (db.Table<Person>()).CountAllRecords |> assertEquals 0L
      let! _ = trap { it (db.Table<Song>()) }
      return ()
    }
