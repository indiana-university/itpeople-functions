// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

[<Migration(2L, "Create Tool Tables")>]
type CreateToolTables() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    CREATE TABLE tool_groups ( 
      id SERIAL NOT NULL,
      name TEXT NOT NULL UNIQUE,
      description TEXT NOT NULL,
      PRIMARY KEY (id)
    );

    CREATE TABLE tools ( 
      id SERIAL NOT NULL,
      name TEXT NOT NULL,
      description TEXT NOT NULL,
      tool_group_id INTEGER NULL REFERENCES tool_groups(id),
      PRIMARY KEY (id)
    );

    CREATE TABLE unit_tool_groups (
      id SERIAL NOT NULL,
      unit_id INTEGER NULL REFERENCES units(id),
      tool_group_id INTEGER NULL REFERENCES tool_groups(id),
      PRIMARY KEY (id)
    );

    CREATE TABLE unit_member_tools (
      id SERIAL NOT NULL,
      membership_id INTEGER NULL REFERENCES unit_members(id),
      tool_id INTEGER NULL REFERENCES tools(id),
      PRIMARY KEY (id)
    );
    """)

  override __.Down() =
    base.Execute("""
    DROP TABLE IF EXISTS unit_member_tools CASCADE;
    DROP TABLE IF EXISTS unit_tool_groups CASCADE;
    DROP TABLE IF EXISTS tools CASCADE;
    DROP TABLE IF EXISTS tool_groups CASCADE;
""")
