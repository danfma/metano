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
#      file, .metano-stamp and package.json, an asset a target adds before the
#      transpile, and that a second build skips the transpile;
#   3. builds a consumer without the tool and asserts METANOBUILD001 with the
#      install instructions.
#   4. builds a consumer of a bridge NuGet package (buildTransitive only), and
#      a project-reference graph with strict static-graph isolation.
#
# The NuGet package cache, DOTNET_CLI_HOME and the tool lookup are isolated in a
# temporary directory, so the script neither reads nor pollutes the developer's
# package caches or global tools. Only nuget.org's HTTP downloads are reusable.
# Exits non-zero on the first failing check. Used by CI
# and by the release workflow (before anything is tagged or published).
#
# Requires: python3, the .NET SDK of global.json and access to nuget.org (the packages'
# own dependencies, such as Roslyn, come from there).
#
# Optional:
#   RUNNER_TEMP — parent directory for the throwaway feed and consumers.
#   METANO_SMOKE_NUGET_CACHE — reusable nuget.org HTTP cache (default: repo obj/).
#   METANO_SMOKE_DOTNET_TIMEOUT_SECONDS — per-command deadline (default: 300 seconds).

readonly SMOKE_VERSION="99.0.0-smoke"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

WORK_DIR="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/metano-build-smoke.XXXXXX")"
FEED_DIR="$WORK_DIR/feed"
trap 'rm -rf "$WORK_DIR"' EXIT

export NUGET_PACKAGES="$WORK_DIR/nuget-packages"
export NUGET_HTTP_CACHE_PATH="${METANO_SMOKE_NUGET_CACHE:-$REPO_ROOT/obj/build-package-smoke-cache/http}"
mkdir -p "$NUGET_HTTP_CACHE_PATH"
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

# Bound every dotnet invocation, including the compiler processes MSBuild starts.
# Python is available on the CI runners and avoids a GNU timeout dependency on macOS.
smoke_wrapper_pid=""
terminate_smoke() {
  local signum="$1"
  trap '' TERM INT
  if [ -n "$smoke_wrapper_pid" ]; then
    kill -"$signum" "$smoke_wrapper_pid" 2>/dev/null || true
    wait "$smoke_wrapper_pid" 2>/dev/null || true
  fi
  exit "$((128 + signum))"
}
trap 'terminate_smoke 15' TERM
trap 'terminate_smoke 2' INT

run_dotnet() {
  local status=0
  python3 -c '
import math, os, signal, subprocess, sys
deadline = float(os.environ.get("METANO_SMOKE_DOTNET_TIMEOUT_SECONDS", "300"))
if not math.isfinite(deadline) or deadline <= 0:
    sys.exit("METANO_SMOKE_DOTNET_TIMEOUT_SECONDS must be a positive finite number")
process = subprocess.Popen(["dotnet", *sys.argv[1:]], start_new_session=True)

def kill_group(signum):
    try:
        os.killpg(process.pid, signum)
    except ProcessLookupError:
        pass

class SmokeSignal(BaseException):
    def __init__(self, signum):
        self.signum = signum

def forward_signal(signum, frame):
    # Unwind the active wait before reaping: wait() is not signal-reentrant.
    raise SmokeSignal(signum)

signal.signal(signal.SIGTERM, forward_signal)
signal.signal(signal.SIGINT, forward_signal)
try:
    sys.exit(process.wait(timeout=deadline))
except SmokeSignal as termination:
    signal.signal(signal.SIGTERM, signal.SIG_IGN)
    signal.signal(signal.SIGINT, signal.SIG_IGN)
    kill_group(termination.signum)
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        pass
    # The parent can exit before descendants that ignore the forwarded signal.
    kill_group(signal.SIGKILL)
    process.wait()
    sys.exit(128 + termination.signum)
except subprocess.TimeoutExpired:
    kill_group(signal.SIGKILL)
    process.wait()
    print(f"dotnet exceeded the {deadline:g}-second smoke-test deadline", file=sys.stderr)
    sys.exit(124)
' "$@" &
  smoke_wrapper_pid=$!
  wait "$smoke_wrapper_pid" || status=$?
  smoke_wrapper_pid=""
  return "$status"
}

# Retry only restore/download failures. Compilation, assertions and missing-tool
# failures must surface immediately, without being disguised as network flakiness.
restore_with_retry() {
  local attempt status
  for attempt in 1 2 3; do
    if run_dotnet "$@" >"$WORK_DIR/restore.log" 2>&1; then
      return 0
    else
      status=$?
    fi

    if [ "$attempt" = 3 ] || ! grep -Eiq 'NU130[01]|(^|[^[:alnum:]_])(status code( does not indicate success)?|HTTP)[^0-9]{0,3}(429|50[234])([^0-9]|$)|timed out|temporarily unavailable|SSL connection|unexpected EOF' "$WORK_DIR/restore.log"; then
      cat "$WORK_DIR/restore.log" >&2
      return "$status"
    fi

    echo "NuGet download failed; retrying ($attempt/3)" >&2
    sleep "$((attempt * 2))"
  done
}

pack_packages() {
  step "Packing Metano-packages.slnx at $SMOKE_VERSION"

  restore_with_retry restore "$REPO_ROOT/Metano-packages.slnx" \
    -p:Configuration=Release -p:Version="$SMOKE_VERSION" \
    -p:MinVerVersionOverride="$SMOKE_VERSION" -p:PackageVersion="$SMOKE_VERSION"

  run_dotnet pack "$REPO_ROOT/Metano-packages.slnx" -c Release --no-restore \
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
  <!-- An asset a target adds before the transpile must reach the tool, as in 2.x. -->
  <Target Name="AddSeed" BeforeTargets="MetanoTranspile">
    <ItemGroup>
      <MetanoAsset Include="seed.sql" OutputPath="db/seed.sql" />
    </ItemGroup>
  </Target>
</Project>
EOF

  echo "create table todo (title text);" >"$dir/seed.sql"

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
    (cd "$dir" && run_dotnet new tool-manifest >/dev/null &&
      restore_with_retry tool install Metano.Compiler.TypeScript --version "$SMOKE_VERSION")
  fi
}

check_consumer_build() {
  step "Building a consumer with the tool installed"

  local dir="$WORK_DIR/consumer"
  write_consumer "$dir" with-tool

  local first
  (cd "$dir" && restore_with_retry restore Consumer.csproj)
  first="$(cd "$dir" && run_dotnet build --no-restore -nologo 2>&1)" || {
    echo "$first"
    fail "the consumer build failed"
  }

  grep -q "Metano: Transpiling Consumer" <<<"$first" || fail "the first build did not transpile"
  [ -f "$dir/generated/src/.metano-stamp" ] || fail ".metano-stamp was not written"
  [ -f "$dir/generated/package.json" ] || fail "package.json was not written"
  grep -rqs "class Todo" "$dir/generated/src" --include='*.ts' || fail "no generated .ts declares Todo"
  [ -f "$dir/generated/src/db/seed.sql" ] || fail "the asset added by a target was not copied"

  local second
  second="$(cd "$dir" && run_dotnet build --no-restore -nologo 2>&1)" || {
    echo "$second"
    fail "the second consumer build failed"
  }

  if grep -q "Metano: Transpiling" <<<"$second"; then
    fail "the up-to-date build ran the transpile again"
  fi

  local changed
  changed="$(cd "$dir" && run_dotnet build --no-restore -nologo '-p:MetanoFilePrefix=// first%3Bsecond' 2>&1)" || {
    echo "$changed"
    fail "the changed-argument consumer build failed"
  }
  grep -q 'Metano: Transpiling Consumer' <<<"$changed" || fail "a changed -p: setting did not rerun the tool"
  grep -rqs '^// first;second$' "$dir/generated/src" --include='*.ts' || fail "the file prefix lost its semicolon"
  check_up_to_date "$dir" '-p:MetanoFilePrefix=// first%3Bsecond'
  step "Changed -p: setting reran; semicolon preserved; unchanged setting skipped"

  cat >"$dir/Directory.Build.props" <<'EOF'
<Project><PropertyGroup>
  <MetanoFilePrefix>// props;prefix</MetanoFilePrefix>
</PropertyGroup></Project>
EOF
  changed="$(cd "$dir" && run_dotnet build --no-restore -nologo 2>&1)" || {
    echo "$changed"
    fail "the Directory.Build.props consumer build failed"
  }
  grep -q 'Metano: Transpiling Consumer' <<<"$changed" || fail "a Directory.Build.props setting did not rerun the tool"
  grep -rqs '^// props;prefix$' "$dir/generated/src" --include='*.ts' || fail "the props file prefix lost its semicolon"
  check_up_to_date "$dir"
  step "Changed Directory.Build.props setting reran; unchanged setting skipped"

  cat >"$dir/Directory.Build.targets" <<'EOF'
<Project><PropertyGroup><MetanoExtraArgs>--file-prefix &quot;// extra;args&quot;</MetanoExtraArgs></PropertyGroup></Project>
EOF
  changed="$(cd "$dir" && run_dotnet build --no-restore -nologo 2>&1)" || {
    echo "$changed"
    fail "the Directory.Build.targets consumer build failed"
  }
  grep -q 'Metano: Transpiling Consumer' <<<"$changed" || fail "a Directory.Build.targets setting did not rerun the tool"
  grep -rqs '^// extra;args$' "$dir/generated/src" --include='*.ts' || fail "extra arguments lost their semicolon"
  check_up_to_date "$dir"
  step "Changed Directory.Build.targets extra args reran; semicolon preserved; unchanged setting skipped"
}

check_up_to_date() {
  local dir="$1"
  shift
  local output
  output="$(cd "$dir" && run_dotnet build --no-restore -nologo "$@" 2>&1)" || {
    echo "$output"
    fail "the up-to-date consumer build failed"
  }
  if grep -q 'Metano: Transpiling' <<<"$output"; then
    fail "unchanged effective arguments ran the transpile again"
  fi
}

check_transitive_consumer() {
  step "Building a buildTransitive-only consumer"

  local bridge="$WORK_DIR/bridge"
  write_consumer "$bridge" without-tool
  cat >"$bridge/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><PackageId>Smoke.Bridge</PackageId><Version>$SMOKE_VERSION</Version></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Metano" Version="$SMOKE_VERSION" />
    <PackageReference Include="Metano.Build.TypeScript" Version="$SMOKE_VERSION" PrivateAssets="none" />
  </ItemGroup>
</Project>
EOF
  (cd "$bridge" && restore_with_retry restore Consumer.csproj && run_dotnet pack --no-restore -c Debug -o "$FEED_DIR" -v:quiet)

  local dir="$WORK_DIR/transitive-consumer"
  write_consumer "$dir" with-tool
  cat >"$dir/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><MetanoOutputDir>generated/src</MetanoOutputDir></PropertyGroup>
  <ItemGroup><PackageReference Include="Smoke.Bridge" Version="$SMOKE_VERSION" /></ItemGroup>
</Project>
EOF
  (cd "$dir" && restore_with_retry restore Consumer.csproj)
  grep -q 'buildTransitive/Metano.Build.TypeScript.targets' "$dir/obj/Consumer.csproj.nuget.g.targets" || fail "the consumer does not import buildTransitive targets"
  local output
  output="$(cd "$dir" && run_dotnet build --no-restore -nologo 2>&1)" || {
    echo "$output"
    fail "the transitive consumer build failed"
  }
  grep -q 'Metano: Transpiling Consumer' <<<"$output" || fail "the transitive consumer did not transpile"
  [ -f "$dir/generated/src/.metano-stamp" ] || fail "the transitive consumer did not write its stamp"
  grep -rqs 'class Todo' "$dir/generated/src" --include='*.ts' || fail "the transitive consumer did not emit Todo"
  [ -f "$dir/generated/package.json" ] || fail "the transitive consumer did not emit package.json"
  check_up_to_date "$dir"
}

check_static_graph() {
  step "Building a strict isolated static graph"

  local dir="$WORK_DIR/graph-consumer"
  write_consumer "$dir" with-tool
  mkdir -p "$dir/library"
  cat >"$dir/library/Library.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>
EOF
  cat >"$dir/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><MetanoOutputDir>generated/src</MetanoOutputDir></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Metano" Version="$SMOKE_VERSION" />
    <PackageReference Include="Metano.Build.TypeScript" Version="$SMOKE_VERSION" />
    <ProjectReference Include="library/Library.csproj" />
    <Compile Remove="library/**/*.cs" />
  </ItemGroup>
</Project>
EOF
  (cd "$dir" && restore_with_retry restore Consumer.csproj)
  local first second
  first="$(cd "$dir" && run_dotnet msbuild Consumer.csproj -graphBuild -isolate -nodeReuse:false -nologo 2>&1)" || {
    echo "$first"
    fail "the isolated static-graph build failed"
  }
  grep -q 'Metano: Transpiling Consumer' <<<"$first" || fail "the graph consumer did not transpile"
  [ -f "$dir/generated/src/.metano-stamp" ] || fail "the graph consumer did not write its stamp"
  grep -rqs 'class Todo' "$dir/generated/src" --include='*.ts' || fail "the graph consumer did not emit Todo"
  [ -f "$dir/generated/package.json" ] || fail "the graph consumer did not emit package.json"
  second="$(cd "$dir" && run_dotnet msbuild Consumer.csproj -graphBuild -isolate -nodeReuse:false -nologo 2>&1)" || {
    echo "$second"
    fail "the second isolated static-graph build failed"
  }
  if grep -q 'Metano: Transpiling' <<<"$second"; then
    fail "the up-to-date static graph ran the transpile again"
  fi
}

check_missing_tool() {
  step "Building a consumer without the tool"

  local dir="$WORK_DIR/consumer-without-tool"
  write_consumer "$dir" without-tool
  (cd "$dir" && restore_with_retry restore Consumer.csproj)

  local output
  if output="$(cd "$dir" && run_dotnet build --no-restore -nologo 2>&1)"; then
    fail "the build without the tool succeeded"
  fi

  grep -q "METANOBUILD001" <<<"$output" || {
    echo "$output"
    fail "the build without the tool did not report METANOBUILD001"
  }
  grep -q "dotnet tool install Metano.Compiler.TypeScript" <<<"$output" ||
    fail "METANOBUILD001 does not carry the install instructions"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  pack_packages
  check_package_layout
  check_consumer_build
  check_transitive_consumer
  check_static_graph
  check_missing_tool

  echo
  echo "Metano.Build.TypeScript smoke test passed."
fi
