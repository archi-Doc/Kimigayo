# Kimigayo Compiler Implementation Status

Implementation coverage is informative and does not weaken language rules or Compiler requirements. All implementation-progress notes are centralized here; `planned` in a retained snapshot means implementation work, not permission to use an undesigned feature.

### C.1. Coverage summary

This table defines the recorded status of each compiler stage. The notes below describe covered cases and specific remaining work; they do not assign a separate stage status. Parsing alone does not guarantee execution. **Implemented** and **Partial** are limited to the coverage described below. **Not implemented** means the recorded stage is unavailable; it does not imply that all related design details are settled; **Not assessed** means the recorded notes do not establish that stage's coverage. **N/A** means the feature has no such stage.

| Feature | Parsing | Binding | Analysis | Lowering | Runtime |
| --- | --- | --- | --- | --- | --- |
| Functions and Constraint Clauses | Partial | Not implemented | Partial | Not implemented | Not implemented |
| Static Contracts and associated Types | Partial; see C.4 | Not implemented | Not implemented | Not implemented | N/A |
| `#if` / `#match` | Implemented | Not implemented | Partial | Not assessed | N/A |
| Types | Partial | Not implemented | Partial | Not implemented | Not implemented |
| Origins | Partial | Not implemented | Not implemented | Not implemented | Not implemented |
| Properties | Implemented | Not implemented | Partial | Not implemented | Not implemented |
| Constructors and aggregate destruction | Not assessed | Not implemented | Partial | Not implemented | Not implemented |
| `@Type` | Implemented | Not implemented | Partial | Not implemented | Not implemented |
| `@move` / Consume | Not implemented | Not implemented | Not implemented | Not implemented | Not implemented |
| Control flow and `defer` | Implemented | Not implemented | Partial | Not implemented | Not implemented |
| Enum Cases, Patterns, and guards | Not implemented | Not implemented | Not implemented | Not implemented | Not implemented |
| Explicit full specialization and generic code sharing | Not assessed | Not implemented | Not implemented | Not implemented | Not implemented |
| Option / Result / Abort | Not assessed | Not implemented | Not implemented | Not implemented | Not implemented |

Build inputs, source snapshots, strings, generated sources, and backend restrictions are described in the detailed notes. A stage marked Implemented does not certify a fully checked or executable program. A rule's design status is listed separately under Deferred features.

The integrated Closure/capture/Callable model, runtime `is` and refinement, `require`, and Object Type System are language specifications, not implementation completion claims. Their complete parser, Binding, ownership, lowering, and runtime coverage has not been assessed here; existing broad coverage rows do not certify these additions.

### C.2. Builds, modules, and source artifacts

**Current implementation status:** project loading, target preparation, tokenization, parsing, early directive selection, and partial control-flow/type analysis are implemented. The current LLVM backend preparation requires a supported pointer width and LLVM data-layout string. The current `Build` API reports front-end checks only; it does not certify a finalized program or produce a binary.

Loading external Kotonoha libraries is not yet implemented.

The recorded compiler supports only its current language version. Exact version selection, fallback defaults, rejection of unsupported versions, and build metadata are specified.

The current source-snapshot serialization/reparse facility is not a [binary interface](SPEC.md#123-source-artifacts-and-binary-interfaces).

### C.3. Lexical forms and types

The current front end parses escaped strings, raw strings, and string interpolation, including nested expressions. Escape sequences are validated during parsing; evaluating interpolated strings is deferred to later compilation stages.

Compile-time basic-value evaluation currently supports integer representations fitting `i64` and finite literals parsed as `f64`; exact decimal retention and contextual single rounding remain unimplemented (C.10).

固定長配列の `[N of T]`、要素型の `_` 推論、長さの定数式、関数の長さジェネリクスの仕様本文は、Index・Range・Sliceと合わせて[統合前仕様](SEQUENCE_TYPES.md)にまとめている。今回の変更は文書のみ。専用の構文解析・定数束縛と評価・長さ推論・配置・所有権解析・コード生成は未実装であり、既存の基本値評価や配列リテラルの解析だけでは対応済みとしない。

The front end parses recursive Semantics prefixes, distinct grouped and Tuple Types, and independently annotated inner Origins, preserving them through writing and source serialization. Type resolution, layout validation, subtyping, ownership rules, and most Type semantics remain unimplemented; parsing a nested Type or storage-borrow target does not establish its semantic legality. Syntax-level control-flow facts retain supported nested pointer Types and leave unresolved reference/Origin checks pending.

Body parsing for `enum` and `extension` is not implemented.

The enum Case, construction, Pattern, guard, and coverage rules are specified but not implemented. `EnumKoto` skips its body, and `MatchArmKoto.Pattern` stores a general `Koto`; this does not implement dedicated Pattern parsing. Case Symbols, candidate/body binding plans, whole-Subject acquisition, payload Move Paths, guard Loans, and coverage diagnostics remain required. The general control-flow row does not certify these features.

Split-structure integration, generated-source integration, and layout generation are planned, not implemented.

Inheritance access domains, recursive API signature accessibility, and protected-receiver checks are specified but not implemented by general Binding. Lexing has modifier token kinds, which does not establish complete parsing or semantic support for `open struct`, base clauses, compound access, or overrides. Inheritance parsing coverage is not assessed here. Inherited lookup and virtual/override declaration spellings retain their syntax boundaries. Object dispatch, metadata, upcasts, casts, compatibility, base lifetime, and Copy rules are specified; this documentation integration does not establish their implementation.

The Parser supports Origin lists on structures and functions, simple and qualified annotations, intersections, and named arguments. Origin name resolution, inference, variance analysis, and borrow checking are not implemented.

### C.4. Declarations and compile-time directives

Complete-Type slot/pair binding, structural Signature normalization, explicit `specialize func` selection, and the generic code-sharing policy are specified by this revision, not implemented by this documentation change. Parser acceptance alone does not establish these binding, validation, metadata, or backend guarantees.

The current Parser stores leading Constraint Clauses separately from executable body items and preserves deferred directives on them. It checks clause subjects against the declared generic parameters and diagnoses clauses placed after executable items. Semantic validation of Constraints during Binding and specialization is not implemented.

The static Contract model is specified, not implemented by this documentation change. `ContractKoto` currently accepts inline Property requirements and `associate Name is requirement`; it does not implement function requirements or bare associated-Type declarations. Refinement, qualified specifications/projections, signature-first Constraint collection, and conformance matching/mappings/access checks require implementation. Any accepted generic Contract headers do not establish language support; user-defined generic Contracts are excluded. Runtime Contract Views remain outside the initial Contract implementation.

**Current implementation status:** the Parser validates the environment-only Condition expression set, evaluates known scalar operations, and propagates Errors before short-circuit truth results. Every directive `is` test is rejected; there is no requirement evaluation, Type narrowing, or generic-dependent selection. Its internal **Pending** result currently retains unresolved scalar Names as validation obligations; this is an implementation limitation, not a valid language dependency. Such Names can only resolve in the prepared environment and must otherwise be diagnosed before finalization. Pending nodes and control-flow PendingBinding reporting retain those validation obligations; unknown-Name finalization remains unimplemented.

### C.5. Properties

The Parser records Properties, inline and block accessors, explicit getter result annotations, and basic syntax errors. Control-flow analysis checks known accessor result Types. Accessor expansion, contextual binding of `self`, `storage`, and `value`, storage classification, access and initialization checks, general accessor type checking, Property Consume, and field-scoped standard operations are not implemented; successful parsing alone does not validate them.

### C.6. Expressions and operators

The Parser supports `@Type` precedence, left associativity, and generic/comparison boundaries. Treating a `@move` operand as a Type does not implement a lifetime operation; its stage status is listed in the coverage table. Basic expressions, argument labels/defaults, collections, and anonymous functions have syntax-tree support. Member-name restrictions still need additional validation.

General type inference, overload and argument matching, function-value execution, numeric checks, dictionary duplicate detection, evaluation order during execution, and single-access Property updates require semantic analysis and runtime implementation. Abort name/type validation, diagnostics, and common termination handling are also planned. Control-flow and cleanup coverage is detailed below. Examples using application-specific functions or Types illustrate semantics rather than promise standard-library APIs.

Implementation references:

- [Parser.cs](Kimi/Compiler/Parsing/Parser.cs) and [expression Koto nodes](Kimi/Compiler/Parsing/Koto/Expressions): syntax and precedence.
- [ControlFlowAnalysis.cs](Kimi/Compiler/Analysis/ControlFlowAnalysis.cs): results, transfers, short-circuit paths, and partial unsafe checks.
- [ExpressionPrecedenceTest.cs](xUnitTest/Tests/ExpressionPrecedenceTest.cs), [ParserRegressionTest.cs](xUnitTest/Tests/ParserRegressionTest.cs), and [SpecConformanceParseTest.cs](xUnitTest/Tests/SpecConformanceParseTest.cs): grouping, diagnostics, generic boundaries, and expression syntax.
- [CollectionLiteralParseTest.cs](xUnitTest/Tests/CollectionLiteralParseTest.cs) and [RangeIndexParseTest.cs](xUnitTest/Tests/RangeIndexParseTest.cs): collection and boundary syntax.
- [ControlFlowAnalysisTest.cs](xUnitTest/Tests/ControlFlowAnalysisTest.cs) and [ControlFlowRevisionParseTest.cs](xUnitTest/Tests/ControlFlowRevisionParseTest.cs): control constructs and result checks.

### C.7. Control flow and failure handling

The constructor/destruction coverage row includes only the existing analysis of executable-body control flow. Constructor selection and synthesis, completion checks, general destructor declaration validation, per-component/base cleanup, reentry prevention, and final object release require semantic and runtime implementation. Complete parsing coverage for `init` declarations, `Type.init` calls, and base-constructor initializers is not assessed; body parsing or a recognized keyword does not establish lifetime support.

**Implementation status:** The Parser preserves explicit branch body forms. Control-flow analysis checks selections, loops, value-producing Labeled Blocks, lexical transfer targets, and the completion effects of explicitly registered Deferred Blocks. It also checks lexical Unsafe permission for known operations and binder-selected function references. The default type provider handles primitive literals, simple declared Types, and basic raw-pointer and contextual `null` checks. General name/overload resolution, conversions, pattern Binding, ownership, automatic destruction, Origin compatibility, and runtime cleanup generation remain planned; unresolved checks are exposed as pending obligations. Bodies containing deferred compile-time directives await directive selection before analysis.

### C.8. Specification review integration (2026-09-08)

This documentation revision changes no compiler implementation. New recommendations adopted as language rules must not be mistaken for implemented features.

| Review items | Evidence and decision |
| --- | --- |
| A-1, A-6 | Tokenizer.ReadLine measures four-space levels, ignores comment-only lines, diagnoses misalignment, tracks delimiter/chain indentation, and closes blocks at EOF. Strict one-level body increases and grammar-aware Case/body precedence are adopted requirements; current raw leading-dot recognition and multi-level increase recovery do not fully enforce them. |
| A-2 | Tokenizer diagnoses every semicolon and emits a Separator only for recovery. 固定長配列は `[N of T]` に改訂し、セミコロンを許可する例外は削除した。 |
| A-3 | TokenKind/TokenHelper provide the punctuation and keyword baseline. The normative reserved set also incorporates existing prose (Self, init, access words, require, defer); internal contextual classification is not full enforcement. Root :: token handling remains required. |
| A-4 | NumberLiteralHelper currently scans an ordinary fraction after a member dot. The dedicated digit-only Tuple index split is a new specified requirement. |
| A-5 | Parser.ParseType consumes identifier/slash Semantics prefixes without lookup, stops slash after primitive or grouped heads, and recognizes adjacent generics. The specification records this commitment and mandatory grouping for ambiguous division; newer array/root-qualified targets still require support. |
| A-7, B-8 | Parser.ParseIfExpression joins across separators; ParseCodeBlock separates leading generic Constraints and diagnoses invalid subjects/late clauses. The specification makes layout/subject commitment explicit, including associated projections beyond current simple-name coverage. |
| A-8, A-10 | Root-qualified expression/Type and reserved construction suffix rules are specified; complete parsing/Binding coverage is not established by this revision. |
| A-9, C-2 | Parser.ParseAttributeKoto and HasLibraryImport already retain Attributes and allow bodyless imports; BlockSyntaxParseTest covers that exception. Restricted Attribute placement, exact import arguments, unsafe requirement, C ABI validation/linking and calls are newly specified, not implemented. |
| B-1, B-2, B-4, B-5, B-6 | Fixed-array Types, required Core identities, non-lending iteration (with a narrowly specified complete-Type Element exception), Stringify, and minimal comparison mappings are adopted recommendations. General Binding, protocol acquisition, collection/runtime support and lowering remain unavailable. |
| B-3 | Kimi/Misc/SemanticsMask.cs supplies the exact category sets. In particular reference includes object owners and unsafe, value is owner only, and object excludes object borrows. Internal Safe/All masks have no source category spelling. |
| B-7 | ExtensionKoto does not implement a body. The revision explicitly excludes extension declarations/candidates and retains their rules as future constraints. |
| C-1, C-3 | ProjectFile has no entry selection or static-initialization setting; no executable backend is completed. Single-entry startup, lazy static initialization, and explicit deferred concurrency are specification decisions. |
| D-1 | The abort production now requires exactly one Expression; this is not a claim of completed intrinsic argument validation. |
| D-2, D-3 | StringLiteralHelper preserves literal text and skips nested strings/chars/comments while finding interpolation ends. SpecConformanceParseTest checks nested interpolation and physical newline/indentation preservation. The scanner currently rejects recursive interpolation scanning at its internal depth counter >= 128; this counter is not a language-level nesting count. |
| D-4 | Condition binding scope and per-iteration identity are adopted rules; general Binding and associated cleanup remain unimplemented. |
| D-5, D-6 | Directive placement is enumerated without expanding the accepted item categories. The former Appendix C snapshot is preserved in this file, with a link remaining in SPEC.md. |

Open design remains for concurrency/memory ordering/thread transfer, extension declarations, user-defined arithmetic, general Attribute semantics other than LibraryImport, and the FFI extensions listed in SPEC §14.12. These are explicit boundaries, not missing permissions supplied by parser acceptance. Previously deferred features outside this review remain deferred.

### C.9. Follow-up specification review (items 1–6)

This revision changes documentation only. The settled language rules below do not establish compiler support. Earlier snapshot entries describe the implementation observed at that review.

| Item | Implementation evidence and specification decision |
| --- | --- |
| 1. Qualified enum Cases | Parser.TryParsePostfixExpression already constructs MemberAccessKoto and InvocationKoto without Case/Type lookup. Qualified expressions now have exactly that syntax; only leading-dot expressions have dedicated inferred-Case grammar. Binding classification and enum validation remain unimplemented. |
| 2. Generic body verification | ControlFlowTypeSystem exposes pending Type/getter facts; SyntaxControlFlowTypes leaves generic/user Copy and getter semantics unresolved. These hooks do not prove universal generic validity. Adopt definition-time universal verification, proved symbolic effect/getter families, and no hidden body capability conditions. General Binding, symbolic ownership proofs, and concrete finalization still require implementation. |
| 3. Static fixed-array indices | No fixed-array Move Path or overlap analyzer is implemented. Adopt integer-literal/grouping-only recognition; keep bounds failure and literal fitting distinct from static path eligibility. General constant folding must not expand accepted paths. |
| 4. static | Parser.ConsumeAttributeAndModifier and WriteModifier recognize/preserve ModifierKind.Static; PropertyParseTest covers this permissive syntax. There is no completed semantic model for struct static Properties. Adopt Origin-only contextual use of static and reject declaration modifiers; parser acceptance must be tightened accordingly. |
| 5. Pipeline | Compilation exposes front-end/control-flow analysis; the recorded Build API does not emit a binary. Correct the reference pipeline so concrete effects and cleanup precede lowering and shared executable code generation. This is a scheduling requirement, not completed backend work. |
| 6. Contract fragments | ContractKoto parses a declaration body; this does not establish merged Contract identity validation. Specify duplicate selected Contract declarations as errors and retain fragments only as a future feature. |

No unresolved design decision remains for these six items. 長さに必要な限定的な定数評価はSPEC §3.2.2に従う。General constant evaluation, struct static-member modifiers, and future Contract fragments are not introduced; existing unrelated deferred designs retain their previous status.

### C.10. Type and literal review (items 3–14)

Documentation only; compiler code and tests are unchanged. These decisions supersede conflicting earlier specification snapshots.

| Items | Decision and implementation gap |
| --- | --- |
| 3. Function parameters | Require a distinct parenthesized parameter list: `() -> U` has zero parameters; `(T,) -> U` has one `T` parameter. Parser.ParseDeclarationType and NestedTypeParseTest still accept the now-invalid `ref/(i32) -> bool`; dedicated list enforcement remains required. |
| 4–5. Semantics requirements | Clarify concrete-name equality already allowed by §4.4. Keep the existing category sets: `reference` includes `unsafe`; `owning or borrow` excludes only outer `unsafe`. Neither proves recursive safety. Requirement Binding is unimplemented; directive Conditions still reject every `is` test. |
| 6–7, 14. Origins | Clarify object-borrow-only outer Origins, explanatory pair projection `o`, and reflexive outlives notation. No new Origin binding or constraint-declaration syntax is introduced; semantic Origin support remains unimplemented. |
| 8–9. Numeric literals | Adopt exact decimal retention and one rounding to the determined floating Type; direct literal fitting failures are compile-time errors. NumberLiteralHelper.ParseFloat and NumberLiteralKoto.Literal currently parse/write via `f64`; representation, serialization, and contextual fitting require updates. |
| 10. Base prefixes | Require at least one valid digit. NumberLiteralScanTest and NumberLiteralParseTest still accept empty/separator-only prefixes as zero. Existing malformed-suffix scanning already rejects examples such as `0xg`. |
| 11. String newlines | Normalize physical LF/CRLF/CR to LF in both string forms, preserving spaces, escapes, and interpolated values. StringLiteralHelper and StringLiteralParseTest currently preserve physical newline sequences; value normalization and test expectations require updates. |
| 12. Unicode | Pin category and NFC data to Unicode 15.0.0; a minimum version with optional newer characters would retain acceptance differences. IdentifierHelper currently uses host Unicode APIs; fixed-data validation is unimplemented. |
| 13. Boolean validity | Specify `0x00`/`0x01` as the only valid `bool` storage representations. Backend valid-value enforcement is not established by the current front-end coverage. |
