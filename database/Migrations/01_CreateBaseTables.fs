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
      parent_id INTEGER NULL REFERENCES units(id),
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

    CREATE TABLE support_relationships (
      id SERIAL NOT NULL,
      unit_id INTEGER NOT NULL REFERENCES units(id),
      department_id INTEGER NOT NULL REFERENCES departments(id),
      PRIMARY KEY (id)
    );
    
    CREATE TABLE unit_members (
      id SERIAL NOT NULL,
      unit_id INTEGER NOT NULL REFERENCES units(id),
      person_id INTEGER NOT NULL REFERENCES people(id),
      title TEXT NULL,
      role INTEGER NOT NULL DEFAULT 2,          -- default: member
      percentage INTEGER NOT NULL DEFAULT 100,  -- default: 100%
      tools INTEGER NOT NULL DEFAULT 0,         -- default: no tools
      permissions INTEGER NOT NULL DEFAULT 2,   -- default: viewer
      PRIMARY KEY (id)
    );

    CREATE TABLE logs
    (
      timestamp timestamp without time zone,
      level character varying(50) COLLATE pg_catalog."default",
      elapsed integer,
      status integer,
      method text COLLATE pg_catalog."default",
      function text COLLATE pg_catalog."default",
      parameters text COLLATE pg_catalog."default",
      query text COLLATE pg_catalog."default",
      detail text COLLATE pg_catalog."default",
      exception text COLLATE pg_catalog."default",
      ip_address text COLLATE pg_catalog."default",
      netid text COLLATE pg_catalog."default"
    );
    """)

  override __.Down() =
    base.Execute("""
    DROP TABLE IF EXISTS logs CASCADE;
    DROP TABLE IF EXISTS unit_members CASCADE;
    DROP TABLE IF EXISTS support_relationships CASCADE;
    DROP TABLE IF EXISTS units CASCADE;
    DROP TABLE IF EXISTS people CASCADE;
    DROP TABLE IF EXISTS departments CASCADE;
""")
