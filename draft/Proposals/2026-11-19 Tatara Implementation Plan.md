# Tatara Implementation Plan

2026-11-19. Approved by the owner on 2026-11-19 (decisions 1–13 below, each "as recommended"). Base dev `5dd6543e6`. LLVM / LLD 22.1.8.

The design it follows is the owner's Tatara Design r3 (a skeleton; r2 is the reference for per-unit detail, and r3 wins where they
differ). Both are outside the repository (`OneDrive\Dev\Kimigayo\Design\2026-11-19 Tatara Design r3.md`, `... r2.md`); this plan
restates everything a unit needs.

## 1. Goal

- Kimi checks, generates code and links in one process. A build starts no external tool.
- `kimi tatara` enters an interactive mode; each line runs `build`, `run` or `exit`.
- **Stage A** replaces only the executor: outputs, records, diagnostics and executable bytes stay as they are.
- **Stage B** changes rules, one unit per change.

```text
> kimi tatara
tatara> build A.kimiproj
tatara> run B.kimiproj
tatara> exit
```

## 2. Decisions (the owner's, 2026-11-19)

| # | Decision |
|---|---|
| 1 | Two stages. A: the executor only. B: rule changes. |
| 2 | The executable keeps its fixed path. The build record is the single truth (no generation directories). |
| 3 | Until the switch, the new path is reachable only through `kimi tatara`. |
| 4 | A Tatara lane (T) plans and implements; the Middle reviews. |
| 5 | LLVM / LLD static libraries come from a pinned prebuilt archive. |
| 6 | Only the five per-build tools are replaced: `opt`, `llc`, `llvm-nm`, `llvm-readobj`, `lld-link`. |
| 7 | Stage A's Tatara is one `tatara.dll` in the toolchain directory. |
| 8 | This plan lands in `draft/Proposals` through the Coordinator. |
| 9 | Stage A revises IMPL twice: a `tatara` row in §20.3 (unit A3) and §20.3.2's Toolchain wording (unit A4). |
| 10 | The single-shot switch happens in a dedicated window once its gate passes. |
| 11 | The session that wrote this plan is the T lane's planner. Seats 1+1. The prompt uses SimplePrompt. |
| 12 | The T lane is added to AGENTS, WORKFLOW and COMPILER_ARCHITECTURE in the commit that adopts this plan. |
| 13 | C++ is a thin layer only. Steps and verdicts stay in C# (P13). |
| 14 | Unit A4 is judged on the `BuildChain` series run as an interleaved paired A/B (the base on the external tools against the candidate): on every row the candidate's p50 wall time is at most 1.03 times the base's, and its p95 peak committed memory is at most 64 MiB above the base's (the owner, 2026-11-20, board O-452, verbatim 「(b) と (m2)」). |
| 15 | In stage A a `run` program inside `kimi tatara` shares the session's input: it gets no terminal input of its own (a STATUS limit and a PLAN open issue; the redirected reader reads without read-ahead) (the owner, 2026-11-20, board O-454, 「推奨どおり」 and 「質問1は (b)」). |
| 16 | `Kimigayo.NativeExecutor`, the native executor's factory, is an approved temporary reader of Build until unit A4 (one `ArchitectureRulesTest.BuildReaders` row and one COMPILER_ARCHITECTURE sentence, both removed by A4) (the owner, 2026-11-20, board O-455, 「推奨どおり」 and 「質問2は (a)」). |
| 17 | After unit A4 a second Ctrl+C ends any command at once with 130, except during a publication SPEC 23.2.3 protects (`check --Format json`) (the owner, 2026-11-21, board O-460, 「(a) as recommended」). |

Assumed without a question: agents run Opus 5.5; branches are `claude/t-*`; the usage-stop rule and the heavy-run rules apply as to
every lane.

## 3. Facts from the current code (corrections to r3)

Read on dev `5dd6543e6`: `src/Kimi/Build/NativeToolchain.Build.cs` and the verification scripts.

1. **Stage A reads an O2 module's IR twice, as today.**
   - Today `opt` writes the optimized IR as text, the paths in it are redacted, and `llc` parses that text again.
   - Generating from the in-memory module can differ from generating from the re-parsed text (use-list order).
   - Stage A's condition is equal bytes, so it reproduces the text round trip. Parses drop from four to two.
   - Generating directly is a later candidate (§8).
2. **The target table is built in stage B, with the Linux target.** `WindowsProfile` already is the Windows row. In stage A a request
   to Tatara carries the triple, CPU, features, relocation model and code model as values; the C++ side holds no Windows constant.
3. **The switch does not touch the native fixtures.** The 6,283 fixtures run through `src/backend/windows-x64/test-scalars.ps1`, which
   calls the LLVM tools directly and never `kimi build`. The script and the tools stay after the switch as an independent control; its
   reuse key does not move.
4. **The new path writes no `.inspection.txt`.** The file is `llvm-readobj`'s raw output; IMPL's artifact list does not name it and no
   test reads it. The same checks run on the facts Tatara returns. Unit A2's first commit stops writing it on the old path too (no
   reader; IMPL's artifact list does not name it), so the two paths write equal file sets from A2.
5. **The record's `tools` field changes:** the five tool paths become `tatara.dll`'s identity. The record's format is
   implementation-defined (IMPL §20.3.2), so IMPL does not change for it.

The reproducible link setting (`/Brepro`) and "link to a temporary file, then replace" already exist in the current code.

## 4. Structure

```text
CLI ──> P13 (C#: steps, verdicts, the record) ──> executor ──> object / facts / executable
                                                   ├ external tools (today's; deleted in unit A4)
                                                   └ Tatara (tatara.dll: thin C++ over LLVM / LLD)
```

- **The executor** has the four calls that run LLVM or LLD, and nothing else. Generation is two calls because the redaction of the
  optimized text between them is P13's one rule, in C#.
  - **Optimize:** verifies the IR, runs `default<O2>` and writes the text as `opt -S` writes it.
  - **Emit:** verifies the IR and writes the object as `llc` writes it.
  - **Inspect:** reads the object; returns facts: undefined symbols, external definitions, whether unwind information exists, whether
    default-library directives exist.
  - **Link:** takes the permitted inputs and settings; writes the executable.
- **P13 decides.** Today's C# verdicts (the allowed undefined symbols, one `_fltused` definition, and the rest) are used unchanged.
- **Two executors coexist only from unit A2 to unit A4.** A4 deletes the external executor and the seam; P13 then calls Tatara directly.
  This temporary structure and its removal stage are recorded in COMPILER_ARCHITECTURE's ledger.
- **Tatara's rules:**
  - LLVM / LLD state is released per request. No process-wide setting (`cl::opt`, `/mllvm`).
  - No exception crosses the C ABI. In stage A every failure Tatara reports is the toolchain's failure, as a failed tool is today
    (stage A's P13 neither parses a user library nor judges which library supplies a symbol, and `build --Manifest` takes the user's
    IR); after a fatal LLVM or LLD error Tatara cannot run again in that process, and every later call fails naming the first cause.
    Both become internal errors with the stage B unit that gives P13 that judgment.
  - At start P13 checks `tatara_abi` (the SHA-256 of `tatara.h`, which `Kimi.dll` embeds) and LLVM's version; the whole-source build
    identity is recorded.
  - Every operation runs inside LLVM's crash recovery on a thread with the tools' stack (10,000,000 bytes); crash recovery, the
    fatal-error handler and the target's registration are Tatara's only process-wide state.

```c
// tatara.h (the whole surface, as a sketch: nine exports)
void        tatara_identity(TataraIdentity* identity);                         // the build identity and LLVM's version
const char* tatara_abi(void);                                                 // the SHA-256 of tatara.h
int32_t     tatara_optimize(target, ir, optimizedIr, TataraResult** result);  // verify, default<O2>, the text
int32_t     tatara_emit(target, ir, obj, optimizationLevel, TataraResult** result);  // verify, the object
int32_t     tatara_inspect(obj, TataraResult** result);                       // the object's facts
int32_t     tatara_link(arguments, count, TataraResult** result);             // permitted inputs -> executable
TataraText  tatara_text(const TataraResult* result, int32_t kind);            // a result's text
int32_t     tatara_flag(const TataraResult* result, int32_t kind);            // a result's flag
void        tatara_release(TataraResult* result);
```

## 5. Stage A units

Each unit is Major (design, independent design review, implementation, review, Session, window). The Middle reviews.

### A1 Baseline and material

No product code changes.

- **LLVM check:** whether CI's official archive (`clang+llvm-22.1.8-x86_64-pc-windows-msvc.tar.xz`, SHA-256 pinned) carries the static
  libraries needed (LLVM and LLD) and the headers, and whether its CRT fits. If not, build from source once and keep the result with
  its hash.
- **Build:** `src/Tatara` holds the C++ sources and a build script (`build.ps1`) that uses the archive's `clang-cl` and `lld-link`.
  Setup, `installation.json` and CI take the DLL in A4: every worktree shares one `toolchain` directory through a junction, so
  until the switch T builds into a directory of its own.
- **Baseline:** time and memory of single-shot `kimi build` on fixed inputs (Empty, Hello, some milestones, a large input). The
  comparison code lives in `src/Benchmark`.
- **Survey:** the tests and scripts that read the record's `tools` field or the old options.

**Done when:** `build.ps1` builds, from the pinned archive, a `tatara.dll` that only returns its build identity, and checks it by
loading it; the baseline is in `artifacts/benchmarks/`; the owner has the proposed time and memory budgets.

### A2 The Windows path

Three commits, one window: `.inspection.txt` no longer written; the seam (structure only); the Tatara executor (its
native and managed sides).

1. **Structure only (behavior unchanged):** the tool-using parts of `NativeBuild`'s Compile, Inspect and Link become the executor. The
   only executor is the external tools.
2. **The Tatara executor:** the C++ generate, inspect and link, and the C# that calls them. The CLI cannot reach it yet; only tests do.

**Comparison tests** (two, in xUnit, run only when selected explicitly; inputs: the native fixtures' `.ll` at O0 and O2, the
milestones, the FFI examples):

- **Executor comparison (until A4):** the same input through the product's two executors, compared down to the record and the
  diagnostics. Deleted in A4 with the external executor.
- **Tool control test (permanent):** the test calls the same archive's external tools (`opt`, `llc`, `lld-link`) directly and compares
  their object and executable with Tatara's. It stays after A4 and is the pass condition of an LLVM update (§6).

Both comparisons are xUnit methods (`Explicit` until A4); the corpus run executes the same methods over a named Session's fixtures
and every milestone.

**Done when (this is also the switch's gate):**

| Compared | Condition |
|---|---|
| Object, the O2 `.ll`, executable | equal bytes |
| Record | equal except the `tools` field |
| Diagnostics | equal text |
| Child processes | zero on the new path |
| Repetition | requests repeated in one process give the same results |

An input that still differs is investigated; a difference that cannot be removed goes to the owner with its example.

### A3 `kimi tatara`

- **Command:** `TataraCommand` (`kimi tatara`). A line is a command line: the member that parses and executes the text after `kimi `
  runs it (SimpleCommandLine's split; no lexical rule of its own). Only `build`, `run` and `exit` are allowed (compared as command
  names are, case-insensitively); an empty line does nothing; anything else, `exit` with arguments and a line with an unenclosed `|`
  are that line's error. A line holds at most 32,767 code units (a command line's limit), on both reading paths. The executor is
  Tatara.
- **Reading:** at a terminal SimplePrompt (single line, no multi-line delimiter, empty input allowed); redirected, one line at a time
  without read-ahead (a carriage return before the line end dropped; a byte order mark at the start skipped).
- **Execution:** one command at a time, each line in a new DI scope (a new command object and `Solution`). Carries between lines: the
  library per target, Tatara's loaded DLL per path and its refusal, `Kimigayo`, the current directory, the runtime. Inputs are read
  from disk every time.
- **Ctrl+C:** one owner, `Program`'s handler, through one replaceable action. At the prompt it cancels the read; during a line it
  cancels that line (a build stops at its next check, after an LLVM or LLD call in progress; a running program is ended as `run` ends
  it, since the program has a hidden console of its own and Kimi's Ctrl+C cannot reach it); during a line already cancelled it ends
  the process at once with 130. Every other command's Ctrl+C is unchanged.
- **Ends:** `exit`, end of input, the root's termination (the window closing), and Tatara's refusal after a fatal error (one sentence;
  a new session starts clean). At a terminal SimplePrompt has no end-of-input key.
- **Documents:** a `tatara` row in IMPL §20.3 (the Coordinator edits; independent review).

**Done when:** in one process, edit → failure → fix, end of input and Ctrl+C work; products and diagnostics equal single-shot
`kimi build`'s; interactive time (first command, later commands) and the session's memory are measured on `BuildChain`.

### A4 The single-shot switch

After A2's gate passes, in a dedicated window. The gate runs on the unit's head before its deleting commit (kept as a branch),
merged with the window's dev.

- `build`, `run` and `test` use Tatara.
- The external executor, the seam and the build's tool launching are deleted (`toolchain verify` still runs the tools it checks).
- `toolchain verify` checks `tatara.dll`.
- Setup builds `tatara.dll` (`setup.ps1 -LlvmRoot <the archive>`, required; a file whose destination has the same SHA-256 is never
  written; `tatara.dll` and the record are replaced by a retried move, a loaded DLL renamed aside), the installation record holds its
  hash once (`tatara.sha256`, with `schemaVersion` 1 kept: older readers ignore the member), and CI builds it.
- `LlvmBin` stays accepted and builds stop reading it; its removal is B6's question.
- A second Ctrl+C ends the process at once with 130 (decision 17).
- IMPL §20.3.2's Toolchain wording is revised, and §20.3 states the second Ctrl+C.
- The setup tools and the native fixture runner do not change (§3, item 3).

**Done when:** the Session passes; single-shot time and memory are within budget (decision 14), judged on A1's series run at A4 as an interleaved
paired A/B (the base on the external tools against the candidate); `kimi build` starts no child process.

## 6. Updating LLVM

A new LLVM version is one unit. The rules below hold from stage A.

**Rules**

- **One place names the version:** `llvmVersion` in the existing supply catalog `src/backend/windows-x64/profile.json`. Tatara gets no
  version setting of its own.
- **Tools and libraries come from the same archive.** The setup tools, the tools the control test calls and the libraries Tatara links
  cannot drift apart.
- **Tatara reports its LLVM version.** At start P13 compares it, with the build identity, against the catalog's; a mismatch is an error
  before any build.
- **Only `src/Tatara` touches LLVM's C++ API.** No LLVM type crosses the C ABI, so a version change leaves the C# side alone.
- **The tool control test stays** (A2): it compares Tatara with the external tools of the same version, so every update is judged by
  the same condition.
- **The settings of a source build are kept.** `src/Tatara`'s README states the configuration a source build must reproduce. 22.1.8's
  prebuilt archive fits, so A1 exercises no source build; a version whose archive does not fit exercises one then.

**The update (one unit)**

1. Change the catalog's `llvmVersion` and the archive's SHA-256 (CI's pinned value included) together.
2. Setup rebuilds the tools, the backend and `tatara.dll`.
3. Where LLVM's or LLD's API changed, fix it inside `src/Tatara` only.
4. Pass the tool control test.
5. Run the native fixtures and a Session. Expectation changes from changed generated code follow the existing adoption procedure (the
   backend's `ADOPTION.md`, VERIFICATION).

After B6a the compiler carries the link material: an update also adopts the reproduced `kernel32.lib`'s hash (`kernel32.sha256`)
beside the backend's `artifactSha256` and commits both reproduced `.lib` files under `src/backend/windows-x64/`.

## 7. Stage B units (outline)

Each unit is designed, and the owner asked (with example code), before it starts.

| Unit | Content | Decided first |
|---|---|---|
| B5 | The record is the truth: IMPL §20.3.2's wording, a lock file per output for exclusion, failure tests. Closes PLAN G1045. | The IMPL wording |
| B6 | Single file: Tatara linked statically into the NativeAOT Kimi, link material embedded; the old options (`--ToolchainRoot` and the others) and the reserved names' special case removed. | IMPL §20.3.2 and §20.4, what is embedded |
| B7 | Linux target: `WindowsProfile` becomes a row of the target table (structure only), then the Linux row and the ELF checks. | musl version, minimum kernel, FFI, SPEC §22 |
| B8 | Linux host: Kimi on Linux builds both targets. | The libc requirement of Kimi for Linux |

## 8. Later candidates

Measured first; each one adopted is a unit of its own.

1. **Direct O2 generation:** generate from the optimized module without re-parsing. Where an object changes this is a behavior change,
   so the differences over every fixture are counted first.
2. **Reuse of library check results** within an interactive session, for libraries of equal content (within IMPL §A.2).
3. **Link time by part:** read, resolve, write, close; then choose a measure.
4. **Schema 5:** each external declaration names its supplier, with PLAN T1's schema 5.

## 9. Working rules

- **Parallel:** A1–A3 run beside R7 and R8. The product's default path does not change before A4.
- **Shared files:** a board hold before the first edit of `Unit` (`CommandUnit.cs` and the commands), `Kimi.csproj` and `Build`'s
  existing files.
- **Place:** a dedicated worktree; the main checkout is not edited.
- **Verification:** AGENTS's table (Unit, and a Session at the end of the working session). The comparison tests and the native
  fixtures run by explicit selection. Heavy runs go through `quiet_guard` and avoid timing slots.
- **Documents:** changes to IMPL, COMPILER_ARCHITECTURE, DECISIONS, PLAN and STATUS go to the Coordinator as drafted text. DECISIONS
  records the new component (Tatara), the temporary executor seam with its removal stage, and the T lane.

## 10. Risks

| Risk | Measure |
|---|---|
| The prebuilt LLVM lacks a library, or its CRT does not fit | Checked first in A1. Otherwise built from source once, outside timing slots. |
| Some inputs give different bytes | The text round trip is reproduced as it is. A remaining difference is investigated, then goes to the owner. |
| Single-shot start slows (loading a large DLL) | Judged by A1's baseline and A4's measurement. Over budget: the owner decides before the switch. |
| An LLVM fatal error ends the process | Crash recovery turns it into the toolchain's failure; Tatara cannot run again, so the interactive session ends. |
| The same files as R7 and R8 | Holds, minimal edits; A4 in a dedicated window. |

## 11. Owner questions to come

| When | Subject |
|---|---|
| After A1 | Time and memory budgets (answered: decision 14) |
| During A2 | Inputs that still differ, if any (answered: none; 12,602 fixture rows and 80 milestone pairs equal) |
| Before B5 | The IMPL wording for "the record is the truth" |
| Before B6 | What is embedded; removing the old options |
| Before B7 | The scope of Linux support |
