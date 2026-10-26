# piconet-tools

[![CI](https://github.com/piconet-live/piconet-tools/actions/workflows/ci.yml/badge.svg)](https://github.com/piconet-live/piconet-tools/actions/workflows/ci.yml)

`Fzgx.GameCube`: .NET readers for GameCube disc images and the Amusement Vision file formats F-Zero GX uses (the
disc's file system, LZ compression, GMA models, TPL textures and the GX texture formats, PNG output). The
[piconet-launcher](https://github.com/piconet-live/piconet-launcher) builds it from source as its submodule
`external/tools`, to read and prepare your own disc. Piconet is online multiplayer for F-Zero GX: see
[piconet](https://github.com/piconet-live/piconet).

No game data is stored here: the tests build their discs, models and textures in code.

| Folder or file | What it is |
|---|---|
| `src/Fzgx.GameCube/` | The library (ahead-of-time compatible, no package dependencies) |
| `tests/Fzgx.GameCube.Tests/` | Its tests (xunit.v3 on Microsoft.Testing.Platform) |
| `piconet-tools.slnx` | The solution |
| `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.editorconfig`, `tests/.editorconfig` | The shared settings (warnings are errors), the package versions, the SDK, the code style |
| `check.sh` | The plain check: build and tests |

## Requirements

- the .NET SDK 10.0.401 or a later 10.0 patch (`global.json`)
- nuget.org, for xunit.v3 (the tests only)

## Build and test

    ./check.sh
    # or by hand:
    dotnet build piconet-tools.slnx
    dotnet test --project tests/Fzgx.GameCube.Tests

## Contributing

Fork the repository, make a branch, and open a pull request; `check.sh` must pass. Please discuss bigger changes on
Piconet's [Discord](https://discord.gg/DcauFmsGh) first. The details are in [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

GPL-3.0-or-later (`LICENSE`). The LZ decoder (`Compression/AvLz.cs`) is ported from
RaphaelTetreault/GameCube.AmusementVision and the GMA reader's layout (`Models/GmaReader.cs`) comes from
bobjrsenior/GxUtils, both GPL-3.0; their headers name them.
