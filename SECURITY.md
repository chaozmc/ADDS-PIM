# Security Policy

ADDS-PIM manages privileged Active Directory group memberships. Security reports therefore need to be handled carefully and, where appropriate, privately.

## Reporting a vulnerability

**Do not open a public GitHub issue for a suspected security vulnerability.**

Please report security vulnerabilities privately by email:

**chaozmc[at]is-jo.org**

Include enough information to understand and reproduce the issue, but do not send real credentials, private keys, TOTP secrets, production authentication tokens or other unnecessary sensitive data.

Useful information may include:

- the affected ADDS-PIM version or commit,
- the affected component,
- prerequisites and configuration required to reproduce the issue,
- a minimal proof of concept,
- expected and observed security impact,
- suggested mitigations, if known.

Please allow reasonable time for investigation and remediation before public disclosure.

## Scope

Security-relevant areas include, among others:

- authentication and Windows/Kerberos sign-in,
- authorization and entitlement enforcement,
- MFA / FIDO2 / WebAuthn / TOTP,
- signed requests and replay protection,
- Active Directory Worker isolation,
- TTL-based privileged group membership execution,
- gMSA and delegated permission boundaries,
- certificate and key handling,
- audit integrity,
- privilege escalation,
- bypass of approval or policy enforcement,
- exposure of protected secrets.

General bugs, feature requests and documentation problems should use the public GitHub issue forms instead.

## Supported versions

ADDS-PIM is currently a feature-complete beta and is not yet declared production-ready. Security fixes are normally made against the current development line unless a release explicitly states otherwise.
