// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

[<Migration(11L, "Create HR People Table")>]
type CreateHrPeopleTable() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    CREATE TABLE hr_people (
      id SERIAL NOT NULL,
      netid TEXT NOT NULL UNIQUE,
      name TEXT NOT NULL,
      position TEXT NULL,
      campus TEXT NOT NULL,
      campus_phone TEXT NULL,
      campus_email TEXT NULL,
      hr_department TEXT NOT NULL,
      PRIMARY KEY (id)
    );""")

  override __.Down() =
    base.Execute("""
    DROP TABLE IF EXISTS hr_people CASCADE;
    """)
