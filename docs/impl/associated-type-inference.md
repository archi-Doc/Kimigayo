# Associated-Type inference implementation requirements

This is part of the initial implementation profile. The [language rules](../spec/associated-type-inference.md) are authoritative; implementation staging never narrows them.

## 1. Shared declaration pipeline

Maintain one staged header per declaration. Register Name, generic and Origin identities once and share the ordinary Origin equality/elision completion rules between inference and final validation. Bind parts determined by frozen premises, retain unresolved parts, identify implementations, extract evidence, complete associated bindings, then finish headers and verify compatibility. Never publish an unfinished Type as a completed syntax BoundType or set HeaderBound prematurely.

Distinguish pending Type values from final missing evidence. One merged nominal definition owns its inference batch, with separate path environments. Preserve stable match status, Member Identity, substitutions and receiver correspondence; inference and normal checking use the same identification routine. Do not reuse call-site inference, whose adaptation, Origin and length rules serve a different purpose.

Explicit and inferred bindings share normalization and consumer metadata. Preserve formation and witness obligations of early external values and shared evidence; Type-value availability is separate from verified conformance. Propagate later failures. Reset header/evidence/negative caches together for their input generation; never import an earlier inferred binding into a new frozen basis. Skip evidence collection when no eligible hole exists.

## 2. Work and storage

Walk shared Type graphs, not expanded trees. Memoize normalization and structural pair visits by the relevant Type nodes, frozen environment/path, binder substitutions and input generation. Skip portions without eligible holes and reuse extraction positions where valid. Index evidence by associated declaration and bound structure, then prove premise entailment in the destination environment. Avoid comparisons of unrelated paths, arbitrary Type enumeration and repeated candidate selection.

Reuse retained buffers and indexes on warm Binding. Preserve zero-allocation regression workloads and bounded retained capacity across success, failure, edits and recovery. Resource refusal remains a distinct incomplete outcome; a budget cannot make a legal declaration illegal.

## 3. Verification and measurements

Required functional coverage includes:

- Direct and nested results: independent Vec2 Outputs, non-Self results, explicit/omitted Unit, Option/Result/Tuple/fixed arrays/concrete Semantics; whole Function Types versus prohibited internal inversion.
- Frozen identification: unknown inputs, ambiguous overloads, no result/premise/Effect filtering or reselection; known result portions keep normal compatibility.
- Identity and premises: aliases, agreement/conflict, different bound Contracts sharing a declaration, capability-only clauses, redundant parent/diamond paths, conditional equality isolation, generic/conditional/inherited mappings and external Origins.
- Scope and boundaries: method Type/length/Origin escape, body/instantiation/expected-Type independence, excluded Property/Place/families, inverse projection and same-unit chains, explicit Origin shortening and ordinary Unit body validation.
- Dependencies and recovery: merged fragments and order independence, external published values, nominal mutual references versus Type-value cycles, edits to results/candidates/specifications/bases/conditions, removal of old inferred values, late formation/witness/Effect failure and repair.
- Public behavior: CLI/JSON/LSP cause/location agreement and display limits, inferred-binding Hover, module/generic consumers, retained witness/Function Item calls, native O0/O2 equivalence to explicit bindings.

Keep strict allocation/capacity regressions separate from timing measurements. Benchmark explicit and inferred variants under identical cold/warm conditions; scale declaration/requirement/candidate counts, depth, shared Type graphs, converging refinement paths, external projection chains and unrelated edits. Fix workload and warm-up counts before measurement. Preserve failures; never warm until a failing allocation check passes.

## 4. Implementation sequence

| Unit | Completion condition |
| --- | --- |
| U0 | Independent accepted/rejected reproducers and public diagnostic expectations; commit with their first passing implementation. |
| U1 | Nominal batches, shared staged headers and retained identification; direct extraction, explicit precedence, complete Types, scope, diagnostics and recovery. |
| U2 | Structural extraction, fixed family expansion, premise-aware sharing and external Type-value dependencies; generic/conditional/refinement/inherited coverage. |
| U3 | Composed edit/failure/recovery, late witness failures, published contracts, Hover and CLI/JSON/LSP consistency. |
| U4 | Native dispatch, allocation/capacity/scaling measurements, example/doc consistency and complete Session verification. |

Each unit includes its own invalidation and diagnostic correctness. Required verification follows repository Unit/Session rules; final Session may satisfy the last Unit when it covers identical inputs and all required checks. Record remaining valid unsupported forms in STATUS/PLAN, without treating an incomplete unit as complete. NativeAOT is excluded unless separately requested.
