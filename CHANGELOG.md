# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

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
