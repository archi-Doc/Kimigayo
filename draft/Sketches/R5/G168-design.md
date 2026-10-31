# G168 design: `Field`'s Opaque after `Member` built the receiver

Base `d459bc5b8`. Design only; implementation waits for the planning session's GO. Probes: a scratch test (uncommitted) that
compiles each source with `MirBuildTest.Compile`, builds MIR (the driver runs A1-A5) and prints Binding's issues, each member
access's HIR facts, `program.Records`, `program.Findings`, module-0 problems and `MirDump`.

| Revision | Change |
| --- | --- |
| 0 (f579d4bd4) | Design. |
| 1 | R4's review 1 (REVISE): B1, rows c1Used and c3Moved with base's records, rows c2 and c3 called guards; `Reborrowed` folded in (R4 released its lines); (a)'s CLI result in item 1; N1 recorded as an open item; N2's window. The coordinator's addition: the interim for (a) lies partly in R4-held `Selections.cs`, so BU takes it; STATUS lines drafted. |

## Finding: valid programs reach `Field`'s failure

PLAN G168 says no valid program reaches it. SPEC 8.2.3's identity premise does: in `func f<T>(...)` with `T is Pair` (a complete
Type), Binding resolves a member of a Place of Type `T` through `Pair` (Field Op, Type, stored Field symbol, no issue), but the MIR
receiver's Type is `T`, which no builder rule or TyCtxt fact maps to `Pair`. A value receiver (`(s@move).a`, `take(s@move).a`,
`first(s, t@move).a`) has Binding's Type `Pair` and builds; a Place of Type `T` (a parameter, its Deref, an element) does not.

## The three cases

| Case | Reachability (probe, base) | Guarantee | Target |
| --- | --- | --- | --- |
| (c) the symbol is not a stored Field of the receiver's Type | Valid: `func f<T>(xs: ref/Array<T>, t: string) -> i32` / `T is Pair` / `return xs[pos(t@move)].a`: `Unsupported_Kd` at the member, site `opaque 0: _1 _2`, and A1's `MovedPlace_Kd` at the member (`t` moved by the index, named again by `SiteRoots`): F19's B1 on a valid program. Twin `Array<Pair>`: nothing. `s.a` with `s: ref/T` or `s: T`: `Unsupported_Kd`, site `_1`. | None: the HIR is valid (Binding's facts hold); the MIR lacks the identity. | A valid unimplemented form: the located `Unsupported_Kd` stays (not `InternalInvariant_Kd`); the site names only the receiver's root. |
| (a) the receiver Place has no Type | Valid: `T is Array<Pair>` with `s[pos(t@move)].a` (`s: ref/T`), `T is (Pair, i32)` with `s.0.a`: the receiver's Index or element projection over `T` has no Type, so `Field` records `Unsupported_Kd` (site `_1 _2` in the first, `_1` in the second), and `MirValidator` reports `Projection` for the body (no A1 problem follows). | MIR's own: `MirValidator.Admits` rejects every projection row without a Type, and every local has one (`TypeOf` falls back to Unit). So (a) occurs only beside the validator's finding on the receiver's projection, whose cause is the identity, not `Field`. | As (c): `Unsupported_Kd` (a valid form; the invalid projection is already reported by its validator), site the receiver's root. The finding goes to the open issue below. |
| (b) the Field's stored Type did not bind | Not reached. Probes: a Field whose Type `MISSING` spells (`(b@move).m`), an Origin-failed Field Type (`C{x}` / `origin x.s == zz`, read by `(d@move).h` and `take(d@move).h`), a `T.Missing` Field Type, `storage` in a getter and a setter of a `MISSING` Property: Binding rejects the access (no Type: an Error, or the whole `let` replaced, site `_1` only, no problem) or builds no accessor body. Reason: a Field Op has its Type (`HasFacts`: an operation needs it), `MemberRead`'s `PartTypeIn(declared, DeclaringType)` = `KeptType` + `ContractType`, and the stored Type is `WithoutRawOrigins(KeptType(written, layer))`: one producer (DECISIONS BU-50). The receiver's Type differs from the declaring Type only under an identity, which lands in (c). `storage` (Op Name, no Type required) needs a built accessor of a Property without a stored Type. | Binding's (BU-50), not a validator. | Derived (SPEC 23.1.2, R4-M109): no record beside Binding's; site the receiver's root only, so no moved root of the key's syntax (R4-M149). |

## Target behavior and hunk (prose)

`MirBuilder.Places.cs` `Field` (the file R4 released). Where no layer stores the symbol or the stored Type is missing, the
value is an Opaque in an Opaque temp of the node's Type (`opaqueTemps`; R4-M149: nothing lends it), whose site names only the
receiver Place's root, the part built already (R5-M63's rule as R5-M84 extended it), never the node's syntax roots: `roots` set to
`receiver.Root` (the caller-collected pattern of `Transfer` and `Defaults`), then `Unsupported(node, temp, node)` for (a) and (c)
(the one located record, skipped where Binding rejected the node) or `Site(node, temp, Completes(node))` for (b). The receiver's own
code (the index call's move, its records) is built before, unchanged: every independent record stays.

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
   premises, BU's, with G170, held until C3): a Place of Type `T` under `T is S` is projected as `T`. A Field is `Unsupported_Kd`
   (G168's (a) and (c)); an Index, constant index or Tuple element has no Type, and the valid program ends in `InternalInvariant_Kd`
   on both pipelines (CLI, base: "An internal compiler invariant was violated while analyzing this construct; no artifact is
   produced. Please report this input" at `func f<T>(s: ref/T, t: string) -> i32`, beside `Unsupported_Kd` at
   `s[pos(t@move)].a`).
2. **N1, one built root:** `Transfer`, `Defaults`, `Field` and `Reborrowed` each set `roots` to one built part's root by hand. One
   helper (a built root, then the site) would state R5-M63's rule once, but it must replace every use in its unit (AGENTS), and
   `Transfers.cs` and `Defaults.cs` are R4-held: a later unit, not part of G168.

## The interim for (a) (the coordinator's addition): outside the released lines

G168's `Field` hunk does not end (a) at one located `Unsupported_Kd`: the typeless projection is interned when the receiver is built,
before `Field` runs, and `MirValidator` checks every interned row with a prefix (`MirValidator.Tables.cs`, `Admits`), so the
`Projection` finding and its `InternalInvariant_Kd` stay. The typeless projections are formed in `MirBuilder.Selections.cs`
(`Element`'s `Index` row for `s[pos(t@move)]`, `ConstElement`'s `ConstIndex` row), which R4 holds and did not release, and in
`Places.cs` `Member`'s folded Tuple element (`s.0`). Covering only `s.0` would accept a narrow subset of one family, so G168 does
neither: BU takes the interim (each projection a Place of an identity-premise Type would form without a Type ends in the one located
`Unsupported_Kd`, its site the receiver's root; removal: when TyCtxt's identity query lands, the projection has its Type and the
record goes), with its row (exactly one located `Unsupported_Kd`, no `InternalInvariant_Kd`; fail-first on base) and STATUS's
boundary line (draft: Types and Contracts' remaining boundary gains "A Place of a Type parameter that a Constraint makes identical
to a complete Type (SPEC 8.2.3) is `Unsupported_Kd` in the MIR builder until TyCtxt's identity query (C3)"). Until that interim
lands, (a) is a known defect; G168's landing adds, if it lands first: `| Identity-premise Places | A valid generic body whose
Constraint makes a Type parameter identical to an Array or Tuple Type (SPEC 8.2.3) and selects an element of a Place of that
parameter reports InternalInvariant_Kd on both pipelines (PLAN identity Places). |`

## Tests

A `MirBuildTest.Places` theory, each row the records and module-0 problems (expectations: R4's review probe on base, and after):

| Row | Source (in `func f<T>(…)`, `T is Pair`) | Base | After G168 | Kind |
| --- | --- | --- | --- | --- |
| c1 | `return xs[pos(t@move)].a`, `xs: ref/Array<T>` | `Unsupported_Kd` and `MovedPlace_Kd` at the member | `Unsupported_Kd` at the member; site `_1` only | fail-first |
| c1Used | `let v = xs[pos(t@move)].a`, `use(t@move)` | as c1, plus `MovedPlace_Kd` at `t` | `Unsupported_Kd` at the member, `MovedPlace_Kd` at `t` only | fail-first; independent record |
| c3Moved | `let m = s@move`, `return s.a`, `s: T` | `Unsupported_Kd` and `MovedPlace_Kd` at `s.a` | the same | guard: independent record (a hunk naming no root fails it) |
| c2 | `return s.a`, `s: ref/T` | `Unsupported_Kd` at `s.a` | the same; site `_1` | guard |
| c3 | `return s.a`, `s: T` | `Unsupported_Kd` at `s.a` | the same; site `_1` | guard |

No invariant test: (a) and (c) are not broken contracts, and `Reborrowed`'s path has no input. No (b) row. (a)'s row is the
interim's (BU).

## Classification, size and window

Major: one owning component and its contract; the PLAN row's premise changes. No DECISIONS entry (R5-M63/R5-M84's rule, no new
representation or vocabulary). PLAN: G168 closes; items 1 (the planning session writes it) and 2 open. Size: `Places.cs` about +9/-2
code and +3 comment lines, `Acquisitions.cs` about +4/-1, the test about +30 lines. Window: the default Session with
`-DiagnosticSnapshot` against `d459bc5b8`, `-Pipeline mir`, sel where it applies, and the full mir ledger from its own run. Expected
ledger movement: only rows where a false member `MovedPlace_Kd` beside an `Unsupported_Kd` goes (none expected: F19 round 2 found no
MIR-side `Unsupported_Kd` at a `MemberAccessKoto` in the suite), each moved row classified by its probe; any A1-A6 verdict change
otherwise stops the unit.
