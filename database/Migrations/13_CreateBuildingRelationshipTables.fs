// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

[<Migration(13L, "Create Building Relationship Tables")>]
type CreateBuildingRelationshipTables() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    CREATE TABLE buildings ( 
      id SERIAL NOT NULL,
      name TEXT NOT NULL,
      code TEXT NOT NULL UNIQUE,
      address TEXT NULL,
      city TEXT NULL,
      state TEXT NULL,
      country TEXT NULL,
      post_code TEXT NULL,
      PRIMARY KEY (id)
    );

    CREATE TABLE building_relationships (
      id SERIAL NOT NULL,
      unit_id INTEGER NOT NULL REFERENCES units(id),
      building_id INTEGER NOT NULL REFERENCES buildings(id),
      PRIMARY KEY (id)
    );
        """)

  override __.Down() =
    base.Execute("""
    DROP TABLE IF EXISTS building_relationships CASCADE;
    DROP TABLE IF EXISTS buildings CASCADE;
""")
