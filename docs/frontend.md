# Frontend

The frontend is a Blazor web application. It is where people authenticate, request time-limited group membership, confirm a second factor when required, review their own request history, and - for administrators - manage entitlements, groups, people, and MFA factors. It is deliberately kept out of the trust boundary: **the frontend displays, prepares, confirms, and signs requests, but it is never authoritative for identity, entitlement, TTL, or MFA.** Every decision it renders is re-validated by the API against current server-side data immediately before anything happens. See [security-model.md](security-model.md) for the signing and MFA mechanics, and [identity-and-lifecycle.md](identity-and-lifecycle.md) for what a membership request actually contains.

## Authentication and access

The web application runs under Windows/Kerberos single sign-on and talks to the API over HTTPS using application-level request signing. Access to the normal PIM interface is gated by a configured application-user Active Directory group: the web host reads the authenticated Windows user's primary SID and evaluates current, transitive group membership against a writable domain controller. It does not trust browser-supplied claims, PAC data, or cached token groups as authorization evidence - any missing, malformed, or unverifiable directory result denies access.

The only other Active Directory lookup the frontend performs on its own is cosmetic: resolving the current user's display name for the "Welcome, ..." greeting in the top bar. This is a best-effort, read-only lookup that falls back silently to the raw Windows account name on failure, and it carries no authorization weight whatsoever.

## Design philosophy

The interface favors clarity and function over visual complexity: modern, responsive, intuitive, consistent, accessible, and fully keyboard-operable across desktop, tablet, and mobile. Dashboards and effects take a back seat to making a request's consequences unambiguous.

For every request, the interface makes the following visible at a glance: the authenticated actor account, the selected target account, the target group, purpose, criticality, duration, expiry, the required second factor (if any), whether a reason or ticket reference is mandatory, and the outcome. When the actor account and target account differ, that distinction is called out prominently in the final confirmation step - this is exactly the case where a person is requesting privilege on someone else's behalf, and it must never be easy to overlook. Important actions are visually distinct, and critical administrative actions require deliberate, explicit confirmation rather than a single click.

Error messages are written to be understandable and security-conscious: they include a correlation ID for support purposes and follow the disclosure rules described in [api-reference.md](api-reference.md) (no internal details, stack traces, or object existence hints leak into a generic-looking error).

The visual design is built on Bootstrap 5 and Bootstrap Icons rather than a bespoke component library, themed with a small set of design tokens for color, spacing, and radius so that both custom and untouched Bootstrap elements stay visually consistent.

## Request flow

The primary flow starts at a landing page with branding and navigation into the request form, the requester's own request history, and the MFA overview. The request form itself is the direct-entitlement flow: it shows only the current entitlement options the API actually returns for the signed-in actor, together with their effective TTL constraints - duration is presented in hours, never in raw seconds or minutes, and the UI only ever offers values already permitted by the effective entitlement and group policy. A selected option is presentation data only; the API re-evaluates entitlement and policy again immediately before dispatching anything to the AD Worker.

If the target group requires approval, the request is submitted normally and a successful submission that lands in an "awaiting approval" state is shown as such - the frontend has no approval logic of its own; it only reflects the state the API returns. See [identity-and-lifecycle.md](identity-and-lifecycle.md) for the full request state machine.

If the target group requires a second factor, the API first checks - before any request is even created - that the person has at least one active factor among the types the group allows, and rejects immediately if not, so nobody is handed a transaction they have no way to complete. Otherwise the API returns an "awaiting second factor" status and the form shows a confirmation step that names the actor account and the target account separately, exactly as required whenever the two differ (see above). The step offers whichever factor types the policy allows, side by side - a passkey confirmation button when FIDO2 is allowed, a one-time-code field when TOTP is allowed - both leading into the same confirm cycle, just against a different verification endpoint. The frontend never marks a factor as successful before the server-side check has actually confirmed it.

The result of a submission - success, rejection/failure, or "awaiting approval" - is shown in a fixed panel that stays visible without scrolling, even on a long form that includes MFA confirmation and a ticket reference. On a completed submission the reason, ticket reference, and duration fields reset for the next request while the group/account selection is kept; on an error, nothing is cleared, so the person can correct and resubmit without retyping.

A separate view lists the signed-in person's own requests as a compact, paginated, filterable table (by month/year, entitlement, or target group), with a row expanding to show the underlying identifiers, reason, and ticket reference. The frontend only supplies the current actor identity and the chosen presentation filters in its signed request; which requests belong to that person is determined by the API against current identity data, not by anything the browser asserts.

Approvers get their own work area listing requests currently awaiting their decision, with inline approve/reject actions (rejection requires a typed reason). Its visibility in navigation is a convenience - the page and every approve/reject action independently re-verify the approver's right on the server, so hiding or showing the menu entry never functions as the actual access control.

## Multi-factor authentication in the UI

A status badge in the top bar, shown on every page, indicates per-factor whether the signed-in person has an active TOTP secret and/or an active passkey - green when set up, with a click revealing the enrollment date; amber when not set up, with a click leading straight into enrollment. The badge refreshes on every in-app navigation so it reflects a just-completed enrollment or an administrator-triggered revoke without a full page reload.

A dedicated MFA overview page links to enrollment/management for each factor type:

**TOTP enrollment** rejects a second concurrent attempt while an active factor already exists - recovering from a lost authenticator is a separate, audited administrative action (see below), not self-service re-enrollment. It displays a QR code (rendered server-side, not via a browser-side script) plus the same secret in copyable form for manual entry, and requires a confirmation code from the authenticator app before the factor becomes active. The raw secret is shown exactly once and never again.
**Passkey (FIDO2) enrollment** allows multiple active passkeys per person, so there is no equivalent reject-on-existing rule. A person's very first second factor of any kind can be registered on Windows authentication alone; every registration after that - another passkey, or a first passkey when TOTP is already active - requires proving an already-active factor first. The actual WebAuthn ceremony runs in the browser through a thin interop layer that only shuttles opaque byte data back and forth; all challenge generation and cryptographic verification happens server-side. A newly registered passkey is active immediately - there is no separate confirm-with-code step the way there is for TOTP, because the ceremony itself already proves possession and user verification.

Throughout, the interface distinguishes clearly between three different moments that could all loosely be called "MFA": signing in, re-confirming identity, and confirming one specific membership request. For a privileged request, the required factor is asked for immediately before submission and, where policy demands it, bound to that specific request - a confirmed factor becomes invalid again if the person, actor account, target account, or target group changes relative to what was actually confirmed. See [security-model.md](security-model.md) for how that binding, transaction-tying, enrollment, recovery, and replay protection actually work; the frontend's job is only to present the right step at the right time and never claim success before the server has actually confirmed it.

## Administration area

A separate, gated section of the application covers operational administration: managing which AD accounts are known to the system as people and target accounts, maintaining target-group policy and their approvers, managing direct entitlements, running directory-reconciliation checks against AD, and cleaning up orphaned in-flight requests (second-factor confirmations or approvals whose window has lapsed unconsumed). Every mutation in this area is independently guarded by a configured administrator group, optimistic-concurrency checks, and audit logging - the frontend enforces none of this itself, it only presents the workflow.

Administrators can also revoke a person's TOTP factor or FIDO2 credential - a soft-delete, never a hard delete, and always audited - as the only route back to a working second factor after a lost device, matching the deliberate "separately authorized, audited administrative workflow" the enrollment rules above require. A related view lets administrators look up the technical cause behind a generic error a user reports, keyed by its correlation ID, without exposing internals in the original HTTP response; this technical error log is kept structurally separate from the security/business audit trail described in [audit-and-observability.md](audit-and-observability.md). A dedicated audit-log view lets administrators search and filter the security/business audit trail itself.

The administration area also has a settings hub covering configuration that doesn't fit the entity-management pages above: per-target-group ticket-reference pattern management (the regular expressions described in [identity-and-lifecycle.md](identity-and-lifecycle.md)), and the TOTP secret-protection certificate's status and rollover workflow (validity window, private-key accessibility, and the typed-confirmation-gated re-encryption of every stored TOTP secret to a new certificate - see [operations.md](operations.md) for the operational procedure).

Across the administration area, critical or destructive actions consistently require an explicit confirmation step - a modal dialog for create/edit flows, or an inline confirmation panel for destructive ones - rather than taking effect on a single click.

## Localization

The interface is available in German (the default) and English, switchable at any time from a dropdown in the
top bar. Translations are compiled directly into the frontend application as resource files - there is no
database table or API call involved in choosing a language, consistent with the frontend never being
authoritative for anything beyond how it presents data. Because the frontend maintains a live connection to the
server for each session, switching language triggers a full page reload so the new language takes effect
everywhere at once, rather than updating only part of the page.

## What the frontend is not

The frontend contains no domain or authorization logic. It cannot create or delete Active Directory objects, cannot decide entitlement or approval outcomes, cannot mark a second factor as verified on its own say-so, and cannot bypass the duration limits an entitlement and group policy allow. Everything it displays is presentation over data the API returns; Active Directory and the system's SQL Server database remain the only authoritative sources of truth. See [architecture.md](architecture.md) for how the frontend fits into the overall component boundary, and [security-model.md](security-model.md) for why that boundary is enforced the way it is.
