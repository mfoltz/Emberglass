# Emberglass release flow

Emberglass follows the Vampire Reference Assemblies Thunderstore packaging workflow as the umbrella guidance for V Rising package normalization. Shared packaging rules should be adopted only when they are backed by evidence from multiple repos; Emberglass-specific release identity and API promises remain repo-owned.

Bloodcraft is the current concrete CI precedent for Thunderstore publication. Its release workflow proves the preferred shape: validate canonical version metadata, select an existing GitHub Release tag, download the already-built release artifact, and publish that artifact with `tcli`.

## Canonical flow

1. Keep repo-local version metadata aligned:
   - `Emberglass.csproj` `<Version>`
   - `thunderstore.toml` `versionNumber`
   - `manifest.json` `version_number`
   - latest `CHANGELOG.md` entry
2. For release-bound PRs, record changes under `## Unreleased`, then run the bump helper once before merge:

   ```powershell
   pwsh .codex/scripts/bump-version.ps1 -Version 0.1.2
   ```

   The helper updates all version metadata and moves the current `Unreleased` notes into `## vX.Y.Z`, leaving a fresh empty `Unreleased` section for the next pass. Use a plain canonical `X.Y.Z`; CI derives `-pre` and feature-testing suffixes instead of committing them.
3. Run the local metadata gate:

   ```bash
   bash .codex/scripts/version-metadata.sh
   ```

4. Run the repo gates from the intended release commit:
   - `git diff --check`
   - `bash .codex/install.sh`
   - `.codex/tests/Network`
5. Rehearse the Thunderstore package locally and inspect the zip contents before upload:

   ```powershell
   pwsh .codex/scripts/package-thunderstore.ps1
   ```

6. Create a GitHub Release artifact from the same commit and binary hash intended for public release.
7. Publish to Thunderstore from the existing GitHub Release artifact through a manual CI workflow, not from an ad hoc local upload.

## Publication policy

- The public release promise is the networking beta centered on `VNetwork`.
- Keep canonical stable versions as plain `X.Y.Z` in tracked metadata.
- Do not commit branch-derived, feature-testing, or disposable prerelease version strings.
- Do not publish from a local working tree.
- Do not publish if package metadata, README, changelog, binary hash, network tests, trust bootstrap, or the Bloodcraft/Eclipse enabled bridge proof are out of sync.

## Windows/Codex GitHub auth note

On this Windows workstation, `gh auth status` from a Codex shell can report `The token in default is invalid`
even when Git HTTPS push and GitHub connector PR operations are healthy through their own credential paths.
Treat that as an execution-boundary signal, not immediate proof that credentials are globally broken.

Before asking for re-authentication or changing credentials, prefer the GitHub connector for PR inspection or
creation when it is available. Verify CLI-only work from the same keyring-aware boundary that will run it, and
keep `git push` evidence separate from `gh` CLI evidence because Git Credential Manager can succeed when `gh`
cannot.

## Remaining before public release

- Review the manual Emberglass release workflow in `.github/workflows/release.yml` after the final package artifact exists.
- Run one final network test pass and package rehearsal from the exact release commit.
- Rerun the Bloodcraft/Eclipse enabled bridge proof only if the release commit or release DLL changes after the 2026-05-13 green proof recorded in `docs/testing/bloodcraft-eclipse-bridge-proof.md`.
- Confirm the generated package zip hash matches the binary and receipt intended for upload.
- Keep `VEvents`, `VSystemBase`, detour helpers, config/menu/keybind helpers, and VShare outside the headline stable promise unless later proof promotes them.

## Shared vs repo-specific

Shared umbrella guidance belongs in the VRA Thunderstore packaging workflow when it is backed by evidence from multiple repos. Emberglass-specific release identity, API stability labels, proof receipts, dependency choices, and package wording stay in this repo.

Bloodcraft's workflow is a precedent, not a template dump. Emberglass should copy the contract only where it fits: version metadata validation, existing-release publication, allowed tag policy, and `tcli` publication. Build matrix details, changelog formatting, and package contents remain repo-owned unless the VRA workflow later normalizes them across the umbrella.
