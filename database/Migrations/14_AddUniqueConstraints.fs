// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Migrations
open SimpleMigrations

[<Migration(14L, "Add Unique Constraints")>]
type AddUniqueConstraints() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
ALTER TABLE support_relationships ADD CONSTRAINT support_relationships_unique_unitid_departmentid UNIQUE (unit_id, department_id);
ALTER TABLE building_relationships ADD CONSTRAINT building_relationships_unique_unitid_buildingid UNIQUE (unit_id, building_id);
ALTER TABLE unit_members ADD CONSTRAINT unit_members_unique_unitid_personid UNIQUE (unit_id, person_id);
ALTER TABLE unit_member_tools ADD CONSTRAINT unit_member_tools_unique_toolid_membershipid UNIQUE (tool_id, membership_id);
""")

  override __.Down() =
    base.Execute("""
ALTER TABLE support_relationships DROP CONSTRAINT support_relationships_unique_unitid_departmentid;
ALTER TABLE building_relationships DROP CONSTRAINT building_relationships_unique_unitid_buildingid;
ALTER TABLE unit_members DROP CONSTRAINT unit_members_unique_unitid_personid;
ALTER TABLE unit_member_tools DROP CONSTRAINT unit_member_tools_unique_toolid_membershipid;
""")
