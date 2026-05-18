# Emberglass Agent Guide

This file is for Codex and other coding agents working in this repository. Keep changes narrow, evidence-led, and specific to Emberglass's V Rising networking/runtime-sharing beta.

## Start Here

- Before acting, restate the exact problem, main non-goals, and the condition that should make you stop instead of pushing forward.
- Check the current branch and worktree before editing. Do not rebase, push, merge, delete, retarget, or rewrite branches unless explicitly asked.
- Prefer small, local changes that follow the existing Emberglass patterns. Avoid broad rewrites, style-only churn, or generic architecture cleanups.
- Treat adjacent repos such as Bloodcraft, Eclipse, and Emery as evidence or consumers. Do not move consumer-owned behavior into Emberglass unless the task explicitly asks for that promotion.

## Build And Verification

- Run the repository bootstrap before any `dotnet` build or test work:

  ```bash
  bash .codex/install.sh
  ```

- On this Windows machine, default `bash` may resolve to WSL without a distro. If that happens, use Git Bash:

  ```powershell
  & 'C:\Program Files\Git\bin\bash.exe' .codex/install.sh
  ```

- Use this verification ladder unless the task is explicitly docs-only:
  - `git diff --check`
  - `bash .codex/install.sh`
  - `dotnet test .codex/tests/Network/Emberglass.Network.Tests.csproj --configuration Release -p:DeployToServer=false -p:DeployToClient=false --no-restore` when networking/runtime surface confidence is needed
- `Emberglass.csproj` copies build output to local V Rising plugin folders by default when they exist. Use `-p:DeployToServer=false -p:DeployToClient=false` for tests or compile checks that should not stage DLLs.
- Keep Codex tooling, probes, temporary receipts, and agent-only tests under `.codex/`.

## V Rising Modding Constraints

- Preserve BepInEx, Harmony, VampireReferenceAssemblies, IL2CPP, and V Rising lifecycle assumptions unless the task is specifically to change them.
- Be careful around static initialization, world access, IL2CPP registration, client/server readiness, and main-thread dispatch. Do not move runtime lookups earlier without proving the startup path still works.
- For `VNetwork`, maintain typed packet registration, direction-aware dispatch, handshake trust behavior, and main-thread callback routing. Avoid reintroducing brittle one-off `ChatMessage` bridge assumptions into new Emberglass APIs.
- For VShare and hotload work, treat transfer success, digest/trust checks, duplicate assembly handling, and runtime load receipts as separate proof points.
- Avoid importing patterns from consumer mods wholesale. Use Bloodcraft and Eclipse as compatibility evidence, not as direct donors for public Emberglass API shape.

## Release And Metadata Boundaries

- Keep the canonical version plain `X.Y.Z` in `Emberglass.csproj`, `manifest.json`, `thunderstore.toml`, and the top `CHANGELOG.md` entry.
- Do not commit branch-derived `-pre` or `-ft.*` versions. Those are CI outputs only.
- For public-surface changes, update the relevant docs in `docs/` with the same stability posture as the code. `VNetwork` is the headline beta surface; other APIs should keep their documented stability labels.
- Defer final README, changelog, and Thunderstore wording until after build and focused verification when a feature branch is still being stabilized.

## Workflow And Review Guidance

- For GitHub Actions changes, prefer minimal reliability fixes and use YAML-aware validation. Do not run `bash -n` against workflow YAML.
- For shell installer changes, verify through `.codex/install.sh` and limit shell linting to real shell scripts.
- For API or ABI changes, check existing consumers and tests before narrowing overloads, renaming members, or changing event signatures.

## Stop Conditions

- Stop if remote or GitHub state cannot be verified for a branch/merge-train task.
- Stop if the available evidence cannot distinguish Emberglass-owned behavior from consumer-mod behavior, stale installs, dependency mismatch, or local environment failure.
- Stop if verification fails in a way that would require broadening beyond the requested scope.
- Stop before advising public release, Thunderstore publication, or consumer migration when build/test evidence or runtime receipts do not support the claim.
