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
| 11 | The session that wrote this plan is the T lane's planner. Seats 1+1 (one implementer, one designer or reviewer seat); raised to 2+1 by the owner on 2026-11-21 (「Tataraの席を 2+1に増やしてください。」, relayed by K, 19:1x): two implementers and one designer or reviewer; the one-implementer-per-toolchain-root rule stands. The prompt uses SimplePrompt. |
| 12 | The T lane is added to AGENTS, WORKFLOW and COMPILER_ARCHITECTURE in the commit that adopts this plan. |
| 13 | C++ is a thin layer only. Steps and verdicts stay in C# (P13). |
| 14 | Unit A4 is judged on the `BuildChain` series run as an interleaved paired A/B (the base on the external tools against the candidate): on every row the candidate's p50 wall time is at most 1.03 times the base's, and its p95 peak committed memory is at most 64 MiB above the base's (the owner, 2026-11-20, board O-452, verbatim 「(b) と (m2)」). |
| 15 | In stage A a `run` program inside `kimi tatara` shares the session's input: it gets no terminal input of its own (a STATUS limit and a PLAN open issue; the redirected reader reads without read-ahead) (the owner, 2026-11-20, board O-454, 「推奨どおり」 and 「質問1は (b)」). |
| 16 | `Kimigayo.NativeExecutor`, the native executor's factory, is an approved temporary reader of Build until unit A4 (one `ArchitectureRulesTest.BuildReaders` row and one COMPILER_ARCHITECTURE sentence, both removed by A4) (the owner, 2026-11-20, board O-455, 「推奨どおり」 and 「質問2は (a)」). |
| 17 | After unit A4 a second Ctrl+C ends any command at once with 130, except during a publication SPEC 23.2.3 protects (`check --Format json`) (the owner, 2026-11-21, board O-460, 「(a) as recommended」). |
| 18 | Unit B5 keeps IMPL 20.3.2's "an Application publishes its executable only on success" and meets it: the build links to a private name, writes the success record naming the new hash, then moves the executable into place; a failed move invalidates the record again; a lock per build record (the owner, 2026-11-21, board O-467, 「O-467 a」). |
| 19 | The windows that two file writes cannot close (the process ends between the success record and the move; a failed move whose record rewrite fails; a failed first invalidation) are stated in IMPL 20.3.2, not held off by a second Ctrl+C (the owner, 2026-11-21, board O-468, 「O-468 a」). |
| 20 | One `kimi.exe` (NativeAOT, static UCRT) carries Tatara and the link libraries; `--ToolchainRoot` and `LlvmBin` are errors naming the removal, Kimi reads `KIMI_TOOLCHAIN_ROOT` nowhere, and `kimi toolchain verify` is a self-check (the owner, 2026-11-21, board O-469, 「O-469 a a a」). |
| 21 | The Linux target (B7): musl 1.2.6 built once from a pinned source tarball with the pinned clang and embedded, **linked only when a program imports a C library**; Kimi's Linux runtime uses no C library (its own start, direct system calls, its own allocator over `mmap`); the minimum kernel is 5.10; `static` only on Linux; a reserved supply `c`; windows-x64 stays SPEC §22's validity basis; `Kimi.Windows` only under the `windows` Condition; `build` and `emit` work for Linux on Windows, `run` and `test` are errors (the owner, 2026-11-21, board O-473, 「O-473 Q1 a, Q2 b, Q3a a, Q3b a, Q4a a, Q4b a, Q5 a」, Q1 changed to (d) in T's dialog, 「O-473 の Q1 は (d) に変えます。」). |
| 22 | A freestanding target (unit B9) follows B7, for the owner's goal of writing an OS in Kimigayo: `x86_64-unknown-none` (ELF, loaded by a bootloader); the program implements a platform contract (page allocation, output, abort, clock) as a Kimigayo Contract that the Kimi runtime and library run on; the target row's code generation for kernels (no red zone, no SSE with soft float, the kernel code model, no stack probes, an entry symbol and a linker script). The language features an OS needs beyond this (inline assembly or intrinsics, volatile access, interrupt functions, sections) are a later plan, not started (the owner, 2026-11-21, in T's dialog, 「推奨の組み合わせで結構です」 and 「今後の計画なので、今はまだ進めないでください」). |
| 23 | Windows moves from the process heap (`HeapAlloc`) to `VirtualAlloc`: one Kimi allocator for every target over the platform layer's pages; it serves blocks up to about 256 KiB from its regions (VirtualAlloc's 64 KiB granularity), maps larger ones alone, and is checked on the O2 kernels' timing; its optimization is a later task (the owner, 2026-11-21, in T's dialog, 「Windowsで、HeapAllocからVirtualAllocに変更して」, option 「a」; 「ひとまず、最低限動く形で結構です。最適化は、将来の課題にしましょう。」). |
| 24 | `memcpy`, `memmove`, `memset` and `memcmp` are written once in LLVM IR in Kimi's runtime for every target, with `"no-builtins"`; the backend archive's assembly versions go after measurement (the owner, 2026-11-21, in T's dialog, 「mem* は LLVM IR で書きましょう。」). |
| 25 | The Linux clock is a 28th intrinsic, `MonotonicClock` (`IntrinsicId` 27 -> 28; the vocabulary limit becomes 28): `Time`'s non-Windows counter calls the platform layer's `__kimi_clock` (one `syscall` at O2); the Linux row has no backend archive, and a freestanding row (B9) supplies the clock with its platform layer (the owner, 2026-11-22, board O-478 (i) (a)). |
| 26 | The musl 1.2.6 download is approved: the source tarball, its `.asc` signature and the release key from musl.libc.org into `artifacts\tatara\b7b4\download\` (git-ignored), the signature checked with gpg, the tarball's SHA-256 pinned in `src/backend/linux-x64/profile.json`, the built products committed there, the tarball not committed; a second download (such as LLVM's Linux archive) is asked again. musl is linked only into a Linux program that imports a C library; a Kimigayo-only program uses no C library (decision 21) (the owner, 2026-11-22, O-478 (ii) (a), confirmed in T's dialog 「(a) のまま」). |
| 27 | SPEC §22.5.2's `KIMI_E_PROCESS_HEAP` row is removed in B7r2 (no target has a process heap after decision 23), with K's independent review of the SPEC edit (the owner, 2026-11-22, O-478 (iii) (a)). |
| 28 | Decision 23's memory gate: a corpus kernel whose peak working set grows by more than 50% **and** by more than 4 MiB stops the merge and comes to the owner with the numbers; below it, the numbers go into the allocator's DECISIONS entry (the owner, 2026-11-22, O-478 (iv) (a)). |
| 29 | The freestanding program names its platform by one root-level struct of the Application's own project that declares `Self is Platform` (a Kimi Contract, a SPEC §22.1 row), judged as startup selection is (SPEC §22.4.1): exactly one, non-generic, without Origin parameters, its conformance verified; two are an error on every target; none on `x86_64-unknown-none` is a located `Unsupported_Kd`; one on a hosted target is a located `Unsupported_Kd` at the conformance. No project setting and no Attribute (the owner, 2026-11-22, board O-488, 「O-488 (a) a (b) a (c) a (d) a (e) a (f) 全てa」, (a) (a)). |
| 30 | The runtime holds one platform value: it calls `start(boot: Boot) -> Self` once before the startup body, keeps the value in its own storage (not a static slot), passes `uniq/Self` to `mapPages`, `releasePages`, `write`, `clock` and `exit`, one member per routine of B7b's platform layer, and never destroys it (`Self is Copy`). The members must not allocate, use `Console` or use static storage; an Abort inside a member ends in the runtime's spin loop without a report (the owner, O-488 (b) (a)). |
| 31 | The boot interface is the Limine boot protocol. The runtime's IR carries Limine's base-revision tag and the higher-half direct map and memory map requests, and passes their answers in `Boot`. The image is linked at 0xFFFFFFFF80000000 by the compiler's own linker script, fixed per target row and embedded; a project-supplied script waits for the sections feature (the owner, O-488 (c) (a)). |
| 32 | SPEC §19.2.1's `os` values and IMPL §20.2.1's table gain `none` for the triple OS `none`; `unknown` keeps meaning an unrecognized triple (the owner, O-488 (d) (a)). |
| 33 | Floating point on `x86_64-unknown-none` (soft float, no SSE) links compiler-rt's soft-float builtins, built once from the pinned LLVM 22.1.8 compiler-rt source with the pinned clang (`--target=x86_64-unknown-none -mno-red-zone -mcmodel=kernel -msoft-float -mno-sse`). They are committed as this row's `kimi_backend` archive, hashed in the catalog and linked lazily. The source download (the tarball and its signature) is asked when that unit starts (the owner, O-488 (e) (a)). |
| 34 | SPEC §22's first paragraph adds the freestanding profile `x86_64-unknown-none`, and §22.1's table gains `Platform` and `Boot`; §22.4 states the order: `start`, the startup body, shutdown, then `exit(0)`. IMPL §21.6 states, for this row, that a pointer converted from an integer address reaches the memory or device at that address, with the authority the program's platform documents. A stack overflow there is undefined behaviour (no stack probes). No new vocabulary entry: no `IntrinsicId` and no Attribute for the platform; the clock is decision 25's `MonotonicClock`. QEMU, Limine and compiler-rt's source are downloads asked when their units start (the owner, O-488 (f) 1-5 (a)). Its sentence 'A stack overflow there is undefined behaviour (no stack probes)' stands, and SPEC §17.4.3 states it as the freestanding exception (decision 44; O-494 M1 (b)). |
| 35 | The language-side OS features (inline assembly or fixed intrinsics in Kimigayo, volatile access, interrupt functions, sections), until now "not started", are now in design (the owner, 2026-11-22, in T's dialog, 「さらに、「言語側の OS 機能」の設計も開始してください。」). The design is T's, at its designer seat: `artifacts\handoff\tatara\units\OSF\`. Its owner questions carry example code. No implementation, SPEC text or proposal comes until the owner decides on the design. |
| 36 | Machine instructions are fixed intrinsics in the Kimi library, a group `X86` under `#if arch == "x86_64"`: bodyless functions, one per instruction (`pause`, `readTimestamp`, `halt`, `disableInterrupts`, `enableInterrupts`, `readPort8/16/32`, `writePort8/16/32`; later `loadInterruptTable`, `loadDescriptorTable`, `invalidatePage`, CR and MSR access, `cpuid`), each written by the compiler as one `asm sideeffect` with a memory clobber. Members that touch devices or processor tables are `unsafe` with `- safety:` items. General inline assembly is an Appendix D line (the owner, 2026-11-22, board O-489, 「O-489 全てa」, (a) (a)). |
| 37 | The instructions are one intrinsic family, `IntrinsicId.Instruction`, written `#Intrinsic("Instruction", "<row>")` as `Fail` is, over a Backend table `InstructionRows` (one row per instruction: its asm template and constraints); a new instruction is a row and a library declaration (O-489 (b) (a)). |
| 38 | `X86`'s privileged members exist only where `os == "none"`; the user-mode ones (`pause`, `readTimestamp`, later `cpuid`) on every x86_64 target. On a hosted target a privileged member is "no member", as `Kimi.Windows` is on Linux (O-489 (c) (a)). |
| 39 | Volatile access is `Raw.readVolatile<T>(storage: raw/T) -> T` and `Raw.writeVolatile<T>(storage: raw/T, value: T)`, `unsafe`, `T is PrimitiveInteger`, on every target: each call is exactly one access, never merged, removed or reordered with another volatile access or an `X86` member; not atomic (O-489 (d) (a)). |
| 40 | Interrupt functions are Kimi Contracts `X86.InterruptHandler` (`func handle(frame: ref/InterruptFrame)`) and `X86.ErrorCodeHandler` (`handle(frame, code: u64)`) over a C-layout `X86.InterruptFrame`, with `unsafe func entry<H>() -> usize` / `errorCodeEntry<H>()` giving the code address of a compiler-generated `x86_intrcc` thunk that calls `H.handle`. The entry's `- safety:` is the handler rule (no allocation, `Console`, task or deliberate Abort; only statics Initialized before interrupts are enabled; shared storage only through volatile accesses or with interrupts disabled). The none row's runtime executes `cli` before shutdown and on its Abort path. No Attribute and no syntax (O-489 (e) (a)). |
| 41 | Sections are deferred: the runtime's Limine requests grow by rows as `Boot` fields, and Appendix D gains "Sections, eager statics and project linker scripts" (O-489 (f) (a)). |
| 42 | The order: volatile (OSF2) in any window; the instruction family with `Board`'s members (OSF1a) after B9's second window and before B9h, so B9h's QEMU run reads COM1 and ends on QEMU's exit device; the remaining instructions (OSF1b) and the interrupt declarations (OSF3a) and entries (OSF3b) after it; an interrupt run under QEMU (OSF3c, evidence) after B9h (O-489 (g) (a)). |
| 43 | `IntrinsicId`'s limit rises 28 → 32 (`Instruction`, `VolatileRead`, `VolatileWrite`, `InterruptEntry`), each raised by its own unit with COMPILER_ARCHITECTURE's line; the documents are SPEC §5.6 and §22.1's `Raw` row, §22.1's per-target sentence for `X86`, Appendix D.6's two lines, IMPL §21.3 (the volatile lowering) and §21.6 ("Machine instructions", "Interrupt entries"), each with K's independent review; no download (O-489 (h) 1-3 (a)). |
| 44 | SPEC §17.4.3 gains a freestanding exception: on a profile whose program supplies its platform (`x86_64-unknown-none`), stack exhaustion is undefined behaviour, and no stack check or probe is generated there (decision 34 kept). It is an explicit SPEC revision, landed with B9's unit that adds the row (B9b), with K's independent review. Rejected: a stack check at each function's entry against a runtime limit (O-494 M1 (a)) (the owner, 2026-11-22, O-494 M1 (b); C-458 M1). |
| 45 | A `Platform` member, and every function it calls, must not access static storage: such a member is rejected at its declaration, a located Language error read from its effect summary. Re-entry through the runtime (allocation, `Console`, the clock, an Abort) still halts through the busy flag, and the platform keeps its state in `self` (decision 30). Rejected: composing the members' effects into the runtime operations' summaries (O-494 M2 (b)), and stating the consequence in SPEC §22.4.5 alone, which leaves an unsound acceptance from safe code (M2 (c)) (the owner, 2026-11-22, O-494 M2 (a); C-458 M2). |
| 46 | Decision 45's record is the existing Language code `UnsatisfiedEffectBound_Kd` at the member's declaration with a new requirement `Ownership.PlatformStatic` ("a platform member and every function it calls access no static storage (SPEC 22.4.5)"), as `Ownership.VirtualEffectBound` reports a virtual's bound: `OwnershipRequirement` +1, this answer its approval under the enum's rule (named in B9c2's commit); no new code. Rejected: reusing `Ownership.EffectBound`, the destruction bound of SPEC 8.6/22.2.3, which this rule is not (O-495 (b)); a new code `PlatformStaticAccess_Kd`, a second code for one kind of failure (O-495 (c)) (the owner, 2026-11-22, O-495 (a)). |

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
| B6 | Single file: Tatara linked statically into the NativeAOT Kimi, link material embedded; the old options (`--ToolchainRoot` and the others) and the reserved names' special case removed. Lands as B6a (the link libraries supplied by the compiler) and B6b1-B6b6 (Tatara in the process; the static `kimi.exe` stacked on verify's self-check; the command-line options; `LlvmBin` in projects and manifests; the root's `windows_x64` removed). | IMPL §20.3.2 and §20.4, what is embedded |
| B7 | Linux target: `WindowsProfile` becomes a row of the target table (structure only), then the Linux row and the ELF checks. Lands as B7a1 (the reserved supplies stated once), B7a2 (`TargetProfile`'s first row) and B7b (the Linux row: musl's build and embedding, the libc-free platform layer, Tatara's ELF side, the native build's per-target steps, the commands). B7 lands B7a1 and B7a2, then B7b1 (W1), B7r4 (W2), B7r1-B7r3 (W3, all or none), B7b2a1, B7b2a2, B7b6, B7b2b and B7b5 (W4), B7r5 and B7b4 (W5), B7b7 (W6), B7b8 (W7) and B7b9 (W8) (design `units\B7\design-b7b.md` Revision 2.5, C-445 ACCEPT; the owner's O-504: each call writes its own import's extension; the group B7r is the runtime for every target: B7r4 `mem*` in IR, B7r1 the platform layer, B7r2 the minimal allocator over the platform's pages, B7r3 kernel32's set, B7r5 the backend archive with `__chkstk` alone). | musl version, minimum kernel, FFI, SPEC §22 |
| B8 | Linux host: Kimi on Linux builds both targets. A future plan, not implemented now (the owner, 2026-11-21, board O-479, 「「B8：Linux ホスト上の Kimi」は、今後の計画とします。まだ実装しなくて結構です。」); its preparation (`artifacts\handoff\tatara\units\B8\owner-question.md`, `design-b8-outline.md`) is kept, and its questions Q1-Q6 stay open until the owner takes B8 up. | The libc requirement of Kimi for Linux |
| B9 | Freestanding target `x86_64-unknown-none` (decision 22): the target row, the platform contract the program implements, the kernel code-generation settings, an entry symbol and a linker script. Decisions 29-34 (O-488) fix the platform contract's shape; B9's design follows `units\B9\design-b9-outline.md` (B9a-B9h) after B7b's Linux units. | The platform contract's shape (an owner question with example code before its design) |
| B10 | Language-side OS features (decisions 35-43): fixed intrinsics in a Kimi group `X86` under one family id `Instruction` (Backend table `InstructionRows`), `Raw.readVolatile`/`writeVolatile`, interrupt entries through the Contracts `X86.InterruptHandler`/`ErrorCodeHandler` and an entry intrinsic, sections deferred (an Appendix D line). Units OSF2 (volatile, any window), OSF1a (the instruction family with `Board`'s members, before B9h), OSF1b (the remaining instructions), OSF3a (interrupt declarations), OSF3b (interrupt entries) and OSF3c (an interrupt run under QEMU, evidence, after B9h and OSF3b); design `units\OSF\design.md` Revision 2.1. | The features' forms (the design's owner questions) |

## 8. Later candidates

Measured first; each one adopted is a unit of its own.

1. **Direct O2 generation:** generate from the optimized module without re-parsing. Where an object changes this is a behavior change,
   so the differences over every fixture are counted first.
   Closed 2026-11-23 by its measurement M1 (LC1 design Revision 0, T-335): the second parse and verification of an O2 text cost
   0.58% of Milestone30's O2 build, 0.02% of Large's and 1.55% serial of the native check (about 0.26% of its wall time at
   `-Parallel 6`); every reading is below the 1% gate in wall time, so LC1 stops (T's ruling). No unit.
2. **Reuse of library check results** within an interactive session, for libraries of equal content (within IMPL §A.2).
   Examined 2026-11-23 (LC2 design Revision 0): the Kimi library's parse, Binding, HIR and library layer are already reused once per
   process and target (DECISIONS R8-M53, R8-M55); what a `kimi tatara` line still repeats is MIR work over the library (the owner's
   O-381 (b)), estimated at or below 2% of a Milestone30 session line at O2 (C-381's figures), below the 3% gate, so O-381 (b)
   stands; reuse of a project's library dependencies waits for a workload with one (none in the repository but
   `docs/examples/SourceDependencies`). No unit now; the design's options and owners are the starting point if a gate passes later.
3. **Link time by part:** read, resolve, write, close; then choose a measure.
   Measured (LC3 Revision 0, T-335, `artifacts\benchmarks\lc3-ml-20261123-1427`): the whole link 15.4 ms warm (54% of Hello O0's
   28.6 ms line), 24.8 ms cold. LLD's driver (4.1 ms) is its MSVC toolchain and SDK detection; write (6.5) and close (2.1) are
   LLVM's output buffer (an early writability probe, the output's temporary file, its commit). Adopted: S1 (a compiler's supply
   reused by its catalog hash; in its measurement slot T-339) and D1 (`/lldignoreenv /winsysroot:<stem>.native`: no toolchain
   detection and no environment, about 4 ms per warm link, and the fix of PLAN G1334 (D-1200); probe P-D: 11.9 → 0.35 ms per fresh
   link, bytes equal). Stopped: W-c (a held CryptoAPI context: probe P-W shows no saving), write and close otherwise (no lever
   without a patched LLD; P-W's split recorded in LC3 `r1\p-w.md`), W2 (`/threads:1`) until a measured `kimi.exe` row (D-1171).
4. **Schema 5:** each external declaration names its supplier, with PLAN T1's schema 5.
5. **The native check's time by part (NV2):** measured 2026-11-24 (NV2 design Revisions 0-1, M-N T-340,
   `artifacts\benchmarks\nv2-mn-20261124-0536`): a full rerun (6,349 fixtures, six sessions) 200 s on a quiet machine, about 400 s in
   K's windows; builds 53% (a line 31.6 ms at O0, 60.8 ms at O2; the link 17%, optimize 11%, emit 11%, four record writes 6%,
   staging 3% of the step), runs 40% (6.3 ms per execution), the fixture keys 4% (77% of an all-reused step's 5.4 s). Adopted:
   U1 (a rebuilt fixture whose executables, sidecars and run scripts did not change is not run again, from an entry really executed
   within 12 hours; O-519 (a), O-522 (a2)). Closed after U1 by the owner (2026-11-24: the further gains are not large enough):
   R3 (run workers; P-R2 found the new-executable start hold system-wide), K1 (the reuse path's keys), R1 (runs overlapping builds),
   B1 (more sessions), B3 (fewer intermediate record writes), B4 (one staging folder per output directory), K3 (hard-linked pair IR).
   Their designs stay in `artifacts\handoff\tatara\units\NV2\` as the starting point if the owner reopens NV2. Stopped by measurement
   earlier: launching from several processes, the line read, the template emit, no console per execution.

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
| Before B5 | The IMPL wording for "the record is the truth" (answered: decisions 18 and 19) |
| Before B6 | What is embedded; removing the old options (answered: decision 20) |
| Before B7 | The scope of Linux support (answered: decision 21; decisions 23 and 24 for the runtime under it) |
| Before B7b | The clock, the musl download, `KIMI_E_PROCESS_HEAP`, the memory criterion (answered: decisions 25-28) |
| Before B9 | The platform contract's shape (answered: decisions 29-34) |
| Before B10's units | The language-side OS features' forms (answered: decisions 36-43, O-489) |
| Before B9-W1 | Stack exhaustion on the freestanding row and a platform member's static access (answered: decisions 44-45, O-494) |
| Before B9c2 | B9c2's requirement (answered: decision 46, O-495) |
