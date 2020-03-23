// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

[<Migration(19L, "Create logs_automation table")>]
type CreateAutomationLogTable() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    CREATE TABLE logs_automation
    (
      timestamp timestamp without time zone,
      level character varying(50),
      invocation_id uuid NOT NULL,
      function_name text NOT NULL,
      message text NULL,
      properties json NULL
    );

    -- this table will replace the automationlog_tools table
    DROP TABLE IF EXISTS automationlog_tools CASCADE;
    """)

  override __.Down() =
    base.Execute("""
    DROP TABLE IF EXISTS logs_automation CASCADE;

    -- restore the automationlog_tools table
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
