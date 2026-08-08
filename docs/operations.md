# Operations

This page is the reference for AD operations technicians and administrators who install, configure, operate, and troubleshoot ADDS-PIM: what has to exist before installation, how the components go in, how the signing and secret-protection certificates are provisioned and rotated, how the Worker host is configured, what to monitor, and what the error categories mean when something goes wrong. Certificate operations also matter to security-governance reviewers, since certificate rollover is a security-relevant administrative action that is audited like any other.

For the architecture behind what is being installed, see `architecture.md`. For the authorization, signing, and MFA guarantees these procedures protect, see `security-model.md`. For how AD writes are executed and verified by the Worker, see `active-directory-worker.md`. For audit event details, see `audit-and-observability.md`.

The actual installation and configuration scripts, with their full parameter lists, live in [`../scripts/README.md`](../scripts/README.md). This page explains what each stage of installation does and why; it does not restate script parameters.

## 1. Prerequisites

ADDS-PIM currently ships as a single reproducible installation path for one Windows domain-joined host running Web, API, and Worker together (or split across dedicated hosts using the same components). It does not yet ship a full setup/upgrade/repair/rollback installer package - the documented Worker and Web/API scripts automate the steps described below, but do not create gMSAs, DNS records, firewall rules, certificates, private-key ACLs, or install SQL Server themselves.

Before installing, an administrator provides:

- **IIS with Windows Authentication**, the current .NET ASP.NET Core Hosting Bundle installed, and IIS restarted afterward.
- **Separate group Managed Service Accounts (gMSAs)** for Web, API, and Worker (e.g. `EXAMPLE\svc-adds-pim-web$`, `EXAMPLE\svc-adds-pim-api$`, `EXAMPLE\svc-adds-pim-worker$`), installed and usable on the target host(s), each with the necessary service principal names (SPNs). Web and API gMSAs receive **no** rights to change AD group memberships. The Worker gMSA receives only the AD rights delegated for the specific target groups it is allowed to manage - never a membership in Domain Admins, Enterprise Admins, or other Tier-0 groups is required.
- **DNS resolution** for the Web, API, and Worker host names, with TLS server certificates whose SAN entries cover those names - for example `pim.example.org`, `pim-backend.example.org`, and `pim-worker.example.org:8990` - imported into `LocalMachine\My` with private-key access granted to the corresponding IIS/service gMSA.
- **A separate API-to-Worker client certificate** for mutual TLS, with its public thumbprint allow-listed on the Worker side.
- **The two application certificates** the system depends on, either pre-provisioned or the administrator permits their creation during setup:
  - Web request-signing certificate - Subject/Friendly Name `ADDS-PIM Web API Signing`, RSA 3072, `DigitalSignature` key usage.
  - API TOTP secret-protection certificate - Subject/Friendly Name `ADDS-PIM API TOTP Secret Protection`, RSA 3072, `KeyEncipherment` key usage.

  Both live in `LocalMachine\My` on their respective server and must not be reused as TLS or mTLS certificates. See [Certificate operations](#3-certificate-operations) below for detail.
- **SQL Server reachability** (SQL Server or SQL Server Express is sufficient at this scale). The installing administrator needs temporary rights to run migrations and create runtime logins/roles; runtime components use only Windows Integrated Security - SQL Server passwords are never used.
- **The AD Privileged Access Management (PAM) optional feature** enabled in the forest, and a reachable, writable domain controller for the Worker's AD operations and TTL read-back verification.
- **Firewall and DNS access** so the API can reach the Worker's private HTTPS port; the Worker must not be reachable from general client networks.
- **Reachability of the issuing certificate authority and its revocation lists (CRL/OCSP)** from the API's service context. The API performs full online certificate-chain and revocation checking against the Worker's mTLS certificate; an unreachable CRL/OCSP deliberately blocks request execution rather than failing open.

A prerequisite-verification script is available in the repository to check domain membership, required AD modules, and the PAM feature before installation begins - see `../scripts/README.md`.

## 2. Installation walkthrough

This is a single-host-reproducible procedure, not a documented high-availability production rollout. The architectural boundaries are unchanged throughout: Web and API can never modify AD groups; only the Worker executes verified, TTL-bound group membership changes; SQL Server remains authoritative for policies, entitlements, and audit.

### 2.1 Database and Worker

Restore packages and tools: `dotnet restore` on the solution, then `dotnet tool restore` for the pinned `dotnet-ef` tool.
Publish the Worker project and run the elevated Worker installation script against the **production** SQL connection string. The script is idempotent: it validates the installed gMSA and the presence of the required `LocalMachine` certificates, bootstraps the database schema and the Worker's runtime SQL login, applies pending EF Core migrations, writes the Worker's registry configuration and Event Log source, and creates or starts the Worker Windows service.

For gMSA compatibility, service creation is deliberately two-stage: the service is first created disabled under `LocalSystem` (never started in that state), its identity is then switched to the configured gMSA through the Windows service control manager, and only after that succeeds is startup changed to automatic. If the identity switch fails, the still-disabled service is removed rather than left in an inconsistent state.

The script does **not** create the gMSA itself, delegate AD rights, enroll certificates, grant private-key access, install SQL Server, or open firewall rules - those remain separate, deliberately reviewed administrative actions that the script then validates rather than performs.

The database bootstrap script (`database/Install-ADDS-PIM-Database.sql` in the repository) is safe to re-run at any time and is executed both before and after migrations, so that narrow, least-privilege runtime permissions exist for the API and Worker SQL logins once every table - including any added by a migration - has been created. Always use this versioned script as the bootstrap input; a local, modified copy is not a reproducible installation record.

Start the Worker service and verify it locally before testing it from the API side only - the Worker's HTTPS endpoint is private and is not intended to be exercised directly from a browser or general client.

### 2.2 Web and API

1. Publish Web and API using the repository's publish script. When overwriting a running IIS publish folder, place a temporary `app_offline.htm` in only the affected folder, publish, then remove it - the publish script itself does not create or remove this file.
2. Generate the Web signing certificate and register its public key with the API's database as described in [Web signing certificate operations](#web-signing-certificate-registration-and-rotation) below. This must happen before the Web application is fully functional, because every protected `/admin/*` endpoint already requires a validly signed request, which in turn requires an already-registered signing key - there is no way to bootstrap the very first key through the running application itself.
3. Provide production configuration files for Web and API outside source control, containing at minimum:
   - **API**: the database connection string, directory/AD settings, application access settings, FIDO2 settings, TOTP secret-protection settings, and Worker client settings (endpoint, client certificate, and expected Worker server certificate thumbprint).
   - **Web**: the API base address, directory/AD settings, application access settings, FIDO2 settings, and the signing certificate thumbprint/key ID matching the key registered in the previous step.
4. Run the elevated IIS installation script once. It creates only missing application pools and sites and registers the API's Windows Event Log source; it does not create gMSAs, DNS records, firewall rules, configuration files, or private-key ACLs, and it does not replace an already-existing IIS site.
5. Verify IIS bindings, HTTPS certificates, Windows Authentication, and application pool identities. Anonymous authentication is not the intended access path - browsers need a Kerberos-capable intranet session.

### 2.3 PIM data and AD delegation

1. Create the target AD groups and grant the Worker's gMSA the minimal delegation needed to manage them - never Domain Admins, Enterprise Admins, or other Tier-0 groups.
2. In the admin area, link a person to the correct AD object, create an active target group with a TTL policy, and create an active direct entitlement. SQL Server is the authoritative source for all of this - nothing here is inferred from AD group nesting.

### 2.4 Post-installation acceptance checks

1. `GET https://pim-backend.example.org/health/live` returns HTTP 204. This proves only that the API process is running - not database, Worker, or AD readiness (see [Health checks and monitoring](#5-health-checks-and-monitoring)).
2. Sign in as the intended user in a Kerberos-capable browser session. The root page is a static welcome page with no entitlement or MFA logic; its "Request access" action leads to the request page, which must show the signed-in user's server-side-resolved entitlements. Entries that cannot currently be requested (for example, approval-required groups) remain visible but disabled with an explanation.
3. Submit an eligible request with a ticket reference and justification. Approval-required entitlements are visible in the selection UI but not yet requestable (TOTP and FIDO2 MFA are fully implemented). Its status must reach `Succeeded` only after the Worker has executed the change and the AD read-back has verified it - request IDs and API/Worker/SQL audit records must all agree.
4. Repeat the same request before its TTL expires. The expected result is `Failed` with a clear explanation that the membership already exists - never a silent renewal or replacement.
5. Recycle the API application pool and repeat step 3. This specifically re-exercises the full mTLS chain including online CRL/OCSP checking, which a liveness check alone does not cover.

### 2.5 Current limitations

- A complete installer with upgrade, repair, rollback, and uninstall is not yet implemented; the documented scripts cover fresh installation only.
- Ticket-format validation is implemented; integration with an external ticketing system's API is out of scope for the current release.

None of these limitations relax server-side checks - the API always re-evaluates entitlement and policy immediately before Worker execution, regardless of what the frontend displayed earlier.

## 3. Certificate operations

ADDS-PIM uses several distinct certificates for different purposes, and they must never be confused or reused across purposes:

| Certificate | Purpose | Location | Key access |
|---|---|---|---|
| Web/API HTTPS (TLS) | Browser/transport protection | `LocalMachine\My` on each IIS host | respective IIS gMSA |
| Web signing certificate | Authenticates and signs Web→API requests (current mechanism, see below) | `LocalMachine\My` on Web host(s) | Web gMSA |
| API TOTP secret-protection certificate | Encrypts stored TOTP factor secrets | `LocalMachine\My` on API host | API gMSA |
| API→Worker mTLS client certificate | Mutual TLS to the Worker | `LocalMachine\My` on API host | API gMSA |
| Worker server certificate | TLS/mTLS server identity for the Worker's private endpoint | `LocalMachine\My` on Worker host | Worker gMSA |

### Web signing certificate registration and rotation

The Web request-signing certificate authenticates every Web-to-API request with an asymmetric signature (the current mechanism; it superseded an earlier shared-HMAC-key design - see the note at the end of this section). The API never stores a private key: it stores only the public DER-encoded certificate content, alongside a key ID, purpose (`ApiRequestSigning`), status, and validity interval, in its `WebSigningCertificates` table. During a planned rotation the API can accept more than one active public key at once; an expired or deactivated certificate is rejected fail-closed.

**Generating a certificate** (elevated PowerShell, on the Web server):

```powershell
$cert = New-SelfSignedCertificate `
 -Subject 'CN=ADDS-PIM Web API Signing' `
 -CertStoreLocation 'Cert:\LocalMachine\My' `
 -KeyAlgorithm RSA `
 -KeyLength 3072 `
 -HashAlgorithm SHA256 `
 -KeyUsage DigitalSignature `
 -NotAfter (Get-Date).AddYears(1) `
 -FriendlyName 'ADDS-PIM Web API Signing'

$cert.Thumbprint
```

A self-signed certificate is acceptable here because the API does not trust a general PKI chain for this purpose - it trusts only the single, administratively registered public certificate matching a given key ID. This certificate must not be reused for TLS or any other purpose. The installer sets the private-key ACL for the Web gMSA; do not grant broader Users, IIS_IUSRS, or application-pool group access.

**Registering the public key** with the API has no dedicated UI or route, because every protected `/admin/*` endpoint already requires a validly signed request - a chicken-and-egg problem for the very first key in an environment. The repository's registration script writes the public certificate content (no private key) directly and transactionally into the database:

- **First key in an environment**: run the registration script against the production SQL connection string; it prompts for thumbprint and key ID if not supplied as parameters, and writes an active entry with purpose `ApiRequestSigning`.
- **Rotation with overlap** (current certificate still valid): register the new certificate under a new key ID without deactivating the old one, roll out the new key ID/thumbprint to Web configuration, verify a signed readiness check succeeds, and only after all Web instances are updated and any in-flight retries have expired, deactivate the old database entry and remove the old private key.
- **Immediate replacement** (certificate already expired or compromised, no overlap window desired or possible): the same registration script accepts a deactivation parameter that registers the new key and deactivates the old key ID in a single transaction, so there is never a moment with no active key.

Every registered key is retained rather than deleted when deactivated, which preserves a history of what was active when - the new entry records who registered it and when. Deactivation itself is a plain `IsActive` flip on the existing row and does not currently record who performed it or when; this table's history is not integrated with the application's formal audit-event trail described in [audit-and-observability.md](audit-and-observability.md), so treat it as a database record of key history, not as an audited administrative action in the same sense as actions taken through the application itself.

**Historical note:** an earlier design used a shared HMAC key, independently envelope-encrypted for Web
and API using per-host encryption certificates, with its own rotation and emergency-revocation
procedure. That mechanism has been superseded by the asymmetric signing certificates described above and
should not be implemented or operated in a current deployment.

### TOTP secret-protection certificate rollover

This certificate (referenced in configuration as `TotpSecretProtection:CertificateThumbprint`) is distinct from the Web signing certificate, the IIS HTTPS certificate, and the Worker mTLS client certificate - it encrypts every stored TOTP factor secret. Only the API gMSA has private-key read access. Each stored TOTP factor (active, unconfirmed, or mid-enrollment) records the thumbprint of the certificate that currently protects its secret.

An administrative page shows the certificate's thumbprint, validity window, whether the private key is currently accessible to the API gMSA, and how many TOTP factors are currently protected - with a red/green status tile that turns red once remaining validity drops to 30 days or the certificate cannot be found. This view is the primary place an administrator notices that a rollover is due; it does not replace external certificate-expiry monitoring.

A rollover is a one-time, transactional re-encryption of every stored secret from the old certificate to a new one, performed through the admin certificate-rollover page (backed by a corresponding API endpoint), in three stages with different privilege levels:

1. **Provision the new certificate** (administrator, elevated PowerShell, on the API server) - generated the same way as the Web signing certificate but with `KeyUsage KeyEncipherment` instead of `DigitalSignature` (the protection mechanism uses RSA-OAEP-SHA-256 directly on the secret, not an envelope/data-encryption-key scheme, and rejects any certificate without this key usage). Grant the API gMSA private-key read access; leave the old certificate and its ACL untouched, since both certificates must be simultaneously readable by the API gMSA during the next stage.
2. **Run the re-encryption** through the admin page: confirm the new certificate is not yet the active configured thumbprint, enter its thumbprint, type an exact confirmation phrase, and execute. The operation runs server-side in a single database transaction - either every factor (active, unconfirmed, and mid-enrollment) is re-encrypted successfully, or none are; any failure (unreachable/invalid certificate, unexpected protection-key mismatch, concurrent modification of a factor) rolls back the entire transaction. A successful rollover produces an audit event recording the outgoing and incoming key IDs and the number of factors re-encrypted.
3. **Switch configuration and remove the old certificate** (administrator, elevated PowerShell, on the API server): update `TotpSecretProtection:CertificateThumbprint` in the API's published configuration on every API host, recycle the API application pool so the singleton protector picks up the new certificate (a full redeploy is not needed for a configuration-only change), confirm liveness and a spot-check TOTP verification against an active factor, and only then remove the old certificate's gMSA ACL and/or delete it from the certificate store.

Do not confuse this procedure with Web signing certificate rotation, the IIS HTTPS certificate, or the API-to-Worker mTLS certificate - each is operated independently.

## 4. Worker host configuration

The Worker reads its startup configuration only from `HKLM\SOFTWARE\ADDS-PIM\Worker` on its host; a configuration change requires a service restart. The registry holds identifiers and connection settings only - never private keys or certificate files.

| Registry value | Type | Purpose |
|---|---|---|
| `BindAddress` | `REG_SZ` | Specific, non-loopback IP address of the Worker's interface. Wildcard and loopback bindings are rejected. |
| `Port` | `REG_DWORD` | Private HTTPS port. |
| `ServerCertificateThumbprint` | `REG_SZ` | TLS server certificate in `LocalMachine\My`; the Worker gMSA needs private-key read access. |
| `AllowedApiClientCertificateThumbprints` | `REG_MULTI_SZ` | Explicit allow-list of API mTLS client certificate thumbprints; at least one entry is mandatory. |
| `SqlConnectionString` | `REG_SZ` | SQL Server connection string using Windows Integrated Security; SQL Server passwords are prohibited. |
| `DomainController` | `REG_SZ` | Fixed, writable domain controller used for the AD operation and its read-back verification. |
| `CommandMaxAgeSeconds` | `REG_DWORD` | Accepted command age, 30–600 seconds; the installer default is 300. |

The Worker writes to the Windows Event Log under the fixed name `ADDS-PIM` with a fixed source, `ADDS.PIM.AdWorker`, created by the installer while elevated - the Worker never attempts to create this source at runtime. Event IDs `4100`–`4106` cover service startup, command receipt, rejection, completion, cancellation, failure, and rejected TLS client certificates. Events carry command, request, correlation, and caller-certificate identifiers where available, but never secrets, private keys, or connection strings.

The repository's Worker host configuration script is the elevated helper for writing these registry values and the Event Log source. It deliberately does not create a certificate, grant private-key access, install the Windows service, or open a firewall rule - those remain separately reviewed deployment actions.

## 5. Health checks and monitoring

What exists today is intentionally minimal - there is no readiness or diagnostics endpoint yet, only a liveness check on each of the two long-running processes:

- `GET /health/live` on the API.
- `GET /internal/v1/health/live` on the Worker (reachable only from wherever the private Worker endpoint is reachable from, see [active-directory-worker.md](active-directory-worker.md)).

Both are deliberately liveness-only: a `200`/`204` from either one only means the process is up and answering HTTP requests. It does not prove database reachability, Worker reachability, AD reachability, a valid mTLS chain, or anything else about downstream health. After any change to the issuing CA, the certificate chain, or a revocation list, recycle the affected service and exercise a real membership request end to end - a liveness check alone will not catch a broken chain.

Beyond the two liveness endpoints, the following exist and are useful for monitoring, but are not exposed as a unified health-check API:

- **API startup Event Log check.** At startup, the API verifies that its dedicated Windows Event Log source is registered and writable, and deliberately refuses to serve traffic if it is absent, assigned to another log, or unwritable - Event Log source registration is an elevated, one-time setup responsibility, not something the API runtime creates for itself.
- **Admin certificate-status pages.** The TOTP secret-protection certificate status page and the certificate-status overview (see [certificate operations](#3-certificate-operations) below) surface certificate validity and private-key accessibility, but as authenticated admin UI, not as a machine-readable health endpoint.
- **Pre-install prerequisite check.** `scripts/Test-AddsPimPrerequisites.ps1` verifies domain membership, the RSAT Active Directory PowerShell module, and the AD PAM optional feature - this runs once, before installation, not continuously against a running deployment.
- **Purge-completion outbox monitoring.** Any open purge-completion outbox entries are logged in the protected technical log and retried; they are an operational alert worth investigating, not something a health endpoint currently surfaces.

A combined readiness/diagnostics health-check endpoint covering database, Worker, and AD reachability is a natural next step, not something implemented today - don't rely on `/health/live` for anything beyond "the process is running."

## 6. Error catalog

Every error in ADDS-PIM is classified into one of the categories below. Each concrete error additionally carries a stable error code, a correlation ID, the affected component, and whether it is `Transient` or `Permanent` - transience depends on the specific error code; the category alone never authorizes a retry. HTTP status mapping and audit fields for these categories are documented in `api-reference.md` and `audit-and-observability.md` respectively - this table is the normative source for what each category means.

| Category | Meaning |
|---|---|
| `Validation` | Input is syntactically or semantically invalid. |
| `Authentication` | User or client authentication failed. |
| `Authorization` | Current policy does not permit the operation. |
| `Signature` | The application-level request signature or its bound content is invalid. |
| `Replay` | A nonce, request ID, command ID, or MFA factor was reused or has expired. |
| `Mfa` | A required multi-factor authentication factor is missing or invalid. |
| `Database` | Persistence, transaction, or schema error. |
| `ActiveDirectory` | An AD query or change failed. |
| `WorkerCommunication` | The Worker was unreachable, or the protocol/transport failed. |
| `Certificate` | A certificate's trust, purpose, validity, revocation status, or key access is invalid. |
| `Configuration` | Required configuration is missing or contradictory. |
| `Timeout` | A defined time limit was exceeded. |
| `Concurrency` | A competing state change or duplicate-submission collision occurred. |
| `Verification` | The AD read-back after a change did not produce a conclusively positive result. |
| `Unexpected` | An unanticipated internal error. |

## 7. Error-handling philosophy

Errors are classified internally against the catalog above and are never swallowed or silently converted into success-like fallback values. Expected security failures, transient infrastructure failures, and truly unexpected exceptions remain distinguishable from one another rather than collapsed into a generic failure state.

Users see understandable, security-conscious messages paired with a correlation ID they can hand to support. Internal detail - stack traces, query text, AD distinguished names, and similar - appears only in protected technical logs and, where the event is audit-relevant, in audit records; it is never returned to an unauthorized caller.

Responses never disclose the existence or properties of protected groups, entitlements, accounts, certificates, or policies to a caller who is not entitled to see that information - a denial looks the same whether the target does not exist or simply is not visible to the caller. Retries are bounded and state-aware: a retried or duplicated request observes existing state rather than re-executing an operation that already succeeded, using the request's globally unique ID for idempotency.
