# G168 design: `Field`'s Opaque after `Member` built the receiver

Base `d459bc5b8`. Design only; implementation waits for the planning session's GO. Probes: a scratch test (uncommitted) that
compiles each source with `MirBuildTest.Compile`, builds MIR (the driver runs A1-A5) and prints Binding's issues, each member
access's HIR facts, `program.Records`, `program.Findings`, module-0 problems and `MirDump`.

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

`MirBuilder.Places.cs` `Field` only (the file R4 released). Where no layer stores the symbol or the stored Type is missing, the
value is an Opaque in an Opaque temp of the node's Type (`opaqueTemps`; R4-M149: nothing lends it), whose site names only the
receiver Place's root, the part built already (R5-M63's rule as R5-M84 extended it), never the node's syntax roots: `roots` set to
`receiver.Root` (the caller-collected pattern of `Transfer` and `Defaults`), then `Unsupported(node, temp, node)` for (a) and (c)
(the one located record, skipped where Binding rejected the node) or `Site(node, temp, Completes(node))` for (b). The receiver's own
code (the index call's move, its records) is built before, unchanged: every independent record stays.

Other callers: `Storage` from `Named` (an implicit `self` Field, an accessor's `storage`) now names `self`'s root where its site named
nothing (reached only through (b)); `Storage` from an update's standard accessor names the located receiver root, as `SiteRoots`
did. `Values.cs` and `Opaque*.cs` are unchanged.

## Same-cause search

Every `Unsupported(`/`Opaque(` caller in `Mir/Build`: only `Reborrowed` (`MirBuilder.Acquisitions.cs:100`, R4-held) forms a
`SiteRoots` site after building the node's Place, on its `InternalInvariant_Kd` path only. The others form the Opaque before any
part is built (Defaults and Transfer collect their own roots). Proposed open issues:

1. **Identity Places** (SPEC 8.2.3): a Place of Type `T` under `T is S` (struct, Array, Tuple) is projected as `T`: a Field is
   `Unsupported_Kd`, an Index or element projection has no Type, a `MirValidator` `Projection` finding on a valid program. The owner
   publishing the identical Type (TyCtxt or Binding) is to be decided.
2. **`Reborrowed`'s site** names the node's syntax after its Place was built (B1's family, broken-contract path only), for R4.

## Tests

- Fail-first (a valid program, since (b) has no reproducer): a `MirBuildTest.Places` theory, rows (1) `xs[pos(t@move)].a` over
  `ref/Array<T>`: one `Unsupported_Kd` at the member, site `_1` only, no module-0 `MovedPlace_Kd` (before: site `_1 _2`,
  `MovedPlace_Kd`); (2) `s.a` with `s: ref/T`, and (3) with `s: T`: `Unsupported_Kd`, site `_1` (the rule's other forms).
- No invariant test: (a) and (c) are not broken contracts. (a)'s rows wait for issue 1 (its validator finding). No (b) row.

## Classification and size

Major: one owning component and its contract, but the search found a same-cause site (held file) and the PLAN row's premise
changes. No DECISIONS entry (R5-M63/R5-M84's rule, no new representation or vocabulary). PLAN: G168 closes; issues 1 and 2 open.
Size: `Places.cs` about +9/-2 code and +3 comment lines; the test about +25 lines. Window: default Session with
`-DiagnosticSnapshot` against `d459bc5b8` and the full mir ledger; no moved row expected (F19 round 2: no MIR-side
`Unsupported_Kd` at a `MemberAccessKoto` in the suite).
