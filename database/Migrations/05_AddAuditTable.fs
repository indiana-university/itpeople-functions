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

[<Migration(5L, "Add audit table")>]
type AddAuditTable() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    CREATE TABLE audit (
      id SERIAL NOT NULL,
      table_modified TEXT NOT NULL,
      column_modified TEXT NOT NULL,
      timestamp timestamp without time zone,
      username TEXT NOT NULL,
      change_type TEXT NOT NULL,
      value TEXT NOT NULL,
      PRIMARY KEY (id)
    );
    """)

  override __.Down() =
    base.Execute("""
    DROP TABLE IF EXISTS audit CASCADE;
""")
