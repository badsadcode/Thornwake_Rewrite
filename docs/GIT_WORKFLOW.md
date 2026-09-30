# GitHub setup and review workflow

This repository contains the existing Thornwake Godot 4.5.1 / C# project, its authored assets, tests, frozen world-generation fixtures and design/checkpoint documents. Git setup originally preserved Algorithm 3 as default and Algorithm 4 as an opt-in experiment. Algorithm 4 was subsequently formally accepted and promoted for new macro worlds; Algorithm 3 remains reconstructible. See [the current acceptance checkpoint](MASTER_A4_HYDROLOGY_ACCEPTANCE.md).

## Developing from a clone

Keep the existing `README.md` for gameplay and project information. On Windows, install the .NET 9 SDK (the core test executable targets net9.0; the Godot project targets net8.0). Run the existing `tools/bootstrap.ps1` to restore the locally downloaded Godot 4.5.1 .NET editor and export templates into `.tools/`. This is also required by the local Godot package source in `NuGet.Config` and the test launchers. Then run:

```powershell
dotnet restore Thornwake.csproj
dotnet build Thornwake.csproj --no-restore
dotnet run --project Tests/Thornwake.Core.Tests.csproj
```

The existing `tools/build.ps1` bootstraps the engine, runs tests, builds and exports the Windows package. Engine downloads, Godot import caches, build output and local preview evidence are recreated locally; they are not part of Git history. Some optional art/preview helper scripts retain pre-existing machine-specific paths; they were not rewritten during repository setup.

## Tracked and local-only files

Track source, scenes, resources, shaders, content definitions, design documents, tests and frozen fixtures, authored textures/models/Blender assets, licenses, project configuration, Godot `.uid` files and source `.import` settings. The README is preserved.

Exclude `.godot/`, `.tools/`, `bin/`, `obj/`, `dist/`, `artifacts/`, manual `backups/`, the root backup/source-review archives, IDE/OS caches, logs, temporary files and credentials. Files excluded from Git remain on disk. Historical documentation links into `artifacts/` refer to local generated evidence and will not resolve on GitHub; the written findings and checkpoints are tracked.

No Git LFS filters are required for the initial repository: all retained authored binary assets are small. Reassess LFS before adding large or frequently revised binary assets. Do not blanket-track images/models in LFS. `.gitattributes` preserves existing file bytes without automatic line-ending conversion.

## Future work and review

The owner has authorized ordinary commits and pushes to `main` after coherent, stable Thornwake milestones. Review the diff and staged files, exclude generated/secret data, run appropriate checks, use a descriptive commit message and include the resulting hash in the completion report. For major work, report the previous and new commit hashes so a reviewer can inspect the exact diff.

Do not force-push, overwrite remote history, silently resolve meaningful conflicts, publish credentials or commit clearly experimental work to `main` without an appropriate branch/decision. Follow any later instruction not to commit or push. The initial repository preserves the existing opt-in mountain experiment as part of the requested current-state snapshot; that is not design acceptance or default promotion.

After GitHub access is authorized for the reviewing account/integration, provide repository, branch and commit to Noor. Initial review:

```text
Noor, review the Thornwake GitHub repository.
Repository: badsadcode/Thornwake
Branch: main
Current commit: <full commit hash>

Please inspect the actual repository and review the current code/project architecture.
```

Later milestone review:

```text
Noor, Codex just finished work on Thornwake and pushed it.

Repository: badsadcode/Thornwake
Branch: main
Commit: <latest commit hash>

Please review what changed in this commit and check it against the Thornwake design documents.
```
