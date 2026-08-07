# Contributing to ADDS-PIM

Thanks for your interest in ADDS-PIM. Please read this page before opening a pull request - it explains the licensing terms your contribution is made under.

## Licensing of contributions

ADDS-PIM is distributed under the [Prosperity Public License 3.0.0](LICENSE.md), which is free for noncommercial use and offers commercial users a thirty-day trial. Commercial licenses are available separately (see [Commercial licensing](#commercial-licensing)).

To keep that model workable, contributions are accepted under a permissive license. **By submitting a pull request, patch, issue attachment, or any other material for inclusion in ADDS-PIM, you agree that your contribution is offered under the terms of the** [**Apache License 2.0**](https://www.apache.org/licenses/LICENSE-2.0)**.**

This is the path the Prosperity license itself anticipates: contributing changes back under a standardized permissive license such as Apache 2.0 is expressly not treated as commercial use.

What this means in practice:

* You keep the copyright in your contribution. You are not assigning anything.
* The maintainer may distribute your contribution as part of ADDS-PIM under the Prosperity license **and** under separately negotiated commercial licenses.
* Your contribution carries the Apache 2.0 patent grant, which protects both you and downstream users.

## Developer Certificate of Origin

Please sign off every commit:

```
git commit -s -m "your message"
```

The sign-off is your statement that:

1. You wrote the contribution yourself, or you have the right to submit it under the Apache License 2.0.
2. The contribution is not encumbered by rights of a third party - your employer, a client, or another project's license.
3. You understand that your contribution and your sign-off are public and are kept as part of the project's permanent record.

If you are contributing in the course of your employment, make sure your employer permits it. This is the most common source of licensing trouble in projects like this one, and it is much easier to sort out before a merge than after.

## Third-party code and dependencies

If a change requires a new NuGet package, say so in the pull request and name the package's license - only permissive licenses (MIT, Apache 2.0, BSD, MS-PL) will be considered, and any addition must also be recorded in `THIRD-PARTY-NOTICES.txt`.

## Before you open a pull request

Given what this project does - modifying privileged Active Directory group memberships - a few things are non-negotiable:

* `dotnet build ADDS-PIM.slnx` and `dotnet test ADDS-PIM.slnx` must pass.
* Changes that touch the AD Worker, the authorization decision, MFA, or the audit trail need tests, and should be discussed in an issue first.
* The architectural invariants listed in the README are not up for incidental change. If you believe one needs to change, open an issue and argue for it rather than changing it in passing.
* Never commit real domain names, GUIDs, certificate thumbprints, account names, or anything else from a live environment. Placeholders only.

## Security issues

Please do **not** open a public issue for a security vulnerability. Report it privately to chaozmc@is-jo.org and allow reasonable time for a fix before any public disclosure.

## Commercial licensing

If you want to use ADDS-PIM commercially - including internal use inside a for-profit company beyond the thirty-day trial - contact chaozmc@is-jo.org.

