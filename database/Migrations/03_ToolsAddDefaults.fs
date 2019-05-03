// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

[<Migration(3L, "Add default tools")>]
type ToolsAddDefaults() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    ALTER TABLE tools
    ADD CONSTRAINT unique_name UNIQUE (name);

    INSERT INTO tools (name, description) VALUES ('IT Pro Web', '')
    ON CONFLICT DO NOTHING;

    INSERT INTO tools (name, description) VALUES ('IUware', '')
    ON CONFLICT DO NOTHING;

    INSERT INTO tools (name, description) VALUES ('MAS Tools', '')
    ON CONFLICT DO NOTHING;

    INSERT INTO tools (name, description) VALUES ('Product Keys', '')
    ON CONFLICT DO NOTHING;

    INSERT INTO tools (name, description) VALUES ('Account Management', '')
    ON CONFLICT DO NOTHING;

    INSERT INTO tools (name, description) VALUES ('AMS Admin', '')
    ON CONFLICT DO NOTHING;

    INSERT INTO tools (name, description) VALUES ('UIPO Unblocker', '')
    ON CONFLICT DO NOTHING;

    INSERT INTO tools (name, description) VALUES ('Superpass', '')
    ON CONFLICT DO NOTHING;
    """)

  override __.Down() =
    base.Execute("""
    ALTER TABLE tools
    DROP CONSTRAINT unique_name;
    """)
