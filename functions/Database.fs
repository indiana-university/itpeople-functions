namespace MyFunctions

open Chessie.ErrorHandling
open Types
open Common
open Dapper
open System.Data.SqlClient
open Fakes

module Database =

    let like (term:string)  = 
        term.Replace("[", "[[]").Replace("%", "[%]") 
        |> sprintf "%%%s%%"


    /// USER

    type IdFilter = {
        Id: Id
    }

    type NetIdFilter = {
        NetId: NetId
    }

    let tryGetFirst seq msg = 
        match seq |> Seq.tryHead with
            | None -> fail (Status.NotFound, msg)
            | Some (resp) -> ok resp

    let queryUserByNetId connStr netId = asyncTrial {
        let fn () = async {
            use cn = new SqlConnection(connStr)
            let! seq = cn.GetListAsync<User>({NetId=netId}) |> Async.AwaitTask
            return ok seq
        }
        let! result = tryfResult Status.InternalServerError "Failed to fetch user by netId" fn
        let! head = tryGetFirst result (sprintf "No user found with netid '%s'" netId)
        return head
    }

    let queryTypeById<'T> connStr id = asyncTrial {
        let fn () = async {
            use cn = new SqlConnection(connStr)
            let! result = cn.GetAsync<'T>(id) |> Async.AwaitTask
            match box result with
            | null -> return fail (Status.NotFound, sprintf "No %s found with id %d" (typeof<'T>.Name) id)
            | _ -> return ok result
        }
        let! result = tryfResult Status.InternalServerError (sprintf "Failed to fetch %s by id %d" (typeof<'T>.Name) id) fn
        return result
    }

    let queryAll<'T> connStr query msg id = asyncTrial {
        let fn() = async {
            use cn = new SqlConnection(connStr)
            let! seq = cn.QueryAsync<'T>(query, {Id=id}) |> Async.AwaitTask
            return ok seq
        }
        let! result = tryfResult Status.InternalServerError msg fn
        return result        
    }

    let queryDepartmentsSupportedByUser connStr userId = asyncTrial {
        let query = """
SELECT d.* FROM Departments d
JOIN SupportedDepartments sd on d.Id = sd.DepartmentId 
WHERE sd.UserId = @Id
ORDER BY Name ASC"""
        let msg = "Failed to query supported departments by user id"
        let! result = queryAll<Department> connStr query msg userId
        return result
    }

    let queryDepartmentsSupportedByUnit connStr unitId = asyncTrial {
        let query = """
SELECT d.* FROM Departments d
JOIN SupportedDepartments sd on d.Id = sd.DepartmentId 
JOIN Users u on u.Id = sd.UserId
WHERE u.UnitId = @Id 
ORDER BY Name ASC"""
        let msg = "Failed to supported departments by unit id"
        let! result = queryAll<Department> connStr query msg unitId
        return result
    }

    let getOrgUnitsInDepartment connStr deptId = asyncTrial {
        let query = """
SELECT un.* FROM Units un
JOIN Users u on un.Id = u.UnitId
WHERE u.HrDepartmentId = @Id 
ORDER BY Name ASC"""
        let msg = "Failed to get org units in department"
        let! result = queryAll<Unit> connStr query msg deptId
        return result
    }

    let getUnitsSupportingDepartment connStr deptId = asyncTrial {
        let query = """
SELECT un.* FROM Units un
JOIN Users u on un.Id = u.UnitId
JOIN SupportedDepartments sd on u.Id = sd.UserId 
WHERE sd.DepartmentId = @Id 
ORDER BY Name ASC"""
        let msg = "Failed to get units supporting department"
        let! result = queryAll<Unit> connStr query msg deptId
        return result
    }

    let queryUserProfile connStr id = asyncTrial {
        let! user = queryTypeById<User> connStr id
        let! unit = queryTypeById<Unit> connStr user.UnitId
        let! dept = queryTypeById<Department> connStr user.HrDepartmentId
        let! supportedDepartments = queryDepartmentsSupportedByUser connStr user.Id
        let profile = {
            User=user
            Unit=unit
            Department=dept
            SupportedDepartments=supportedDepartments
            ToolsAccess=[]
        }       
        return profile
    }

    type SimpleSearchQuery = {
        Term: string
    }

    let querySearch<'T> connStr term conditions = asyncTrial {
        let fn() = async {
            use cn = new SqlConnection(connStr)
            let! seq = cn.GetListAsync<'T>(conditions, {Term=(like term)})  |> Async.AwaitTask
            return ok seq
        }
        let! result = tryfResult Status.InternalServerError (sprintf "Failed to get %s by search term" typeof<'T>.Name ) fn
        return result
    }

    let querySimpleSearch connStr term = asyncTrial {
        let! users = querySearch<User> connStr term "WHERE Name LIKE @Term OR NetId LIKE @Term"
        let! units = querySearch<Unit> connStr term "WHERE Name LIKE @Term OR Description LIKE @Term"
        let! depts = querySearch<Department> connStr term "WHERE Name LIKE @Term OR Description LIKE @Term"
        return {
            Users=users
            Units=units
            Departments=depts
        }
    }

    let queryUnits connStr = asyncTrial {
        let fn () = async {
            use cn = new SqlConnection(connStr)
            let! seq = cn.GetListAsync<Unit>() |> Async.AwaitTask
            return { Units = seq } |> ok 
        }
        let! result = tryfResult Status.InternalServerError "Failed to fetch user by netId" fn
        return result
    }

    type UnitQuery = {
        UnitId: Id
    }

    let getUnitMembers connStr id = asyncTrial {
        let fn() = async {
            use cn = new SqlConnection(connStr)
            let! seq = cn.GetListAsync<User>({UnitId=id})  |> Async.AwaitTask
            return ok seq
        }
        let! result = tryfResult Status.InternalServerError "Failed to get members by unit id" fn
        return result        
    }

    let queryUnit connStr id = asyncTrial {
        let! unit = queryTypeById<Unit> connStr id
        let! people = getUnitMembers connStr id
        let! supportedDepartments = queryDepartmentsSupportedByUnit connStr id
        let admins = people |> Seq.filter (fun p -> p.Role = Role.ItManager)
        let itpros = people |> Seq.filter (fun p -> p.Role = Role.ItPro)
        let selfs = people |> Seq.filter (fun p -> p.Role = Role.SelfReport)
        return {
            Unit=unit
            Admins=admins
            ItPros=itpros
            Selfs=selfs
            SupportedDepartments=supportedDepartments
        }
    }

    let queryDepartments connStr = asyncTrial {
        let fn () = async {
            use cn = new SqlConnection(connStr)
            let! seq = cn.GetListAsync<Department>() |> Async.AwaitTask
            return { Departments = seq } |> ok 
        }
        let! result = tryfResult Status.InternalServerError "Failed to fetch user by netId" fn
        return result
    }



    let queryDepartment connStr id = asyncTrial {
        let! dept = queryTypeById<Department> connStr id
        let! orgUnits = getOrgUnitsInDepartment connStr id
        let! supportingUnits = getUnitsSupportingDepartment connStr id
        return {
            Department=dept
            OrganizationUnits=orgUnits
            SupportingUnits=supportingUnits
        }
    }

    type DatabaseRepository(connectionString:string) =
        let connStr = connectionString

        interface IDataRepository with 
            member this.GetUserByNetId netId = queryUserByNetId connStr netId
            member this.GetProfile id = queryUserProfile connStr id
            member this.GetSimpleSearchByTerm term = querySimpleSearch connStr term
            member this.GetUnits () = queryUnits connStr
            member this.GetUnit id = queryUnit connStr id
            member this.GetDepartments () = queryDepartments connStr 
            member this.GetDepartment id = queryDepartment connStr id
