# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed

- `mutation-select` no longer falls back to the merge-base with the default branch when it has neither a record of a completed analysis nor an upstream. It uses the HEAD at that moment as the base and records it in `.mutation-output/select/analyzed-commit`, so later runs measure from that commit even if the first run does not finish. Going back to the default branch made the first run analyse every change since the branch point instead of the changes since the previous analysis. `selection.json` states in `baseSource` and in the first entry of `unanalyzed` that source before that HEAD is out of scope of the selected analysis; a question that needs it is answered with `mutation-full`. A base that could not be decided no longer fails the run, because HEAD is always the last choice; `diff-failed` remains for a diff that cannot be read.
- Breaking for scripts that call `run --since REF` without `--changed-lines`: it used to mutate only the files whose content changed. It now selects the whole production scope of every target that a change reaches (a production source, the paired tests, a referenced project, a build input, a deletion, or an unknown input anywhere in the repository) and nothing for a target that no change reaches, so such a run mutates more files and takes longer. Use `--changed-lines` for the explicit subset of changed lines of changed production files, which does not widen and records the relations it leaves unanalysed under `run.selection[].unanalysed`, or `--mutate` globs to narrow the scope by hand. `run.selection` and the console line that starts with `選別` report the scope that was chosen.
- Breaking library API: generation and judgement now belong to one core context, with `Mutation.Domain`, `Mutation.Application` and `Mutation.Infrastructure` namespaces instead of two coupled contexts and shared business types.
  `BuildCore.Create()` returns `MutationApi`; call `Run.ExecuteAsync(RunRequest, ...)` or `ChangedLines.Execute(...)`.
  The forwarding `MutationTesting` facade, flat `MutationRunOptions`, duplicate target/selection carriers and caller-supplied fingerprint string are removed.
  `PreparedTarget` owns its generation input; preparing the same project again no longer overwrites an earlier target's selection.

### Fixed

- A test runner failure is no longer counted as a detection. A worker `Failed` response to a run, a run outcome other than `killed`, `survived` or `hitlimit` (which used to count as a survival), a shared or isolated worker that cannot open its test host, and a missing response that is neither a deadline expiry nor a cancellation used to become `Survived`, `TimedOut` or `Killed`, which the mutation score counted as detected or undetected. They are now `Pending` mutants with `unjudgedBy.runnerFailure` and a `statusReason` that carries the worker's reason; they stay out of the score, `run` prints their count and exits with 3 (`run.state` is `unjudged`), and a changed-line mutant in that state keeps `changed-lines` from passing. A failing test, a probe-count or confirmed deadline `Timeout`, and a worker process that dies while the mutant runs are still `Killed` or `Timeout`, and a cancellation still discards the result of the mutant being tested. Library callers see the new `UnjudgedCause.RunnerFailure` case, `TargetResult.RunnerFailureCount` and `MutationRunResult.RunnerFailureCount`.
- A run in which a target failed before judgement (build, target location, generation or baseline failure) is no longer reported as complete. `run.state` is the new value `failed`, `run.complete` is false, and `run.failedTargets` names the targets, even when no mutant is `Pending`; a cancellation still takes precedence (`interrupted`). The exit status was already 1. Scripts that read `run.state` must accept `failed`. Library callers see `MutationRunResult.FailedTargets`.
- Both whole-result reconstruction and per-mutant inheritance require the same `ReuseIdentity`: compiler inputs, referenced assemblies, generators, resources, test runtime assets, dotnet host, the complete worker deployment, engine/compiler assemblies, runtime/environment description and structured settings.
  Worker runtimeconfig, deps and dependency files are included, as are test files under `TestResults`.
  Only the engine-owned work directory is excluded; if it contains the test assembly, reuse is disabled.
  The host is captured once per run and used for actual worker launches; common identity inputs are refreshed for every run rather than cached for the process lifetime.
  JSON preserves settings-array element boundaries, avoiding collisions between one comma-containing item and two items.
  Missing inputs disable both reuse paths. Snapshots with unsettled mutants keep the closure identity, but no whole-result fingerprint.
  Library ports use `IFingerprints.Capture` and `Compute`; prepared targets, verification and snapshot requests carry the paired `ReuseIdentity`, while persisted snapshots retain `ClosureIdentity`.
- Reuse identity also includes the process environment, working directory, runtime identifier and OS version. Environment names and values are hashed, not stored as plaintext. Tests depending on uncaptured external files, network state or time must run without `--with-baseline`.
- The current `CompileError`, `NoCoverage` and `Ignored` classification now wins over an inherited verdict. An inherited verdict could overwrite it, and the inherited count included mutants that were not run.
- `--since` no longer treats a diff without production sources as no change.
  Tests, build inputs, referenced projects, deletions and unknown input relations select the target's whole production scope.
  Files outside known compiler inputs are not assumed unrelated: external imports and runtime fixtures may live there.
  Unknown files under `bin` or `obj` are treated the same way; directory names do not prove absence of runtime inputs.
  Unchanged callers cannot be selected at a finer grain, so a production change also selects the whole target.
- Project dependencies come from StructuredLogger's evaluated MSBuild `ProjectReferenceGraph`, including transitive references, rather than assembly names or guessed output layouts. Referenced projects whose compilation was skipped are recovered by a `--no-incremental` build. Library callers see `TargetGraph.UnbuiltProjects`, `LocatedTarget` as the result of `ILocateTargets.Locate` and `PreparedProject.Graph`; binlog parsing is now internal to the target-location implementation.
- Untracked files are collected from the repository root rather than the production project's directory, so new tests, shared inputs and runtime fixtures outside that directory participate in `--since` selection.
- A global build failure replaces stale successful reports with `failed`, `complete: false` and all failed target names before returning the original failure; cancellation still takes precedence.
- The changed-line gate refuses explicitly failed or interrupted runs even when no mutant was generated, rejects unknown run states, and retains omitted-impact diagnostics on failure.
- Direct apphost launches resolve a shared dotnet host for both MSBuild and workers rather than recursively launching the CLI.
  The existing verdict-category E2E now starts the apphost with `DOTNET_HOST_PATH` unset.
- Snapshot-reuse regression cases now change compiler settings and the process environment separately, so either invalidation path must work independently.

### Added

- `run.selection` in `mutation-report.json` and `timings.json` records, per target of a `--since` run, the scope (`full-production`, `changed-lines`, `no-impact`), the relations that decided it, and the relations an explicit `--changed-lines` subset left unanalysed.
  The console summary prints one line per target, and `changed-lines` prints the unanalysed relations after its verdict, so a per-change pass is not read as an analysis of the affected scope.
  `ChangedLines.Deleted`, `ChangedLineVerdicts.Unanalysed` and `RecordedReport.Unanalysed` carry the new information to library callers.
- `tests/e2e/medium/reuse/verify-reuse.sh` and `tests/e2e/medium/impact/verify-impact.sh`, part of `verify-push` and `regression-full`, exercise reuse invalidation by environment, build settings, another source, tests and execution options, plus test-only, configuration, deleted-file and unknown-input selection with and without `--changed-lines`.

## [0.5.0] - 2026-11-16

### Added

- Versioned maintainer documentation under `docs/`, describing the current architecture, limitations and performance measurement procedure. Plans, investigations and experiment results belong in the ignored `work/` directory instead.
- `run --detection-matrix` runs every covering test of each mutant instead of stopping at the first failing test, and records the detection matrix in `mutation-report.json`: every failing test in `killedBy`, the tests the mutant ran in `coveredBy`, and the number of observed results in `testsCompleted`. A mutant covered both by ordinary tests and by static or ambient execution also runs its isolated tests after the shared run detects it, so `killedBy` and `coveredBy` include the tests that detect it only in a fresh process. When a timeout or crash loses results, `statusReason` names the tests that did not run. Verdicts are still decided by the first failure, and runs without the option write the same report as before. The option disables `--with-baseline` inheritance, because previous verdicts carry no matrix. Library callers set `MutationRunOptions.DetectionMatrix`, which defaults to `false`.
- `mutation-report.json` and `timings.json` carry a `run` object (`state`, `complete`, `unjudged`, `unjudgedBy`, `interruptedTargets`) that tells whether the run settled every mutant. A mutant with no verdict is reported as `Pending` with a `statusReason`; it counts neither as detected nor as undetected, so the mutation score and `--break-at` ignore it, and `changed-lines` exits 1 instead of 0 when a changed-line mutant is `Pending` and none is undetected. Reports without `Pending` mutants differ from before only by the added `run` object. Library callers see the new `MutantVerdict.Unjudged` case and `PipelineFailure.Interrupted` and `PipelineFailure.UnjudgedOnChangedLines` failures.
- Every settled verdict is appended to `<output>/work/<target>/snapshot.verdicts.jsonl` as it is settled, and the snapshot is written at the end of the run and replaces that file. When a run is killed before it finishes, `--with-baseline` inherits the verdicts it had settled and runs only the mutants without one. A snapshot that still has mutants without a verdict carries no input fingerprint, so it is never reused as a whole result. Detection-matrix runs do not inherit, so they cannot continue this way.
- `run` handles `SIGINT`, `SIGTERM` and `SIGHUP`: the first signal stops workers, starts no further mutant, writes settled verdicts with `run.state: interrupted`, and exits with 128 plus the signal number.
  A second signal ends the process at once; build and generation finish their current work before cancellation takes effect.
  Library callers pass a `CancellationToken` to `RunMutationTesting.ExecuteAsync`; cancellation returns an interrupted result rather than throwing.
- `tests/e2e/medium/interruption/verify-interruption.sh`, part of `verify-push` and `regression-full`, sends `SIGTERM` and `SIGKILL` to a run of a fixture with slow tests and checks that the settled verdicts stay in the report and the journal, that they equal the verdicts of an uninterrupted run, and that the same command with `--with-baseline` tests only the remaining mutants.
- A mutant that exceeds its time budget is no longer counted as detected when the machine was merely slow. After the deadline expires, the engine runs the same tests without the mutant in a fresh process. If that control run does not fit the mutant's budget (or does not finish), the mutant is reported as `Pending` (`unjudgedBy.contendedTimeout`, `statusReason` with the control and budget times), counts neither as detected nor as undetected, and `run` exits with 3 (`run.state` is `unjudged`; the `mutation-select` entry records it as incomplete). If the control run fits, the mutant runs once more in a fresh process and only a second expiry is a `Timeout`; a finished re-run supplies its own verdict. A probe-count `Timeout` and the response-deadline kill are unchanged otherwise, and a worker killed at its deadline is a `Timeout` even if the process had already exited. Library callers construct `TestingOrchestrator` and `IsolationRuns` with `RunDeadlines`, whose `Standard` is the former fixed five-second grace.

### Changed

- Breaking for scripts that call `run`: its exit status has new values. Before, `run` exited only with 0, 1 or 2. It now exits with 3 when it finished but some mutants have no verdict (`run.state` is `unjudged`, for example a mutant whose time budget expired while the machine was contended, which used to be counted as detected). It exits with 128 plus the signal number when `SIGHUP`, `SIGINT` or `SIGTERM` stops it (129, 130, 143) after writing a partial report, where such a signal used to kill the process and leave no report. A script that treats every status other than 1 and 2 as success, or that handles only 0, 1 and 2, must handle 3 and 143 (and 129 and 130): 3 means the verdicts are incomplete, not that the run succeeded or that the score is below `--break-at`, and 143 means a partial report exists in the output directory with `run.state` set to `interrupted`, not that none was written. Status 3 takes precedence over 2, because the score ignores the mutants without a verdict.
- Library callers that implement the engine ports must follow three changes: `ISnapshots.Save` is replaced by `ISnapshots.Begin`, which returns an `IVerdictJournal`; `IWorkerRuns.ObserveAsync` takes a `CancellationToken`; and `IWorkerRuns.ExecuteAsync` takes an `ExecutionControl` that carries the settled-verdict callback and the cancellation. `TestingOrchestrator.ExecuteAsync` takes the same `ExecutionControl`.
- `mutation-select` records the report's `run` object as `reportRun` in `.mutation-output/select/selection.json`, so an analysis stopped at its limit shows how many mutants it had judged. The partial report of a stopped analysis stays under `.mutation-output/select/runs/<UTC time>-<HEAD>/reports/`.
- `verify-full` appends its backlog to `work/backlog.md` instead of `docs/backlog.md`, and the manual medium benchmark writes to `work/performance/medium/` instead of `tests/performance/medium/work/`. Preserve existing local records under `work/` before rerunning the benchmark, which recreates its target and results directories.
- `verify-push` no longer runs changed-line mutation or the `changed-lines` gate on the engine itself, so no mutation result decides a push. `verify-full` now runs the new `regression-full` entry (`verify` and the end-to-end checks, without narrowing them to a change) and then the new `mutation-full` entry (whole-engine mutation, recorded in `work/backlog.md` as before) as two separate runs, and exits with the status of `regression-full`.
- `verify-full` now runs the new `mutation-select` entry instead of `mutation-full`. It runs `run --since <base> --changed-lines --with-baseline`, which generates only the mutants on changed lines, where the base is the commit of the last completed analysis (or, without a record, the merge-base with the upstream or the default branch). It records the base, the changed files as a reference only, the mutated files, and the unanalysed scope under `.mutation-output/select/`, records the `changed-lines` judgement with its raw exit status apart from analysis failures, and distinguishes a diff failure, no change, zero mutants, and a run stopped at its limit. Each analysis is kept under `.mutation-output/select/runs/<UTC time>-<HEAD>/` with its base, HEAD, raw report, and an `undetected.json` that lists each undetected mutant, whether `changed-lines` reported it on a changed line, and empty cause, evidence, and severity fields for later classification. `regression-full` and `mutation-select` now stop at the limits declared in `tiergate.json` (900 and 1800 seconds) and keep the raw timeout status of an incomplete run. `mutation-full` remains an explicit entry for whole-engine analysis.
- Engine results now use TypeModeling's union-struct `Result` API, and closed hierarchies use `[Closed]`. Consumers of `BuildCore.RunAsync` and `BuildCore.OnChangedLines` must replace references to the removed nested `Succeeded` and `Failed` case types; `IsFailed` extracts the failure or success value without boxing.
- The `verify` and `verify-push` time budgets are declared in `tiergate.json` at the repository root. The declaration file was renamed and its keys are unchanged.

### Fixed

- Resident-worker outcomes are retried in a fresh process when the active mutant branch was never reached, whether the tests passed or failed. A failed resident run in this state discards the worker; a replacement starts only when the next shared run needs one.
- Unexpected worker replies during test discovery are identified in the failure diagnostic instead of being reported as a missing reply.
- The existing 15-minute `verify-push` budget is declared in `tiergate.json` so the managed pre-push hook can resolve and execute the repository's verification entry.

## [0.4.1] - 2026-10-02

### Fixed

- `run --since`, `--changed-lines`, and the `changed-lines` gate treat a file moved or renamed since `REF` as deleted and added, so every line at its new path counts as changed. Git's rename detection used to leave a pure move with no changed lines and a move with edits with only its edited hunks, so both mutant generation and the gate skipped them. This reverses the 0.3.0 change that stopped selecting pure renames.
- `run --since REF --with-baseline` resolves `REF` and its diff before reusing the previous run, and the reuse key includes the changed lines of the target's sources. When `REF` moves, the run no longer returns results for the old changed lines, and when `REF` no longer resolves, the target fails instead of succeeding from the saved snapshot.
- `--since` runs keep their snapshot and mutated assembly under `since/` in the output directory, apart from whole runs. They no longer overwrite the whole-run snapshot, so the next whole `--with-baseline` run in the same output directory can still reuse it.

## [0.4.0] - 2026-10-02

### Added

- `run --since REF --changed-lines` generates and executes only mutants whose reported source span intersects a changed line. Untracked files remain fully selected, and a change with no eligible mutants succeeds without executing unrelated mutants.

### Changed

- `--since` resolves its base to a commit before diffing, so invalid refs and Git options supplied as refs fail instead of silently changing the selection.
- The pre-push verification runs changed-line generation before applying the existing `changed-lines` report gate; whole-project mutation remains a separate, non-gating release check.

## [0.3.1] - 2026-10-01

### Fixed

- Schemata compilation no longer expands nested boolean expressions exponentially by copying mutated descendants into every alternative. Analyzer projects with long boolean chains can now be mutated without overflowing Roslyn syntax spans.
- Failed-source dumps retain the rewritten source text and its original trivia rather than normalizing the syntax tree, so diagnostics remain available when compilation fails.

## [0.3.0] - 2026-10-01

### Added

- `changed-lines` command: reads the `mutation-report.json` of a `run --since REF`, selects the mutants whose line range overlaps a line changed since `REF`, prints the undetected ones (Survived or NoCoverage), and exits with 0 when there are none, 2 when there are, and 1 when the report or the diff cannot be read.

### Changed

- `--since` reads the changed files from one `git diff -U0` of the working tree against the ref, so files whose content did not change (pure renames, mode changes) are no longer selected, and a failure to list untracked files now fails the run instead of being ignored.
- Self-applied verification is split by time budget into `devenv shell verify` (build with cognitive complexity S3776 at threshold 15 as an error, and unit tests), `verify-push` (end-to-end checks and the changed-line gate on the engine) and `verify-full` (whole-engine mutation whose new failures and undetected mutants are appended to a backlog). The score-floor gate and `--exclude-static` are no longer used.

### Fixed

- `--since` now selects untracked files of a project that lives below the repository root; their paths were resolved against the wrong directory.
- `--since` and `changed-lines` read the repository that contains the project or the report even inside a git hook, which exports `GIT_DIR` and related variables for the calling repository; they used to read that repository and treat the working directory as its work tree.

## [0.2.0] - 2026-09-21

### Added

- Multi-target runs: `--project` and `--test-project` accept comma-separated pairs, the build runs once through a generated solution, per-target artifacts live under `work/<target>/` and `mutated/<target>/`, and one merged report covers every target.
- Relational pattern mutations (`> x` and its family in `is` patterns, switch expression arms, switch statement labels and property patterns), guarded against replacements that break exhaustiveness or subsume an earlier arm.
- Negation mutants for conditions that declare `out` or pattern variables, woven as `(condition) ^ IsActive(id)` so definite assignment survives.

### Changed

- A failing target no longer aborts the run. The target is recorded as abandoned with its reason and elapsed time, the remaining targets are still verified, and the exit code becomes 1.
- Only targets whose compiler arguments are missing from the binlog are rebuilt, instead of rebuilding every target.

### Fixed

- Rewritten sources are written to `mutated/failed-sources/` when the rollback retry limit is reached, matching the other compile-failure paths.

## [0.1.0] - 2026-08-25

### Added

- Mutation testing pipeline: csc-argument replay from a single MSBuild binlog, semantic-aware mutant generation, single schemata compilation, resident in-process workers with per-test coverage and fail-fast, a mutation-testing-schema report and per-phase timings.
- Test framework hosts: xunit v2 (in-process front controller), TUnit / xunit v3 on Microsoft.Testing.Platform 1.x / 2.x (test discovery via server mode), NUnit / MSTest (VSTest adapters run in process), with runtime tracking of static-initialization mutants.
- Selection options `--mutate`, `--since`, `--ignore-operators`, `--ignore-methods`, the CI gate `--break-at`, survivor re-verification `--validate-survivors`, `--exclude-static`, and `--with-baseline` snapshot inheritance with a full-match short circuit.
- Mutation operators covering arithmetic, comparison, logical, literal, LINQ, string and Math method pairs, initializer clearing, and statement removal for simple assignments and increments.
- Self-applied verification: the engine mutates itself in `devenv shell verify` and fails below the recorded score floor.
