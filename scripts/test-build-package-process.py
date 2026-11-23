#!/usr/bin/env python3
"""Exercise the real smoke deadline and signal wrapper with a dotnet process tree."""

import ast
import io
import os
from pathlib import Path
import signal
import subprocess
import sys
import time
from types import SimpleNamespace
from unittest.mock import Mock


root = Path(sys.argv[1])
smoke = Path(__file__).with_name("smoke-test-build-package.sh").resolve()


def wrapper_source():
    return smoke.read_text().split("python3 -c '\n", 1)[1].split("\n' \"$nonce\" \"$@\" &", 1)[0]


WRAPPER_MODULE = ast.parse(wrapper_source())
stub_dir = root / "stub-bin"
stub_dir.mkdir()
stub = stub_dir / "dotnet"
stub.write_text(
    "#!" + sys.executable + "\n" + r'''
import os, pathlib, signal, subprocess, sys, time
if len(sys.argv) > 1 and sys.argv[1] == "exit":
    print("standard output", flush=True)
    print("standard error", file=sys.stderr, flush=True)
    sys.exit(int(sys.argv[2]))
root = pathlib.Path(os.environ["PROCESS_FIXTURE"])
role = "child" if len(sys.argv) > 1 and sys.argv[1] == "child" else "parent"
(root / (role + ".sigint-default")).write_text(str(signal.getsignal(signal.SIGINT) != signal.SIG_IGN))
def stop(signum, frame):
    (root / (role + ".signal")).write_text(str(signum))
    if role == "child" and os.environ.get("IGNORE_CHILD_SIGNAL") == "1":
        return
    sys.exit(0)
signal.signal(signal.SIGTERM, stop)
if os.environ.get("GRACEFUL_SIGINT") != "1":
    signal.signal(signal.SIGINT, stop)
if role == "parent":
    (root / "wrapper.pid").write_text(str(os.getppid()))
    subprocess.Popen([sys.executable, __file__, "child"])
(root / (role + ".pid")).write_text(str(os.getpid()))
try:
    while True:
        time.sleep(0.05)
except KeyboardInterrupt:
    stop(signal.SIGINT, None)
'''
)
stub.chmod(0o755)


def check_tree(
    name, termination=None, bash_only=False, ignoring_child=False,
    call_site="direct", graceful_sigint=False,
):
    fixture = root / name
    fixture.mkdir()
    env = dict(os.environ, PATH=str(stub_dir) + os.pathsep + os.environ["PATH"])
    env["PROCESS_FIXTURE"] = str(fixture)
    env["IGNORE_CHILD_SIGNAL"] = "1" if ignoring_child else "0"
    env["GRACEFUL_SIGINT"] = "1" if graceful_sigint else "0"
    # Leave enough time for both Python descendants to start on a busy CI runner.
    # Five seconds still proves the override instead of waiting for the 300s default.
    env["METANO_SMOKE_DOTNET_TIMEOUT_SECONDS"] = "5" if termination is None else "10"
    invocation = {
        "direct": "run_dotnet fixture",
        "subshell": 'run_in_directory "$PWD" run_dotnet fixture',
        "capture": 'status=0; capture_dotnet output "$PWD" fixture || status=$?; printf "%s\\n" "$output"; exit "$status"',
    }[call_site]
    process = subprocess.Popen(
        ["bash", "-c", 'source "$1"; ' + invocation, "fixture", str(smoke)],
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        start_new_session=True,
    )
    started = time.monotonic()
    try:
        while not (fixture / "child.pid").exists():
            if process.poll() is not None or time.monotonic() - started > 8:
                raise AssertionError("dotnet process tree did not become ready")
            time.sleep(0.01)
        if termination is not None:
            target = process.pid if bash_only else int((fixture / "wrapper.pid").read_text())
            signalled_at = time.monotonic()
            os.kill(target, termination)
        output = process.communicate(timeout=10)[0].decode()
        expected = 124 if termination is None else 128 + termination
        assert process.returncode == expected, (process.returncode, output)
        assert time.monotonic() - started < 10, "wrapper did not honor its deadline/signal"
        if termination is None:
            assert "5-second smoke-test deadline" in output, output
        else:
            assert time.monotonic() - signalled_at < 5, "signal was deferred until the dotnet deadline"
            for role in ("parent", "child"):
                assert (fixture / (role + ".signal")).read_text() == str(termination)
                if termination == signal.SIGINT:
                    assert (fixture / (role + ".sigint-default")).read_text() == "True", role
        # SIGKILL cannot write a marker. Confirm neither descendant is still running;
        # a briefly unreaped zombie is already dead, not a leaked compiler process.
        for role in ("parent", "child"):
            pid = (fixture / (role + ".pid")).read_text()
            stopped_deadline = time.monotonic() + 2
            while True:
                state = subprocess.run(
                    ["ps", "-p", pid, "-o", "stat="], capture_output=True, text=True, timeout=2
                ).stdout.strip()
                if not state or state.startswith("Z") or time.monotonic() > stopped_deadline:
                    break
                time.sleep(0.01)
            assert not state or state.startswith("Z"), (role, pid, state)
    finally:
        for pid_file in (fixture / "parent.pid",):
            if pid_file.exists():
                try:
                    os.killpg(int(pid_file.read_text()), signal.SIGKILL)
                except ProcessLookupError:
                    pass
        if process.poll() is None:
            os.killpg(process.pid, signal.SIGKILL)
        process.communicate(timeout=5)


def check_capture(exit_code):
    env = dict(os.environ, PATH=str(stub_dir) + os.pathsep + os.environ["PATH"])
    result = subprocess.run(
        ["bash", "-c", 'source "$1"; status=0; capture_dotnet result "$PWD" exit "$2" || status=$?; '
         'printf "%s\\n" "$result"; exit "$status"', "fixture", str(smoke), str(exit_code)],
        env=env, capture_output=True, text=True, timeout=10,
    )
    assert result.returncode == exit_code, result
    assert result.stdout == "standard output\nstandard error\n", result


def check_reaped_group(platform, states, refused):
    # Exercise the real embedded kill function without relying on Darwin's rare
    # EPERM/reaping race. A live group or another platform must still fail loudly.
    function = next(node for node in WRAPPER_MODULE.body
                    if isinstance(node, ast.FunctionDef) and node.name == "kill_group")
    namespace = {
        "os": SimpleNamespace(killpg=Mock(side_effect=PermissionError)),
        "process": SimpleNamespace(pid=123),
        "sys": SimpleNamespace(platform=platform),
        "subprocess": SimpleNamespace(run=Mock(return_value=SimpleNamespace(stdout=states))),
    }
    exec(compile(ast.Module(body=[function], type_ignores=[]), str(smoke), "exec"), namespace)
    try:
        namespace["kill_group"](signal.SIGKILL)
    except PermissionError:
        assert refused, (platform, states)
    else:
        assert not refused, (platform, states)


def run_reap_after_signal(refusal_count):
    # Drives the real kill_group_recording/reap_after_signal (AST-extracted
    # from the wrapper, not reimplemented) with a fake killpg that refuses
    # the first `refusal_count` calls, returning (reap_error, calls, stderr).
    functions = [
        node for node in WRAPPER_MODULE.body
        if isinstance(node, ast.FunctionDef) and node.name in ("kill_group_recording", "reap_after_signal")
    ]
    assert {f.name for f in functions} == {"kill_group_recording", "reap_after_signal"}, (
        "kill_group_recording/reap_after_signal were not found verbatim in the wrapper"
    )

    calls = []
    remaining = {"n": refusal_count}

    def fake_kill_group(signum):
        calls.append(("kill_group", signum))
        if remaining["n"] > 0:
            remaining["n"] -= 1
            raise PermissionError(f"Operation not permitted ({signum})")

    class FakeProcess:
        pid = 4242

        def wait(self, timeout=None):
            calls.append(("wait", timeout))

    stderr = io.StringIO()
    namespace = {
        "process": FakeProcess(),
        "kill_group": fake_kill_group,
        "signal": SimpleNamespace(SIGKILL=signal.SIGKILL, SIGTERM=signal.SIGTERM),
        "subprocess": SimpleNamespace(TimeoutExpired=subprocess.TimeoutExpired),
        "sys": SimpleNamespace(stderr=stderr),
    }
    exec(
        compile(ast.Module(body=functions, type_ignores=[]), str(smoke), "exec"),
        namespace,
    )
    reap_error = namespace["reap_after_signal"](signal.SIGTERM)
    return reap_error, calls, stderr.getvalue()


def check_reap_despite_eperm():
    # A killpg refusal (the Darwin EPERM race) must not skip the SIGKILL
    # escalation and the final wait; it must only fail after both ran (#717).
    reap_error, calls, stderr_text = run_reap_after_signal(refusal_count=1)
    assert isinstance(reap_error, PermissionError), reap_error
    assert calls == [
        ("kill_group", signal.SIGTERM),
        ("wait", 5),
        ("kill_group", signal.SIGKILL),
        ("wait", 10),
    ], calls
    assert "failed" in stderr_text, stderr_text

    # If the SIGKILL escalation is ALSO refused, its error — not the original
    # signal's — must be the one that surfaces, since it is the more final
    # failure; the call order and the final bounded wait must still happen.
    reap_error, calls, stderr_text = run_reap_after_signal(refusal_count=2)
    assert isinstance(reap_error, PermissionError), reap_error
    assert str(signal.SIGKILL) in str(reap_error), reap_error
    assert calls == [
        ("kill_group", signal.SIGTERM),
        ("wait", 5),
        ("kill_group", signal.SIGKILL),
        ("wait", 10),
    ], calls
    assert stderr_text.count("failed") == 2, stderr_text


def check_stale_pid_guard():
    # A wrapper.pid that outlives its process can be reused by an unrelated
    # one before the file is cleaned up (#717, a negligible-probability
    # window). verified_wrapper_pid must refuse a pid whose live command line
    # does not carry the nonce run_dotnet stamped on its own argv, instead of
    # trusting the file blindly. Exercised directly (not through a real
    # SIGTERM/terminate_smoke round trip): kill's own argument parsing would
    # already reject an unverified "pid nonce" pair, which would make an
    # end-to-end test pass for the wrong reason.
    real_nonce = f"nonce-{os.getpid()}-{id(object())}"
    live = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(10)", real_nonce])
    try:
        deadline = time.monotonic() + 5
        command = ""
        while time.monotonic() < deadline:
            command = subprocess.run(
                ["ps", "-ww", "-p", str(live.pid), "-o", "command="],
                capture_output=True, text=True, timeout=2,
            ).stdout
            if real_nonce in command:
                break
            time.sleep(0.05)
        assert real_nonce in command, "could not observe the live helper's own nonce via ps"

        def verified_pid(content):
            result = subprocess.run(
                ["bash", "-c", 'source "$1"; verified_wrapper_pid "$2"', "fixture", str(smoke), content],
                capture_output=True, text=True, timeout=5,
            )
            return result.stdout

        assert verified_pid(f"{live.pid} {real_nonce}") == str(live.pid), \
            "a live pid with its own nonce was not accepted"
        assert verified_pid(f"{live.pid} wrong-nonce") == "", \
            "a live pid with a mismatched nonce was accepted"
        assert verified_pid(str(live.pid)) == "", \
            "a legacy pid-only entry (no nonce) was accepted"
        assert verified_pid(f"999999999 {real_nonce}") == "", \
            "a nonexistent pid was accepted"
    finally:
        live.kill()
        live.wait(timeout=5)


check_reaped_group("darwin", "", False)
check_reaped_group("darwin", "123 Z\n456 S\n", False)
check_reaped_group("darwin", "123 S\n", True)
check_reaped_group("linux", "", True)
check_reap_despite_eperm()
check_stale_pid_guard()
check_capture(0)
check_capture(17)
check_tree("deadline")
check_tree("sigterm", signal.SIGTERM)
check_tree("sigint", signal.SIGINT)
check_tree("bash-sigterm", signal.SIGTERM, bash_only=True)
check_tree("bash-sigint", signal.SIGINT, bash_only=True)
check_tree("ignoring-child", signal.SIGTERM, ignoring_child=True)
check_tree("graceful-sigint", signal.SIGINT, bash_only=True, graceful_sigint=True)
for call_site in ("subshell", "capture"):
    check_tree(f"{call_site}-deadline", call_site=call_site)
    for termination in (signal.SIGTERM, signal.SIGINT):
        check_tree(f"{call_site}-{termination}", termination, bash_only=True, call_site=call_site)
    check_tree(
        f"{call_site}-graceful-sigint", signal.SIGINT, bash_only=True,
        call_site=call_site, graceful_sigint=True,
    )
print("Build-package smoke deadline/process-group tests passed.")
