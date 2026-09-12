#!/usr/bin/env python3
"""VaultID one-shot setup and run script.

Runs, in order:
  1. Prerequisite checks (.NET SDK, Node.js, npm)
  2. dotnet restore + build of the service layer (services-layer/VaultID.Services.slnx)
  3. dotnet restore + build of the backend solution (backend/VaultID.slnx)
  4. npm install for the Angular frontend (frontend/vaultid-app)
  5. Optional test run (--with-tests)
  6. Optional production frontend build (--build-frontend)
  7. Starts the backend API (http://localhost:5080) and the Angular dev server
     (http://localhost:4200) together, streaming both logs until Ctrl+C.

Usage:
    python run_vaultid.py                 # full setup, then run both servers
    python run_vaultid.py --setup-only    # install + build, do not start anything
    python run_vaultid.py --run-only      # skip setup, just start both servers
    python run_vaultid.py --with-tests    # also run backend + frontend unit tests
    python run_vaultid.py --build-frontend
"""

from __future__ import annotations

import argparse
import os
import platform
import shutil
import signal
import subprocess
import sys
import threading
import time
from pathlib import Path
from typing import NoReturn

ROOT = Path(__file__).resolve().parent
SERVICES_DIR = ROOT / "services-layer"
BACKEND_DIR = ROOT / "backend"
API_DIR = BACKEND_DIR / "VaultID.Api"
FRONTEND_DIR = ROOT / "frontend" / "vaultid-app"

SERVICES_SLN = "VaultID.Services.slnx"
BACKEND_SLN = "VaultID.slnx"

API_URL = "http://localhost:5080"
APP_URL = "http://localhost:4200"

IS_WINDOWS = platform.system() == "Windows"


# ---------------------------------------------------------------- helpers ---


def log(message: str) -> None:
    print(f"\n=== {message}", flush=True)


def fail(message: str) -> NoReturn:
    print(f"\nERROR: {message}", file=sys.stderr, flush=True)
    sys.exit(1)


def resolve(program: str) -> str:
    """Find an executable on PATH, or abort with a helpful message."""
    found = shutil.which(program)
    if not found:
        fail(
            f"'{program}' was not found on PATH.\n"
            "  .NET 10 SDK: https://dotnet.microsoft.com/download\n"
            "  Node.js LTS: https://nodejs.org/"
        )
    return found


def run(cmd: list[str], cwd: Path, description: str) -> None:
    """Run a command to completion, aborting the script if it fails."""
    log(f"{description}\n    {' '.join(cmd)}  (cwd: {cwd})")
    result = subprocess.run(cmd, cwd=str(cwd))
    if result.returncode != 0:
        fail(f"{description} failed with exit code {result.returncode}.")


# ------------------------------------------------------------------ steps ---


def check_prerequisites() -> dict[str, str]:
    log("Step 1/6 — Checking prerequisites")
    tools = {
        "dotnet": resolve("dotnet"),
        "node": resolve("node"),
        "npm": resolve("npm.cmd" if IS_WINDOWS else "npm"),
    }
    for name, path in tools.items():
        version = subprocess.run(
            [path, "--version"], capture_output=True, text=True
        ).stdout.strip()
        print(f"  {name:7} {version or '(unknown)'}  -> {path}", flush=True)

    for required in (SERVICES_DIR / SERVICES_SLN, BACKEND_DIR / BACKEND_SLN,
                     FRONTEND_DIR / "package.json"):
        if not required.exists():
            fail(f"Expected file not found: {required}")
    return tools


def build_services(dotnet: str) -> None:
    log("Step 2/6 — Restoring and building the service layer")
    run([dotnet, "restore", SERVICES_SLN], SERVICES_DIR, "Restore service layer")
    run([dotnet, "build", SERVICES_SLN, "-c", "Release", "--no-restore"],
        SERVICES_DIR, "Build service layer")


def build_backend(dotnet: str) -> None:
    log("Step 3/6 — Restoring and building the backend (Domain + Application + Api)")
    run([dotnet, "restore", BACKEND_SLN], BACKEND_DIR, "Restore backend")
    run([dotnet, "build", BACKEND_SLN, "-c", "Release", "--no-restore"],
        BACKEND_DIR, "Build backend")


def install_frontend(npm: str) -> None:
    log("Step 4/6 — Installing frontend dependencies")
    cmd = [npm, "install", "--no-audit", "--no-fund"]
    print(f"    {' '.join(cmd)}  (cwd: {FRONTEND_DIR})", flush=True)
    result = subprocess.run(cmd, cwd=str(FRONTEND_DIR))
    if result.returncode != 0:
        # A corporate registry can produce truncated-JSON errors; retry publicly.
        log("npm install failed — retrying against the public npm registry")
        run(
            [npm, "install", "--no-audit", "--no-fund", "--legacy-peer-deps",
             "--registry=https://registry.npmjs.org"],
            FRONTEND_DIR,
            "Install frontend dependencies (public registry)",
        )


def run_tests(dotnet: str, npm: str) -> None:
    log("Step 5/6 — Running tests")
    run([dotnet, "test", BACKEND_SLN, "-c", "Release", "--no-build"],
        BACKEND_DIR, "Backend tests")
    run([npm, "run", "test", "--", "--watch=false", "--browsers=ChromeHeadless"],
        FRONTEND_DIR, "Frontend unit tests")


def build_frontend(npm: str) -> None:
    log("Building the frontend for production (dist/vaultid-app)")
    run([npm, "run", "build"], FRONTEND_DIR, "Frontend production build")


# ---------------------------------------------------------------- running ---


def stream(prefix: str, process: subprocess.Popen) -> None:
    assert process.stdout is not None
    for line in process.stdout:
        print(f"[{prefix}] {line.rstrip()}", flush=True)


def spawn(cmd: list[str], cwd: Path, prefix: str) -> subprocess.Popen:
    creationflags = subprocess.CREATE_NEW_PROCESS_GROUP if IS_WINDOWS else 0
    process = subprocess.Popen(
        cmd,
        cwd=str(cwd),
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        bufsize=1,
        creationflags=creationflags,
        start_new_session=not IS_WINDOWS,
        env={**os.environ, "FORCE_COLOR": "0"},
    )
    threading.Thread(target=stream, args=(prefix, process), daemon=True).start()
    return process


def terminate(process: subprocess.Popen, prefix: str) -> None:
    if process.poll() is not None:
        return
    print(f"[{prefix}] stopping...", flush=True)
    try:
        if IS_WINDOWS:
            process.send_signal(signal.CTRL_BREAK_EVENT)
        else:
            os.killpg(os.getpgid(process.pid), signal.SIGTERM)
        process.wait(timeout=15)
    except Exception:
        process.kill()


def run_everything(dotnet: str, npm: str, already_built: bool) -> int:
    log("Step 6/6 — Starting the backend API and the Angular dev server")
    print(f"    API : {API_URL}", flush=True)
    print(f"    App : {APP_URL}", flush=True)
    print("    Press Ctrl+C to stop both.\n", flush=True)

    api_cmd = [dotnet, "run", "-c", "Release"]
    if already_built:
        api_cmd.insert(2, "--no-build")
    api = spawn(api_cmd, API_DIR, "api")
    # Give the API a moment so the first frontend requests do not fail.
    time.sleep(5)
    web = spawn([npm, "start"], FRONTEND_DIR, "web")

    exit_code = 0
    try:
        while True:
            if api.poll() is not None:
                print(f"\n[api] exited with code {api.returncode}", flush=True)
                exit_code = api.returncode or 0
                break
            if web.poll() is not None:
                print(f"\n[web] exited with code {web.returncode}", flush=True)
                exit_code = web.returncode or 0
                break
            time.sleep(1)
    except KeyboardInterrupt:
        print("\nShutting down...", flush=True)
    finally:
        terminate(web, "web")
        terminate(api, "api")
    return exit_code


# ------------------------------------------------------------------- main ---


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Install, build and run the whole VaultID stack."
    )
    parser.add_argument("--setup-only", action="store_true",
                        help="Install and build only; do not start the servers.")
    parser.add_argument("--run-only", action="store_true",
                        help="Skip install/build; start the servers only.")
    parser.add_argument("--with-tests", action="store_true",
                        help="Run backend and frontend unit tests after building.")
    parser.add_argument("--build-frontend", action="store_true",
                        help="Also produce a production frontend build.")
    args = parser.parse_args()

    if args.setup_only and args.run_only:
        fail("--setup-only and --run-only cannot be used together.")

    started = time.time()
    tools = check_prerequisites()
    dotnet, npm = tools["dotnet"], tools["npm"]

    if not args.run_only:
        build_services(dotnet)
        build_backend(dotnet)
        install_frontend(npm)
        if args.with_tests:
            run_tests(dotnet, npm)
        if args.build_frontend:
            build_frontend(npm)
        log(f"Setup complete in {time.time() - started:.0f}s")

    if args.setup_only:
        print("\nSetup finished. Start the stack with: python run_vaultid.py --run-only",
              flush=True)
        return 0

    return run_everything(dotnet, npm, already_built=not args.run_only)


if __name__ == "__main__":
    sys.exit(main())
