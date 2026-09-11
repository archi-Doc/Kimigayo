# Kimigayo Compiler Implementation Status

Implementation coverage is informative and does not weaken language rules or Compiler requirements. All implementation-progress notes are centralized here; `planned` in a retained snapshot means implementation work, not permission to use an undesigned feature.

Section references follow SPEC.md. C.22 records the adopted Lowering/Windows profile; C.23 records startup and Core.writeLine Binding; C.24 records whole-Place ownership and cleanup analysis; C.25 records the Core declaration catalog and native backend candidate. Specification adoption is separate from implementation. Later progress entries supersede earlier snapshots for their listed cases.

### C.1. Coverage summary

This table defines the recorded status of each compiler stage. The notes below describe covered cases and specific remaining work; they do not assign a separate stage status. Parsing alone does not guarantee execution. **Implemented** and **Partial** are limited to the coverage described below. **Not implemented** means the recorded stage is unavailable; it does not imply that all related design details are settled; **Not assessed** means the recorded notes do not establish that stage's coverage. **N/A** means the feature has no such stage.

| Feature | Parsing | Binding | Analysis | Lowering | Runtime |
| --- | --- | --- | --- | --- | --- |
| Functions and Constraint Clauses | Partial | Partial; see C.16, C.18–C.21 | Partial | Not implemented | Not implemented |
| Static Contracts and associated Types | Partial; see C.4, C.21 | Partial; see C.20–C.21 | Partial declaration/path verification | Not implemented | N/A |
| `#if` / `#match` | Implemented | Not implemented | Partial | Not assessed | N/A |
| Types | Partial | Partial; see C.16–C.19 | Partial | Not implemented | Not implemented |
| Core intrinsic identities and Copy / Owned classification | Partial; conditional Copy clauses | Partial; see C.19, C.25 | Partial; see C.19 | N/A | N/A |
| Origins | Partial | Partial; see C.17 | Partial declaration requirements; see C.17 | Not implemented | Not implemented |
| Properties | Implemented | Partial; declarations/witnesses, C.21 | Partial declaration verification | Not implemented | Not implemented |
| Constructors and aggregate destruction | Implemented | Not implemented | Partial | Not implemented | Not implemented |
| `@Type` | Implemented | Not implemented | Partial | Not implemented | Not implemented |
| Ordinary Copy / Move acquisition | N/A; no dedicated Move operator | Partial; committed calls and local write permissions | Partial; whole Places, initialization, Move and cleanup, C.24 | Not implemented | Not implemented |
| Control flow and `defer` | Implemented | Partial; see C.16 | Partial | Not implemented | Not implemented |
| Enum Cases, Patterns, and guards | Implemented | Partial; Case payload Types only, C.19 | Partial; payload capabilities, C.19 | Not implemented | Not implemented |
| Explicit full specialization and generic code sharing | Source specialization syntax | Not implemented | Not implemented | Not implemented | Not implemented |
| Option / Result / Abort | Not assessed | Not implemented | Not implemented | Not implemented | Not implemented |
| Core.writeLine and executable startup | Implemented source forms | Partial; canonical function and startup selection, C.23 | Partial; startup contracts, whole-Place ownership and cleanup, C.23–C.24 | Not implemented | Not implemented |

Build inputs, source snapshots, strings, generated sources, and backend restrictions are described in the detailed notes. A stage marked Implemented does not certify a fully checked or executable program. A rule's design status is listed separately under Deferred features.

The front end retains captures, Callable requirements, runtime `is` targets, `require`, and object declaration syntax as Koto trees. **C.16–C.21 and C.23–C.24 record Binding and analysis coverage and supersede earlier notes for the cases they list.** Refinement, ownership, dispatch, and execution remain incomplete. See C.15 for the Property and Move syntax update; C.8–C.14 retain historical review snapshots.

### C.2. Builds, modules, and source artifacts

**Current implementation status:** project loading, target preparation, tokenization, parsing, early directive selection, partial Binding (C.16), and partial control-flow/type analysis are implemented. The current LLVM backend preparation requires a supported pointer width and LLVM data-layout string. The current `Build` API reports front-end checks only; it does not certify a finalized program or produce a binary.

Loading external Kotonoha libraries is not yet implemented.

The recorded compiler supports only its current language version. Exact version selection, fallback defaults, rejection of unsupported versions, and build metadata are specified.

The current source-snapshot serialization/reparse facility is not a [binary interface](SPEC.md#183-source-artifacts-and-binary-interfaces).

### C.3. Lexical forms and types

The current front end parses escaped strings, raw strings, and string interpolation, including nested expressions. Escape sequences are validated during parsing; evaluating interpolated strings is deferred to later compilation stages.

NumberLiteralKoto retains exact floating-point source spellings until contextual fitting and preserves them through source serialization. Its existing basic-value adapter still evaluates finite values as `f64`; Binding fits floating literals directly at the selected `f32`/`f64` precision (C.16).

The Parser builds fixed-array Types `[N of T]`, unevaluated length expressions, function length parameters/arguments, and initializer-dependent `_` element syntax. Binding handles concrete literal lengths (C.16); general constant evaluation, length and element inference, layout, ownership, and code generation remain unimplemented.

The front end parses recursive Semantics prefixes, distinct grouped and Tuple Types, and independently annotated inner Origins, preserving them through writing and source serialization. Type resolution, layout validation, subtyping, ownership rules, and most Type semantics remain unimplemented; parsing a nested Type or storage-borrow target does not establish its semantic legality. Syntax-level control-flow facts retain supported nested pointer Types and leave unresolved reference/Origin checks pending.

Enum bodies, payload Cases, inferred Case expressions, dedicated Patterns, and guards are parsed and preserved. Qualified Case expressions retain ordinary member/invocation syntax for later Binding. Case Symbols, acquisition, payload Move Paths, guard Loans, and coverage diagnostics remain unimplemented. Extension declarations are diagnosed as unsupported.

Split-structure integration, generated-source integration, and layout generation are planned, not implemented.

Inherited member lookup and protected-receiver checks have partial Binding coverage (C.21); complete inheritance/object legality and execution remain pending. The Parser retains `open struct`, base clauses, and compound access spellings. This does not validate inheritance or overrides. Inherited lookup and virtual/override declaration spellings retain their syntax boundaries. Object dispatch, metadata, upcasts, casts, compatibility, base lifetime, and Copy rules are specified; this documentation integration does not establish their implementation.

The Parser supports Origin lists on structures and functions, simple and qualified annotations, intersections, and named arguments. Origin name resolution, inference, variance analysis, and borrow checking are not implemented.

### C.4. Declarations and compile-time directives

Complete-Type slot/pair binding, structural Signature normalization, explicit `specialize func` selection, and the generic code-sharing policy are specified by this revision, not implemented by this documentation change. Parser acceptance alone does not establish these binding, validation, metadata, or backend guarantees.

The current Parser stores leading Constraint Clauses separately from executable body items after immediately selecting their compile-time directives. It checks clause subjects against the declared generic parameters and diagnoses clauses placed after executable items. Semantic validation of Constraints during Binding and specialization is not implemented.

ContractKoto retains base requirements, function requirements, `property` requirements with inline operations or explicit bodyless signatures, bare associated-Type declarations, and qualified specifications/projections. Constraint collection precedes executable body items. Refinement, conformance matching, mappings, and access checks require Binding. Any accepted generic Contract headers do not establish language support; user-defined generic Contracts are excluded. Runtime Contract Views remain outside the initial Contract implementation.

The implemented static Binding subset, including function/Property witnesses, associated Types, conditional conformance and implementation blocks, is described in C.20–C.21. Complete expression/effect/ownership verification remains pending.

**Current implementation status:** the Parser validates and evaluates environment-only Conditions in one traversal, using only the prepared Compilation values and Project settings. Unknown Names are diagnosed immediately at their source locations; results are True, False, or Error. Both logical operands and all explicit Conditions of a reached #match are validated, including nested Conditions in unselected arms except inside False #if targets. Valid directives select syntax during parsing. Pending-condition storage, deferred #if nodes, and Directive Binding are removed; invalid #match groups remain only for recovery.

### C.5. Properties

The Parser distinguishes stored `let`/`var`, `computed`, and Contract `property` declarations. It retains standard accessors, full custom/required signatures (receiver, setter input, result Types and Origins), bodies, source order, and spans. It checks declaration context, required getter/signature/body syntax, duplicate accessors, let setters, receiver presence, Unit setter result syntax, and forbidden inline forms, initializers, parameter forms, and requirement modifiers/Attributes. Control-flow analysis checks known accessor result Types. C.21 records subsequent accessor declaration/context binding, storage classification, access/Copy checks and Property witnesses. General expression operation selection, initialization/consume/Loan verification and execution remain incomplete; successful parsing alone does not validate them.

### C.6. Expressions and operators

The Parser supports `@Type` precedence, left associativity, and generic/comparison boundaries. `move` is an ordinary Name: `source@move` parses as Type/Semantics adaptation, with target lookup deferred to Binding. There is no dedicated Move syntax kind; capture entries permit ordinary acquisition, `@ref`, or `@uniq`, and `var` permits only ordinary acquisition. Copy/Move semantics remain unimplemented. Basic expressions, argument labels/defaults, collections, and anonymous functions have syntax-tree support. Reserved member-name restrictions and the digit-only Tuple member syntax are validated.

General type inference, overload and argument matching, function-value execution, numeric checks, dictionary duplicate detection, evaluation order during execution, and single-access Property updates require semantic analysis and runtime implementation. Abort name/type validation, diagnostics, and common termination handling are also planned. Control-flow and cleanup coverage is detailed below. Examples using application-specific functions or Types illustrate semantics rather than promise standard-library APIs.

Implementation references:

- [Parser.cs](Kimi/Compiler/Parsing/Parser.cs) and [expression Koto nodes](Kimi/Compiler/Parsing/Koto/Expressions): syntax and precedence.
- [ControlFlowAnalysis.cs](Kimi/Compiler/Analysis/ControlFlowAnalysis.cs): results, transfers, short-circuit paths, and partial unsafe checks.
- [ExpressionPrecedenceTest.cs](xUnitTest/Tests/ExpressionPrecedenceTest.cs), [ParserRegressionTest.cs](xUnitTest/Tests/ParserRegressionTest.cs), and [SpecConformanceParseTest.cs](xUnitTest/Tests/SpecConformanceParseTest.cs): grouping, diagnostics, generic boundaries, and expression syntax.
- [CollectionLiteralParseTest.cs](xUnitTest/Tests/CollectionLiteralParseTest.cs) and [RangeIndexParseTest.cs](xUnitTest/Tests/RangeIndexParseTest.cs): collection and boundary syntax.
- [ControlFlowAnalysisTest.cs](xUnitTest/Tests/ControlFlowAnalysisTest.cs) and [ControlFlowRevisionParseTest.cs](xUnitTest/Tests/ControlFlowRevisionParseTest.cs): control constructs and result checks.

### C.7. Control flow and failure handling

The constructor/destruction coverage row includes only the existing analysis of executable-body control flow. Constructor selection and synthesis, completion checks, general destructor declaration validation, per-component/base cleanup, reentry prevention, and final object release require semantic and runtime implementation. The Parser records `init`, `deinit`, constructor references, and base initializers; these nodes do not establish lifetime support.

**Implementation status:** The Parser preserves explicit branch body forms. Control-flow analysis checks selections, loops, value-producing Labeled Blocks, lexical transfer targets, and the completion effects of explicitly registered Deferred Blocks. It also checks lexical Unsafe permission for known operations and binder-selected function references. The default type provider handles primitive literals, simple declared Types, and basic raw-pointer and contextual `null` checks. General name/overload resolution, conversions, pattern Binding, ownership, automatic destruction, Origin compatibility, and runtime cleanup generation remain planned; unresolved checks are exposed as pending obligations. Valid compile-time directives are selected before analysis; bodies containing invalid #match groups are skipped after parser diagnostics.

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

Open design remains for concurrency/memory ordering/thread transfer, extension declarations, user-defined arithmetic, general Attribute semantics other than LibraryImport, and the FFI extensions listed in SPEC §22.3. These are explicit boundaries, not missing permissions supplied by parser acceptance. Previously deferred features outside this review remain deferred.

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

No unresolved design decision remains for these six items. 長さに必要な限定的な定数評価はSPEC §4に従う。General constant evaluation, struct static-member modifiers, and future Contract fragments are not introduced; existing unrelated deferred designs retain their previous status.

### C.10. Type and literal review (items 3–14)

Documentation only; compiler code and tests are unchanged. These decisions supersede conflicting earlier specification snapshots.

| Items | Decision and implementation gap |
| --- | --- |
| 3. Function parameters | Require a distinct parenthesized parameter list: `() -> U` has zero parameters; `(T,) -> U` has one `T` parameter. Parser.ParseDeclarationType and NestedTypeParseTest still accept the now-invalid `ref/(i32) -> bool`; dedicated list enforcement remains required. |
| 4–5. Semantics requirements | Clarify concrete-name equality already allowed by §8.2. Keep the existing category sets: `reference` includes `unsafe`; `owning or borrow` excludes only outer `unsafe`. Neither proves recursive safety. Requirement Binding is unimplemented; directive Conditions still reject every `is` test. |
| 6–7, 14. Origins | Clarify object-borrow-only outer Origins, explanatory pair projection `o`, and reflexive outlives notation. No new Origin binding or constraint-declaration syntax is introduced; semantic Origin support remains unimplemented. |
| 8–9. Numeric literals | Adopt exact decimal retention and one rounding to the determined floating Type; direct literal fitting failures are compile-time errors. NumberLiteralHelper.ParseFloat and NumberLiteralKoto.Literal currently parse/write via `f64`; representation, serialization, and contextual fitting require updates. |
| 10. Base prefixes | Require at least one valid digit. NumberLiteralScanTest and NumberLiteralParseTest still accept empty/separator-only prefixes as zero. Existing malformed-suffix scanning already rejects examples such as `0xg`. |
| 11. String newlines | Normalize physical LF/CRLF/CR to LF in both string forms, preserving spaces, escapes, and interpolated values. StringLiteralHelper and StringLiteralParseTest currently preserve physical newline sequences; value normalization and test expectations require updates. |
| 12. Unicode | Pin category and NFC data to Unicode 15.0.0; a minimum version with optional newer characters would retain acceptance differences. IdentifierHelper currently uses host Unicode APIs; fixed-data validation is unimplemented. |
| 13. Boolean validity | Specify `0x00`/`0x01` as the only valid `bool` storage representations. Backend valid-value enforcement is not established by the current front-end coverage. |

### C.11. Executable preparation and sequence review (2026-09-09)

今回の変更はSPEC.md／STATUS.mdのみ。コンパイラー・ランタイム・テストコードは変更していない。下表の「採用」は言語仕様または実装計画としての決定であり、実装完了ではない。S1–S10は添付の配列関連リストの順番、R1–R16は添付の全体レビューの番号に対応する。以前のスナップショットにある異なる仕様説明は、今回の決定で置き換わる。

| 項目 | 判断・反映先 | 実装上の根拠／残る実装 |
| --- | --- | --- |
| S1 充填構築 | §4.3–§4.4に未導入の境界を明記。workの配列は型形成の例であり、初期化済みbufferではない。既存の配列全体を引数・戻り値等から取得する手段と、その配列への操作は可能。 | 専用の充填・generator構築はない。評価回数、Copy条件、部分構築cleanupを含む新機能は当面導入しない。 |
| S2 / R13 layout | §4.1、§5.3、§21.1でsize／alignment／strideを定義し、固定配列sizeをN * stride(T)、ポインター歩進をstride(T)に統一。 | IrTargetはpointer width／LLVM DataLayoutを用意するが、言語aggregate layoutを実装していない。規則は推奨仕様として採用。 |
| S3 長さ式の型 | §4.2で既知の整数型からの推論を先行し、型情報がない場合だけisizeを既定にする。例のWidth／Heightはisize。 | 定数の既存Typeは維持する。暗黙に全定数をisizeへ変換する案は採らず、型の混在と既存Typeでのoverflowを明記。専用evaluatorは未実装。 |
| S4 長さの正規化 | §4.4でtyped +／*の二項の交換正規化を採用。N + 4と4 + Nは同じdependent Type／formation obligationになる。結合・分配・相殺は行わない。 | 記号的長さのBinding／正規化は未実装。通常の実行時演算順序は変えない。 |
| S5 private長さ定数 | §4.2の既存の展開許可を維持。公開signatureに使用した展開値は公開APIの一部で、変更は利用側を壊し得ることと理由を明記。 | privateの名前を公開する必要はない。artifactへの値・依存関係の保存と無効化は未実装。 |
| S6 Unit | §3.1.5／§21.1でsize 0、alignment 1、stride 0を採用。§21.3の物理ABI記述と整合させ、zero-sized Typeでも必要なcleanupを残す。 | ControlFlowType.Unitは意味上のTypeで、物理layoutの根拠ではない。推奨仕様として採用し、unsafe/()の歩進・indexを禁止。 |
| S7 可変Slice | §4.5とAppendix Dに未導入の境界と代替手段を明記。 | 所有配列だけに限定せず、既存§4.6の権限に従うwhole-array uniq経由のindex更新も可能。lending iteratorなしでもindex走査による変更はできる。 |
| S8 リテラル候補／借用要素 | §4.3–§4.4で固定配列注釈、typed intermediate、候補の区別方法を追加。Array優先の新しい順位は採用しない。 | 既定の独立式と候補ごとのfittingは区別する。staticを含むsafe borrowのArray格納は禁止されるので、Ownedだけで説明しない。 |
| S9 length syntax | §4.4を「§4.2の長さ定数式、lengthキーワードなし」に修正。 | 文言整理。専用の構文・slot Bindingは引き続き未実装。 |
| S10 統合先リンク | C.3をSPEC §4／§4.6へ修正。 | 存在しない統合前文書への参照を解消。 |
| R1 出力経路 | §22.1／新設§22.4でCore.writeLine(text: string) -> ()を採用。所有引数、UTF-8＋LF、NUL、書き込み失敗時Abortを定義し、完全な1行アプリ例を追加。 | Kimi.ConsoleServiceやPlaygroundのConsole.WriteLineはホストC#用であり言語APIではない。Core identity、string runtime、I/O、Lowering、linkは未実装。 |
| R2 最小サブセット | C.12に実行可能になるまでの範囲・設定案・検証条件を記載。完全なCoreと部分実装の適合性を§22.4で区別。 | ProjectFile／Projectの現状を確認。現Buildはfront-end結果のみで、実行ファイルは生成しない。 |
| R3 引数の可変性 | §7で通常引数とsetter valueを初期化済みlet相当とし、参照先の権限とbindingの再代入を区別。 | FunctionParameterKotoには名前・Type・default等はあるが可変性の完成したBindingモデルはない。規則は推奨仕様であり未実装。 |
| R4 receiver | 新設§7.3でselfの内部名、任意の記載位置、許容Type、禁止するrename/default、receiver-first呼び出し、unbound参照を定義。 | Parserはparameter listを保持するがreceiver indexや意味上の適合性を確定していない。§16.2.1の記載位置順cleanupを維持。 |
| R5 除外診断 | §19.3／§19.5／A.2／B.2を、prepared environmentとソース上の入れ子だけで診断が決まる規則に統一。False #ifの投機的な通常文法診断は抑制する。 | Parserは条件を即時評価し、未知名を即エラーにする。unselected #match内の到達条件、False #if内の検証省略、積み重ねた#if、診断のソース位置・再デシリアライズをテスト済み。将来の遅延・cache実装も同じ規則に従う。 |
| R6 改行した=> | §14.7.1で論理header行と物理行を区別し、if／else／match armの例を追加。元のheaderからbody深さを測る。 | 既存layout／Parserの対応範囲だけでは各新例の受理を保証しない。grammar-aware continuationの検証が必要。 |
| R7 空Container | §6.1.1とF.3でgroup／rootgroup／struct／contractのbody省略を許可。comment-only、EOF、選択後の空を定義。enum／実行Blockは除外。 | DeclarationContainerKoto.TryParseDeclarationContainerはStartBlockがある場合のみbodyをparseする。既存の省略経路を確認したが、全Containerの配置・選択後検証は未完成。 |
| R8 raw delimiter | §2.9.2に最大開始quote run、2個だけの空escaped string、終了runの余剰quoteを内容にする規則を追加。 | StringLiteralHelper.ScanStringLiteral／ScanRawStringLiteralの実装に一致。StringLiteralParseTestは6／8個だけのquoteをInvalidと期待しており、empty rawとするコメントよりassertionを根拠にした。 |
| R9 default所有権 | §7.2でpending argument slots、先行引数へのMove／変更禁止、新規Loanをdefault結果に保持しない制限、失敗時の逆取得順cleanupを定義。 | default式の構文保持はあるが実行・所有権解析はない。既存shared borrowのCopyと、結果に残らないshared inspectionは許可。 |
| R10 継承検索 | §9.5で最初のaccessible・role-compatibleな宣言層へ確定し、基底overloadを混合しない規則を採用。deferred indexも修正。 | 継承Bindingは未実装。候補の発見はref/Derivedからref/Baseへの未定義変換を追加しない。virtual等の最終構文は引き続き保留。 |
| R11 定数重複キー | §12.3.4で組み込みliteral／Unit／整数の直接符号／groupingに限定し、§17.3.4の最適化非依存規則と整合。 | DictionaryのBinding・重複検査は未実装。定数let・計算式・float等はこの静的検査に含めない。 |
| R12 floatキー | §12.3.4／§13.4.1でEquatableのNaN-reflexive equalityとsigned-zero equalityを採用。IEEE組み込み比較は維持。 | floatを禁止する追加型制約は採用しない。generic Contract呼び出しの特殊化、Tuple等のContract合成、hash整合性に注意。user equality法則違反だけでmemory unsafetyを許可しない。 |
| R14 static Loan | §15.6.4／A.3でcallee・default・initializer・cleanupを含む保守的なstatic effect summaryと、戻り値のstorage anchorを要求。 | 別Kotonoha・間接呼び出し・再帰を含む効果解析は未実装。欠けたsummaryを無効果と解釈しない。最初の実行サブセットではstatic Propertiesを扱わない。 |
| R15 Type alias | §18.1／Appendix Dでsource Type aliasは未導入と明記。aliasはContainerを開くだけ。moveというTypeの回避例はqualificationへ修正。 | AliasKoto／GroupKotoの経路はContainer pathを保持する。新しいType別名構文を実装する根拠ではない。 |
| R16 例と文法 | §6.2のViewにOriginを追加し、§8.3の混在Requirement／Boolean式を削除。F.3、関連本文・境界一覧を同期。 | 完全な言語例と宣言抜粋を区別する。今回追加した仕様例を現在のParserがすべて受理するという意味ではない。 |

### C.12. First executable milestone

**Adopted design; not yet executable.** The first program is either one document containing `::Core.writeLine("Hello, world!")` or one eligible root-level public main. Startup selection and Library restrictions are now normative in [SPEC §22.2](SPEC.md#222-program-startup-and-static-initialization). No EntrySource override or configured-empty-entry shortcut is adopted.

The compiler's initial output is a matched pre-optimization **.ll + .link.json** pair, not an .exe. LLVM optimization, object generation, linking, and execution are manual, separate validation stages under [SPEC §20.8](SPEC.md#208-initial-llvm-output-and-manual-build). Existing build/run command names do not establish an implemented generation or execution pipeline.

| Area | Current implementation / remaining work |
| --- | --- |
| Target preparation | ProjectFile retains Targets; existing IrTarget supplies pointer width and DataLayout. This does not verify windows-x64-v1 layout, emitted IR, or execution. |
| Settings | ProjectFile currently has Targets, KotonohaArray, Alias, LangVersion, CompileTimeSettings. OutputKind, OutputPath, NativeLibraries, and Optimization remain to be added. Their adopted schema/defaults are owned by SPEC §20.8, not this status document. |
| Front end / Core | Partial Binding includes compiler-owned Core identities and Copy/Owned (C.19) and later Contract/member work (C.20–C.21). Complete Core execution and writeLine generation remain pending. |
| Final acceptance | CFG ownership, initialization/consumption, Loan/Origin/lifetime, and cleanup verification must precede executable lowering. Partial Binding is not final acceptance. |
| Layout / ABI | Layout modes, scalar/Tuple/enum storage, ValueLowering and internal/C ABI are specified in SPEC §§21.1, 21.4, 22.3; their backend implementation is pending. |
| Emission / artifacts | Checked CFG to LLVM, emitted runtime/Core bodies, __kimi_start, matched manifest publication and hashes are pending. Library output is inspection-only, not a native library or cross-Kotonoha ABI. |
| Runtime | The six operations, seven Windows imports, UTF-8 string handle and normal/Abort exit codes are specified in SPEC §22.5; generated implementations are pending. |
| Toolchain supply | LLVM 22.1.8 and the versioned backend archive are adoption requirements, not evidence of installed tools or a validated supply package. |

The first execution subset covers ordinary functions, simple locals, Unit, string literals, required Copy/Move and cleanup, and Core.writeLine. Arrays, Dictionary, inheritance, closures, static Property execution, general generic sharing, and multi-Kotonoha linking are later execution coverage; no language rule is relaxed.

Completion requires the **produced executable itself** to emit the 14 bytes `Hello, world!\n`, empty stderr, and exit 0; host C# output is not a substitute. Test O0/O2 and compiler Debug/Release, ownership failures, unsupported generated operations, output failure/Abort code 1, and actual helper resolution. IR generation, LLVM verification/object generation, native linking, and execution are separately reported successes. See [SPEC §A.14](SPEC.md#a14-layout-llvm-and-runtime-verification) for acceptance tests.

### C.13. Remaining implementation selections

The initial design now fixes LLVM 22.1.8, windows-x64-v1, custom __kimi_start, normal exit 0 / Abort 1, the six runtime operations and seven Windows APIs, string storage, internal/C ABI, and the native backend-support contract. These are no longer open design selections. Implementation and supply validation remain incomplete.

- Obtain and validate the actual toolchain and SDK/library inputs. The backend catalog still needs an adopted immutable packageVersion and actual archive SHA-256 for kimi-backend-windows-x64 ABI 1; examples do not constitute supplied binaries.
- Implement/check native memcpy, memmove, memset and the special __chkstk ABI, with required unwind information and no hidden CRT, TLS, initialization or extra library dependencies. Supply _fltused once in generated IR, not in the archive.
- Connect the existing compiler entry points to checked .ll/.link.json production and truthful publication diagnostics. Automatic opt/llc/link/run, toolchain discovery, CLI execution arguments/status propagation, and CodeView/PDB generation are future extensions.
- Define physical representations before emitting borrow/object/common-function handles, dynamic collections, Slice, closures, rc/arc counts/ordering, or shared-generic metadata. C aggregate passing, exports/callbacks/varargs, extra alignment, Unicode console adaptation, arbitrary exit codes, and dynamic FP-environment control remain deferred.

The detailed normative rules and manual commands are in SPEC §§20.8–22.5. This status file records availability rather than duplicating their configuration or ABI tables.

### C.14. Tokenize/Parse increment (2026-09-09)

This increment ends at Koto construction. It adds no Binding or type-inference implementation.

- Lexing enforces the reserved spellings, root `::`, Tuple index boundaries, strict body indentation, and recognized generic continuation, including nested closing angles. ASCII identifier scanning also validates characters. Unicode category and NFC validation use generated Unicode 15.0.0 data, independent of the host OS; the uncommon normalization path uses stack storage or pooled storage for long identifiers.
- Shared declaration/body/name helpers cover enum Cases, static Contract requirements, constructors/destructors, base clauses, compound accessibility, complete-Type generic slots/pairs, length parameters, and explicit function specialization.
- Fixed-array Types, root-qualified names, captures, inferred anonymous parameter Types, `@move`, runtime `is`, `require`, Patterns, and guards retain syntax and source spans for later stages. Small forms share Koto child ownership, replacement, and writing. Tuple Types avoid eagerly allocating mutable lists.
- Decimal literals retain their original source spans and exact spelling instead of eagerly rounding through `f64`. Attribute placement and the `$abort(Expression)` shape are diagnosed syntactically.
- Debug and Release each pass 1,348 tests; the Release solution build has zero warnings and errors. An independent check against Unicode 15.0 NormalizationTest.txt passed all 95,370 normalization/name combinations. FrontEndSyntaxTest covers valid/invalid forms, parent/source ownership, parse/write/parse, and exact-decimal serialization. UnicodeIdentifierTest covers fixed-version categories, NFC, Hangul, and pooled normalization. The generator is Design/generate_unicode_identifiers.py; Unicode's license is retained alongside it.
- Benchmark/Benchmarks/FrontEndBenchmark.cs supplies validated common and extended syntax corpora with allocation measurement. Local Release measurements of the common corpus allocate 7,872 bytes per parse, versus 8,048 bytes at the starting revision (2.2% less); timing varies with host scheduling and is not a throughput guarantee.

The existing early directive evaluator and partial analyses are unchanged in scope. Semantic checks such as name/Case resolution, conformance, literal fitting, capture legality, length evaluation, inheritance, and foreign ABI validation remain downstream work. Historical snapshots above describe their original review dates, not the current front-end coverage.

### C.15. Property and Move syntax update (2026-09-10)

This increment aligns Lexing and Parsing with the Property revision. `computed` and `property` are contextual declaration keywords. PropertyKoto retains the declaration kind; PropertyAccessorKoto retains explicit receiver/input/result Types, including Origins, through child traversal, source writing, and source-artifact serialization. Compound accessor accessibility is parsed in its two-word source form.

The dedicated `@move` operation and capture spelling are removed. A Type named `move` follows ordinary adaptation-target parsing, including qualified/generic targets and Semantics prefixes. Benchmark source fixtures and syntax tests use the revised declarations and captures.

PropertyRevisionParseTest covers current and rejected forms, recovery to following declarations, contextual names, declaration/receiver contexts, parent links, source spans, writing, and serialization. Binding, accessor Type/Copy/Origin compatibility, capture and ownership analysis, Lowering, and Runtime remain downstream work.

Validation: all 1,445 Debug tests pass; the Release solution build completes with zero warnings and errors.

### C.16. Initial Binding pipeline (2026-09-10)

`Compilation.Bind()` now runs declaration collection and provisional Binding, a reserved no-op Mod execution boundary, then a fresh final Binding and Bound check. `Project.Build` uses these results and supplies the bound type provider to control-flow analysis. An incomplete Binding or pending control-flow obligation fails the build. This is still a front-end result, not a fully validated executable or an emitted binary.

Koto retains `BindingState`, `BoundType`, and `BoundSymbol`; invocation nodes retain the selected function, generic arguments, and source-argument-to-parameter mapping. There is no second Bound tree, Binding generation counter, or speculative syntax replacement. Successful and failed provisional results are reset before reanalysis. Symbols, canonical types, scopes, call-plan arrays, and collection capacity are reused. Binding diagnostics remain separate until explicitly reported after final analysis.

Implemented cases:

- Separate Type/Value lookup, lexical local visibility, forward local functions, source-document isolation of top-level execution scopes, source-local Container aliases, Core/Qualifier filtering, basic lexical/private/internal/public access, and qualified member lookup.
- Primitive and nominal Types, ordinary generic Type parameters and substitution, transparent grouping/owner normalization, tuples, function Type structure, and concrete literal-length fixed arrays. Complete numeric literal fitting uses the selected target Type; floating literals are parsed directly at the target precision.
- Explicit-or-Unit named function results, parameter/local/property Type binding, local initializer inference, primitive operations, assignments, condition checking, and basic blocks/transfers. Declaration Signature comparison normalizes ordinary generic parameter slots rather than their spelling.
- Direct calls with exact argument Types or fitted literals, positional/named/default arguments, ordinary generic inference, and explicit generic arguments. Candidate scratch state is isolated from Koto. Equal substituted Types use nongeneric preference and then fewer defaults; unrelated numeric Types remain incomparable. Unknown candidates and later declaration additions cannot be bypassed by retaining an old winner.
- Final unresolved/invalid diagnostics and the bridge to existing control-flow checks. Basic in-memory Symbol identity is independent of the removed KotoId concept.

Still incomplete: Contract/conformance and Constraint proof, associated Types, Semantics/Type pair projection, safe-handle Origin inference and ownership obligations, inheritance/subtyping and full API access-domain validation, fragment-header validation beyond parser merging, function/closure value acquisition, constructor/enum/Pattern binding, complete Property/accessor semantics, general fixed-array length evaluation/inference, specializations, nested contextual-call inference, adaptation ranking, external Kotonoha loading, and Mod execution. Unsupported semantic forms retain explicit unresolved obligations and cannot pass Bound checking; no synthetic success Type is substituted for them. Binding completion alone does not assert that the remaining ownership/layout/runtime checks are implemented.

All concrete node families provide `VisitChildrenCore` using direct child storage and indexed loops; Binding never uses `ChildNodes` iterators or Container-table snapshots. Nested Containers retain an ordered list alongside the lookup table. `ReplaceArgument` and `ReplaceItem` update known slots in constant time, preserving source/attribute provenance and parent links. The current binder does not need to replace any nodes.

`BindingTest` covers provisional dependencies resolved by appended declarations, invalidation of successful overload selection, Symbol/node/call-plan reuse, lexical scopes, source isolation, generic normalization/inference, literal fitting, candidate ordering, argument mappings, and unsupported obligations. A warm allocation regression test checks eight final passes over 256 generic calls allocate zero bytes on the current .NET runtime. `BindingBenchmark` separately measures final Binding and the complete provisional/final pipeline at 32 and 512 calls; parsing is excluded from the measured operations.

Validation: all 1,500 Debug tests pass; the Release solution build completes with zero warnings and errors.

### C.17. Complete Type representation and declaration Origins (2026-09-10)

Binding now retains nested Semantics, complete generic arguments, per-layer borrow Origins, declaration-ordered aggregate Origin arguments, and symbolic fixed-array lengths. Grouping and redundant owner layers normalize without discarding dependencies. Type identity uses canonical objects; Signature comparison separately excludes Origins and compares generic slots structurally. Shared structural fitting and substitution are used by the binder and its control-flow type provider. There is still no separate Bound tree or Binding generation.

Declaration schemas distinguish ordinary, pair, and function-length slots. A pair consumes one whole Type and exposes a SemanticsTarget projection; reconstructing the original pair preserves its whole Type, including Origins. Generic use-site whole-Type substitution is supported. Applying a projected Semantics to a different target and using an unproved SemanticsTarget as a value Type retain definition-side obligations. Matching Container fragments must agree on generic and Origin names, order, and kinds. Origin declarations retain their source spans.

Origin lookup resolves declared abstract binders, direct input Origins, static, input-carried named projections, and intersections. Origin expression nodes retain their resolved meaning. Named aggregate mappings reject unknown/duplicate names, normalize to declaration slots, and preserve explicitly supplied arguments. Self retains its containing declaration's complete generic/Origin bindings. Direct input omission, nested-input restrictions, result elision, instance storage requirements, and declaration-fixed local inference variables follow position-specific rules. Local omissions retain initializer-to-declaration constraints for the subsequent Origin solver; they are not silently replaced with static. Accessor signatures use their own direct input/result contexts; complete storage/accessor matching and accessor execution remain downstream work.

Origin variance and Loan requirements propagate over a reusable dependency worklist, including recursive and forward-referenced declaration schemas. Only consumers of changed summaries are requeued. Intersections share a deterministic structural normal form using binder source identity and slot position; only context-independent ordering proofs are currently simplified. Distinct input Loan dependencies are not represented as a license to acquire or erase Loans. Static storage checks follow actual stored components and generic substitution, excluding raw-pointer pointees and callable signatures from retained-borrow traversal.

Fixed-array Type binding supports checked literal arithmetic and bound length-slot expressions, with target-isize bounds and explicit instantiation obligations. General Constant-readable Binding evaluation, typed-constant arithmetic, length inference at calls, and layout remain incomplete.

Binding exposes outstanding obligations separately from resolved syntax, with their source uses, referenced Types/Origins, and deadlines. Unproved definition-side roles cannot pass final Bound checking. Body-Origin and instantiation obligations remain explicit; Project.Build cannot report success while any remain. This is not an ownership/lifetime certificate. Full Constraint/conformance and associated-Type proof, call-site Origin inference/propagation, general Origin constraint solving, full accessor/enum payload formation, runtime Contract Views, and actual Loan/ownership checking remain subsequent work. Calls requiring declaration-Origin inference stay unresolved rather than erasing the callee's Origin contract.

Type and Origin facts share one reference slot on Koto, so Origin support adds no extra reference field to every syntax node. Schemas, canonical Types/Origins/length expressions, scope dictionaries, obligation sets, dependency edges, and work buffers are reused. BindingBenchmark now includes Origin-rich declarations as well as ordinary generic calls. TypeBindingTest verifies nested preservation, positional elision, schema correspondence, pair substitution, Signature collisions, symbolic lengths, source spans, static storage, variance, and invalidation after appended storage changes. Its allocation regression test measures zero bytes for eight warmed final passes over 128 Origin-rich function declarations; the earlier 256-call zero-allocation regression also remains in place.

Validation: all 1,537 tests pass in both Debug and Release; the Release solution build completes with zero warnings and errors. Both warm allocation regression tests pass in both configurations.

### C.18. Constraint proof foundation and declaration validation (2026-09-11)

Binding collects declaration input Constraints before binding Signature Types. Existing IsKoto nodes retain compilation-interned bound propositions; ordinary runtime tests keep their separate interpretation. Proposition keys use reference identities for complete Types, Symbols, and subpropositions, preserving Origins and lexical generic binders. There is no second tree, generation counter, or name-based equivalence between unrelated parameters.

The shared proof engine implements SPEC §8.7's four outcomes: Proven, Refuted, Unknown, and Error. It supports exact assumptions, recursive conjunction elimination, conjunction/disjunction introduction and refutation, negation and double-negation normalization, concrete Type identity, and concrete Semantics/category membership. Both operands are validated even when another operand determines truth. Direct contradictions invalidate the environment. Disjunction elimination, contraposition, De Morgan rewriting, and inference from contradiction are deliberately absent. Public Binding.Prove queries either an existing proposition or a substitution of its declaration slots in a use-site environment.

Function calls infer arguments first, substitute complete Types into the candidate's Constraints, and require proof in the caller's environment. Candidate probing shares the existing pooled scratch buffers and commits only after selection; Constraints add no overload ranking or Signature distinction. Generic Container Type uses check their input Constraints using the same substitution/proof path. Missing proof cannot pass final validation. Invalid requirement Types cannot remain usable as assumptions. Pair-to-pair forwarding preserves both whole-Type and DirectTarget identities.

Definition-side role obligations can now be discharged from validated positive input facts: a pair target's value role under value/borrowed-value/pointer Semantics, an object target constrained to a supported struct Type, and Semantics application proved to require no outer Origin. Discharged obligations are removed with one in-place compaction pass. Other formation and Origin obligations retain their deadlines; this does not implement an Origin or Loan solver. Signature alpha comparison now renames only the compared functions' own slots, including symbolic lengths, and preserves enclosing binder identities. Function subject validation and the prohibition on user Contract generic/Origin parameters are also checked in Binding.

This milestone is the proof foundation, not complete static conformance or expression Type Checking. Named Contract input assumptions bind by Symbol identity, but unverified Self conformance declarations are never assumptions, and missing conformance never supplies negative evidence. Verified conformance mappings/refinements, associated-Type resolution, intrinsic Copy/Owned identities and derivation, parameterized Callable requirements, specializations, full declaration access-domain validation, and the remaining expression operation/acquisition rules are subsequent steps. Same-spelled user Contracts receive no intrinsic behavior. Source uses requiring these unfinished proofs cannot pass final checking.

Constraint environments, interned propositions, Types, and collection capacities are reused across provisional/final passes. ConstraintBindingTest exercises the permitted and forbidden proof rules, contradictions and error absorption, scoped substitution, Origin-sensitive identity, constrained calls and Type uses, declaration-order independence, Signature collisions, role obligations, and source-append invalidation. Eight warmed final passes over 128 constrained generic calls allocate zero bytes on the current .NET runtime; both earlier allocation regressions remain covered.

Validation: all 1,587 tests pass in Debug and Release; the Release solution build completes with zero warnings and errors.

### C.19. Core intrinsic identities and Copy / Owned (2026-09-11)

Each Compilation now owns a synthesized Core requirement module with stable Copy, Owned, and Callable Symbol Identities and compiler-compatible language-version metadata. Synthesis uses declaration construction directly and does not parse source or freeze target preparation. Binding exposes the identities through Compilation.Core / Binding.Core, ordinary qualified lookup, Core namespace aliases, and default requirement lookup. The reserved `::Core` path continues to reach the designated module when a user declaration shadows ordinary `Core` lookup. Same-spelled user declarations retain their ordinary identities. Binding validates the synthesized declaration shapes and rejects missing, duplicate, or incompatible definitions.

This is an intrinsic identity bootstrap, explicitly marked IsCompleteLibrary = false. It does not synthesize the rest of SPEC §22.1, implement Core runtime operations, or load/validate a complete external Core library. Callable has its designated identity, but its parameterized requirement semantics remain a later milestone; a bare Callable is not accepted as a complete requirement. Function Item / concrete Closure capability rules await their complete bound representations and capture analysis. Ordinary user Contract matching, refinement, and associated Types remain pending.

Copy and Owned plug into the existing four-valued Constraint proof engine and call applicability. Binding.ProveCopy / ProveOwned expose the same judgments to subsequent analyses without selecting Copy, Move, or Borrow operations. Copy classifies primitives, nested Semantics, common Function Types, tuples, fixed arrays (including zero-length element requirements), and explicitly opted-in structs/enums. `Self is Copy` validates every own stored Field, every enum Case payload, and the direct base under declaration input Constraints; user deinit prohibits derivation, and computed members contribute no storage. General inherited member Binding and full accessor execution are still separate work. An individual successful instantiation never validates an unproved generic Copy declaration.

Body-free `Self is Copy when P` clauses now parse at Type member positions. Their positive, comma-separated premises have a separate assumption scope; they do not become ordinary Type-formation Constraints. Derivation must succeed under those premises before a concrete use can gain Copy, and each use separately proves the substituted conditions. Insufficient definition premises, forbidden negation/disjunction, and Copy implementation bodies are rejected. This does not implement general conditional Contract implementation blocks or witness matching. The conditional syntax retains its operands and source spans on existing syntax forms.

Owned follows actual retained storage and substitutes both complete generic Types and declared Origin arguments. Absent/static retained Origins satisfy the guarantee; unresolved abstract/input/inference Origins remain Unknown and cannot prove `not Owned`. Raw-pointer pointees and common callable signatures do not count as stored borrow dependencies. A static outer borrow does not erase an inner dependency. Unused generic slots introduce no stored dependency. Owned continues to grant no exemption from the separate prohibition on retaining even static safe borrows in global/heap storage.

One reusable storage description per declaration supplies Field/base/enum-payload enumeration to capability analysis, declaration Origin requirements, and static-storage borrow checks. Field initializer inference is prepared before nominal capability queries; concrete leaf proofs remain available during that preparation. One substitution path handles both generic slots and named Origins. Direct concrete leaf judgments avoid graph allocation. Nontrivial capability dependencies use reusable work nodes, deduplicated edges, and a queue that wakes consumers only when results change. Copy cycles alone supply no proof; independent finite evidence can resolve them. Owned uses the structural reachability fixed point for finite storage graphs. Repeatedly expanding generic instances retain Unknown instead of materializing an unbounded type graph; required unknown judgments still fail at their deadline. Cached type depths account for shared type DAGs without expanding their tree size. Analysis results and active registrations are reset across Binding passes, including source-append changes; no Binding generation or replacement Bound tree is introduced.

IntrinsicCapabilityTest covers intrinsic identity/shadowing, concrete and symbolic capabilities, conditional and unconditional derivation, Origin substitution, pointer/unused-slot exclusions, enum payload storage, direct bases, computed members, cyclic and expanding dependencies, error/unknown behavior, and mutation invalidation. Two additional allocation regressions exercise 128 intrinsic-constrained calls with primitive arguments and with conditional-Copy generic struct arguments. Eight warmed final Binding passes allocate zero bytes in both cases on the current .NET runtime. BindingBenchmark now selects Calls, Origins, or Capabilities workloads at 32 and 512 calls; no throughput claim is made from the allocation tests.

Validation: all 1,657 tests pass in Debug and Release; the Release solution build completes with zero warnings and errors.

### C.20. Static function Contracts and associated Types (2026-09-11)

The next bounded increment implements Contract requirements/refinement, explicit associated-Type resolution, and unconditional function conformance. Existing Koto nodes retain their bound Symbols and complete Types; reusable BoundContract and BoundConformance metadata supplies the declaration relationships. No second Bound tree, serialization identifier, or Binding generation is introduced.

Refinement resolves Contract identities, rejects cycles and invalid parents, and deduplicates repeated paths by declaration identity. Independent same-named requirements and associated Types remain distinct. Ordinary Signature rules and implementation-identification keys remain separate: witness identification requires generic slot kinds/order, external labels, receiver presence, and normalized parameter structure, while ignoring Origins only during identification. Contradictory fixed associated identities and provably incompatible inherited labels/generic shapes are rejected even without concrete uses. Copy/Owned ancestors retain their compiler-designated derivation rules.

Associated declarations/specifications and short/qualified projections resolve by their original declaration identities. Explicit specifications and inherited Type-identity Constraints determine bindings before witness matching; implementation parameters, results, and bodies supply no inference evidence. Ordinary associated bindings must prove their Core role. Leading premises precede signature projections, including source-qualified Contract names and projections through another associated Type. Contract premises are expanded only through declared or referenced dependencies rather than materializing an unbounded recursive associated-Type closure. Ambiguous, missing, contradictory, cyclic, and Semantics-applied bindings cannot validate conformance. Projection uses retain their separate conformance-evidence checks.

Conformance registration is not evidence. After identifying exactly one implementation, verification checks requirement-to-implementation Constraint implication, input and result Origin contracts, structural result fitting, safety, and access. Origin correspondence is derived from the inputs and checked through the existing variance-aware Type relations; stronger static-input requirements or weaker result guarantees fail without retrying another member. Access checks cover public/internal/lexical domains, enclosing declaration restrictions, exposed associated/signature Types, and each ancestor conformance independently. General inheritance-based protected-domain proofs and additional subtype rules remain pending.

Only verified definition mappings prove ordinary conformance. Inherited requirements reuse the ancestor's verified mapping. BoundConformance.GetImplementation retrieves a member directly by requirement identity; generic requirement calls retain that identity and the symbolic conforming Type on their existing BoundCall. Instantiation therefore needs no new source member search. Definition verification uses declaration/requirement premises, never favorable concrete instantiations. Missing conformance still does not establish negative evidence. Body errors remain separate from declaration-contract verification and still prevent program acceptance.

Generic requirement calls share the existing call candidate/applicability/selection code. The candidate enumerator is a value type and uses indexed storage for requirements, with no iterator allocation. Existing Type substitution, Origin substitution, alpha correspondence, and proof routines are shared with witnesses and storage analysis. Input-anchored requirement-call Origins are substituted before checking argument fit and result Types; unresolved result-only Origins do not escape as the requirement's abstract binder. Repeated paths to one requirement are one candidate. Independent requirements are kept distinct; proving their full symbolic call-contract/mapping equivalence for candidate merging remains later work.

Binding owns reusable nested scratch buffers for Types, Origins, argument indices, and flags. Buffers grow only when a new nesting depth or capacity is required, retain peak capacity for the compilation's lifetime, and clear used reference slots on return. This avoids shared-pool contention causing warmed Binding allocations in concurrent compilations. Conformance/associated metadata, identity-indexed witness tables, name-indexed requirement groups, premise storage, and call plans also reuse capacity across Bind passes. Rebinding invalidates verified mappings and checks generated/changed declarations again.

Still pending: Property witness bridges and full accessor semantics; general conditional Contract implementation blocks and path coherence; complete shared-requirement candidate equivalence; general member inheritance; full ordinary-call Origin inference, adaptations, and expression operation selection; parameterized Callable and complete function/closure representations; the remaining required Core library declarations and intrinsic complete-Type Element exceptions; CFG-based initialization/Move/Loan/escape/cleanup verification. These forms do not gain synthetic successful mappings from this increment.

ContractBindingTest adds 73 cases covering positive/negative witnesses, refinements, associated identities and evidence, generic calls and premises, Origins, access, intrinsic ancestors, generated-member completion, and mutation invalidation. A workload with 128 requirement calls, refinement, and an associated binding allocates zero bytes across eight warmed final Bind passes; existing allocation regressions retain their zero-byte assertions. BindingBenchmark adds the Contracts scenario at 32 and 512 calls. Allocation checks are not throughput measurements.

Validation: all 1,730 tests pass in Debug and Release. Release solution build: zero warnings and errors.

### C.21. Property, conditional conformance, and inherited member Binding (2026-09-11)

This section supersedes the pending lists in C.20 for the implemented cases below. It records existing compiler work; the specification integration in C.22 introduces no further compiler implementation.

- Property declarations retain stored/computed/requirement distinctions, complete accessor contracts, contextual self/value/storage, access and Copy checks, and operation-specific witnesses.
- Conditional conformance keeps identity separate from proof paths, associated-Type and witness metadata, premise scopes and definition-side coherence. Implementation blocks share the enclosing namespace while retaining their conditions; ordinary call applicability carries Proven/Refuted/Unknown/Error outcomes.
- Inherited lookup shares access/role layer selection across calls and function/Property matching. Receiver position, original-receiver protected checks, constructed base Types, paths, Type/Origin substitutions and cached candidate operations are retained. A later receiver, condition, or accessor failure does not reopen a base layer.
- Standard storage projection is distinct from whole-base borrow. Function witnesses retain receiver correspondence. ObjectCompatible currently has a proof entry and retained Unknown obligation, not body/callee/returned-Loan verification; final acceptance rejects unproven projected calls.

Rebinding invalidates availability while reusing storage. The recorded inherited-receiver increment added 56 cases; its Release/Debug suites each passed 2,027 tests. Warm final Binding for 1/32/512 Types allocated zero bytes; the Benchmark Release build had no warnings/errors. These are prior implementation results, not tests rerun for C.22 or throughput measurements.

Full Property expression operations, function-value materialization, complete generic requirement merging and Origin inference, Access Effects/ObjectCompatible, CFG ownership/lifetime/cleanup, and execution remain incomplete. See the change records for [Property Binding](doc/Changes/2026-09-11%20Property%20Binding.md), [conditional conformance](doc/Changes/2026-09-11%20Conditional%20Conformance%20Binding.md), [conditional members](doc/Changes/2026-09-11%20Conditional%20Member%20Binding.md), and [inherited receivers](doc/Changes/2026-09-11%20Inherited%20Receiver%20Binding.md).

### C.22. Lowering and Windows profile specification integration (2026-09-11)

Integrated the adopted [startup/layout/LLVM/runtime design](doc/Design/2026-09-11%20Program%20Startup%20and%20Windows%20Runtime.md) into SPEC, taking that design over conflicting older rules. This is a documentation change; no lowering, runtime, configuration, or native supply implementation was added or validated.

| Contract now specified | Normative location | Implementation status |
| --- | --- | --- |
| Public main versus implicit runtime body; uniqueness, Library, shutdown | SPEC §§6.1.1, 22.2 | Dedicated startup Binding/emission pending |
| Layout Attribute, fragments, C exchange, scalars/Tuples/enums | SPEC §§6.5, 21.1 | Syntax infrastructure alone; layout semantics/generation pending |
| Finalized generation set, ValueLowering, FunctionAbi, result-slot ownership | SPEC §21.4 | Pending; analysis prerequisites incomplete |
| CFG/SSA, checked operations, transfers/constants, attributes, FP and unwind | SPEC §21.5 | Pending; initial i128 division/remainder/FP conversion explicitly unsupported by the adopted profile |
| Runtime, Windows symbols, string and writeLine | SPEC §22.5 | Pending |
| NativeLibraries, manifest/publication, O0/O2 and manual build | SPEC §20.8 | Settings, artifact generation, and integration pending |
| Backend supply identity, hash, symbols, adoption tests | SPEC §§21.5.7, A.14 | Native archive/catalog validation pending |

Removed superseded .exe-output, automatic link/run, EntrySource selection, and unspecified initial runtime-ABI plans from C.12–C.13. Older historical entries remain snapshots; C.21 describes newer Binding coverage. Specification examples and tool commands do not certify that an executable or backend archive currently exists.

### C.23. Startup selection and Core.writeLine Binding (2026-09-11)

ProjectFile.OutputKind defaults to Application and supports Library using the specified string configuration values; unknown names are rejected. Project.Build now performs output-specific startup checking after final Binding. Ordinary Bind remains usable for declaration fragments. Binding.CheckStartup retains original source/body identities and reusable runtime-item/diagnostic buffers, invalidated on every Bind. It rejects missing, mixed or multiple Application startup candidates and Library runtime items, including Unit expressions and uninitialized locals.

Source-root public main is registered in the shared root while retaining its declaration-site scope and aliases. Application validates every such main, without choosing a convenient overload; Library keeps ordinary function rules. Other root functions remain source-local. The generated top-level wrapper is not a legal return target. No public main or duplicate body tree is synthesized.

Core now owns the canonical safe writeLine(text: string) -> () function Symbol and its compiler-implementation identity. Ordinary lookup, call selection, Type checking and BoundCall storage are reused, including shadowing, aliases and reserved ::Core qualification. Core shape validation covers this declaration. It remains a partial bootstrap with IsCompleteLibrary = false; no runtime implementation is supplied. The control-flow bridge evaluates committed direct-call receivers and explicit arguments without treating the callee designator as an unresolved function value, and preserves unsafe-call checks. Unsupported specialization arguments no longer enter generic-parameter schema construction.

The [change record and initial execution subset table](doc/Changes/2026-09-11%20Startup%20Binding.md) distinguish front-end acceptance from the remaining acquisition, Move, initialization, cleanup and emission checks. Successful startup Binding does not prove ownership or permit executable lowering. LLVM IR, startup/runtime execution and native supply remain unimplemented.

StartupBindingTest adds 73 cases covering source selection, invalid signatures, duplicate mains, declaration environments, Library behavior, Core identity and call failures, reBind, control flow and configuration. Warm Compilation.Bind plus startup selection allocates zero bytes for both implicit and explicit bodies with 1, 32 and 512 calls. StartupBindingBenchmark provides separate selection and Bind-plus-selection workloads; no throughput result is claimed.

Validation: all 2,100 tests pass in Debug and Release. The Release Benchmark build completes with zero warnings and errors.

### C.24. Whole-Place ownership CFG and cleanup plans (2026-09-11)

Compilation now owns reusable ownership analysis after final Binding. It consumes committed call/adaptation information and shares the existing control-flow transfer resolver. Project.Build requires ownership verification as well as startup and Binding checks. Unsupported or invalid bodies prevent verification; every Bind invalidates prior results. This remains front-end verification, not executable finalization.

The initial subset covers primitive whole locals and parameters, owned string, temporary/result Places, direct owned-argument calls, scalar operations, simple comparisons, if/short-circuit/while control flow and ordinary return/loop transfers. Every supported Place use records Read, Consume, Write or Borrow. Borrow/Reborrow, defaults, captures, general Property/storage access, aggregates, partial Moves, defer execution, string concatenation and nonnumeric compound assignment remain unsupported. A non-Copy comparison view across complex RHS evaluation also requires later Loan support.

The CFG solves must-initialized, may-initialized, may-moved and assignment-history facts to a fixed point. Local let first placement is checked here; Binding retains structural assignment restrictions. Generic CopyOrMove plans check subsequent uses at the definition and retain conditional acquisition without changing the result Type. RHS-first state determines initialization, replacement or conditional replacement, including non-Copy self-assignment.

Edge cleanup plans retain Destroy/Skip/Conditional actions in execution order. Temporary Places include Static string literals. Scope cleanup preserves reverse declaration order independently of initialization time; parameters precede body bindings. Calls transfer acquired argument responsibility only at entry, and normal return secures the result before cleanup and delivery. Interrupted argument acquisition cleans earlier temporaries in the caller. Abort has no normal cleanup edge. These plans are reusable lowering inputs once remaining finalization requirements are implemented.

ControlFlowAnalysis now supports reanalysis with pooled node/boundary records and reusable transfer/registration buffers. Ownership uses identity indexes, indexed adjacency/incoming edges and retained state/work buffers. The solver stores converged input states only at single-entry, single-exit block leaders as four Place bit lanes and replays block prefixes on demand, instead of an operation-by-Place matrix (512 conditional string locals: about 1.3 ms warm analysis versus 28 ms before, on one local machine). Warm Bind plus both analyses allocates zero bytes for 1, 32 and 128 conditional string locals. OwnershipAnalysisBenchmark supplies analysis-only and combined workloads; no throughput improvement is claimed from allocation checks.

See the [change record and subset boundary](doc/Changes/2026-09-11%20Ownership%20CFG.md). Loan/Origin/lifetime verification, partial storage, complete cleanup effects, the concrete generation gate, LLVM IR and runtime execution remain pending. No unsupported operation gains an emission certificate.

Validation: 56 ownership cases were added and the old Binding-only let-reassignment expectation moved to CFG checking. All 2,155 tests pass through dotnet test in Debug and Release, including allocation assertions. The new retained-state workloads run separately from other test classes after overlapping allocation measurements showed intermittent failures; zero-byte assertions remain unchanged. Release Benchmark build: zero warnings and errors.

### C.25. Core declaration catalog and native backend candidate (2026-09-11)

Core now exposes an indexed catalog of the 18 required declaration groups, with stable identities and Missing/Invalid/Validated states. Copy, Owned, Callable and writeLine retain their existing Symbols; the other 14 entries remain Missing and supply no lookup candidates. Registration, restoration and shape validation share this catalog. Validation now checks Callable's declaration shape as well as Copy and Owned. IsCompleteLibrary requires every entry to validate and remains false; declaration completeness would still not establish body, layout or runtime completeness.

The catalog uses retained arrays, direct identity indexing and a ReadOnlySpan view. Rebinding checks changed declarations without replacing Symbols or allocating enumeration objects. Six new tests cover catalog states, all three intrinsic shape checks, missing Option lookup and zero-byte warmed Bind/catalog validation. Option/Result bodies and Case operations, Array/collection storage, Iterator/Iterable semantics, and the remaining Core runtime operations are not implemented. General enum Case Binding and ownership provide the next foundation for Option/Result.

The [Windows x64 backend sources and build instructions](backend/windows-x64/README.md) now supply a project-owned native candidate containing memcpy, memmove, memset and __chkstk. Memory operations share the forward-copy loop and use eight-byte chunks with byte tails, without heap allocation or out-of-range accesses. The archive has exactly four strong external symbols, Windows unwind records, no calls or undefined symbols, and no CRT dependency. LLVM 22.1.8 replaces 22.1.5 in SPEC and the retained startup/runtime design.

The build script verifies native COFF members and emitted test IR, links and runs O0/O2 tests with a custom entry and /NODEFAULTLIB, and records source/tool/SDK/archive hashes in bin/verification.json. Both pipelines pass overlap, boundary, return-pointer, guarded-page and stack-probe tests, including a compiler-generated 32 KiB frame. The current archive SHA-256 is `74f87661ca1493fedd9d688df5e64f1346ddf9190a12db2f1b012b2f509b500c`. Tools reporting a version matched 22.1.8; llvm-lib is explicitly recorded as unversioned with its executable hash.

This is a tested candidate, not an adopted backend package: adopted is false and packageVersion is unset. Assembly/unwind review, further register and OS unwind validation, representative Kimigayo-generated modules, and a frozen package version/hash remain adoption work. The compiler still has no LLVM emitter, complete generation gate, runtime execution or native supply catalog. These tests do not establish Hello world execution or a throughput improvement.

Validation: all 2,164 tests pass in Debug and Release, including the new zero-byte allocation assertion. The Release Benchmark build completes with zero warnings and errors. Native O0/O2 tests pass with LLVM 22.1.8 and Windows SDK 10.0.26100.0.
