<p align="center"> <img src="adds-pim-hero.svg" alt="ADDS-PIM" width="480" /> </p>

# ADDS-PIM

ADDS-PIM is a Privileged Identity Management (PIM) system for **time-limited Active Directory group memberships**. Instead of standing/permanent membership in privileged AD groups, users request temporary access with a bounded TTL; the system grants it, verifies the grant actually took effect in Active Directory, and automatically removes it again once the TTL expires - with a full audit trail of who requested what, who approved it, and what actually happened in the directory.

## Why

Standing membership in privileged AD groups (Domain Admins, Tier-0 service groups, delegated administrative groups, etc.) is a persistent attack surface: every account with standing access is a target every hour of every day, whether or not the access is actually being used right now. Time-limited, request-driven membership shrinks that window to only the time the access is actually needed, without requiring people to remember to clean up after themselves.

## Architecture

Clean/onion architecture with a strict one-way dependency flow and a hard trust boundary around Active Directory execution:

- **`ADDS.PIM.Web`** - Blazor frontend. Displays/prepares/confirms/signs requests but is never authoritative for identity, entitlement, TTL, security class, or MFA.
- **`ADDS.PIM.Api`** - REST API; the authoritative orchestrator. Makes the final authorization decision using fresh server-side data immediately before execution.
- **`ADDS.PIM.Application`** - use cases, workflow orchestration, validation, ports consumed by Infrastructure.
- **`ADDS.PIM.Domain`** - entities, value objects, policies, state transitions, domain errors. No infrastructure dependencies.
- **`ADDS.PIM.Infrastructure`** - SQL Server (EF Core), certificates/signing, MFA provider integration, audit sink, Worker communication - the concrete implementations of Application's ports.
- **`ADDS.PIM.Contracts`** - versioned DTOs/transport contracts shared across process boundaries.
- **`ADDS.PIM.AdWorker`** - isolated, privileged service that is the *only* component allowed to modify privileged AD group memberships, via fixed, versioned PowerShell operations. Every AD write is followed by a read-back TTL verification before a request may be marked successful.
- **`ADDS.PIM.Shared`** - small, genuinely cross-cutting primitives.

Non-negotiable invariants:

- Web/API identities are never granted rights to modify privileged AD groups; only the AD Worker can.
- A request is successful only after its AD change has been read back and TTL-verified.
- SQL Server is authoritative for entitlements, policies, request state, idempotency, and audit; Active Directory is authoritative for the effective state of users/groups/memberships.
- Every membership request and worker command carries a globally unique ID; retries must observe existing state.
- Technical logs and business/security audit events are structurally separate; audit records are append-only, never updated in place.

## Getting started

1. Build: `dotnet restore ADDS-PIM.slnx && dotnet build ADDS-PIM.slnx --no-restore`
2. Test: `dotnet test ADDS-PIM.slnx --no-build`
3. Database: bootstrap with `database/Install-ADDS-PIM-Database.sql`, then apply EF Core migrations.
4. Deployment: see `scripts/README.md` for the manual bootstrap path (IIS setup, AD Worker service install, authorization seeding).

`appsettings.json` in `src/ADDS.PIM.Api` and `src/ADDS.PIM.Web` ships with placeholder values (`example.org` domain, all-zero GUIDs/thumbprints). Replace them with your own environment's directory, database, and certificate details before running anything for real.

## Documentation

Full design, security, and operations documentation lives in [`docs/`](docs/README.md), organized for four reader paths - AD operations technicians, architects, security governance staff, and enthusiasts. Start at [`docs/README.md`](docs/README.md).

## Status

Feature-complete beta: time-limited membership requests, MFA (FIDO2/WebAuthn and TOTP), an approval workflow, and admin tooling are implemented and have been exercised end to end in a test environment. Not yet fully hardened or pen-tested for production use.

## License

ADDS-PIM is **source-available, not open source**. It is licensed under the [Prosperity Public License 3.0.0](LICENSE.md).

**Free of charge**, with no time limit:

- personal use - study, experimentation, home labs, hobby projects
- educational institutions, for teaching, study and coursework
- public research organizations doing noncommercial research
- charitable organizations, public safety and health organizations, environmental protection organizations
- government institutions

**Thirty-day trial, then a paid license is required**, for any commercial use. This explicitly includes purely internal use by a for-profit company for its own Active Directory administration - you do not have to resell ADDS-PIM for your use to be commercial. One trial period covers your whole organization, not one per person.

For commercial licensing get in contact: **chaozmc\[at]is-jo.org**

The license text is authoritative; the summary above is a plain-language guide, not a substitute for reading it. If you are unsure which side of the line you fall on, ask - the answer is usually quick and often free.

### Third-party components

Dependencies and their licenses are listed in [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt). ADDS-PIM does not bundle or sublicense Microsoft products; Windows Server, Active Directory, SQL Server and the .NET runtime must be licensed separately by the operator.

### No warranty

ADDS-PIM modifies Active Directory group memberships. It is provided as is, without warranty or liability of any kind, as far as the law allows. Testing in a non-production directory, a working backup and restore path, and a reviewed delegation model are your responsibility, not the author's. See the **Status** section above before deploying anywhere that matters.

## Contributing

Pull requests are welcome - please read [`CONTRIBUTING.md`](CONTRIBUTING.md) first. Contributions are accepted under the Apache License 2.0 so that they can be included in both the freely available and the commercially licensed builds.
