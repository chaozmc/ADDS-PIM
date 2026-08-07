# Dependency Policy

ADDS-PIM is a security-sensitive PAM system, so its policy on external dependencies is deliberately conservative: keep the dependency footprint as small as possible, and vet anything that gets added.

## Preferred sources first

Before reaching for a third-party package, prefer:

- Built-in .NET / ASP.NET Core functionality
- `Microsoft.Extensions.*` packages
- EF Core
- Other Microsoft-maintained packages

External libraries and NuGet packages are added only when the standard toolset genuinely doesn't cover the need.

## Checks before adding a new dependency

Every candidate dependency is evaluated against the following before it is introduced:

- **Necessity** - is there a real requirement that isn't already met by the platform or an existing dependency?
- **Platform alternative** - could a built-in or Microsoft-maintained option do the job instead?
- **Maintenance activity** - is the project actively maintained?
- **Security history** - does it have a track record of vulnerabilities, and how were they handled?
- **License** - is the license compatible with an open-source project?
- **Breaking-change behavior** - how disruptive are upgrades historically?
- **Transitive dependencies** - what does it pull in beyond the package itself?
- **Cost of replacement** - how hard would it be to swap out later if needed?
- **Encapsulation** - can it be wrapped behind an application-owned interface rather than leaking its API throughout the codebase?

Security-sensitive libraries - FIDO2/WebAuthn support in particular - have their actual ceremony/verification logic wrapped behind an application-owned interface rather than being called directly from business logic; the WebAuthn registration and assertion ceremonies are only ever invoked through that one boundary. The library's types are also unavoidably referenced once more, at the composition root where dependency injection is wired up - that's ordinary DI registration, not a second logic boundary, but it does mean the package name isn't literally confined to a single file. The point of the pattern is that a future replacement or version upgrade only has to touch the ceremony boundary and its registration, not scattered call sites throughout the application.

## UI component libraries

UI component libraries are only acceptable when they provide clear, concrete value, and they must not create disproportionate lock-in to a proprietary or short-lived frontend framework.

## Frameworks that just save a few lines

Mediator, mapping, validation, "Results," or CQRS frameworks are not adopted merely to save a small amount of boilerplate code. If the functionality is simple enough to implement directly in a few lines, that is preferred over taking on an additional dependency.

## Current third-party dependencies

The concrete list of currently used NuGet packages and .NET tools, each with its version and license, is
maintained separately in [`../THIRD-PARTY-NOTICES.txt`](../THIRD-PARTY-NOTICES.txt) rather than duplicated
here, so there is exactly one place to update when a dependency is added, upgraded, or removed. This page
describes the policy that governs those additions; the notices file is the current factual inventory.

## Related documentation

- [architecture.md](architecture.md) - overall system architecture and project layout
- [security-model.md](security-model.md) - security posture this policy supports
- [testing.md](testing.md) - how dependencies (including test-only ones) are exercised in CI
