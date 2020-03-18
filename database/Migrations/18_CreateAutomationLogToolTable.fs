// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

[<Migration(18L, "Create automationlog_tools table")>]
type CreateAutomationLogToolsTable() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    CREATE TABLE automationlog_tools (
      id SERIAL NOT NULL,
      ts timestamp without time zone default (now() at time zone 'utc'),
      netid TEXT NOT NULL,
      tool_name TEXT NOT NULL,
      tool_path TEXT NOT NULL,
      change_type TEXT NOT NULL,
      unit_id TEXT NULL,
      unit_name TEXT NULL,
      PRIMARY KEY (id)
    );
    """)

  override __.Down() =
    base.Execute("""
    DROP TABLE IF EXISTS automationlog_tools CASCADE;
    """)
