// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

[<Migration(15L, "Add HR department description column to HR people table")>]
type AddDeptDescFieldToHrPeopleTable() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    ALTER TABLE hr_people ADD COLUMN hr_department_desc text NULL;
    """)

  override __.Down() =
    base.Execute("""
    ALTER TABLE hr_people DROP COLUMN hr_department_desc;
    """)
