/*
  Run as a SQL Server sysadmin or approved deployment identity with sqlcmd:
  sqlcmd -S localhost -E -v DatabaseName="ADDS_PIM" WorkerLogin="EXAMPLE\gMSA_PIM_Worker$" ApiLogin="EXAMPLE\gMSA_PIM_API$"
*/
:setvar DatabaseName "ADDS_PIM"
:setvar WorkerLogin "EXAMPLE\gMSA_PIM_Worker$"
:setvar ApiLogin "EXAMPLE\gMSA_PIM_API$"

IF DB_ID(N'$(DatabaseName)') IS NULL
BEGIN
    CREATE DATABASE [$(DatabaseName)];
END;
GO

USE [master];
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(WorkerLogin)')
BEGIN
    CREATE LOGIN [$(WorkerLogin)] FROM WINDOWS;
END;

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(ApiLogin)')
BEGIN
    CREATE LOGIN [$(ApiLogin)] FROM WINDOWS;
END;
GO

USE [$(DatabaseName)];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(WorkerLogin)')
BEGIN
    CREATE USER [$(WorkerLogin)] FOR LOGIN [$(WorkerLogin)];
END;

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(ApiLogin)')
BEGIN
    CREATE USER [$(ApiLogin)] FOR LOGIN [$(ApiLogin)];
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'ADDS_PIM_WorkerRuntime')
BEGIN
    CREATE ROLE [ADDS_PIM_WorkerRuntime];
END;

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'ADDS_PIM_ApiRuntime')
BEGIN
    CREATE ROLE [ADDS_PIM_ApiRuntime];
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_role_members AS membership
    INNER JOIN sys.database_principals AS role_principal ON role_principal.principal_id = membership.role_principal_id
    INNER JOIN sys.database_principals AS member_principal ON member_principal.principal_id = membership.member_principal_id
    WHERE role_principal.name = N'ADDS_PIM_WorkerRuntime'
      AND member_principal.name = N'$(WorkerLogin)'
)
BEGIN
    ALTER ROLE [ADDS_PIM_WorkerRuntime] ADD MEMBER [$(WorkerLogin)];
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_role_members AS membership
    INNER JOIN sys.database_principals AS role_principal ON role_principal.principal_id = membership.role_principal_id
    INNER JOIN sys.database_principals AS member_principal ON member_principal.principal_id = membership.member_principal_id
    WHERE role_principal.name = N'ADDS_PIM_ApiRuntime'
      AND member_principal.name = N'$(ApiLogin)'
)
BEGIN
    ALTER ROLE [ADDS_PIM_ApiRuntime] ADD MEMBER [$(ApiLogin)];
END;

IF OBJECT_ID(N'dbo.WorkerCommands', N'U') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.WorkerCommands TO [ADDS_PIM_WorkerRuntime];
    DENY DELETE ON OBJECT::dbo.WorkerCommands TO [ADDS_PIM_WorkerRuntime];
END;
GO

/*
  The API has no schema-alter or DELETE authority. It may read current
  authorization data and append/transition the request, MFA, replay and audit
  records required by the accepted MVP workflows.
*/
DECLARE @ApiRuntimeObjects TABLE (ObjectName sysname NOT NULL);
INSERT INTO @ApiRuntimeObjects (ObjectName) VALUES
    (N'DirectoryScopes'), (N'Persons'), (N'DirectoryAccounts'),
    (N'PersonAccountLinks'), (N'GroupPolicies'), (N'TicketReferencePatterns'), (N'TargetGroups'),
    (N'GroupApprovers'), (N'DirectEntitlements'), (N'MembershipRequests'),
    (N'MembershipRequestStatusHistory'), (N'AuditEvents'),
    (N'ApiRequestReplays'), (N'WebSigningCertificates'),
    (N'TotpFactors'), (N'TotpUsedTimeSteps'), (N'Fido2Credentials'),
    (N'Fido2Challenges'), (N'MfaTransactions'), (N'DirectoryReconciliationRuns'),
    (N'DirectoryReconciliationFindings'), (N'PurgeEventOutbox'),
    (N'TechnicalErrorLogEntries'), (N'WorkerServerCertificateObservations');

DECLARE @ObjectName sysname;
DECLARE api_object_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT ObjectName FROM @ApiRuntimeObjects WHERE OBJECT_ID(N'dbo.' + ObjectName, N'U') IS NOT NULL;

OPEN api_object_cursor;
FETCH NEXT FROM api_object_cursor INTO @ObjectName;
WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @Sql nvarchar(max) = N'GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.' + QUOTENAME(@ObjectName) + N' TO [ADDS_PIM_ApiRuntime]; DENY DELETE ON OBJECT::dbo.' + QUOTENAME(@ObjectName) + N' TO [ADDS_PIM_ApiRuntime];';
    EXEC sys.sp_executesql @Sql;
    FETCH NEXT FROM api_object_cursor INTO @ObjectName;
END;
CLOSE api_object_cursor;
DEALLOCATE api_object_cursor;
GO

/* Ticket-reference patterns are ordinary, administrator-managed configuration.
   Their deletion is audit-recorded by the API but is not a purge operation. */
REVOKE DELETE ON OBJECT::dbo.[TicketReferencePatterns] FROM [ADDS_PIM_ApiRuntime];
GRANT DELETE ON OBJECT::dbo.[TicketReferencePatterns] TO [ADDS_PIM_ApiRuntime];
GO

/*
  Purge graph: the API may delete only local operational identity
  records after its signed, server-rechecked purge workflow. Audit, replay,
  reconciliation and outbox evidence remains non-deletable for the API role.
*/
DECLARE @ApiPurgeDeleteObjects TABLE (ObjectName sysname NOT NULL);
INSERT INTO @ApiPurgeDeleteObjects (ObjectName) VALUES
    (N'Persons'), (N'DirectoryAccounts'), (N'PersonAccountLinks'),
    (N'GroupPolicies'), (N'TargetGroups'), (N'GroupApprovers'), (N'DirectEntitlements'),
    (N'MembershipRequests'), (N'MembershipRequestStatusHistory'),
    (N'MfaTransactions'), (N'TotpFactors'), (N'TotpUsedTimeSteps'),
    (N'Fido2Credentials'), (N'Fido2Challenges');

DECLARE @PurgeObjectName sysname;
DECLARE api_purge_object_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT ObjectName FROM @ApiPurgeDeleteObjects WHERE OBJECT_ID(N'dbo.' + ObjectName, N'U') IS NOT NULL;

OPEN api_purge_object_cursor;
FETCH NEXT FROM api_purge_object_cursor INTO @PurgeObjectName;
WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @PurgeSql nvarchar(max) = N'REVOKE DELETE ON OBJECT::dbo.' + QUOTENAME(@PurgeObjectName) + N' FROM [ADDS_PIM_ApiRuntime]; GRANT DELETE ON OBJECT::dbo.' + QUOTENAME(@PurgeObjectName) + N' TO [ADDS_PIM_ApiRuntime];';
    EXEC sys.sp_executesql @PurgeSql;
    FETCH NEXT FROM api_purge_object_cursor INTO @PurgeObjectName;
END;
CLOSE api_purge_object_cursor;
DEALLOCATE api_purge_object_cursor;
GO

/*
  Schema deployment is deliberately separate. Apply the reviewed EF Core
  migrations with a deployment identity, then rerun this script once to grant
  the WorkerCommands runtime rights.
*/
