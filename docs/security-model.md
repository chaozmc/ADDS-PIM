# Security Model

This page is the security reference for ADDS-PIM: how a membership request is actually authorized, how users and administrators are authenticated (including multi-factor authentication), how requests from the Web frontend to the API are protected in transit, how the system's own service identities are scoped to the minimum rights they need, and how secrets and configuration are handled. It is written for security governance staff and architects who need to understand the guarantees the system does - and does not - provide.

For the identity model behind "person," "actor account," and "target account," see `identity-and-lifecycle.md`. For how AD writes are executed and verified, see `active-directory-worker.md`. For how security-relevant events are recorded, see `audit-and-observability.md`.

## Authoritative sources of truth

SQL Server is authoritative for people, the AD accounts linked to them, and which concrete combination of person, privileged target account, and target group may be requested through ADDS-PIM. Active Directory is authoritative for the existence, uniqueness, scope, and current membership state of the AD objects themselves. The Web frontend is never an authorization source: it can display and prepare a request, but nothing it renders, caches, or has the user sign is treated as proof that a request is allowed.

Identity itself is modeled as three distinct roles - the person, the actor account that authenticated the current session, and the target account that receives the requested membership - which are checked independently rather than assumed to be interchangeable. See `identity-and-lifecycle.md` for the full model; it matters here because several of the authorization conditions below reference person, actor account, and target account as separate, independently verified entities, and a request never derives the target account from the actor account by default.

## The authorization decision

A membership request always names its person, actor account, target account, and target group explicitly; none of these are inferred or silently defaulted. A non-empty business justification is mandatory for every request, unconditionally - this cannot be turned off by group policy.

The single most important rule in this model is that **the API re-evaluates the complete authorization decision from current server-side data immediately before handing a request off for execution.** A decision that was computed earlier, cached, or merely displayed and confirmed in the frontend is not authorization evidence. This matters in practice because entitlements, policies, and AD state can change between the moment a user opens a request form and the moment the request actually executes; the system always uses what is true right now, not what was true when the page was loaded.

A request may proceed only when every applicable one of the following holds:

- The actor account and the calling client are authenticated and unambiguously identified, and the actor account is linked, server-side, to exactly one active person.
- The request's technical signature and replay protection succeed (see [Request signing](#request-signing-web-to-api)).
- An active link connects the person to the specific target account and permits using that account as a privilege target.
- A currently active direct entitlement connects the person, the target account, and the target group. (Indirect entitlement through nested AD group membership is not currently supported - see [Scope of entitlements](#scope-of-entitlements-direct-only) below.)
- The relevant links, entitlement, target group, and policy are all active and within their validity periods at execution time.
- The requested TTL falls within the target group's configured minimum, maximum, and default TTL, and uses one of its allowed TTL steps.
- The target group's concrete policy is satisfied: any required re-verification/MFA, and any required binding of that factor to this specific transaction, actually took place.
- The justification is non-empty; any configured ticket-reference or approval requirement is satisfied. If the effective policy requires approval, a positive decision from an approver assigned to that target group is additionally required (see [Approval](#approval) below).
- The actor account, target account, and target group each resolve uniquely and within the allowed scope, and the target account is actually eligible for the operation.
- The target account's existing memberships, concurrent activity, and idempotency state permit the request to execute (so a duplicate click, retry, or replayed request cannot double-execute a grant).

This is a **deny-by-default** model: any information that is missing, ambiguous, stale, or cannot be verified results in the request being denied rather than allowed through. Errors are classified through a shared error catalog, and every decision - granted or denied - is captured in the audit trail described in `audit-and-observability.md`.

### Scope of entitlements (direct only)

An entitlement is a direct link between one person, one target account, one target group, an active window, TTL bounds and steps, and a factor requirement. An entitlement for a person is never treated as implicitly applying to every AD account that person might control - it is scoped to the specific target account it names.

Supporting AD group membership itself as a source of entitlement (so that being nested into a group would grant the request right, rather than an explicit row in SQL) is a design extension that has not been built. Until that is deliberately designed - including how nested membership would be resolved, how nesting depth and cycles would be handled, and how freshness would be guaranteed - only direct, explicit entitlements for a specific person/target-account/target-group combination are honored.

### Per-group policy, not a fixed tier system

Each managed AD group is governed by its own explicit, application-managed policy: TTL bounds and steps, whether a second factor is required and which kind, whether a ticket reference is required, whether approval is required, and the group's active periods. These requirements are configured per group by an application administrator, deliberately rather than derived from a fixed classification scheme (such as labeling a group "standard," "sensitive," or "tier-0") - group names and informal labels carry no authorization meaning. This keeps the actual requirement for a given group explicit and inspectable rather than implied by a label that could drift out of sync with what the group actually protects.

### Approval

A target group's policy - or a direct entitlement that narrows it - can require approval before a request executes. The right to decide approvals is bound to the same person/account link that carries the person's interactive sign-on; an approver right attached to a different account belonging to the same person does not count. A target group can have zero, one, or several active approvers assigned to it; a decision (approve or deny) from any one assigned approver is final - there is no quorum requirement, and an approver may decide a request they submitted themselves.

Both the approval requirement itself and the deciding approver's current right to approve for that specific target group are re-checked against live data immediately before the decision takes effect - a previously displayed list of pending approvals is not authorization evidence, the same way a previously displayed entitlement isn't. A request left waiting for approval does not expire automatically; it can only be cleared through an explicit administrative cleanup action, which is itself audited.

## Authentication and MFA

ADDS-PIM distinguishes three separate things that are easy to conflate: signing in to the application, re-verifying the user during a session, and cryptographically confirming one specific membership request. A general MFA session - having recently completed a second factor for something - does not by itself satisfy a policy that requires the factor to be bound to this particular request.

### Signing in

Initial sign-in uses Windows Integrated Authentication and Kerberos single sign-on. The application never stores, logs, or evaluates domain credentials itself - it relies entirely on Windows authentication for the initial identity check. SAML 2.0 and OpenID Connect are potential future additions, not currently implemented.

Windows Integrated Authentication identifies the interactive actor account. The server-side link from that account to exactly one active person is a separate step (see `identity-and-lifecycle.md`), and succeeding at sign-in does not by itself authorize that account, or any other account linked to the same person, as a privilege target.

Application access itself - reaching the ordinary user interface at all, versus the administrative interface - is gated by transitive membership in one of two installation-configured AD groups (an application-user group and an application-administrator group), resolved from the signed actor's AD identity against a configured domain controller. Nested/intermediate groups are honored through Active Directory's own transitive-membership evaluation; group names, token claims, and SID history are not used as evidence. The Web layer uses the user-group check as a gate before showing its interface, but the API independently re-checks administrator-group membership immediately before every administrative mutation - the frontend's admin branch is a convenience, not an authorization decision.

Administrative access itself is deliberately flat: any member of the configured administrator group can perform every administrative capability in the system (person and account management, target-group policy changes, entitlement management, identity purge, directory reconciliation, approver assignment, ticket-pattern configuration, technical error-log access, and so on) - there is no internal separation of duties, such as a read-only auditor role or a policy-only role, among administrators. This is a deliberate, proportionate choice rather than an oversight: the administrator group is expected to stay a small, already-trusted set of people, and splitting administration into narrower sub-roles would add a permanent maintenance burden (every new administrative capability would need its own role decision) for a separation-of-duties requirement that has not actually arisen. The mitigating control is that administrator group membership is tightly scoped and AD-managed, and every administrative mutation is still individually audited against the acting administrator's identity - the flat model reduces *who is allowed to act*, not *how visible acting is*. Should a genuine need for a narrower administrative capability arise, the intended pattern is a purpose-specific, independently re-verified right (the same shape used for the approver right described above), not a general role system retrofitted onto this check.

### FIDO2 and passkeys

For highly privileged requests, phishing-resistant FIDO2/WebAuthn verification is the preferred second factor. The system supports credential registration, per-user multiple credentials, fresh one-time challenges with a defined lifetime, user verification, and signature-counter tracking, using a maintained FIDO2 library behind an application-owned interface rather than a generic platform passkey integration (the application already owns its own person/account model on top of Windows authentication, so it does not delegate to ASP.NET Identity's passkey support). Attestation is not required by default. Private FIDO2 keys never leave the authenticator or the user's device - the server stores only the public credential material, the signature counter, and enrollment/revocation metadata.

Revoking (resetting) a FIDO2 credential is an administrative action, not a self-service one, and there are no recovery codes for FIDO2 - losing every enrolled authenticator requires an administrator to intervene.

The signature-counter check rejects a replayed or cloned authenticator by comparing the counter reported in an assertion against the last stored value, but a reported counter of exactly zero is deliberately exempted from that comparison. This follows the WebAuthn specification, which explicitly permits authenticators that do not maintain a counter to report zero on every assertion - this is normal behavior for synced platform passkeys such as Apple's iCloud Keychain-backed Face ID/Touch ID credentials. Treating a reported zero as a regression would reject every assertion from those authenticators as a false "cloned credential" signal regardless of the device's actual state, so the check only flags a regression when the reported counter is nonzero and does not exceed the previously stored value.

### TOTP

Time-based one-time passwords (RFC 6238) are supported as an optional or transitional factor where a group's policy allows it. TOTP secrets are generated with sufficient entropy, encrypted at rest, and never persisted or logged in plaintext. Enrollment must be confirmed, the accepted time-step window is narrow, a given time step cannot be reused once consumed, and repeated failed attempts trigger rate limiting and a temporary lockout of the factor. Because TOTP is a shared-secret factor rather than a phishing-resistant one, it is never silently substituted for a policy that specifically requires FIDO2.

### Binding a factor to a specific request

Where a target group's policy requires a second factor, the factor confirmation itself is bound to the concrete transaction: the challenge covers the person, actor account, target account, target group, requested TTL, request ID, timestamp, and a nonce, so completing the factor confirms not just "this person has their authenticator" but specifically "this person authorizes granting membership to this target account, in this group, for this TTL." Both FIDO2 challenges and TOTP codes are protected against reuse. This transaction-binding is a separate mechanism from the technical request signature described next - a valid MFA confirmation does not imply a valid request signature, and vice versa.

## Request signing (Web-to-API)

All Web-to-API traffic runs over HTTPS. On top of that, every security-relevant request from the Web frontend to the API carries an application-level digital signature, independent of TLS.

### Why asymmetric signatures, not a shared secret

An earlier design used a shared HMAC secret: both the Web frontend and the API held the same key, distributed to each side through certificate-based envelope encryption. That design worked, but it meant a compromise of either the Web or the API process exposed a secret usable to forge requests from the other side, and rotating it required coordinating a secret-distribution step between two components.

The current design instead gives each Web frontend instance its own dedicated application-signing certificate, held in the Windows certificate store with its private key accessible only to that frontend's own service identity. The API never holds a private signing key for this purpose at all - it only stores the corresponding *public* certificates, explicitly allowlisted, and verifies signatures against them. This is a meaningful difference from the earlier approach: a compromise of the API cannot be used to forge a Web-originated request, because the API never possessed key material capable of creating one. The certificate is not a TLS certificate and is not trusted through a certificate chain - trust is explicit: the API only accepts certificates it has been told, out of band, to allowlist, along with their validity window and their being marked as certificates that this application actually uses for signing rather than some other purpose. Multiple certificates can be active at once, which is what allows several Web frontends to sign concurrently and allows a certificate to be rotated without a service interruption: a new certificate is registered and switched to active, requests continue to validate against either certificate for a short overlap window, and only then is the old certificate revoked.

### What is covered and what is checked

The signature binds a canonical representation of the request: HTTP method, API path, normalized query parameters, a hash of the request body (rather than the raw, arbitrarily-formatted JSON, so that non-semantic formatting differences can't break or bypass verification), a request ID, a correlation ID, an issued UTC timestamp, a cryptographic nonce, the signing client's identity, and content-type/API version where relevant to interpreting the request. For a membership request specifically, the signed content includes the exact references to person, actor account, target account, and target group - so swapping the target account after the request was signed is detectable as tampering with signed content, not a separate check bolted on afterward.

Before doing any business processing, the API verifies the client identity, the signature itself, that the algorithm and certificate are on the current allowlist, that the timestamp falls within an accepted window, and that the nonce and request ID have not been seen before. Nonce and request-ID uniqueness is tracked durably (not just in an in-memory cache), so replay protection survives a process restart and works across multiple API instances. A resubmission of a request that already succeeded is recognized through this durable record rather than executed twice; a resubmission that reuses an ID but changes the signed content is rejected outright.

### Relationship to mutual TLS and MFA

Request signing, mutual TLS, and user MFA are independent controls that are not substitutes for one another. Mutual TLS between components (used, for example, between the API and the AD Worker) protects the transport channel; it does not replace the application-level request signature described here. Likewise, a user having completed MFA for a request does not itself vouch for the technical validity of the Web-to-API signature - the two checks protect different things and both apply where relevant. Above all, a valid signature only proves the request came from an allowlisted Web instance unmodified in transit; it does not itself authorize the membership grant, which is still decided fresh by the authorization checks described earlier on this page.

## Least privilege and service identities

Every process boundary in ADDS-PIM - the Web frontend, the API, and the AD Worker - runs under its own technical identity (a Group Managed Service Account, or gMSA), and each identity is scoped to only the rights its own role actually requires. Separate accounts per environment or per deployed instance are expected. None of these service identities is broader than the narrowest set of rights that lets its component do its job.

**The Web identity** may start its own IIS application pool, read its own configuration, access its own request-signing private key, call the API over HTTPS, and write to its own technical logs. It has no direct AD write rights and no general database access.

**The API identity** may start its own application pool, hold the specific database rights it needs (via integrated security, not a stored credential), access the certificates and keys it is explicitly allowed to use, communicate with the AD Worker, write to its own logs and audit paths, and - only where architecturally required - hold narrowly scoped AD read rights. It has no rights to modify privileged AD groups; that capability belongs to the AD Worker alone. This split is the practical enforcement of the trust boundary described in `active-directory-worker.md`: the API decides *whether* a change should happen, but only the AD Worker is able to *make* it happen.

**The AD Worker identity** may start its own service, read the users, groups, and memberships it needs to operate on, and - critically - holds only *delegated* TTL-modification rights on the specific AD groups the installation has explicitly designated as in scope. It does not receive broad administrative rights over Active Directory. A target group being more sensitive than another does not, by itself, justify giving the Worker broader rights; if a deployment needs a genuinely separate Worker or service identity for its most sensitive groups, that is a distinct architectural decision to make deliberately, not a default.

**An installer identity**, where one is used, receives only the temporary rights needed for the specific installation step being performed. Running service identities do not inherit any lasting setup, schema- ownership, or account-management rights left over from installation.

Across all of these, private keys are made accessible, through Windows certificate-store access control lists, only to the specific identity that actually needs to use them - and routine rights checks or diagnostics are not permitted to expose private key material or other secrets as a side effect.

## Secrets and configuration

Configuration is separated into a few categories that are handled differently: ordinary non-sensitive application configuration, environment-specific configuration, security-relevant configuration, keys and certificates, and enrollment/recovery data.

Secrets and private keys are never checked into source control, written to unencrypted configuration files, logged, or embedded in test data. Private keys live in the Windows certificate store, protected by the access-control rules described above. Depending on what a given piece of configuration actually needs to protect, the system draws on the Windows certificate store, DPAPI, ASP.NET Core Data Protection, Windows Credential Manager, or protected registry locations - with a dedicated enterprise secret store as a possible future option for deployments that need one. Certificate expiry is monitored and expected to be reported ahead of time rather than discovered when a certificate has already lapsed; rollover requirements specific to the Web signing certificate are covered above under [Request signing](#request-signing-web-to-api).
