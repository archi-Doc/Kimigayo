# Test execution profile

This normative supplement to [§20.9](20-compilation-configuration.md#209-test-command-and-discovery) and [§22.6](22-core-execution-and-foreign-functions.md#226-test-execution-and-reporting) defines the initial Windows x64 test interface. The owning chapters define language semantics; this file defines configuration, identity, transport and output.

## Inputs and selection

`kimi test <input>` accepts a project, source, solution or directory using §20.8.6.1 input resolution. A solution selects every listed project, once per resolved project path. A dependency is a test target only when independently selected. Each project has its own immutable all-case artifact and project-root working directory. A single `--Target` selects a configured target in every project; otherwise each project must have exactly one configured target. Execution requires the Windows x64 host and target. Semantic listing does not require native tools.

Verify all selected projects and all their test bodies before filtering. Collect independent diagnostics across projects. A load, resolution or semantic failure prevents execution of every case. Build every artifact needed by the selected cases before launching any case; a build failure also prevents execution. A project with no matching cases is allowed. An empty selection across the entire command is an error unless `--allow-empty` is present. An unknown `--case` is always an error. `--list` follows the same empty-selection rule.

The command owns one scheduler and one total budget. `--jobs N` limits live cases across all projects, including their recovery. Build concurrency is separate. Case failures do not stop other cases; cancellation or an unrecoverable runner error stops new launches. Unstarted cases are reported as cancelled or not started with a reason, never passed or skipped.

## Options and settings

Unknown options, repeated scalar options, invalid values and conflicting selection/concurrency options are errors. Boolean flags take no value. Existing compiler options retain their existing spelling and values, including `--Debug true`; Release remains the default semantic mode, independently of O0/O2.

| Option | Meaning / default |
| --- | --- |
| `--list` | Verify and list selected cases without generation, linking or execution. |
| `--filter TEXT` | Case-sensitive literal substring of the fully qualified function name. |
| `--case ID` | One exact CaseId in the selected project set; conflicts with `--filter`. |
| `--jobs N` | Positive command-wide limit; default `min(available logical CPUs, 4)`, at least one. |
| `--no-parallel` | One live case; conflicts with `--jobs`. |
| `--allow-empty` | Permit an empty overall case selection. |
| `--timeout DURATION` | Override each project's case execution deadline. |
| `--recovery-grace DURATION` | Override each project's total recovery grace. |
| `--format text\|json` | Standard-output interface; default `text`. JSON mode writes one result document and sends compiler/progress diagnostics to stderr. |
| `--results-dir PATH` | Command-wide result store; relative to the invoking directory. Default `bin/test-results` beside the selected solution, or in the project/directory input root. |
| `--case-diagnostic-count N` | Override per-case retained failure count. |
| `--case-diagnostic-bytes N` | Override per-case retained diagnostic bytes. |
| `--case-log-bytes N` | Override bytes retained from each of stdout and stderr per case. |
| `--run-diagnostic-count N` | Command-wide retained failure count. |
| `--run-diagnostic-bytes N` | Command-wide retained diagnostic bytes. |
| `--run-log-bytes N` | Command-wide retained stdout/stderr bytes. |

Durations use a positive decimal integer followed by `ms`, `s` or `m`, with checked conversion and a maximum of 24 hours. Counts and byte limits use nonnegative decimal integers; zero disables detail retention, not control records or evaluation. No unlimited spelling is supported. Invalid settings are errors, not silently clamped values.

`.kimiproj` has one `Test` settings record, with `Timeout = "30s"`, `RecoveryGrace = "5s"`, `DiagnosticCount = 1000`, `DiagnosticBytes = 1048576`, `LogBytes = 4194304`, and `Environment`, a string-to-string map. CLI case settings override project settings for every selected project. Named profiles and settings inheritance are not introduced. Environment names use the host's comparison rules; duplicate names, NUL, empty names and names containing `=` are errors. `TMP`, `TEMP` and names starting with `KIMI_TEST_` are reserved and cannot be configured.

Command-wide defaults are 100000 failure details, 67108864 diagnostic bytes and 268435456 log bytes. Counts and byte budgets may not exceed `Int32.MaxValue`. A frame is at most 65536 bytes; each worker uses at most 262144 bytes of user-space pending transport data. Metadata and mandatory control storage are reserved before launch, separately from optional detail budgets. Resource exhaustion or an unrepresentable case table is an execution error. Never silently omit final per-case outcomes.

Selection, scheduling, environment and retention settings are recorded in the run result but do not change compiler meaning or require recompilation. Target, build mode, compilation settings and source/dependency changes retain ordinary artifact invalidation.

## Environment and temporary directory

Capture the parent's environment once, apply each project's `Test.Environment`, then create a private copy for each child. Only reserved case settings differ between cases of the same project. Set `TMP` and `TEMP` to an existing unique absolute temporary directory. The working directory remains the case's project root. Close stdin so that reads see EOF. Do not invoke a shell.

`Kimi.Test` is a compiler-provided public nongeneric group containing `tempDirectory() -> string`. The safe, receiver-free function returns an independently owned string containing the current case's absolute temporary directory, without a required trailing separator. Its path is valid throughout initialization, body, cleanup and shutdown. It is available only in test-only bodies and is misuse without an active case. It does not create a second directory. FFI libraries may use `TMP`/`TEMP`; libraries ignoring those variables are not redirected.

The parent reclaims the temporary directory after success, failure or cancellation. No keep-temporary-files option is provided. User-created files outside it are not rolled back. Diagnostic budgets do not bound user-created files or user-code allocations. Failure to recover resources within grace is reported; no deletion follows links outside the owned directory.

## Process management

Use a separate Windows Job Object per case, assigned when the process is created, with kill-on-last-handle-close and no permitted breakaway. The parent owns its non-inherited job handle. The result channel and stdout/stderr handles are inherited or connected only as required; descendants must not inherit the result writer. Assignment failure is an execution error, never fallback to unmanaged execution. Terminate and recover managed descendants even after the case process exits normally. Parent shutdown must not leave managed cases running.

The parent's monotonic execution deadline starts before launch and excludes build and queue time. Recovery uses one absolute grace deadline across process termination, channel draining and temporary cleanup; each stage does not get a fresh grace period. Preserve the original exit/Abort/timeout reason alongside recovery errors.

## Identity

`TestProjectId`, an optional nonempty `.kimiproj` string, supplies the persistent project identity. Otherwise use `PackageId` when present, then the project-file basename (including its extension); an implicit source project uses its source basename. These names are ordinal, NFC-normalized UTF-8. Conflicting project identities in one command are errors with a diagnostic requesting distinct `TestProjectId` values. Never disambiguate by absolute path or solution position. The same project retains its IDs when selected alone or through a solution.

Canonical identity fields are UTF-8 strings, each preceded by an unsigned 32-bit little-endian byte length. TestId is `t1-` plus the lowercase SHA-256 of domain `kimi-test-v1`, project identity, root-relative declaring Container path, normalized function Signature, and the project-relative logical source path using `/`. Generated sources require a stable logical source identity. CaseId is `c1-` plus SHA-256 of domain `kimi-case-v1`, TestId and `default`. Preserve case in logical paths. Compare canonical identities as well as hashes; any collision is an error. Renaming or moving declarations need not preserve IDs.

SiteId is a zero-based unsigned 32-bit index in the artifact's static diagnostic table. IssueId is a positive unsigned 64-bit case-local occurrence counter; zero means omitted details. Never wrap. Counts saturate and carry an `atLeast` indication. Hash algorithms and encodings are versioned, not runtime-dependent `GetHashCode` values.

ArtifactId is `a1-` plus SHA-256 of a canonical manifest containing compiler identity, target, compile settings, fixed input content IDs and the current diagnostic/case tables. It excludes runtime selection/settings. The manifest separately stores the executable's SHA-256; an executable does not hash its own embedded identity. Validate and pin the immutable snapshot once before launching cases. Every child still identifies its ArtifactId and CaseId. Listing has no executable ArtifactId requirement.

## Reporting protocol

The child uses a private result channel separate from stdout/stderr. Protocol version 1 uses ordered, length-prefixed binary frames, little-endian integer fields and UTF-8 text. Reject oversized lengths before allocating. A header carries frame length, version and event kind. Events cover handshake, basic failure, optional scalar values/message, require-Abort, ordinary Abort and completion. Handshake must match the assigned artifact and case before accepting user events. Unknown versions/kinds, duplicate handshake/completion, events after completion, invalid SiteIds/IssueIds and inconsistent counts are execution errors.

Basic failure transmission precedes condition cleanup and message evaluation. Details refer to an explicit IssueId. Only a distinct require-Abort event emitted after temporary cleanup identifies that termination reason. Exit code 1 or a recorded false condition is insufficient. Runtime reporting failure terminates the child without successful completion. Completion includes latched failure state, saturated counts and omission information. The parent requires both valid completion and normal exit zero for a normal outcome.

Use static source/type tables and compact value bits; format in the parent. No success events or failure allocations are introduced by true checks. Limit retained/transmitted details in the child as well as the parent, reserving independent bounded control capacity. Once limits are reached, keep evaluating conditions, messages and cleanup while draining and discarding excess details. Omission alone is not an execution error.

The 32-byte header contains total frame bytes (`u32`, offset 0), version (`u16`, 4), kind (`u16`, 6), IssueId (`u64`, 8), site or flags (`i32`, 16), phase (`i32`, 20), and total observed failures (`u64`, 24). Phases are body=1, cleanup=2, initialization=3, shutdown=4. Unused site fields are -1; unused IssueIds are zero. Counts never decrease.

| Kind | Payload and additional rules |
| --- | --- |
| 0, 1 | ArtifactId then CaseId, each exactly 67 ASCII bytes; zero counts and IssueIds. Both precede user events. |
| 2 | Basic failure; no payload, valid SiteId, positive increasing IssueId equal to total failures. The first false result is mandatory even with zero detail budgets. |
| 3 | Message UTF-8 bytes for an explicit earlier IssueId; site field is 0 for complete, 1 for truncated. At most one message per issue. |
| 4 | Require-Abort after temporary cleanup; no payload, valid SiteId and a nonzero failure count. IssueId may be zero when details were omitted. |
| 5 | Ordinary Abort; no payload. |
| 6 | Normal completion; no payload. A positive count requires an earlier basic failure. |
| 7 | Scalar snapshot: operand role (`i32`, left=0/right=1), kind (`u16`, bool=1/signed=2/unsigned=3/float=4), bit width (`u16`), original bits zero-extended to `u128`. It refers to an earlier IssueId. |

Abort and completion are terminal records. A truncated final frame is an execution error. Scalar snapshots preserve the original value's bits, including floating-point encodings; they never call user formatting. A receiver discards optional details beyond its budgets without discarding failure state. UTF-8 truncation ends at a complete code point.

## Results and retention

JSON results use `schemaVersion: 1`, `operation: "list" | "run"`, `settings`, `projects`, `cases`, `summary`, `errors` and `exitCode`. Command settings record selection, concurrency, total budgets and the result directory. Project records include identity, path, target and effective case settings; environment additions are recorded by name, without copying environment values into reports. Case records include TestId, CaseId, name, source location, selected state, ArtifactId when generated, start/completion state, verification failures, termination, management errors, durations, log paths and omission information. Failures contain IssueId, SiteId, phase, source expression/location and retained values/message. The termination and management fields are independent of verification failures. Durations are integer microseconds; 64-bit integers are decimal strings, with explicit lower-bound flags where needed. Unknown optional fields may be ignored; a different schema version requires explicit support.

Listing and final case display use ordinal fully qualified name, logical source path, project identity and CaseId order. Logs remain per case; parallel output is not interleaved in stored logs. Retained details under a shared budget may vary with scheduling; identities, selection and success criteria do not.

Each invocation owns a new result directory. Save the aggregate result for both success and failure. Keep detailed stdout/stderr only for unsuccessful cases. Delete temporary directories independently of log retention. The store retains at most ten completed runs and 1 GiB of completed results, removing oldest completed runs first; never remove active runs. A single oversized completed run is eligible for removal after reporting its summary. Cleanup failure is reported, and resource shortages never become success. Final result publication uses a temporary file and atomic rename. Process interruption can leave a visibly incomplete run, never a fabricated successful result.

| Exit | Meaning |
| --- | --- |
| 0 | Successful listing, all selected cases passed, or explicitly allowed empty selection. |
| 1 | Verification failure, case Abort/crash or timeout. |
| 2 | Invalid arguments/settings/selection or source/build failure. |
| 3 | Runner startup, communication, recovery or result-storage failure. |
| 130 | User cancellation. |

When causes coexist, choose `130 > 3 > 2 > 1 > 0` and retain all causes in results. A normally completed child exits zero even when verification failed; these are parent-command exit codes.
