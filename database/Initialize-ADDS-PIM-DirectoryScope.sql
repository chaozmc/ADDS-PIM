/*
  Run as a SQL Server sysadmin or approved deployment identity with sqlcmd:
  sqlcmd -S <server> -E -C -d ADDS_PIM ^
    -v DirectoryScopeId="<guid>" DomainDnsName="<domain>" ForestDnsName="<forest>" ^
    -i database\Initialize-ADDS-PIM-DirectoryScope.sql

  Idempotent one-time bootstrap: creates the dbo.DirectoryScopes row that the
  Directory:ScopeId configured in the Web and API appsettings.Production.json
  must resolve to. Every dbo.DirectoryAccounts / dbo.TargetGroups row carries
  a required foreign key to DirectoryScopes.DirectoryScopeId, so without this
  row nothing - not a single Person, directory account, or target group - can
  be created through the admin UI on a fresh install.

  This intentionally seeds nothing else. Persons, target groups, policies and
  entitlements are real operational data and belong in the admin UI. For a
  disposable demo/test environment with sample data instead, see
  Initialize-ADDS-PIM-MvpAuthorization.ps1.
*/

IF NOT EXISTS (SELECT 1 FROM dbo.DirectoryScopes WHERE DirectoryScopeId = '$(DirectoryScopeId)')
BEGIN
    INSERT INTO dbo.DirectoryScopes (DirectoryScopeId, StableScopeIdentifier, DisplayName, IsActive, CreatedUtc, ModifiedUtc)
    VALUES (
        '$(DirectoryScopeId)',
        N'configured:$(ForestDnsName)/$(DomainDnsName)',
        N'$(ForestDnsName) ($(DomainDnsName))',
        1,
        SYSUTCDATETIME(),
        SYSUTCDATETIME()
    );
    PRINT 'DirectoryScope $(DirectoryScopeId) created.';
END
ELSE
BEGIN
    PRINT 'DirectoryScope $(DirectoryScopeId) already exists; nothing to do.';
END
GO
