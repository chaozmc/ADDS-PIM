# Overview

## What ADDS-PIM is

ADDS-PIM (Active Directory Domain Services Privileged Identity Management) is a self-contained web application for granting **Active Directory group memberships in a controlled, time-limited, and fully auditable way**. Users who are entitled to a given privileged group can request membership in it for a bounded duration. That membership is created with a TTL (time-to-live) via the AD DS Privileged Access Management (PAM) Optional Feature, and Active Directory itself expires and removes it once the TTL runs out - the temporary access does not depend on someone remembering to revoke it later.

In short: instead of people holding *standing* membership in sensitive AD groups indefinitely, they request access when they actually need it, get it for exactly as long as it's needed, and lose it automatically afterwards, with every step of the process recorded.

For a walkthrough of how the system is put together internally, see `architecture.md`. For the authorization, MFA, and request-signing rules that decide whether a given request is actually granted, see `security-model.md`.

## Why this exists

Standing membership in privileged AD groups - Domain Admins, Tier-0 service groups, delegated administrative groups, and similar - is a persistent attack surface. Every account that holds this kind of access is a target around the clock, regardless of whether the access is being used at that moment. If such an account is compromised, an attacker inherits its privileges immediately and for as long as the membership stands, which in many environments is effectively forever.

Time-limited, request-driven group membership shrinks that exposure window down to only the period the access is actually needed for. It also creates a natural, structured record of *who* requested *what* access, *why*, whether MFA was performed, whether the request was approved, and what actually happened in the directory as a result. That combination - bounded exposure plus a trustworthy audit trail - is the core problem ADDS-PIM addresses.

Every membership request must carry a non-empty business justification. That justification is stored immutably alongside the request as part of its history; it cannot be turned off by group policy, and it cannot be edited after the fact.

## Target platform

ADDS-PIM is built for on-premises Windows enterprise environments, not for the cloud:

- Microsoft .NET and ASP.NET Core, with a Blazor frontend
- Microsoft SQL Server (SQL Server Express is sufficient for development)
- Microsoft IIS on Windows Server
- Active Directory Domain Services, using the AD DS PAM Optional Feature for TTL-based memberships
- Group Managed Service Accounts (gMSA) for the system's own service identities

The solution is designed to run entirely under Windows Server, without a dependency on cloud services, and to work inside network-isolated enterprise environments. See `dependencies.md` for the concrete package- and runtime-level dependency list, and `operations.md` for deployment and environment details.

## Initial product scope

The first version of ADDS-PIM covers:

- Windows Authentication and Kerberos single sign-on
- separate user and administration views
- management of which target groups are available for requests, and who is entitled to request them
- configurable minimum, default, and maximum TTLs, with fixed TTL steps a user can choose from
- signed frontend requests submitted to a versioned backend REST API
- a separate AD Worker component that executes AD changes through a fixed set of typed operations (see [active-directory-worker.md](active-directory-worker.md) for how it talks to Active Directory today)
- multi-factor authentication via TOTP or FIDO2/passkey
- persistence in SQL Server, with complete audit logging and Windows Event Log integration
- setup tooling, schema creation, and database migrations

The specific authorization decision behind any given request is defined in `security-model.md`; a request is only considered successfully completed once it has passed the verification described in that document (the AD change is read back from Active Directory and confirmed before the request is marked successful). Identity roles and the request lifecycle are covered in `identity-and-lifecycle.md`.

The first version is scoped to a single AD domain and a single forest. That said, it is designed so this doesn't unnecessarily foreclose extending to more complex topologies later.

## Non-goals for the first version

ADDS-PIM deliberately does **not** try to do the following, at least not yet:

- **Replace Microsoft Entra PIM.** ADDS-PIM is a focused, on-premises AD group-membership tool, not a full identity governance platform.
- **Run in the cloud, on Linux, or as a production container workload.** The target platform is Windows Server, IIS, and on-premises Active Directory.
- **Manage arbitrary AD attributes or grant permanent standing permissions.** Scope is limited to time-limited group memberships, not general-purpose AD administration.
- **Execute arbitrary PowerShell.** The AD Worker runs a fixed, versioned set of operations - it is never a channel for running arbitrary or user-supplied scripts.
- **Provide a full workflow engine or complex multi-stage approvals.** Approval support exists, but it is not a general-purpose business process engine.
- **Support multi-forest topologies, SAML, OpenID Connect, or high availability.** These are explicitly out of scope for the initial version.
- **Automatically create gMSAs.** Service accounts are provisioned by an administrator, not by the application.
- **Automatically enable the AD DS PAM Optional Feature.** Enabling this feature is a deliberate, explicit administrative action outside the application's control - ADDS-PIM relies on it being enabled, it does not enable it itself.

## Design priorities

Where these concerns compete, ADDS-PIM prioritizes them in this order: security, correct authorization decisions, auditability, least privilege, data integrity, reliable AD execution and verification, maintainability, usability, performance, and convenience. In practice this means security and correctness-related work takes precedence over convenience features, and incomplete, ambiguous, stale, or unverifiable security information leads to a request being denied rather than allowed through.

At the same time, the project aims to keep security, audit, and operational mechanisms proportionate to the actual risk involved: the core function - controlled, time-limited, traceable AD group membership - takes priority over building out additional operational infrastructure beyond what that core function actually needs.

## Where to go next

- `architecture.md` - component breakdown and how the pieces fit together
- `security-model.md` - authorization, MFA, and request-signing rules
- `identity-and-lifecycle.md` - identity roles and the membership request lifecycle
- `data-model.md` - persisted entities and schema
- `active-directory-worker.md` - how AD changes are actually executed and verified
- `operations.md` - deployment, configuration, and running the system
