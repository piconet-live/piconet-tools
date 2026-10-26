# Contributing to piconet-tools

This repository holds `Fzgx.GameCube`, the readers for the disc and the game's file formats.

## Building

The [README](README.md) lists the requirements, with versions, and how to build and check this repository. Clone it
with `--recurse-submodules`.

## Making a change

1. Fork the repository and make a branch for your change.
2. Keep a pull request to one change, with a message that says what it changes and why.
3. Open the pull request against `main`. `check.sh` must pass, and so must CI.

The file formats and messages the parts share are not kept here: a change to one goes to
[piconet-protocol](https://github.com/piconet-live/piconet-protocol) first.

Please discuss bigger changes on the [Discord](https://discord.gg/DcauFmsGh) before you start on them. Bugs and
small fixes can go straight to an issue or a pull request.

## Licence

piconet-tools is licensed under GPL-3.0-or-later. By opening a pull request you agree that your contribution is
licensed under the same terms.
