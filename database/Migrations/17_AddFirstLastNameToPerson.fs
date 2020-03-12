// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

[<Migration(17L, "Add first and last name column to people table")>]
type AddFirstLastNameToPerson() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    ALTER TABLE people ADD COLUMN name_first text NULL;
    ALTER TABLE people ADD COLUMN name_last text NULL;
    ALTER TABLE hr_people ADD COLUMN name_first text NULL;
    ALTER TABLE hr_people ADD COLUMN name_last text NULL;
    """)

  override __.Down() =
    base.Execute("""
    ALTER TABLE hr_people DROP COLUMN name_last;
    ALTER TABLE hr_people DROP COLUMN name_first;
    ALTER TABLE people DROP COLUMN name_last;
    ALTER TABLE people DROP COLUMN name_first;
    """)
