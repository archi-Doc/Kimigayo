# G168 design: `Field`'s Opaque after `Member` built the receiver

Base `d459bc5b8`. Design only; implementation waits for the planning session's GO. Probes: a scratch test (uncommitted) that
compiles each source with `MirBuildTest.Compile`, builds MIR (the driver runs A1-A5) and prints Binding's issues, each member
access's HIR facts, `program.Records`, `program.Findings`, module-0 problems and `MirDump`.

| Revision | Change |
| --- | --- |
| 0 (f579d4bd4) | Design. |
| 1 | R4's review 1 (REVISE): B1, rows c1Used and c3Moved with base's records, rows c2 and c3 called guards; `Reborrowed` folded in (R4 released its lines); (a)'s CLI result in item 1; N1 recorded as an open item; N2's window. The coordinator's addition: the interim for (a) lies partly in R4-held `Selections.cs`, so BU takes it; STATUS lines drafted. |
| 1a | Window: no sel lane (R4's correction); the newest shared base. |
| 2 | R4 released `Element`/`ConstElement`: the identity interim (R4's four conditions: Binding's published identity-substituted Type as the positive guard, one rule with `Field`'s and its removal condition, rows a1-a4 on both pipelines, A1-A6 after the fix); `Field`'s non-identity failure is `InternalInvariant_Kd`; the STATUS boundary line replaces the known-defect draft; a DECISIONS entry. |

## Finding: valid programs reach `Field`'s failure

PLAN G168 says no valid program reaches it. SPEC 8.2.3's identity premise does: in `func f<T>(...)` with `T is Pair` (a complete
Type), Binding resolves a member of a Place of Type `T` through `Pair` (Field Op, Type, stored Field symbol, no issue), but the MIR
receiver's Type is `T`, which no builder rule or TyCtxt fact maps to `Pair`. A value receiver (`(s@move).a`, `take(s@move).a`,
`first(s, t@move).a`) has Binding's Type `Pair` and builds; a Place of Type `T` (a parameter, its Deref, an element) does not.

## The three cases

| Case | Reachability (probe, base) | Guarantee | Target |
| --- | --- | --- | --- |
| (c) the symbol is not a stored Field of the receiver's Type | Valid: `func f<T>(xs: ref/Array<T>, t: string) -> i32` / `T is Pair` / `return xs[pos(t@move)].a`: `Unsupported_Kd` at the member, site `opaque 0: _1 _2`, and A1's `MovedPlace_Kd` at the member (`t` moved by the index, named again by `SiteRoots`): F19's B1 on a valid program. Twin `Array<Pair>`: nothing. `s.a` with `s: ref/T` or `s: T`: `Unsupported_Kd`, site `_1`. | None: the HIR is valid (Binding's facts hold); the MIR lacks the identity. | A valid unimplemented form: the located `Unsupported_Kd` stays (not `InternalInvariant_Kd`); the site names only the receiver's root. Revision 2: decided by the identity guard before `Field`; without the identity, a broken contract (`InternalInvariant_Kd`). |
| (a) the receiver Place has no Type | Valid: `T is Array<Pair>` with `s[pos(t@move)].a` (`s: ref/T`), `T is (Pair, i32)` with `s.0.a`: the receiver's Index or element projection over `T` has no Type, so `Field` records `Unsupported_Kd` (site `_1 _2` in the first, `_1` in the second), and `MirValidator` reports `Projection` for the body (no A1 problem follows). | MIR's own: `MirValidator.Admits` rejects every projection row without a Type, and every local has one (`TypeOf` falls back to Unit). So (a) occurs only beside the validator's finding on the receiver's projection, whose cause is the identity, not `Field`. | Revision 2: the identity interim forms no typeless projection: one located `Unsupported_Kd` at the projecting node, site the receiver's root, no finding. `Field` over a typeless receiver otherwise: a broken contract. |
| (b) the Field's stored Type did not bind | Not reached. Probes: a Field whose Type `MISSING` spells (`(b@move).m`), an Origin-failed Field Type (`C{x}` / `origin x.s == zz`, read by `(d@move).h` and `take(d@move).h`), a `T.Missing` Field Type, `storage` in a getter and a setter of a `MISSING` Property: Binding rejects the access (no Type: an Error, or the whole `let` replaced, site `_1` only, no problem) or builds no accessor body. Reason: a Field Op has its Type (`HasFacts`: an operation needs it), `MemberRead`'s `PartTypeIn(declared, DeclaringType)` = `KeptType` + `ContractType`, and the stored Type is `WithoutRawOrigins(KeptType(written, layer))`: one producer (DECISIONS BU-50). The receiver's Type differs from the declaring Type only under an identity, which lands in (c). `storage` (Op Name, no Type required) needs a built accessor of a Property without a stored Type. | Binding's (BU-50), not a validator. | Derived (SPEC 23.1.2, R4-M109): no record beside Binding's; site the receiver's root only, so no moved root of the key's syntax (R4-M149). |

## Target behavior and hunk (prose)

`MirBuilder.Places.cs` `Field` (the file R4 released). Where no layer stores the symbol or the stored Type is missing, the
value is an Opaque in an Opaque temp of the node's Type (`opaqueTemps`; R4-M149: nothing lends it), whose site names only the
receiver Place's root, the part built already (R5-M63's rule as R5-M84 extended it), never the node's syntax roots: `roots` set to
`receiver.Root` (the caller-collected pattern of `Transfer` and `Defaults`), then `Unsupported(node, temp, node)` for (a) and (c)
(the one located record, skipped where Binding rejected the node) or `Site(node, temp, Completes(node))` for (b). The receiver's own
code (the index call's move, its records) is built before, unchanged: every independent record stays. Revision 2 narrows (a) and
(c): the identity is decided before `Field` (next section), so `Field`'s remaining "no layer stores it" is a broken contract
(`InternalInvariant_Kd`, the same site; validated HIR: a Field Op has its Type, `HasFacts`, and Binding's member selection names a
stored Field of the receiver's published Type, BU-50), and (b) stays derived.

Other callers: `Storage` from `Named` (an implicit `self` Field, an accessor's `storage`) now names `self`'s root where its site named
nothing (reached only through (b)); `Storage` from an update's standard accessor names the located receiver root, as `SiteRoots`
did. `Values.cs` and `Opaque*.cs` are unchanged.

`MirBuilder.Acquisitions.cs` `Reborrowed` (revision 1; R4 released these lines): its `InternalInvariant_Kd` path calls
`Unsupported(node, derived: true, stored)` after `place` was built, so `SiteRoots(node)` names the node's syntax roots again (B1's
family; the record does not stop A1-A6, which can add a false `MovedPlace_Kd` beside it). The same rule: an Opaque temp of `stored`,
`roots` set to `place.Root`, `Site(node, temp, Completes(node))`, no second record. No input reaches it (a broken contract: Binding's
expected Type of a Reborrow is a same-mode reference to the stored referent, BU-27), so no test; the change is the site's roots only.

## Same-cause search

Every `Unsupported(`/`Opaque(` caller in `Mir/Build`: only `Reborrowed` forms a `SiteRoots` site after building the node's Place
(folded above). The others form the Opaque before any part is built; `Transfer` and `Defaults` collect their own roots.

Open items:

1. **Identity Places** (SPEC 8.2.3; PLAN item written by the planning session; by meaning TyCtxt's identical Type under the
   premises, BU's, with G170, held until C3; it removes G168's interim): a Place of Type `T` under `T is S` is projected as `T`. A Field is `Unsupported_Kd`
   (G168's (a) and (c)); an Index, constant index or Tuple element has no Type, and on base the valid program ends in
   `InternalInvariant_Kd` on both pipelines until G168's interim makes it one located `Unsupported_Kd` (CLI, base: "An internal compiler invariant was violated while analyzing this construct; no artifact is
   produced. Please report this input" at `func f<T>(s: ref/T, t: string) -> i32`, beside `Unsupported_Kd` at
   `s[pos(t@move)].a`).
2. **N1, one built root:** `Transfer`, `Defaults`, `Field` and `Reborrowed` each set `roots` to one built part's root by hand. One
   helper (a built root, then the site) would state R5-M63's rule once, but it must replace every use in its unit (AGENTS), and
   `Transfers.cs` and `Defaults.cs` are R4-held: a later unit, not part of G168.

## The identity interim (revision 2; R4 released `Element` and `ConstElement` in `MirBuilder.Selections.cs`)

Why G168's `Field` hunk alone is not enough: the typeless projection is interned when the receiver is built, before `Field` runs,
and `MirValidator` checks every interned row with a prefix (`MirValidator.Tables.cs`, `Admits`), so (a) keeps its `Projection`
finding and its `InternalInvariant_Kd`. The interim decides before any projection is formed.

**The positive fact (condition 1).** Binding gives every expression the one Type its scope's Type-identity premises make of its
Types (`Binding.BindAndAdaptNode`, `SubstituteIdentityPremises`; owner Binding's `Binding.TypeIdentity`, SPEC 8.2, 8.2.3: only
premises whose subject is a Type parameter are applied), and the HIR publishes it in the Meaning column (`HirTables.Meaning`). Probe
(base): `s` in `func f<T>(s: ref/T…)` / `T is Array<Pair>` is published `ref/Array<Pair>` (with `T is (Pair, i32)`:
`ref/(Pair, i32)`; with `T is (Pair, U)`: `ref/(Pair, U)`), while its MIR local is `ref/T`. The guard `Identical(place, node)`:
the Place the builder formed for a node has a Type parameter's Type (`BoundTypeKind.Parameter`), and the node's published Type,
followed through the node's published Layers by `TyCtxt.Layer` (the rule `Follow` applies to the Place), is not a Type parameter.
Inside its own body Binding replaces a Type parameter in an expression's Type only by an identity premise, so the guard is that
premise's published effect; it never tests that a projection has no Type. Any other typeless projection still reaches
`MirValidator` (a broken contract stays `InternalInvariant_Kd`).

**One rule with `Field`'s (condition 2).** Where the guard holds on the receiver a projection would select from, the node is the
one located `Unsupported_Kd` (`Unsupported(node, temp, node)`), its value an Opaque in an Opaque temp of the node's published Type,
the site naming only the receiver's root, and no projection is formed: `Member` (before a folded Tuple element or `Field`, receiver
`member.Left`), `Element` (receiver `index.Left`; right after the receiver is built, before `Handle`, the guard borrow, `Length` or a
key's resolution read its Type: the index, a position, `^x`, a range or a key, is built as an operand, so its own code and records
stay) and `ConstElement` (receiver `index.Left`). One helper in `Places.cs` holds the guard and that record; its
comment states the temporary structure and its removal: when TyCtxt's identity query lands (BU, with G170 after C3), the builder
projects with the identical Type and the helper, its calls and the `Unsupported_Kd` go. The PLAN identity-Places item names the same
condition.

**No premise (condition 3's guard row).** None reachable. Probed: with the invalid `T is ref/Pair` (`InvalidConstraint_Kd`),
`s@follow.a` and `s.a` are Errors (`TypeMismatch_Kd`, `UnresolvedBinding_Kd`); without a premise, `s.a` on a bare `T` has no member
to select (not probed; the implementation adds it to the probe set). A typeless projection without the identity is reached only by a broken contract, so there is no row; its
validator finding is unchanged.

**A1-A6 after the fix (condition 4).** Today a validator finding stops A1-A6 for these bodies; after the fix they are analyzed. Only
the Opaque's own effect is expected (its site reads the receiver's root); the window's ledger classifies any new A1-A6 record, and
anything else stops the unit and goes to R4.

**STATUS** (replacing the known-defect draft): the Types and Contracts row's remaining boundary gains "A Place of a Type parameter
that a Constraint makes identical to a complete Type (SPEC 8.2.3), selected by a Field, an index or a Tuple element, is
`Unsupported_Kd` until TyCtxt's identity query (PLAN identity Places)."

## Tests

A `MirBuildTest.Places` theory, each row the records and module-0 problems (expectations: R4's review probe on base, and after):

| Row | Source (in `func f<T>(…)`, `T is Pair`) | Base | After G168 | Kind |
| --- | --- | --- | --- | --- |
| c1 | `return xs[pos(t@move)].a`, `xs: ref/Array<T>` | `Unsupported_Kd` and `MovedPlace_Kd` at the member | `Unsupported_Kd` at the member; site `_1` only | fail-first |
| c1Used | `let v = xs[pos(t@move)].a`, `use(t@move)` | as c1, plus `MovedPlace_Kd` at `t` | `Unsupported_Kd` at the member, `MovedPlace_Kd` at `t` only | fail-first; independent record |
| c3Moved | `let m = s@move`, `return s.a`, `s: T` | `Unsupported_Kd` and `MovedPlace_Kd` at `s.a` | the same | guard: independent record (a hunk naming no root fails it) |
| c2 | `return s.a`, `s: ref/T` | `Unsupported_Kd` at `s.a` | the same; site `_1` | guard |
| c3 | `return s.a`, `s: T` | `Unsupported_Kd` at `s.a` | the same; site `_1` | guard |

Rows c1-c3 keep their records under the interim (c1's receiver `(*_1)[_6]` is of `T`, the index node published `Pair`: the
guard holds at the member). Identity rows (revision 2): each compiled with `Compilation.Pipeline` set to the default and to the MIR
pipeline (as `PipelineSwitchTest` sets it), asserting the published diagnostics: exactly one located `Unsupported_Kd`, no
`InternalInvariant_Kd`, no `program.Findings`, and no module-0 problem.

| Row | Source (`s: ref/T` unless stated) | Base | After | Kind |
| --- | --- | --- | --- | --- |
| a1 | `T is Array<Pair>`, `return s[pos(t@move)].a` | `Unsupported_Kd` at the member, `Projection` finding, CLI `InternalInvariant_Kd` | one `Unsupported_Kd` at `s[pos(t@move)]`, site `_1` | fail-first |
| a2 | `T is (Pair, i32)`, `return s.0.a` | `Unsupported_Kd` at the member, `Projection` finding | one `Unsupported_Kd` at `s.0` | fail-first |
| a3 | `T is (Pair, U)` in `func f<T, U>`, `return s.0.a` | as a2 (published `ref/(Pair, U)`) | as a2 | fail-first; the rule's other form |
| a4 | `T is [2 of Pair]`, `return s[0].a` (`ConstElement`) | expected as a2 (confirm on base) | one `Unsupported_Kd` at `s[0]` | fail-first |

No invariant test: no input reaches `Field`'s or `Reborrowed`'s broken-contract path or a typeless projection without the premise.
No (b) row.

## Classification, size and window

Major: one owning component (the MIR builder) and its contract; the PLAN row's premise changes. DECISIONS entry (revision 2): the
identity interim is a temporary structure (AGENTS), so the landing records it with its guard, its removal condition and `Field`'s
broken-contract classification; the planning session numbers it. PLAN: G168 closes; items 1 (the planning session writes it, with the
removal condition) and 2 open. Size: `Places.cs` about +25/-2 (the helper, `Member`, `Field`), `Selections.cs` about +6,
`Acquisitions.cs` about +4/-1, the tests about +50 lines. Window: the default Session with
`-DiagnosticSnapshot` against the newest shared base and R5's own full mir ledger (a `-Pipeline mir` Session optional; no sel lane). Expected
ledger movement: only rows where a false member `MovedPlace_Kd` beside an `Unsupported_Kd` goes, or an identity body that the validator
used to stop is analyzed with the Opaque's own effect (none expected: F19 round 2 found no MIR-side `Unsupported_Kd` at a
`MemberAccessKoto` in the suite), each moved row classified by its probe; any other A1-A6 record or verdict change stops the unit and
goes to R4.
