# Contributing to QControl

QControl is currently working toward its first alpha. Start with the
[domain language](CONTEXT.md), [design](docs/DESIGN.md), and accepted
[architecture decisions](docs/adr/) before proposing behavioral changes.

## Development standard

- Work one behavior issue at a time in the order and dependency structure in
  [`docs/PLAN.md`](docs/PLAN.md).
- Agree on the public test seam before writing a test.
- Follow red → green for each vertical slice; test behavior through public
  interfaces rather than internal classes.
- Record the observed failing test before implementation and provide targeted,
  full-suite, and applicable real-boundary evidence afterward.
- Keep credentials, media names, torrent names, private network details, and
  local test state out of commits and issue/PR output.
- Do not contact public trackers or operator Jellyfin/qBittorrent services from
  automated tests.

The full requirements are in the [testing strategy](docs/TESTING.md). Until the
first alpha exists, open a design discussion before implementing behavior not
already covered by an accepted ADR and issue specification.
