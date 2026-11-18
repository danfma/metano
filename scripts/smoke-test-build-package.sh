#!/usr/bin/env bash
set -euo pipefail

# Smoke-test the *packed* Metano.Build.TypeScript package the way a NuGet
# consumer uses it (baseline FR-087, #328).
#
# Packs Metano-packages.slnx with the release command line at a throw-away
# version into a local feed, then:
#   1. checks the package layout: both .targets files in build/ and
#      buildTransitive/, and no Metano.Build package any more;
#   2. installs the metano-typescript tool from the feed into a local tool
#      manifest and builds a throw-away consumer that references Metano and
#      Metano.Build.TypeScript with PackageReference; asserts the generated .ts
#      file, .metano-stamp and package.json, and that a second build skips the
#      transpile;
#   3. builds a consumer without the tool and asserts METANOBUILD001 with the
#      install instructions.
#
# The NuGet package cache, DOTNET_CLI_HOME and the tool lookup are isolated in a
# temporary directory, so the script neither reads nor pollutes the developer's
# caches or global tools. Exits non-zero on the first failing check. Used by CI
# and by the release workflow (before anything is tagged or published).
#
# Requires: the .NET SDK of global.json and access to nuget.org (the packages'
# own dependencies, such as Roslyn, come from there).
#
# Optional:
#   RUNNER_TEMP — parent directory for the throwaway feed and consumers.

readonly SMOKE_VERSION="99.0.0-smoke"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

WORK_DIR="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/metano-build-smoke.XXXXXX")"
FEED_DIR="$WORK_DIR/feed"
trap 'rm -rf "$WORK_DIR"' EXIT

export NUGET_PACKAGES="$WORK_DIR/nuget-packages"
export DOTNET_CLI_HOME="$WORK_DIR/cli-home"
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

# `dotnet metano-typescript` also resolves a `dotnet-metano-typescript` binary on
# PATH, so drop every global tool folder from it.
PATH="$(printf '%s' "$PATH" | tr ':' '\n' | grep -v '/\.dotnet/tools' | paste -sd ':' -)"
export PATH

step() {
  echo
  echo "==> $*"
}

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

pack_packages() {
  step "Packing Metano-packages.slnx at $SMOKE_VERSION"

  dotnet pack "$REPO_ROOT/Metano-packages.slnx" -c Release \
    -p:Version="$SMOKE_VERSION" -p:MinVerVersionOverride="$SMOKE_VERSION" \
    -p:PackageVersion="$SMOKE_VERSION" -o "$FEED_DIR" -v:quiet -nologo >/dev/null
}

check_package_layout() {
  step "Checking the package layout"

  local package="$FEED_DIR/Metano.Build.TypeScript.$SMOKE_VERSION.nupkg"
  [ -f "$package" ] || fail "Metano.Build.TypeScript was not packed"

  if compgen -G "$FEED_DIR/Metano.Build.$SMOKE_VERSION.nupkg" >/dev/null; then
    fail "the deprecated Metano.Build package is still packed"
  fi

  local entries
  entries="$(unzip -Z1 "$package")"

  for folder in build buildTransitive; do
    for file in Metano.Build.TypeScript.targets Metano.Build.Common.targets; do
      grep -qx "$folder/$file" <<<"$entries" || fail "$folder/$file is missing from the package"
    done
  done
}

# Writes a consumer project at $1; $2 is "with-tool" or "without-tool".
write_consumer() {
  local dir="$1"
  mkdir -p "$dir"

  cat >"$dir/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="smoke" value="$FEED_DIR" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

  cat >"$dir/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <MetanoOutputDir>generated/src</MetanoOutputDir>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Metano" Version="$SMOKE_VERSION" />
    <PackageReference Include="Metano.Build.TypeScript" Version="$SMOKE_VERSION" />
  </ItemGroup>
</Project>
EOF

  cat >"$dir/Todo.cs" <<'EOF'
using Metano.Annotations;

[assembly: EmitPackage("smoke-consumer")]

namespace Smoke;

[Transpile]
public record Todo(string Title, bool Done);
EOF

  # Global usings and a Directory.Build.* above the temp dir must not leak in.
  echo '<Project />' >"$dir/Directory.Build.props"
  echo '<Project />' >"$dir/Directory.Build.targets"

  if [ "$2" = "with-tool" ]; then
    (cd "$dir" && dotnet new tool-manifest >/dev/null &&
      dotnet tool install Metano.Compiler.TypeScript --version "$SMOKE_VERSION" >/dev/null)
  fi
}

check_consumer_build() {
  step "Building a consumer with the tool installed"

  local dir="$WORK_DIR/consumer"
  write_consumer "$dir" with-tool

  local first
  first="$(cd "$dir" && dotnet build -nologo 2>&1)" || {
    echo "$first"
    fail "the consumer build failed"
  }

  grep -q "Metano: Transpiling Consumer" <<<"$first" || fail "the first build did not transpile"
  [ -f "$dir/generated/src/.metano-stamp" ] || fail ".metano-stamp was not written"
  [ -f "$dir/generated/package.json" ] || fail "package.json was not written"
  grep -rqs "class Todo" "$dir/generated/src" --include='*.ts' || fail "no generated .ts declares Todo"

  local second
  second="$(cd "$dir" && dotnet build -nologo 2>&1)" || {
    echo "$second"
    fail "the second consumer build failed"
  }

  if grep -q "Metano: Transpiling" <<<"$second"; then
    fail "the up-to-date build ran the transpile again"
  fi
}

check_missing_tool() {
  step "Building a consumer without the tool"

  local dir="$WORK_DIR/consumer-without-tool"
  write_consumer "$dir" without-tool

  local output
  if output="$(cd "$dir" && dotnet build -nologo 2>&1)"; then
    fail "the build without the tool succeeded"
  fi

  grep -q "METANOBUILD001" <<<"$output" || {
    echo "$output"
    fail "the build without the tool did not report METANOBUILD001"
  }
  grep -q "dotnet tool install Metano.Compiler.TypeScript" <<<"$output" ||
    fail "METANOBUILD001 does not carry the install instructions"
}

pack_packages
check_package_layout
check_consumer_build
check_missing_tool

echo
echo "Metano.Build.TypeScript smoke test passed."
