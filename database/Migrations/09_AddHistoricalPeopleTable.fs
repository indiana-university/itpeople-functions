
// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

[<Migration(9L, "Add Historical People table")>]
type AddHistoricalPeopleTable() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    CREATE TABLE historical_people (
      netid TEXT NOT NULL,
      metadata JSON NOT NULL,
      removed_on timestamp without time zone
    );""")

  override __.Down() =
    base.Execute("""
    DROP TABLE IF EXISTS historical_people CASCADE;
""")
