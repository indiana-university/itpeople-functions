
// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

// Table Modified
// Column Modified
// Timestamp
// Username of the individual who made the change
// Change type (Add/Delete)
// Value added or removed

[<Migration(7L, "Add person service admin column")>]
type AddPersonServiceAdminColumn() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    ALTER TABLE people ADD COLUMN is_service_admin BOOLEAN NOT NULL DEFAULT FALSE;
    
    UPDATE people 
    SET is_service_admin = true
    WHERE netid IN ('jerussel', 'brrund', 'jhoerr', 'kendjone');
    """)

  override __.Down() =
    base.Execute("""
    ALTER TABLE people DROP COLUMN is_service_admin;
""")
