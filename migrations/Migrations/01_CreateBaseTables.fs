// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

[<Migration(1L, "Create Base Table")>]
type CreateBaseTables() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    CREATE TABLE units ( 
      id SERIAL NOT NULL,
      name TEXT NOT NULL,
      description TEXT NOT NULL,
      url TEXT NULL,
      PRIMARY KEY (id)
    );

    CREATE TABLE departments ( 
      id SERIAL NOT NULL,
      name TEXT NOT NULL UNIQUE,
      description TEXT NOT NULL,
      display_units BOOLEAN NOT NULL DEFAULT FALSE,
      PRIMARY KEY (id)
    );

    CREATE TABLE people (
      id SERIAL NOT NULL,
      hash TEXT NOT NULL,
      netid TEXT NOT NULL UNIQUE,
      name TEXT NOT NULL,
      position TEXT NOT NULL,
      location TEXT NOT NULL,
      campus TEXT NOT NULL,
      campus_phone TEXT NOT NULL,
      campus_email TEXT NOT NULL,
      expertise TEXT NULL,
      notes TEXT NOT NULL,
      photo_url TEXT NOT NULL,
      responsibilities INTEGER NOT NULL DEFAULT 0,
      tools INTEGER NOT NULL DEFAULT 7,
      department_id INTEGER NULL REFERENCES departments(id),
      PRIMARY KEY (id)
    );

    CREATE TABLE supported_departments (
      unit_id INTEGER NOT NULL REFERENCES units(id),
      department_id INTEGER NOT NULL REFERENCES departments(id),
      PRIMARY KEY (unit_id, department_id) 
    );

    CREATE TABLE unit_relations (
      child_id INTEGER NOT NULL REFERENCES units(id),
      parent_id INTEGER NOT NULL REFERENCES units(id),
      PRIMARY KEY (child_id, parent_id) 
    );
    
    CREATE TABLE unit_members (
      unit_id INTEGER NOT NULL REFERENCES units(id),
      person_id INTEGER NOT NULL REFERENCES people(id),
      title TEXT NULL,
      role INTEGER NOT NULL DEFAULT 2,
      percentage INTEGER NOT NULL DEFAULT 100,
      tools INTEGER NOT NULL DEFAULT 0,
      PRIMARY KEY (unit_id, person_id)
    );

    """)

  override __.Down() =
    base.Execute("""
    DROP TABLE IF EXISTS unit_members CASCADE;
    DROP TABLE IF EXISTS unit_relations CASCADE;
    DROP TABLE IF EXISTS supported_departments CASCADE;
    DROP TABLE IF EXISTS units CASCADE;
    DROP TABLE IF EXISTS people CASCADE;
    DROP TABLE IF EXISTS departments CASCADE;
""")
