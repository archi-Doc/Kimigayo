# Compiler review, dead-code removal and emission restructuring

This review follows the first native Hello executable. It checks the Tokenizer, Parser, Binder, analyses and emitter against SPEC, removes unused paths, and prepares emission for growth beyond the literal-output slice. STATUS.md C.39 records the resulting coverage.

## SPEC conformance review

Every `kimi` block in SPEC.md (238) and every `examples/*.kimi` file was parsed. Diagnostics near lines that mark an intended error were ignored; the remaining 26 blocks were triaged:

| Finding | Classification | Resolution |
| --- | --- | --- |
| `(i32, string)`, `ref/T from o`, `Pair<A, B> from (...)` as standalone lines | Type excerpts parsed at statement level | None; valid in Type positions |
| Bodyless `func f(...) -> ...` lines | Signature excerpts | None |
| Accessor and conditional-conformance lines outside a struct | Member excerpts | None; valid inside their Container |
| `let protected_or_internal = 1` fails | Tokenizer defect: identifier-shaped keywords not in §2.5.1 | Removed; compound access stays two keywords |
| `func store<T> origin a, b : a(...)` fails | Parser defect: F.3 `OriginParameter := Name (":" OriginBound)?` | Parse, write, fragment agreement, Binding target resolution |
| Delimiter content one level below a `->` line fails | Layout defect against SPEC's result example | Header continuation level; §2.2.1 clarified |
| `let resource = open()` | SPEC example uses a reserved word | Example renamed |
| `-> View<T> ...` followed by a separate `=> .Some(value)` line | SPEC example violates §2.2 | `=>` moved to the `->` line |

Bounded Origin declarations are resolved but reported as UnsupportedBinding. §15.3.4 requires bound proofs at declarations and uses, and certifying a bounded declaration without them would be unsound.

## Removed paths

| Removed | Reason |
| --- | --- |
| `Signature.cs` records, `PrimitiveKind`, `NumericLiteralKind`, `IConsoleServiceExtension` | No references |
| `BasicValueHelper` (507 lines) | A second, unused compile-time evaluator; `CompileTimeConditionEvaluator` implements §19.3 |
| `Koto.ResolveIdentifier`, `Koto.AddAttribute`, `DeclarationContainerKoto.AddGenericArguments/AddOrigins`, `CodeBlockKoto.ReplaceItem`, `TokenReader.CreateInlineReader`, `Tokenizer.ToReadOnlySequence`, `SourceRange.FromString`, `IdentifierHelper.IsAsciiPart`, `TokenHelper.IsBlockToken`, `ControlFlowContext.IsResultRequiringLabeledBlock`, `LspHelper.ToLspSeverity` | Never called |
| `Solution.LoadForRun`, `SingleFile` and their strings | `run` already loads projects through the build path |
| Kimigayo `DumpToConsole`, `ReadLine`, LogLevel/hash console overloads | Never called |
| `EmissionPlan`, `MinimalEmission` | Replaced by the components below |

## Emission components

| Component | Responsibility |
| --- | --- |
| `LlvmEmitter` | Public entry (`Compilation.Emission`): module gate over target, current Binding/startup/ownership, external modules and selected bodies; plans `__kimi_start` |
| `BodyLowering` | Lowers one verified ownership body; validates cleanup plans, abort edges and every operation kind, including unreachable operations |
| `EmissionModule`, `EmissionFunction`, `EmissionInstruction`, `EmissionOperand` | Closed, pooled physical plan; no syntax or analysis references |
| `LlvmModuleWriter` | Streams the plan into the destination `TextWriter`; one serialization rule per opcode |
| `LlvmConstantPool` | Private `unnamed_addr` constants shared by UTF-8 content; empty strings have no constant |
| `SourceLocationTable` | Escaped `path:line:column` runtime locations cached per operation site |
| `FunctionAbi`, `WindowsLowering` | Physical signatures shared by definitions and calls; String, Unit and §21.1.4 scalar representations |
| `WindowsProfile` | Codegen settings, backend supply identity, entry symbol, runtime imports and provided symbols shared by IR, manifest, llc and dependency checks |

The Hello IR is unchanged apart from the header comment: one string slot, one Static store, one Core call and `ret void`. `__kimi_start` calls the entry body, then `__kimi_exit(0)`. Sequential calls reuse one slot per temporary and one constant per distinct string. Each owned argument transfers to the callee, which destroys it once.

## Extending emission

1. **Operations.** Add the operation kind to `BodyLowering.LowerOperation` together with an unsupported-case test. The final kind check deliberately rejects anything not listed.
2. **Types.** Add a `ValueLowering` with verified layout before any operation uses it. Borrows, object handles and aggregates have no implicit `ptr` fallback.
3. **Control flow.** Replace the straight-path walk with explicit blocks and terminators; keep edge cleanup plans and use `phi` only from actual predecessors (§21.4.4).
4. **User functions.** Add a worklist keyed by declaration identity, concrete arguments and selected implementation (§21.4.1). Create `EmissionFunction`s before lowering bodies so calls reference prepared `FunctionAbi`s; mangle names with Kotonoha and declaration identity (§21.5.2).
5. **Results.** Add result slots or SSA values to `EmissionInstruction`; `LlvmModuleWriter.WriteCall` currently rejects non-void callees.

## Measurements

A local Release probe created 200 fresh Hello compilations and averaged the last 100:

| Phase | Before | After |
| --- | --- | --- |
| Emission (first, per compilation) | 36,600 B / 17.5 µs | 3,816 B / 10.9 µs |
| Re-emission (warm) | 0 B | 0 B / 6.2 µs |
| Binding construction + first Bind | 30,664 B / ~78 µs (combined) | 15,760 B / 20 µs + 14,904 B / 62 µs |
| Rebind (warm) | — | 0 B / ~48 µs |

The Binding numbers did not change with this work; they are split here only for diagnosis. Warm Bind time is spread across Core catalog steps; the largest is BindConstraints at ~9 µs. These are informative local timings.

## Validation

All 2,868 C# tests pass in Debug and Release, with zero build warnings. LLVM 22.1.8 passes 64 native executions per compiler configuration. CLI build and run of examples/Hello succeed. A multi-call project prints `first\n\n日本語\0x\nfirst\n` byte-exact at O0 and O2.
