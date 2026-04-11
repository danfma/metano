#!/usr/bin/env bash
set -euo pipefail

# Sync the npm package version to the git tag that triggered the release,
# then publish metano-runtime.
#
# Called from the release workflow after dotnet-releaser finishes.
# Must be run from the repository root.
#
# Expects:
#   GITHUB_REF_NAME — the tag name (e.g., "v1.2.3")
#   NODE_AUTH_TOKEN — npm auth token

if [ -z "${GITHUB_REF_NAME:-}" ]; then
  echo "ERROR: GITHUB_REF_NAME is not set" >&2
  exit 1
fi

VERSION="${GITHUB_REF_NAME#v}"

if [ -z "$VERSION" ] || [ "$VERSION" = "$GITHUB_REF_NAME" ]; then
  echo "ERROR: GITHUB_REF_NAME does not look like a version tag: $GITHUB_REF_NAME" >&2
  exit 1
fi

# Resolve the repo root based on the script's own location so this works
# whether you invoke it from the repo root or from anywhere else.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
RUNTIME_DIR="$REPO_ROOT/js/metano-runtime"

if [ ! -d "$RUNTIME_DIR" ]; then
  echo "ERROR: metano-runtime directory not found at $RUNTIME_DIR" >&2
  exit 1
fi

echo "Syncing metano-runtime npm version to $VERSION"
cd "$RUNTIME_DIR"

# Update package.json version field
jq --arg v "$VERSION" '.version = $v' package.json > package.json.tmp
mv package.json.tmp package.json
echo "Updated package.json to version $VERSION"

# Ensure dist/ exists (expected to have been built by the caller)
if [ ! -d "dist" ]; then
  echo "ERROR: dist/ not found — run 'bun run build' before publishing" >&2
  exit 1
fi

# Publish to npm. NODE_AUTH_TOKEN must be set; the .npmrc is configured
# by actions/setup-node in the workflow.
if [ -z "${NODE_AUTH_TOKEN:-}" ]; then
  echo "ERROR: NODE_AUTH_TOKEN is not set" >&2
  exit 1
fi

npm publish --access public
echo "Published metano-runtime@$VERSION to npm"
