# Audit and Observability

ADDS-PIM keeps two structurally separate record streams: a security/business **audit trail** that exists to answer "who did what, to what, with what outcome" for compliance and forensic purposes, and **technical logs** that exist to help operators diagnose failures. This separation is deliberate - the two have different integrity requirements, different retention needs, and different audiences (security governance staff vs. AD operations technicians), and conflating them would weaken both. This page describes the audit model, why audit records are append-only, how audit data differs from technical logging, and how both surface in Windows Event Log for monitoring.

For what triggers a security-relevant decision in the first place (authorization, MFA, approval), see [security-model.md](security-model.md). For the underlying persistence schema, see [data-model.md](data-model.md). For health checks and general monitoring integration, see [operations.md](operations.md).

## The audit trail

Audit events capture security-relevant and business-relevant actions: successful and failed sign-in, MFA enrollment/use/lockout, request creation and decision, rejection, Worker execution, AD verification, entitlement/policy/TTL/security-class changes, certificate rollover, configuration changes, and administrative access to sensitive data. Every membership request and administrative command carries a globally unique identifier, and each step of a request's lifecycle is recorded as its own correlated audit event.

### Audit fields

Every audit event carries, where applicable to that event type, the following information. Not every field applies to every event - fields that don't apply are left explicitly unset rather than filled with an invented placeholder value. The event type and its central event ID determine which subset of fields is mandatory for that event.

| Field group | What it captures |
|---|---|
| Event identity | Unique event ID, event type, UTC timestamp |
| Identity | Person identity, authenticated actor account, privileged target account, and technical (client) identity - recorded separately even when the actor and target account point to the same AD object |
| Source | Source component, source IP address (see below), and, where available, the browser's client-side IP |
| Correlation | Correlation ID and request ID, so every decision and action belonging to one request can be tied together |
| Request details | Target group and requested TTL |
| Outcome | Result and failure category |
| Administrative change | Administrative actor (the identity field's actor account), plus a free-text summary of what changed - see below |
| Authentication | Authentication method used |

For a membership request, the requesting **person**, the authenticated **actor account**, and the privileged **target account** are always recorded as distinct fields - even when the actor account and the target account happen to be the same AD object. Where available, stable directory identities and a historical, domain-qualified display name are stored alongside them, so a record remains readable even after the underlying account is renamed or removed. Changes to how a person is mapped to an account are themselves audited administrative events; there's no dedicated pair of "previous value"/"new value" columns for this - the actor who made the change is recorded in the normal actor field, and what changed is captured as a free-text summary rather than as structured before/after data.

The technical (client) identity field is a generic identifier of the calling application instance - currently always a web front-end instance identifier, not an individual browser session or a certificate thumbprint in its own right, even though that instance identifier happens to be provisioned per signing key in the current deployment. There is no separate audit field carrying a certificate thumbprint or Worker/ domain-controller identity: that information (which domain controller a Worker operation actually used, for example) is recorded on the Worker's own command record rather than on the audit event, and can be correlated back to an audit event via the shared correlation ID. The source component is currently always the API, because the API is the only component that writes audit events; the AD Worker reports results back over its own command channel instead of writing audit records directly, but the field remains mandatory in case a second writer is introduced later.

### Two IP address fields, two different meanings

Because ADDS-PIM's front end and API are two separate tiers, "source IP" is not a single unambiguous value, so the audit model records two distinct columns:

**Source IP address** is the IP address of the calling web front-end instance, as the API observes it on its own TCP connection. It is stable per web server and independent of which physical device the end user is on, because Blazor Server terminates the browser connection at the web tier, which then makes its own signed HTTPS call to the API - the API never sees the browser directly.
**Client source IP address** is the IP address the web tier observes on its own incoming browser connection. It is forwarded to the API unsigned, via an `X-Client-Ip` header, and is informational/forensic only - it is not part of the request-signing canonicalization (see [security-model.md](security-model.md)) and carries no authorization or trust weight. It is populated across the membership request lifecycle (including second-factor verification), administration, directory reconciliation, identity purge, and MFA enrollment/step-up.

### Direct-entitlement lifecycle events

Direct entitlement lifecycle actions append one of `DirectEntitlementCreated`, `DirectEntitlementReactivated`, `DirectEntitlementDeactivated`, `DirectEntitlementValidityUpdated`, or `DirectEntitlementConstraintsUpdated`, as applicable. An exceptional, administrator-authorized physical cleanup of an inactive and unreferenced entitlement must append a dedicated cleanup audit event before deletion - this is a documented exception path and never a substitute for the normal lifecycle or identity-purge workflow.

### Administrative actions on someone else's data

When an administrator triggers a state change on data that belongs to someone else - for example, clearing an orphaned, no-longer-completable request through the admin cleanup tooling - the resulting audit event's actor field identifies the administrator who acted, not the person who originally submitted the request. This is consistent with how administrative mutations are recorded elsewhere (person/account management, entitlement changes). The original requester remains traceable through the person field and through the request's own unchanged historical events.

The same convention applies to an approver's decision: an approval or rejection by an assigned approver records the approver's account as the actor, while the person field continues to identify the original requester. A rejection additionally records a distinct failure category so it can be told apart from other rejection causes.

## Integrity and data protection

Audit data is append-only: historical facts are never modified in place. Every decision and action belonging to a request must be correlatable through its shared identifiers (correlation ID, request ID, command ID).

Audit events are retained even after an inactive PIM identity is purged, so the historical record of what happened remains intact even after the person, account, or group IDs it originally referenced no longer point to a live operational record. Before an identity purge's transactional deletion, the application writes a dedicated purge audit event carrying the entity type, stable identifiers, an immutable snapshot of the purged data, the purge scope, the administrative actor, the technical client, the correlation ID, and the result. A correlated Windows Event Log entry is written as well (see below).

The following are never captured in an audit event: passwords, TOTP secrets, recovery material, private keys, full tokens, cookies, or other sensitive credential payloads.

Internal error details are stored only in protected form; what a caller actually sees in an API response follows the rules in [api-reference.md](api-reference.md). Tamper-resistance mechanisms and retention policy for the audit store are areas that require further design work beyond what is described here.

## Technical logs vs. the audit trail

Technical logs are structurally and conceptually separate from audit events. They cover process start/stop, configuration, database connectivity, inter-service communication, certificates, the PowerShell/AD adapter layer, API errors, timeouts, health checks, and unexpected exceptions. Logs are structured, correlatable, and use centrally defined event IDs/categories; failure categories are drawn from the same catalog used elsewhere in the system rather than duplicating the audit field schema as a general-purpose logging schema.

This separation matters because the two streams have different integrity and retention requirements: a technical log can be verbose, run at debug level, and be rotated or aged out, because its only job is helping an operator diagnose a problem. An audit event cannot be handled that way - it is a compliance and forensic record and is never rotated away or overwritten.

The same secrecy rules apply to technical logs as to audit events: secrets, credential payloads, and full tokens are never captured, including inside exception details, test snapshots, or telemetry. Protected internal details never appear in a response to an unauthenticated or unauthorized caller.

Unhandled API exceptions are explicitly *not* audit events. They are stored separately as technical error log entries - recording the request/correlation ID, request path, exception type, message, and stack trace, and optionally mirrored to a locally configurable file - and are surfaced through an administrative technical- errors view for diagnosing a problem a user has reported by request ID. This table does not carry the audit trail's tamper-resistance guarantees; it is not a security proof and must never be used as authorization or audit evidence.

## Windows Event Log and SIEM integration

Today ADDS-PIM registers two dedicated Windows Event Log sources - `ADDS.PIM.Api` and `ADDS.PIM.AdWorker`, both under a shared `ADDS-PIM` log - and is designed so more event sources or a broader stream of audit-relevant events can be added the same way over time; the API source currently carries purge-lifecycle events specifically, not yet a general application- or security-audit-wide stream. Events are structured so they can be consumed by enterprise monitoring and SIEM systems (for example, Splunk). The event source, event IDs, and event categories are centrally documented so operations staff can build reliable monitoring rules against them; the content of any audit-related event always follows the audit field model above, and technical failure categories follow the same catalog used by technical logging.

As one concrete example, the API-side identity purge writes to a dedicated event source in a dedicated `ADDS-PIM` log, using a fixed range of event IDs for its lifecycle (start, completion, failure). The completion entry is delivered through a durable SQL-backed outbox rather than being a best-effort write - if the Windows Event Log write cannot be delivered immediately, the event is preserved with its correlation ID and an unmodifiable payload, and a dispatcher retries delivery. The API checks that its registered event source is writable at startup, using a dedicated diagnostic event ID for that check. If a delivery ultimately cannot succeed, the API logs the failure defensively, keeps the outbox entry rather than discarding it, and shuts down in a controlled way rather than continuing to operate with an incomplete audit trail.

For AD operations technicians, this means the audit-relevant subset of activity (purges, and other channels as they are added) is discoverable directly in Windows Event Log alongside other server events, without needing direct access to the SQL Server audit tables, and can be wired into existing enterprise log forwarding and alerting pipelines.

## See also

- [security-model.md](security-model.md) - what counts as a security-relevant action and how authorization decisions are made
- [data-model.md](data-model.md) - the persisted schema behind audit events and technical error log entries
- [operations.md](operations.md) - health checks and general operational monitoring
- [api-reference.md](api-reference.md) - how internal error detail is kept out of API responses
