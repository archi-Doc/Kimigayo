# A5-F4 Origin stratum prototype (unit 2x-P4)

Status (2026-10-19): draft, run; its review comes with 2a0-D (plan §4.1). Working record, not authority: SPEC and DECISIONS decide.
Base `origin/dev` `4ced4e4e`. Unit row: [A5 step 2 plan](../../Proposals/2026-10-18%20A5%20Step%202%20Implementation%20Plan.md) §4.1
2x-P4; prerequisite 2a0-0 ([f4-census.md](f4-census.md)). Model: [A5OriginsPrototype.cs](A5OriginsPrototype.cs), a file of its own
(PD3), not a round of `../A5 Step 0/A5RulesPrototype.cs`.

## 1. What it models

The Origin stratum of design A3 §7 with errata E4 (the containment cut), E7 and E12 (the merge) and the plan's PD11, per body and per
class of views; the file's header states each rule. In short:

- **Atoms by kind** (`OriginAtom.Of`): Fixed; FiniteBody (a Borrow reached from a body root by Field, element and own steps only);
  FiniteStatic (a Borrow of mutable static storage, P6); FiniteGuard (a guard candidate's guard-local Origin, SPEC 14.8.3); Inferred
  (body variables and call regions, a Borrow through a borrow, a raw Place's anchor, a Borrow of immutable static storage).
- **Rows** `(site, longer, shorter, Equality, case mask, use, return fit)`; Equality gives both edges; a meet at the longer end is
  split; an all-fixed meet at the shorter end is one fixed node, any other meet there an inferred node with operand edges.
- **E4**: a Ref's layers counted outward from the Borrow; an exclusive layer adds its edge and continues, a shared one adds it and
  stops, a pair layer by its case, an own step is no dereference, a raw dereference stops.
- **Classes**: views with equal edges and premises run once (`k-views`: 64 views, 2 classes).
- **Refuted**: A3 §7's multi-source BFS from the finite nodes with one witness per node; variant AllSources (finite-source bitsets).
- **Unknown**: Tarjan over the inner nodes and fixed-source bitsets (64 per word) in topological order; each edge into a fixed node
  asks the premise oracle (reflexive and transitive premise closure, `static`, `==`, the meet laws; one reverse closure per target).
- **Merge (PD11)**: a destroy conflict on a Loan `_0` carries goes when the stratum refuted its Borrow at a return fit; a guard
  candidate's err(ended) goes when its γ reaches `_0` and its guard-local Origin was refuted at a return fit; nothing else goes.
- **Reference**: every chain by SPEC 15.6.5's definition, a DFS from each end through inner nodes. On every case and class and on
  3,000 random graphs it equals the design with AllSources (Refuted and Unknown reports alike); the one-witness BFS's Refuted reports
  are a nonempty subset of it whenever it has one.
- **Loan problems** are the merge's inputs. `-loans` computes them for the 12 merge cases with `A5RulesPrototype.cs`'s design (design A
  with E8's ending, rounds 13–14), so they are derived, not assumed: all 12 equal (§3.7).
- **Inference (OJ5)**, as a cross-check of the stratum's input only: candidates per SPEC 15.3.6 with S3's sentence (as integrated on
  `claude/r4-spec-1019`: "A variable's bounds are followed through other variables to fixed or finite Origins and `static`. If none is
  reached, the variable has no candidate"), substituted into the rows (CAND); CANDDECL takes only the declaration's bounds; NoCandErr
  makes a variable without a candidate an error.

**Readings** the cases rest on (each with its clause): a body variable is an inferred region in judging (15.6.5 "a slot of an
unannotated local's Type") and its bounds include later uses (15.3.6 "constrained by variance, relations and uses"; 15.4.4 "later uses
constrain them"); the guard-local Origin is finite, bounded by the guard (14.8.3 "ends at guard cleanup"; 15.6.5 "a finite Origin
belongs to a Borrow with an upper bound"); a raw Place's Borrow is an inferred region and recovers no containment (5.2.2 "no owner or
existing Loan is recovered"; 15.6.5); an immutable-static Borrow is inferred (11.8: it "may be `during static`"); an own step is no
dereference (15.6.2, 15.6.5 "owning-layer projections"); a Copy of a stored reference is no Borrow (11.8).

**Expected reports.** Each case lists SPEC's failures and, for each, the reports SPEC and DIAGNOSTICS admit (`S` Unsatisfied, `P`
Unproven, `C` ComparisonLoanConflict at a site; `x>y` the chain's ends); "optional" marks a report a failure caused only by another may
add (SPEC 23.1.2). A run is UNSOUND (SPEC error, no report), OVERSTRICT (SPEC valid, a report), LOST (a failure without a report),
EXTRA (a report no failure admits) or DOUBLE (a redundant report of another code). SPEC 15.6.5 fixes the code of a returned local
borrow ("the Refuted relation at the returned value") and of an in-body use past a local's lifetime ("a Loan conflict at its
destruction"); 14.8.3 leaves a guard value's escape to "the ordinary Origin and Loan checks against g", so both codes are admitted.

## 2. How to run

In a folder outside the repository (the repository's `Directory.Build.props` must not apply), .NET SDK 11.0.100-rc.2:

```text
dotnet run A5OriginsPrototype.cs              # the tables below; exit code = UNSOUND + OVERSTRICT + reference disagreements
dotnet run A5OriginsPrototype.cs -- -v        # also each variant's reports and the cases' notes
dotnet run A5OriginsPrototype.cs -- -random N # N random graphs against the reference (default 3,000)
```

`-loans` (§3.7), in an empty folder with both prototype files copied there:

```text
sed '1,/^\/\/ ==== end of entry/d; /^#if F4LOANS$/d; /^#endif \/\/ F4LOANS$/d' A5OriginsPrototype.cs > origins.cs
sed '/^var verbose = /a if (args.Contains("-f4loans")) return F4Loans.Run();' A5RulesPrototype.cs > proto.cs
cat origins.cs >> proto.cs && dotnet run proto.cs -- -f4loans
```

## 3. Results

Run of 2026-10-19: 55 cases; the design has 0 UNSOUND and 0 OVERSTRICT cases and 2 report findings (j-two, g-two; §4); the
reference agrees on every case and on 3,000 random graphs; exit code 0. The tables are the run's output.

### 3.1 Cases under the design (55)

| Case | Program | SPEC | design | Reports |
| --- | --- | --- | --- | --- |
| r7-get | `func get(b: objref/Holder<ref/i32 during c> during o) -> ref/i32 during o => b.value` | valid (SPEC 15.6.1) | ok | none |
| r7-identity | `func identity<T>(value: View<T>) -> View<T>{result}`, `origin result.source == value.source`, `return value@move` | valid (SPEC 15.3.1) | ok | none |
| r7-useView | `func useView<T>(x: View<T>, callback: (View<T>{c}) -> ())`, `origin c.source == x.source`, `callback(x@move)` | valid (SPEC 15.3.4) | ok | none |
| r7-inv | `func f(x: uniq/(ref/i32 during a), y: uniq/(ref/i32 during b))`, `origin b outlives a`, `let z: uniq/(ref/i32 during a) = y` | error: P init a>b (SPEC 15.6.1 (== at an invariant position), 15.6.5) | ok | P init a>b |
| r7-inv-eq | r7-inv with `origin a == b` | valid (SPEC 15.6.1) | ok | none |
| r7-subst | `keep(x: uniq/(ref/i32 during a), y: ref/i32 during b)` with `origin b outlives a`; `func use(p: uniq/(ref/i32 during c), q: ref/i32 during d) => keep(p, q)` | error: P call d>c (SPEC 15.6.4 step 3) | ok | P call d>c |
| r7-subst-ok | r7-subst with `origin d outlives c` | valid (SPEC 15.6.4 step 3) | ok | none |
| c-get | View: `func get(self: ref/Self) -> ref/T during self.source => self.data` (a Copy of the stored reference) | valid (SPEC 15.5) | ok | none |
| c-getUniq | UniqView: `func getUniq(self: uniq/Self) -> uniq/T during self => self.data` (a Reborrow through self) | valid (SPEC 15.5, 15.6.3) | ok | none |
| c-intoUniq | UniqView: `func intoUniq(self: Self) -> uniq/T during self.source => self.data@move` | valid (SPEC 15.5) | ok | none |
| c-detach | UniqView: `func detach(self: uniq/Self) -> uniq/T during self.source => self.data` (spec case 15.5/0) | error: P ret self>self.source (SPEC 15.5, 15.6.3) | ok | P ret self>self.source |
| c-box | `func f(b: uniq/Box<uniq/i32 during a> during c) -> uniq/i32 during a => b.item` (SETTLED) | error: P ret c>a (SPEC 15.6.3) | ok | P ret c>a |
| c-peek | `func peek(r: ref/(ref/i32 during a) during b) -> ref/i32 during a => r@follow@follow@ref` | valid (SPEC 15.6.3) | ok | none |
| c-peekU | `func peekU(r: ref/(uniq/i32 during a) during b) -> ref/i32 during a => r@follow@follow@ref` | error: P ret b>a (SPEC 15.6.3) | ok | P ret b>a |
| c-cut | E4's cut: `func cut(r: uniq/(ref/(uniq/i32 during a) during b) during c) -> ref/i32 during b => r@follow@follow@follow@ref` | valid (SPEC 15.6.3) | ok | none |
| c-own | `func own(input: ref/i32) -> ref/i32 during input`, `let h: obj/Cell = Cell.init(5)`, `return h.value@ref` (an own step) | error: S ret b_h>input (SPEC 15.6.2, 15.6.5) | ok | S ret b_h>input |
| c-pair | `func pd<s/T>(p: s/(uniq/i32 during a) during b) -> uniq/i32 during a`, `s is owner or uniq`, returns a Reborrow of the referent of p's `uniq/i32` | error: P ret b>a (SPEC 15.6.3, 15.6.5 (per case)) | ok | P ret b>a |
| j-bad | `func bad(x: ref/i32) -> ref/i32 during x`, `let local: i32 = 1`, `return local@ref` | error: S ret b_local>x (SPEC 15.6.5) | ok | S ret b_local>x |
| j-f | `func f(x: ref/i32, y: ref/i32) -> ref/i32 during y`, `let r = x`, `return r` | error: P ret x>y (SPEC 15.6.5) | ok | P ret x>y |
| j-pick | `func pick<s/T>(b: ref/Box<s/T during a> during c, x: ref/i32 during a) -> ref/i32 during c`, `s is owner or ref`, `return x` | error: P ret a>c (SPEC 15.6.5) | ok | P ret a>c |
| j-choose | `func choose<T>(x: ref/T, y: ref/T) -> ref/T` (during `(x and y)`), `return x` | valid (SPEC 15.3.6) | ok | none |
| j-region | `func bad(input: ref/i32) -> ref/i32 during input`, `var view = input`, `let local = 5`, `view = local@ref`, `return view` (LocalRegionInferenceTest) | error: S ret b_local>input (SPEC 15.6.5) | ok | S ret b_local>input |
| j-region-loop | j-region with `loop => ()` before `return view` (Origins only) | error: S ret b_local>input (SPEC 15.6.5 (neither liveness nor reachability)) | ok | S ret b_local>input |
| j-region-proof | `func bad(a: ref/i32, b: ref/i32) -> ref/i32 during a`, `var view = a`, `view = b`, `return view` | error: P ret b>a (SPEC 15.6.5) | ok | P ret b>a |
| j-region-ok | j-region-proof with the result during `(a and b)` | valid (SPEC 15.3.6, 15.6.5) | ok | none |
| j-unreach | `func u(x: ref/i32) -> ref/i32 during x`, `return x`, then `let t = 1`, `return t@ref` | error: S ret2 b_t>x (SPEC 15.6.5, 14.9) | ok | S ret2 b_t>x |
| j-current | `func current(anchor: ref/i32) -> ref/string during anchor => State.text@ref` (mutable static) | error: S ret b_text>anchor (SPEC 15.6.4, 15.6.5) | ok | S ret b_text>anchor |
| j-leak | `func leak(anchor: ref/i32, target: uniq/(ref/string during anchor))`, `target@follow = State.text@ref` | error: S store b_text>anchor (SPEC 15.6.4, 15.6.5) | ok | S store b_text>anchor |
| j-limitRef | `func limitRef() -> ref/i32 during static => Config.limit@ref` (immutable static) | valid (SPEC 11.8) | ok | none |
| j-nameRef | `func nameRef() -> ref/string during static => Config.name@ref` (mutable static) | error: S ret b_name>static (SPEC 11.8, 15.6.5) | ok | S ret b_name>static |
| j-copyStatic | `group G { var slot: ref/i32 during static }`, `func g() -> ref/i32 during static => G.slot` (a Copy, no Borrow) | valid (SPEC 11.8) | ok | none |
| j-local | `var a = 1`, `let r = a@ref`, `var s = r`, `inspect(s)` (finite to finite only) | valid (SPEC 15.6.5) | ok | none |
| j-two | `func two(x: ref/i32, c: bool) -> ref/i32 during x`, `let l1 = 1`, `let l2 = 2`, `var r = x`, `if c => r = l1@ref else => r = l2@ref`, `return r` | error: S ret b_l1>x; S ret b_l2>x (SPEC 15.6.5, 23.1.2; DIAGNOSTICS 4.3 (each chain a problem)) | LOST+EXTRA | S ret b_l1>x; C end2 L2 |
| j-bits | 70 parameters `p0` … `p69`, `var v = p0`, `v = p1` … `v = p69`, `return v` during `p0` (two bitset words) | error, 69 failures (SPEC 15.6.5) | ok | P ret p1>p0; P ret p2>p0; P ret p3>p0; … (69) |
| s-demo | audit-origins §5: `func demo<T>(x: View<T>, anchor: ref/i32 during s, sink: (View<T> during s) -> ()) -> View<T> during x.source`, `origin x.source outlives s`, `let pair: (View<T>{w}, (View<T>{k}) -> ()) = (x@move, sink@move)` with `origin k.source outlives w.source`, `return pair.0@move` | valid (SPEC 15.3.6 (with S3), 15.4.4, 15.6.5) | ok | none |
| s-none | c8c1f3f534b0: `let refs = Collection<ref/Node>.init([])` (an omitted Origin with no bound) | valid (SPEC 15.3.6) | ok | none |
| s-vars | a variable bounded only by variables: `let a: Option<ref/i32> = .None`, `let b: Option<ref/i32> = a`, `inspect(b)` | valid (SPEC 15.3.6 (with S3: none is reached, no candidate, a local region)) | ok | none |
| s-vars-fixed | s-vars with `let a: Option<ref/i32> = .Some(x)` and `return b` during `y` | error: P ret x>y (SPEC 15.3.6 (with S3: x is reached through a), 15.6.5) | ok | P ret x>y |
| s-noprincipal | no principal candidate: `let f: (View<i32>{k}) -> () = if c => sink1 else => sink2` (sinks during s1, s2), `f(x@move)` (x during a), `origin a outlives s1`, `origin a outlives s2` | valid (SPEC 15.3.6 (no candidate: a local region), 15.6.5) | ok | none |
| s-noprincipal-err | s-noprincipal without `origin a outlives s2` | error: P init a>s2 (SPEC 15.3.6, 15.6.5) | ok | P init a>s2 |
| n-use | `func use(source: uniq/i32, bound: ref/i32) -> i32` with `origin source outlives bound`, nested `func pass(value: uniq/i32 during bound) -> uniq/i32 during bound => value`, `let child = pass(source)` | valid (SPEC 15.3.4) | ok | none |
| n-pass | nested in `use`: `func pass(value: uniq/i32 during bound) -> uniq/i32 during source => value` | error: P ret bound>source (SPEC 15.3.4, 15.6.5) | ok | P ret bound>source |
| n-inner | nested in `use`: `func inner() -> ref/i32 during bound`, `let t = 1`, `return t@ref` | error: S ret b_t>bound (SPEC 15.3.4, 15.6.5) | ok | S ret b_t>bound |
| a-split | Storage.splitFirst: `func splitFirst<E>(state: uniq/UniqRemainder<E>) -> Option<uniq/E during state.source>`, `return .Some(state.storage[position]@uniq)` | valid (SPEC 5.2.2, 15.6.3, 15.6.5) | ok | none |
| a-wf | `func viaAnchor(p: raw/(ref/i32 during a), anchor: ref/i32 during r) -> ref/(ref/i32 during a) during r`, `unsafe => return (*p)@ref` (a chain a → anchor → r) | valid (SPEC 5.2.2, 15.6.1, 15.6.5) | ok | none |
| g-return | p12 guardReturn: `func guardReturn(packet: ref/Packet, fallback: ref/Data) -> ref/Data during packet`, `var saved = fallback`, `match packet@follow`, `.Data(let d) if keep(saved@uniq, d) => ()`, `return saved` (keep: `slot: uniq/(ref/Data during s), value: ref/Data during s`) | error: S ret g_d>packet or C cleanup Ld; P ret fallback>packet (SPEC 14.8.3, 15.6.5) | ok | S ret g_d>packet; P ret fallback>packet |
| g-local | SPEC 14.8.3's example (p03 guard1): `var saved = fallback@ref` (a local), `match packet`, `.Data(let d) if keep(saved@uniq, d) => ()`, `use(saved)` | error: C cleanup Ld (SPEC 14.8.3) | ok | C cleanup Ld |
| g-two | g-return with `var other = fallback`, the guard `keep(saved@uniq, d) and keep(other@uniq, d)` and `use(other)` after the match | error: S ret g_d>packet or C cleanup Ld; C cleanup Ld; P ret fallback>packet (SPEC 14.8.3, 15.6.5, 23.1.2) | LOST | S ret g_d>packet; P ret fallback>packet |
| m-36f | 36f without its write: `func f<S>(src: uniq/S) -> Option<S.Item>`, `S is Take`, `var h = H(inner: src, other: 0)`, `var r = h@uniq`, `let first = r.inner.take()`, `return first@move` | error: C end Lh (SPEC 15.6.3 (P12), 15.6.5, 15.6.6) | ok | C end Lh |
| m-write | `func w(p: ref/i32) -> ref/i32 during p`, `var x = 1`, `let r = x@ref`, `x = 1`, `return r` | error: C asg Lx; S ret b_x>p (SPEC 15.6.2, 15.6.5, 23.1.2) | ok | S ret b_x>p; C asg Lx |
| m-pair | P1's other Borrow: `func pr(p: ref/i32) -> ref/i32 during p`, `var x = 1`, `var y = 2`, `let pair = (x@ref, y@ref)`, `return pair.0` | error: S ret b_x>p; C endy Ly (SPEC 15.6 (holders), 15.6.5, 15.6.6) | ok | S ret b_x>p; C endy Ly |
| m-scope | `func f(x: ref/i32) -> ref/i32 during x`, `var r = x`, `do { let local = 1; r = local@ref }`, `return r` | error: S ret b_local>x (optional: C scope Ll) (SPEC 15.6.5, 23.1.2) | ok | S ret b_local>x; C scope Ll |
| m-scope-use | m-scope with `inspect(r)` after the `do` | error: S ret b_local>x; C scope Ll (SPEC 15.6.5, 23.1.2) | ok | S ret b_local>x; C scope Ll |
| m-store | `func leakL(target: uniq/(ref/i32 during a))`, `let local = 1`, `target@follow = local@ref` (storage the body does not own, not `_0`) | error: S store b_local>a (optional: C end Ll) (SPEC 15.6, 15.6.5, 23.1.2) | ok | S store b_local>a; C end Ll |
| k-views | `func k<s0/T0, …, s5/T5>(b: ref/Box<s0/T0 during a> during c, x: ref/i32 during a, …) -> ref/i32 during c`, each `s_i is owner or ref`, `return x` | error: P ret a>c (SPEC 15.6.5 (per case), 23.1.2 (one failure in several cases)) | ok | P ret a>c |

### 3.2 Stratum variants

| Case | design | AllSources | RootCut | NoContain | RawContain | OwnAsBorrow | AnchorTop | AnchorFinite | GuardInferred | CAND |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| r7-get | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| r7-identity | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| r7-useView | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| r7-inv | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| r7-inv-eq | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| r7-subst | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| r7-subst-ok | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| c-get | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| c-getUniq | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| c-intoUniq | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| c-detach | ok | ok | ok | UNSOUND | ok | ok | ok | ok | ok | ok |
| c-box | ok | ok | ok | UNSOUND | ok | ok | ok | ok | ok | ok |
| c-peek | ok | ok | OVERSTRICT | ok | ok | ok | ok | ok | ok | ok |
| c-peekU | ok | ok | ok | UNSOUND | ok | ok | ok | ok | ok | ok |
| c-cut | ok | ok | OVERSTRICT | ok | ok | ok | ok | ok | ok | ok |
| c-own | ok | ok | ok | ok | ok | UNSOUND | ok | ok | ok | ok |
| c-pair | ok | ok | ok | UNSOUND | ok | ok | ok | ok | ok | ok |
| j-bad | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| j-f | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| j-pick | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| j-choose | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| j-region | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| j-region-loop | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| j-region-proof | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| j-region-ok | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| j-unreach | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| j-current | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| j-leak | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| j-limitRef | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| j-nameRef | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| j-copyStatic | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| j-local | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| j-two | LOST+EXTRA | ok | LOST+EXTRA | LOST+EXTRA | LOST+EXTRA | LOST+EXTRA | LOST+EXTRA | LOST+EXTRA | LOST+EXTRA | ok |
| j-bits | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| s-demo | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| s-none | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| s-vars | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| s-vars-fixed | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| s-noprincipal | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| s-noprincipal-err | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| n-use | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| n-pass | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| n-inner | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| a-split | ok | ok | OVERSTRICT | ok | OVERSTRICT | ok | ok | OVERSTRICT | ok | ok |
| a-wf | ok | ok | ok | ok | ok | ok | OVERSTRICT | OVERSTRICT | ok | ok |
| g-return | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| g-local | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| g-two | LOST | LOST | LOST | LOST | LOST | LOST | LOST | LOST | ok | LOST |
| m-36f | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| m-write | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| m-pair | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| m-scope | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| m-scope-use | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| m-store | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| k-views | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |

### 3.3 Merge variants (cases with Loan problems)

| Case | design | NoMerge | Membership | ByBorrow | GuardReverse | AllSources+GuardReverse |
| --- | --- | --- | --- | --- | --- | --- |
| j-bad | ok | EXTRA | ok | ok | ok | ok |
| j-region | ok | EXTRA | ok | ok | ok | ok |
| j-two | LOST+EXTRA | LOST+EXTRA | LOST | LOST+EXTRA | LOST+EXTRA | ok |
| g-return | ok | DOUBLE | ok | ok | ok | ok |
| g-local | ok | ok | ok | ok | ok | ok |
| g-two | LOST | DOUBLE | LOST | LOST | ok | ok |
| m-36f | ok | ok | UNSOUND | ok | ok | ok |
| m-write | ok | ok | ok | LOST | ok | ok |
| m-pair | ok | EXTRA | LOST | ok | ok | ok |
| m-scope | ok | ok | ok | ok | ok | ok |
| m-scope-use | ok | ok | ok | LOST | ok | ok |
| m-store | ok | ok | ok | ok | ok | ok |

### 3.4 Inference (OJ5's candidates substituted into the rows; S3)

| Case | design | CAND | CANDDECL | NoCandErr |
| --- | --- | --- | --- | --- |
| r7-inv | ok | ok | ok | ok |
| r7-inv-eq | ok | ok | ok | ok |
| r7-subst | ok | ok | ok | ok |
| r7-subst-ok | ok | ok | ok | ok |
| j-region | ok | ok | LOST+EXTRA | ok |
| j-region-loop | ok | ok | LOST+EXTRA | ok |
| j-region-proof | ok | ok | LOST+EXTRA | ok |
| j-region-ok | ok | ok | OVERSTRICT | ok |
| j-local | ok | ok | ok | ok |
| j-two | LOST+EXTRA | ok | LOST+EXTRA | ok |
| j-bits | ok | ok | LOST+EXTRA | ok |
| s-demo | ok | ok | OVERSTRICT | ok |
| s-none | ok | ok | ok | OVERSTRICT |
| s-vars | ok | ok | ok | OVERSTRICT |
| s-vars-fixed | ok | ok | ok | ok |
| s-noprincipal | ok | ok | ok | OVERSTRICT |
| s-noprincipal-err | ok | ok | ok | EXTRA |

### 3.5 Classes and cost

| Case | Views | Classes | Work |
| --- | ---: | ---: | ---: |
| c-pair | 2 | 2 | 21 |
| j-pick | 2 | 2 | 9 |
| k-views | 64 | 2 | 9 |

### 3.6 Scaling (V16 shape (9): N chained locals and N signature Origins; and N finite Borrows into one chain)

| N | Nodes N_o | Edges E_o | B = N_o + E_o·⌈F/64⌉ | design work | /B | AllSources work | /(B + E_o·⌈Fin/64⌉) | design S | AllSources S |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 64 | 194 | 192 | 386 | 1088 | 2.82 | 1151 | 1.99 | 1 | 64 |
| 128 | 386 | 384 | 1154 | 2431 | 2.11 | 2813 | 1.46 | 1 | 128 |
| 256 | 770 | 768 | 3842 | 5885 | 1.53 | 7673 | 1.11 | 1 | 256 |
| 512 | 1538 | 1536 | 13826 | 15865 | 1.15 | 23537 | 0.90 | 1 | 512 |

Random graphs: 3000, reference disagreements 0.
Design: 0 failures (UNSOUND, OVERSTRICT or reference), 2 report findings (LOST, EXTRA, DOUBLE).

### 3.7 Loan problems (-loans)

| F4 case | Loan problems (A5RulesPrototype design) | F4's inputs | |
| --- | --- | --- | --- |
| j-bad | D end Ll (_0) | D end Ll (_0) | equal |
| j-region | D end Ll (_0) | D end Ll (_0) | equal |
| j-two | D end1 L1 (_0); D end2 L2 (_0) | D end1 L1 (_0); D end2 L2 (_0) | equal |
| g-return | E cleanup Ld (_0) | E cleanup Ld (_0) | equal |
| g-local | E cleanup Ld | E cleanup Ld | equal |
| g-two | E cleanup Ld (_0) | E cleanup Ld (_0) | equal |
| m-36f | D end Lh (_0) | D end Lh (_0) | equal |
| m-write | W asg Lx | W asg Lx | equal |
| m-pair | D endx Lx (_0); D endy Ly (_0) | D endx Lx (_0); D endy Ly (_0) | equal |
| m-scope | D scope Ll | D scope Ll | equal |
| m-scope-use | D scope Ll | D scope Ll | equal |
| m-store | D end Ll | D end Ll | equal |

## 4. Findings

1. **One witness per node loses an independent Refuted chain (j-two).** Two local borrows assigned to one returned variable give two
   Refuted chains at one return (SPEC 15.6.5; DIAGNOSTICS 4.3: each chain is a problem; SPEC 23.1.2: each independent failure is
   reported). A3 §7's one-witness BFS finds only the first source's chain; the second Borrow then has no Refuted twin, so PD11 keeps its
   storage-end conflict: Unsatisfied for `l1`, ComparisonLoanConflict for `l2` (LOST and EXTRA). Finite-source bitsets (AllSources),
   the representation Unknown already uses, find both, at E_o·⌈Fin/64⌉ more words (scaling table: the work stays within a constant of
   N_o + E_o·⌈F/64⌉ + E_o·⌈Fin/64⌉). No acceptance changes.
2. **PD11's guard half loses an independent err(ended) (g-two).** When a guard candidate's γ reaches `_0` and also a holder used after
   the match, PD11 drops the err(ended) for its Refuted twin, so the second escape (SPEC 14.8.3's own example form) is no longer
   reported. GuardReverse keeps the err(ended) and drops the guard-local Origin's Refuted reports instead: err(ended) is one problem
   per candidate that covers every escape of the candidate's values (each escape reaches a value live after the cleanup), so no
   independent failure can be lost; 14.8.3 admits either code and DIAGNOSTICS already maps err(ended) to ComparisonLoanConflict at
   the candidate's cleanup. AllSources with GuardReverse passes every case. The alternative, dropping an err(ended) only when all its
   witness holders flow only into `_0`, needs a flow fact the Loan stratum does not have. No acceptance changes.
3. **PD11's destroy half is confirmed.** Its "`_0` carries the Loan" condition is needed: dropping by Refuted Borrow alone (ByBorrow)
   loses m-write's write conflict and m-scope-use's in-body use, and E12's first row (Membership) accepts 36f without its write
   (UNSOUND). An inner-scope return (m-scope) keeps two codes, admitted by SPEC 23.1.2 (the scope-end conflict is caused only by the
   return); a store through a parameter (m-store) keeps both, its conflict being in storage the body does not own, not `_0`.
4. **E4 stops at a raw dereference.** A walk that continues past it (RawContain), A3 §7's cut before E4 counted from the root (RootCut)
   and a finite anchor (AnchorFinite) all reject the library's `Storage.splitFirst` form (a-split), which 2a0-0's finding 4 predicted;
   RootCut also rejects `peek` and E4's three-layer cut, which rustc accepts. A3 §7's containment sentence should state the raw stop.
   Binding's static-like anchor (AnchorTop) rejects a-wf, a valid chain through an anchor (no corpus program has it, 2a0-0 (f)).
5. **The guard-local Origin is a finite atom.** 2a0-2's `OriginAtom.Of` kinds (Fixed, FiniteBody, FiniteStatic, Inferred) have no
   guard-local kind; PD11's guard half needs the Refuted twin that only a finite guard Origin gives. Under finding 2's GuardReverse the
   kind changes no report (GuardInferred reports as GuardReverse on every case), but SPEC 14.8.3 bounds it, so the view should publish
   it as finite.
6. **The S3 demo is valid under the integrated sentence.** With uses as bounds (15.3.6, 15.4.4), w reaches `x.source`, k reaches `s`
   and, through w's return bound, `x.source`, so both take `x.source`; the only chain `x.source → w → x.source` is Proven, which agrees
   with OQ12's "no verdict change" and today's Binding (audit-origins §5, H). audit-origins §5's rejection at the clause holds only with
   the declaration's bounds (CANDDECL), a reading that also rejects LocalRegionInferenceTest's accepted `pick`-into-`(a and b)` form
   (j-region-ok) and moves j-region's Unsatisfied to the assignment. So 2a0-3b's inference must take later uses' bounds, as Binding's
   outer local regions do and its slot inference (`MeetLocalUpperBounds`: initializer and clauses) does not.
7. **The stratum needs no candidates.** Judging every body variable as an inferred node (the design) gives the verdicts of substituted
   candidates (CAND) on every case; S3 only decides which variables have none, and those are inferred nodes either way (s-vars,
   s-none, s-noprincipal: valid; an error for "no candidate" (NoCandErr) rejects all three, as the old path's unresolved obligation and
   Binding's "no principal candidate" record do today).
8. **Cost.** Shape (9) (N chained locals, N signature Origins, N finite Borrows): work/(N_o + E_o·⌈F/64⌉) falls from 2.82 to 1.15 as N
   doubles from 64 to 512, so the bitset term is A3 §7's E_o·⌈F/64⌉ (a Dense term under the restated V16), linear for fixed F. The
   premise oracle is linear per target only with an adjacency index (a first run that scanned the premise list per step was
   quadratic).

## 5. Decisions needed (planning session, at 2a0-D)

- **D1 (finding 1).** Refuted by finite-source bitsets instead of A3 §7's one-witness BFS. Recommended: yes (2a-1a's V16 row "BFS work
  ≤ c·(N_o + E_o)" becomes ≤ c·(N_o + E_o·⌈Fin/64⌉)).
- **D2 (finding 2).** PD11's guard half: (a) GuardReverse (recommended: no new fact, every case passes); (b) the witness-flow
  condition (a new Loan-stratum fact); (c) keep PD11 and lose g-two's report (against SPEC 23.1.2). It supersedes PD11's second half
  and A3 E7's merge sentence (both planning-session decisions), recorded in DECISIONS with 2a-2.
- **D3 (findings 4, 5).** Two lines for 2a0-D's records: E4's walk stops at a raw dereference; `OriginAtom.Of` publishes the
  guard-local Origin as finite.
- **D4 (finding 6).** No SPEC change; the SPEC session's reviewer and 2a0-3b should know that audit-origins §5's verdict is
  superseded (valid), and 2a0-3b's inference must count later uses.

## 6. Not modelled, not verified

- The Binding boundary (2a0-3a, OQ11 (b)): the model judges every relation, so its reports are the union of A5's and Binding's (for
  example `pick`'s and `n-pass`'s fixed–fixed relations stay Binding's until R7).
- Closures (A5-F7), effects and static keys (A5-F5), defaults and inline copies (A5-F9, R4-M69), diagnostics facts beyond (code, site,
  ends), PD10's key beyond (code, site, longer, shorter), and premises named by a meet.
- Rows are hand-encoded from the programs (the "Program" column); no compiler ran on them. The Loan problems are computed by the Loan
  prototype's design on hand-encoded MIR (§3.7), not by MIR.
- OJ5 is a sketch: an invariant class takes an end reachable both ways, and a fixed Origin outlives a finite one in candidate choice.
