# Static Slots for P42

Status: proposal for the owner's decision; not integrated, not implemented. Date: 2026-10-22. Base: `dev` `4902dd3c`. Written by
the R4 planning session on the owner's instruction (2026-10-21): 「この勢いで、先ほどの新しく挙げる候補 2、4、5、6の提案書も書いてください。」
(candidate 2). It proposes no SPEC change: on a closer reading, SPEC already states one model of static storage (§1). It proposes
how P42's design is organized, and answers two optional SPEC narrowings recorded in the A5 plan. No unit waits for it before P42,
the first feature work after R6-check's acceptance flip (PLAN §4). R5's planning session checked its code-generation and runtime facts (2026-10-22) and agrees with option A; BU's and R5's planning
sessions will each supply a package reviewer. Its independent review (`../Sketches/R4 Proposals 2026-10-21/review-3.md`) accepted it;
the non-blocking notes are applied. "(H)" marks a claim no run has checked.

## 1. The model SPEC already states

| Rule | SPEC |
| --- | --- |
| A static stored Property needs an initializer and an Owned complete storage Type; each slot initializes on first actual access | [§11.8](../../docs/spec/11-properties.md#118-static-storage) |
| A slot is Not started, Initializing, Initialized or Finished; every access checks the state; a cycle Aborts with `KIMI_E_STATIC_CYCLE`; shutdown destroys `var` slots, then `let` slots, in reverse initialization order | [§22.4.3](../../docs/spec/22-core-execution-and-foreign-functions.md#2243-static-initialization-and-shutdown) |
| A slot's identity is its **static storage key**: the Field declaration plus the enclosing Type arguments with Origins erased; one slot, one state and one destruction per key; Loan checking and effect summaries treat all keys of one Field as one root | [§22.4.4](../../docs/spec/22-core-execution-and-foreign-functions.md#2244-static-storage-keys) |
| A root of an access path is "a local, parameter or temporary, the keys of a static Field, or the fresh anchor of a raw Place borrow" | [§15.6.2](../../docs/spec/15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts) |
| An effect summary includes reads, borrows, writes, replacements, destructions and first-access initializations of static Fields, also through lazy initialization and cleanup | [§15.6.4](../../docs/spec/15-ownership-and-lifetime-analysis.md#1564-calls) |
| A Borrow of mutable static storage is bounded by the body; a shared borrow of initialized immutable static storage may be `during static` | §11.8, [§15.6.5](../../docs/spec/15-ownership-and-lifetime-analysis.md#1565-judging-origin-relations) |

So the SPEC has two identities with distinct roles: the **key** for storage, state and destruction (per instance), and the
**Field** for Loans and effects (one root). Nothing is missing at the language level that this proposal found.

## 2. Problem: the implementation has no single owner of either identity

| Area | State on dev | Owner |
| --- | --- | --- |
| Binding | Every explicit static Borrow (qualified, and the unqualified in-group form after 2b-6 (b)) is a located `Unsupported_Kd` until P42 (BU-18, R4-M90 PQ-1); SPEC 11.8's valid `limitRef` is rejected (PLAN G197); a Borrow through a static object handle's payload (G100) | BU |
| MIR | A static access is a `staticEnsure` of the StaticInit body `MirProgram.StaticInit` names; `MirStatic` is deduplicated by symbol with the declared Type, so SPEC 22.4.4's key is not representable, and the constant fold keeps no slot state (R5 package C §3.7's owner facts); a `staticEnsure` without a StaticInit body had three treatments (2b-7's side finding, PLAN G191; a validator rule pending) | R4 (builder), R5 |
| A4 / A5 | Effect keys per Field and view (R4-M89, R4-M92); static Loans are implicit (a group member's receiver or argument) until P42 | R4 |
| Code generation | Under MIR code generation, statics are R5's units C7a/C7b: `StaticEnsure` is still an unwritten writer row (R5-M92); a static that can be set, is not a scalar, or belongs to a generic container's group is the `StaticStorage` capability row, `Unsupported_Kd` at its declaration, until P42 (R5-M82, superseding R5-M60's per-access row). First-read initialization of closed immutable scalars is the old generator's | R5 |
| Runtime | The old generator reports a cycle with `KIMI_E_ABORT` at the Field declaration instead of `KIMI_E_STATIC_CYCLE` at the access (STATUS "Static initialization"), until G3 (R5-M6); C7a/C7b give `KIMI_E_STATIC_CYCLE` at the access under MIR code generation before P42 (package C §3.7). The Finished state, `KIMI_E_STATIC_SHUTDOWN` and the shutdown order are P42's | R5 |

Each area will need the per-instance key when P42 opens generic group statics, static Borrows, `var`, aggregate and drop-needing
statics and shutdown (PLAN P42). Built separately, the key would exist three times (MIR, code generation, the runtime) with three
interning rules, which AGENTS' Share by meaning forbids (one owning component per decision); A4 and A5 read the Field.

## 3. Proposal

1. **One representation of the key.** A **static slot key**, (Field symbol, `TyCtxt.Erase` of the enclosing Subst), owned by
   `TyCtxt`, reusing the identity the instance collector already keys instances by ((definition, erased Subst), R5-M39, R5-M40),
   is the identity of a slot everywhere
   storage, state or destruction is meant: code generation's slot storage and its shutdown registry, and the runtime's state
   word. MIR is generic, so its `staticEnsure` carries the open key (the Field and the enclosing Subst in the body's own Type
   parameters), which `TyCtxt.Compose` closes at admission (R5-M40); the StaticInit body stays one per Field, instantiated per key.
   The Field remains the root for Loans and effects (§22.4.4); A4's and A5's keys read the Field and need no change.
2. **One P42 design package** (full process: two designs, a design review, the owner's approval, like 2a0-D) before any P42 unit,
   covering Binding (static Borrows: 2b-6's `Unsupported_Kd`, G197, G100), the MIR builder and validator (`staticEnsure`, the
   pending program-level rule), A4/A5 (explicit static Loans under §15.6.4 and §15.6.5) and code generation and runtime (slots,
   states, cycle code and location, shutdown order). One reviewer from each of R4, R5 and BU.
3. **The two optional narrowings of the A5 plan §5.2 are not adopted:** "static storage keys are never generic" (it would reject
   Milestone 42's `Cache<T>.Statistics`, P42's completion program) and "static storage needs no shutdown cleanup" (Milestone 42 tests
   the order). Both narrow the SPEC for implementation reasons; with item 1 the per-instance key is one representation, not a cost
   per area.

## 4. Options

| Option | Content | Assessment |
| --- | --- | --- |
| **A (recommended)** | §3, items 1–3 | One key, one package, no SPEC change. Item 1 fixes a shared representation from this proposal's one design, so the package's MIR part still takes the full process (two designs); A does not lighten it |
| B | Items 2 and 3 only: the package decides the key's owner | The same result if the package chooses one owner; leaves the choice open |
| C | Adopt one or both narrowings | Smaller P42; rejects Milestone 42's forms (DEC O3) and changes SPEC §22.4.3/§22.4.4 for implementation reasons |
| D | No change: each area designs its part when P42 reaches it | Risks three key representations |

## 5. Consequences (H)

- No SPEC, DIAGNOSTICS or STATUS change now; PLAN P42's row gains "one design package first (DECISIONS)".
- R5's `StaticStorage` row (R5-M82) and BU's 2b-6 `Unsupported_Kd` keep their removal condition (P42) and get the package as their
  removal unit.
- MIR: `MirStatic` gains the key (a MIR representation change, so the package's MIR part takes the full process), and the
  constant fold is narrowed or keeps the slot state (package C §3.7).
- Runtime: C7a/C7b fix the cycle's code and location under MIR code generation before P42; the package's runtime item is the
  Finished state, `KIMI_E_STATIC_SHUTDOWN` and the shutdown order.

## 6. Principles

1. **One concept, one canonical form.** One key for storage and state; one root for Loans and effects, as SPEC states.
2. **Local reasoning.** Not affected: no rule changes.
3. **Explicit semantics.** Not affected.
4. **Compiler Server Protocol.** Static diagnostics (cycle, shutdown) get one location rule from the package.

## 7. Owner questions

| ID | Question | Options | Recommendation |
| --- | --- | --- | --- |
| OQ-S1 | One static slot key owned by `TyCtxt`, and one P42 design package before P42's units? | (a) A; (b) B, the package decides the owner; (d) D | (a) |
| OQ-S2 | The A5 plan's two narrowings (keys never generic; no shutdown cleanup)? | (a) not adopted; (b) adopt one or both (DEC O3 for Milestone 42) | (a) |

## 8. Unverified

- That every area's need is the key as stated (R5's shutdown registry and the runtime state word are read from R5-M60, R5-M82 and
  STATUS, not from code).
- That `TyCtxt` can intern the key without a new table (H: the collector's (definition, erased Subst) identity already exists).
- That `TyCtxt.Erase` keeps unused enclosing arguments, which §22.4.4 says distinguish keys (H: R5-M39's null rule reads
  `Instantiates`; R5-M82 records Milestone 42's `Cache<T>.Statistics.count`, which does not use `T`).
