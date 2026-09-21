[English](README.md) | [日本語](README.ja.md)

# mutation-dotnet

mutation-dotnet is a fast mutation testing tool for C# / .NET projects. Its run time scales with the tests each mutant actually covers, not with the number of builds.

## Why mutation-dotnet

Mutation testing is usually slow because each mutant pays for a build and a test-host start. mutation-dotnet removes those costs:

- **Mutant schemata** — all mutants are compiled into a single Roslyn compilation and switched at run time, so there is one build instead of one per mutant.
- **Resident in-process test hosts** — worker processes keep the test framework loaded and run only the tests that cover the active mutant, with per-test coverage and fail-fast.
- **Semantic-aware generation** — mutations that cannot compile are excluded by type checks before emit, so compile-error mutants are avoided instead of rolled back one by one.
- **RelationalPatternMutator** — swaps relational pattern operators while preserving compilable switch coverage.
- **Snapshot inheritance** — verdicts from the previous run are inherited for unchanged mutants, and a fully unchanged input short-circuits the whole run.

Measured on a real 21-module project, a full run over 2,921 mutants finishes in 4 minutes 21 seconds with zero compile-error mutants. On an 18,000-mutant project, an unchanged re-run short-circuits through snapshot inheritance in 32 seconds.

## Requirements

- .NET SDK 10 (`devenv shell` in this repository provides it)
- A sibling checkout of [typemodeling-dotnet](../typemodeling-dotnet), referenced by relative `ProjectReference`
- Target projects: SDK-style .NET projects (verified on net8.0 and net10.0; .NET Framework is out of scope because the worker is a .NET 10 process)
- Test frameworks: xunit v2 (in-process front controller), TUnit / xunit v3 (Microsoft.Testing.Platform 1.x / 2.x), NUnit / MSTest (VSTest adapter run in process)

## Getting started

```bash
dotnet build Mutation.slnx
dotnet src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll run \
  --project path/to/Target.csproj \
  --test-project path/to/Target.Tests.csproj \
  --output .mutation-output
```

For a repository split into per-module projects, pass the pairs as comma-separated lists. The build runs once and the reports are merged.

```bash
dotnet src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll run \
  --project core/a/A.csproj,core/b/B.csproj \
  --test-project core/a/tests/A.Tests.csproj,core/b/tests/B.Tests.csproj \
  --output .mutation-output
```

A target that fails does not stop the others. Abandoned targets appear in the summary and in `timings.json`, and the exit code becomes 1.

## Options

| Option | Meaning |
|---|---|
| `--project PATHS` | Projects to mutate. `,` separates several |
| `--test-project PATHS` | Test projects. Same count and order as `--project` |
| `--concurrency N` | Number of workers. Defaults to half the logical cores |
| `--configuration NAME` | Build configuration. Defaults to Debug |
| `--mutate GLOBS` | Globs of files to mutate, relative to the project directory. `!` excludes, `,` separates |
| `--since REF` | Mutate only files changed since the given git ref |
| `--break-at SCORE` | Exit with code 2 when the mutation score is below this value |
| `--ignore-operators NAMES` | Exclude mutation operators (for example `LiteralMutator`). `,` separates |
| `--ignore-methods NAMES` | Do not mutate inside calls to these methods (for example `ConfigureAwait`). `,` separates |
| `--validate-survivors` | Re-verify surviving mutants in a fresh process |
| `--exclude-static` | Ignore mutants that only run during static initialization |
| `--with-baseline` | Inherit verdicts of unchanged mutants from the previous run |

## Reports

The run writes a console summary and two files under `<output>/reports/`:

- `mutation-report.json` — the mutation-testing report schema, viewable with mutation-testing-elements
- `timings.json` — per-phase and per-mutant timings for bottleneck analysis

## Development

`devenv shell verify` runs the build, all tests, the end-to-end checks, and a self-applied mutation gate: mutation-dotnet mutates its own engine and fails the verification when the score drops below the recorded floor. Branch, commit, and release conventions are described in [CONTRIBUTING.md](CONTRIBUTING.md), and released changes in [CHANGELOG.md](CHANGELOG.md).

## Limitations

- Linux is the measured platform. The code has no platform-specific assumptions, but Windows and macOS are not yet verified.
- Mutants that only execute during static initialization are attributed by runtime tracking; with `--exclude-static` they are reported as Ignored.

## License

MIT
