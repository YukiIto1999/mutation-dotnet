[English](README.md) | [日本語](README.ja.md)

# mutation-dotnet

mutation-dotnet is a mutation testing tool for C# / .NET projects. It combines mutant schemata, coverage-based test selection, and resident test hosts to reduce repeated compilation and test-host startup.

## Why mutation-dotnet

Mutation testing can spend time compiling mutants, starting test hosts, and running unrelated tests. mutation-dotnet uses the following mechanisms to reduce repeated work:

- **Mutant schemata** — generated mutants share a Roslyn compilation and are switched at run time, avoiding a full project build per mutant. Build preparation and rollback can still require additional compilation.
- **Resident in-process test hosts** — worker processes keep the test framework loaded and run only the tests that cover the active mutant, with per-test coverage. xunit v2, TUnit, and xunit v3 stop at the first failing test.
- **Semantic-aware generation** — operator-specific type checks reject some invalid candidates before emit. Invalid candidates identified during emit are rolled back and reported as CompileError; an unrecoverable compilation fails the target.
- **RelationalPatternMutator** — swaps relational pattern operators while preserving compilable switch coverage.
- **Snapshot inheritance** — previous verdicts are reused only when the whole input closure matches: the compiler arguments, every source, the referenced assemblies, analyzers and generators, additional files, analyzer config, resources, the test directory, the engine and compiler versions, and the settings. A matching fingerprint and matching test and mutated assembly hashes can skip generation, baseline and mutant execution after build and target preparation; any other difference re-judges the mutants. [docs/limitations.md](docs/limitations.md) lists what the closure leaves out.

Measured in August 2026 on earlier revisions, a full run over 2,921 mutants of a real 21-module project finished in 4 minutes 21 seconds with zero compile-error mutants, and an unchanged re-run of an 18,000-mutant project short-circuited through snapshot inheritance in 32 seconds. These figures were not re-measured on the current revision. [docs/performance.md](docs/performance.md) describes how to measure.

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

For a repository split into per-module projects, pass the pairs as comma-separated lists. The targets share one build and the reports are merged.

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
| `--since REF` | Select candidates from what changed since the given git ref (working tree against `REF`, plus untracked files, deletions and moves). A change that reaches the target selects the whole production scope of that target; a change that reaches nothing selects nothing. See [Selecting by change](#selecting-by-change) |
| `--changed-lines` | With `--since REF`, an explicit subset: generate only mutants whose reported line range intersects changed lines of changed production files, and record every other relation the change reaches as unanalysed. It is not an impact analysis. A diff failure fails the run; zero generated mutants pass a per-change run |
| `--break-at SCORE` | Exit with code 2 when the mutation score is below this value |
| `--ignore-operators NAMES` | Exclude mutation operators (for example `LiteralMutator`). `,` separates |
| `--ignore-methods NAMES` | Do not mutate inside calls to these methods (for example `ConfigureAwait`). `,` separates |
| `--validate-survivors` | Re-verify surviving mutants in a fresh process |
| `--exclude-static` | Ignore mutants in a static context, or that run outside a test or during static initialization, even when ordinary tests cover them. They are reported as Ignored |
| `--with-baseline` | Reuse settled verdicts from the same output directory and run kind only when captured inputs and settings match. The current `CompileError`, `NoCoverage` and `Ignored` classification always wins. Interrupted runs retain settled verdicts and test the remaining mutants on resumption |
| `--detection-matrix` | Do not stop at the first failing test: run every covering test of each mutant and record every failing test in the report. Verdicts are unchanged; previous verdicts are not inherited, so `--with-baseline` has no effect |

Reuse assumes that tests depend only on the captured compilation, test output and execution environment, with installed runtimes held fixed. Copy runtime fixture files into test output; omit `--with-baseline` for tests that read uncaptured external files, network state or time. Environment variables and the working directory participate in the identity as a hash, not as stored plaintext. Worker assets and test data, including files under `TestResults`, participate; only the engine-owned work directory is excluded. If that exclusion would hide the test assembly, reuse is disabled.

## Reports

The run writes a console summary and two files under `<output>/reports/`:

- `mutation-report.json` — the mutation-testing report schema, viewable with mutation-testing-elements
- `timings.json` — per-phase and per-mutant timings for bottleneck analysis

With `--detection-matrix`, each executed mutant in `mutation-report.json` lists every failing test in `killedBy`, the tests it ran in `coveredBy`, and the number of tests whose result was observed in `testsCompleted`. A mutant whose run lost some results, for example after a timeout or a crash, is incomplete: `testsCompleted` is smaller than `coveredBy`, and `statusReason` names the tests that did not run. Tests are identified by their display names. Microsoft.Testing.Platform runs each test as a separate invocation in this mode.

Both files carry a `run` object that tells whether the run settled every mutant. `state` is `completed` when every target reached judgement and every mutant has a verdict, and `interrupted`, `failed` or `unjudged` otherwise (in that order of precedence): `interrupted` after a cancellation, `failed` when a target did not reach judgement for another reason (build, target location, generation or baseline failure), `unjudged` when some mutants have no verdict. `complete` is true only for `completed`; `unjudged` counts the mutants without a verdict and `unjudgedBy` splits them by cause (`notRun`, `interrupted`, `contendedTimeout`, `runnerFailure`); `interruptedTargets` names the targets a cancellation stopped and `failedTargets` the targets that failed before judgement. A mutant without a verdict has the status `Pending` and a `statusReason`. It counts neither as detected nor as undetected, so it stays out of the mutation score.

A build failure also replaces the previous report with a failed run; an earlier successful report is not left as the result of the new invocation.

A test runner failure is such a cause. When the worker cannot open its test host (shared or isolated), answers a run with a failure or with an outcome other than `killed`, `survived` or `hitlimit`, or gives no answer without being stopped for a deadline or cancellation, the mutant is `Pending` with `unjudgedBy.runnerFailure`, and no test is recorded as having detected it. Only a failing test, a probe-count or deadline `Timeout` (after the control run above), and a worker process that dies while the mutant runs are `Killed` or `Timeout`. `run` prints the number of runner failures and exits with 3.

For a `--since` run, `run.selection` has one entry per target: its `scope` (`full-production`, `changed-lines` or `no-impact`), the `reasons` (relation and files) that decided it, and the `unanalysed` relations an explicit `--changed-lines` subset left out.
See [Selecting by change](#selecting-by-change).

## Stopping a run

`run` handles `SIGINT`, `SIGTERM` and `SIGHUP`. The first signal cancels the run: the running workers are stopped, no further mutant is started, and the verdicts settled so far are written to `mutation-report.json` and `timings.json` with `run.state` set to `interrupted`. The mutant that was being tested when the signal arrived and the mutants that were never started are `Pending` (`statusReason` tells which), and a target that was never reached is listed in `run.interruptedTargets`. A second signal ends the process at once. The build and the mutant generation run to their end before a cancellation takes effect.

Each verdict is also appended to `<output>/work/<target>/snapshot.verdicts.jsonl` as it is settled, so a run killed with `SIGKILL`, which cannot be handled, keeps what it had settled. To continue, run the same command with `--with-baseline`: the settled verdicts are inherited and only the `Pending` mutants are tested. A `--detection-matrix` run does not inherit, so it cannot continue this way.

| Exit code of `run` | Meaning |
|---|---|
| 0 | Every mutant has a verdict, no target failed, and the score meets `--break-at` |
| 1 | A target failed, or the arguments or the build are wrong |
| 2 | The mutation score is below `--break-at` |
| 129, 130, 143 | The run was stopped by `SIGHUP`, `SIGINT` or `SIGTERM` (128 plus the signal number) and wrote a partial report |
| 3 | The run finished, but some mutants have no verdict (`run.state` is `unjudged`) |

A mutant whose tests exceed their time budget is not counted as detected at once. The budget is fixed from the baseline durations (twice the covering tests plus two seconds, and five seconds of grace), so a machine busier than the baseline also exceeds it. After an expiry the engine runs the same tests without the mutant in a fresh process: when that control run does not fit the budget either, the mutant is `Pending` with `unjudgedBy.contendedTimeout` instead of `Timeout`. When it fits, the mutant runs once more in a fresh process; its verdict is taken if it finishes, and only a second expiry is a `Timeout`. A probe-count limit is independent of time and stays a `Timeout`. Stryker.NET, StrykerJS and PIT keep the fixed deadline and leave a busy machine to a larger constant tuned by hand.

## Selecting by change

With `--since REF`, the engine classifies every changed file against the compiler inputs of the target and the paired test project. Known relations include production sources, tests, referenced projects and build inputs. A file outside those inputs is not proof of no impact: an external import or runtime fixture can still affect the tests. Unknown files and deletions therefore select the whole target, including unknown files under `bin` or `obj`.

| What changed | Selection |
|---|---|
| No changes | `no-impact`: no candidates |
| A production source, test, referenced project, build input, deleted file or file of unknown relation | `full-production`: every production file of the target that `--mutate` allows. Unchanged callers and unknown input relations cannot justify a finer scope. `run.selection[].reasons` lists the relations and files |
| The same changes with `--changed-lines` | `changed-lines`: only mutants on changed lines of changed production files, as an explicit subset. Tests, build inputs, referenced projects, deletions and unchanged callers are recorded under `run.selection[].unanalysed`, the console summary prints them, and `changed-lines` prints them after its verdict |

Selection is reported per target. An unknown file can broaden every supplied target, including when it looks like documentation. Project references come from MSBuild's evaluated `ProjectReference` graph through StructuredLogger, including transitive references, rather than assembly names or output-directory guesses. Selection participates in whole-result reuse identity. [docs/limitations.md](docs/limitations.md) lists what the classification does not see.

## Changed-line gate

`run --since REF --changed-lines` generates only mutants whose reported line range overlaps a changed line. `changed-lines` judges that report against the same `REF`: changes include the new side of the working-tree diff, all lines of untracked files, and moved or renamed files treated as deleted and added.

Changed-line mutants with `Survived` or `NoCoverage` make the gate exit 2. With none undetected, a changed-line `Pending` mutant or an explicitly `failed` or `interrupted` run makes it exit 1, even when no mutant was generated. Unknown run states are unreadable; legacy reports without `run` retain their previous interpretation.

A completed per-change run with zero generated mutants passes only for that subset; a whole-project run with zero generated mutants fails the full verification gate. Omitted impact is printed on both normal verdicts and judgement failures, so the result does not imply that unanalysed relations were covered.

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
| 1 | The report or diff is unreadable, or no changed-line mutant is undetected but a mutant is `Pending` or the reported run failed or was interrupted |
| 2 | At least one mutant on a changed line is undetected. Each is printed as `path:line:column status mutator: replacement` |

## Development

Verification runs through devenv entries, split by time budget:

- `devenv shell verify` (within the 120-second budget declared in [tiergate.json](tiergate.json), before every commit) builds the solution with every analyzer warning as an error, including cognitive complexity (SonarAnalyzer S3776, threshold 15), and runs the unit tests.
- `devenv shell verify-push` (within 15 minutes, before every push) runs `verify` and the end-to-end checks on the fixtures (verdict categories, the three test frameworks, the changed-line gate, the detection matrix, a run stopped by `SIGTERM` and `SIGKILL`, snapshot reuse and its invalidation, and selection by change). It runs no mutation testing on mutation-dotnet's own engine.
- `devenv shell verify-full` (not a gate, before every release) runs `regression-full` and then `mutation-select` as two separate runs, each stopped at its own limit declared in [tiergate.json](tiergate.json), and exits with the status of `regression-full`.
- `devenv shell regression-full` (within 15 minutes) runs `verify` and the end-to-end checks without narrowing them to a change, keeps its exit status and failed stages under `.mutation-output/regression-full/`, and appends failed stages that the previous run did not report to `work/backlog.md`, an ignored working ledger. A stage stopped at the limit is recorded as incomplete, with exit status 124, not as a failure.
- `devenv shell mutation-select` (within 30 minutes) analyses the engine with `run --since <base> --changed-lines --with-baseline`, where the base is the commit of the last completed analysis (without a record, the merge-base with the upstream; with neither, the HEAD at introduction, not the merge-base with the default branch), and judges the report with `changed-lines --since <base>`, keeping its raw exit status (2 when a changed-line mutant is undetected) apart from a failed or stopped analysis. A run that uses the HEAD at introduction records the source before that HEAD as out of scope of the selected analysis in `.mutation-output/select/selection.json`; a question that needs it is answered with `mutation-full`. It records the base, the changed files as a reference list, the mutated files, and the unanalysed scope (unchanged lines of changed files, dependencies and dependents of the mutated files, and the effects of other changed inputs, pending [#1](https://github.com/YukiIto1999/mutation-dotnet/issues/1)) with its raw exit status under `.mutation-output/select/`, and keeps every analysis under `.mutation-output/select/runs/` with the raw report and an `undetected.json` that marks each undetected mutant reported by `changed-lines` and leaves cause and severity fields to fill in. It reports a diff failure, no change, zero mutants, and a run stopped at the limit separately. It is not a whole-engine analysis and never changes the regression result.
- `devenv shell mutation-full` (no budget) analyses the whole engine with mutation testing when that question needs answering, keeps its raw exit status in `.mutation-output/full/exit-status`, and appends failures and undetected mutants that the previous analysis did not report to `work/backlog.md`. `verify-full` does not run it.

Branch, commit, and release conventions are described in [CONTRIBUTING.md](CONTRIBUTING.md), and released changes in [CHANGELOG.md](CHANGELOG.md).

## Documentation

[docs/](docs/README.md) holds the tracked documents for maintainers: architecture, limitations, the performance measurement procedure, and deviations from the architecture standard. They are written in Japanese and project the current source and tests.

`work/` holds ignored working records such as plans, research, experiments, drafts, and the `verify-full` backlog. It exists only in a checkout that created it, and no tracked document depends on its contents.

## Limitations

- Linux is the measured platform; Windows and macOS are not yet verified.
- Mutants in a static context, or that execute outside a test or during static initialization, may require isolation. When ordinary covering tests exist, the shared worker runs them first; only survivors are checked again in a fresh process. `--exclude-static` reports these mutants as Ignored instead.
- More limits, each with its source location, are in [docs/limitations.md](docs/limitations.md).

## Architecture standard

The architecture standard is at `/home/nixos/environment/architecture-standard`.
Conformance is always judged against the current text of the standard.
Application follows the four rules of application and the usage procedures in the standard's README.
Decisions are recorded where they live: how in the code, what in the tests, why in commit subjects, and why-not in implementation comments next to the code that owns the decision.
Deviations from the standard are in [docs/exceptions.md](docs/exceptions.md).

## License

MIT
