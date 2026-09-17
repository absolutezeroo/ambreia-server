"""Compile the actual purchase executor against test doubles, with no NuGet restore.

Requires a .NET 10 SDK with the ASP.NET Core reference pack; run from any directory.
This is a scoped audit reproduction, not an Orleans/SQL integration test.
"""
import json
import pathlib
import re
import shutil
import subprocess
import tempfile

here = pathlib.Path(__file__).resolve().parent
repo = here.parents[2]
dotnet = shutil.which("dotnet")
if not dotnet:
    raise SystemExit("A .NET 10 SDK must be on PATH")
sdk_lines = subprocess.check_output([dotnet, "--list-sdks"], text=True).splitlines()
sdks = [re.fullmatch(r"(10\.\d+\.\d+) \[(.+)\]", line) for line in sdk_lines]
sdks = [m for m in sdks if m]
if not sdks:
    raise SystemExit("No .NET 10 SDK found")
sdk = max(sdks, key=lambda m: tuple(map(int, m[1].split('.'))))
sdk_root = pathlib.Path(sdk[2]) / sdk[1]
runtime_root = pathlib.Path(dotnet).resolve().parent
refs = {}
for pack in ("Microsoft.NETCore.App.Ref", "Microsoft.AspNetCore.App.Ref"):
    versions = sorted((runtime_root / "packs" / pack).glob("10.*"),
                      key=lambda p: tuple(map(int, p.name.split('.'))))
    if not versions:
        raise SystemExit("Missing reference pack: " + pack)
    for dll in (versions[-1] / "ref" / "net10.0").glob("*.dll"):
        refs[dll.name] = dll
with tempfile.TemporaryDirectory(prefix="vortex-wallet-audit-") as tmp:
    target = pathlib.Path(tmp) / "probe.dll"
    command = [dotnet, str(sdk_root / "Roslyn" / "bincore" / "csc.dll"),
               "-nologo", "-target:exe", "-nullable:enable", "-langversion:latest",
               "-nostdlib+", "-out:" + str(target)]
    command += ["-r:" + str(dll) for dll in refs.values()]
    command += [str(here / "wallet_purchase_probe.cs"),
                str(repo / "Vortex.Primitives/Players/Wallet/WalletPurchaseExtensions.cs")]
    subprocess.run(command, check=True)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps({"runtimeOptions": {
        "tfm": "net10.0", "frameworks": [
            {"name": "Microsoft.NETCore.App", "version": "10.0.0"},
            {"name": "Microsoft.AspNetCore.App", "version": "10.0.0"}]}}))
    subprocess.run([dotnet, str(target)], check=True)
