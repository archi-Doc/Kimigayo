# P40 Remaining Work (handoff)

This filename retains its pre-2026-10-15 ID for archived links; the current program is P40 ([number mapping](../../tests/milestones/README.md#implementation-order)).

P40 (rc/arc shared objects, shared object track U7) is IN_PROGRESS. The 2026-10-09 completion audit confirmed 85 findings, 27 of them blocking. Units U7-1 to U7-7 fixed part of them; this file records what remains, grouped by root cause. The partial implementations of the other groups were discarded at the user's request; restart each group from its root cause below, as serial verified units. Line references are approximate (at `404136bf`). On 2026-10-31, at R6-gen's exit (G11a), the **G** forms were probed under MIR code generation, the one code generator since R6-gen; each **G** bullet states the result.

## Completed (pushed, each with Unit evidence)

| Unit | Commit | Result |
| --- | --- | --- |
| U7-1 | `bdc55ed8` | A handle Moved or initialized on only some paths gets a live flag; the double release (use-after-free) is fixed. |
| U7-2 | `3b46e619` | Tests pin the counted-payload read-only rules, handle duplication and Moved-handle reuse, payload-follow Loans, escapes and retain-free views. |
| U7-3, 3b, 3c | `3f9caec1`, `adbc61ce`, `1146f7c7` | An unproven composite clause (`s is rc or arc`) is a located diagnostic (it faulted the checker); one clause-subject rule; the OCC-X limit only when it is a clause's whole remaining cause. |
| U7-4 | `b6bda043` | An owned obj/rc/arc parameter's payload slots are the caller's Loans (no self-Loan). |
| U7-5 | `549190fa` | Two vacuous tests now fail only on their rule. |
| U7-6 | `d910d72f` | Direct calls through an object callee (`h()`, `v()`, `h@follow()`) acquire the payload as the written follow does (they segfaulted, produced invalid IR or failed generation). |
| U7-7 | `404136bf` | Temporary object handles are borrowable through one admission rule and are Loan roots (two use-after-free cases fixed); `for` iteration-source temporaries last for the loop. |

## Remaining, by root cause

Severity: **W** wrong code, **G** accepted by check then `GenerationFailed_Kd` from the old generator (both deleted at R6-gen), **L** a Language code for a permitted form, **U** a located Unsupported code (acceptable boundary, to implement).

### R1. Selection does not model the object layer reached through reference layers (SPEC 3.4.3, 7.3, 13.5.5)

Selection passes `ref`/`uniq` layers and stops at an obj/rc/arc/objref/objuniq layer. No single function computes that stop and its authority; Binding, Ownership and Emission each peel one layer or guess from syntax.

- **W** Inherited (BaseBorrow) receivers through reference layers read the wrong address: getters and `ref/Self` methods through `ref/(ref/Leaf)`, `uniq/(uniq/Leaf)`, `ref/(rc|obj|objref/Leaf)`, `uniq/(obj|objref/Leaf)` (audit probes i01, i03, m01, m02, k09, k12, k13, n1, n10-n13). Cause: the receiver core is computed by duplicated one-layer expressions (`OwnershipAnalysis.Reservations` BaseBorrowTypes, `BodyLowering.StructBorrows` base check), and AdaptInput's projected branch records the unprepared SourceType.
- **G** Payload follows through a reference to a handle: `r@follow@follow@ref`, `slot@follow.read()`, Copy read `h@follow@follow` (slot: `ref/(rc/T)`), `return h@follow@follow`, `let c = r@follow` with `r: uniq/(uniq/T)` ("Reborrow has no matching reference source"). Cause: `OwnershipAnalysis.BorrowStruct` collapses a Follow into one Borrow on the name; the stored-reference sites require an exactly equal referent. Since R6-gen these build and run correctly (probes: `slot@follow@follow` and `slot@follow@follow@ref` with `slot: ref/(rc/i32)`, `slot@follow.read()` with `slot: ref/(rc/Cell)`, `let c = r@follow` with `r: uniq/(uniq/i32)`).
- **G/L** Writes through `uniq/(rc|arc/T)`: a custom setter `slot.level = 9` and compound assignment `slot.level += 1` report `InternalInvariant_Kd` at check (2026-10-31, probed through `uniq/(rc/T)`; PLAN's BU row); a Field write there reports `SharedPathAccess_Kd`; direct setters on rc/arc lack the shared-authority Note. Cause: `Binding.PathAuthority` returns Owner for members below a held handle; the BindReference write gate reads only the outer Semantics.
- **L** Implicit receivers at a held handle: `slot.read()` (`NoApplicableOverload_Kd`), `h.slot.look()`/`poke()` (`TransferRequired_Kd`), getters `slot.twice`.
- **U** Field reads `slot.id` through `ref/(rc/T)`; value-call callees reached through a reference or element (`r@follow()`, `holder.callback()`, `hs[0]()`, U7-6 limit).

Root fix: one selection model (all ref/uniq layers, then one object layer, with the nearest shared layer) used by Binding authority (PathAuthority, write gate, IsBarePlace), receiver adaptation (AdaptInput, ReceiverThroughLayers recording the prepared reference, TryPayloadProjection), Ownership (lend the held object's view then payload by reusing `BorrowStoredObject`'s tail; one "lends through the stored reference" predicate at every stored site); the Emission part (a base check failing closed) left with the old generator, and MIR code generation reads the base subobject (DECISIONS R5-M114, R5-M158: the five `InheritedObjectReceiverLayers*` fixtures pass natively; the audit probes were not re-run). Replace the separate `IndirectObjectCallee` limit in `Binding.ValueCalls`. Reviews: drop any accessor-only guard; check pair layers (weakest admitted case), get-only Property writes (keep their code), `@move` through a held handle, user-index writes.

### R2. In-place operand consumers bypass the common Place route (SPEC 13.4, 13.5.5)

- **W** String comparison use-after-free: `let r = s@ref; if r == take(s@move)` and `let view = values@ref; view[0] == take(values@move)` are accepted and read freed memory. Cause: the comparison is not a use of its operands, so the left reference's Loan ends before the right operand runs.
- **G** `h@follow@ref == "abc"`, `s@ref == "abc"`, `g@follow@ref.length`, `r@follow@ref[1]`. Cause (the old generator): `BodyLowering.ValidateStringInspection` and `LowerSequence` matched the operation's source by exact syntax equality. Since R6-gen these build and run correctly (probes: `h@follow@ref == "abc"` with `h: rc/string`, `s@ref == "abc"`, `g@follow@ref.length` and `g@follow@ref[1]` with `g: rc/Array<i32>`, `r@follow@ref[1]` with `r = values@ref`).
- **L** `show(h@follow@ref)` and `h@follow == "abc"` on string payloads (`TransferRequired_Kd`), interpolation `"\(h@follow)"`, `g@follow.length`, `g@follow[1]`; Field of a followed payload `a@follow.id`; Non-Copy Field comparison at a handle layer `a.name == "n"`, `r@follow.name == "n"`; `a@follow[i]`. Cause: string-argument inspection, CompareInPlace, SequenceReceiver and ReadSlice value-read the followed Place; `ElementAccess.FollowedReference` steps through Follow only.

Root fix: make the comparison value a use of both operand Places (no extra operation; a corrected design must not add a Read op, which breaks temporaries); one "on the operand's selection spine" rule for the lowering validators; route followed Places through `BorrowStruct` at every in-place consumer; let FollowedReference step through PayloadFollow.

### R3. Temporary Loan roots are an allow-list

`OwnershipBody.IsTemporaryOriginRoot` and the `BorrowStruct` value route keep separate lists of materialized temporary Types. Generic Parameter/AssociatedProjection/TargetProjection temporaries are not roots, so `keep<T>(make: () -> T)` with `make()@ref` used later passes the definition and fails only at an instance (SPEC 8.15; STATUS limit). Root fix: one shared predicate, including generic Types; run the whole suite.

### R4. obj payload content writes record no Loan on the handle

- **G** `local.value = m@ref` (local: `obj/Cell`, ref Field): the implicit Field write stores without recording the borrow's Loan (`OwnershipAnalysis.WriteBorrowedField`); the explicit `local@follow.value = m@ref` is `Unsupported_Kd`. Since R6-gen the implicit write builds and runs, reading back the written reference (probe: `struct Cell{q}` with `value: ref/i32 during q`); whether the handle records the borrow's Loan was not probed.
- **L** `var o = [4, 5]@obj; o@follow[0] = 7` is `InvalidAssignment_Kd` although a writable obj path grants Write.

Exclusive-object semantics belong to P39. For P40 completion, at least report both Origin-carrying spellings as one located Unsupported and fix the element-write category; recording Loans on the handle is the root fix.

### R5. Callee remainder

- **L** A pair-Semantics callee `v()` with `v: s/F` is `NotCallable_Kd`; `v@follow()` with `ref/s/F` is `UnresolvedBinding_Kd`.
- **U** A Consuming `h@follow()` of a Copy closure is `Unsupported_Kd` (SPEC 7.3 allows Copying the referent).

### Other

- **L** `func get(handle: rc/View) -> ref/i32 during handle.source` is `InvalidOriginBinding_Kd`: `Binding.Origins` resolves `x.source` through borrow layers only, not through an owning obj/rc/arc handle (the binding-set spelling `rc/View{a}` with `a.source` works).
- Non-blocking audit items (tests to add or STATUS limits): creation forms without a test (borrow/raw inputs, reboxing, independent input inference, generic object formation), objuniq and upcast rows for rc/arc, `during` on rc/arc, no implicit payload follow at fixed Types, object equality vs value equality, owning receivers through rc/arc, aggregate and scope-exit release order, destructor Abort, payload projections without Take.
- Outside P40, to file as PLAN issues: a borrow of a Moved Place also reports `ComparisonLoanConflict_Kd` beside `MovedPlace_Kd` (all Types); implicit `raw/F` calls need no unsafe context (SPEC 5.2); `Kimi.Weak<rc/T>` is a Language `UnresolvedBinding_Kd` (P41/G4); the omitted-base Unsupported has no evidence; exclusive base receivers through obj (`h.poke()`, OCC-X); a Never-typed `let` initializer reports `InternalInvariant_Kd`.

## Completion steps after the fixes

README P40 row and expected output; PLAN milestone table and U7; STATUS; CODEMAP; `src/Benchmark/SharedObjects.md`; closing Session with `-Milestone ((1..37) + 40)`; `--object-plans` and `benchmark-object-finalization.ps1` with nothing else running.
