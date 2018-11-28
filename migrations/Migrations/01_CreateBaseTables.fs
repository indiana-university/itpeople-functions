namespace Migrations
open SimpleMigrations

[<Migration(1L, "Create Base Table")>]
type CreateBaseTables() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    CREATE TABLE units ( 
      id SERIAL PRIMARY KEY,
      name TEXT NOT NULL UNIQUE,
      description TEXT NOT NULL,
      url TEXT NULL 
    );

    CREATE TABLE departments ( 
      id SERIAL PRIMARY KEY,
      name TEXT NOT NULL UNIQUE,
      description TEXT NOT NULL,
      displayUnits BOOLEAN NOT NULL DEFAULT FALSE 
    );

    CREATE TABLE people (
      id SERIAL PRIMARY KEY,
      hash TEXT NOT NULL,
      netId TEXT NOT NULL UNIQUE,
      name TEXT NOT NULL,
      position TEXT NOT NULL,
      location TEXT NOT NULL,
      campus TEXT NOT NULL,
      campusPhone TEXT NOT NULL,
      campusEmail TEXT NOT NULL,
      expertise TEXT NULL,
      notes TEXT NOT NULL,
      photoUrl TEXT NOT NULL,
      responsibilities INTEGER NOT NULL DEFAULT 0,
      tools INTEGER NOT NULL DEFAULT 7,
      hrDepartmentId INTEGER NULL REFERENCES departments(id) 
    );

    CREATE TABLE supportedDepartments (
      unitId INTEGER REFERENCES units(id),
      departmentId INTEGER REFERENCES departments(id),
      PRIMARY KEY (unitId, departmentId) 
    );

    CREATE TABLE unitRelations (
      childUnitId INTEGER REFERENCES units(id),
      parentUnitId INTEGER REFERENCES units(id),
      PRIMARY KEY (childUnitId, parentUnitId) 
    );
    
    CREATE TABLE unitMembers (
      unitId INTEGER REFERENCES units(id),
      personId INTEGER REFERENCES people(id),
      title TEXT NULL,
      role INTEGER NOT NULL DEFAULT 2,
      percentage INTEGER NOT NULL DEFAULT 100,
      tools INTEGER NOT NULL DEFAULT 0,
      PRIMARY KEY (unitId, personId)
    )

    """)

  override __.Down() =
    base.Execute("""
    DROP TABLE IF EXISTS unitMembers;
    DROP TABLE IF EXISTS unitRelations;
    DROP TABLE IF EXISTS supportedDepartments;
    DROP TABLE IF EXISTS units;
    DROP TABLE IF EXISTS people;
    DROP TABLE IF EXISTS departments;
""")
