## Summary

<!-- What does this pull request change, and why? -->

## Related issue

Fixes #

## Type of change

- [ ] Bug fix
- [ ] New feature
- [ ] Security hardening
- [ ] Refactoring / maintainability
- [ ] Documentation
- [ ] Tests
- [ ] Build / CI / tooling
- [ ] Other

## Components affected

- [ ] Web / Blazor frontend
- [ ] Backend API
- [ ] Application / domain logic
- [ ] Infrastructure
- [ ] Active Directory Worker
- [ ] Authentication / authorization
- [ ] MFA
- [ ] Database / migrations
- [ ] Audit / logging / monitoring
- [ ] Installer / deployment
- [ ] Documentation / GitHub Pages

## Security impact

<!-- Describe effects on authorization, trust boundaries, AD/gMSA privileges, request signing, replay protection, MFA, Tier-0 exposure or auditability. -->

## Active Directory / privilege impact

<!-- Does this modify AD reads/writes, TTL behavior, worker commands, delegated permissions, gMSA requirements or verification logic? -->

## Testing

- [ ] `dotnet build ADDS-PIM.slnx` passes
- [ ] `dotnet test ADDS-PIM.slnx` passes
- [ ] Relevant unit/integration/security tests were added or updated
- [ ] AD-related changes were tested only in an isolated test domain
- [ ] No productive Tier-0 groups were used for testing

## Database / migration impact

- [ ] No database schema change
- [ ] Migration included and tested
- [ ] Upgrade path considered

## Dependencies

- [ ] No new external dependency
- [ ] New dependency documented below and added to `THIRD-PARTY-NOTICES.txt`

<!-- If applicable: package name, version, license and reason. -->

## Documentation

- [ ] No documentation change required
- [ ] Relevant documentation updated
- [ ] Architecture decision / ADR added or updated where appropriate

## Contributor checklist

- [ ] I have read `CONTRIBUTING.md`.
- [ ] My commits include a Developer Certificate of Origin sign-off (`git commit -s`).
- [ ] I have the right to submit this contribution under the terms described in `CONTRIBUTING.md`.
- [ ] I did not include real domain names, account names, certificate thumbprints, secrets or other data from a live environment.
- [ ] I did not weaken an architectural security invariant as an incidental part of this change.

## Additional notes

<!-- Anything reviewers should know before reviewing this change. -->
