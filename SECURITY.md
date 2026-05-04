# Security policy

## Supported versions

Only the latest minor release on `main` is actively supported. Older releases may receive security fixes on a best-effort basis.

| Version | Supported |
|---|---|
| 1.0.x | ✅ |
| < 1.0 | ❌ |

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Instead, use GitHub's [private security advisories](https://github.com/alexisometric/nopaynoplay/security/advisories/new) to report the vulnerability privately. Include:

- a description of the issue,
- the affected version,
- steps to reproduce or a proof of concept,
- the impact you anticipate.

You should receive an acknowledgement within a few days. We will work with you on a fix and coordinate disclosure once a patched release is available.

## Scope

In scope:

- Authentication / authorization bypass
- Privilege escalation
- Injection (SQL, command, XSS, …)
- Sensitive data exposure (IBAN, PayPal links, etc.)

Out of scope:

- Issues that require physical access to the server
- Bugs that only affect non-default Jellyfin configurations marked as unsafe
- Third-party plugins (e.g. File Transformation) — please report those upstream
