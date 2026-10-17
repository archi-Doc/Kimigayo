# Q3: Instance key, collector, and collection before acceptance (OD4)

Base: checkout `dev` = `5c42b577` (clean). Documents were read from `origin/dev` = `2aac946b`. For Binding/Core/Mir code I read
the origin/dev version wherever it differs (`git diff --stat 5c42b577 origin/dev` changes `TyCtxt.cs` by 3 lines (a doc
comment), `TyCtxt.Effects.cs` by `Published`'s tuple, and 8 Mir files; none of the sites below moved). The pending branches
`claude/a5-1b-mir` and `claude/a5-1b-core` touch only `MirTypes.cs` (a new `SelectsByValue`), `Binding.Enums.cs` and
`Binding.Expressions.cs` among the files this design reads (`git diff --stat origin/dev...<branch>`). Neither touches
`MirAnalysis.cs`, `MirProgram.cs`, `TyCtxt*.cs` or `Compilation.Pipeline.cs`.

The report is read-only: no build, test, or native run. `(H)` marks an inference I did not verify by running code.
`C/` = `src/Kimi/Compiler/`.

## 0. Answer in brief

- **The key.** Use `Instantiation(MirDefKey Def, Subst Erased)`, where `Erased` comes from a new canonicalizer
  `TyCtxt.Erase` (Binding layer, memoized).
  - `Erase` drops every Origin. It sets `Origin` to null and `OriginArguments` to empty, and empties `Subst.Origins` and
    `Subst.Inputs`.
  - It also erases closure contexts recursively. That requires the A5 plan's start condition: `BoundType.ClosureContext`
    becomes a `Subst` instead of a `CallPlan` (design A/C1 below).
  - Each instance keeps a **representative unerased Subst** (the first request, in a deterministic order). Every
    proof-based Binding view runs on it: witness, `ContractType` normalization, Copy/Sealed, override applicability.
    Those views take Origins as input (OJ3). The erased form serves only identity, Layout and codegen ("erase late").
  - Binding's own Types, which OJ5 and Hover read, are never replaced.
- **The collector.** Make it one Backend component (`Backend/Instances/`), run in `Compilation.BuildMir()`'s chain after
  `MirAnalysis.Run`, so that it runs before acceptance.
  - It owns the checked set (SPEC §8.15.3) with one worklist, edges per MIR form, and the four limits judged in one
    `Admit`. Each limit is located at the requesting call with the existing `GenerationResourceLimit_Kd`.
  - A3b and the instance-stage capability check are its passes, through one capability function.
  - The "generated" column is a mark pass at emit time from the output roots over the recorded edges. Test discovery
    therefore stays after acceptance.
- **Records.** Collector records join the MIR records that R3-1 already publishes under `mir`, in the ControlFlow
  partition. There they already decide mir acceptance (`Compilation.Pipeline.cs:34-42`); PLAN10 §7's "P11–P12 → Emission"
  yields for collection.
  - On the default pipeline, they publish when MIR becomes mandatory. I recommend R6-gen (an owner decision against
    OD10's stage split), so the 20 instance-failure tests flip once.
- **The requirement witness by Subst.** Extract the decision part of the call-keyed `InstantiateRequirementCall` into one
  resolver that returns `(Target, Subst, BasePath)` or a refusal, exposed as `TyCtxt.Witness`. The old CallPlan
  materializer calls the same resolver. Origin translation is not needed for the key.
- **Units.** 10 units plus one conditional early unit, listed in §10. The cross-phase ones are U-K0, U-K1, U-K2, U-K3,
  U-K6, U-K7, U-K9 and U-K10. None needs a hold on a shared MIR file except an optional `MirProgram.StaticInit` helper
  (U-K4).

---

## 1. The instance key

### 1.1 How identities are interned today (evidence)

| Object | Interning / identity | Evidence |
| --- | --- | --- |
| `BoundType` | Hash-consed in `TyCtxt.Intern`, the only constructor site besides static primitives (`grep "new BoundType(" C/` = 1 hit, `TyCtxt.cs:186`). Equality is reference identity (`BindingModel.cs` `Equals => ReferenceEquals`). The key includes `ClosureContext` by reference (`TyCtxt.cs:132,159`). | `C/Binding/TyCtxt.cs:83-189` |
| `Subst` | Interned by `TyCtxt.Intern(target, self, declaring, types, lengths, origins, inputs)`; identity is equality. `Origins`/`Inputs` are part of the key. | `TyCtxt.cs:292-341, 347-373` |
| `BoundOrigin` | Not interned in general. Compared by reference everywhere (`TyCtxt.cs:142-146`). Two `new BoundOrigin` sites (`Binding.Origins.cs:243,648`). | `Binding.Origins.Model.cs:113-150` |
| `ClosureContext` | A **synthetic `CallPlan`** created in `CloseClosureTypes`. It is deduplicated by a linear scan over **all** contexts ever made, keyed by (closure function, target, ReturnType, declaring, Type args, lengths, Origins, input Origins). The list `closureContexts` is never cleared: only `functionItemContexts` is cleared per pass (`Binding.cs:198`; `git grep closureContexts` = 3 hits, all in that file). (H) The list holds `FunctionKoto` references from earlier passes and grows over an LSP session. | `Binding.ClosureInstances.cs:9, 15-80, 54-71` |
| Function item context | `FunctionItemContext(item)` creates and caches a `CallPlan` per Item Type and **mutates** storage (`PrepareInstantiatedStorage`). An ordinary item's context has no Origins (`Set(..., lengthArguments)` without origins). | `Binding.FunctionItems.cs:61-116` |

Readers of `ClosureContext` (`git grep ClosureContext origin/dev -- src tests`):

- **Behavioral readers.**
  - `TyCtxt.SubstOfClosure` (`TyCtxt.cs:220`).
  - `Binding.ClosureSignature` (`ClosureInstances.cs:11-13`, through the `CallPlan` overload of `InstantiateStorageType`).
  - Hover `TypeIdentity` (`Checking/Hover/HoverBuilder.cs:601-603`, `CallIdentity(context)`).
  - Old Emission: `GenericStoragePlan.cs:312` (passes the CallPlan to `PrepareEntry`) and `BodyLowering.Closures.cs:84`
    (a `GenericCalls[context]` lookup).
- **Reference comparisons**, textually type-agnostic: `MirTypes.cs:84`, `Binding.CompleteTypeIdentity.cs:27`,
  `Binding.OpenOrigins.cs:84`, `Binding.TypeRelations.cs:48,143`, `Binding.FunctionTypes.cs:257`.
- **Rebuild sites** that pass `type.ClosureContext` into `types.Intern` unchanged: 17 sites in Binding.
- **Tests:** `TyCtxtTest.cs:85` (`AClosureIsInItsInstancesSubstitution`) and `HoverProjectionTest.cs:66`
  (`HiddenClosureContextsKeepLengthSubstitutionsInAgreement`).

There is no Origin erasure anywhere today. `grep EraseOrigin|WithoutOrigins|StripOrigin` finds nothing, and
`MirTypes.Agree` (`MirTypes.cs:66-100`) and `Binding.SameModuloOpenOrigins` (`Binding.OpenOrigins.cs:74-110`) are
predicates, not canonicalizers.

### 1.2 What the SPEC allows, and the constraint the surveys missed

- **Origins are not part of instance identity.**
  - SPEC §8.13.3: "The selection key is the original declaration with its normalized static arguments, excluding only
    Origins" (`docs/spec/08-...md:912`). `SelectSpecialization` compares with `SignatureEquals`, which never reads
    `Origin`/`OriginArguments` (`Binding.cs:277-320`, `Binding.Specializations.cs:341-357`).
  - SPEC §22 gives the static storage key "with Origins erased" (`docs/spec/22-...md:374`).
  - The old emitter's own comment says: "SPEC 15.3.7, IMPL 21.3: Origin differences alone never duplicate generated
    code" (`Binding.OpenOrigins.cs:74-75`).
  - The witness mapping is fixed per (Type, bound Contract reference): "Equal bound references of one Type are
    duplicates, whatever their conditions", and the mapping "is fixed by the conformance" (`docs/spec/08-...md:360-362`).
- **But proofs read Origins.**
  - PLAN10 §5.5 OJ3 keeps "Origin-involving Constraints (Owned, Callable, Type identity)" in applicability.
  - Today `InstantiateRequirementCall` proves the conformance (`ConformanceWitness` → `ResolveConformance(...) ==
    Proven`, `Binding.RequirementInstances.cs:132-139`).
  - `TyCtxt.Apply` → `InstantiateStorageType` normalizes projections in the target's Constraint scope (`ContractType`,
    `Binding.Structs.cs:23-35`).
  - Copy, Sealed and `VirtualApplicability` are proofs.
  - (H) A conditional conformance or member under an Owned premise can be unprovable for an erased `ref/T` (Origin null)
    yet provable for the original `ref/T during static`. Computing an instance *from* an erased Subst could then fail
    spuriously or normalize a projection differently.
- **Consequence: erase late.** Resolution runs on a representative unerased Subst. The erased Subst is the key, and
  `Erase(Apply(t, rep))` is the Type that Layout and codegen see.
  - Why the representative choice does not matter: `Erase(Apply(t, s1)) == Erase(Apply(t, s2))` whenever
    `Erase(s1) == Erase(s2)`, and selection and witnesses are Origin-independent for programs the caller's premises made
    valid (§8.15.1: "A generic call proves its callee's requirements from the caller's premises").
  - This is (H) argued from SPEC. U-K2 and U-K3 check it in shadow: for each instance, the view results on the
    representative are compared with the call-keyed old results.
  - Object payloads are Owned (non-static erased views are deferred, SPEC App. D line 47), so VTable applicability under
    Owned premises does not split one erased payload Type (H).

### 1.3 Two candidate designs

**Design A (recommended): erased interned Types plus Subst closure contexts.**

1. **C1: `BoundType.ClosureContext : Subst?`.**
   - `CloseClosureTypes` interns the instance Subst through a new internal TyCtxt factory, replacing the CallPlan creation
     and the linear scan.
   - `SubstOfClosure` becomes `closure.ClosureContext ?? Identity(binder)`, and `ClosureSignature` becomes
     `types.Apply(closure.Signature, context)`.
   - Hover identifies the context by a content key `SubstIdentity`, which it must, to keep `HoverAgreement` across
     compilations.
   - The two old-Emission sites get their per-closure `CallPlan` from one transitional bridge,
     `Binding.ClosureCall(BoundType closure)`, memoized per closure Type and marked `Transitional("R6-gen")`. This is a
     D5 mechanical follow-up that the R5 plan must name.
   - The 17 rebuild sites and the six reference comparisons do not change textually.
   - Identity semantics are preserved: a Subst holds the same fields the CallPlan dedup compared, except ReturnType, which
     is a function of them (H). Keep the result instantiation as the failure check so behavior is unchanged.
2. **`TyCtxt.Erase(BoundType)` and `TyCtxt.Erase(Subst)`** in a new partial `TyCtxt.Erasure.cs`, memoized in two
   dictionaries that live as long as TyCtxt's interning.
   - Canonical form: no Origins anywhere (`Origin` null, `OriginArguments` empty, `Subst.Origins`/`Inputs` empty), closure
     contexts erased recursively. It is idempotent.
   - Empty Origin spans are safe for `Apply`: `SubstituteStoredOrigin` bounds-checks every slot and returns the Origin
     unchanged (`Binding.Storage.cs:334-370`).
   - A nongeneric callee then erases to `Identity(binder)`, the same key a root uses.
3. **`Instantiation(MirDefKey Def, Subst Erased)`** (Backend) is a record struct with reference equality on the interned
   Subst, so a lookup is O(1).

**Design B: a comparer key with no Binding change.**

- `Instantiation(MirDefKey, Subst rep)` with an `IEqualityComparer` that hashes and compares Types and Substs modulo
  Origins (a hashing extension of the `MirTypes.Agree` walk), comparing closure contexts through `SubstOf(context)` modulo
  Origins.
- Pros: no change to Binding, Hover or old Emission, and no R7 item pulled forward.
- Cons:
  - Every lookup walks Types. Interned identity is gone, so each lookup costs O(type size) and needs a per-Type hash memo.
  - Every Backend table (Layout, DropGlue(Ty), VTable(Ty), ObjectDescriptor(Ty), Abi) needs the same comparer. That is
    an open family of "modulo Origins" keys, the AGENTS symptom "a second copy of a rule".
  - Layout memoization by reference is lost.
  - The closure-context linear scan stays.
  - It departs from the A5 plan §9.1 start condition ("`MirDefKey` with an Origin-erased Subst, with `ClosureContext`
    normalized through `TyCtxt.SubstOf`"), which takes an owner decision.

**A variant of A: C2.**

- Keep the `CallPlan` field and make it canonical per (closure function, Subst) through one memo
  `Binding.ClosureContextOf(fn, Subst)`. `CloseClosureTypes` and `Erase` call the same producer.
- Smaller now (about −10 lines) and no old-Emission follow-up.
- But it builds a fact outside R7's target shape (A5 plan §7: "Each fact is built in R7's target shape"), creates
  synthetic CallPlans for erased Substs, and leaves PLAN §3's R7 item open.
- Choosing it departs from the A5 plan start condition (owner).

**Recommendation: Design A with C1.**

- It deletes the scan and the never-cleared list, and gives the Backend one Type representation with reference identity.
  Layout.Of memoizes on erased Types, as PLAN10 §5.7 says: "`Layout.Of(消去済みの型)`", that is, Layout of the erased Type.
- It completes PLAN §3's carried-forward R7 item (`docs/dev/PLAN.md:33` on origin/dev).
- What it must not change:
  - Binding's Types keep their Origins (OJ5's principal substitution and Hover display).
  - `Erase` only adds interned values. It never rewrites existing ones.
  - Hover's identity keeps distinguishing contexts that differ in any binding (pinned by
    `HoverProjectionTest.HiddenClosureContextsKeepLengthSubstitutionsInAgreement`).
  - `SubstOfClosure` stays equal to `SubstOf(call)` for ordinary calls (pinned by `TyCtxtTest.AClosureIsInItsInstancesSubstitution`).

### 1.4 Sites that change (Design A)

| Site | Change |
| --- | --- |
| `C/Binding/BindingModel.cs:517` | `CallPlan? ClosureContext` → `Subst? ClosureContext`. Keep the name so held `MirTypes.cs:84` needs no edit. |
| `C/Binding/TyCtxt.cs:83,132,159,186` | `Intern(..., Subst? closureContext)` |
| `C/Binding/TyCtxt.cs:208-221` | `SubstOfClosure` reads the field. Add an internal Subst factory for closings. |
| `C/Binding/Binding.ClosureInstances.cs:9-80` | Delete the list and the scan. Intern the Subst. Add the `ClosureCall` bridge (Transitional R6-gen). |
| `C/Binding/Binding.ClosureInstances.cs:11-13` | `ClosureSignature` goes through `Apply`. |
| `src/Kimi/Checking/Hover/HoverBuilder.cs:601-603` | `SubstIdentity(context)`, defined in a partial. `HoverBuilder.cs` is about 5,800 symbols by a rough count (H: at or over the 5,000-token limit), so it must not grow. |
| `C/Emission/GenericStoragePlan.cs:312`, `C/Emission/BodyLowering.Closures.cs:84` | `binding.ClosureCall(type)` (D5 mechanical follow-up) |
| `C/Binding/TyCtxt.Erasure.cs` (new) | `Erase(BoundType)`, `Erase(Subst)`, memo |
| `C/Backend/Instances/*` (new) | `Instantiation`, `CheckedSet`, `InstanceCollector` |

**Later deletion candidates enabled by A:**

- `Binding.SameModuloOpenOrigins` (Emission is its only caller: 8 sites; it goes at R6-gen).
- `MirTypes.Agree` could become `Erase(a) == Erase(b)`. That is about −30 lines in a held R4 file, optional, and an open
  issue for R4 (H).

---

## 2. The collector algorithm

### 2.1 Placement and contract

- **Owner.** `InstanceCollector` (Backend layer, R5-owned new files; ARCH "R4 and R5 in parallel").
  - It reads MIR, `TyCtxt` and declaration tables, which matches ARCH's Backend dependency row (ARCH:31).
  - It does not read A2's drop table or borrow state: edges are verdict-independent (see "Drop" below).
  - It is not a `MirAnalysis` pass. The driver's contract says "Must not ... decide acceptance" (`MirAnalysis.cs:11`),
    the driver is shared and next edited by R4's 1b-5, and the layout-obligation pass needs Backend Layout, which Mir
    analyses may not read.
- **Run point.** Make `Compilation.BuildMir()` = build → validate → `MirAnalysis.Run` → `collector.Run` (`Compilation.Pipeline.cs:145`).
  - Every BuildMir reader then sees the checked set. That includes `CompilerPipelineBenchmark`'s stage 3, which keeps
    the fixed stage-name contract (critic §3.5). Its doc row in `src/Benchmark/CompilerPipeline.md` needs an auto update.
  - Like `MirAnalysis`, the collector runs once per program version and skips a program with validator findings.
  - **"A3b's slot in the pass order"** (A5 plan §9.1, line 833) is then: the collector's first pass, after step 4 of
    `MirAnalysis` and before acceptance. The ARCH pipeline table records it (§6).
- **Output:** a `CheckedSet` with these columns:
  - per instance: key, representative Subst, case view index (`MirCases.Views(body, rep)`, one bit for a closed Subst; H),
    parent, depth and the Generated bit;
  - edges: an (instance → instance, runtime) pair list;
  - records: `List<Problem>`, merged per code and location like `MirProgram.Report` (`MirProgram.cs:73-79`).

### 2.2 Roots (the checked set, SPEC §8.15.3)

- **Included:** every nongeneric body of the compilation's own modules (`compilation.SourceModules`, dependencies
  included; H). That is Fn, Getter, Setter, StaticInit, Test, module top-level statements `(null, Fn, module)`, and
  Closure bodies of nongeneric enclosing definitions, each under `Identity(binder)`.
  - Bodies of overrides no instance applies (R4-M28) are nongeneric roots: checked, and not generated (nothing references
    them).
- **Excluded:** the bundled library's bodies. They enter only through edges, as the old analysis reached them
  (`OwnershipAnalysis.cs:1814-1826` `CollectLibraryBody`).
  - Reason: `MirBuilder.Build` builds every library body (`MirBuilder.cs:52-60`), and rooting them would collect the
    library's own instances in every compilation. R4-M12 already says the library produces no problems.
  - This is a planning decision to record in the OD4 entry. The cost is unmeasured (H).
- **"Nongeneric" needs a published fact.** Today it is `FunctionKoto.RequiresInstantiation` (`FunctionKoto.cs:87-89`,
  syntax), which the Backend may not read (ARCH:31). Add a declaration-table view in U-K2, e.g.
  `TyCtxt.Instantiates(BindingSymbol)`.

### 2.3 Edges per MIR form

Write `rep` for the instance's representative Subst. `C(s)` = `TyCtxt.Compose(s, rep)`, and the key is `Erase(C(s))`.

| MIR form | Edge(s) | Evidence of the existing views |
| --- | --- | --- |
| `Call(Fn(sym, s, Arg))` | `Implementation(sym, C(s), 0)` selects a specialization for a closed key (nongeneric, Identity) or sym itself. `Arg > 0` → `(sym, DefaultTemplate, Arg-1)`, else Fn/Getter/Setter by `MirProgram.Callee`. A body-less callee (Body-disposition or `#LibraryImport`) → its legacy row's Kimigayo callees (intrinsics topic; e.g. Dictionary helpers that request `clearEntries<K,V>`). | `Binding.Specializations.cs:226-245`; `MirProgram.cs:58-59` |
| `Call(Requirement(req, s, Contract))` | `TyCtxt.Witness(req, C(s), Contract)` (§5) → Fn edge, built-in shim item, or located `Unsupported_Kd` (OCC-X) | — (new, U-K3) |
| `Call(Virtual(slotFn, s))` | none directly. Implementations are reached through VTable(Ty) items made at object creation (below). | R4-M41 |
| `Call(Value)`, operand Type `t = Apply(type, rep)` | Closure Type → `((closure, Closure), Erase(SubstOfClosure(t)))`. FunctionItem → item target: ordinary items through a pure item Subst; requirement items through `Witness`. Function Type → none (runtime). | A4 does the same: `MirEffects.Solve.cs:320-337` |
| `Aggregate(Closure)` | the closure body instance. Creation alone requires it, as the old `PrepareClosures` did (`GenericStoragePlan.cs:299-320`). | — |
| `Const(FnItem)` | the item's target instance (old `PrepareFunctionItems`, `GenericStoragePlan.cs:258-297`) | — |
| `Cast(Erasure)` from Closure / FunctionItem | `Shim(ClosureErasure, Ty)` / `Shim(ItemAdapter, Ty)` items plus the body instance (shims topic) | `MirBuilder.Values.cs:184-188` |
| `Drop(place)` terminator | `DropGlue(Erase(Apply(placeType, rep)))`. Glue edges come from a `MirTypes.Destruction` visitor: a struct's destructor `Fn(AdtDef.DestructorSymbol, Synthesized(destructor, type, []))` with no field cleanup (R4-M6b); an owning handle's payload glue (Sealed through `TyCtxt.Sealed` on rep); Array/Dictionary/string storage and element glue. **The A2 verdict is ignored** for the checked set. | `MirTypes.Destruction.cs:68`; A4's `Destroy`, `MirEffects.Solve.cs:409-430` |
| `Intrinsic(MakeObj/MakeRc/MakeArc/Clone, s)` | `ObjectDescriptor(payload)` + `VTable(payload)`. Entries come from `VTableDef` and override applicability Proven on rep (another topic's views). | old `ObjectGenerationPlan.cs:71-155` |
| `Intrinsic(TypeTest, TypeToken)` | ObjectDescriptor of the tested Type (runtime type identity) | old `ObjectGenerationPlan.cs:94-120` |
| `Intrinsic(DestroyValues, s)` | `DropGlue(T)` | `MirDescriptors.cs` row |
| `Intrinsic(StaticEnsure, [Const])` | `StaticInit` item `(body.Statics[op].Symbol, StaticInit)`. This is the third copy of this lookup (`MirEffects.cs:160`, `MirBuildTest.cs:39`). Share a `MirProgram.StaticInit(body, operand)` helper (shared file, hold) and leave A4's switch to R4 as an open issue. | `MirBuilder.Places.cs:140-155` |
| Format intrinsics | none. The builder emits the adapter calls as ordinary callees; per-instance estimates are codegen's (R3-10). | `MirBuilder.Formatting.cs:24-133` |

- **The runtime bit.** Each edge carries one: the requesting block is reached by runtime edges from the entry (`MirReach`
  without CheckTarget, cached per body and version). Codegen emits nothing for check-only blocks (R4-M37), so "generated"
  follows runtime edges only. The checked set uses all edges, check-only blocks included, as PLAN10 §5.7's instance-stage
  scan says.
- **Per-body edge templates.** Once per version, list each body's edge sites (and its Subst-dependent capability sites,
  §2.6), so one instance costs O(edge sites), not O(body).

### 2.4 Generated column

- **Today:** every nongeneric source body is generated (`LlvmEmitter.cs:124-137, 222-237`), plus the instances they
  reach.
- **Target:** IMPL §21.3.1 generates each closed instantiation "that the output requires"; PLAN10 §5.6.6 gives the roots
  as the startup closure, Library public generation roots, and `#Test` + `Shim(TestEntry)` in test builds.
- **Recommendation:** compute Generated at emit time with one mark pass from the output roots over the recorded runtime
  edges.
  - Test discovery stays after acceptance (`Project.cs:510-515`), and the checked set does not depend on it, because
    `#Test` bodies are nongeneric roots anyway.
  - Codegen emits exactly the Generated instances. A codegen request outside the collected set is
    `InternalInvariant_Kd`, so codegen never re-decides.
- **IR shape:** unused nongeneric functions disappear from the MIR IR. Native behavior is unaffected; IR-shape
  assertions may notice (H). Classify those under OD-d (planning decision; it touches the owner's IR-assertion
  classification).

### 2.5 Limits, judged in one `Admit(def, rep, parent, site)`

| Limit | Value (kept from the old path) | Old evidence | Judgment |
| --- | --- | --- | --- |
| Growing key (polymorphic recursion) | 16 instances of one `MirDefKey` on the ancestor chain | `GenericStoragePlan.cs:17, 360-376` | Walk the parent chain (≤ depth bound) |
| Context depth | 128 | `GenericStoragePlan.cs:18, 396-400` | `parent.Depth + 1` |
| Substitution set per body | 1,024 distinct erased Substs per `MirDefKey` | `GenericStoragePlan.cs:19-23, 447-452` (thread-static test override) | Per-def counter |
| Storage depth | `TyCtxt.Apply` returns null past depth 64 (G128) | `Binding.Structs.cs:53-58`; origin/dev PLAN G128 | `Apply == null` on a closed composed rep is this limit. G128's removal condition (R7) is the removal condition. |

- **The record.** `GenerationResourceLimit_Kd` (Resource, no arguments, `DiagnosticCode.tinyhand:774-776`) at the
  requesting call's `body.Subject(terminator.Source)` (`MirBody.cs:289`), as IMPL §21.3.2 requires ("at the affected
  declaration, call or token").
  - Inside an Inlined scope, the site is the omitting call (H).
  - It is recorded once per (code, site). The instance is not admitted, and the requester continues, so independent
    errors stay visible (SPEC §23.1.2).
  - DIAGNOSTICS:306 already says virtual descriptor dependencies share this code. Today it is reported at
    `c.Project.FilePath` with a free-text note (`LlvmEmitter.cs:67-79`), so it becomes located.
- **Default evaluators** need no limit of their own: `DefaultGenerationPlan.cs:94-99`'s special limits disappear, since
  recursive defaults are ordinary `Fn(DefaultTemplate)` instances (PLAN10 §5.7).
- **Layout limits** (`AggregateLayout.cs:47, 171-175`) belong to Layout. U-K7's layout pass reports them through the same
  recorder and code.
- **The G128 interaction:**
  - a finite deep Subst (`pick<Option^32<ref/i32>>`) becomes a Resource record at check from the publication stage on;
  - (H) whether the old *build* accepts it is unverified: old Emission also calls `InstantiateStorageType` (6 sites);
  - U-K2 adds the reproducer. If the old build succeeds, it is a regression, and G128's removal moves before the
    publication stage (conditional unit U-KG).

### 2.6 Capability passes (A3b and the instance stage)

- **One function, `Capability.Check(operation, types)`**, reached in two passes (PLAN10 §5.7; DECISIONS R4-M8 "A3b moves
  to R5 with the single capability table"):
  - **A3b:** every body once, generic or not, check-only blocks included. It covers operations whose operand Types are
    closed in the body (no `ContainsParameter`).
  - **Instance stage:** per closed instance, only the Subst-dependent sites.
- **Records:** `Unsupported_Kd` once per location.
  - Skip Opaque operands: Binding-rejected nodes are already Opaque, so Binding's definition gate
    (`Binding.Expressions.cs:1382-1386`) and A3b never duplicate (`MirBuilder.cs:403-412`).
- **Scope:** today's capability gaps are i128 `/` `%` and their compound forms (`OwnershipAnalysis.cs:1454-1459`,
  `OwnershipAnalysis.Values.cs:134-137`) and i128↔f32/f64 conversions (IMPL §21.3.3). Layout capability rows come with
  U-K7.

### 2.7 Scaling and warm zero allocation

- **Scaling.**
  - Work is Σ over instances of the edge sites per body. Each edge costs one `Compose` (scratch arrays,
    `TyCtxt.cs:22-25, 249-251`), one memoized `Erase`, one O(1) dictionary probe, and an `Admit` of at most 128 steps.
  - The old path was O(calls × entries) by linear scans (`GenericStoragePlan.cs:434-443`, `DefaultGenerationPlan.cs:71-84`,
    `ClosureInstances.cs:54-63`).
  - Tests: a 2×/4× xUnit scaling check on work counters (pattern: `MirEffectsTest`/`MirAnalysisTest` scaling cases), and
    the existing `--compiler-pipeline` Generic shape 1/32/64/128 (`src/Benchmark/CompilerPipeline.md:4-9`) for time, now
    inside stage 3.
- **Warm zero allocation.**
  - Pooled struct arrays for instances and edges, a `Dictionary<Instantiation,int>` cleared with its capacity kept, an
    int worklist, and per-body templates reset by counts.
  - `Erase`'s memo and `TyCtxt` interning hit on a warm run.
  - `Apply` uses Binding's rented scratch (`InstantiateStorageType`).
  - Assert 0 bytes for a warm `BuildMir` after a same-shape rebind (extend `MirBuildTest.cs:149/200/284`), tagged
    `Purpose=Allocation`.
  - Retained memory: the erased Types and the memo grow with distinct Types. Measure and record this under the
    2026-10-11 allocation policy (AGENTS).

---

## 3. OD4 plumbing

### 3.1 Partition, severity and acceptance

- **The acceptance formula.** `binding.IsComplete && startup.IsComplete && analysis.IsVerified &&
  !HasErrorsThrough(Ownership)` (`Project.cs:510`). `HasErrorsThrough` counts Error-severity records in partitions
  Input..Ownership (`DiagnosticOwner.cs:80-91`; severity decided by the catalog, `DiagnosticCollection.cs:250`).
- **Resource codes reject.** Every Resource code defaults to severity Error: "Severity defaults to Error"
  (`DiagnosticCode.tinyhand:1`), and `GenerationResourceLimit_Kd`, `OwnershipStorageLimit_Kd`, `DefaultExpansionLimit_Kd`
  and `OwnershipCaseLimit_Kd` have no Severity line (`:774-797`).
  - SPEC §23.2.2: a result "is accepted when its outcome is Completed and it reports no Error". So a Resource record at
    check rejects. That is legal ("never rules the program invalid" is about category, SPEC §23.1.1), and existing check-time
    Resource codes already do it.
  - **Correction:** DIAGNOSTICS:292's "Resource and output limits do not change acceptance (DiagnosticContractTest)" sits
    in the *Bounded excerpts* bullet and concerns `DiagnosticLimits` (rendering bounds: `DiagnosticContractTest.cs:56, 255,
    335-353`), not Resource-category diagnostics. No special case is needed.
- **Partition.** Collector records go in the **ControlFlow** partition with `DiagnosticRequirement.ControlFlow`, through
  the existing MIR-record path (`Compilation.Pipeline.cs:98-109`).
  - This is R3-1's rule ("published only under the mir pipeline, in the ControlFlow partition", DEC:379-380) and PLAN10
    §7's mapping for A3b ("P6 and P7 (A0–A3b) → ControlFlow", PLAN10:501).
  - PLAN10 §7's "P11–P12 → Emission" yields for collection records. Codegen failures stay in Emission
    (`InternalInvariant_Kd`, toolchain, output).
  - No new requirement vocabulary is needed: R4-M3a derives published requirements at R6-check.
- **Plumbing.** `Compilation.MirRecords` (`Compilation.Pipeline.cs:163-179`) also adds `checkedSet.Records`.
  `Analysis.IsVerified` (`:34-42`) and `Generates` (`:154-159`) then count them with no other change. `Project.cs` needs no
  reordering.

### 3.2 Test discovery, `kimi test`, Library projects, cost

| Topic | Fact | Consequence |
| --- | --- | --- |
| Test discovery | `Tests.Discover` runs only after acceptance (`Project.cs:511-515`) and in `LlvmEmitter.TryPrepare` (`:121-124`) | No change. The checked set does not need test roots, and Generated is computed at emit (§2.4). |
| `kimi test` | On generation failure it records only `project.Name + ": " + failure` (`TestCommand.cs:131-135`) | Once collector records publish at check on the default pipeline, these failures reach the test command as located check diagnostics. The text path remains only for codegen/toolchain failures. |
| Library projects | `Build` never emits a Library (`Project.cs:212-224`) | Under OD4, a Library's instance-stage Unsupported and Resource now fail its check and build. SPEC §23.2.2 leaves it implementation-defined whether check reports Unsupported; OD4 places the capability pass before acceptance; IMPL §21.3.3 requires a report "for every instantiation of the checked set". Ledger class (d). A rejected library or milestone program is O3. |
| Check cost | The collector adds work to every `check` and every LSP edit | Library bodies are not roots (§2.2). The checked set equals the user program's instance closure. Measure stage 3 on the Generic shape and the warm LSP Analyze (H: small next to building and analyzing every library body). |

### 3.3 Tests that change

These are the methods that read generation-time instance facts (`grep Emission.FailureInstance|FailureIsResourceLimit|
Emission.ReportFailure|Ownership.FailedInstance|Ownership.AnalyzeInstance|Ownership.ClearInstances|SubstitutionSetLimit|
DiagnosticPartition.Emission|GenerationResourceLimit_Kd`): 19 methods in 9 files, plus 1 CLI method the regex misses, 20 in
all.

| File | Methods |
| --- | --- |
| `GenericGenerationDiagnosticTest` | `InstanceFailuresAreLocatedOnly` (4 rows, asserts check accepts and `WriteIr` fails, `:16-70`), `IndependentUnsupportedOperationsRetainSeparateLocations`, `InstanceStorageLimitsKeepResourceFactsAndRecover`, `SuccessfulInstancesKeepTheExistingAllocationFreePath`, `TypeAndLengthSlotsAppearOnceInDeclarationOrder`, `WarmSuccessfulInstancePreparationAllocatesNothing`, plus the CLI `GeneratePublishesAnUnsupportedOperationInsteadOfAnInternalFailure` (`:161-180`) |
| `GenericDestructorEmissionTest` | `DestructorSubstitutionsRespectTheGenerationLimit`, `GrowingDestructorContextsAreResourceDiagnosed` |
| `GenericStorageEmissionTest` | `FiniteGenericRecursionReusesItsInstance`, `GrowingSubstitutionKeysAreResourceDiagnosed`, `OversizedSubstitutionSetsAreResourceDiagnosed` |
| `InlineLayoutTest` | `EnumTagAndAlignmentCountTowardsTheSizeLimit`, `FiniteLayoutsBeyondTheInternalSizeOrCountBoundAreResourceLimits`, `NestingBeyondTheDepthBoundIsAResourceLimit` |
| `OwnershipStorageLimitTest` | `ConcreteInstanceLimitsRemainResourceFailuresAndRecover` (old-instance storage; deleted with `AnalyzeInstance`) |
| `VirtualGenericDispatchTest` | `DescriptorDependenciesReuseFiniteCyclesAndRejectGrowingKeys` |
| `GenericObjectFactoryTest` | `FactoriesKeepDestructorExpansionBounded` |
| `GenericPointerEmissionTest` | `VerifiedTemplateBuildsDependentConcreteReads` |
| `RecursiveDefaultTest` | `GrowingDefaultSubstitutionsAreResourceDiagnosed` |

- **When they change.** In R5 they keep their old expectations on the default pipeline. Under `KIMI_PIPELINE=mir` the
  i128 and Resource rows join the mir measurement's failure set, classified (d) with SPEC clause (§23.2.2, §8.15.3, IMPL
  §21.3.3; R4-M46's clause column). They are rewritten once, at the publication stage (U-K9): check rejects with located
  records, and the old `c.Emission.*`/`c.Ownership.FailedInstance` reads disappear.
  - `ArchitectureRulesTest` ratchets fall: "generation failure facts ≤ 21" and "instance analysis reads ≤ 11"
    (mir-state §4).
- **New collector tests use the MIR entry explicitly** (`c.BuildMir()` / `c.Pipeline = Mir`, like `MirBuildTest`), so
  they do not depend on `KIMI_PIPELINE`.

### 3.4 Keeping check and build agreed

SPEC §23.2.2 says an accepted program "never fails `emit`, `build` or `run` with a Language or Proof error", and §8.15.3
says layout obligations are checked for the whole checked set "reported by checking". The rules that keep the two agreed:

1. Every instance-stage judgment is a collector pass before acceptance: capability, layout obligations and limits.
2. Codegen reads only the collected and Generated instances, and re-checks nothing.
3. A codegen refusal is `InternalInvariant_Kd`. `GenerationFailed_Kd` is deleted with the old Emission (PLAN10 §5.7).

Evidence: R5 evidence part (3) (A5 plan §9.1: equal stdout/stderr/exit across pipelines). In addition, a session check
asserts that every check-accepted fixture or milestone generates MIR IR without records (U-K8).

---

## 4. Publication by stage

| Stage | Collector runs | Its records | Rationale |
| --- | --- | --- | --- |
| R5 (shadow) | under `mir` only (the pipeline that builds MIR, `Compilation.Pipeline.cs:90`) | **published under `mir`**, ControlFlow, deciding mir acceptance, like construction records | R3-1's rule covers exactly these codes (located `Unsupported_Kd`, a Resource code). R4-M1's reason ("duplicate records and test churn twice", DEC:405-408) does not apply: the old path reports instance failures only at build, which a rejected mir check never reaches. It needs no generation-time publication path. The codegen selector reads only accepted programs. |
| R6-gen (MIR codegen default; MIR mandatory) | always | **Recommended: published on the default pipeline at check**, together with construction records and validator findings (critic §4.4) | The old Emission and its instance facts are deleted here, and the 20 tests must be rewritten anyway. Rewriting them once onto check-time records avoids a second rewrite at R6-check. Owner decision, because OD10 puts "MIR decides acceptance" at R6-check. |
| R6-check | always | published. MIR analysis problems join (R4-M1 ends). Construction and collector records fold into `MirReport`'s derived-requirement rule (R4-M3a) or keep their own merge. | Unchanged by this design |

- **The alternative at R6-gen: gate generation only.** Publish collector records in the Emission partition at emit,
  located, until R6-check.
  - It keeps check results unchanged until R6-check.
  - But it needs a temporary generation-time publication path, and the 20 tests are rewritten twice: onto MIR build
    failures at R6-gen, onto check diagnostics at R6-check.

---

## 5. Requirement witness by Subst

### 5.1 What exists

- **Definition time.** A requirement call with a closed Self is already resolved at definition time in Binding:
  `BindSelectedRequirement` calls `InstantiateRequirementCall` when `!DependentType(self)` and stores the resolved CallPlan
  (`Binding.SelectedRequirements.cs:41-53`). MIR therefore sees a `Requirement` callee only for a dependent Self in a
  generic body (`MirBuilder.Calls.cs:134`), and the collector resolves only those.
- **Call-keyed resolution** (`Binding.RequirementInstances.cs:53-128`) handles these cases:
  1. abstract Self (stays a requirement call);
  2. a property requirement (`InstantiatePropertyRequirementCall`, `Binding.PropertyWitnesses.cs:109`);
  3. a built-in compiler target (`CompilerRequirementTarget`, `Binding.Formatting.cs:122-160`: numeric arithmetic,
     BuiltinEquals/Compare, BuiltinFormat);
  4. fixed-array entries (`Binding.FixedArrays.cs:84`, G32);
  5. integer positions (`Binding.IntegerPositions.cs:59`);
  6. the conformance witness through `InstanceReference` (bound Contract instantiated, `:176-205`) + `ConformanceWitness`
     (`:132-139`), Origin translation (`Translate`, `:120-127`), base receiver projection (`ProjectWitnessCall`,
     `:143-171`), and OCC-X refusal for an exclusive receiver (`:162-165`).
- `TyCtxt.Witnesses` (A4's view, `TyCtxt.Effects.cs:37-38`, `Binding.RequirementInstances.cs:13-50`) returns (target,
  declaring) under a definition's premises, for `ref/Self` requirements, with composite-comparison leaves. It has no
  method Type arguments, base path or property case. Its contract differs ("what may run under premises").
- `TyCtxt.Implementation` covers specialization (closed key → index 0) but not witnesses.

### 5.2 What the collector and codegen need

- The **target symbol** and its **Subst**: Self (none for an implementation), Declaring (the base instance for an
  inherited witness), the call's method Type arguments and lengths, and the translated Origins of the representative.
  Origins are irrelevant to the key.
- The **base path** for the receiver adjustment at the call site (codegen).
- A **refusal** for OCC-X, which becomes a located `Unsupported_Kd` at the requirement call (instance stage).
- For built-in targets, the **item**: a comparison or format shim over Ty, whose leaves come from "comparison plans by
  Type" (A5 plan §7 row), or a numeric intrinsic.
- No new enum is needed: the target's `CompilerFunction` and the Kimi member identify the case.

### 5.3 Options

| Option | Content | Verdict |
| --- | --- | --- |
| W1 (recommended) | Extract the decision part of `InstantiateRequirementCall` and its four helpers into one resolver, `ResolveRequirementWitness(requirement, self, contractRef, scope, types, lengths, origins, inputs) → (Target, Self?, Declaring, Types, Lengths, Origins, Inputs, BasePath)` or a refusal. The CallPlan materializer (old consumers: `SelectedRequirements.cs:43`, `RequirementItems.cs:131`, `Specializations.cs:164`) calls it and copies the operations. `TyCtxt.Witness(requirement, Subst rep, contract, out WitnessChoice)` interns the Subst. It shares `ConformanceWitness`/`CompilerRequirementTarget` with `Witnesses`. | One rule, two consumers. Behavior-preserving extraction first (legacy `.ll` equal), then the view. Size about +110/−60 Binding, +12 TyCtxt. Full process (a Binding fact view crossing phases), or Q6 form 1 if step 0's audit-table schema is owner-approved. |
| W2 | The collector synthesizes a `CallPlan` from a Subst and calls `InstantiateForwardedCall` | Rejected: fake HIR objects (operations, receiver operation, ReturnType), mutation after Bind (A5 plan §11), and the Backend reading Binding internals (ARCH:31). |
| W3 | Widen A4's `Witnesses` to return Substs and serve both | Rejected: different contracts (premises versus a closed instance, several targets versus one). AGENTS "Share by meaning": share only the closed-Self steps W1 already shares. |

**Requirement items** (`FunctionItemContext` → `RequirementItemContext`, `Binding.RequirementItems.cs:131`) go through the
same `Witness` view. **Ordinary items** get a pure Subst from the Item Type: `Intern(symbol, null, ItemDeclaringType,
item arguments, LengthArguments, [], [])`, the same fields `FunctionItemContext` copies (`Binding.FunctionItems.cs:96-110`).
That removes the collector's dependence on the mutating context cache.

**Disposition correction.** R0 marks `Binding.RequirementInstances` "削除 R6" (delete at R6). Only its forwarded-call use
(`Specializations.cs:164`, through `Binding.EffectBounds`) goes at R6-check. The resolver stays for the definition-time
callers.

---

## 6. Records each cross-phase unit carries

| Record | Content | Unit |
| --- | --- | --- |
| DEC "R5-C1" | A closure Type's context is its instance's Subst (PLAN §3's R7 item pulled forward per A5 plan §9.1). The transitional `ClosureCall` bridge for old Emission, removed at R6-gen, named as a D5 mechanical follow-up. Rejected: C2 and Design B. | U-K1 |
| ARCH canonical row "Types and substitutions" | adds "a Closure Type's context is a `Subst` (`SubstOfClosure`)" and later "`Erase`, the one Origin erasure" | U-K1, U-K2 |
| PLAN §3 carry-forward line | drops "`ClosureContext` ... become `Subst` identity (R7)" (auto, no review) | U-K1 |
| **DEC "OD4"** (required in the first implementing commit, DEC R4-M35:955-957) | Instance collection is a Backend phase before acceptance: the checked set, roots (own modules; library by edges), the erase-late key, the four limits and their values, one located Resource code, edges per form, Generated computed at emit. Records published like R3-1's under `mir` in ControlFlow, superseding PLAN10 §7's P11 mapping for collection. The default-pipeline publication stage (owner decision). A3b as the collector's first pass. Rejected: a collector inside `MirAnalysis`, Generated before discovery, Design B. | U-K2 |
| ARCH target pipeline (ARCH:11-21) | P7–P9 loses "definition-stage capability check". P10 is "Acceptance after the instance collection". P11 becomes "Backend: instance collection over the checked set — definition- and instance-stage capability checks, layout obligations, resource limits (before P10, DECISIONS OD4)". P12 becomes "shims, layout/ABI, LLVM text for the generated instances". Keep the phase numbers to avoid clashing with plan §18 P-numbers. | U-K2 |
| ARCH canonical rows | "Instance collection" (`InstanceCollector`, `CheckedSet`). "Capability table" (U-K6). Backend's first file activates the LINQ rule at R5 (ARCH:163-165; the tests extension is the owner's call). | U-K2, U-K6 |
| DIAGNOSTICS | `GenerationResourceLimit_Kd` located at the requesting call (`:306`). Collector records in ControlFlow (`:287` neighbour). `:56` (instance refusals in Emission) and `:285` rewritten at U-K9. | U-K2, U-K9 |
| DEC "requirement witness by Subst" | the resolver contract, its six cases, the OCC-X refusal, and its consumers. Also added to A5 plan §7's table through R5 step 0's audit. | U-K3 |
| DEC capability table | one table and two passes (implements R4-M8) | U-K6 |
| STATUS | the "Generic generation limits" row (§4) and the default-pipeline lag | U-K9 |
| CODEMAP | new Backend rows (auto, no review, Q6 form 3) | each |

---

## 7. Owner decisions

1. **Publication stage on the default pipeline.**
   - Recommended: collector records, together with MIR construction records and validator findings, publish at check from
     R6-gen. The 20 instance-failure tests are then rewritten once, and check/build agree as soon as MIR generates.
   - Alternative: gate generation only until R6-check. Check stays unchanged two stages longer, at the cost of a temporary
     Emission-partition path and a second rewrite. This changes OD10's split, which puts "MIR decides acceptance" at
     R6-check.
2. **Instance-stage Unsupported and Resource at check** (implied by OD4's "its passes", but it changes Library build
   outcomes).
   - Confirm that check reports them for the checked set (SPEC §23.2.2 leaves it implementation-defined).
   - Trade-off: libraries can no longer pass while holding an unbuildable generic instance, but check costs more. Any
     library or milestone program rejected by it is O3.
3. **C1 versus C2** (only if the planning session departs from the A5 plan's start condition). C1 completes the R7 item
   now, with two D5 follow-up sites in old Emission. C2 is smaller now but leaves a CallPlan representation for R7 to
   change.

Planning-session decisions to record in DEC OD4 (no owner gate unless a reviewer escalates):

- the bundled library as edges-only (cost versus scope);
- Generated as reachability (IMPL §21.3.1) versus every nongeneric body (IR-shape churn, which interacts with OD-d);
- the limit values kept at 16/128/1,024/64.

---

## 8. Risks

- **Origin-dependent proofs.** Resolution must use the representative unerased Subst, never the erased one (§1.2). One
  representative per erased key assumes SPEC's Origin-independence of selection and witnesses (H). U-K2 and U-K3 add
  shadow cross-checks against the call-keyed old results over the library and milestones.
- **G128.** A deep finite Subst becomes a Resource record at check. If the old build accepts it (unverified), the
  publication stage regresses, and G128's removal (R7) must move earlier (U-KG).
- **AnalyzeInstance (OD10).** Erased keys cannot feed `AnalyzeInstance(CallPlan)` faithfully.
  - The representative Subst column could drive a Subst→CallPlan bridge, but one representative per erased key analyzes
    fewer Origin variants than the old per-closed-Origin contexts (H).
  - That design belongs to Q1 (`deep/q1-analyzeinstance.md`), which recommends fixing X1 and generic temporaries in the
    old definition run instead.
- **Shared files and parallel streams.**
  - `Compilation.Pipeline.cs` and Binding/TyCtxt files are not covered by the hold protocol (H), and R4's A5 units also
    extend TyCtxt (`TyCtxt.Retention.cs`). Coordinate each unit with a note in PLAN §3.
  - Any `MirProgram.cs` helper needs a hold.
- **The ledger.** Collector records under `mir` add "MIR rejects, old accepts" rows. A rejection of a library or
  milestone program under `mir` blocks the R4 exit (ARCH ledger rule), so U-K2 must show zero collector records on the
  library and all milestones.
- **Hover.** `HoverBuilder.cs` is likely over the token limit (H). U-K1 must keep it from growing by putting
  `SubstIdentity` in a partial and replacing the call one-for-one.
- **Unmeasured check cost.** It is measured in U-K2. If it is significant, per-body edge templates and library
  edges-only are the levers.
- **Immutable after Bind** (A5 plan §11).
  - `TyCtxt.Apply` still mutates storage-preparation caches and interning (monotonic memos).
  - The collector avoids `FunctionItemContext`'s cache by the pure item Subst (U-K3).
  - Named here as required.

---

## 9. Corrections to the survey reports

1. **critic §3.6 and §4.2.** The claim is "Resource records never change acceptance (DIAGNOSTICS:292)". That line belongs
   to the *Bounded excerpts* bullet and concerns rendering limits (`DiagnosticLimits`, `DiagnosticContractTest.cs:56,
   255, 335-353`).
   - Resource codes have severity Error by default (`DiagnosticCode.tinyhand:1`, `:774-797`), and SPEC §23.2.2 accepts
     only results with no Error.
   - A collector Resource record in a partition ≤ Ownership rejects acceptance, as check-time `OwnershipStorageLimit_Kd`,
     `OwnershipCaseLimit_Kd` and `DefaultExpansionLimit_Kd` already do.
2. **instances.md §7.1 and §6** ("intern the erased Subst" as the instance's working Subst). Proof-based views read
   Origins (`ConformanceWitness` → `ResolveConformance`, `Binding.RequirementInstances.cs:132-139`; `ContractType` in
   `InstantiateStorageType`, `Binding.Structs.cs:30-33`; OJ3). The collector must resolve with a representative unerased
   Subst and erase only for the key and for Layout.
3. **instances.md §3.4 and tests-harness §5** ("19 methods in 9 files"). The CLI test
   `GenericGenerationDiagnosticTest.GeneratePublishesAnUnsupportedOperationInsteadOfAnInternalFailure` (`:161-180`) also
   pins generation-time instance failure, so 20 methods change.
4. **R0 disposition of `Binding.RequirementInstances`** ("削除 R6", delete at R6, via instances.md §1.1). `InstantiateRequirementCall`
   also has definition-time callers (`Binding.SelectedRequirements.cs:43`, `Binding.RequirementItems.cs:131`). Only the
   forwarded path (`Binding.Specializations.cs:164`) goes at R6-check.
5. **instances.md §4.4.** It describes `CloseClosureTypes`' dedup, but omits that `closureContexts` is never cleared (only
   `functionItemContexts` is, `Binding.cs:198`). The scan grows across passes and retains earlier passes' `FunctionKoto`s
   (H). Design A deletes it.

---

## 10. Units

| id | title | stage | change (owner, files) | prerequisites | deliverable | verification (AGENTS row) | net size | difficulty | review weight | shared MIR hold |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| U-K0 | OD4 instance-collection design (two independent designs A/B, publication stages, edges table, limits, witness view) | R5 (step 0) | Planning record in R5 step 0 (`draft/Sketches/...`, needs explicit instruction); no code | R5 step 0 opened; R4-M35 OD4 | Reviewed design chosen; owner decisions §7 asked | Documentation only: diff and reference review | docs only | 3: cross-phase design with three stages of publication | full process (two designs, design review) | no |
| U-K1 | Closure contexts as Subst identity (C1) | R5 | Binding: `BindingModel.cs:517`, `TyCtxt.cs` (Intern, SubstOfClosure, closing factory), `Binding.ClosureInstances.cs` (delete scan and list; `ClosureCall` bridge Transitional R6-gen); Checking: `SubstIdentity` in a HoverBuilder partial, call site `:601-603`; Emission: `GenericStoragePlan.cs:312`, `BodyLowering.Closures.cs:84` (D5 follow-up). Behavior unchanged. | U-K0 approved | One closure-context representation; DEC R5-C1; ARCH row; PLAN §3 line | Compiler row: Unit `-Class TyCtxtTest,HoverProjectionTest,MirEffectsTest,MirBuildTest` + closure emission classes; behavior-preserving procedure (diagnostic snapshot, semantic snapshot, legacy `.ll` equal on the closure fixture families); Session at the end | +40 / −40 (H) | 3: identity used across Binding, Hover, old Emission; HoverBuilder at its token limit | full process (shared representation); one design + one review if the owner approves U-K0 (Q6 form 1) | no (MirTypes.cs:84 unchanged textually) |
| U-K2 | Origin erasure and the instance collector core before acceptance (Fn family) | R5 | Binding: new `TyCtxt.Erasure.cs` (`Erase`), a declaration view for "instantiated per Subst"; Backend (new): `Instantiation`, `CheckedSet`, `InstanceCollector` (roots, worklist, Fn/DefaultTemplate/Getter/Setter/closure/ordinary-item edges, `Admit` with four limits, located `GenerationResourceLimit_Kd`, runtime bits, per-body templates); Core: `Compilation.Pipeline.cs` (BuildMir chain, MirRecords includes collector records) | U-K1; U-K0 | First consumer of `Erase` (R4-M2a, no stub); checked set and records under `mir`; DEC OD4 + ARCH pipeline table and rows; DIAGNOSTICS `:306`; CompilerPipeline.md stage 3 row | New `InstanceCollectorTest` (Origin-only variants give one instance; closure-Type args; finite recursion reuse; growing key, set size, depth, deep finite Subst (G128) located at the call; warm 0 B; 2×/4× counters), `TyCtxtTest` erasure cases (idempotent; `Erase(Apply(t,s1)) == Erase(Apply(t,s2))`); zero collector records over library and all milestones; Unit + `ArchitectureRulesTest`; Session with the mir measurement (VERIFICATION:96); slnx build (Benchmark stage doc) | +430 / −0 code (Binding +80, Backend +330, Core +20) + tests ~220 (H) | 4: new component, phase move, warm allocation, limits parity, cross-stream Core file | full process | no (new Backend files; Core coordinated, not a Mir hold) |
| U-K3 | Requirement witness by Subst and requirement/item edges | R5 | Binding: extract `ResolveRequirementWitness` from `InstantiateRequirementCall` + Property/FixedArray/IntegerPosition helpers (behavior-preserving commit first), view `TyCtxt.Witness`, pure ordinary item Subst; Backend: Requirement edges, OCC-X → located `Unsupported_Kd` | U-K2 | One witness rule for old and new consumers; DEC entry; A5 plan §7 row added by step 0's audit | Unit: new witness-view test (view versus call-keyed instantiation over the requirement calls of generic library and milestone bodies: same target, equal erased Subst), `InstanceCollectorTest` requirement cases, `TyCtxtTest`; behavior-preserving commit: snapshots + legacy `.ll` equal (comparison, format, iterator and fixed-array families); Session with mir measurement | +150 / −60 (H) | 4: six cases plus OCC-X must match exactly or native output differs | full process (Binding fact view); one design + one review if the step-0 schema is owner-approved | no |
| U-K4 | Drop, destructor and static edges | R5 | Backend: Drop → DropGlue(Ty) items through a `MirTypes.Destruction` visitor, destructor Fn instances (`AdtDef.DestructorSymbol` + `Synthesized`), payload glue (Sealed on rep), DestroyValues, StaticEnsure → StaticInit; optional `MirProgram.StaticInit` helper | U-K2 | DropGlue/StaticInit items in the checked set; destructor limits located | Unit: `InstanceCollectorTest` (generic destructors, growing destructor contexts, statics); Session | +120 / −0 (+5 Mir if the helper is added) | 3: destruction walk reuse, Sealed/Copy on rep | one review (inside OD4's recorded item kinds) | yes, only if the `MirProgram.StaticInit` helper is added (recommended; A4's copy left to R4) |
| U-K5 | Object descriptors and VTables in the collector | R5 | Backend: MakeObj/MakeRc/MakeArc/Clone/TypeTest → ObjectDescriptor(Ty) + VTable(Ty), entries by `VTableDef` and applicability Proven on rep; inapplicable bodies stay checked-only roots (R4-M28) | U-K2; the VTableDef and override-applicability views (another topic's unit) | Virtual items in the checked set; growing descriptor keys located | Unit: collector tests mirroring `VirtualGenericDispatchTest` (finite cycles reuse, growing keys) and `VirtualConditionalDispatchTest` slot nulls; Session | +120 / −0 (H) | 3: depends on new Binding views; Owned-payload assumption (H) | one review (if the views' unit fixed the contract) | no |
| U-K6 | Capability passes: A3b and instance stage | R5 | Backend: one capability function and table rows (i128 `/` `%` and compound forms, i128↔float), A3b pass over every body, instance pass over Subst-dependent sites; records `Unsupported_Kd` per location; skip Opaque | U-K2 | R4-M8 implemented; DEC capability-table entry; ARCH row | Unit: tests on `GenericGenerationDiagnosticTest`'s sources under MIR (direct, forwarded, remainder, independent locations, compound); unused nongeneric i128 still reported by A3b; no duplicate with Binding's gate; Session with mir measurement (ledger (d) rows) | +150 / −0 (H) | 3: two passes, one rule; duplicate-avoidance with Binding | full process (new canonical component), unless the capability design is owner-approved | no |
| U-K7 | Layout obligations of collected instances | R5 | Backend: by-value uses per instance → `Layout.Of(erased)` obligations: C-layout zero-sized Fields of inferred instantiations (Language, at the use), Layout size/depth limits (Resource); one owner for the C-layout rule shared with `Binding.CheckCLayoutInstance` (`Binding.Attributes.cs:382-403`) | U-K2; Layout.Of (layout topic) | SPEC §8.15.3 obligations at check (on `mir`) | Unit: inferred C-layout instantiation test, `InlineLayoutTest` equivalents; Session | +100 / −10 (H) | 4: Language error from the Backend; the rule is shared with Binding's `HasZeroStride` | full process (cross-phase rule ownership) | no |
| U-K8 | Generated column at emit and codegen consumption | R5 | Backend: mark pass from output roots (startup closure, test roots after `Tests.Discover`, Library public roots) over runtime edges; codegen reads Generated only; outside-set request → `InternalInvariant_Kd` | U-K2; codegen driver (codegen topic) | IMPL §21.3.1 / §21.3.7 output closure | Unit: test builds generate only the test closure; unreferenced instances not generated; a session check that every check-accepted fixture generates without records; Session | +70 / −0 (H) | 2: linear mark over recorded edges | one review | no |
| U-K9 | Instance findings at check on the default pipeline | R6-gen | Core: MIR mandatory; MirRecords (construction, validator, collector) published on the default pipeline; tests: the 20 methods rewritten onto check-time records; old instance facts deleted with Emission; ratchets lowered; DIAGNOSTICS `:56`, `:285`, `:306`; STATUS rows; `GenerationFailed_Kd` deleted | U-K2..U-K8; owner decision §7.1 | check and build agree for instance findings | Session with native fixtures and milestones selected explicitly; the 20 methods; CLI test; `ArchitectureRulesTest` ratchets only downward | tests ±400, Core +30/−20 (H) | 4: an acceptance-visible switch across both pipelines | full process + final review (part of the R6-gen switch) | no |
| U-K10 | Remove the remaining duplicate rules | R6-check | Binding: delete the definition-stage i128 gate (`Binding.Expressions.cs:1382-1386`, A3b owns it), the forwarded witness path (`Specializations.cs:164` with EffectBounds), `SameModuloOpenOrigins` if still present; Core: fold the collector records into `MirReport`'s requirement rule (R4-M3a) | U-K6; R6-check switch | One capability owner; one witness path | Session (R6-check row) | −60 (H) | 2: deletions with diagnostic snapshot evidence | full process (part of R6-check) | no |
| U-KG | *(Conditional)* Substitution without storage preparation (G128 removal) | early-R6-prereq | Binding: `TyCtxt.Apply` stops preparing storage (R7 item pulled forward) | U-K2's G128 reproducer shows the old build accepts what the collector rejects | No Resource regression at the publication stage | Unit: the G128 reproducer, `TyCtxtTest`, the A5 `Retains` tests; Session | +60 / −40 (H) | 4: Apply is TyCtxt's owner contract, and A4/A5 read it | full process | no |

