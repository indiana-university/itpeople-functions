// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

[<Migration(3L, "Add Columns to Log Table")>]
type AddColumnsToLogTable() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    ALTER TABLE logs ADD COLUMN ip_address text;
    ALTER TABLE logs ADD COLUMN netid text;
    ALTER TABLE logs RENAME COLUMN raise_date TO timestamp;
    """)

  override __.Down() =
    base.Execute("""
    ALTER TABLE logs DROP COLUMN ip_address;
    ALTER TABLE logs DROP COLUMN netid;
    ALTER TABLE logs RENAME COLUMN timestamp TO raise_date;
    """)
