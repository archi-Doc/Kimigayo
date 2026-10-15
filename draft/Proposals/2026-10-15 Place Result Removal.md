# Place Result Removal: Indexable Returns References

**Status:** proposal. The owner requested this design on 2026-10-15.

- It is not integrated into the specification and not implemented.
- It does not approve the owner decisions in §1.
- Two independent designs (A and B), written from the same brief, are compared in §8 by the AGENTS priorities.
- An independent review accepted the result with required changes. This revision applies them (§17).

Line numbers refer to `4e205c94`. **(H)** marks a hypothesis that a reproducer must settle.

## 1. Summary and owner decisions

**Proposal.** Remove Place results ([SPEC §7.1.1](../../docs/spec/07-functions-and-callable-values.md#711-place-results)):

- A function result is a Type and nothing else.
- `index` returns `ref/Element during self`, and `indexUniq` returns `uniq/Element during self`.
- `receiver[key]` designates the referent of the reference that the selected `index` or `indexUniq` call returns. This
  is the Place `@follow` selects.

**Owner decisions this design needs.**

- **O3** (DECISIONS, R4 owner decisions). The acceptance changes outside plan Appendix B:
  - `-> place …` is no longer valid;
  - the library sources change;
  - Milestones 27 and 28 are re-spelled;
  - a result Origin narrower than the receiver's, such as `during self.source`, no longer keeps the receiver Loan (§2);
  - a Place expectation no longer infers result-only Type arguments (§2).
- **Staging** (§12). Choose one:
  - **S2** ranks first on coherence and needs a D5 exception for about 8 lines in the frozen old Analysis.
  - **S1** is the coherent option within D5. It keeps one temporary until the R6 deletion of the old paths.

  The two are within about 15 tokens of each other. Choose S2 if the exception is granted, otherwise S1.

**Not needed.**

- No vocabulary limit changes. `HirPlaceKind` keeps 11 entries. `BindingFailure`, `DiagnosticCode` and the Binding enum
  `ExpectedAdaptationKind` each lose one entry; reductions need no approval.
- No cap changes.
- No SETTLED entry is reopened; none covers Place results.

## 2. Required meaning

**Rule R: results.** A function result is a Type. A function lends existing Storage only through the value it returns,
such as `ref/T` or `uniq/T` with its Origin.

- The caller receives a value, and an expected Type adapts it (§10.2).
- `@follow` selects the referent. The referent offers Read, and Write through `uniq/T`, but never Take (§13.5.5,
  [13:334](../../docs/spec/13-operators-and-assignment.md#1355-follow)).
- A borrow through the referent depends on the referent and the parent Loan, not on the temporary that holds the
  reference:
  - shared: §10.2 (10:72) and §13.5.6 (13:399, 13:408);
  - exclusive: §15.6.3 (15:513).

**Rule I: index expressions** (§4.7.1).

- `receiver[key]` resolves through `Indexable<Key>` or `UniqIndexable<Key>`. Their requirements return a reference to
  the selected element.
- An index expression whose element is reached through a conformance designates the referent of the reference that the
  selected call returns.
- Unchanged: selection by use (steps 2–3) and the receiver-capability rule.
- An index is already an Access Designator (§3.4, 03:255), so `x[k]` stays a Place.
- A direct fixed-array element keeps its built-in projection (§4.7.1, §4.7.2).

**Equivalence and its two exceptions.**

§7.1.1 grants a Place result exactly the capabilities of Rule R (07:26–32). So a Place result is a reference result plus
an implicit `@follow` at every call site:

- the original proposal fixed its ABI to that of the reference result
  ([Shared Place Results §7](../Changes/2026-09-25%20Shared%20Place%20Results.md));
- Binding completes a Place call with the referent, while its plan keeps the reference (`Binding.Calls.cs:898–902`);
- MIR lowers it as a temp of the reference Type plus a Deref (`MirBuilder.Places.cs:256–261`; `tests/mir/calls.mir`);
- every library implementation obtains a reference and returns `@follow` of it (`Dictionary.kimi:15–27`,
  `UniqSlice.kimi:36–46`).

Two rules exist only for Place results. They go with them, and O3 records both:

1. **Receiver Loan.** "`during self.source` never removes the actual receiver Loan" (07:30).
   - A reference result `ref/E during self.source` names no receiver Origin, so it carries no receiver Loan (§15.6.4).
   - This relaxes acceptance for implementations whose result Origin is narrower than `self`, such as `self.source` or
     `static`.
   - It is sound, because body checking proves the reference valid for its Origin. It also matches the Slice row of
     §4.7.1.
   - The compiler already takes the Origin of the reference (ArgumentOperations.cs:731–738).
2. **Result-only inference.**
   - Today the Place expectation (`PlaceExpected`, Calls.cs:1217–1225) lets `let v: i32 = get()` fix `T` for
     `get<T>() -> place ref/T`.
   - With `-> ref/T`, `T` stays unbound, because an expected Type is never inverted through a value read (§10.2, §10.8).
   - The caller writes `get<i32>()`.

**Family of forms governed:**

1. Index expressions on every Indexable receiver, in every use.
   - Receivers: user Types; Dictionary; UniqSlice, through the `indexPosition*` calls that `Binding.Views.cs:78`
     synthesizes; `S is Indexable<K>`; receivers reached through `ref`/`uniq` layers.
   - Uses: bare Copy read; write; compound update; `@ref`/`@uniq`; Reborrow of a stored `uniq/U`; Subject; Receiver
     Expression; projection; callee (`m[k](5)`); comparison; fixed expected `ref/E`.
2. Written calls `x.index(k)` and `x.indexUniq(k)`. They become ordinary calls whose value is the reference.
3. Function references, Function Types and `Callable<… -> ref/E>`. They have no result-mode identity.
4. Contract implementations, overrides and associated-Type evidence. These use ordinary result compatibility (§10.7)
   and §8.5.5 extraction.
5. Generic forwarders (`firstPlace`, Milestone 27).
6. General functions that published Places (`first`, `slot`). They now return references, and the caller writes
   `@follow`.

## 3. What the current representation lacks or wrongly allows

- **One concept, two forms.** "Give the caller access to existing Storage" can be written two ways:
  - `-> ref/T`: the caller follows explicitly;
  - `-> place ref/T`: every call site follows invisibly.

  This contradicts Principle 1, Principle 3 and §3.3.3 ("a reference value is not read as its referent by default").
- **A result mode woven into Type identity.**
  - `FunctionResultMode` (`BindingModel.cs:33`) is a field of every `BoundType` (:394–400, :472).
  - It takes part in interning (`TyCtxt.cs:92, 169`).
  - It is compared in Binding.cs:278, CompleteTypeIdentity.cs:26, TypeRelations.cs:47/143/430,
    TyCtxt.Relations.cs:107, ReferenceTypes.cs:89, FunctionTypes.cs:262 and Calls.cs:2128.
  - About 20 `Intern` calls pass it through.
  - It is copied into `CallPlan.ResultMode` (CallPlan.cs:77/239/268/337) and into Hover's Type key
    (`HoverBuilder.cs:593`).
- **One call with two Types.** A Place call's Type is the referent, while its plan keeps the reference (Calls.cs:898–902;
  ValueCalls.cs:552–554). Because of this:
  - consumers ask `ElementAccess.IsPlaceCall` or `PlaceCallReference` (ElementAccess.cs:32–40) at 34 sites: 13 in
    Binding, 16 in Analysis, 5 in Emission;
  - MIR keeps a result-temp branch (MirBuilder.Calls.cs:136).
- **One decision made twice**, once as Follow and once as Place call:

  | Decision | Follow case | Place-call case |
  | --- | --- | --- |
  | Path authority | ArgumentOperations.cs:190–193 | :200–202 |
  | Writable target | Expressions.cs:47–51 | :71–74 |
  | Place Origin | ArgumentOperations.cs:722–728 | :731–734 |
  | Effect access | EffectBounds.cs:752–755 | :758–760 |
  | Compound update (old Analysis) | OwnershipAnalysis.Arithmetic.cs:68–72 | :73–76 |
  | Assignment (old Analysis) | OwnershipAnalysis.cs:1371–1374 | :1376–1379 |
- **Rules that exist only for the second form:**
  - the return-operand check and `PlaceRequired_Kd` (Expressions.cs:749–753, 845–856; Functions.cs:101–105;
    Closures.cs:630–634);
  - the Place expectation `PlaceExpected` (FunctionTypes.cs:12–13; Calls.cs:1217–1225; CallableSelection.cs:260–264);
  - the slot-publishing adaptation and its `ExpectedAdaptationKind.ExclusiveBorrow` (ArgumentOperations.cs:47–48,
    549–559);
  - result-mode filters in identification (ContractMatching.cs:458, 525–529), overrides (VirtualOverrides.cs:440–466),
    closures (Closures.cs:369) and Callable fits (FunctionTypes.cs:1006);
  - the associated-inference exclusion (AssociatedInference.cs:292–293);
  - the §10.3 "Place required" row and its `@ref` sentence.
- **Dead or inconsistent remnants.**
  - The HIR "getter's place result" disjunct (`Binding.Hir.cs:359`, `HirVocabulary.cs:184`) is dead:
    - accessors parse their result with `ParseAccessorReturnType` → `ParseDeclarationType` (Parser.cs:1045, 1170–1184);
    - `GetterSignature` takes a Type (Appendix F);
    - CallPlan.cs:268 sets a mode only for `FunctionKoto`.
  - The result-category key of §8.6.3 (08:402) sits beside §8.5.5's "never filtered by result" (08:320).

## 4. Owning components and the shared representation

| Concern | Owner | After the change |
| --- | --- | --- |
| Meaning of `x[k]` | SPEC §4.7.1. In Binding, the index owner: `Binding.Indexers` (`TryBindIndexer` :50, `BindIndexerCall` :212) and `Binding.Views` (`TryBindViewSelection`, `BindViewCall`). | The index owner types `x[k]` as the referent of its selected call's plan result. It reads `IndexerCall(index, false).BoundCall.ReturnType`, which is Binding's own fact. It already decides selection, the conformance and the exclusive entry. |
| Result grammar | `Parser.ParseFunctionReturnType` (:599–608) and Function Types (:4733) | Both call `ParseDeclarationType`, and `ParseFunctionResult` (:4756) is deleted. |
| Type of a call | `Binding.Calls` | Always the plan's `ReturnType`. S1 keeps one exception for synthesized indexer calls until R6 (§12). |
| HIR Place shape | `Binding.Hir.KindOf` (:359) | `HirOp.Call => IsUserIndex(node) ? CallResult : Value`. `CallResult` reads: "The referent of the reference the node's selected index call returns (SPEC 4.7.1)." |
| MIR | `MirBuilder.Referent` (Places.cs:256–261) | It reads `ExpansionPlan.Of(node).Second.ReturnType`, which is HIR publication and is read only by MIR. The `?? CallPlan.Of(node)` fallback goes. MirBuilder.Calls.cs:136–137 becomes `destination`. The MIR analyses are unchanged; they already see temp plus Deref. |
| Written `f()@follow` | Existing Follow (`HirOp.Deref`, MIR Deref) | No new rule. |

`ElementAccess.IsUserIndex` and `IndexerCall` stay. `IsPlaceCall` and `PlaceCallReference` go. Under S1, they first
narrow to synthesized indexer calls and are removed with the old paths at R6.

## 5. What becomes unnecessary

| Removed | Sites |
| --- | --- |
| `FunctionResultMode`, `BoundType.ResultMode`, the `resultMode` parameter of `TyCtxt.Intern` (11 → 10 parameters, reducing existing excess), the interning comparison, 9 identity/relation comparisons and the pass-through arguments | BindingModel.cs, TyCtxt.cs and the §3 sites. Pass-throughs: AssociatedTypes:648, CallSlots:37, ClosureInstances:77, Closures:359/661, Constraints:742, FunctionItems:51, FunctionTypes:1097, InferenceBoundaries:190, LocalRegions:270/301, OpenOrigins:320/416/479, OriginDeclarations:715, PairCases:239, Storage:422, TypeIdentity:82, TypeOrigins:109, TypeRelations:81/648, ValueCalls:552 |
| `CallPlan.ResultMode`, `ResultModeOf`, `PlaceExpected` | CallPlan.cs; FunctionTypes.cs:9–13; Calls.cs:890, 1217–1225; CallableSelection.cs:260–264 |
| The two-Type call | Calls.cs:898–902; ValueCalls.cs:554 |
| `PlaceResultKoto`, `KotoKind.PlaceResult`, `SyntaxForm.PlaceResultSuffix`, `ParseFunctionResult`, the `place` contextual keyword | Parsing: the file; Koto.cs:309; SyntaxForm.cs:183–184; Parser.cs:4751–4790 |
| `BindingFailure.PlaceRequired`, `PlaceRequired_Kd`, the `Binding.PlaceRequired` and `Syntax.PlaceResultSuffix` requirement entries, `CheckPlaceResultOperand` and its three callers | BindingModel.cs:214; Binding.cs:1153; DiagnosticCode.cs:142; DiagnosticCode.tinyhand:665–667; DiagnosticRequirement.tinyhand:233–235, 513–514; Expressions.cs:749–753, 843–856; Functions.cs:101–105; Closures.cs:630–634 |
| The slot-publishing adaptation, `ExpectedAdaptationKind.ExclusiveBorrow` and its two arms. `HirAdaptation.ExclusiveBorrow` stays; it has other producers. | ArgumentOperations.cs:47–48, 549–559; EffectBounds.cs:622–624; HirVocabulary.cs:144 |
| Binding's Place-call cases, each subsumed by Follow or by the index owner | ArgumentOperations.cs:200–202, 347, 360 (part), 468–472, 496 (part), 706 (part), 731–734, 829–832; Expressions.cs:71–74; EffectBounds.cs:758–760; the dead getter disjunct of Binding.Hir.cs:359 |
| Result-mode identity in matching, and its display | ContractMatching.cs:458, 525–529; VirtualOverrides.cs:440, 446, 466; Closures.cs:369; FunctionTypes.cs:1006; DiagnosticTypes.cs:189; HoverBuilder.cs:593 (key part) |
| `PlaceResultKoto` cases; the associated-inference exclusion | Types.cs:568–571; OriginInheritance.cs:47–49; AssociatedInference.cs:292–293 |
| The MIR result-temp branch and the `Referent` fallback | MirBuilder.Calls.cs:136; MirBuilder.Places.cs:258 |
| The old paths' general Place-call branches (deletion only) | OwnershipAnalysis.cs:932–936, 1376–1379; Arithmetic.cs:73–76; Closures.cs:115–116 (clause), 209; Instances.cs:176 and Reservations.cs:131 (clauses, once shown dead) |
| SPEC rules | All of §7.1.1; §10.3's Place-required row and `@ref` sentence; §8.6.3's result-category key; §6.4.1's "Place mode"; the §7.1.1 receiver-Loan rule and the Place expectation (§2) |

## 6. Sites sharing the cause (found by search)

**SPEC**

- §4.7.1 (04:270–305), §4.7.2 (04:315)
- §6.4.1 (06:262)
- §7.1 (07:7), §7.1.1 (07:24–42)
- §8.4.1 example (08:203–206), §8.4.2 (08:210), §8.5.3 (08:300), §8.5.5 (08:320), §8.6.3 (08:402), §8.10 (08:637)
- §9.1 (09:37)
- §10.2 (10:52), §10.3 (10:152, 155), §10.7 (10:296–298)
- §13.5.6 (13:401)
- §15.1.5 (15:86), §15.6.1 (15:473)
- §22.1 (22:37)
- Appendix E (55, 85)
- Appendix F: 143, 148, 280–283, and the `FunctionResult` uses at 394, 455, 466 and 613

**Docs**

- SPECJ.md at the matching rules: 365, 603, 698, 712, 720, 802, 922, 1323, 1881–1886, 2400, 2414, 2583, 2588,
  2644–2647, 2731, 2778, 2789, 2894, 3232
- SPECJ2.md (added in `4e205c94`): 605, 700, 924, 1325, 1885–1888, 2416, 2590, 3234
- LIBRARY.md 158–170, 216, 247–248, 342–344
- STYLE.md 245, 251–252
- GUIDE.md 175–176
- STATUS.md 25, 36, 72, 124, 144, 157, 262
- CODEMAP.md 95, 119, 149
- DIAGNOSTICS.md 133, 300
- VERIFICATION.md 139
- COMPILER_ARCHITECTURE: the S1 temporary and the R6 row
- DECISIONS: a new entry (§15)
- SETTLED: entries for the rejected language options (§15)
- PLAN: one line; it is at 197 of 200 lines
- `tests/milestones/README.md`: the re-spelling record

The review logs PARSING_REVIEW and BINDING_REVIEW stay as history.

**Library:** `Indexable.kimi:8–22`, `Dictionary.kimi:14–27`, `UniqSlice.kimi:36–55`.

**Compiler.** The sites in §3 and §5, plus these comments to reword:

- MirBuilder.Calls.cs:41, MirBuilder.Places.cs:256, HirVocabulary.cs:184, ArgumentOperations.cs:48, Indexers.cs:9–12;
- old-path comments: OwnershipAnalysis.Elements.cs:10, BodyLowering.Pointers.cs:9 and 99, BodyLowering.Elements.cs:503;
- the code comments that cite "SPEC 7.1.1": recite them to §4.7.1 or §13.5.5, or delete them with their code.

**Frozen old paths that serve user indexes.** These are unchanged under S1 and edited under S2:

- OwnershipAnalysis.cs:1018, 1137, 1381 and 1666;
- OwnershipAnalysis.Pointers.cs:292–360, where `ReadPlaceCall` and `WritePlaceCall` read `call.BoundType` as the stored
  Type;
- Borrows.cs:152–168 and Arithmetic.cs:45–48;
- the five Emission sites `PlaceCallReference(x) ?? x.BoundType`;
- the indexer-call identification `ElementAccess.cs:130–140` (`ValueSource`, called only from Analysis and Emission).

**Extension:** `src/kimi-ext/syntaxes/kimi.tmLanguage.json:197–200` and `src/test/unit/grammar.test.ts:210`. Lines
227–228, which treat `place` as a word, stay.

**Tests** (`place (ref|uniq)/`)

- CallablePlaceResultTest, PlaceResultTest, PlaceResultParseTest, IndexableContractTest
- DictionaryIndexableTest, HirColumnsTest, UserArithmeticUpdateTest, SyntaxEditInvalidationTest
- ReferenceReplacementTest, PairFollowTest, KimiLibraryShapeTest:317–318, IteratorOriginEffectsTest
- VirtualOverrideContractTest:53–61, Utf8ReserveEffectTest, UniqSliceTest, PublishedPlaceProjectionTest
- PlaceCalleeTest, NestedValueCallOriginTest, MirInitTest, LocalRegionCompletionTest, DiagnosticPrecisionTest

Also affected:

- direct calls of `index`/`indexUniq` in DictionaryCostTest:51/78, DictionaryIndexableTest:74–108 and
  DictionaryStoredReferenceTest:83–90;
- SemanticSnapshotTest:330–332 and 530–532, which print ResultMode;
- `tests/mir/calls.kimi:22–23` and its `.mir` golden;
- `tests/diagnostics/syntax.json:593–594`.

**Milestones:** `Milestone27.kimi:10–29, 79` (`Pair`, `firstPlace`) and `Milestone28.kimi:18–19`.

## 7. Options

| Option | Content | Judgment |
| --- | --- | --- |
| A1 | Restrict Place results to the Indexable requirements and their implementations (a correction inside Syntax and Binding) | Rejected on coherence. The mode stays in Function Types, CallPlan and matching, because `Dictionary.index` is still referenceable. It adds a shape allowlist and deletes nothing: about +100 tokens. |
| A1′ | Delete the syntax and `BoundType.ResultMode`, but keep `CallPlan.ResultMode` for synthesized indexer calls (the review's smaller attempt) | Rejected. It keeps the 34 Place-call consumers, the MIR branch and the two-Type call permanently, and Follow and Place call stay two decisions for one meaning. About −1,000 tokens, against B's −1,700 to −1,900. |
| A2 | `place` as sugar for a reference plus a call-site follow | Rejected. The mode must still reach every call site through references, Callable and Contracts. It keeps two forms (Principle 1) and a hidden operation (Principle 3). |
| **B** | Remove Place results; Indexable returns references and the index expression follows them (a change of shared structure) | **Chosen.** One form, one owner, no mode and no call whose Type differs from its result. About −1,700 to −1,900 tokens (§13). |
| B′ | Every reference-returning call designates its referent | Rejected. `let r = f()` must keep the reference (§3.3.3, §3.6); this would rebuild Place results for every function. |
| C | A user index as a synthesized `@follow` node, or published as `HirPlaceKind.Deref` over its call | Rejected. The use selects one of two calls after binding (`Binding.Hir.Adjust.cs:127–132`). The §4.7.1 receiver rule is not the Follow rule: same shape, different contract. A node would be a second Koto form for one concept. MIR's Deref takes its operand from a syntax child, which a user index lacks, so a branch for one form would be needed: about −10 tokens net, with a symptom-fix sign. Keeping `CallResult` with one producer is right. |
| D | Make `x[k]` an ordinary call, so users write `x[k]@follow` | Rejected. It breaks §4.7.2's parity between Kimi-written collections and built-in elements. |

The SPEC cannot land ahead of the compiler. Until the compiler changes, a reference-returning Indexable implementation
gets `MissingContractImplementation_Kd`, which is a conservative Language error for a valid form. AGENTS forbids that.
SPEC, library, compiler and tests therefore land in one behavior unit (U2).

Deferring the whole change until after R6 is also rejected. It would keep both forms, the duplicated Binding decisions
and the MIR result-temp branch (about 1,900 tokens of incoherence) through R4–R6. It would save only S1's transitional
code or S2's frozen-path edits.

## 8. Comparison of the two independent designs

Both designs chose option B. Where they differed:

| Point | Design A | Design B | Decision and reason |
| --- | --- | --- | --- |
| Staging | S1: one temporary until R6; frozen paths get deletions only | S-now with a D5 exception (S2), or wait for R6; rejects the temporary | **Both offered.** S2 ranks first on coherence (§1). S1 is the coherent option within D5, and only the owner can grant the exception. They differ by about 15 tokens. S2 also keeps one temporary until R6: the frozen-only indexer-call identification (ElementAccess.cs:130–140). |
| Evidence before removal | — | U0: commit the reference-plus-`@follow` equivalents first, on both pipelines | **Adopted** (AGENTS: diagnose from evidence). The forms are already valid; a failure is a pre-existing limit for the owner to weigh before U2 (§12). |
| Restructuring commit | None needed | A separate behavior-preserving commit | **Adopted** as U1. The Binding index owner reads its own plan result instead of the two-Type call while Place results still exist. That is provable with VERIFICATION's behavior-preserving evidence, and it leaves U2 as deletion plus the SPEC change. |
| §6.4.1 override "Place mode" | Found | Missed | Included. |
| `Dictionary.indexUniq` body | `return value@move` | Bare `return value` (Reborrow) | `@move`, the library idiom (`Array.kimi:107–109`). Both are valid; the transfer forms no child Loan. |
| Implementation convention | Implicit borrow where possible | Explicit `@ref`/`@uniq` | **Explicit.** §10.2 gives no implicit exclusive borrow. The explicit forms are valid in every admitted case and state the borrow (Principle 3); whether the implicit row applies to a generic `T` is not relied on. |
| INTEGRATED | No edit | A note on the 2026-09-25 rows | This proposal's own intake entry records that it supersedes §7.1.1. The existing register rows stay. |
| Area caps | — | Root is near its cap | Root has about 139 tokens of headroom (A1 7,206 / 7,345; only a comment commit touched Root since). Measure it in U1 and U2. |
| Getter Place results | Reachable | Dead (H) | Dead (§3). The review settled it. |

## 9. Proposed SPEC text

**§4.7.1** replaces 04:272–297. Steps 1–3, Position normalization, the conformance list and the fixed-array paragraph
are unchanged.

> Every single-element index expression, also on user Types and Dictionary, resolves through these Kimi Contracts,
> whose requirements return a reference to the selected element. The key is shared-borrowed, never consumed; a key of
> reference or handle Type is written `key@ref` (§10.2).
>
> ```kimi
> contract Indexable<Key>
>     associate Element
>     func index(self: ref/Self, key: ref/Key) -> ref/Element during self
>
> contract UniqIndexable<Key>: Indexable<Key>
>     func indexUniq(self: uniq/Self, key: ref/Key) -> uniq/Element during self
> ```
>
> Both requirements return a reference to the same element for the same key; `indexUniq` grants exclusive access
> without changing the stored Type. `receiver[key]` resolves statically:
>
> 1.–3. *(unchanged)*
>
> 4. The index expression designates the referent of the reference that the selected `index` or `indexUniq` call
>    returns, the Place `@follow` selects (§13.5.5): through `index` it offers Read, through `indexUniq` also Write;
>    neither offers Take. A direct fixed-array element remains the built-in projection below.
>
> Selecting `indexUniq` *(unchanged sentence)*. A failure never reselects a mode or candidate. Receiver and key stay
> Loaned during the call. The element Place depends on what the returned reference depends on, at most what the
> receiver supplies and never the key, and not on the temporary holding the reference (§10.2, §13.5.6, §15.6.3). A
> written call such as `names.index(1)` is an ordinary call whose value is the reference; `names.index(1)@follow`
> designates, with shared capability, the Place that `names[1]` designates.
>
> | Target | Element Place | Range result | Depends on |
> | --- | --- | --- | --- |
> | `Array<E>`, `[N of E]`, `UniqSlice<E>` | Referent of `ref/E` or `uniq/E` | `Slice<E>` | `a`, the receiver borrow |
> | `Slice<E>` | Referent of `ref/E during s.source` | `Slice<E>` | `s.source` |
> | `Dictionary<K, V>` | Referent of `ref/V` or `uniq/V`; absence Aborts | None | `a`, never the key |

The example gains two lines:

```kimi
let lent = names.index(1)        // ref/string: a written call returns the reference.
// names.indexUniq(1) = "x"      // Error: the target is the reference, not its referent.
```

**§7.1.** Line 7 ends "…and an optional `->` result Type (grammar: §F.6)." Delete §7.1.1 and add to §7.1:

> **Lending existing Storage.** There is no Place result category: a function lends existing Storage only through the
> value it returns, such as `ref/T` or `uniq/T` with its Origin (§15.3.4, §15.4.3). The caller receives the reference
> as a value: an expected Type adapts it (§10.2), and `@follow` selects its referent, which offers Read and, through
> `uniq/T`, Write, but never Take (§13.5.5). A borrow through it depends on the referent and the parent Loan, not on the
> temporary holding the reference (§10.2, §13.5.6, §15.6.3). An index expression follows the reference that `index` or
> `indexUniq` returns (§4.7.1).
>
> ```kimi
> func first<T>(values: ref/Array<T>) -> ref/T during values
>     return values[0]@ref                // Shared borrow of the element.
> func slot(values: uniq/Array<i32>) -> uniq/i32 during values
>     return values[0]@uniq               // An exclusive borrow is written.
>
> // resources: Array<Resource>, Resource Non-Copy; counts: var Array<i32>.
> let view = first(resources@ref)          // ref/Resource.
> slot(counts@uniq)@follow += 1            // Updates counts[0].
> // slot(counts@uniq) += 1                // Error: an assignment target is never redirected.
> ```

**Other SPEC edits**

- §4.7.2 (04:315): "an element Place (through a conformance, the referent of the returned reference, §4.7.1)".
- §6.4.1 (06:262): "Same kind and normalized Type; no covariance".
- §8.4.1 (08:203–206): the example gets the new signatures.
- §8.4.2 (08:210): "an optional value or Place result (§7.1.1)" becomes "an optional result Type".
- §8.5.3 (08:300): delete "and is not a Place result".
- §8.5.5 (08:320): "Property and accessor results supply none". As a result, `index` and `indexUniq` results supply
  evidence for `Element`.
- §8.6.3 (08:402): delete "; and result category (value, or Place with the same mode)".
- §8.10 (08:637): delete "`R` may be a Place result."
- §9.1 (09:37): "return Types".
- §10.2 (10:52): delete "; Place results follow §7.1.1".
- §10.3 (10:152, 155): delete the "Place required" row, and "and an outer `@ref` never prefers a Place-returning
  candidate".
- §10.7 (10:296–298): "required result `Q`".
- §13.5.6 (13:401): "A reference returned by a call or getter is a value: …".
- §15.1.5 (15:86): delete "Places published by functions (§7.1.1),".
- §15.6.1 (15:473): drop the §7.1.1 link.
- §22.1 (22:37): the new signatures.
- Appendix E: delete "Place result". The Indexable row becomes "Contracts whose `index`/`indexUniq` return the
  reference `receiver[key]` follows".
- Appendix F:
  - delete the `place` row (143) and "Place results," (148);
  - `FunctionType := FunctionParameters "->" Type`;
  - delete `FunctionResult` and `PlaceResult`, and write `Type` at 394, 455, 466 and 613.

**Other documents**

- SPECJ and SPECJ2 follow every edit.
- LIBRARY §2.4: the Contracts "return references to the element a key selects; `receiver[key]` follows the returned
  reference". Update rows 216, 247–248 and 342–343.
- STYLE §4.2 row: "Lend an existing element | `ref/T during self` or `uniq/T during self`; Indexable's
  `index`/`indexUniq` for `receiver[key]`".
- GUIDE, STATUS, CODEMAP, DIAGNOSTICS and VERIFICATION change at the §6 lines.

## 10. Library and milestone changes

```kimi
// Indexable.kimi. Doc comments: "Returns a shared (exclusive) reference to the element `key` selects; `receiver[key]`
// follows it." The abort item stays.
func index(self: ref/Self, key: ref/Key) -> ref/Element during self
func indexUniq(self: uniq/Self, key: ref/Key) -> uniq/Element during self

// Dictionary.kimi
public func index(self: ref/Self, key: ref/K) -> ref/V during self
    match self.tryGet(key)
        .Some(let value) => return value                      // Copy of ref/V.
        .None => Storage.missingDictionaryKey()
public func indexUniq(self: uniq/Self, key: ref/K) -> uniq/V during self
    var state = Storage.borrowStorageUniq(self)
    loop
        match Storage.splitFirst(state@uniq)
            .Some((let storedKey, let value))
                if storedKey.equals(key) => return value@move  // As Array.tryGetUniq.
            .None => Storage.missingDictionaryKey()

// UniqSlice.kimi
public func index(self: ref/Self, key: ref/isize) -> ref/T during self
    require key >= 0 and key < self.length else => Storage.indexBounds()
    unsafe => return self.storage[key]@ref
public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/T during self
    require key >= 0 and key < self.length else => Storage.indexBounds()
    unsafe => return self.storage[key]@uniq
internal func indexPosition<P>(self: ref/Self, key: P) -> ref/T during self          // Body unchanged.
internal func indexPositionUniq<P>(self: uniq/Self, key: P) -> uniq/T during self     // Body unchanged.
```

**Milestone 27**

- `Pair.index` returns `self.first@ref` / `self.second@ref`, and `indexUniq` returns the `@uniq` forms.
- `firstPlace` becomes `-> ref/S.Element during items` with `return items[0]@ref`.
- The comparisons at :79 stay valid, because §13.4 follows reference layers.
- The expected output is unchanged.

**Milestone 28.** `index -> ref/E during self => self.items[key]@ref`, and `indexUniq -> uniq/E during self =>
self.items[key]@uniq`.

`tests/milestones/README.md` records both re-spellings, with the SPEC revision as their cause (PLAN §5).

## 11. Diagnostics

**Syntax.** `place` becomes an ordinary Name.

- In `-> place ref/T`, `place` parses as a Type name, the existing header rule reports `ref` once, and the rest rests
  on that error.
- There is no form-specific code, record or Advice; the language has no such form.
- The corpus row (syntax.json:593–594) is replaced by one that pins the record. **(H)** Its code and location.

**Written target on a reference result.** In `f() = v` with `f -> uniq/T`, the target is a value. The existing
write-permission path reports it (Expressions.cs:1263 → `InvalidAssignment_Kd`) **(H)**. There is no Advice or Repair
suggesting `@follow`: a repair needs a stated precondition and guarantee (DIAGNOSTICS). Such a repair can be a separate
later unit.

**Code changes that follow from the rule**

- An `index` implementation with a value result moves from `MissingContractImplementation_Kd` to
  `IncompatibleContractImplementation_Kd`, since identification is never filtered by result (§8.6.3) **(H)**.
- Override reasons lose "value/Place mode" (VirtualOverrides.cs:446, 466). These lines build message text inside
  Binding, which AGENTS Diagnostics forbids. That defect predates this change; record it as an open issue rather than
  fix it in passing.
- Function Types no longer display `-> place` (DiagnosticTypes.cs:189).

**Removed:** `PlaceRequired_Kd` and `Syntax.PlaceResultSuffix`, recorded in PLAN_HISTORY.

**Acceptance changes** (O3; each needs a test)

- `let x = f()` binds the reference, and `_ = f()` is valid for a Non-Copy referent.
- `match f()` and `for x in f()` on a `uniq/T` result are Exclusive, where the former `place uniq/T` was Shared
  (§15.1.6, §14.6.2). `f()@follow` gives the former form.
- `Element` is inferred from `index`.
- An implementation result Origin narrower than `self` keeps no receiver Loan (§2).
- `let v: i32 = get()` no longer infers `T` for a former `get<T>() -> place ref/T`. **(H)** The code for the unbound
  slot.

## 12. Units and staging

**Ordering.**

- Serialize with the in-flight R4 work. C1b is changing Binding, so land after it.
- Report the mir-diff unclassified count at the end of each session; it must not rise.
- Rows that disappear need no class. A new row cites §4.7.1/§7.1 and the O3 record, since it has no Appendix B item.

**U0. Evidence (tests only; no compiler change).** Add to their owning classes the reference-plus-`@follow` equivalent
of each positive Place-result case:

- `f()@follow = v`, `f()@follow += v` and `f()@follow.m()`;
- projections through a returned reference: `f().0`, `f()[0]`, `f().item`;
- a callee through a `ref/F` result (`first(xs@ref)(5)`);
- `match f()@follow` and `for x in f()@follow`;
- generic forwarders with `@ref`/`@uniq`.

Run them on the old pipeline and under `KIMI_PIPELINE=mir`. A passing form becomes a regression test for U2.

D5 bars fixing an old-path failure here. A failure therefore means U2 would remove the only working spelling of that
form from the default pipeline, so the owner decides before U2 whether to proceed or wait for R6.

**U1. Behavior-preserving restructuring (Binding).**

- The Binding index owner reads Binding's own fact: the selected indexer call's plan
  (`IndexerCall(index, false).BoundCall.ReturnType`), whose referent (`Components[0]`) it takes. It reads this instead
  of the call node's two-Type `BoundType` and instead of `PlaceCallReference`, at three sites:
  - `PlaceOrigin` (ArgumentOperations.cs:736);
  - `TryBindIndexer` (Indexers.cs:113);
  - `TryBindViewSelection` (Views.cs:56).
- `ExpansionPlan.Of` stays MIR-only: it reads HIR publication, which `PublishHir` writes only after binding
  (Binding.Hir.cs:35–37). HIR `KindOf` and MIR `Referent` already have their target form and do not change in U1.
- Search for every other non-frozen reader of a synthesized indexer call's `BoundType` and switch it the same way.
- Show no behavior change with VERIFICATION's behavior-preserving evidence: the diagnostic snapshot, the semantic
  snapshot with HIR and MIR, and IR equality.

**U2. Behavior change (SPEC revision).** Contents:

- SPEC, SPECJ, SPECJ2 and the docs of §6;
- the library; Milestones 27/28 and their README record;
- the §5 deletions in Syntax, Binding, HIR and MIR; deletions only in the old paths;
- the test migration of §14, the MIR golden, the syntax corpus and the tinyhand data;
- DECISIONS and SETTLED (§15), STATUS, CODEMAP, PLAN and PLAN_HISTORY;
- this proposal's intake entry in `draft/INTEGRATED.md`.

The staging choice applies here:

- **S2.** The indexer call keeps its plan Type. About 8 frozen lines read the referent instead (OwnershipAnalysis.cs:1137,
  1666; Pointers.cs:294, 297, 338, 340), plus clause deletions, with the D5 exception recorded. `ElementAccess.cs:130–140`
  stays until R6 deletes its only callers.
- **S1.** `BindIndexerCall` and the element case of `BindViewCall` re-complete the synthesized call with its referent,
  which moves the Calls.cs:898 state to its owner. After U1, only the frozen paths read it. `IsPlaceCall` and
  `PlaceCallReference` narrow to synthesized indexer calls.
  - Each carries `[Transitional("R6", Reason = "The frozen old paths read a user index through its indexer call.")]`.
  - Each is listed under COMPILER_ARCHITECTURE's temporary structures.
  - The R6 row records their deletion together with the old paths.

U2 checks:

- the selected unit checks, plus `KimiLibraryShapeTest` and `ArchitectureRulesTest`;
- the explicitly selected native fixtures: IndexableContractTest, DictionaryIndexableTest, UniqSliceTest,
  PlaceCalleeTest and the migrated Callable fixtures;
- Milestones 27 and 28 at O0 and O2;
- the MIR dump goldens and the `KIMI_PIPELINE=mir` ledger run;
- the §13 measurements;
- a Session at the end.

**U3. Extension.** Delete the grammar rule and its test, per `src/kimi-ext/AGENTS.md`.

**R6.** The deletion of the old paths also deletes S1's temporary, or S2's frozen-only identification.

## 13. Net size and performance

These are estimates (lines × 6.48, the A1 ratio), not measurements. Each commit states measured tokens per area.

| Area | Lines (ref) | Tokens (est.) | Content |
| --- | ---: | ---: | --- |
| Parsing | −95 | ≈ −400 | `PlaceResultKoto.cs` (58 lines, mostly doc comments), `ParseFunctionResult` (≈35), two enum rows |
| Binding | −140 plus ~55 partial lines | −950 to −1,350 | §5 deletions. U1 is about neutral. S1's re-completion adds ~15. |
| Root | ±0 | ≈ +5 (S1), −50 (S2) | narrowed helpers; the ReferenceTypes.cs:89 clause. R6 removes the rest. |
| Mir | −3 | ≈ −25 | Calls.cs:136; Places.cs:258 |
| Analysis (frozen) | −15 to −22 | −100 to −140 | dead general Place-call branches; S2 rewrites ~8 lines |
| Emission (frozen) | 0 | 0 (S1), −35 (S2) | — |
| **Compiler** | **≈ −255** | **≈ −1,700 to −1,900** | No area grows; no R4-M30 allowance is used. |

The review estimates all of S1's transitional code at about +100 to +120 tokens until R6. Outside the compiler, tests
shrink by about 300–350 lines: three files are deleted after their unique cases move.

**Native O2 and allocations.** No change is expected. A Place result already uses the reference-result ABI, and the
library bodies return the same pointer. Measure under fixed conditions and keep every report:

- IR equality for the unchanged programs;
- an O2 IR diff of the changed library bodies (`Dictionary.index`/`indexUniq`, `UniqSlice.index`/`indexUniq`,
  `indexPosition*`) and of one user-index call site;
- `src/Benchmark/Kimi/ExclusiveViews.kimi` (UniqSlice), 7 matched O2 runs before and after; no benchmark indexes a
  Dictionary, so the IR diff and DictionaryCostTest cover it;
- the allocation counts of DictionaryCostTest and UniqSliceTest;
- the output of Milestones 27 and 28.

**Compiler.** Interning compares one field fewer, and selection and returns lose a branch each. Nothing new is allocated.

- Record Bind cold and warm time and allocations.
- The warm zero-allocation regressions must stay at 0.
- **(H)** Whether `BoundType` shrinks (padding).

## 14. Test migration

Each rule gets a valid form and an invalid counterpart:

| Rule | Valid (fails before, passes after) | Invalid |
| --- | --- | --- |
| Reference-returning conformance | Read, Copy, `@ref`, `@uniq`, write, `+=`, bare Reborrow of a stored `uniq/U`, `match pair[0]`, `pair[0].bump()`, `pts[0].x = 5` (the IndexableContractTest fixtures re-spelled) | A shared-only update gives `SharedPathAccess_Kd`; a `let` receiver gives `InvalidAssignment_Kd`; `pair[0]@move` gives `ExclusivePathTake_Kd`; `@uniq` under `S is Indexable` gives `SharedPathAccess_Kd` |
| Narrow result Origin | A view whose `index` returns `ref/E during self.source`: the element is kept while the view handle is reborrowed or replaced | Writing the source while the element is live |
| Written calls | `let r = pair.index(1)`, then `r@follow == 2`; `pair.indexUniq(0)@follow = 7` | `pair.indexUniq(0) = 7` (pin the code) |
| Function references | `Dictionary<i32, i32>.index` at `(ref/Dictionary<i32, i32>, ref/i32) -> ref/i32` | The same at `-> i32`: `TypeMismatch_Kd` **(H)** |
| Contract implementations | `Element` inferred from `index` when `associate Element` is omitted | `index -> i32` gives `IncompatibleContractImplementation_Kd`; `index -> ref/i32` with `indexUniq -> uniq/i64` gives differing evidence; an `indexUniq` body without `@uniq` |
| Result-only inference | `get<i32>()` with `get<T>() -> ref/T during static` | `let v: i32 = get()` (pin the code) |
| Syntax | `place` as an ordinary Name | the `-> place ref/T` corpus row (the record from a run) |

**Re-spelled**, keeping each rule's expectation:

- DictionaryIndexableTest
- DictionaryStoredReferenceTest:89–90 (`entries.index(1)@follow@follow@ref`) and DictionaryCostTest
- UniqSliceTest, PublishedPlaceProjectionTest and PlaceCalleeTest
- UserArithmeticUpdateTest:50 (`slot(total@uniq)@follow += right`)
- ReferenceReplacementTest, PairFollowTest, IteratorOriginEffectsTest and Utf8ReserveEffectTest
- NestedValueCallOriginTest, LocalRegionCompletionTest, MirInitTest, DiagnosticPrecisionTest and
  SyntaxEditInvalidationTest
- HirColumnsTest: the user-index rows keep `CallResult`
- KimiLibraryShapeTest:317–318
- VirtualOverrideContractTest:53–61: the Origin dependency is kept, and the mode assertion goes
- SemanticSnapshotTest: ResultMode is no longer printed
- `tests/mir/calls.kimi` and its golden

A bare `let n = f()` that relied on a Copy becomes `let n = f()@follow`.

**Deleted**, after their unique cases move to their owners:

- PlaceResultTest
- PlaceResultParseTest, whose Contract type-parameter and refinement parse assertions move
- CallablePlaceResultTest, except its non-duplicate reference forms (zero-size, length item, the projection
  `f(a@uniq)@follow.0 = 40`, nested reference) and its allocation case

No expectation is weakened. Each change follows from the SPEC revision, and the commit says so.

## 15. Draft DECISIONS and SETTLED entries

### DECISIONS: R4-M33

R4 section, the next free ID at the time of writing. Header: planning session; two independent designs compared;
independent review; owner O3 and the staging choice, with dates.

**Decision.**

- A function result is a Type.
- `index` returns `ref/Element during self`, and `indexUniq` returns `uniq/Element during self`.
- A user index expression designates the referent of the reference its selected call returns (SPEC 4.7.1, 7.1,
  13.5.5).
- The Binding index owner types it from that call's plan.
- `HirPlaceKind.CallResult` keeps one producer, the user index. MIR `Referent` reads the selected plan.

**Deleted:**

- `FunctionResultMode` and `BoundType.ResultMode`, with their interning, identity, relation and pass-through sites;
- `CallPlan.ResultMode`;
- `PlaceResultKoto`, `KotoKind.PlaceResult` and `SyntaxForm.PlaceResultSuffix`;
- `BindingFailure.PlaceRequired`, `PlaceRequired_Kd` and the return-operand checks;
- `PlaceExpected`, the slot-publishing adaptation and `ExpectedAdaptationKind.ExclusiveBorrow`;
- the result-mode filters in identification, overrides, closures and Callable fits;
- Binding's Place-call cases;
- the old paths' general Place-call branches;
- the MIR result-temp branch.

**Temporary until R6, by staging choice.**

- S1: the synthesized indexer call keeps its element as its node Type, and `IsPlaceCall`/`PlaceCallReference` name only
  those calls, because the frozen old paths read a user index through them (D5).
- S2: only the frozen-only indexer-call identification remains.

**Rejected:**

- restricting Place results to Indexable, in full (A1) or only in CallPlan (A1′);
- a synthesized Follow node, or a Deref publication, per index (C);
- deferring everything to after R6 (§7).

**Which earlier reason no longer holds.** The 2026-09-25 proposal (§1.1) compared Place results with a value result
whose Move is forbidden. It did not compare them with a reference result followed by the index expression. The caller's
choice it wanted (Copy, borrow or replace, as for Array) is exactly what following the returned reference gives, and its
§7 had already fixed the ABI to that of the reference result.

### SETTLED entries

Add these in SETTLED's format (problem, example, proposed fix, reason):

- **`place` as sugar for a followed reference (A2).** Rejected for P1/P3: two forms, and a hidden operation whose mode
  every signature must carry.
- **Every reference-returning call designates its referent (B′).** Rejected: §3.3.3 and §3.6 need `let r = f()` to keep
  the reference.
- **`x[k]` as an ordinary call followed by the user (D).** Rejected: it breaks §4.7.2 parity with built-in elements.

## 16. Unverified

1. Whether the old path and the MIR shadow support each U0 form. DereferenceUpdateTest shows
   `pick(...)@follow += 10`.
2. The exact codes and locations in §11 and §14.
3. Whether non-frozen readers of a synthesized indexer call's `BoundType` exist beyond the three U1 sites. U1's search
   settles this.
4. Associated-Type inference from `index` once the exclusion is removed.
5. The Origin fit of `unsafe => return self.storage[key]@ref` to `during self` on both pipelines.
6. The IR identity of the library bodies and the O2 timing.
7. Every token figure.
8. The effect on mir-diff rows keyed on Place-call nodes.

## 17. Review record

The independent review (2026-10-15) accepted the design with required changes. Each finding is applied in this
revision as follows.

**Blocking findings**

- **B1. U1 read HIR publication during binding.** `ExpansionPlan.Of` reads published columns, and `PublishHir` skips
  while binding runs. The Binding element typing at Indexers.cs:113 and Views.cs:56 also consumed the two-Type call.
  *Applied:* U1 reads `BoundCall.ReturnType` at the three Binding sites and leaves `ExpansionPlan.Of` to MIR (§4, §12).
- **B2. The deletion set missed `ExpectedAdaptationKind.ExclusiveBorrow`** and its two arms. *Applied* in §5 and §15.
- **B3. The equivalence omitted the §7.1.1 receiver-Loan rule and result-only inference through the Place
  expectation.** *Applied* in §1, §2, §11 and §14, with the dependency sentence reworded.

**Non-blocking findings**

- The getter claim was false: it is now settled as dead (§3).
- Missed sites added: SPECJ2, §8.5.3, the MirBuilder.Calls.cs:41 and ArgumentOperations.cs:48 comments.
- The §7.1 wording is narrowed, and the §4.7.1 wording ("selected call", "with shared capability", citations) applied.
- The staging is re-ranked by coherence, the reason to reject deferral is stated, and the R6 citation is corrected.
- U0's failure handling is corrected.
- The migration traps (`let n = f()`, `for`) are added.
- The measurements gain a library and call-site IR diff.
- The mir-diff classification is corrected.
- DECISIONS placement is moved to R4-M33, and the rejected language options go to SETTLED.
- The size figures and counts are corrected.
- The review placed the Appendix F "Place results," at line 147. The text shows it at 148, so 148 stays.

**Open issues to record (pre-existing; not part of this change)**

- The `[N of E]` row with "neither offers Take" sits beside the Take of constant-index fixed-array elements (§4.7.2,
  15:86). The current `place ref/E` row has the same tension. §9's step 4 keeps the built-in projection explicit, but the
  table wording needs a SPEC owner's check.
- VirtualOverrides.cs:446/466 build message text inside Binding, which AGENTS Diagnostics forbids.

**Smaller or more general designs the review tried**

- A1′: worse on coherence and size.
- A Deref publication: −10 tokens net with a symptom-fix sign.
- Timing after R6: worse on coherence for R4–R6.

None is required.
