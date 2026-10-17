# Q1: `AnalyzeInstance` between R6-gen and R6-check, and the X1 safety role

Base: checkout `dev` = `5c42b577` (clean); documents read from `origin/dev` = `2aac946b`. `git diff --stat 5c42b577 origin/dev`
touches no file under `Compiler/Analysis` or `Compiler/Emission`, so every code citation below holds on origin/dev. Read-only:
no build, test or native run. `(H)` marks an inference I did not verify by running code. Paths are repo-relative;
`A/` = `src/Kimi/Compiler/Analysis/`, `E/` = `src/Kimi/Compiler/Emission/`.

## 0. Answer in brief

- `AnalyzeInstance` is the **plan provider of the old lowering**, not an acceptance step. It re-runs the whole old ownership
  analysis of a verified generic body under one closed `CallPlan`, and `BodyLowering` consumes the resulting `OwnershipBody`.
  `check` never runs it. It runs only when code is generated (`emit`, `build`, `kimi test`), so its refusals act as an
  incidental **build-time gate**.
- Its refusals fall into five classes. Four of them stop mattering at R6-gen: plan or old-subset gaps, old-analysis storage
  limits, old invariants, and i128 capability (R5's instance-stage capability check replaces that one, OD4). The fifth is a
  real safety role: **Language errors that only the instance run sees**, because the old *definition* run has two
  definition-level holes:
  - **X1:** `CleanupObservesBorrows` is false for an abstract Type.
  - **Generic temporaries:** a temporary of abstract Type is never a Loan root.

  An audit of every instance-only branch finds no third class (§2.2).
- On paper, both holes produce programs that `check` accepts (violating SPEC §8.15.1) and that `build` rejects only for
  instances whose concrete Type exposes the hole (violating §23.2.2). No old-path test exists. A MIR-side X1 rule row
  exists only on the rejected branch `r4-u7-rejected`.
- If R6-gen removes the gate with nothing in its place, the default pipeline would **accept and generate** those programs
  until R6-check, which can mean a destructor reading destroyed storage or a dangling temporary. A5 sees them but publishes
  nothing before R6-check (R4-M1).
- **Recommendation: option (e).** Fix the two holes at their cause in the old definition run before R6-gen. These are
  D5-permitted miscompile fixes under A5 plan §9.2's rule, mirroring `MirTypes.Observes` (R4-M36) and A5's StorageDead
  destroy access. Then delete `AnalyzeInstance` together with its only callers at R6-gen. This needs an owner decision that
  supersedes OD10's clause "`AnalyzeInstance` stays a check-only step until R6-check". The fallbacks are (b), a
  Subst→CallPlan check-only driver (full process, transitional), or (d), R6-gen waits for R4's exit (no code, Emission
  lives longer).

## 1. What `AnalyzeInstance` does and consumes

### 1.1 Entry and mechanism

- Signature: `internal OwnershipBody? AnalyzeInstance(OwnershipBody generic, CallPlan call, int defaultParameter = -1)`
  (`A/OwnershipAnalysis.Instances.cs:49`).
- What its doc comment says it does: "Rebuilds the ownership plan of a verified generic body under one closed call
  substitution. The universal verification remains the acceptance proof; the instance plan only fixes acquisitions, value
  flow and cleanup for concrete lowering" (`:40-44`).
- Steps:
  - It refuses at once when the template is unverified or a case run is active (`:51`).
  - It takes a pooled body (`:56-63`) and sets `this.instance = call` (`:65`).
  - It runs `Build(generic.Function, defaultParameter)` (`:69`). For an instance, `Build` skips case enumeration
    (`A/OwnershipAnalysis.cs:449`) and runs `BuildBody` → `CompleteBody`. `CompleteBody` does `Solve`,
    `FinalizeResults`, `PrepareCallReservations`, `VerifyBorrows`, `VerifyCallReservations` and `CheckUnreachable`
    (`A/OwnershipAnalysis.cs:658-674`).
  - A refused instance is stored in `FailedInstance`. An unexplained refusal gets `OwnershipFailure.Internal` (`:70-81`).
- Why it exists: commit `869b8350` ("Record P22 probe: instances need per-substitution ownership analysis"). The lowering
  of a verified generic body needed concrete acquisitions and value flow.

### 1.2 The instance key: a `CallPlan` (not `InterpretationContext`)

- `InterpretationContext` is **not** the instance. It is `record struct InterpretationContext(int Index)`, the index of a
  default-argument interval inside one body, with `-1` for the root (`A/InterpretationContext.cs:5-19`). Definition, case
  and instance runs all use it alike.
- The instance is the `CallPlan` held in `OwnershipAnalysis.instance` (`Instances.cs:13`) and copied into
  `OwnershipBody.Instance` / `InstanceBinding` (`A/OwnershipModel.cs:361-363, 384-399`).
- The `CallPlan`'s **content** is read in exactly one way: `Binding.InstantiateStorageType(type, call)`
  (`Instances.cs:307`, `OwnershipModel.cs:503`). That overload unpacks
  `(Target, DeclaringType, TypeArguments, LengthArguments, Origins, InputOrigins)` (`Binding/Binding.Structs.cs:19-20`).
  These are the fields of `Subst`, and `TyCtxt.Apply(type, subst)` calls the same method with them, plus `self: subst.Self`
  (`Binding/TyCtxt.cs:234-235`).
- The other `this.instance` reads only test for null (`A/OwnershipAnalysis.Borrows.cs:509`, `Closures.cs:218`,
  `Pointers.cs:14`, `OwnershipAnalysis.cs:449, 477, 512, 1672`).
- Tests already build an instance `CallPlan` by hand from a symbol and Type arguments
  (`tests/xUnitTest/Tests/GenericPointerEmissionTest.cs:97-99`, `call.Set(symbol, element, null, [0], [element])`).

### 1.3 Consumer: the old lowering

- `LlvmEmitter.LowerInstances` (`E/LlvmEmitter.cs:417-484`) calls `AnalyzeInstance(entry.Template.Body, call)` for every
  `GenericStoragePlan` entry (`:429`). It then passes the returned body to `this.lowering.Lower(c.Library, body, …)` (`:436`).
- `LowerDefaults` does the same for recursive default evaluators (`E/LlvmEmitter.Defaults.cs:14-15, 31`).
- `BodyLowering` consumes `OwnershipBody` throughout: 811 `Ownership*` and 1,617 `body.X` references
  (emission-inventory §4 row "BodyLowering*").
- The instance list comes from `GenericStoragePlan.Entries`. It is keyed by `CallPlan` contexts and discovered while
  lowering runs:
  - generic destructors are found by the layout engine (`E/LlvmEmitter.cs:36` → `E/AggregateLayout.cs:306-307`);
  - Dictionary storage calls are found by the lowering itself (`E/LlvmEmitter.cs:35`, `RequireDictionaryOperation`);
  - both are drained by the fixed point at `E/LlvmEmitter.cs:239-251`.

### 1.4 Failures and their publication path

- **Sequence.**
  - Failing instance: `LowerInstances` records `FailureInstance`, the context text "While instantiating f<…>" and the
    requesting site (`E/LlvmEmitter.cs:455-466`), then stops at the first failure.
  - Storage limit: `InstanceStorageLimit` turns the failure into a Resource failure (`:291-295`).
  - `ClearInstances(preserveFailure: true)` keeps the failed body until publication (`:301`).
- **Publication.** `Project.Emit` / `EmissionArtifacts.Publish` call `Emission.ReportFailure` (`src/Kimi/SolutionAndProject/Project.cs:543-551`).
  - `ReportFailure` (`E/LlvmEmitter.cs:67-79`) calls `Ownership.ReportInstanceDiagnostics(failed, context, site)`.
  - That routes through the ordinary ownership reporter with `DiagnosticRequirement.Emission`, the context and the
    "instantiation" related location (`A/OwnershipAnalysis.cs:131-132, 159-…`).
- **Records.** This is the G69 path: PLAN row G69 on origin/dev, and the DIAGNOSTICS contract at
  `docs/dev/DIAGNOSTICS.md:56` (origin/dev).
- **`kimi test`.** It also generates (`src/Kimi/Unit/Command/TestCommand.cs:131`), but it reports only the failure string
  (`:133`).
- **What can be reported.** Anything `CompleteBody` reports, unfiltered:
  - located `Unsupported_Kd`: i128 `/` `%` (`A/OwnershipAnalysis.cs:1454-1459`), compound updates
    (`A/OwnershipAnalysis.Values.cs:134-137`), concrete Types outside the old subset (`A/OwnershipAnalysis.cs:701-704`),
    pointer values (`A/OwnershipAnalysis.Pointers.cs`);
  - `OwnershipStorageLimit_Kd` (Resource, `A/OwnershipAnalysis.cs:470-481`);
  - Internal: unexplained refusal (`Instances.cs:74-79`) or a surviving pair layer (`Instances.cs:115`);
  - **any borrow, move or reservation Language code** from `VerifyBorrows` and friends.

### 1.5 Tests that pin it

| Test | Pins |
| --- | --- |
| `GenericGenerationDiagnosticTest` (182 lines; 11 references to `AnalyzeInstance`/`FailedInstance`/`FailureInstance`/`InstanceStorageLimit`) | i128 `/` `%` direct and forwarded: located `Unsupported_Kd`, `c.Analysis.IsVerified` and `Problems` empty, i.e. check accepts (`:16-70`); allocation-free success (`:72-81, 150-159`); slot order in the note via a forced storage limit (`:83-101`); two locations (`:103-114`); storage-limit Resource facts and recovery (`:116-148`); CLI output (`:161-180`) |
| `GenericPointerEmissionTest` | a direct `AnalyzeInstance` call on a hand-built `CallPlan` (`:94-99`) |
| Language codes from an instance | **none**: no test asserts a borrow/move code coming from an instance (grep of the reference sites above; instances survey §3.2) |

## 2. Does it protect soundness today?

### 2.1 Classes of refusal and what happens to each at R6-gen

| Class | Example and site | After R6-gen (old Emission deleted) |
| --- | --- | --- |
| P: plan or old-subset representation | `SupportsType` Unsupported on a concrete Type (`A/OwnershipAnalysis.cs:701-704`); instance pointer reads (`Pointers.cs:14`) | Irrelevant. MIR codegen has its own capability table (R5); a missing case there is a located Unsupported or an `InternalInvariant_Kd`, never silent code |
| R: old-analysis Resource | `OwnershipStorageLimit_Kd` in an instance (`A/OwnershipAnalysis.cs:470-481`) | Irrelevant: no old instance run, no old instance storage |
| I: old invariants | unexplained refusal, surviving pair layer (`Instances.cs:74-79, 115`) | Irrelevant: MIR validator and codegen invariants replace them |
| C: capability | i128 `/` `%`, compound updates in an instance | Replaced by R5's instance-stage capability check, a collector pass before acceptance (A5 plan §9.1 "Instance collection is part of acceptance", OD4; PLAN10 §5.5 condition 3). **Prerequisite of R6-gen** |
| L: Language errors only the instance sees | X1 and generic temporaries (§2.3, §2.4) | **Lost without a replacement.** Wrong code possible until R6-check |

### 2.2 Audit of the instance-only branches (is L larger than two classes?)

An instance run and a definition run differ only where a rule tests `this.instance` or reads a Type that is abstract in the
definition.

- **`this.instance` branches.** All of them are definition-conservative, so none can add a refusal in an instance:
  - `GenericInteger` holds only in the definition, and it *accepts* there (`A/OwnershipAnalysis.Borrows.cs:507-511`).
  - `CallableEffects` and `RequirementEffects` are recorded only in the definition (`Closures.cs:218`,
    `OwnershipAnalysis.cs:1672`).
  - Abstract pointer reads are accepted only in the definition, so the instance can add only Unsupported, class P
    (`Pointers.cs:14`).
  - CopyOrMove resolves to the exact acquisition in an instance (`A/OwnershipAnalysis.cs:684-691`). The definition commits
    the Move-like conservative effect (H).
- **Type-dependent rules where the definition is not conservative**, i.e. abstract parts are treated as *harmless*:
  - **Cleanup observation:** `CleanupObservesBorrows(type)` (`A/OwnershipAnalysis.Cleanup.cs:9-60`) recurses only through
    structs, handles, closures, tuples, arrays, dictionaries and enum payloads. A `Parameter`, `TargetProjection` or
    `AssociatedProjection` falls through to `return false`. Its only reader makes a `Cleanup` operation a use of the place
    only when the flag is set (`A/BorrowLiveness.cs:137`). The flag is set per place at `A/OwnershipAnalysis.cs:698`.
  - **Temporary Loan roots:** `IsTemporaryOriginRoot` (`A/OwnershipBody.Borrows.cs:2745-2751`) admits a temporary of
    String, struct, enum, fixed array, Tuple, Array, Dictionary, Closure, scalar, handle or borrow Type. An abstract Type
    is not in the list. Its callers are `:583, 589`.
- Other uses of `HasAbstractPart` in the old analysis make abstract holders *carry more* (`OwnershipBody.Borrows.cs:725, 1712`),
  so they are conservative.
- **Conclusion:** class L has two known members, matching PLAN10 App. B X1 and STATUS "Generic temporaries"
  (`docs/STATUS.md:64` on origin/dev). The audit is by reading, not by running (H for completeness).

### 2.3 X1 (abstract destruction), constructed on paper

The program uses the shape of the rejected U7 MIR rule row: `r4-u7-rejected` / `211a8e61`, `MirBorrowTest` row "R9 an
abstract Type's drop observes its Origins (SPEC 15.6.6, X1)", which has a concrete `Array<i32>` counterpart that does not
conflict. Here it gets an observing instance:

```kimi
struct Cursor {source}
    Self is LendingIterator
    associate LentItem(step) is i32
    let value: ref/i32 during source
    public init(value: ref/i32 during source) => self.value = value
    public func next(self: uniq/Self during step) -> Option<i32> => .None
    drop
        Console.writeLine("\(self.value@follow)")        // observes its source (SPEC 15.6.6)
struct Values
    Self is Iterable
    associate Iterable.IteratorType(source) is Cursor during source
    var value: i32 = 42
    public init() => ()
    public func iterate(self: ref/Self during source) -> Cursor during source => Cursor.init(self.value@ref)

func f<C>(items: C, other: C)
    C is Iterable
    var local = items@move
    let it = local.iterate()     // it: C.IteratorType(source), carries a shared Loan on local
    local = other@move           // destroys the old local while it's destruction is still ahead
                                 // (end of scope: drop of `it`)
f(Values.init(), Values.init())
```

(Syntax follows `tests/xUnitTest/Tests/BorrowingIterableContractTest.cs:23` and `src/Kimi/Library/Iterable.kimi`.)

- **Old definition run (H).**
  - `it`'s Type is a projection, so `CleanupObservesBorrows` is false.
  - The cleanup of `it` is not a use (`BorrowLiveness.cs:137`), so the shared Loan on `local` is dead at
    `local = other@move`.
  - The definition is **accepted**. SPEC §8.15.1 ("valid for every well-formed Type … Types are never enumerated") together
    with §15.6.6 ("A user-defined `drop` is assumed to observe all reachable Origins") requires rejection, as PLAN10 §5.5
    condition 1 states.
- **Old instance `f<Values>` (H).**
  - `Cursor` declares a drop, so `CleanupObservesBorrows` is true.
  - The cleanup of `it` uses the Loan, so the write conflicts.
  - `build` fails with a located Loan conflict in the Emission partition, carrying the "While instantiating" context.
  - `check` accepts, which violates §23.2.2.
- **Old instance `f<Array<i32>>` (H).** `ArrayIterator<T>` declares no drop (`src/Kimi/Library/Array.kimi:274-280`). The
  instance is accepted and builds; the generated code is harmless.
- **MIR.**
  - `MirTypes.Observes` counts "an abstract part not proven Copy" (`src/Kimi/Compiler/Mir/MirTypes.Destruction.cs:36-55`,
    DECISIONS R4-M36).
  - A5's liveness marks an observing Drop as a use (`claude/a5-1b-core:src/Kimi/Compiler/Mir/Analysis/MirBorrows.Liveness.cs:21, 161-163`,
    `MirBorrows.Checks.cs:398-403`).
  - So A5 rejects the definition, but only in shadow: problems stay unpublished until R6-check (R4-M1,
    `docs/dev/DECISIONS.md:405-408`).
- **Wrong code if the gate goes.** With `Values` holding an `Array<i32>` and `Cursor` holding a Slice of it, the drop reads
  a freed buffer, i.e. a use after free (H).

### 2.4 Generic temporaries, constructed on paper

STATUS before `bb8f33e7` (line 140) gives the shape: "a generic definition over a bare `T` (`make: () -> T` with
`make()@ref` used later) is still judged at instantiation, since generic parameter temporaries are not Loan roots yet".

```kimi
func peek<T>(make: () -> T) -> ()
    let r = make()@ref           // borrow of a temporary of abstract T
    let s = r                    // use after the temporary's storage ended
```

- **Old definition run (H):** an abstract temporary is not a root (`OwnershipBody.Borrows.cs:2747-2751`), so the program
  is accepted.
- **Old instance (H):** any concrete instance (scalar, borrowed, or a struct) makes it a root, so the instance refuses at
  build.
- **MIR (H):** the temporary is a local whose `StorageDead` is a Destroy access (`MirBorrows.Checks.cs:439, 466` on
  `claude/a5-1b-core`), so A5 rejects the definition.
- **If the gate goes:** a dangling reference to a destroyed temporary; for `T = Array<i32>` the buffer is freed (H).

### 2.5 Evidence status

- **No old-path reproducer exists.**
  - Grep finds nothing in `tests/` for `X1`, "abstract destruction" or `CleanupObservesBorrows`, except
    `SharedEngineTotalityTest.cs:76`, which sets the flag synthetically.
  - `git log --all --grep=X1` finds only `211a8e61` and the plan commit `ee3482d6`.
  - PLAN10's R0 row required the reproduction ("抽象型の破棄の穴（付録 B の X1）の再現", PLAN10 line 667; risk 11,
    line 772). It never happened (critic C11).
- **The MIR row exists only on the rejected branch** `r4-u7-rejected`. No current branch (`origin/dev`,
  `claude/a5-1b-core`, `claude/a5-1b-mir`) has an X1 or abstract-temporary row (`git grep` over `tests/xUnitTest/Tests`).
- **Verdict:** today the old pipeline is safe at build *only through* the instance re-analysis (H), and it violates
  §8.15.1 (check accepts) and §23.2.2 (build fails with a Language error) for these programs. Removing the gate at R6-gen
  with nothing in its place opens a wrong-code window until R6-check.

## 3. Options for the window between R6-gen and R6-check

### (a) A slimmed CallPlan-keyed enumerator moved into the old Analysis

- **What it means.** Keep `GenericStoragePlan`'s request walk without ABI and drive `AnalyzeInstance` from it at build.
- **The walk is interleaved with layout and lowering:**
  - generic destructors are discovered inside `AggregateLayout` (`E/AggregateLayout.cs:306-307` via `E/LlvmEmitter.cs:36`);
  - Dictionary storage calls are discovered by the lowering (`E/LlvmEmitter.cs:35`);
  - object and virtual entries come from `ObjectGenerationPlan` and `VirtualGenerationPlan`;
  - recursive defaults come from `DefaultGenerationPlan`;
  - everything is drained by the fixed point (`E/LlvmEmitter.cs:239-251`).

  An enumerator without layout and lowering must re-derive destructor and Dictionary discovery from each instance body's
  place Types.
- **Cost (H).** About 600–900 lines kept or rewritten out of 2,724: collectors 891 + 346 + 398 + 172 + 247, driver 670 (`wc -l`).
  It is a second instance collector beside R5's: AGENTS "one owning component" and "a second copy of a rule" make it a
  symptom fix. It is new code in an area frozen by D5 (ARCH "Freeze") for one stage. Build time stays the same as today.
  It deletes nothing.
- **Verdict:** reject.

### (b) A Subst→CallPlan bridge in the old Analysis, driven by R5's collector

- **Feasible with the current signatures:**
  - The instance `CallPlan` is read only through `InstantiateStorageType`'s six fields (§1.2), which a Subst carries.
  - Templates are found by `FunctionKoto` (`Ownership.TemplateBody(function, analyze: false)`, `E/LlvmEmitter.Defaults.cs:14`).
  - A `MirDefKey(Decl, Kind, Index)` maps to it: Fn → `AnalyzeInstance(t, call)`; DefaultTemplate →
    `defaultParameter = Index − 1`, since `Arg = index + 1` (`Mir/Build/MirBuilder.Defaults.cs:135`, H for the Index
    mapping); Closure, Getter and Setter → their `FunctionKoto`; the DropGlue destructor → its destructor body.
  - The bridge lives on the old side (Analysis or Core). The backend may not read HIR (ARCH dependency table), so the
    collector cannot hold `CallPlan`s; the survey option "R5's collector records each instance's requesting CallPlan"
    (r6check-scope §7) is not allowed.
- **Gaps:**
  1. **Origin-erased key** (A5 plan §9.1 start condition). `InstantiateStorageType` needs Origins, so the collector must keep
     a representative unerased Subst per instance: a column only this bridge reads (H).
  2. **Reverse-mapping fidelity.** `TyCtxt.SubstOf` replaces a null `DeclaringType` with the bound Contract
     (`TyCtxt.cs:202`), and `Apply` passes `self`, which the CallPlan overload does not (`Binding.Structs.cs:20`). A
     rebuilt `CallPlan` can therefore instantiate differently (H).
  3. **Duplicate i128 records** with R5's capability pass. A filter would be a symptom fix.
  4. **Old-only refusals block MIR codegen.** Classes P, R and I would still block MIR codegen of programs MIR can
     generate. That equals today's behavior, so it is no regression.
- **Cost (H).** About +150–200 lines transitional (bridge, Describe/site, collector column, test migration), removed at
  R6-check. It crosses phases (old Analysis reads the backend's instance table), so it takes the full process and a DEC
  temporary-structure entry. It keeps the instance re-analysis that PLAN10 §5.5 says should not exist ("ジェネリック本体は
  インスタンスで再解析しない").
- **What it buys:** OD10 kept as written, and `check` unchanged until R6-check.

### (c) Drop the gate at R6-gen and rely on R5's passes plus A5

- **Kept:** class C, through R5's instance capability check. P, R and I are irrelevant.
- **Lost:** class L (§2.3, §2.4). The default pipeline accepts and generates X1 and abstract-temporary programs from
  R6-gen to R6-check. A5 sees them but publishes nothing before R6-check (R4-M1).
- **Gating generation on A5's problems** (extending `Generates`, `src/Kimi/Compiler/Core/Compilation.Pipeline.cs:154-159`)
  before R4's exit would block builds on an incomplete analysis. A5 plan §6.2 step 1: "Until the joint core lands, the
  library and milestones may report A5 problems". Gating on a subset of problems would be a symptom fix.
- **Verdict:** acceptable only if the window is empty (d) or class L is closed first (e).

### (d) Reorder: R6-gen after R4's exit, or R6-gen and R6-check adjacent

- Removes the window: MIR acceptance exists, and R6-check unit a can precede or accompany the codegen switch.
- No code.
- **Cost:**
  - The old Emission (90 C# files, 20,917 lines, plus 2,335 lines of `.ll.in`; r6check-scope §7) and the codegen selector
    stay until R4 exits.
  - Every D5 miscompile fix lands on both generators.
  - R6-gen inherits R4's schedule risk: R4 is at A5 step 1 with 1b-5 and 1c pending (ARCH R4 row on origin/dev), and R4
    stalled twice before (A5 plan §2.1).
- It supersedes OD10's purpose (R6-gen "right after R5", A5 plan §9.1, line 873), so it is an owner decision.

### (e) Close class L at its cause in the old definition run, then delete `AnalyzeInstance` at R6-gen

- **e1: abstract destruction observes.** In `A/OwnershipAnalysis.Cleanup.cs`, an abstract part (`Parameter`,
  `TargetProjection`, `AssociatedProjection`, a pair owner entity in an owner case) not proven Copy under the definition's
  premises observes. This is the same rule as `MirTypes.Observes` (R4-M36) and SPEC §15.6.6. The Copy proof needs the
  place's source, which `Place()` has (`A/OwnershipAnalysis.cs:684-698`). About +10–20 lines (H).
- **e2: an abstract temporary is a Loan root.** Add abstract Types to `IsTemporaryOriginRoot`
  (`A/OwnershipBody.Borrows.cs:2747-2751`), conservatively without the scalar "borrowed only" restriction, since some
  instance is a struct. About +2–5 lines (H; the reproducer may reveal a second site).
- **Allowed under D5:**
  - ARCH "Freeze" allows "miscompile fixes mirrored on the new path".
  - A5 plan §9.2 (line 888) says: "An old-path acceptance that can produce wrong code is a miscompile and is still fixed under D5".
  - The new path already has both rules: `MirTypes.Observes` on dev, and A5's liveness and StorageDead on `claude/a5-1b-core`.
  - X1 is an anticipated Appendix B item with a SPEC clause (DEC O3 lists X1).
- **Effect:**
  - `check` now rejects both shapes at the definition (SPEC-correct), and §23.2.2 holds for them.
  - The ledger loses rows instead of gaining (d) rows.
  - `AnalyzeInstance` keeps only classes P, R, I and C, so at R6-gen it can go with its only callers.
- **Deletions at R6-gen (H, about −150 to −200 lines in old Analysis):**
  - `Instances.cs:10-14, 21-94` and the instance branch of `Resolve`;
  - the `this.instance` branches listed in §1.2;
  - `ReportInstanceDiagnostics` (`OwnershipAnalysis.cs:131-132`);
  - the instance half of `OwnershipBody.Reset` and `Concrete` (`OwnershipModel.cs:361, 390, 399, 503`);
  - `GenericPointerEmissionTest:94-99`.
- **Risk:** new rejections in the corpus.
  - Landing e1/e2 after A5's step 1 makes these known: from the end of step 1, "the library and the milestones produce no
    A5 problems" (A5 plan §8, R4-M12) under the same `Observes` rule, and "Rejections caused by … PLAN10's X1 … then show
    early" (A5 plan §8).
  - A library or milestone rejection is an O3 owner trigger.
- **Process:**
  - e1 and e2 are each local to the old ownership analysis. They add a branch, so each gets a short design and one review.
  - The OD10 supersession is a DEC entry and needs the owner.
- **Verdict:** recommended.

### (f) Other ideas checked and rejected

- **Run `AnalyzeInstance` inside `Analyze()` (at `check`) over the collector's instances.** This is (b) plus an acceptance
  change, so it is strictly heavier than (e).
- **Keep `AnalyzeInstance` as dead code until R6-check.** It has no caller after R6-gen. AGENTS "Remove what the change
  makes redundant in the same unit".

### Comparison

| | Code | Deletes | Window safe | `check` change before R6-check | Process | Keeps OD10 |
| --- | --- | --- | --- | --- | --- | --- |
| (a) | +600–900 transitional (H) | nothing until R6-check | yes | no | full + D5 exception | yes |
| (b) | +150–200 transitional (H) | nothing until R6-check | yes, for collected instances | no | full | yes |
| (c) | 0 | AnalyzeInstance at R6-gen | **no** | no | owner (supersede OD10) | no |
| (d) | 0 | — | yes (no window) | no | owner (reorder) | no (intent) |
| (e) | +15–25 fix, then −150–200 (H) | AnalyzeInstance at R6-gen | yes (class L closed) | yes: X1 and abstract temporaries rejected at definition (SPEC-correct) | one review each + owner (supersede OD10's clause) | the two-stage split yes; the clause no |

## 4. Recommendation and owner decision

**Choose (e).**

1. Reproduce both classes on the old path (unit Q1-0).
2. Fix them at the definition (Q1-1, Q1-2), each with its regression test, after A5's step 1 lands.
3. Record a DEC entry superseding OD10's clause (Q1-3).
4. At R6-gen, delete `AnalyzeInstance` and its instance machinery with the old Emission (Q1-4).
5. R5's instance capability check is a prerequisite (Q1-6).
6. A5 adds the same programs as rule rows (Q1-5), so behavior is kept after R6-check.

**Branches on Q1-0's result:**
- Neither hole reproduces (the definition already rejects): skip Q1-1/Q1-2 and go to Q1-3.
- The instance does not refuse either (both runs accept): the old path miscompiles today, and Q1-1/Q1-2 are present
  miscompile fixes.

**Owner decision needed:**
- **Supersede OD10's clause** "keep `AnalyzeInstance` as a check-only step until R6-check" (DECISIONS R4-M35,
  `docs/dev/DECISIONS.md:960-961`; A5 plan line 877), conditional on Q1-1/Q1-2.
  - Trade-off: the default `check` rejects X1 and abstract-temporary programs at definition before R6-check (SPEC §8.15.1
    and §15.6.6; A5 rejects them anyway at R6-check) and loses no generated-code safety. In return R6-gen needs no
    transitional bridge and does not wait for R4.
- **If declined, choose between:**
  - (b): no `check` change, but a cross-phase bridge of about 150–200 lines (full process) living until R6-check;
  - (d): no code, but R6-gen, the codegen selector and about 23k lines of old Emission wait for R4's exit.

## 5. Corrections to the task framing and the surveys

1. **"Its input is a CallPlan-keyed instance (InterpretationContext)" is wrong.**
   - `InterpretationContext` is the default-interval index (`A/InterpretationContext.cs:12`).
   - The instance is a `CallPlan` (`Instances.cs:13, 49, 65`; `OwnershipModel.cs:361`), read only through
     `InstantiateStorageType`, whose six inputs are Subst's fields (`Binding.Structs.cs:19-20`; `TyCtxt.cs:234-235`).
   - So an option (b) bridge builds a `CallPlan` (or adds a Subst overload), not an `InterpretationContext`.
2. **OD10 / A5 plan line 877's "check-only step" is wrong.**
   - `AnalyzeInstance` is never part of `check` (`Project.cs:502-515`). It runs only where code is generated:
     `EmissionArtifacts.cs:27` from `Project.Emit` and `TestCommand.cs:131`.
   - It is the lowering's plan provider (commit `869b8350`; doc comment `Instances.cs:40-44`).
   - Its "(H) the old acceptance relies on instance re-analysis" is true only for *build*, and only for class L.
3. **Critic C11 ("no … test reproduces it") is incomplete.** No old-path reproducer exists, but a MIR-side X1 rule row
   with a concrete counterpart exists on the rejected branch `r4-u7-rejected` (`211a8e61`, `MirBorrowTest` R9).
4. **The surveys located no mechanism for STATUS "Generic temporaries".** It is `IsTemporaryOriginRoot` excluding abstract
   Types (`A/OwnershipBody.Borrows.cs:2745-2751`; H that this is the only site). Its shape is in STATUS before `bb8f33e7`,
   line 140.
5. **Instances survey §3.2 is too broad.** It lists "anything else `CompleteBody` can report" as possible instance Language
   errors. Per §2.2 here, the instance-only branches are definition-conservative; only the two abstract-Type rules make the
   definition run lenient.
6. **r6check-scope §7 option (b)** ("R5's collector records each instance's requesting `CallPlan`") breaks the ARCH rule
   that the backend may not read HIR. The mapping must go Subst→CallPlan on the old side.
7. **Instance discovery happens in lowering as well as layout.** Dictionary storage calls come from the lowering
   (`E/LlvmEmitter.cs:35`) as well as destructors from layout (`:36`). This raises option (a)'s cost beyond "port the
   request walk".
8. **`kimi test` also runs `AnalyzeInstance`** (`TestCommand.cs:131`), not only emit and build.

## 6. Units

Verification rows refer to AGENTS "Verification and Delivery". "Shared MIR hold" means a file listed in ARCH's R4/R5
parallel rule (`MirProgram`, `MirBuilder*`, `MirVocabulary`, `MirDescriptors`, `MirValidator*`, `MirDump*`, the
`MirAnalysis` driver, `tests/mir` goldens).

| Id | Title | Stage | What changes (owner, files) | Prerequisites | Deliverable | Verification | Net size | Difficulty | Review weight | Shared MIR hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Q1-0 | Reproduce X1 and abstract temporaries on the old path | early-R6-prereq | None committed. A CLI probe (memory "CLI diagnostic probe") of §2.3/§2.4 and the `Array<i32>` counterpart: `check` verdict, then `WriteIr` verdict and code | none | A recorded result per program (definition accepts? instance refuses with which code?) in the unit's design note for Q1-1/Q1-2; decides the branch of §4 | Probe run only (no build of changes); evidence kept in `artifacts/` | 0 | 1: running a known program; the only judgment is reading the codes | none (evidence feeds Q1-1/2 reviews) | no |
| Q1-1 | Old definition run: an abstract part's destruction observes (X1) | early-R6-prereq | Old ownership analysis: `A/OwnershipAnalysis.Cleanup.cs` (`CleanupObservesBorrows`), its call site `A/OwnershipAnalysis.cs:698` (Copy proof under the definition's premises). Regression test in the owning class (`GenericGenerationDiagnosticTest`, or `BlockEscapeLoanTest`, which holds abstract-holder cases) | Q1-0 reproduces; A5 step 1 merged (`claude/a5-1b-core`: the MIR mirror exists and R4-M12's corpus condition holds) | `check` rejects §2.3's definition at `local = other@move` with the old Loan-conflict code. `T is Copy` and concrete counterparts stay accepted. Commit states the D5 miscompile basis (A5 plan §9.2) and the ledger effect | Unit: the related classes (GenericGenerationDiagnosticTest, BlockEscapeLoanTest, Borrowing*, GenericOwnershipCaseTest/pair-cases, PairFollowTest) plus `-DiagnosticSnapshot`; Session with milestones and `KimiLibraryShapeTest`, since acceptance changes | +15 / −2 src, +30 tests (H) | 3: a static walk needs the Copy premise environment; old-path test fallout and possible library rejections (O3) | one review (short design: local to one owner but adds a branch) | no |
| Q1-2 | Old definition run: a temporary of abstract Type is a Loan root (STATUS "Generic temporaries") | early-R6-prereq | Old ownership analysis: `A/OwnershipBody.Borrows.cs` `IsTemporaryOriginRoot` (`:2747-2751`), and any second site the reproducer shows. Regression test in the owning class (`BlockEscapeLoanTest` or a temporary-borrow test class). STATUS row "Generic temporaries" removed (Auto) | Q1-0 reproduces; A5 1b-core merged (StorageDead destroy access) | `check` rejects `let r = make()@ref; let s = r` at definition; a use inside the statement stays valid | Same rows as Q1-1 | +5 / −1 src, +25 tests, STATUS −1 row (H) | 3: the mechanism is located by reading only (H); scalar versus struct root conditions must stay conservative | one review | no |
| Q1-3 | DEC: `AnalyzeInstance` is deleted with the old Emission at R6-gen | early-R6-prereq | `docs/dev/DECISIONS.md`: a superseding entry for OD10's clause, naming the reason that no longer holds (X1 and abstract temporaries closed by Q1-1/Q1-2). `docs/dev/COMPILER_ARCHITECTURE.md` R6 row: drop "keeping `AnalyzeInstance` as a check-only step until then" (Auto) | Q1-1, Q1-2 landed (or Q1-0 shows no hole); **owner approval** | Recorded decision; R6-gen's unit plan cites it | Documentation only: diff and reference review | docs +8 / −2 | 1 | one review (DECISIONS keeps its review, AGENTS documentation row) | no |
| Q1-4 | Delete `AnalyzeInstance` and the instance machinery | R6-gen | Old Analysis: `A/OwnershipAnalysis.Instances.cs` (pool, `instance`, `FailedInstance`, `InstanceStorageLimit`, `ClearInstances`, `AnalyzeInstance`, `Resolve`'s instance branch), `this.instance` branches (`Borrows.cs:509`, `Closures.cs:218`, `Pointers.cs:14`, `OwnershipAnalysis.cs:131-132, 138, 449, 477-480, 512, 1672`), the instance half of `OwnershipModel.cs:361-363, 384-399, 503`. Tests: `GenericPointerEmissionTest:94-99`. `docs/dev/DIAGNOSTICS.md:56` rewritten; PLAN G69 closed | The R6-gen switch unit has deleted `LlvmEmitter` (the only callers), or is the same unit; Q1-3 | No `AnalyzeInstance` symbol in src or tests; behavior unchanged (no caller remained) | Unit: old-Analysis classes, ArchitectureRulesTest, GenericOwnershipCaseTest/PairFollowTest (cases share `Substituting`); covered by R6-gen's Session | −150 to −200 src, −10 tests (H) | 2: deletion; care with `InstanceBinding`, which case runs also use | one review (or the R6-gen group's shared review, Q6(2)) | no |
| Q1-5 | A5 rule rows for X1 and abstract temporaries | early-R6-prereq (R4 stream) | R4-owned test of A5: `tests/xUnitTest/Tests/MirBorrowsTest.cs` (follows `MirBorrows`). Rows from §2.3/§2.4 plus concrete counterparts (the `r4-u7-rejected` R9 shape) | A5 1b-core merged | A5 rejects both definitions; the concrete `Array<i32>` counterpart is accepted | Unit: MirBorrowsTest | +20 tests | 1: adding rows; A5's rule already exists | covered by the A5 family's shared review (Q6(2)) | no (an R4-owned file, not shared) |
| Q1-6 | Instance capability parity before the codegen switch | R5 | Tests: `GenericGenerationDiagnosticTest`'s i128 rows (`:16-70, 103-114, 161-180`) gain the R5 expectation (the instance-stage capability record at `value / 2` with an "instantiation" related location) in the per-pipeline expectation form R5 adopts. The storage-limit rows (`:83-101, 116-148`) are marked for deletion at R6-gen (old-instance storage) or re-pointed at the collector's Resource record | R5's instance-stage capability pass (OD4) and its DEC entry; R5's per-pipeline expectation form | Class C covered by R5 before R6-gen; the old rows deleted in the R6-gen switch | Unit: GenericGenerationDiagnosticTest under both pipelines | +30 / −0 now, −80 at R6-gen (H) | 2: depends on the OD4 publication decision (check versus build) | one review | no (tests only; the capability pass belongs to R5's own files) |
| Q1-B1 | *(Fallback, only if the owner keeps OD10)* Design: check-only instance driver from R5's collector | R6-gen (design before the switch) | Two independent designs of: the collector exposing per instance (`MirDefKey`, representative unerased Subst, requesting `SourceRef`); an old-side `OwnershipAnalysis.CheckInstances` building a pooled `CallPlan` from a Subst (fidelity: `DeclaringType`/`BoundContract`, `self`), run at build before MIR codegen; publication through `ReportInstanceDiagnostics`; rule for duplicate i128 records | R5 collector landed; owner declines Q1-3 | DEC entry (temporary structure, removed at R6-check) and the reviewed design | Documentation only | docs +20 | 3: cross-phase; three fidelity gaps (§3 b) | full process | possibly: only if the representative Subst column lives in a shared Mir file such as `MirProgram` (H) |
| Q1-B2 | *(Fallback)* Implement the check-only driver | R6-gen | Old Analysis (`OwnershipAnalysis.Instances.cs` gains `CheckInstances`), Core (`Compilation.Pipeline.cs` calls it before generation), R5 collector column; `GenericGenerationDiagnosticTest` migrated to the driver; X1 and abstract-temporary reproducers asserting build failure | Q1-B1 | Today's build-time refusals preserved under MIR codegen until R6-check | Unit: GenericGenerationDiagnosticTest, GenericPointerEmissionTest, the reproducers; Session at the end of the working session | +150–200 (H), all removed at R6-check | 4: Subst→CallPlan fidelity, warm allocation (pooled `CallPlan`), duplicate records | full process (final review) | possibly (see Q1-B1) |

Fallback (d) needs no unit: it is an owner decision to reorder R6-gen after R4's exit, recorded in DECISIONS and the ARCH R6
row, together with R6-check unit a.
