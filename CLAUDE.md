# CLAUDE.md

Project-wide coding guidance for this repository lives in @AGENTS.md — follow it for all code generation.

## Docs (reference)
- Overview: [README.md](README.md) (English) · [README.ko.md](README.ko.md) (Korean)
- The cage's declaration — one table row per code declaration; the developer edits the table, the agent matches the code to it: [docs/cage.md](docs/cage.md)
- The vertex mapping method (why MVC): [docs/cage-deformation-plan.md](docs/cage-deformation-plan.md)
- Session journal: [docs/journal.md](docs/journal.md)
- The live demo: [unity/Assets/demo/](unity/Assets/demo/) — its bake (`cage_bake.bytes`) is written by the mapping tester's **export demo bake** or `tools/cage_sweep -- --bake`; the web build by **Demo ▸ Build Web** into `docs/unity/`
