// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

[<Migration(4L, "Add department scope flag to tools table")>]
type AlterToolsWithDepartmentScopeFlag() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    ALTER TABLE tools
    ADD COLUMN department_scoped BOOLEAN NOT NULL DEFAULT FALSE;

    UPDATE tools
    SET department_scoped = TRUE
    WHERE name in ('UIPO Unblocker', 'Superpass', 'Account Management', 'AMS Admin');
    """)

  override __.Down() =
    base.Execute("""
    ALTER TABLE tools
    DROP COLUMN department_scoped;
""")
