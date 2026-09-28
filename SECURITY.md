# Security policy

## Reporting a vulnerability

Please **do not open a public issue** for security problems. Use GitHub's
[private vulnerability reporting](https://docs.github.com/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability)
("Report a vulnerability" on the repository's *Security* tab) and include steps to reproduce.

## Operating a bot securely

- **Protect the container's data.** The `signal-cli-config/` volume holds the account's private keys. Treat it like a password: never commit it, and back it up encrypted.
- **Keep the REST API private.** signal-cli-rest-api has no authentication. Bind it to `localhost` or an internal network, and never expose port 8080 to the internet.
- **Restrict privileged commands.** Use `[RequireAdmin]` / `[RequireGroupAdmin]` and configure `Signal:Commands:Admins`. Consider `Signal:AccessControl:AllowedSenders` for private bots.
- **Treat message text as untrusted input.** It comes from arbitrary Signal users.
