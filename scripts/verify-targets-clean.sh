#!/usr/bin/env bash
# Pre-push gate that mirrors the CI 'Verify samples are regenerated cleanly'
# step — runs `dotnet build` for the whole solution and fails if any file
# under targets/ ends up dirty (meaning the C# source was changed without
# committing the regenerated TS output).
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

if ! dotnet build Metano.slnx --nologo -v q; then
  echo "verify-targets-clean: dotnet build failed — fix the build before pushing." >&2
  exit 1
fi

if ! git diff --quiet -- targets/; then
  echo "verify-targets-clean: generated TS under targets/ is out of sync." >&2
  echo "Run 'dotnet build Metano.slnx', commit the regen, and push again." >&2
  git status --short -- targets/ >&2
  exit 1
fi
