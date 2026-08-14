# Issue 003 — qBittorrent API client and credentials

## Behavior

Provide a narrow qBittorrent 5.2 Web API adapter with bearer authentication,
version negotiation, stored/file credential sources, bounded requests, and
secret-safe diagnostics.

## Examples

- A valid API key reads versions and state from a compatible server.
- A Windows path or Unix path can supply the API key through a file.
- Invalid authentication produces a useful redacted status.
- A URL containing `user:password@host` is rejected.
- TLS certificate errors fail normally and cannot be bypassed by configuration.

## ADRs

- ADR-0005

## TDD sequence

1. Use a recording HTTP handler to write failing tests for bearer headers,
   base-path composition, timeouts, and cancellation.
2. Add version and compatibility parsing.
3. Add typed reads and allowlisted mutations required by `DESIGN.md`.
4. Add stored and secret-file credential providers using temporary files and
   platform-neutral path APIs.
5. Inject malformed responses, network failures, and secrets into errors to
   drive redaction tests.
6. Run the same client contract against pinned qBittorrent.

## Acceptance tests

- Only documented QControl endpoints are callable through the adapter.
- No mutation accepts `hashes=all`.
- API key appears only in the outbound bearer header.
- Configuration reads, status, journal fixtures, logs, and thrown errors contain
  no key or secret-file contents.
- qBittorrent 5.2 minimum and unsupported-version behavior are tested.
- Real-container contract evidence covers every endpoint used by V1.

## Out of scope

- Username/password and SID cookies.
- qBittorrent 4.x endpoints.
- General-purpose qBittorrent proxying.
- Certificate-trust management or VPN/public-IP checks.
