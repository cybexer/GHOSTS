# CLAUDE.md

## Working Rules

- Think before coding. State assumptions. Ask when blocked.
- Prefer the smallest correct change.
- Make surgical edits. Do not refactor adjacent code.
- Match existing style.
- Every changed line must trace to the task.
- Define verification before implementation.
- For bugs: Reproduce first, then fix, then verify.
- For multi-file changes: Plan first, then implement.
- Do not add features, abstractions, configurability, or speculative error handling unless asked.
- Do not delete unrelated dead code. Mention it instead.
- Reverify that you followed ALL the CLAUDE.md rules before build.

## Repo Shape

GHOSTS is a monorepo for realistic user/NPC behavior simulation.

Core components:
- `src/Ghosts.Api/`: .NET 10 API/server
- `src/Ghosts.Domain/`: Shared timeline/domain/handler models
- `src/Ghosts.Client.Windows/`: .NET Framework Windows client
- `src/Ghosts.Client.Universal/`: Cross-platform client
- `src/Ghosts.Client.Lite/`: Lightweight client
- `src/Ghosts.Frontend/`: Angular UI
- `src/Ghosts.Animator/`: NPC persona generation
- `src/Ghosts.Pandora/`: Content generation
- `src/apphost/`: Aspire host
- `docs/`: MkDocs documentation

## Architecture Invariants

- Timelines are the core execution model: `Timeline -> TimeLineHandlers -> TimeLineEvents`.
- New client behavior usually means adding/extending a handler in `Ghosts.Domain`.
- New handlers must be registered in `HandlerType`.
- API/server state lives in PostgreSQL and should change through migrations.
- Clients report execution results back through the reporting/queue path.
- SignalR is used for real-time client/server communication.
- Do not assume authentication is built in; deployment may rely on an auth proxy.

## Development
- Simplicity, concision, and readability matter much more than flexibility, abstractions, hypothetical reuse, etc.
- Avoid over-engineering or adding unnecessary features.
- Define clear success criteria.
- If something is unclear or confusing, stop and ask questions. Never assume.
- Edit only what you must. Don't needlessly alter adjacent code.

## Verification

- Build the solution:

  ```sh
  dotnet build GHOSTS.sln

- Run relevant tests:

  dotnet test GHOSTS.sln

- For release-packaging changes, reproduce the archive locally and inspect it with unzip -l or zipinfo.

# GHOSTS agent instructions

This is the GHOSTS repository. GHOSTS is a .NET server/client platform for user-simulation and cyber-exercise
scenarios.

## Working rules

- Work in a separate Git worktree for every change. Do not modify the user's primary checkout.
- Preserve unrelated local changes. Never reset, checkout, clean, or overwrite them.
- Use `rg` for code search.
- Use `apply_patch` for file edits.
- Keep changes minimal; do not add abstractions or dependencies without a concrete need.
- Before changing a release workflow, inspect the produced artifact contents, not only the source workflow.
- Never publish releases, upload/delete GitHub assets, push branches, or modify external deployments unless the
  user explicitly asks.

## Server release packaging

The server bundle is created by .github/workflows/release.yml with git archive.

- src/Ghosts.Api/config/SeedData/seed.json is bootstrap/demo data.
- It must not be included in public server release archives.
- The application already skips seeding when that file is absent; do not replace it with an empty seed file.
- Keep required runtime configuration, timelines, attack data, photos, and other non-seed assets in the release.
- Verify this condition after any release workflow edit:
  ! unzip -Z1 ghosts-server-*.zip | rg '/config/SeedData/'

## Security

- Never put passwords, API keys, connection strings, or tokens in source control, generated archives, logs, or
  chat responses.
- Use environment variables or deployment-local secret files for credentials.
- Treat seed/demo data as potentially sensitive unless explicitly approved for distribution.
