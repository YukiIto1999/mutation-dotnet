# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-08-25

### Added

- Mutation testing pipeline: csc-argument replay from a single MSBuild binlog, semantic-aware mutant generation, single schemata compilation, resident in-process workers with per-test coverage and fail-fast, a mutation-testing-schema report and per-phase timings.
- Test framework hosts: xunit v2 (in-process front controller), TUnit / xunit v3 on Microsoft.Testing.Platform 1.x / 2.x (test discovery via server mode), NUnit / MSTest (VSTest adapters run in process), with runtime tracking of static-initialization mutants.
- Selection options `--mutate`, `--since`, `--ignore-operators`, `--ignore-methods`, the CI gate `--break-at`, survivor re-verification `--validate-survivors`, `--exclude-static`, and `--with-baseline` snapshot inheritance with a full-match short circuit.
- Mutation operators covering arithmetic, comparison, logical, literal, LINQ, string and Math method pairs, initializer clearing, and statement removal for simple assignments and increments.
- Self-applied verification: the engine mutates itself in `devenv shell verify` and fails below the recorded score floor.
