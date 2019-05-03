
// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

// Table Modified
// Column Modified
// Timestamp
// Username of the individual who made the change
// Change type (Add/Delete)
// Value added or removed

[<Migration(6L, "Drop person hash column")>]
type DropPersonHashColumn() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    ALTER TABLE people DROP COLUMN hash;
    """)

  override __.Down() =
    base.Execute("""
    ALTER TABLE people ADD COLUMN hash TEXT NOT NULL DEFAULT '';
""")
