namespace Migrations
open SimpleMigrations

[<Migration(1L, "Create Base Table")>]
type CreateBaseTables() =
  inherit Migration()

  override __.Up() =
    base.Execute("""
    CREATE TABLE Departments( 
      Id INT NOT NULL IDENTITY PRIMARY KEY,
      Name VARCHAR(16) NOT NULL UNIQUE,
      Description VARCHAR(128) NULL
    )

    CREATE TABLE Units( 
      Id INT NOT NULL IDENTITY PRIMARY KEY,
      Name VARCHAR(16) NOT NULL UNIQUE,
      Description VARCHAR(128) NULL
    )

    CREATE TABLE Users (
      Id INT NOT NULL IDENTITY PRIMARY KEY,
      Hash VARCHAR(128) NOT NULL,
      NetId VARCHAR(16) NOT NULL UNIQUE,
      Name VARCHAR(128) NOT NULL,
      Role TINYINT NOT NULL,
      Position VARCHAR(128) NOT NULL,
      Location VARCHAR(256) NULL,
      Campus VARCHAR(16) NOT NULL,
      CampusPhone VARCHAR(16) NULL,
      CampusEmail VARCHAR(32) NOT NULL,
      Expertise VARCHAR(1024) NULL,
      Responsibilities VARCHAR(1024) NULL,
      HrDepartmentId INT NOT NULL,
      UnitId INT NOT NULL,
      CONSTRAINT FK_HrDepartment 
        FOREIGN KEY (HrDepartmentId) REFERENCES Departments (Id),
      CONSTRAINT FK_Unit 
        FOREIGN KEY (UnitId) REFERENCES Units (Id)
    )

    CREATE TABLE SupportedDepartments (
      UserId INT,
      DepartmentId INT,
      CONSTRAINT PK_UserDepartment PRIMARY KEY (UserId, DepartmentId),
      CONSTRAINT FK_User 
        FOREIGN KEY (UserId) REFERENCES Users (Id),
      CONSTRAINT FK_Department 
        FOREIGN KEY (DepartmentId) REFERENCES Departments (Id) 
    )
""")

  override __.Down() =
    base.Execute("""
    DROP TABLE SupportedDepartments
    DROP TABLE Users
    DROP TABLE Units
    DROP TABLE Departments
""")
