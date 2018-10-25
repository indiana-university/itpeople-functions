namespace Migrations
open SimpleMigrations

[<Migration(1L, "Create Base Table")>]
type CreateBaseTables() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    CREATE TABLE Units ( 
      Id SERIAL PRIMARY KEY,
      Name text NOT NULL UNIQUE,
      Description text NULL,
      Url text NULL );
    """)

      // CREATE TABLE Departments( 
      //       Id SERIAL PRIMARY KEY,
      //       Name text NOT NULL UNIQUE,
      //       Description text NULL,
      //       DisplayUnits boolean NOT NULL DEFAULT 0
      //     );
//     CREATE TABLE Users (
//       Id INT NOT NULL IDENTITY PRIMARY KEY,
//       Hash VARCHAR(128) NOT NULL,
//       NetId VARCHAR(16) NOT NULL UNIQUE,
//       Name VARCHAR(128) NOT NULL,
//       Position VARCHAR(128) NOT NULL,
//       Location VARCHAR(256) NULL,
//       Campus VARCHAR(16) NOT NULL,
//       CampusPhone VARCHAR(16) NULL,
//       CampusEmail VARCHAR(32) NOT NULL,
//       Expertise VARCHAR(2048) NULL,
//       Notes VARCHAR(2048) NULL,
//       Role TINYINT NOT NULL,
//       Responsibilities INT NOT NULL DEFAULT 0,
//       Tools INT NOT NULL DEFAULT 7,
//       HrDepartmentId INT NOT NULL,
//       CONSTRAINT FK_Users_HrDepartment 
//         FOREIGN KEY (HrDepartmentId) REFERENCES Departments (Id),
//     )

//     CREATE TABLE SupportedDepartments (
//       DepartmentId INT,
//       UnitId INT,
//       CONSTRAINT PK_SupportedDepartments_UserDepartment PRIMARY KEY (UnitId, DepartmentId),
//       CONSTRAINT FK_SupportedDepartments_Department 
//         FOREIGN KEY (DepartmentId) REFERENCES Departments (Id),
//       CONSTRAINT FK_SupportedDepartments_Unit 
//         FOREIGN KEY (UnitId) REFERENCES Units (Id) 
//     )

//     CREATE TABLE UnitMembers (
//       UserId INT,
//       UnitId INT,
//       CONSTRAINT PK_UnitMembers_UnitMember PRIMARY KEY (UserId, UnitId),
//       CONSTRAINT FK_UnitMembers_User 
//         FOREIGN KEY (UserId) REFERENCES Users (Id),
//       CONSTRAINT FK_UnitMembers_Unit 
//         FOREIGN KEY (UnitId) REFERENCES Units (Id) 
//     )
// """

    // DROP TABLE IF EXISTS unitMembers;
    // DROP TABLE IF EXISTS supportedDepartments;
    // DROP TABLE IF EXISTS users;
    // DROP TABLE IF EXISTS departments;

  override __.Down() =
    base.Execute("""
    DROP TABLE IF EXISTS units;
""")
