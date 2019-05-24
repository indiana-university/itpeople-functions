
// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

[<Migration(10L, "Add Notes field to Unit Members table")>]
type AddNotesFieldToUnitMembersTable() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    ALTER TABLE unit_members ADD COLUMN notes TEXT NOT NULL DEFAULT '';
""")

  override __.Down() =
    base.Execute("""
    ALTER TABLE unit_members DROP COLUMN notes;
""")
