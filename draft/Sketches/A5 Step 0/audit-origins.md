# Step 0 audit: F4 Origin relations and the Origin boundary table

Base `b2edea8b` (worktree `bin/worktrees/a5-step0`). This is a read-only audit: nothing was built or run.

Citation keys:
- `S15:n` is line n of `docs/spec/15-ownership-and-lifetime-analysis.md`; `S8`, `S10`, `S11`, `S13` and `S14` are the same for chapters 08, 10, 11, 13 and 14.
- `B/` is `src/Kimi/Compiler/Binding/`, `A/` is `src/Kimi/Compiler/Analysis/` and `T/` is `tests/xUnitTest/Tests/`.
- `P:n` is a line of `scratchpad/step0/plan-snapshot.md`.
- `DEC:n` is a line of `docs/dev/DECISIONS.md`.
- `P10:n` is a line of `draft/Proposals/2026-10-10 Compiler Size Reduction and Single MIR Plan.md`, and `OBS13:n` a line of `draft/Obsolete/2026-10-13 R4 Completion Plan and Budget.md`.
- (H) marks an unverified hypothesis.

## Summary

- All five F4 rows can be expressed as finite facts and rules, and none needs S1 or S2. Rows 9c and 20 need only MIR and the existing atom classification. Rows 7, 13 and 19 need Binding views. Row 13's mutable-static bound also needs A4's per-result anchors.
- A5 needs no region sets. Only chains produce errors (Refuted, Unknown). Region inference has no observable output: regions keep no Loan live (S15:456), "Proven" only constrains inference (S15:574), and a use past a Place's lifetime is a Loan conflict (S15:577).
- **Strata.** Origin judgment reads relation facts, the atom classification, premises and case masks. It reads no liveness, no CFG edges and no `carries`. Loan flow reads nothing from Origin judgment, only the contract-level `retains` (S15:529).
- **S3: confirmed.** §15.3.6 alone admits two solutions with different verdicts for one program (§5). §8.3.3 Finality arguably selects S3's reading. S3's wording should say that bounds are followed in the direction of the row.
- **Two SPEC questions for the owner:**
  - (c1) Is a Borrow reached through a borrowed input finite (its failure Refuted) or inferred (its failure Unknown, today's verdict)?
  - (c2) Once OJ5 writes a fixed Origin into a local's Type, the Type no longer shows a mutable-static anchor. The Refuted rule of S15:537 then needs a holder-level carrier (H).
- **Boundary today and after R6.** Today Binding judges and reports fixed–fixed body relations, the old Analysis reports the deferred obligations, and MIR judges none. After R6, every body relation is A5's. R6 must delete four Binding reporters; PLAN10's deletion row (P10:546) omits two of them, `RecordMismatch`'s Origin branch and `CallLocalOrigin`.
- **Doubly owned:** T1 Type-argument clauses (until R7), and the one loop in `ValidateOriginRelations` that judges both locals and declaration-level owners.
- **Unowned:** unresolved inference obligations after R6-check, mutable-static anchors today, and Binding's "no principal candidate" error, which contradicts S15:348.

## 1. F4 audit table

| Row | Rule | SPEC | Class | Facts beyond MIR (producer/owner) | Work bound per body × case |
| --- | --- | --- | --- | --- | --- |
| 7 | Relations by variance; declared and substituted clauses; well-formedness per case | S15:471–477, S15:271–280, S15:317, S15:525, S11:242 | Expressible, with new facts | `Relate` rows, `Clauses`, `WellFormed`, local-clause rows (Binding/TyCtxt) | Θ(Σ fitted Type sizes + Σ calls × clauses + Σ occurrence sizes) |
| 9c | Reborrow containment up to the first shared layer | S15:513, S15:422–424, S13:369–371, S10:72 | Expressible | None: MIR Deref paths, Types, `MirCases` | Θ(Σ Borrow Place sizes) |
| 13 | Judging: fixed, finite and inferred Origins; chains; Refuted, Unknown | S15:563–577, S15:537, S11:242 | Expressible, with new facts | `OriginAtom.Of` (OJ7), `UniversalRelations.For` (OJ2); A4 per-result anchors; static carrier (c2, H) | Θ(N + E + E·⌈F/64⌉ + Ft·Pm) |
| 19 | Origin inference; no candidate means a local region | S15:340–350, S15:300, S15:416 | Expressible with a new fact; decidable only with S3 | Inference outcome rows (Binding, OJ5/OJ6) | Binding Θ(Σg Bg·⌈Vg/64⌉); A5 linear |
| 20 | Nested functions' fixed Origins | S15:298, S15:307–312 | Expressible | None beyond OJ7 and OJ2 | O(1) per atom |

Every row runs per `MirCases` view (DEC:425). Its failures are `UnsatisfiedOriginRelation_Kd` (Refuted) and `UnprovenOriginRelation_Kd` (Unknown) (`B/Binding.Diagnostics.cs:146-149`).

## 2. Shared schema (rows 7–20)

**Nodes.** Binding classifies each atom (OJ7) and publishes it as `OriginAtom.Of` (DEC:416-418; reverted by R4-M33, DEC:870).

| Kind | SPEC | Today |
| --- | --- | --- |
| `fixed(o)`: universal, parameter or receiver Origin, a projection of one, `static`, a meet of fixed Origins; in a Closure, capture, call-receiver and inferred whole-result Origins | S15:565 | `FixedOrigin`, `B/Binding.OriginProof.cs:27-45` |
| `finite(o, body)`: a Borrow anchored in a body root and not reached through a borrow | S15:567 | `BodyLocalOrigin` (Projection, Anchor), `B/Binding.Diagnostics.cs:439-455` |
| `finite(o, static)`: a mutable-static Borrow, or a call result that carries a mutable-static anchor | S15:568 | Not modeled (§6, row B19) |
| `inferred(o)`: local regions, unsolved call variables, raw anchors | S15:569 | `IsLocalRegion`, `B/Binding.LocalRegions.cs:15-16` |

**Outlives sources.** Each fact is `outlives(a, b, site, source, caseMask)`. An `==` adds both directions.

| Source | Generator | Row |
| --- | --- | --- |
| `fit` | `Relate(A, D)` at each MIR fit site; `§11.8` is a fit to `static` | 7 |
| `declared` | substituted callee clauses, local clauses, a Type's own clauses at an occurrence | 7 |
| `wf` | immediate well-formedness edges of each borrow layer of an occurrence, a formed Borrow's Type, and a call's instantiated parameter and result Types | 7 |
| `annotation` | none of its own: `during` and sets fix the destination Type of a fit (S15:235) | 7 |
| `containment` | MIR Deref steps up to the first shared layer | 9c |
| `closureResult` | each result source → the inferred fixed result Origin (C2) | 17 (F7) |
| inference bounds of variables without a candidate | the outcome rows | 19 |

**Meets.**
- A meet at the longer end is split into its operands, which S15:350 makes exact.
- A meet of fixed operands is a fixed node; OJ2 handles it.
- Any other meet at the shorter end is an inferred node with operand edges `a → m` and `b → m` (S15:350, "`a outlives (a and b)`").

**Judgment (S15:571–575).** No liveness and no program points.

```text
e(a,b,s)        :- outlives(a,b,s,_,c), c ⊆ K.                // K: the view's case
inner(x)        :- inferred(x) ; finite(x,_).
end(x)          :- fixed(x) ; finite(x,_).
r(a,b,s)        :- end(a), e(a,b,s).
r(a,c,s)        :- r(a,b,_), inner(b), e(b,c,s).              // s: site of the last edge
refuted(a,b,s)  :- r(a,b,s), finite(a,B), fixed(b), outside(b,s,B).
unknown(a,b,s)  :- r(a,b,s), fixed(a), fixed(b), !entails_K(a,b).   // OJ2
outside(b,_,body).
outside(b,s,static) :- !(result_site(s), bounded_by_borrowed_input(b)).  // S15:537
```

Every other chain is Proven; a chain with an inferred end is never judged (S15:574). A failed relation adds no constraint (S15:477), so nothing feeds back.

## 3. Rule sections

### 3.1 Row 7: relations by variance, clauses, well-formedness

**SPEC.**
- A fit yields relations by variance, and `==` at invariant positions. Declared, substituted and well-formedness relations are relations too, and `§11.8` is a fit to `static` (S15:473).
- Well-formedness is judged per case (S15:471), and variance is defined at S15:317.
- A call proves its substituted clauses and well-formedness (S15:525); clause obligations are listed per owner at S15:273–278.

**Facts (inputs).**

```text
fit(s, A, D, c)                 // MIR: Assign, call argument/receiver, return/yield/exit operand,
                                //      aggregate component, capture entry, static initializer
relate(A, D, a, d, v)           // Binding TyCtxt.Relate: stops at Function Types (whole contract, OJ4)
subst_clause(call, a, b, eq, c) // Binding TyCtxt.Clauses, under OJ5's substitution
local_clause(decl, a, b, eq)    // Binding: clauses attached to a local (S15:278)
type_clause(T, a, b, eq)        // Binding: a Type's clauses at an occurrence (S15:275)
wf(T, i, o, c)                  // Binding WellFormed: immediate edges of each layer B/V during o
occ(s, T)                       // MIR: local Types, temporaries, a call's instantiated Types
```

**Rules.**

```text
outlives(a,d,s,fit,c)          :- fit(s,A,D,c), relate(A,D,a,d,co).
outlives(d,a,s,fit,c)          :- fit(s,A,D,c), relate(A,D,a,d,contra).
outlives(a,d,s,fit,c), outlives(d,a,s,fit,c) :- fit(s,A,D,c), relate(A,D,a,d,inv).
outlives(a,b,call,declared,c)  :- subst_clause(call,a,b,_,c).          // + reverse if eq
outlives(a,b,decl,declared,0)  :- local_clause(decl,a,b,_).
outlives(a,b,s,declared,c)     :- occ(s,T), type_clause(T,a,b,_).
outlives(i,o,s,wf,c)           :- occ(s,T), wf(T,i,o,c).
```

**Class: expressible, with new facts.** The new facts are the `Relate`, `Clauses`, `WellFormed` and clause rows. Their producer and owner is Binding (TyCtxt), and they are immutable after Bind (P:481-484). OBS13 C1 asked for the same publication (OBS13:58).

Today Binding keeps only the non-fixed part as `BindingObligation` rows (`B/Binding.CallRelations.cs:305-309`, `B/Binding.TypeRelations.cs:485-488`, `B/Binding.TypeOrigins.cs:287-312`, `B/Binding.ResultPremises.cs:598-613`) and judges the fixed part itself (`B/Binding.CallRelations.cs:311-323`). The view must publish both parts. Emitting only immediate well-formedness edges keeps the facts linear: transitivity in the chains covers deeper layers, and each case view has its own complete Types (S8:972).

**Bound and V16 check.** Θ(Σ fitted Type sizes + Σ calls × clauses + Σ occurrence sizes), counted per fact emitted. Scale N fits, Type depth N and N clauses by 2× and 4×.

**Rule tests.**
- Valid: `get(b: objref/Holder<ref/i32 during c> during o)` (S15:480-482); `identity` (S15:242-244); `useView` (S15:303-305).
- Invalid: `pick` (S15:589-591) → `UnprovenOriginRelation_Kd`; the relation holds only in the `ref` case.
- Invalid, invariant: inside `func f(x: uniq/(ref/i32 during a), y: uniq/(ref/i32 during b))` with `origin b outlives a`, `let z: uniq/(ref/i32 during a) = y` needs `a == b` → `UnprovenOriginRelation_Kd`. The twin with `origin a == b` is valid.
- Invalid, substituted clause: `func keep(x: uniq/(ref/i32 during a), y: ref/i32 during b)` with `origin b outlives a`, called as `func use(p: uniq/(ref/i32 during c), q: ref/i32 during d) => keep(p, q)`. Here `a == c` and `b = d`, and the clause needs `d outlives c` → `UnprovenOriginRelation_Kd`. The twin with `origin d outlives c` is valid.

### 3.2 Row 9c: containment up to the first shared layer

**SPEC.**
- A Reborrow is contained in the Origin of its parent and of each safe borrow through which the parent is reached, up to and including the first shared layer (S15:513).
- A `uniq` result reached through an exclusive receiver stays within the receiver (S15:422-424).
- Pair layers: in the `owner` case the layer is absent, in the `ref` case it is shared, and in the `uniq` case the walk continues (S13:369-371). A read through layers takes the meet of their Origins (S10:72).

**Facts.**

```text
borrow(s, o_new, place, mode)       // MIR Borrow rvalue, explicit or implicit Reborrow
deref_step(place, k, o_k, m_k, c)   // k-th Deref outward: base reference's outer Origin and mode
```

**Rules.**

```text
shared_at(s,k,c)  :- borrow(s,_,p,_), deref_step(p,k,_,m,c), m ∈ {ref, objref}.   // pair layer per case
stop(s,k,c)       :- shared_at(s,k,c), !(shared_at(s,j,c), j < k).
outlives(o_k, o_new, s, containment, c) :- borrow(s,o_new,p,_), deref_step(p,k,o_k,_,c),
                                           !(stop(s,j,c), j < k).
inferred(o_new)   :- borrow(s,o_new,p,_), deref_step(p,1,_,_,_).    // reached through a borrow (S15:567)
finite(o_new,body):- borrow(s,o_new,p,_), !deref_step(p,_,_,_,_).
```

**Class: expressible** from MIR Place projections, their base Types and `MirCases`, with no Binding fact. (H) MIR writes each implicit Reborrow (S13:399, S15:608) as a Borrow of a Deref Place. A5-1 related containment this way before R4-M33 (DEC:780-781).

Today Binding folds containment into the Reborrow's Type: `PathReborrowOrigin` (`B/Binding.ArgumentOperations.cs:558-618`) meets only signature layers. Body-local parents stay as ancestors in the old Analysis (`:561`). Two findings:

- **(c1)** If `o_new` were finite with fixed bounds, `detach` would be Refuted. As an inferred node it is Unknown, which is what the tests assert today (`T/PairFollowTest.cs:358-386`). A minimal SPEC clarification: "A Borrow reached through a borrow has no finite Origin; its containment relations bound it."
- (H) The skip for implementations under `preserves results` (`B/Binding.ArgumentOperations.cs:565`, `:620-669`) has no basis in S15:513 or S8:772. A5 must not port it; a census of library `next` implementations is needed.

**Bound and V16 check.** Θ(Σ steps up to the first shared layer), which is at most the MIR Place sizes, an input. Scale N nested Derefs with one Borrow at each depth.

**Rule tests.**
- Valid: `get`, `getUniq` and `intoUniq` (S15:430-440).
- Invalid: `detach` (S15:442-443) → `UnprovenOriginRelation_Kd`, as its concrete twins today (`T/PairFollowTest.cs:358-386`).
- Invalid: `uniq/Box<uniq/i32 during a> during c` → `uniq/i32 during a` (docs/SETTLED.md:225-237).
- Valid: `func peek(r: ref/(ref/i32 during a) during b) -> ref/i32 during a => r@follow@follow@ref`. The Borrow is contained only in `a`.
- Invalid: `func peekU(r: ref/(uniq/i32 during a) during b) -> ref/i32 during a => r@follow@follow@ref`. The Borrow is contained in `a` and in `b`, the first shared layer; `b outlives a` → `UnprovenOriginRelation_Kd`.

### 3.3 Row 13: judging chains

**SPEC.** S15:563-577 defines the kinds, chains, Refuted and Unknown, and per-case premises. The mutable-static bound is at S15:537 and S11:242.

**Facts.** `OriginAtom.Of` (OJ7); `entails_K` from `UniversalRelations.For(body, case)` (OJ2: PLAN10 keeps one closure per body, P10:546, and `OriginPremiseClosure` is already linear per target with case-conditioned edges, `B/OriginPremiseClosure.cs:36-44`); case masks (`BindingObligation.Condition`, `B/Binding.Origins.Model.cs:154-162`); `result_site` and `bounded_by_borrowed_input` from OJ1; static sources: static Borrows (F5's static keys) and A4's per-result anchors (P:460-461).

**Rules:** see §2.

**Class: expressible, with new facts.** Binding publishes `OriginAtom.Of` and `UniversalRelations.For`; A4 publishes the per-result anchors.

**(c2, H)** A call result that carries an anchor has the callee's substituted fixed Origin in its Type: `current(anchor)` is `ref/string during anchor` (S15:545-549). After OJ5 writes it into `let x = current(anchor)`, the Origin graph sees only `anchor → anchor`. The Refuted rule of S15:537 then needs a holder-level carrier: the flow-insensitive projection of `flow(H1,H2,P)` (P:138) from static sources to escaped sinks (P:143). That still uses no liveness and no program points, so the stratum holds, but the fact is per holder.

The owner chooses between two options:
- (a) A5 gives each such result position a fresh `finite(_, static)` node and carries it through holders.
- (b) The SPEC states the rule on values.

Neither option is infinite.

**Today.** The judge is `B/Binding.OriginProof.cs:98-140`, with local-region source sets at `B/Binding.LocalRegions.cs:434-458`; Refuted means body-local to fixed (`B/Binding.Diagnostics.cs:146`); the old Analysis judges deferred obligations (`A/OwnershipAnalysis.Borrows.cs:13-121`); the old path accepts `leak` (P:632-633).

**Bound and V16 check.** Θ(N + E + E·⌈F/64⌉ + Ft·Pm): N nodes, E edges, F (Ft) fixed nodes with an edge into (from) an inner node, Pm the signature-level premise graph. Refuted is one multi-source search per bound kind, keeping one witness predecessor per node for the evidence. Unknown propagates fixed-source bitsets over the strongly connected components of inner nodes, then runs one OJ2 closure per fixed target. Count work as `MirEffects.Evaluations` does, dense words as R4-M24 did (DEC:800-803), and scale N chained locals, N statements and N signature Origins by 2× and 4×.

**Rule tests.**
- `bad` (S15:580-582) → `UnsatisfiedOriginRelation_Kd`.
- `f` (S15:584-586) → `UnprovenOriginRelation_Kd`.
- `pick` (S15:589-591) → `UnprovenOriginRelation_Kd`.
- `choose` (S15:353-354) is valid.
- `T/LocalRegionInferenceTest.cs:58-67` (finite → `var` → fixed, also after `loop`) → `UnsatisfiedOriginRelation_Kd`; `:70-77` → `UnprovenOriginRelation_Kd`; `:20` is valid.
- Unreachable source (S15:571): `func u(x: ref/i32) -> ref/i32 during x`, with body `return x`, then `let t = 1` and `return t@ref` → `UnsatisfiedOriginRelation_Kd`.
- Static: `current` is valid and `leak` → `UnsatisfiedOriginRelation_Kd` (S15:545-549); `limitRef` is valid and `nameRef` → `UnsatisfiedOriginRelation_Kd` (S11:249-251).
- Finite to finite only, `var a = 1` / `let r = a@ref` / `var s = r` / `inspect(s)`, is valid.

### 3.4 Row 19: inference; no candidate means a local region

**SPEC.** The inference rows and the local-region rule are at S15:342-350; nested slots are body variables fixed once (S15:300); a local's declaration fixes its Type (S15:416).

**Split.** OJ5 in Binding solves calls (`B/Binding.OriginInference.cs:87-257`) and locals (`B/Binding.OriginDeclarations.cs:747-899`) and writes the results into Types (P10:320, P10:324). A5 reads the outcome.

**Rules in Binding, with S3.**

```text
up(v,x) :- bound(v_site, x, v).              up(v,y) :- up(v,u), var(u), up(u,y).
lo(v,x) :- bound(v_site, v, x).              lo(v,y) :- lo(v,u), var(u), lo(u,y).
cand(v, ⋀{x | up(v,x), end(x)})  :- row(v, co), some such x.
cand(v, x)                       :- row(v, contra), lo(v,x), end(x), ∀ end y with lo(v,y): entails(x,y).
cand(v, x)                       :- row(v, inv), proven equality with x.
nocand(v)                        :- otherwise.
```

**Rules in A5.**

```text
inferred(v) :- nocand(v).                                                   // S15:348
outlives(σa, σb, site, src, c) :- bound(site, a, b, src, c).   // σ: cand substitution; "other bounds are then checked"
```

**Class: expressible with a new fact, decidable only with S3 (§5).** The new fact is the outcome rows (Binding, OJ5/OJ6). Binding's "no principal candidate" record (`B/Binding.OriginProof.cs:443-449`, `MissingOriginBinding_Kd`) contradicts S15:348 for a body local (H).

**Bound and V16 check.** Binding does reachability per inference group, Θ(Σg Bg·⌈Vg/64⌉). A5 is linear in the bounds. Scale one local with N omitted slots, and N calls.

**Rule tests.**
- Valid: `choose` (S15:353-354) and `T/LocalRegionInferenceTest.cs:16-20`.
- The §5 program, with the verdict that the S3 decision fixes.
- A Field or static storage slot without an Origin → `MissingOriginBinding_Kd`, which stays with OJ1 (S15:390).

### 3.5 Row 20: nested functions' fixed Origins

**SPEC.** A named nested function's signature may name visible enclosing Origins; they are fixed Origins of the nested function and capture nothing (S15:298). The example is at S15:307-312. The Loan part, that the Loan ends at its last use, belongs to F1: regions keep no Loan live (S15:456).

**Rules.**

```text
fixed(o)@nested  :- names(nested,o), binder(o) = enclosing.
fresh(call,o)    :- universal(callee,o), binder(o) = callee.      // S15:523; never an enclosing name
premises(nested) ⊇ premises(enclosing).                           // rule R1, B/OriginPremiseClosure.cs:20-21
```

**Class: expressible** with OJ7 and OJ2. Today `FixedOrigin` ignores the binder (`B/Binding.OriginProof.cs:44`), and `Discover` adds only the callee's own atoms (`B/Binding.OriginInference.cs:311-325`). No work beyond classification.

**Rule tests.**
- Valid: `use`/`pass` (S15:307-312).
- Invalid: nested in `use`, `func pass(value: uniq/i32 during bound) -> uniq/i32 during source => value`. Only `source outlives bound` is a premise → `UnprovenOriginRelation_Kd`.
- Invalid: nested `func inner() -> ref/i32 during bound` with body `let t = 1` and `return t@ref` → `UnsatisfiedOriginRelation_Kd`.

## 4. Strata

**What Origin judgment reads** (per body × case view):
- the `outlives` facts of §2–§3: Binding views, MIR fit sites and Borrow Places, and C2 for Closure bodies;
- OJ7 classification, OJ2 premises and case masks;
- A4's per-result anchors.

It reads no `live`, no `edge(P,Q)` reachability, and no `carries` or `issued`. A Borrow contributes only its Origin node and Place. It reads every block, including check-only ones (S15:571, DEC:412). It feeds nothing back (S15:477).

**What Loan flow reads from Origin judgment: nothing.** It reads `retains` (Binding C1/C8, and C2 for Closures), which the instantiated public contract determines (S15:526, S15:529), plus A4 summaries. It does not read inference outcomes. The old path does derive Loans from the Origin graph (`A/OwnershipBody.LocalRegions.cs:494-532`, `A/OwnershipBody.Borrows.cs:480-490`); that must not be ported. OBS13's "source-chain eligibility before retention" (OBS13:59, OBS13:211) is consistent with S15:529 only if eligibility is decided from the contract.

**Shared input.** A4 feeds both strata and reads neither. The two strata are independent, so their order is free, and A6 merges the reports.

## 5. S3: Origin inference bounds (§15.3.6)

**The plan's hypothesis (P:390-391) is confirmed.** In the program below, `View<T> {source}` is the struct of S15:183-185.

```kimi
func demo<T>(x: View<T>, anchor: ref/i32 during s, sink: (View<T> during s) -> ()) -> View<T> during x.source
    origin x.source outlives s
    let pair: (View<T>{w}, (View<T>{k}) -> ()) = (x@move, sink@move)
        origin k.source outlives w.source
    return pair.0@move
```

**The two variables.**
- `w.source` is covariant: a Tuple component, and View is covariant in its slot (S15:317, S15:389).
- `k.source` is a nested input slot, a body variable (S15:300). It is contravariant, because function parameters reverse polarity (S15:317).

**Their bounds.**
- From the initializer: `x.source outlives w.source`, an upper bound of `w`; and `k.source outlives s`, a lower bound of `k`.
- From the clause: `k.source outlives w.source`, an upper bound of `w` that is a variable, and a lower bound of `k` that is a variable.
- By S15:344-346, cand(w) = `x.source and cand(k)`, and cand(k) is whichever of {s, cand(w)} outlives the other. Each candidate is defined through the other, and there is no outlives cycle that would merge them (S15:348).

**Two solutions satisfy every row and every check:**
- (A) w = k = `x.source`. The return holds, so the program is valid.
- (B) w = k = `s`, because `(x.source and s) = s` under the premise (S15:192). The return needs `s outlives x.source`, which is Unknown → `UnprovenOriginRelation_Kd`.

**Is the current text decidable?** Not as a function. §15.3.6's per-row rule has two fixed points with different verdicts. The solver rules (S15:350) decide entailment; they do not choose a candidate. Only §8.3.3 Finality, read as applying to candidates (S8:164: "a fact that depends on its own judgment … supplies no evidence"), removes the mutual bounds:
- w = `x.source` and k = `s`;
- the clause check then needs `s outlives x.source`, which is Unknown, so the program is rejected at the clause.

That is S3's reading. The implementation reaches (A) by the order in which `SolveLocalBounds` updates the two ends (`B/Binding.OriginDeclarations.cs:848-899`). This was read, not run (H), and an order-dependent result is what Finality forbids. (H) A call has the same shape: a callee clause `origin b outlives a`, with `a` covariant and `b` at a Function-Type parameter position, iterated from null (`B/Binding.OriginInference.cs:113-174`).

**Wording.** S3 should say: "followed transitively in the direction of the row's kind (upper bounds through upper bounds, lower bounds through lower bounds)". It is finite reachability per inference group.

**Alternative S3′.** A variable bounded by a variable of the other row's kind has no candidate, so it becomes a local region. The program is then accepted: its only chain is `x.source → w → x.source`. Both options are decidable; S3 is the stricter one and matches Finality.

## 6. Origin boundary table

"Unsat/Unproven" stands for `UnsatisfiedOriginRelation_Kd` / `UnprovenOriginRelation_Kd`. The R6 switch must move ownership of the ledger code `Binding.OriginRelation`, which is classified as A5's (DEC:469-471), not duplicate it.

| # | Judgment (SPEC) | Decided today (code) | Code today | After R6 (plan) | Double-report risk → removal |
| --- | --- | --- | --- | --- | --- |
| B1 | Fits of call receiver and arguments (S15:473, S15:524) | Binding `JudgeCallFit`/`CollectCallRelations`/`JudgeCallPosition` (`B/Binding.CallRelations.cs:196-335`): fixed ends recorded (`:311-323`), others become obligations (`:305-309`); old Analysis judges the obligations with Binding's judge (`A/OwnershipAnalysis.Borrows.cs:31-52`) | Unsat/Unproven; `Unsupported_Kd` for Unrepresentable (`A/OwnershipAnalysis.Borrows.cs:74-79`) | A5 rows 7/13 | Delete `PublishCallRelations` and `ReportMoreCallRelations` (`B/Binding.CallRelations.cs:182-193`, `:391-413`; P10:546). Keep `CollectFitBounds` (OJ5, `:131-152`) |
| B2 | Other fits: assignment, initializer, return, aggregate, capture, default (S15:473, S14:391-404) | Binding `RecordMismatch` (`B/Binding.Diagnostics.cs:939-986`) for fixed ends; `CheckTypeUse`/`AddOriginFit` obligations (`B/Binding.TypeRelations.cs:418-488`) → old Analysis | Unsat/Unproven; `Unsupported_Kd` (`B/Binding.Diagnostics.cs:985`) | A5 | **Not in P10:546.** Delete the Origin branch and the `Unsupported_Kd`; publish fit rows |
| B3 | Function-value fits, erasure, references (S10:297-299, S15:475) | Binding OJ4: `B/Binding.Diagnostics.cs:929-937`, `B/Binding.ResultPremises.cs:50-53`, `B/Binding.OriginProof.cs:334-367` | `UnprovenOriginContract_Kd` | Binding OJ4 | None, if `Relate` stops at Function Types as `B/Binding.CallRelations.cs:216-219` does |
| B4 | Substituted clauses at calls (S15:525) | Binding `JudgeCallClauses` (`B/Binding.CallRelations.cs:154-179`); also OJ5 bounds (`B/Binding.OriginInference.cs:90-104`) | Unsat/Unproven | A5 (`Clauses`); OJ5 keeps them as bounds | Delete the judging call (`:165`) |
| B5 | A Type's clauses at an occurrence (S15:275) | Obligations (`B/Binding.OriginProof.cs:298-332`) → old Analysis; T1 in applicability: `CheckTypeOriginRelations` (`B/Binding.OriginProof.cs:273-296`) via `B/Binding.ConstraintValidation.cs:177` and `B/Binding.Calls.cs:928-958` | Unsat/Unproven; Constraint codes for T1 (H) | A5 for occurrences; T1 Binding until R7 (P:93) | **Doubly owned** (§7.1) |
| B6 | Local clauses (S15:278) | `ValidateOriginRelations`/`JudgeDeclaredRelation` (`B/Binding.OriginProof.cs:394-480`); obligations from `SolveLocalBounds` (`B/Binding.OriginDeclarations.cs:881`) | Unsat/Unproven; `MissingOriginBinding_Kd` (`:443-449`) | A5 rows; candidates stay with OJ5 | Split the loop (§7.2); delete records `:447` and `:455` |
| B7 | Field, Case, associated and alias clauses (S15:276-279) | The same loop (`B/Binding.OriginProof.cs:398`); `B/Binding.AssociatedOrigins.cs:298`, `B/Binding.AssociatedFormation.cs:87` | Unsat/Unproven | Binding OJ1 | One owner after the split |
| B8 | Well-formedness of body Type occurrences (S15:471) | `RetainInnerOutlives` (`B/Binding.TypeOrigins.cs:284-312`); the `CallBorrows` discharge (`:182-192`) is skipped by the old Analysis (`A/OwnershipAnalysis.Borrows.cs:40`) | Unsat/Unproven | A5 (`WellFormed`) | `CallBorrows` becomes redundant under A5 |
| B9 | Well-formedness of a call's instantiated Types (S15:525) | `RequireInputPremises`/`RequireResultPremises` (`B/Binding.ResultPremises.cs:199-231`, `:598-613`) | Unsat/Unproven ("wellFormed") | A5; OJ5 keeps the shortening (`B/Binding.OriginInference.cs:143-153`) | Delete the judging modes (P10:546). The suppression at `B/Binding.ResultPremises.cs:227` becomes an A6 derived rule |
| B10 | Signature well-formedness, storage completion, result relations and defaults (S15:368-412, S15:257) | Binding OJ1: `B/Binding.OriginDeclarations.cs:461-680`, `B/Binding.Origins.cs:679-723`, premises in `B/OriginPremiseClosure.cs:20-27` | `MissingOriginBinding_Kd`; `InvalidOriginBinding_Kd` (`B/Binding.TypeOrigins.cs:172-174`) | Binding OJ1 | None |
| B11 | `Owned`, Callable and Type identity in applicability (S15:475, S10:146) | Binding OJ3 | `Unsatisfied`/`UnprovenConstraint_Kd` | Binding OJ3 | None |
| B12 | Borrow formation (S13:399, S15:566-569) | Binding types the Borrow (`B/Binding.ArgumentOperations.cs:678-722`) and classifies it (OJ7) | None of its own | OJ7 in Binding; A5 emits formation edges | None |
| B13 | Containment (S15:513, S15:422-424) | `PathReborrowOrigin` (`B/Binding.ArgumentOperations.cs:558-618`); the failure appears as a fit | Unproven (`T/PairFollowTest.cs:383-385`) | A5 row 9c | A single report if Binding keeps the meet only as Type data. The skip at `:565` disagrees with A5 (§7.8) |
| B14 | Closure results (S15:710) | `B/Binding.Closures.cs:611-640` (inference) and `:670-675` (refutation) | Unsat | Contract stays in Binding (C2, P:497); source judgment moves to A5 (F7, P:734) | **Not in P10:546.** Delete `:674` with F7 |
| B15 | Call Origin inference (S15:340-350, S15:523-525) | OJ5 (`B/Binding.OriginInference.cs:83-289`, `B/Binding.ResultPremises.cs:169-197`) | `MissingOriginBinding_Kd` (`B/Binding.OpenOrigins.cs:364`) | Binding OJ5 | None: no judgment |
| B16 | Locals, local regions, no candidate (S15:348, S15:416) | `B/Binding.OriginDeclarations.cs:747-899`, `B/Binding.LocalRegions.cs:235-458`, `B/Binding.Origins.cs:672-677` checked by `B/Binding.OriginProof.cs:47-54`; the residue is reported by the old Analysis | `UnprovenConstraint_Kd` (`A/OwnershipModel.cs:244-246`); `MissingOriginBinding_Kd` | Candidates OJ5; graph and judgment A5 (P10:324) | Unowned residue (§7.4) |
| B17 | Judging chains (S15:571-577) | Judge: `B/Binding.OriginProof.cs:98-140` and `B/Binding.LocalRegions.cs:434-458`; reporters: Binding (B1, B2, B6, B14) and old Analysis (`A/OwnershipAnalysis.cs:228-240`) | Unsat/Unproven/Unsupported | A5 row 13 | The old reporter goes at R6-check; Binding's go per B1, B2, B6 and B14 |
| B18 | Fixed Origins made equal through one fresh Origin (S15:473) | `JudgeCallPosition` with `FixedBy` (`B/Binding.CallRelations.cs:315-321`); OJ5 representative (`B/Binding.OriginInference.cs:259-289`) | Unproven | A5 (`==` edges) | As B1 |
| B19 | Mutable-static anchors (S15:537, S11:242) | Nobody: the old path accepts `leak` (P:632-633); MIR has no A5 (`src/Kimi/Compiler/Mir/Analysis/MirAnalysis.cs:3-10`) | None | A5 (F5 with row 13) | New check, no duplicate |
| B20 | T1–T4 (P10:873-876) | Binding: T1 = B5; T2 `B/Binding.Expressions.cs:392-396`, `B/Binding.TypeRelations.cs:21-23`; T3 PLAN10's `Binding.Calls.cs:2306-2316` (not re-verified); T4 `B/Binding.OriginProof.cs:236-254` | Constraint, `TypeMismatch_Kd` or no applicable overload (H) | Binding until R7 | T1 is doubly owned; T2–T4 are not, since A5 sees only the selected call |

## 7. Unowned or doubly owned judgments

1. **T1 (doubly owned until R7).** A Type argument's clause is judged as a Constraint in applicability (B5), and A5 would judge the same clause at the occurrence. Until R7, Binding leaves clauses it proved or failed as Constraints out of the rows.
2. **`ValidateOriginRelations` (doubly owned).** One loop judges locals (A5's after R6) and Fields, Cases and aliases (OJ1) (`B/Binding.OriginProof.cs:394-412`). Split it by owner before F4.
3. **Missing deletions (doubly owned after R6).** P10:546 does not delete `RecordMismatch`'s Origin branch (`B/Binding.Diagnostics.cs:939-986`) or `CallLocalOrigin` (`B/Binding.Closures.cs:670-675`). Add both to the R6-check deletion list, or each such failure is reported twice.
4. **Unresolved inference obligations (unowned after R6-check).** Today the old Analysis stops at them (`A/OwnershipAnalysis.Borrows.cs:45-47`) and reports `UnprovenConstraint_Kd` (`A/OwnershipModel.cs:246`). In a body they are local regions (S15:348); elsewhere Binding must report `MissingOriginBinding_Kd` itself (OJ6).
5. **Mutable-static anchors (unowned today).** See B19 and question c2; this needs F5's A4 publication.
6. **Borrows through a borrowed input (SPEC question c1).** Refuted or Unknown? Today it is Unknown (§3.2). A clarification is needed before F4's rule tests fix codes.
7. **"No principal candidate" (conflict).** Binding reports `MissingOriginBinding_Kd` (`B/Binding.OriginProof.cs:443-449`), where S15:348 makes the variable a local region in a body (H).
8. **`preserves results` containment skip (Binding-only rule).** `B/Binding.ArgumentOperations.cs:565` has no SPEC basis (S15:513, S8:772) (H). Run a library census before F4, because a library rejection triggers DEC O3.
9. **Whitelist residue.** `Unsupported_Kd` at `B/Binding.Diagnostics.cs:985` and `A/OwnershipAnalysis.Borrows.cs:74-79` covers chains that A5 decides. Delete both at the switch, as OD1 requires.
10. **Raw anchors.** The SPEC makes them inferred regions (S15:569); Binding makes them `static`-like (`B/Binding.Origins.cs:98`, `B/Binding.Origins.Model.cs:27-29`). The two agree while nothing flows into an anchor; A5 follows the SPEC.
