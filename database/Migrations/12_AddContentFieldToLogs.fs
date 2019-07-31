// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

[<Migration(12L, "Add Content Field to Logs Table")>]
type AddContentFieldToLogsTable() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    ALTER TABLE logs ADD COLUMN content text NULL;
    """)

  override __.Down() =
    base.Execute("""
    ALTER TABLE logs DROP COLUMN content;
    """)
