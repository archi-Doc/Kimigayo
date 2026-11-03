# G168 design: `Field`'s Opaque after `Member` built the receiver

Base `d459bc5b8`. Design only; implementation waits for the planning session's GO. Probes: a scratch test (uncommitted) that
compiles each source with `MirBuildTest.Compile`, builds MIR (the driver runs A1-A5) and prints Binding's issues, each member
access's HIR facts, `program.Records`, `program.Findings`, module-0 problems and `MirDump`; R4's review probes on base.

| Revision | Change |
| --- | --- |
| 0 (f579d4bd4) | Design. |
| 1 (b1cae31a4) | R4's review 1: rows c1Used and c3Moved; c2 and c3 guards; `Reborrowed` folded in; (a)'s CLI result; N1 an open item. |
| 1a (82e438ab6) | Window: no sel lane; the newest shared base. |
| 2 (95ecb479e) | R4 released `Element`/`ConstElement`: an identity interim with a mismatch guard. |
| 3 (af28c82ad) | R4's review 2: the guard must be a positive premise fact; none is published for the builder. |
| 4 | The planning ruling: split. G168 is the site rule only; the identity interim becomes G168b, after BU publishes the premise fact. |

## Finding: valid programs reach `Field`'s failure

PLAN G168 says no valid program reaches it. SPEC 8.2.3's identity premise does: in `func f<T>(...)` with `T is Pair` (a complete
Type), Binding resolves a member of a Place of Type `T` through `Pair` (Field Op, Type, stored Field symbol, no issue), but the MIR
receiver's Type is `T`, which no builder rule or TyCtxt fact maps to `Pair`. A value receiver (`(s@move).a`, `take(s@move).a`,
`first(s, t@move).a`) has Binding's Type `Pair` and builds; a Place of Type `T` (a parameter, its Deref, an element) does not.

## The three cases (G168)

| Case | Reachability (probe, base) | Guarantee | Target in G168 |
| --- | --- | --- | --- |
| (c) the symbol is not a stored Field of the receiver's Type | Valid: `func f<T>(xs: ref/Array<T>, t: string) -> i32` / `T is Pair` / `return xs[pos(t@move)].a`: `Unsupported_Kd` at the member, site `_1 _2`, and A1's `MovedPlace_Kd` at the member (`t` moved by the index, named again by `SiteRoots`): F19's B1 on a valid program. Twin `Array<Pair>`: nothing. `s.a` with `s: ref/T` or `s: T`: `Unsupported_Kd`, site `_1`. | None: the HIR is valid; the MIR lacks the identity. | The located `Unsupported_Kd` stays; the site names only the receiver's root. |
| (a) the receiver Place has no Type | Valid: `T is Array<Pair>` with `s[pos(t@move)].a`, `T is (Pair, i32)` with `s.0.a`: the receiver's Index or element projection over `T` has no Type; `Field` records `Unsupported_Kd`, and `MirValidator` reports `Projection` (CLI: `InternalInvariant_Kd` on both pipelines). | MIR's own: `MirValidator.Admits` rejects every projection row without a Type. | Unchanged in G168 (a known defect, below); the same site rule applies to `Field`'s record. G168b ends it. |
| (b) the Field's stored Type did not bind | Not reached: a Field whose Type `MISSING` spells, an Origin-failed Field Type, a `T.Missing` Field Type, `storage` in a getter or setter of a `MISSING` Property: Binding rejects the access (an Error, or the whole `let` replaced) or builds no accessor body. A Field Op has its Type (`HasFacts`), and the member's Type and the stored Type come from one producer, `KeptType` (DECISIONS BU-50). | Binding's (BU-50). | Derived (SPEC 23.1.2, R4-M109): no record; site the receiver's root only (R4-M149). |

`Field` cannot tell (a) and (c) from a broken contract without the premise fact, so in G168 both keep `Unsupported_Kd` (c2 and c3
are valid forms and guards). `Field`'s `InternalInvariant_Kd` for a non-identity failure needs that fact and goes to G168b; the
broken-contract `InternalInvariant_Kd` in G168 is `Reborrowed`'s, unchanged.

## Hunk (prose)

- `MirBuilder.Places.cs` `Field` (released by R4): where no layer stores the symbol or the stored Type is missing, the value is an
  Opaque in an Opaque temp of the node's Type (`opaqueTemps`; R4-M149: nothing lends it), whose site names only the receiver Place's
  root, the part built already (R5-M63's rule as R5-M84 extended it), never the node's syntax roots: `roots` set to `receiver.Root`
  (the caller-collected pattern of `Transfer` and `Defaults`), then `Unsupported(node, temp, node)` for (a) and (c) (the one located
  record, skipped where Binding rejected the node) or `Site(node, temp, Completes(node))` for (b). The receiver's own code (the index
  call's move, its records) is built before, unchanged, so every independent record stays. Other callers: `Storage` from `Named`
  (`self`'s Field, an accessor's `storage`) now names `self`'s root (reached only through (b)); `Storage` from an update's standard
  accessor names the located receiver root, as `SiteRoots` did.
- `MirBuilder.Acquisitions.cs` `Reborrowed` (lines released by R4): its `InternalInvariant_Kd` path calls
  `Unsupported(node, derived: true, stored)` after `place` was built, so `SiteRoots(node)` names the node's syntax roots again (the
  record does not stop A1-A6, which can add a false `MovedPlace_Kd`). The same rule: an Opaque temp of `stored`, `roots` set to
  `place.Root`, `Site(node, temp, Completes(node))`, no second record. No input reaches it (BU-27), so no test.

Same-cause search: of every `Unsupported(`/`Opaque(` caller in `Mir/Build`, only `Reborrowed` forms a `SiteRoots` site after
building the node's Place; the others form the Opaque before any part is built.

## Tests (G168)

A `MirBuildTest.Places` theory, each row the records and module-0 problems (expectations: R4's review probe on base, and after):

| Row | Source (in `func f<T>(…)`, `T is Pair`) | Base | After G168 | Kind |
| --- | --- | --- | --- | --- |
| c1 | `return xs[pos(t@move)].a`, `xs: ref/Array<T>` | `Unsupported_Kd` and `MovedPlace_Kd` at the member | `Unsupported_Kd` at the member; site `_1` only | fail-first |
| c1Used | `let v = xs[pos(t@move)].a`, `use(t@move)` | as c1, plus `MovedPlace_Kd` at `t` | `Unsupported_Kd` at the member, `MovedPlace_Kd` at `t` only | fail-first; independent record |
| c3Moved | `let m = s@move`, `return s.a`, `s: T` | `Unsupported_Kd` and `MovedPlace_Kd` at `s.a` | the same | guard: independent record (a hunk naming no root fails it) |
| c2 | `return s.a`, `s: ref/T` | `Unsupported_Kd` at `s.a` | the same; site `_1` | guard |
| c3 | `return s.a`, `s: T` | `Unsupported_Kd` at `s.a` | the same; site `_1` | guard |

bareT (no premise, `s.a` on a bare `T`) is Binding's `UnresolvedBinding_Kd` at `s.a`, an Error node (R4's review 2 probe): no
`Field` call, no row. No invariant test (no input reaches `Reborrowed`'s path). No (b) row.

## STATUS, classification, size and window (G168)

- STATUS known-defect line, in the landing commit: `| Identity-premise Places | A valid generic body whose Constraint makes a Type
  parameter identical to an Array or Tuple Type (SPEC 8.2.3) and selects an element of a Place of that parameter
  (`T is Array<Pair>`: `s[i].a`; `T is (Pair, i32)`: `s.0.a`) reports InternalInvariant_Kd ("An internal compiler invariant was
  violated while analyzing this construct; no artifact is produced. Please report this input") at the function on the default and
  MIR pipelines, beside Unsupported_Kd at the member (PLAN identity Places). |`
- Major: one owning component and its contract; the PLAN row's premise changes. No DECISIONS entry (R5-M63/R5-M84's rule; no new
  representation, vocabulary or temporary structure). PLAN: G168 closes; the identity-Places item (the planning session writes it)
  and N1 (below) open.
- Size: `Places.cs` about +9/-2 code and +3 comment lines, `Acquisitions.cs` about +4/-1, the test about +30 lines.
- Window: the default Session with `-DiagnosticSnapshot` against the newest shared base and R5's own full mir ledger (a `-Pipeline mir`
  Session optional). Published records do not move (each `Unsupported_Kd` stays at its member). Expected ledger movement: only rows
  where a false member `MovedPlace_Kd` beside an `Unsupported_Kd` goes (none expected: F19 round 2 found no MIR-side `Unsupported_Kd`
  at a `MemberAccessKoto` in the suite), each classified by its probe; any other A1-A6 change stops the unit and goes to R4.
- N1 (open item): `Transfer`, `Defaults`, `Field` and `Reborrowed` each set `roots` to one built part's root by hand. One helper would
  state R5-M63's rule once, but it must replace every use in its unit, and `Transfers.cs` and `Defaults.cs` are R4-held.

## G168b, after BU publishes the premise fact (blocked on the owner's word)

**Needed publication (owner BU):** a premise fact the builder may read: a TyCtxt query `IdentityPremise(definition, P)` answering from
`EffectPremises(definition)` with Binding's `AvailableConstraintFact`, or Binding's image per node or scope. None exists on base: the
HIR columns carry no premise (Meaning carries only the substitution's result, `BindAndAdaptNode`); TyCtxt has no identity query
(`TyCtxt.Effects` reads `EffectPremises` only for Copy and Sealed); the facts live in Binding's internal `BindingScope.Constraints`
(`ConstraintEnvironment`), and availability is Binding's private `AvailableConstraintFact`. TyCtxt's identity query (open item:
identity Places, with G170 after C3) can later replace it.

**Predicate, nothing inferred:** `TypeOf(place)` is a Type parameter P, AND the enclosing declaration's published premises contain an
available `TypeIdentity` fact whose subject is P (read, never recomputed; `T is U`, `U is Pair` passes on the premise with subject T),
AND the node's published Type through its Layers (`TyCtxt.Layer`) is not P.

**Rule (one with `Field`'s):** where the predicate holds on the receiver a projection would select from, the node is the one located
`Unsupported_Kd`, its value an Opaque in an Opaque temp of the node's published Type, its site only the receiver's root, and no
projection is formed: in `Member` (before a folded Tuple element or `Field`), `Element` (right after the receiver is built, before
`Handle`, the guard borrow, `Length` or a key's resolution read its Type; the index is built as an operand, so its code and records
stay) and `ConstElement`. `Field`'s remaining failure without the identity is then a broken contract, `InternalInvariant_Kd` (a Field
Op has its Type, `HasFacts`; the selection names a stored Field of the receiver's published Type, BU-50). One helper in `Places.cs`;
its comment and the PLAN item state the removal: when TyCtxt's identity query lands, the projection gets its Type and the helper, its
calls and the `Unsupported_Kd` go. A DECISIONS entry records the temporary structure.

**Rows** (each on the default and the MIR pipeline via `Compilation.Pipeline`, asserting exactly one located `Unsupported_Kd`, no
`InternalInvariant_Kd`, no findings, no module-0 problem):

| Row | Source (`s: ref/T` unless stated) | Base | After | Kind |
| --- | --- | --- | --- | --- |
| a1 | `T is Array<Pair>`, `return s[pos(t@move)].a` | `Unsupported_Kd` at the member, `Projection` finding, CLI `InternalInvariant_Kd` | one `Unsupported_Kd` at `s[pos(t@move)]`, site `_1` | fail-first |
| a2 | `T is (Pair, i32)`, `return s.0.a` | `Unsupported_Kd` at the member, `Projection` finding | one `Unsupported_Kd` at `s.0` | fail-first |
| a3 | `T is (Pair, U)` in `func f<T, U>`, `return s.0.a` | as a2 | as a2 | fail-first |
| a4 | `T is [2 of Pair]`, `return s[0].a` | as a2 (R4's review 2 probe) | one `Unsupported_Kd` at `s[0]` | fail-first |
| chainTU | `T is U`, `U is Pair` in `func f<T, U>`, `return s.a` | `Unsupported_Kd` at `s.a` | the same | guard: the identity's closure |

No premise: none reachable (bareT is Binding's Error; `T is ref/Pair` is `InvalidConstraint_Kd`, its uses Errors), so a typeless
projection without the identity keeps its validator finding. A1-A6 then analyze bodies the validator used to stop: only the Opaque's
own effect is expected; any other record stops the unit and goes to R4. The window's snapshot classifies each identity form's
`Unsupported_Kd` moving from the member to the projecting node as expected. STATUS: the known-defect line becomes the boundary "A
Place of a Type parameter that a Constraint makes identical to a complete Type (SPEC 8.2.3), selected by a Field, an index or a
Tuple element, is `Unsupported_Kd` until TyCtxt's identity query (PLAN identity Places)."
