# Compiler architecture

The compiler reduction plan ([draft](../../draft/Proposals/2026-10-10%20Compiler%20Size%20Reduction%20and%20Single%20MIR%20Plan.md))
was adopted on 2026-10-10. This document holds the enforced rules and the stage ledger; the plan holds the design,
rationale and decisions (its §18). Required behavior remains in SPEC/IMPL, navigation in CODEMAP, verified support in
STATUS, and execution/verification rules in AGENTS and VERIFICATION. The current compiler does not yet have the target
structure below; only completed ledger entries describe enforced changes.

## Target pipeline

| Phase | Owner | Output |
| --- | --- | --- |
| P1 | Syntax | Koto syntax tree with dense pre-order `SyntaxId`; no phase after parsing writes to it |
| P2–P5s | Binding | Declaration tables, `HirTables` keyed by `SyntaxId`, four Plan kinds (Call, Pattern, Closure, Expansion), HIR lints and startup checks |
| P6 | Mir | One MIR body per definition (desugaring happens here) |
| P7–P9 | Mir | Validator, initialization/Move, drop elaboration, runtime lints, definition-stage capability check, effects, borrow checking per body and Semantics case |
| P10 | — | Acceptance (SPEC §23.3.3) |
| P11–P12 | Backend | Monomorphization, shims, instance-stage capability check, layout/ABI, LLVM text written directly from MIR |
| P13 | Build | Native tools, outside the compiler |

## Dependency rules

| Layer | May read | Must not read |
| --- | --- | --- |
| Syntax | Source | Binding |
| Binding | Syntax tree, its own tables | Mir, Backend |
| MIR builder (`Mir/Build`) | `HirTables`, Plans, declaration tables, pure `TyCtxt` functions, syntax children, `SourceRef` | Binding internals, semantic slots on Koto, parent-walking inference |
| MIR analyses | MIR, declaration tables, pure `TyCtxt` functions | Syntax, HIR |
| Backend | MIR, declaration tables, `TyCtxt` | HIR, borrow-checker state, syntax |
| Hover and LSP | `SemanticQuery` | Private Binding state |

## Closed vocabularies

MIR has 5 statement kinds, 9 rvalue kinds, 6 terminator kinds (plus the `CheckTarget` terminator attribute), 7 projection
kinds and 5 callee kinds. Not-yet-implemented forms have one diagnostic code, `Unsupported_Kd`, carrying only a
location, reported by Binding, MIR construction or the capability check. Adding a vocabulary entry or a second
Unsupported code is a design change that needs a SPEC basis and owner approval.

## Rules during the migration

- From R0 until R6 completes, the old Analysis and Emission paths receive no new accepted forms or generation features
  (decision D5). Allowed: deletion, miscompile fixes mirrored on the new path, and the mechanical follow-up that a stage
  of the plan names.
- A form that no general rule handles reports the located Unsupported. Do not add `Supports*`-style gates, shape
  allowlists, Advice or downstream re-verification.
- `CompilerSizeBudgetTest` and `ArchitectureRulesTest` hold the size caps and the no-growth ratchets below; caps fall
  when a reduction stage exits. A cap rises only on owner instruction, or to keep O2 performance and zero allocation
  (plan §18 P5), with the reason recorded.
- O2 run time and zero allocation (compiler warm paths and native allocation counts) may not regress; O0 run time and
  compiler processing time may, and are recorded. A regression is a change beyond the variation of repeated runs under
  identical conditions.

## Stage ledger

| Stage | State | Scope |
| --- | --- | --- |
| R0 | Active | Baselines, size and rule ratchets, disposition ledger, narrow-implementation and Advice/Note inventories, freeze |
| R1 | Pending | Delete-first: one Unsupported code, Advice/Note reduction, library validation into tests, dead code, control-flow type flow, OCC-X waiting machinery, independent narrow implementations |
| R2a | Pending | `SyntaxId`, `HirTables` behind forwarding properties, `AdtDef`, unified CallPlan type, pure fits, child-walk and operator-class consolidation, Hover move |
| R2b | Pending | `TyCtxt`, complete HIR columns and Plans, default declarations, read-only declaration-table views, intrinsic table |
| R3 | Pending | MIR model, descriptor table, validator, dump and builder (shadow) |
| R4 | Pending | MIR analyses A1–A6 and the difference ledger (shadow) |
| R5 | Pending | Monomorphization, shims, layout/ABI, code generation, capability table (shadow) |
| R6 | Pending | Switch the default to MIR, then delete the old Analysis/Emission paths |
| R7 | Pending | Binding consolidation: resolver, inference context, Contract solver, CallPlan pipeline, Problem records |
| R8 | Pending | Library algorithms in Kimigayo, about 28 intrinsics |
| R9 | Pending | Remaining front-end cleanup, Documentation and toolchain moved out, final documentation |

## Baseline

At `419e8e79` the compiler has 458 C# files and 134,591 lines (Binding 60,703; Emission 20,859, of which the toolchain
1,317; Analysis 20,613; Parsing 17,712; Documentation 4,338; Lexing 3,968; Helper 3,472; Core 1,584; root 1,342)
plus 2,335 lines of `.ll.in`. The target is about 84,350 lines from the like-for-like base of 131,271 (relocated
Documentation and toolchain excluded), with a final total cap of 85,850. Line counts include comments and blank
lines; moving code out of the compiler, changing line endings or deleting explanatory comments is not reduction.
