// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

[<Migration(16L, "Add optional email column to units table")>]
type AddEmailFieldToUnit() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    ALTER TABLE units ADD COLUMN email text NULL;
    """)

  override __.Down() =
    base.Execute("""
    ALTER TABLE units DROP COLUMN email;
    """)
