# Testing

ADDS-PIM is a security-sensitive PAM system, so its test strategy is built around the same trust boundaries described in [`security-model.md`](security-model.md) and [`architecture.md`](architecture.md): rules that decide *who gets privileged Active Directory access* are tested directly against the documents that define them, not re-derived ad hoc in test code.

## Test layers mirror the architecture

Each `src/` project in the Clean/onion architecture (see [`architecture.md`](architecture.md)) has a matching test project under `tests/`, following the same layering:

| Layer | Test project | Focus |
|---|---|---|
| Domain | `ADDS.PIM.Domain.Tests` | Entities, value objects, policies, state transitions, domain errors - no infrastructure dependencies. |
| Application | `ADDS.PIM.Application.Tests` | Use cases, workflow orchestration, authorization decisions, validation, ports. |
| Infrastructure | `ADDS.PIM.Infrastructure.Tests` | EF Core/SQL Server persistence, certificate/signing logic, MFA provider integration, audit sink, Worker communication. |
| AD Worker | `ADDS.PIM.AdWorker.Tests` | The isolated privileged service that is the only component allowed to write to privileged AD groups. |
| Contracts | `ADDS.PIM.Contracts.Tests` | Versioned DTOs/transport contracts shared across process boundaries. |

This means a change to, say, the membership request state machine is exercised at the Domain level for pure logic, at the Application level for orchestration, and - where it touches persistence or the Worker - at the Infrastructure or AD Worker level as well, rather than relying on a single broad test suite to catch regressions across layers.

Beyond these per-project unit/integration suites, the strategy also calls for API-level tests, database tests, dedicated security tests, isolated AD integration tests, end-to-end tests, and installer/upgrade tests, reflecting the full set of components described in [`architecture.md`](architecture.md).

## What gets extra scrutiny

Because incorrect behavior here has direct security consequences, several areas are treated as security-critical and are tested directly against their normative source documents rather than against looser, implicit expectations:

- **Authorization decisions** - see [`security-model.md`](security-model.md).
- **Request signing and replay protection** - see [`security-model.md`](security-model.md).
- **Membership request state machine and idempotency** - see the domain model referenced in [`data-model.md`](data-model.md).
- **HTTP response behavior** - see [`api-reference.md`](api-reference.md).
- **TTL verification (read-back after an AD write)** - see [`active-directory-worker.md`](active-directory-worker.md).
- **Audit fields** - see [`audit-and-observability.md`](audit-and-observability.md).
- **Error categories** - see [`operations.md`](operations.md).

Scenarios that are specifically called out for coverage include: tampered or expired requests; nonce and MFA-factor replay; invalid or rotated signing certificates; authorization that is revoked between the time a request is displayed and the time it executes; disallowed TTL values; concurrent requests; pre-existing permanent or differently-timed group memberships; Active Directory, PowerShell, and database failures; a crash of the AD Worker mid-operation; network interruption; verification after an apparently successful write; and repeated retries of the same request.

Identity separation between the person who is signed in, the actor account making the request, and the target account that actually receives group membership (see [`identity-and-lifecycle.md`](identity-and-lifecycle.md)) gets its own dedicated coverage: sign-in as one account with permitted execution on a linked target account; rejected execution against a target account that is unlinked or not concretely authorized; missing or ambiguous person-to-account mapping; an inactive mapping; an attempt to swap the target account after signing or after MFA confirmation; identical actor and target accounts only where that is explicitly authorized; and deletion/recreation of an account under the same name. The AD Worker and the TTL read-back verification are required to act only on the target account named in the authorized request - never on a value re-resolved later.

Account lifecycle and cleanup logic is tested for: rejecting a purge of accounts, persons, or target groups that are still active; computing the purge scope on the server rather than trusting a browser-supplied scope; deleting operational dependencies transactionally while preserving audit snapshots; correlating application audit records with the Windows Event Log; and failing closed when the Event Log is not writable.

## Active Directory integration tests

AD integration tests run exclusively against an isolated test domain. Production Tier-0 groups must never be used for development or testing. Environment-dependent tests that cannot run in a given environment (for example, no test AD forest available) are reported as skipped with the specific reason, rather than failing silently or being faked.

## Running the tests

Build and test commands are documented in the root [`README.md`](../README.md) - in short, `dotnet test ADDS-PIM.slnx` runs the full suite, and individual test projects under `tests/` can be run or filtered independently for faster iteration on a single layer.
