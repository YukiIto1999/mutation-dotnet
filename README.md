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
| `--since REF` | Mutate only files whose content changed since the given git ref (working tree against `REF`, plus untracked files). A file moved or renamed since `REF` counts as added |
| `--changed-lines` | With `--since REF`, generate only mutants whose reported line range intersects changed lines. A diff failure fails the run; zero generated mutants pass a per-change run |
| `--break-at SCORE` | Exit with code 2 when the mutation score is below this value |
| `--ignore-operators NAMES` | Exclude mutation operators (for example `LiteralMutator`). `,` separates |
| `--ignore-methods NAMES` | Do not mutate inside calls to these methods (for example `ConfigureAwait`). `,` separates |
| `--validate-survivors` | Re-verify surviving mutants in a fresh process |
| `--exclude-static` | Ignore mutants that only run during static initialization |
| `--with-baseline` | Inherit verdicts of unchanged mutants from the previous run in the same output directory of the same kind: a whole run inherits from a whole run, and a `--since` run from a `--since` run |

## Reports

The run writes a console summary and two files under `<output>/reports/`:

- `mutation-report.json` — the mutation-testing report schema, viewable with mutation-testing-elements
- `timings.json` — per-phase and per-mutant timings for bottleneck analysis

## Changed-line gate

`run --since REF` narrows mutation to whole files. Add `--changed-lines` to generate only mutants whose reported line range overlaps a line changed since `REF`. `changed-lines` still judges the report of that run, exiting nonzero if any changed-line mutant is undetected. Changed lines are the new side of the working tree against `REF`, every line of an untracked file, and every line of a file moved or renamed since `REF`, which counts as deleted and added. A mutant counts as undetected when its status is `Survived` or `NoCoverage`; mutants outside the changed lines never affect the result. A per-change run with zero generated mutants succeeds; a whole-project run with zero generated mutants fails the full verification gate.

For a pre-push check, pass the point where the pushed range starts, for example the merge base with the upstream branch:

```bash
base=$(git merge-base HEAD '@{upstream}' 2>/dev/null || git merge-base HEAD origin/develop)
dotnet src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll run \
  --project path/to/Target.csproj \
  --test-project path/to/Target.Tests.csproj \
  --since "$base" --changed-lines --output .mutation-output/push
dotnet src/Mutation.Cli/bin/Debug/net10.0/Mutation.Cli.dll changed-lines \
  --report .mutation-output/push/reports/mutation-report.json --since "$base"
```

| Exit code | Meaning |
|---|---|
| 0 | No mutant on a changed line is undetected, including when no mutant lies on a changed line |
| 1 | The report cannot be read, or the diff against `REF` cannot be resolved |
| 2 | At least one mutant on a changed line is undetected. Each is printed as `path:line:column status mutator: replacement` |

## Development

Verification runs through three devenv entries, split by time budget:

- `devenv shell verify` (within 2 minutes, before every commit) builds the solution with every analyzer warning as an error, including cognitive complexity (SonarAnalyzer S3776, threshold 15), and runs the unit tests.
- `devenv shell verify-push` (within 15 minutes, before every push) runs `verify`, the end-to-end checks on the fixtures (verdict categories, the three test frameworks, the changed-line gate), and the changed-line gate on mutation-dotnet's own engine against the push base. One surviving or uncovered mutant on a changed line fails it.
- `devenv shell verify-full` (no budget, not a gate, before every release) runs the end-to-end checks and mutation testing over the whole engine, and appends failures and undetected mutants that the previous full run did not report to the backlog.

Branch, commit, and release conventions are described in [CONTRIBUTING.md](CONTRIBUTING.md), and released changes in [CHANGELOG.md](CHANGELOG.md).

## Limitations

- Linux is the measured platform. The code has no platform-specific assumptions, but Windows and macOS are not yet verified.
- Mutants that only execute during static initialization are attributed by runtime tracking; with `--exclude-static` they are reported as Ignored.

## Architecture standard

The architecture standard is at `/home/nixos/environment/architecture-standard`.
Conformance is always judged against the current text of the standard.
Application follows the four rules of application and the usage procedures in the standard's README.
Decision records are kept in `docs/decisions/`, outside version control.

## License

MIT
