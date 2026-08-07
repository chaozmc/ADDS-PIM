# ADDS-PIM Documentation

ADDS-PIM is a Privileged Identity Management (PIM) system for time-limited Active Directory group memberships: users request temporary membership in a privileged group instead of holding it standing, the system grants it, verifies the grant actually took effect in Active Directory, and removes it again once the requested TTL expires. This documentation set explains the design, the security model, and how to run it.

## Where to start, by role

**AD operations technicians** - installing, running, and troubleshooting the system: [operations.md](operations.md) → [active-directory-worker.md](active-directory-worker.md) → [audit-and-observability.md](audit-and-observability.md)

**Architects** - evaluating or extending the design: [overview.md](overview.md) → [architecture.md](architecture.md) → [identity-and-lifecycle.md](identity-and-lifecycle.md) → [data-model.md](data-model.md) → [api-reference.md](api-reference.md)

**Security governance staff** - reviewing the authorization, MFA, signing, and audit model: [security-model.md](security-model.md) → [identity-and-lifecycle.md](identity-and-lifecycle.md) → [active-directory-worker.md](active-directory-worker.md) → [audit-and-observability.md](audit-and-observability.md)

**Enthusiasts** - understanding what this is and how it fits together: [overview.md](overview.md) → [architecture.md](architecture.md) → [frontend.md](frontend.md)

## Full index

| Doc | Covers |
|---|---|
| [overview.md](overview.md) | What ADDS-PIM is, why standing privileged AD group membership is the problem it solves, target platform, initial scope, and explicit non-goals. |
| [architecture.md](architecture.md) | Component boundaries (Web, API, Application, Domain, Infrastructure, Contracts, AdWorker, Shared), the trust-boundary diagram, and the end-to-end request flow. |
| [security-model.md](security-model.md) | Authorization decisions, authentication and MFA (FIDO2/WebAuthn, TOTP), request signing, least-privilege gMSA rights, and secrets/configuration handling. |
| [identity-and-lifecycle.md](identity-and-lifecycle.md) | The person/actor-account/target-account identity model, directory scope and entitlements, the membership request state machine, concurrency/idempotency, the approval workflow, and offboarding/purge/reconciliation. |
| [api-reference.md](api-reference.md) | REST conventions, contract versioning, the endpoint surface, and HTTP status code semantics. |
| [frontend.md](frontend.md) | The Blazor frontend's non-authoritative role, UX approach, and the user-facing request/MFA flow. |
| [active-directory-worker.md](active-directory-worker.md) | Why the AD Worker is isolated, how it talks to the API, how it writes TTL memberships to AD, and the mandatory read-back verification step. |
| [data-model.md](data-model.md) | The core schema (accounts, groups, policies, entitlements, requests, reconciliation), and why SQL Server and Active Directory are each authoritative for different things. |
| [audit-and-observability.md](audit-and-observability.md) | The audit record model, why it's append-only, its separation from technical/diagnostic logs, and Windows Event Log integration. |
| [operations.md](operations.md) | Prerequisites, installation, certificate operations, Worker host configuration, health checks, and the error catalog. |
| [testing.md](testing.md) | Test project layout and which scenarios are treated as security-critical. |
| [dependencies.md](dependencies.md) | The policy for adding and vetting external dependencies. |

Deployment scripts and their parameters are documented separately in [`../scripts/README.md`](../scripts/README.md).
