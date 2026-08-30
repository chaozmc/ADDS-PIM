# ADDS-PIM installer

Run `Install-ADDS-PIM.ps1` from an elevated PowerShell session on the
target Windows Server. It is the only script you need to run - it asks
interactively for every variable it needs, then executes each numbered
step below in order.

This tree is self-contained: it does not call any script in the parent
`scripts\` or `dev-scripts\` folders. Everything an operator (or anyone
debugging a failed run) needs to understand is in this folder, not hidden
one call away in a script that isn't obviously part of the install path.
That was a deliberate decision after a 2026-08-10 second-host install
found several surprising, hard-to-discover behaviors buried inside the
older per-purpose scripts.

## Steps

| # | Script | What it does |
|---|---|---|
| 01 | `01_HostPrerequisites.ps1` | IIS role/features + Windows Authentication, current .NET ASP.NET Core Hosting Bundle, RSAT AD PowerShell tools, optional SQL Server Express. |
| 02 | `02_CertificatesAndIdentities.ps1` | Resolves a writable domain controller (auto-discovered site-aware via the AD DS locator, or validates the FQDN you supplied) and confirms the forest's Privileged Access Management optional feature is enabled — without it ADDS-PIM cannot grant time-limited memberships. Validates the three gMSAs are installed and usable. For Web/API/Worker TLS and the API-to-Worker mTLS client certificate, prompts for an existing thumbprint or generates a self-signed one. Always generates the Web signing and TOTP protection certificates unless existing thumbprints are supplied. Grants each certificate's owning gMSA read access to its private key. |
| 03 | `03_PublishApplication.ps1` | `dotnet publish` for Web, API, and the AD Worker. |
| 04 | `04_InitializeDatabase.ps1` | EF Core migrations, the versioned `database\Install-ADDS-PIM-Database.sql` bootstrap (before and after migrations), and the `dbo.DirectoryScopes` row this environment's `Directory:ScopeId` needs. |
| 05 | `05_RegisterWebSigningCertificate.ps1` | Registers the Web signing certificate's public key in `dbo.WebSigningCertificates` (ADR-0011). |
| 06 | `06_WriteApplicationSettings.ps1` | Writes `appsettings.Production.json` next to the published `appsettings.json` for Web and API, from every value collected in earlier steps. Never committed to git. |
| 07 | `07_ConfigureIis.ps1` | Web/API application pools under their gMSAs, HTTPS sites bound to the right certificates, the `ADDS.PIM.Api` Event Log source. Refuses to overwrite an already-existing site. |
| 08 | `08_ConfigureWorkerService.ps1` | Worker registry configuration, `ADDS.PIM.AdWorker` Event Log source, and the Windows service (two-stage gMSA creation), started. |
| 09 | `09_Verify.ps1` | Smoke test (API `/health/live`, IIS app pool state, Worker service state) and a "what's next" summary. |

## Resuming a failed run

Every step's collected answers and outputs (certificate thumbprints,
publish paths, the SQL connection string, etc.) are saved to
`C:\ProgramData\ADDS-PIM\Install-State.json` after each step. If a step
fails, fix the underlying problem and re-run:

```powershell
.\Install-ADDS-PIM.ps1 -StartAtStep 5
```

to resume from step 5 without repeating steps 1-4 (and without
re-generating certificates or re-answering every prompt).

## Explicitly out of scope

Creating gMSAs, delegating Active Directory rights, DNS records, firewall
rules, and CA-issued certificate procurement remain separately-approved
operator actions - see `dev-docs/03-security/least-privilege-and-gmsa.md`.
This installer validates these prerequisites exist; it never creates the
privileged AD-side pieces itself.

## Standalone use

Every numbered step script can also be run on its own (it validates its
own prerequisites and is safe to `-WhatIf`), for example to repair a
single piece of an existing install without re-running the whole
sequence.
