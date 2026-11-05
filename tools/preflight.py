#!/usr/bin/env python3
"""Pre-flight checks on release/TeamPlus.zip before it goes anywhere.

    python3 tools/preflight.py

Everything here is a mistake we could actually make: a meta.json one directory
too deep (FortRise skips the mod silently), a stale Content/ folder left over
from the pre-runtime-recolour builds, a hard dependency on WiderSet that would
refuse to install for people without it, or an update id pointing at somebody
else's GameBanana page.
"""
import hashlib
import json
import pathlib
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
ZIP = ROOT / "release" / "TeamPlus.zip"
BUILT = ROOT / "bin" / "Release" / "net10.0" / "TeamPlus.dll"
KIT = ROOT / "release" / "gamebanana" / "TeamPlus.zip"

failures = []


def check(label, ok, detail=""):
    print(f"  {'PASS' if ok else 'FAIL'}  {label}{f' — {detail}' if detail else ''}")
    if not ok:
        failures.append(label)


def sha(path):
    return hashlib.sha1(path.read_bytes()).hexdigest()


if not ZIP.exists():
    sys.exit(f"no zip at {ZIP} — run ./build.sh Release first")

with zipfile.ZipFile(ZIP) as z:
    names = z.namelist()
    files = [n for n in names if not n.endswith("/")]
    meta = json.loads(z.read("meta.json")) if "meta.json" in names else {}
    zip_dll = z.read("TeamPlus.dll") if "TeamPlus.dll" in names else b""

print(f"{ZIP.relative_to(ROOT)} — {ZIP.stat().st_size // 1024} KB, {len(files)} files")

check("meta.json sits at the zip root", "meta.json" in names)
check("the dll meta.json declares is present", meta.get("dll") in files,
      f"declares {meta.get('dll')!r}")
check("mod name is alphanumeric", str(meta.get("name", "")).isalnum(),
      str(meta.get("name")))
check("version matches the csproj",
      f"<ModVersion>{meta.get('version')}</ModVersion>"
      in (ROOT / "TeamPlus.csproj").read_text(), str(meta.get("version")))
check("FortRise is a hard dependency",
      any(d.get("name") == "FortRise" for d in meta.get("dependencies", [])))
check("no WiderSet dependency (an optional one loads Team+ after TF.EX: TEAM+ unreachable, CHANGELOG 0.1.2)",
      all(d.get("name") != "Teuria.WiderSet"
          for d in meta.get("dependencies", []) + meta.get("optionalDependencies", [])))
check("no update.gamebanana.id yet",
      "gamebanana" not in json.dumps(meta.get("update", {})),
      "add it once the page exists")
check("mode icon shipped", "Content/gameModes/threeteam.png" in files)
check("gold menu banner shipped", "Content/menu/teamC2x.png" in files)
check("readme shipped", "INSTALL.txt" in files)
check("no pre-baked sheet folders",
      not any(n.startswith(("Content/yellow", "Content/white")) for n in names))
check("no dotfile junk",
      not any(pathlib.PurePosixPath(n).name.startswith(".") for n in files))
check("the zipped dll matches the built Release dll",
      BUILT.exists() and hashlib.sha1(zip_dll).hexdigest() == sha(BUILT))
check("the submission kit's copy is byte-identical",
      KIT.exists() and sha(KIT) == sha(ZIP))

print()
if failures:
    sys.exit(f"{len(failures)} check(s) failed: " + "; ".join(failures))
print("all clear")
