# zscripts/publish.py
# Usage:
#   python zscripts/publish.py <major|minor|patch|publish-only> <api_key>
#
# Behavior:
# - Finds repo root by walking upward until it finds a *.sln
# - If major/minor/patch:
#     - Bumps <Version>x.y.z</Version> in all *.csproj under:
#         Source/Analyzers, Source/CodeAnalysis, Source/CodeFix
#     - Builds solution (Release)
#     - Packs ALL "packable" projects found in those dirs (see is_packable_csproj)
#     - Pushes all produced .nupkg/.snupkg to nuget.org
#     - Stages the modified csproj files and commits:
#         "[MAJOR/MINOR/PATCH] Analyzer Upgrade"
# - If publish-only:
#     - Does NOT modify versions
#     - Builds solution (Release)
#     - Packs and pushes with current versions
#     - Does NOT touch git
#
# Notes:
# - API key is never printed.
# - Output packages are placed in artifacts/nuget/_publish (cleaned each run).

from __future__ import annotations

import re
import sys
import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, List, Tuple, Optional


NUGET_SOURCE = "https://api.nuget.org/v3/index.json"

PROJECT_DIRS = [
    Path("Source/Analyzers"),
    Path("Source/CodeAnalysis"),
    Path("Source/CodeFix"),
]

ARTIFACTS_DIR = Path("artifacts/nuget/_publish")

VERSION_RE = re.compile(r"(<Version>\s*)(\d+\.\d+\.\d+)(\s*</Version>)", re.IGNORECASE)
PACKAGE_ID_RE = re.compile(r"<PackageId>\s*([^<]+)\s*</PackageId>", re.IGNORECASE)
IS_PACKABLE_RE = re.compile(r"<IsPackable>\s*([^<]+)\s*</IsPackable>", re.IGNORECASE)


@dataclass(frozen=True, order=True)
class SemVer:
    major: int
    minor: int
    patch: int

    @staticmethod
    def parse(s: str) -> "SemVer":
        parts = s.strip().split(".")
        if len(parts) != 3:
            raise ValueError(f"Expected x.y.z, got '{s}'")
        return SemVer(int(parts[0]), int(parts[1]), int(parts[2]))

    def bump(self, kind: str) -> "SemVer":
        kind = kind.lower().strip()
        if kind == "major":
            return SemVer(self.major + 1, 0, 0)
        if kind == "minor":
            return SemVer(self.major, self.minor + 1, 0)
        if kind == "patch":
            return SemVer(self.major, self.minor, self.patch + 1)
        raise ValueError("Bump kind must be one of: major, minor, patch")

    def __str__(self) -> str:
        return f"{self.major}.{self.minor}.{self.patch}"


def eprint(*args: object) -> None:
    print(*args, file=sys.stderr)


def run(cmd: List[str], cwd: Path, redact_values: Optional[Iterable[str]] = None) -> None:
    # Print command but redact secrets.
    redacts = set(redact_values or [])
    printable_parts = []
    for part in cmd:
        if part in redacts and part:
            printable_parts.append("****")
        else:
            printable_parts.append(part)
    print("> " + " ".join(printable_parts))
    subprocess.run(cmd, cwd=str(cwd), check=True)


def find_repo_root() -> Path:
    """
    Walk upward from:
      - current working directory
      - script directory
    until we find a directory containing at least one *.sln.
    """
    starts = [Path.cwd().resolve(), Path(__file__).resolve().parent]
    visited = set()

    for start in starts:
        p = start
        while True:
            if p in visited:
                break
            visited.add(p)

            if any(p.glob("*.sln")):
                return p

            if p.parent == p:
                break
            p = p.parent

    raise RuntimeError("Could not find repo root (a directory containing a *.sln) from cwd or script location.")


def find_solution_file(root: Path) -> Path:
    slns = sorted(root.glob("*.sln"))
    if not slns:
        raise RuntimeError(f"No .sln found in repo root: {root}")
    if len(slns) > 1:
        eprint(f"Warning: multiple .sln files found in {root}. Using: {slns[0].name}")
    return slns[0]


def find_csproj_files(root: Path) -> List[Path]:
    files: List[Path] = []
    for rel_dir in PROJECT_DIRS:
        d = root / rel_dir
        if not d.exists():
            eprint(f"Warning: directory missing: {d}")
            continue
        files.extend(sorted(d.glob("*.csproj")))
    # De-dupe
    uniq = []
    seen = set()
    for f in files:
        rp = f.resolve()
        if rp not in seen:
            seen.add(rp)
            uniq.append(f)
    return uniq


def extract_versions(csproj_text: str) -> List[str]:
    return [m.group(2) for m in VERSION_RE.finditer(csproj_text)]


def replace_version(csproj_text: str, new_version: str) -> Tuple[str, int]:
    def repl(m: re.Match) -> str:
        return f"{m.group(1)}{new_version}{m.group(3)}"

    new_text, count = VERSION_RE.subn(repl, csproj_text)
    return new_text, count


def choose_base_version(projects: Iterable[Path]) -> SemVer:
    # Prefer the first project that has a Version tag (stable deterministic order).
    for p in sorted(projects, key=lambda x: x.as_posix()):
        text = p.read_text(encoding="utf-8")
        versions = extract_versions(text)
        if versions:
            return SemVer.parse(versions[0])
    raise RuntimeError("No <Version>x.y.z</Version> found in any targeted .csproj files.")


def bump_versions_in_projects(root: Path, bump_kind: str) -> Tuple[SemVer, SemVer, List[Path]]:
    csprojs = find_csproj_files(root)
    if not csprojs:
        raise RuntimeError("No .csproj files found in Source/Analyzers, Source/CodeAnalysis, Source/CodeFix.")

    base = choose_base_version(csprojs)
    newv = base.bump(bump_kind)
    newv_str = str(newv)

    print(f"Version bump: {base} -> {newv}")

    changed: List[Path] = []
    for p in csprojs:
        old_text = p.read_text(encoding="utf-8")
        versions = extract_versions(old_text)
        if not versions:
            raise RuntimeError(f"Missing <Version> in: {p.relative_to(root)}")

        new_text, count = replace_version(old_text, newv_str)
        if count <= 0:
            raise RuntimeError(f"Failed to replace <Version> in: {p.relative_to(root)}")

        if new_text != old_text:
            p.write_text(new_text, encoding="utf-8", newline="\n")
            changed.append(p)
            print(f"Updated: {p.relative_to(root)} (replacements: {count})")
        else:
            print(f"Unchanged: {p.relative_to(root)}")

    return base, newv, changed


def is_packable_csproj(csproj_path: Path) -> bool:
    """
    Heuristic:
    - Must contain <PackageId>...</PackageId>
    - Must NOT explicitly set <IsPackable>false</IsPackable>
    """
    text = csproj_path.read_text(encoding="utf-8")
    if not PACKAGE_ID_RE.search(text):
        return False

    m = IS_PACKABLE_RE.search(text)
    if m and m.group(1).strip().lower() == "false":
        return False

    return True


def clean_publish_output_dir(root: Path) -> Path:
    out_dir = root / ARTIFACTS_DIR
    if out_dir.exists():
        shutil.rmtree(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    return out_dir


def build_solution(root: Path, sln: Path) -> None:
    run(["dotnet", "build", str(sln), "-c", "Release"], cwd=root)


def pack_projects(root: Path, out_dir: Path) -> List[Path]:
    csprojs = find_csproj_files(root)
    packable = [p for p in csprojs if is_packable_csproj(p)]

    if not packable:
        raise RuntimeError("No packable projects found (no .csproj with <PackageId> in the target folders).")

    print("Packable projects:")
    for p in packable:
        print(f"  - {p.relative_to(root)}")

    for proj in packable:
        run(["dotnet", "pack", str(proj), "-c", "Release", "-o", str(out_dir)], cwd=root)

    produced = sorted(list(out_dir.glob("*.nupkg")) + list(out_dir.glob("*.snupkg")))
    if not produced:
        raise RuntimeError(f"No packages produced in {out_dir}")
    return produced


def push_packages(root: Path, packages: List[Path], api_key: str) -> None:
    # push .nupkg first, then .snupkg
    nupkgs = sorted([p for p in packages if p.suffix == ".nupkg" and not p.name.endswith(".symbols.nupkg")])
    snupkgs = sorted([p for p in packages if p.suffix == ".snupkg"])

    for pkg in nupkgs + snupkgs:
        print(f"Pushing: {pkg.name}")
        run(
            [
                "dotnet",
                "nuget",
                "push",
                str(pkg),
                "--api-key",
                api_key,
                "--source",
                NUGET_SOURCE,
                "--skip-duplicate",
            ],
            cwd=root,
            redact_values=[api_key],
        )


def git_commit_version_bump(root: Path, changed_files: List[Path], bump_kind: str) -> None:
    git_dir = root / ".git"
    if not git_dir.exists():
        raise RuntimeError("Expected a git repository (no .git directory found). Refusing to commit.")

    if not changed_files:
        print("No csproj files changed; skipping git commit.")
        return

    # Stage only the modified csproj files
    rel_paths = [str(p.relative_to(root)) for p in changed_files]
    run(["git", "add", "--"] + rel_paths, cwd=root)

    msg = f"[{bump_kind.upper()}] Analyzer Upgrade"
    run(["git", "commit", "-m", msg], cwd=root)


def main(argv: List[str]) -> int:
    if len(argv) != 3:
        eprint("Refusing to run.")
        eprint("Usage: python zscripts/publish.py <major|minor|patch|publish-only> <api_key>")
        return 2

    mode = argv[1].lower().strip()
    api_key = argv[2].strip()

    if mode not in ("major", "minor", "patch", "publish-only"):
        eprint("Refusing to run.")
        eprint("First argument must be one of: major, minor, patch, publish-only")
        return 2

    if not api_key:
        eprint("Refusing to run: api_key is empty.")
        return 2

    root = find_repo_root()
    sln = find_solution_file(root)

    print(f"Repo root: {root}")
    print(f"Solution:  {sln.name}")
    print(f"Mode:      {mode}")

    changed: List[Path] = []

    # 1) Version bump (optional)
    if mode != "publish-only":
        _, newv, changed = bump_versions_in_projects(root, mode)
        print(f"New version set to: {newv}")
    else:
        print("publish-only: leaving versions unchanged; no git operations will be performed.")

    # 2) Build solution
    build_solution(root, sln)

    # 3) Pack all packable projects under target dirs
    out_dir = clean_publish_output_dir(root)
    packages = pack_projects(root, out_dir)

    # 4) Push packages
    push_packages(root, packages, api_key)

    # 5) Git stage + commit (only for bump modes, after successful push)
    if mode != "publish-only":
        git_commit_version_bump(root, changed, mode)

    print("Done.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv))
    except subprocess.CalledProcessError as ex:
        eprint(f"Command failed with exit code {ex.returncode}.")
        raise
    except Exception as ex:
        eprint(f"Error: {ex}")
        raise
