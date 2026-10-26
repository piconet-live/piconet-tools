#!/usr/bin/env bash
# The plain check: the library and its tests built (warnings are errors) and the tests run. Needs the .NET 10 SDK.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
dotnet build piconet-tools.slnx -v quiet -nologo
dotnet test --project tests/Fzgx.GameCube.Tests --no-build
