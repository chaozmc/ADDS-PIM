# API Reference

The backend API is the authoritative orchestrator for ADDS-PIM. It holds current authorization state,
group policy, request state, idempotency guarantees, and audit facts - the frontend is never treated as evidence for an authorization decision, only as a convenience layer on top of the API. This page describes the conventions the API is built on and the surface it exposes: how endpoints are versioned and shaped, how request/response contracts relate to the internal domain model, and the HTTP status code rules that govern every response.

For how individual API calls are authenticated and protected against tampering and replay, see [security-model.md](security-model.md). For what a membership request actually contains and how it moves through its lifecycle once accepted, see [identity-and-lifecycle.md](identity-and-lifecycle.md).

## Design conventions

- **Versioned routes.** Endpoints are versioned in the URL path, e.g. `POST /api/v1/membership-requests`.
- **Explicit contracts, not domain leakage.** The API never exposes Domain or persistence entities
  directly. Every request and response body is a dedicated, versioned Data Transfer Object - for example, administrative contracts live under `ADDS.PIM.Contracts/Administration/V1`. This keeps the wire format stable and decoupled from internal refactoring, and prevents accidental disclosure of internal-only fields.
- **Server-side validation on every path.** All endpoints perform explicit server-side validation; nothing about authorization, entitlement, or policy is trusted from the caller.
- **Correlation and request IDs.** Every membership request and administrative command carries a globally unique identifier, and responses carry correlation/request IDs so a caller-reported problem can be traced back to a specific server-side event - including, for unhandled exceptions, a technical error log entry keyed by the same `X-Request-Id` / `X-Correlation-Id` headers when the caller supplies them.
- **Read queries vs. mutations.** Read endpoints that return sensitive or scoped data (a user's own request history, the list of currently requestable entitlements, pending approvals, etc.) are still signed and replay-protected, and still re-resolve identity and scope from current server-side data - they are not a trusted shortcut, and none of them substitute for the authorization check the API performs immediately before executing a mutation.
- **Idempotency and no double execution.** Retried or replayed calls must observe existing state rather than executing twice; this is enforced through the same signed request-ID/nonce mechanism described in [security-model.md](security-model.md).
- **Fixed, typed downstream calls.** The API talks to the Active Directory Worker over a fixed, private
  mTLS endpoint with typed, versioned commands. It never accepts an arbitrary Worker URL, raw LDAP input, a certificate thumbprint, or a scripting fragment from a caller - the Worker's own execution boundary is described in [active-directory-worker.md](active-directory-worker.md).

## Membership request endpoints

`POST /api/v1/membership-requests` is the central mutation: it validates the request's signature,
timestamp, nonce and request-ID replay protection, evaluates current SQL entitlement and group policy, invokes the AD Worker, and only reports success after the resulting AD change has been read back and TTL-verified. If the target group's policy requires a ticket reference, it is validated against the
group's active pattern both at submission and again immediately before dispatch; a missing, malformed, or otherwise non-matching ticket reference - or a required-ticket policy with no active valid pattern - is rejected as unprocessable input. If the policy requires a second authentication factor, the endpoint checks before creating the request that the caller has at least one active eligible factor, so that a request is never created in a state it could never leave.

Two read-only, signed query endpoints support the request UI without themselves being authorization evidence:

- `POST /api/v1/membership-requests/mine/query` - the caller's own request history, resolved from the current Actor-to-Person mapping, filtered and paginated server-side.
- `POST /api/v1/membership-requests/mine/available-entitlements/query` - the entitlements currently requestable by the caller, including effective TTL bounds and MFA/approval/ticket requirements shown as presentation hints. The subsequent `POST /api/v1/membership-requests` call re-evaluates the complete authorization decision from scratch; nothing from this query is trusted at execution time.

## MFA endpoints

Second-factor enrollment and verification have their own endpoints, separate from the membership-request endpoints that consume a factor's outcome:

- `POST /api/v1/mfa/status/query` - the caller's current enrollment status (which factor types, if any, are active) for building the enrollment/management UI.
- `POST /api/v1/mfa/totp/enroll` and `.../enroll/{factorId}/confirm` - starts a TOTP enrollment (returns a provisioning URI and secret) and confirms it with a live code, activating the factor only once the caller has demonstrated possession of it.
- `POST /api/v1/mfa/fido2/register/options` and `.../register/complete` - the two-step WebAuthn credential registration ceremony (server-issued challenge/options, then the browser's signed attestation response).
- `POST /api/v1/membership-requests/{requestId}/second-factor/totp/verify` - confirms a pending request's required TOTP second factor.
- `POST /api/v1/membership-requests/{requestId}/second-factor/fido2/options` and `.../fido2/verify` - the
  equivalent two-step WebAuthn assertion ceremony bound to a specific pending request, rather than a bare sign-in ceremony.

Every second-factor confirmation is bound to the specific pending request it satisfies - a valid TOTP code or FIDO2 assertion confirms only the request it was requested for, not second-factor status in general. See [security-model.md](security-model.md) for the underlying MFA design (transaction binding, the FIDO2 zero-counter exemption, self-service re-enrollment restrictions) and [frontend.md](frontend.md) for how these endpoints are used from the enrollment and request-confirmation UI. 

## Approval workflow endpoints

Where a target group's policy requires approval, a separate set of endpoints supports the approver
experience:

- `POST /api/v1/membership-requests/pending-approvals/eligibility` - whether the current caller is an approver at all.
- `POST /api/v1/membership-requests/pending-approvals/query` - the requests currently awaiting approval for groups the caller is assigned to approve.
- `POST /api/v1/membership-requests/{requestId}/approve` and `.../reject` - re-check both the approval requirement and the specific approver's current assignment immediately before the decision takes effect. Approval moves the request forward for execution; rejection moves it to a terminal rejected state.

Group-approver assignments themselves are administered under `/api/v1/admin/target-groups/{targetGroupId}/approvers` (query, add, deactivate), using the same signature/replay/administrator/row-version/audit controls as other administrative mutations.

## Administrative endpoints

Administrative endpoints require the same request signing and replay protection as regular endpoints, plus a current, freshly-resolved administrator group membership check. Mutations use optimistic concurrency (SQL row versioning) and append an audit event for every change. Implemented areas include person onboarding and query/deactivation/reactivation, policy query/update, target-group registration and deactivation/reactivation, direct-entitlement creation and expiry/TTL-constraint management, account-link management, directory reconciliation, identity purge, and cleanup of orphaned MFA and approval requests.

A few points worth calling out for anyone building against these endpoints:

- **Directory reconciliation** creates a single, durable maintenance run rather than acting inline; only a confirmed directory result produces a finding, and a directory transport, bind, timeout, or ambiguity failure marks the run failed without silently treating an unreachable directory as "the object is gone."
- **Identity purge** is split into a read-only dry-run (`POST /api/v1/admin/identity-purge/query`), which recomputes the full dependent graph server-side and reports whether a purge is currently eligible, and a separate execution endpoint (`POST /api/v1/admin/identity-purge/execute`) that requires an explicit typed confirmation phrase and re-validates eligibility immediately before deleting the approved local operational graph. Active Directory objects and audit records are never deleted by this or any other endpoint.
- **Orphaned-request cleanup** (for requests stuck waiting on a second factor or an approval assignment that was later removed) is non-destructive: the request, its status history, and all associated audit events are retained; the endpoint only transitions the request to a terminal expired state, and the "expire" action re-validates eligibility server-side to avoid racing a legitimate concurrent completion.
- **Technical error diagnostics** (`POST /api/v1/admin/technical-errors/query`) surface unhandled-exception records that are structurally separate from the audit trail - see
  [audit-and-observability.md](audit-and-observability.md) - so operators can look up the technical cause behind a request ID a user reports without mixing technical logs into the security audit trail.

## HTTP status codes

The API uses [RFC 9457 Problem Details](https://www.rfc-editor.org/rfc/rfc9457) for error responses. Every error response includes a correlation ID, and deliberately omits any detail about protected groups, users, policies, entitlements, certificates, or infrastructure that the caller is not already entitled to know.

| Outcome                                                                                                             |                                                  Status |
| ------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------: |
| Successful synchronous query or change                                                                              |                                                `200 OK` |
| Resource successfully created                                                                                       |                                           `201 Created` |
| Request durably accepted, processing in progress                                                                    |                                          `202 Accepted` |
| Successful action with no response body                                                                             |                                        `204 No Content` |
| Syntactically invalid request, or missing/malformed request-protection headers (signature, timestamp, etc.)         |                                       `400 Bad Request` |
| Authenticated, but not authorized for the operation                                                                 |                                         `403 Forbidden` |
| Resource not visible or does not exist                                                                              |                                         `404 Not Found` |
| Method or media-type mismatch                                                                                       | `405 Method Not Allowed` / `415 Unsupported Media Type` |
| State, concurrency, idempotency, signature/replay/certificate verification failure, or permanent duplicate conflict |                                          `409 Conflict` |
| Semantically/business-rule invalid input                                                                            |                             `422 Unprocessable Content` |
| Rate limiting / brute-force throttling                                                                              |                                 `429 Too Many Requests` |
| Unexpected internal error                                                                                           |                             `500 Internal Server Error` |
| Required internal dependency temporarily unavailable                                                                |                               `503 Service Unavailable` |

A few of these are worth explaining in context:

- **`409 Conflict`** covers more than optimistic-concurrency collisions on `rowversion`-protected updates. It is also used for idempotency collisions (a retried or replayed request that would otherwise execute twice) and for permanent duplicate collisions, such as an "expire" action losing a race to a legitimate concurrent verification.
- **`422 Unprocessable Content`** is the standard outcome for business-rule failures that are syntactically valid but semantically wrong - for example, a ticket reference that does not match the target group's configured pattern, or a second-factor requirement the caller does not currently satisfy. The API fails closed: if a required validation cannot actually be performed (for instance, a required-ticket policy with no active valid pattern configured), the request is rejected rather than allowed through.
- **Signature, replay, and certificate failures** are deliberately not given their own dedicated status code that would confirm to an attacker exactly what went wrong. In practice this collapses into two buckets: a request whose protection headers (signature, timestamp, etc.) are missing or structurally malformed gets `400 Bad Request`, same as any other syntactically invalid request; a request whose headers are well-formed but whose signature, certificate, or replay/freshness check actually fails gets `409 Conflict`, the same status used for every other kind of verification/state conflict. The precise internal failure category is still preserved in protected logs and audit data for operators - see [audit-and-observability.md](audit-and-observability.md) - but is not exposed on the wire.

## See also

- [security-model.md](security-model.md) - request signing, replay protection, and authentication of API
  calls.
- [identity-and-lifecycle.md](identity-and-lifecycle.md) - what a membership request contains and how it
  moves through its states.
- [active-directory-worker.md](active-directory-worker.md) - the Worker boundary the API talks to over
  mTLS.
- [audit-and-observability.md](audit-and-observability.md) - the audit trail produced by administrative and
  approval-workflow mutations, and how it differs from technical error logs.
