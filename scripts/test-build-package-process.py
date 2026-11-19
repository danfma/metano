#!/usr/bin/env python3
"""Exercise the real smoke deadline and signal wrapper with a dotnet process tree."""

import os
from pathlib import Path
import signal
import subprocess
import sys
import time


root = Path(sys.argv[1])
smoke = Path(__file__).with_name("smoke-test-build-package.sh").resolve()
stub_dir = root / "stub-bin"
stub_dir.mkdir()
stub = stub_dir / "dotnet"
stub.write_text(
    "#!" + sys.executable + "\n" + r'''
import os, pathlib, signal, subprocess, sys, time
root = pathlib.Path(os.environ["PROCESS_FIXTURE"])
role = "child" if len(sys.argv) > 1 and sys.argv[1] == "child" else "parent"
def stop(signum, frame):
    (root / (role + ".signal")).write_text(str(signum))
    sys.exit(0)
signal.signal(signal.SIGTERM, stop)
signal.signal(signal.SIGINT, stop)
if role == "parent":
    (root / "wrapper.pid").write_text(str(os.getppid()))
    subprocess.Popen([sys.executable, __file__, "child"])
(root / (role + ".pid")).write_text(str(os.getpid()))
while True:
    time.sleep(0.05)
'''
)
stub.chmod(0o755)


def check_tree(name, termination=None):
    fixture = root / name
    fixture.mkdir()
    env = dict(os.environ, PATH=str(stub_dir) + os.pathsep + os.environ["PATH"])
    env["PROCESS_FIXTURE"] = str(fixture)
    # Leave enough time for both Python descendants to start on a busy CI runner.
    # Five seconds still proves the override instead of waiting for the 300s default.
    env["METANO_SMOKE_DOTNET_TIMEOUT_SECONDS"] = "5" if termination is None else "10"
    process = subprocess.Popen(
        ["bash", "-c", 'source "$1"; run_dotnet fixture', "fixture", str(smoke)],
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
            os.kill(int((fixture / "wrapper.pid").read_text()), termination)
        output = process.communicate(timeout=10)[0].decode()
        expected = 124 if termination is None else 128 + termination
        assert process.returncode == expected, (process.returncode, output)
        assert time.monotonic() - started < 10, "wrapper did not honor its deadline/signal"
        if termination is None:
            assert "5-second smoke-test deadline" in output, output
        else:
            for role in ("parent", "child"):
                assert (fixture / (role + ".signal")).read_text() == str(termination)
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


check_tree("deadline")
check_tree("sigterm", signal.SIGTERM)
check_tree("sigint", signal.SIGINT)
print("Build-package smoke deadline/process-group tests passed.")
