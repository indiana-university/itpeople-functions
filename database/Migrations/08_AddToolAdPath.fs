
// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

[<Migration(8L, "Add AD Path to Tool table")>]
type AddToolAdPath() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    ALTER TABLE tools ADD COLUMN ad_path TEXT NOT NULL DEFAULT '';
    """)

  override __.Down() =
    base.Execute("""
    ALTER TABLE tools DROP COLUMN ad_path;
""")
