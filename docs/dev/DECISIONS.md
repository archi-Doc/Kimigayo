# Compiler design decisions

Decisions about the compiler's structure that SPEC/IMPL (language and implementation contracts) and
[SETTLED](../SETTLED.md) (rejected language changes) do not record: new representations, components and vocabulary
entries, responsibilities moved between phases, temporary structures and working-process decisions. The reduction
[plan](../../draft/Proposals/2026-10-10%20Compiler%20Size%20Reduction%20and%20Single%20MIR%20Plan.md) §18 holds the
decisions of its adoption; this log holds the later ones. [COMPILER_ARCHITECTURE](COMPILER_ARCHITECTURE.md) states the
rules that result from them.

- An entry gives its id, date and decider, the decision, the options rejected and why, and the stage that removes a
  temporary structure. It is added in the commit that first implements it. A superseded entry stays and names its
  replacement.
- This log is the authority. Planning and handoff files under `artifacts/` (ignored by Git) cite entries by id instead of
  restating them; a decision recorded only there is not in force.
- Open questions belong in PLAN until decided.

## Working process

- **Q1 Quality mechanisms** (2026-10-12, owner).
  - Adopted: the size budget counts logical size, non-trivia C# tokens (Code Rules Analyzer A1, before R4); restructuring
    and behavior changes are separate commits (AGENTS, [VERIFICATION](VERIFICATION.md#behavior-preserving-commits)); design
    decisions are taken in the design step and recorded here (AGENTS); context stays small: from Code Rules Analyzer A1 each canonical
    component's entry file begins with its contract, and documents keep current rules and positions in short rows.
  - Deferred: metamorphic tests and fuzzing for shape-dependent code; consolidating tests into data-driven corpora;
    per-phase performance budgets; per-session goal metrics, the sequencing of R7 and R8 with milestone work, and lighter
    commit records.
- **Q2 Code Rules Analyzer** (2026-10-11, owner). The
  [proposal](../../draft/Changes/2026-10-11%20Code%20Rules%20Analyzer.md) §9: rules KR1xxx–KR3xxx and the built-in unused-code
  rules as build errors over a shrinking baseline, units A0–A8 after R3 and before R4. The limits are in
  COMPILER_ARCHITECTURE (structure limits).
- **Q3 Compiler allocations** (2026-10-11, owner, AGENTS `8071549f`). An allocation in the compiler is allowed when it
  makes equivalent work faster or reduces compiler code with a reasonable increase in memory, measured and recorded.
  Supersedes plan §18 P11 and §14-8. Native allocation counts and O2 run time may not regress.
- **Q4 Design selection and root-cause method** (2026-10-12, owner; AGENTS Implementation Design).
  - Adopted: options and designs are compared by the AGENTS priorities: coherence first (one owning component per
    decision, one representation per fact, the dependency rules, contracts without modes), then net size over the
    affected scope with deletions, then compilation speed; this replaces "fewer concepts and less code" (AGENTS since
    `8071549f`). Correcting the rule inside the right owning component is a root fix, and the local-change test is
    semantic (responsibility, contract and invariants unchanged, no other site sharing the cause). A design is one
    chain from the required meaning to the special cases it removes. Concepts are shared by meaning and contract; a new
    form is a row only under a canonical table. An open family is closed by a representation or a whitelist. A reviewer
    attempts a smaller design and classifies findings; two rounds on one family go to the owner. Roles and the two
    senses of "owner" are defined.
  - Reasons: concept and token counts favoured a design that hid a distinction (R4 U0a design A dropped independent
    records and was revised after review under SPEC 23.3.6); "the local patch and why it is rejected" presupposed
    rejecting correct owner-local fixes; shape-based sharing and generic tables can merge different contracts; path
    enumeration did not converge where a Type-level fix did (P26/G75).
  - Rejected: keeping counts as the first criterion; deleting the table-row rule (it is the target structure,
    COMPILER_ARCHITECTURE "Adding a feature"); appending a separate rule list instead of replacing the affected rows.
  - Not reopened: decisions of landed units. Decisions of units not yet on `dev` are re-checked only where the
    coherence comparison would change the choice.

## Code rules (2026-10-12)

- **CR-A0** (owner). Begin the adopted Code Rules Analyzer A0–A8 before R4. KR3003 covers only
  all-new-local `var (...)` deconstruction declarations with computed tuple elements. Existing-variable assignments
  are excluded: even uncaptured, syntactically independent locals change observably when a later RHS throws or reads
  a ref alias. Rejected: extending capture/dependency checks into an effect/alias proof; it adds machinery for a density
  rule. Compare diagnostics, semantic/HIR/MIR snapshots and generated IR, not DLL bytes (commit metadata differs).
  Intake changes architecture policy only; A1–A8 introduce implementation and enforcement.

- **CR-A1** (planning session, independently reviewed). CompilerSizeBudgetTest owns one file scan for all area and
  total caps. Count parsed C# tokens without trivia, EOF or missing tokens; discard each tree after counting. Header
  comments mark generated files; LLVM templates retain their newline cap. A0 measured 835,421 tokens / 128,885 C# lines,
  excluding 1,687 generated lines and 2,335 template lines. Convert approved caps at fixed area ratios (future totals at
  the whole-compiler ratio), rounding upward. Reject replacing each of the twelve scans with a parse: it repeats the
  same work. No compiler behavior changes; add entry-file contracts required by Q1. The counter initially belongs to
  the budget test; A2 moves it into the analyzer source owner and deletes the test copy. Reject a shared utility project
  for this single operation. Only aggregate totals survive each file; no syntax-tree cache or whole-compiler tree graph.

- **CR-A2** (planning session, two independent designs compared). CodeRulesAnalyzer owns one compilation's immutable
  configuration, file-local observations and one completion sort/merge with the baseline. Choose flat file batches,
  published once, over concurrent dictionaries per key: fewer allocations, locks and state owners. CodeRule owns the
  catalogue; CodeRulesConfiguration normalizes repository paths; CodeRulesSource owns counts and anchors;
  CodeRulesBaseline owns matching and stale rows.
  Move A1's counter into CodeRulesSource and delete its test copy. Pass the one globalconfig as both analyzer config
  and AdditionalFile, using SourceText's byte checksum and algorithm; scripts share one SARIF reader with Verify.
  Resolve suppression attributes semantically, including aliases; do not maintain an alias-name approximation.
  Actual SDK 10.0.401 csc tests retained suppressed KR/IDE0052 records, including member/type/assembly and unconditional
  attributes, and preserved BOM/CRLF checksums (A2 premise evidence). No fallback for incomplete Report or bad config.
  Observe over-limit measurements only. A completely observed baseline row whose measurement no longer exceeds its
  ordinary limit is redundant and is pruned, rather than retaining a smaller ineffective bound or resolving every
  member's symbol. Report absence as absence, never as a measured zero. This normalizes proposal §3.5's minimum rule;
  later approved lower limits do not restore deleted rows. Required exception/suppression checks are independent.
  KimiCodeRulesLog sets ErrorLog only in Kimi.csproj, with SARIF 2.1 in the project: a solution-wide ErrorLog is
  overwritten by later projects, and an unescaped command-line comma is an MSBuild property separator. Keep one
  shared SARIF reader for reporting and Verify. Initialize requires the newly introduced rule IDs and cannot replace
  existing keys. Static descriptor fields satisfy Roslyn release tracking; catalogue rows reference them without
  repeating IDs or messages. LocalOwner chooses the closest declaration with a documentation ID, cached with source
  identities; collection and name paths share that decision. This includes field declarators in initializers and
  excludes unrelated members from top-level collisions. A declaration without an ID is not a file identity.
  Incomplete declarations produce no member anchor; a successful compiler build remains mandatory for Report use.

- **CR-A3a** (planning session, two independent designs compared). A3's semantic dependency check exposed Koto's
  direct reads of Binding.IsRunning/Result, missed by the old call-only regex. HirTables owns HasCurrentBinding's
  existing fact: move the complete predicate there, using State(node), and keep Koto as an R6 forwarding property.
  Binding.ObjectViews and SemanticQuery read the owner directly. Preserve the exact short-circuit order: do not obtain
  Compilation.Binding before confirming Resolved, because that getter lazily allocates Binding. No state or cache is
  added. Reject relaxing the dependency rule or moving the predicate into SemanticQuery, which requires a query-instance
  access path from syntax. Prepare this behavior-preserving structure in a separate commit before A3 enforcement.
  Delete the syntax-owned predicate and Binding's reads through it. Measured delta: Binding +48 tokens / +4 lines,
  Parsing -25 tokens / -1 line. Verify rebind invalidation, SyntaxId/HIR reuse, allocation regressions and A0 snapshots.

- **CR-A3** (planning session, two independent designs compared). CodeRulesArchitecture owns one immutable resolved
  dependency/member/vocabulary policy per compilation. Configuration owns raw lists and locations. Enumerate source
  types once and publish all declaration layers per original type; partial types cannot hide a forbidden declaration.
  Resolve configured types once and cache each file's longest-prefix layer (compiler_root matches direct files only).
  Choose layer arrays over a fixed-width mask: no extra layer-count concept or cap. Reject per-reference display names,
  syntax reclassification and concurrent symbol caches. SimpleNameSyntax owns normal references; AttributeSyntax owns
  its terminal name and shares its constructor symbol with suppression checking. Qualification types, type arguments
  and attribute arguments keep their own references; XML documentation is not a dependency. This replaces the other
  independent design's complete-name/member-access classifier with fewer syntax-shape decisions.
  Member selectors match the configured type family, including derived Koto slots and overrides. Global child-walk
  prohibitions use bare member names, including declarations: removed members need not exist to remain forbidden.
  Resolve only candidate member names in unconstrained layers. Count each configured enum once, including aliases but
  excluding implicit fields; a missing enum is located KR9002 (Warning in Enforce/Report, Error in Verify), not invalid
  input that prevents Report completion. CodeRule owns stale severity and wording for both configuration and baseline.
  Delete the two MIR dependency tests, vocabulary theory, three migrated source-pattern rows and their exclusive
  helpers. Retain SemanticSlots only for the test-source ratchet, which is outside analyzer scope. Expected change:
  350–500 analyzer lines, about 250 test lines and 90–110 deleted test lines; compiler source delta zero. Measure actual
  csc/analyzer time; do not claim a speedup from replacing tests alone.
  Actual csc exposed A2's list truncation: Roslyn treats semicolons as inline comments, including in globalconfig.
  Choose the independent reviewer's comma-separated lists, verified with the actual parser, and replace all list
  separators together. Reject a separate AdditionalFile parser and escaping/compatibility paths: one file must have
  one grammar. Tests construct effective options through AnalyzerConfig.Parse/AnalyzerConfigSet from the same text
  supplied as AdditionalFile; delete their line-splitting approximation. This also preserves both legacy exclusions.
  Semantic checking then exposed 29 existing builder references to BoundArgumentOperation through CallPlan's published
  ReceiverOperation/ArgumentOperations. Name that already-public result type in mir_build's allow list only; reject
  transitive return-type allowance. The other two missed references were Koto's Binding-state decision, moved to its
  existing fact owner in the separate behavior-preserving CR-A3a commit. Neither repair creates a new language rule.

- **CR-A4** (planning session, two independent designs compared and reviewed). Extend CodeRulesArchitecture in
  .Structure with KR1004/1005/1006/1008, retaining one file walk and the existing observation/baseline pipeline.
  Resolve Koto, KotoKind, collection definitions and System.Exception once per compilation. After out/pattern tests
  exposed one declaration-ownership gap twice, both independent revisions replace the complete-type classifier with
  declared symbols for all collection declarations. A variable declaration owns its shared type once; designations,
  parameters (including inferred lambdas), foreach and members own their bindings/return type. Generic arguments,
  typeof and alias definitions are not declarations. A3's SimpleName symbol still supplies explicit creation types;
  implicit creations own their inferred type. Count declaration and creation separately, once each even if several
  interfaces match. A designation uses its immediate declaration/pattern's type syntax, or its own syntax if absent,
  never an outer pattern's type. Syntax-layer declarations are exempt; no collection-name approximation.
  Actual Roslyn 4.14 tests contradict its XML description: parameter BaseType/AllInterfaces omit constrained types.
  Both independent refinements choose ConstraintClosure only for nonempty type-parameter constraints: one local list
  is the work queue and result, with a visited set at insertion. Consume published ConstraintTypes, then reuse named
  InFamily and collection-interface checks. Named types allocate no traversal state. Reject recursive constraint
  inference, delegate/iterator allocation per reference and a retained cache. Cover chained and cyclic constraints.
  The declaration check shares one symbol for collection type and Supports return checks. CodeRulesSource.RecordMember
  publishes that symbol's identity only when observing a violation; delete eager ResolveDeclaration caching. Ordinary
  locals never enter the identity cache. Reject separate explicit/inferred declaration rules or a per-node fact table.
  Atomic patterns own actual dispatch tests, including switch arms/cases; is/as expressions also count. Logical
  pattern wrappers, var/discard captures and casts do not add tests. KotoKind comparisons use semantic operand types.
  Catch checks first exclude configured boundaries and filters, then match bare catch or exact System.Exception.
  Change NodeAnchor's input to a token sequence: callers select ordinary nodes or body-free catch headers; retain one
  streaming UTF-8 FNV implementation. Reject a hash mode/optional parameter or a wrapper without semantics.
  Both designs chose this existing owner; reject a second analyzer, per-node symbol cache and post-report deduplication.
  Delete the two migrated regex rows; compare their 96/13 subsets and explain semantic inventory differences.
  Initialize only these four rules through the complete Report path. Expected delta: 250–350 analyzer lines,
  150–220 test lines; compiler areas zero tokens/lines. Verify aliases, inferred/derived types, patterns, boundaries,
  header stability, snapshots and actual csc time. Later enforcement changes remain separate units.

- **CR-A5** (planning session, two independent designs compared and reviewed). CodeRulesDensity owns KR3001–3003
  in the existing file walk. Physical lines use the existing SourceText once; only over-limit lines find a token.
  Exclude a whole line only inside one multiline literal/text token, preserving checks in interpolation holes.
  CodeRulesSource owns line scope, location and hash through the same observation append as syntax observations.
  Reject a precomputed literal-span table, a second parse/walker and separate baseline or hash implementations.
  The outermost conditional before a callable boundary owns one observation; stop a bounded descendant search at
  the first nested conditional and prune anonymous/local functions. Reject observing each inner node then deduplicating.
  Computed deconstruction covers only flat var declarations of new locals and tuple-literal values; existing writes,
  discards and nested designations do not qualify. Simple operands are names, literals, this/default and member chains.
  Parentheses are transparent for both tuple and simple-operand recognition through one unwrap operation; retain the
  complete declaration anchor. Review found that separate shape checks would otherwise both flag and hide calculations.
  Mir rewrites preserve left-to-right evaluation, short-circuiting and target types; retain conditions to avoid changing
  KR1006 identities. Delete dense parallel declarations/nested conditionals, format long lines, then initialize KR3
  outside Mir only. Both designs chose these owners; expected 130–180 analyzer lines and 120–180 test lines, Mir token
  reduction from split declarations with reference-line growth. Prove semantic/HIR/MIR, diagnostics and representative
  IR equality, review every changed MIR expression and measure cumulative analyzer time before completion.
  Actual Report exposed null JSON cells: Select-Object cannot project ordered-dictionary keys as properties. Both
  script row producers now publish PSCustomObject rows, preserving ordered keys and the single merge/write path.
  Independent review chose this producer fix over another export adapter; test the public JSON and TSV roundtrips.
  The first cumulative timing exceeded 10% (3.617 s / 35.862 s). Keep the failed measurement and remove unnecessary
  declaration symbol queries at the existing owner: syntax-layer declarations need only the Supports check, and a
  predefined declared type cannot be a syntax-keyed collection; predefined types other than bool cannot be either
  kind of violation. Remaining method/local-function/property declarations retain the original semantic name and
  return-type check, including explicit interface implementations; reject
  copying the name rule into a syntax filter. Aliases, inferred types, user types and constraints keep the existing
  declared-symbol path. Independent review accepted this bounded syntax filter over new symbol caches or weakening
  rules. Re-measure and rerun the unit on the changed inputs.

- **CR-A6** (planning session, two independently written designs compared and reviewed). Analyzer.File owns one
  CSharpSyntaxWalker with entry/exit measurement frames; CodeRulesAccretion owns KR2001–2010. Replace the existing
  DescendantNodes loop, rejecting the root design's second reusable body walker and node-to-measurement tables.
  Save frames as call-stack values: methods, constructors, operators and local functions measure independently;
  properties, indexers and events combine accessor statements and reset accessor depth; field initializer lambdas
  belong to each variable. Local functions leave the parent count unchanged; lambda statements belong to the parent
  member but their depth restarts at zero. Else-if does not add another level. Keep one token counter.
  Parameter limits and logical chains are syntax-first. Resolve aliases only when a possible bool/string violation
  remains, and resolve calls only after an unnamed bool literal is found. A4's declared symbol may be passed to
  the current node's consumer, never stored in a new cache. Source identities are still published only for observations.
  Move transparent-parenthesis removal to Source for KR2005/KR3003; do not copy the operation into another rule.
  CodeRuleAnnotation is sparse source-authored reason/bound data on the existing observation. The existing attribute
  constructor resolution and Roslyn constant values feed it; there is no alias parser or inventory-only tree walk.
  Field attributes apply to each declared variable. Unsupported targets, invalid/duplicate rules, empty reasons and
  bounds at or below the ordinary limit fail at the annotation owner. Emit annotation observations even at zero;
  the existing sorted-key merge applies the bound to the whole member/rule group, including partial declaration/body
  pairs, and reports one KR9005 for baseline overlap or exceeding the bound. Report carries the reason and limit;
  scripts permit zero only on annotations and never add them to the baseline. A7 extends this same path with removal
  stages rather than introducing a second inventory. Attributes remain conditional and absent from product metadata.
  MIR changes use existing responsibilities and records: MirTerminator.Call keeps six essential inputs, with existing
  CheckTarget/Operand fields set by the caller; Call's first, Operand's expected and Row's effects become explicit.
  Replace Test's optional guard/observe modes with ordinary Test, match Guard and verification Observe operations.
  They retain existing scope/assignment/branch primitives; reject callbacks, strategies or a new lifetime carrier to
  eliminate a short sequence whose cleanup obligations differ. Into's flat closed-HirOp dispatch may use the measured
  KR2006 exception rather than a second dispatch. Move the existing formatting concern out of Expansions. Additional
  MIR findings require the actual Report and a responsibility-based decision before edits; no new compiler vocabulary,
  representation or side table is authorized. Expected tooling growth is roughly 350–550 analyzer and 200–350 test
  lines; shared traversal/annotation integration may exceed the proposal's estimate. Measure cumulative csc time and
  compiler token/line deltas, preserve evaluation order and allocation assertions, and prove snapshots/IR unchanged.

- **CR-A6a** (planning session, independently reviewed against the A6 Report). Only Into exceeds the MIR statement
  limit; Inline and Format do not require splitting. Remove Effect's second, syntax-based dispatch: Statement and
  Into map published HirOps directly to Require, Register, Discard and Transfer. Discard owns acquisition into its
  temporary; no new classification, gate or table is introduced. This removes three syntax tests and their baseline
  entries. MirDescriptors uses one explicit row per enum kind in every Of overload; repeated row values are data,
  not a new grouping concept or a logical predicate. Reject parenthesized chains and split case labels as evasion.
  MirTypes.Agree compares the existing identity, shape and component facts in the same short-circuit order with early
  returns. MirValidator.Unknown first rejects ineligible destinations, then checks the existing Opaque or operand
  propagation rule with guarded bounds; retain its reusable array and fixed-point order. No limits or vocabularies
  change. Re-measure Into's flat-dispatch exception after removing the redundant dispatch.

- **CR-A6b** (planning session, independently reviewed against csc/MSBuild evidence). Report's stale KR9002 is a
  warning in SARIF, but MSBuild's warnaserror promotes it after compilation and prevents Prune from acquiring a
  successful Report. Keep warnaserror and exempt only KR9002 from that logger promotion in the Report command.
  Do not change descriptors, severities, suppression checks or thresholds. Actual MSBuild regressions cover Report
  and Prune success, failure on another warning, and Verify's KR9002 remaining an error even with the logger exemption.

- **CR-A7** (planning session, independent designs compared and reviewed). Source-authored transitional declarations
  extend CR-A6's sparse annotation fact, constructor/constant resolution, canonical declaration key and sorted merge.
  Configuration.Stages alone orders removal stages; current greater than removal is overdue, equality is permitted.
  Complete Report includes every valid annotation with stage and ordinal value, including R0's zero. KR1007 never
  enters the baseline. Generalize KR9005 to invalid code-rule annotations rather than add an operational rule.
  Resolve annotations even in areas excluded from accretion checks; no second walk, inventory or alias resolver.
  An exception outside its rule's configured scope is invalid: it cannot relax an inapplicable rule or silently
  disappear from the audit used by approval checks. Transitional annotations still apply to the legacy areas.
  KR9000 publishes annotationKinds=exception,transitional; the complete-Report reader requires this marker so
  approval comparisons cannot mistake an older producer's missing annotations for an empty inventory.
  Mark all direct and indirect Koto semantic forwarding properties and the pipeline switch declarations for R6;
  HirPublisher, hirPublisher and HirPosition for R7; Capacity, FormatBuffer, FormatHint and FormatFinish for R8.
  Published version/cache entry points remain lasting contracts. LegacyRuntimeHelpers does not yet exist; its
  future producer must mark it at introduction. Conditional attributes leave no usage metadata in the product.
  Enable IDE0051/52/60 and remove the measured unread fields and unused parameters with their pure arguments:
  CLI constructors, documentation Cursor, BodyLowering and Binding's type/iteration routines. Frozen legacy changes
  are deletions only. Reject discarded underscore parameters, suppressions, token exemptions and lasting-type tags.
  Expected analyzer growth is about 120 lines plus tests; compiler deletions offset annotation tokens, measured at
  completion. Attribute resolution reuses the existing pass; product state and allocation behavior are unchanged.

- **CR-A7a** (planning session, independently reviewed after A7's retained 10.515% timing failure). The file walker
  already visits every token but also traverses all trivia, while KR2009 separately enumerates the same tokens.
  Use token depth: count each token once with Source's shared missing/EOF predicate, and inspect trivia only on tokens
  containing directives. The suppression owner alone consumes pragma-warning trivia, including on EOF and missing
  tokens. Delete the extra KR2009 token walk and the structured-trivia node path. No cache, table, exclusion or
  semantic candidate rule is added. Reject merely repeating timing and defer the separate conditional-expression
  traversal optimization. Existing token-count, directive, suppression and complete-inventory comparisons verify
  equivalence; matched Enforce measurement decides the gain. Expected net growth is below 20 analyzer lines.

- **CR-A8** (planning session, two independent designs compared and reviewed). verify-commit owns approval orchestration
  after exact verified-input matching; code-rules-approval owns Git trailer validation and relaxation comparison.
  A real Code-Rules-Approved trailer names an approver, calendar date and reason. Without one, compare the exact
  first parent and target: any globalconfig blob change, new/increased baseline key, new/increased exception or
  postponed existing transitional key requires approval. Pruning, reduced bounds, earlier stages and partial-file
  moves do not. Review still rejects removing only an attribute or renaming to evade policy.
  Reuse baseline keys and the analyzer's complete annotation inventory. Extract the ordinary Report build into
  code-rules-report's Invoke-KimiCodeRulesReport; both the command and commit audit use it. No second attribute
  parser, source-change allowlist or historical report cache. The no-trailer audit builds fresh Reports in isolated
  detached worktrees for both commits, verifies input stability and exact Git blobs, retains failure evidence and
  validates cleanup paths. Missing/older/incomplete reports fail. A valid trailer bypasses only this comparison,
  never Unit/Session input association. Reject the local text-diff patch (aliases, constants and partial moves) and
  a cached snapshot protocol (more provenance state for infrequent commit checks). Two additional Report builds
  occur only at unapproved commit association, outside ordinary compilation. Expected tooling growth is about
  130 lines plus tests, deleting the command's duplicate build block; compiler tokens and product allocations do
  not change.

- **CR-A9** (review session after A8, 2026-10-12). The analyzer asked Compilation.GetSemanticModel for each tree from
  a SyntaxTreeAction. That private model bound every inspected body again although the build's other analyzers had
  bound it through the driver's shared model: Kimi.Analyzers took 3.32–3.84 s, 9.4–10.4% of csc. A
  SemanticModelAction reuses the shared model. As plan §3.8 requires, KR1 resolves only names that can denote a
  forbidden type or member: global and layer member names, aliases, `var`, `nint`, `nuint`, and the names and members
  (GetMembers, which includes synthesized delegate methods) of denied types and of types declared in denied layers,
  except allowed types. The complete Report is unchanged. Interleaved Release rebuilds: 1.36–1.86 s against
  32.3–33.3 s of csc (4.1–5.7%), csc about 3 s shorter; a single-thread replay with all 467 build analyzers drops
  from 3.1 s to 0.54 s. Rejected: counting only written KR1004 types to skip declaration symbols; CR-A4 counts
  inferred declarations so `var` cannot evade the rule, and 18 occurrences would disappear.

- **CR-A9a** (review session; KR3003 scope by the owner, 2026-10-12). KR1006 counted `x is { } y` on a Koto input
  as a Koto type test because a typeless recursive pattern's matched type is its input type. It tests only null and
  properties, like `x != null`, which was never counted; a pattern tests a Koto type only when it names one. Nine
  MirBuilder rows become stale and Prune removes them. This narrows what the analyzer reports without a configuration,
  baseline or annotation change, so CR-A8's approval comparison does not see it; it is stated for the owner's review.
  The owner extends KR3003 from CR-A0's `var (...)` to the same all-new-local deconstruction written as a tuple of
  declarations, `(var a, T b) = (...)`, which otherwise evades the rule; nested, discarded and existing targets stay
  outside. The source has no such declaration, so the baseline is unchanged.

- **CR-A9b** (review session, 2026-10-12). Verification trusted a successful build without evidence that the analyzer
  ran on the checksummed policy. Verify now also emits KR9000 and verify reads its error log as a complete report, so a
  build without the analyzer, or with other configuration or baseline bytes, fails; Enforce stays silent in ordinary
  and IDE builds. Effective kimi_code_rules options must equal those parsed from the checksummed CodeRules.globalconfig
  (KR9003 otherwise): a globalconfig with a higher level could relax policy outside CR-A8's comparison of that file.
  Kimi no longer discovers .globalconfig files above the project, which could also change built-in severities that
  analyzer options do not expose. Without discovery, Roslyn's targets stop passing GlobalAnalyzerConfigFiles items
  (the first build failed KR9003 on every option), so CodeRules.globalconfig is an EditorConfigFiles item, as the SDK
  passes its own analysis-level global configs. verify-commit resolves its evidence path once, and the approval
  restore disables build servers before its checkout is removed. Rejected: case-insensitive suppression IDs (Roslyn
  4.14 matches IDs case-sensitively; a lowercase ID suppresses nothing) and per-rule baseline anchor validation (a row
  of the wrong kind cannot match and already fails as stale). The trailers of 755adbdd and f839642a predate CR-A8's
  format; pushed history is not rewritten.

## R2b (2026-10-11, main session, as recommended)

- **R2b-D1** `BoundType` stays the Type representation until R7; `TyCtxt` owns interning and `Subst` converts.
- **R2b-D2** The HIR publisher runs lazily, at most once per Binding pass. R7 writes the columns inline and deletes it.
- **R2b-D3** The declaration-table views (`ImplTable`, `VTableDef`, `UniversalRelations`, `EffectBoundTable`,
  `AvailableBounds`) are built by their first reader (R4, R5).
- **R2b-D5** `BoundMatch` and `BoundClosure` keep their names (the plan's PatternPlan and ClosureDef) until R6.
- **R2b-D7** `HirAdjust` = `HirAcquire` + `HirAdaptation`: `@move` and `@copy` are operand Adjusts, `@ref`, `@uniq` and
  `@raw` are `Op = Borrow`, `@follow` is `Deref`, and numeric conversions are `Op = Convert`.
- **R2b-D9** The HIR snapshot is a separate `.hir.txt` per case (`KIMI_SEMANTIC_HIR=1`).
- D4, D6, D8, D10 and D11 set R2b's budget, published views and unit order; their results are in the stage ledger and
  PLAN_HISTORY.

## R3 (2026-10-11, main session as recommended; R3-9 and R3-10 by the owner)

- **R3-1** MIR records (construction `Unsupported_Kd`, validator `InternalInvariant_Kd`) are published only under the
  `mir` pipeline, in the ControlFlow partition.
- **R3-2** The R3-exit `-Pipeline mir` Session is a measurement, classified into `artifacts/verify/mir-diff/r3.tsv`; the
  gate is the plan's R3 gate.
- **R3-3** Tests read analysis and generation through the `Compilation` entry points. `TryPrepare(out var module)` reads
  wait for R5, and `Ownership.Bodies` and control-flow structure reads for the R4 dump goldens, under ratchets.
- **R3-4** R3 budget: Mir 6,600, Binding 350 lines over its R2b-exit size, Core 1,820, the old paths unchanged (D5).
- **R3-5** A built-in form without an intrinsic row gets an interim row mapping it to an existing compiler function or
  legacy helper, each with its R8 disposition.
- **R3-6** `MirDefKey` is keyed by `BindingSymbol` until R7's declaration table.
- **R3-7** `Problem(Code, SourceRef Subject)` stays minimal until R6.
- **R3-8** Default expansion stops at 65,536 MIR statements per expanded body with the Resource code
  `DefaultExpansionLimit_Kd` (IMPL §21.3.5, plan §16-10).
- **R3-9** (owner) An Array's or Dictionary's `capacity` is the temporary `IntrinsicId.Capacity`
  (`Call(Intrinsic(Capacity), [&handle])`). R5 reads the field under the current layout rules; R8 removes it with the
  representation structs. Rejected: deferring to R5 (the same choice later, plus an R3 gate exception); pulling R8's
  representation structs forward.
- **R3-10** (owner) An owning interpolation uses the temporary intrinsics `FormatBuffer` (named `FormatCapacity` until
  U16), `FormatHint` and `FormatFinish`. `FormatBuffer` creates the private buffer, where R5 chooses stack or heap;
  `FormatFinish` consumes the adapter. The estimates take the literal byte count and a TypeToken of the values' Types and
  are computed per instance in R5; the failure check reads each write's own Result. Rejected: internal library
  declarations with three more `CompilerFunctionKind` members (grows the vocabulary R8 shrinks); deferring with an R3
  gate exception. R8 removes all three; with R3-9 the `IntrinsicId` limit returns from 34 to 30.

## R4 (accepted 2026-10-11)

- **R4-M1** Shadow: under `mir` the old path still decides acceptance, Problems and diagnostics. The analyses run once
  per program version after build and validation (`MirBuilder.Build`, then `MirAnalysis.Run`); MIR problems stay
  unpublished on `MirProgram` until R6 unit a; an analysis exception fails the test. Rejected: MIR deciding codes in R4
  (duplicate records and test churn twice).
- **R4-M2** Layout: `Mir/Analysis/` in namespace `Kimi.Compiler.Mir`: `MirAnalysis` (driver), `MirDataflow`,
  `MirMoveData`, `MirInit` (A1), `MirDrops` (A2), `MirLints` (A3), `MirEffects` (A4), `MirBorrows` with `.Regions`,
  `.Conflicts` and `.Origins` (A5), `MirReport` (A6). The first unit creates all of them as stubs, so parallel units never
  edit the driver. The stub clause is superseded by R4-M2a.
- **R4-M3** `MirProblem(Code, Subject, Related, Requirement, Views, Fact)` with pooled fact rows; one `MirReport.Add`
  applies the derived rule (R4-M10). Core's `Problem(Code, Subject)` is unchanged until R6. The fields are superseded by
  R4-M3a.
- **R4-M4** The first unit fixes the compared code set of each analysis: A1 initialization and moves; A3
  `RequireFallthrough`; A4 effect bounds and the effect-limit Unsupported; A5 Loan conflicts, default-argument access,
  Origin relations and `OwnershipStorageLimit_Kd`; every old Ownership-partition `Unsupported_Kd`. Control-flow lints,
  `TransferRequired`, `OwnershipCaseLimit` (a Binding record since R4-M18), old Internal records and capability Unsupported
  records pass through uncompared.
- **R4-M5** Compilations with an incomplete Binding are compared in their own ledger section. A new-only row there is (d)
  only if A6 shows no dependence on an Opaque site's roots; otherwise it is (b).
- **R4-M6** A2 writes a side table and does not rewrite MIR: a verdict per Drop and view class (Keep, Elide, Flag, Open)
  and the flag set and clear points; R5 code generation reads it. Rejected: rewriting MIR in place (verdicts differ per
  view, and insertions break ranges).
- **R4-M7** A3's missing fact is `MirBody.MustNotComplete(block, SourceRef)`, written by the builder and range-checked by
  the validator; it is not a vocabulary member. Rejected: inferring it from Unreachable (SPEC 14.9.2 forbids pruning).
  The row shape is superseded by R4-M7a.
- **R4-M8** The definition-stage capability check (A3b) moves to R5 with the single capability table; the R4 ledger
  excludes capability records.
- **R4-M9** Binding views for R4, each added to `PublishedBindingTypes` with its reason: `TyCtxt.Relate` (the single
  variance walk), `OriginAtom.Of`, `UniversalRelations.For`, `OriginClauses.Of`, `EffectBoundTable.Of`,
  `ImplTable.Witnesses`, `VTableDef.Of` and `TyCtxt.CallableBounds`. Rejected: Mir re-deriving Origins from syntax or
  scopes.
- **R4-M10** A6: a problem rooted at an Opaque site is derived and dropped; an effect caused by one is Unknown with that
  cause, and obligations on it are derived. A problem inside an Inlined scope belongs to the template's own check,
  except Loan conflicts with the caller and effect contributions. A finding rooted at a PreparedInput local maps to
  `DefaultArgument*_Kd`.
- **R4-M11** A1, A4's local facts and A5 run per `MirCases` view; A6 merges by (code, subject, Reason) with the view mask
  as the case fact. Views are never collapsed. The key is R4-M3a's until U11.
- **R4-M12** Library bodies are analyzed in every compilation. The library and the milestones produce no problems,
  except an (a) row whose library fix lands in the same unit.
- **R4-M13** Tests migrate per method kind (body presence, operations, cleanup verdicts, Loans, verification state, R5/R6
  residue). Ratchets: control-flow structure reads at most 1, `Ownership.Bodies` reads at most 110.
- **R4-M15** `OwnershipStorageLimit_Kd` stays the one Resource code, decided by the byte size of A5's Loan × point arena. The
  arena scope is superseded by R4-M15a.
- **R4-M16** The ledger's classification table is tracked (`tests/xUnitTest/TestData/mir-diff/classification.tsv`); raw
  rows, reproducers and ledgers are evidence under `artifacts/verify/mir-diff/`.
- **R4-M2a** (2026-10-12, planning session; design A of two independent U0b designs; supersedes R4-M2's stub clause, its
  layout stays). U0b creates only `MirAnalysis`, the driver whose contract fixes the pass order (A3 per body; per body and
  view A1, A2, A4's local facts; A4 over the program; A5 per view; A6 inside `MirReport`), and `MirReport`; each analysis
  unit creates its own files and adds one field and one pass row at its position. R4-M2's reason no longer holds: unused
  parameters and members are build errors (CR-A7), and AGENTS (2026-10-12) admits no structure without behavior; parallel
  units' driver edits are line-level, resolved at rebase. The three-consecutive-units restructuring of `MirAnalysis.cs` is
  deferred to the R4 exit (stage ledger). Rejected: stubs (unused parameters or fake uses, edited anyway); partial methods
  or a pass interface (declarations without behavior, one call shape forced on different analyses); the driver and record
  landing with the first analysis unit (parallel units would invent the same files).
- **R4-M3a** (2026-10-12, planning session; design B's record taken into design A; supersedes R4-M3's fields).
  `MirProblem(DiagnosticCode Code, SourceRef Subject, ulong Views)`; `MirReport` merges by (Code, Subject), the canonical
  `Problem` key, OR-ing Views into the first problem, until U11 adds the Reason fact and Reason joins the key (R4-M11's
  (code, subject, Reason)); a merge keeps the first problem's facts. Construction records keep their own merge in
  `MirProgram` until R6 unit a folds them into `MirReport` (stage ledger).
  No Requirement: Mir may not name the legacy `OwnershipFailure` (KR1001), and moving that enum into a shared area would
  make old-path vocabulary a shared concept; R6 unit a derives the published requirement from the code and the facts. No
  Related: related sites are facts, and fact rows (pooled, with their own index on the problem) are added by their first
  producer. The ledger fills its requirement column from the old side only. Rejected: design A's (Code, Subject, Related,
  Requirement, Views) keyed by (requirement, code, subject); a Requirement through a moved legacy enum; an `int Fact` field
  before anything produces or reads it.
- **R4-M17** (2026-10-12, planning session; design A of two independent U0b designs) The difference harness.
  `Compilation.BuildMir` builds, validates and analyzes (`MirAnalysis.Run`), once per Binding pass. The Transitional
  internal static event `Compilation.Analyzed`, raised at the end of every `Analyze` on both pipelines when both paths'
  records of the version are final, is the ledger's one observation point: observers only read, no compiler code
  subscribes, and R6 unit a deletes it. The test assembly's fixture `MirDifferenceLedger` subscribes when `KIMI_MIR_DIFF`
  names a directory and compares each mir-pipeline compilation once per Binding pass: the old path's A-owned records, read
  through `LegacyRecords`, against the MIR's construction records and problems, as multisets on (code, subject node by
  reference), never numbering an old node. Raw rows (`event section side analysis code requirement location form body
  sha`) and each source go to that directory; one aggregation (fixture disposal, or `MirDifferenceTest.RequestedLedger`
  with `KIMI_MIR_LEDGER`) pairs an unmatched old and new row of one code in one body into a `location` difference, counts
  the rest `missing` or `new`, adds an `acceptance` difference when a comparison's A-owned records are on one side only,
  and classifies signatures (section, kind, analysis, code, requirement, form) by the tracked table into `ledger.tsv` and
  `summary.md` (P9: the unclassified signatures). The compared set (R4-M4) is a table in the fixture of requirements, each
  with every code it reports and the owning analysis: ownership failures except TransferRequired and Internal
  (A1, A4, A5; U for the Unsupported gates; - for the expansion limit), ControlFlow `RequireFallthrough_Kd` (A3),
  `Binding.VirtualEffectBound` (A4) and `Binding.OriginRelation` (A5); a MIR problem takes its code's analysis, a MIR
  construction record is B. `Binding.IncompatibleImplementation` and the effect-limit `Binding.Unsupported` also carry
  non-effect records and are compared only from U6, which names how the effect subset is identified. The construction
  records keep their own merge until R6 unit a folds them into `MirReport` (R4-M3a). Tier 1:
  `MirDifferenceTest.RequestedCorpus` checks the diagnostic corpus and every `kimi` block of docs/spec through the check
  entry; tier 2: the Functional suite under mir with the ledger; the Allocation tests run under mir without it, since the
  observer allocates. Rejected: the driver called by `MirBuilder` (construction would own the P7–P9 order); analyzing
  inside `Analyze` (dumps and tests calling `BuildMir` would see unanalyzed programs); a hook inside `MirAnalysis` (the old
  analysis runs after `BuildMir`); capture in test helpers (571 `Analyze` calls; the check entry's compilations unreachable);
  a per-compilation sink (the same static, or 277+ `CreateForTest` edits that still miss the check entry);
  compiler-written evidence (product file output by an environment variable); a DiagnosticListener (more code, same role);
  a ModuleInitializer with ProcessExit (no lifecycle guarantee); a Python aggregator (an untracked second
  implementation); design B's content-hash fast path (the Functional/Allocation split is simpler), its positional split
  of `Compilation.Problems` (order-dependent; computing SourceRefs numbers unnumbered nodes under the observer) and its
  shipping only the hook in U0b (two parallel analysis units would invent the driver and record).
- **R4-M18** (2026-10-12, main session; two independent designs: A chosen with B's table-free report, publication rule and
  dependent adaptations; revised after the independent review for SPEC 23.3.6) The Semantics case bound (`PairBinder.CaseBound`,
  64; SPEC 8.10, 23.3.6.1 Resource) has one owner. `Binding.CaseLimits` decides it once per definition and pass, after the
  signature and before the parts are bound: a function or Property whose defaults, base initializer, body, initializer or accessor
  bodies would be verified over `PairBinder.CaseProduct` > 64 cases fails with CaseLimit (only as its first failure) and reports
  `OwnershipCaseLimit_Kd` once, a function at its signature, a Property at its declaration and a synthesized constructor at its
  structure, the count recomputed from its binders at report time (no table). Its parts are still bound, so their independent
  records stay visible (SPEC 23.3.6.4), but they are not verified per case: a case adaptation inside it is a dependent of the
  CaseLimit failure (no record, no row enumeration), and the HIR publishes a definition's parts (bodies and defaults) only when
  Binding resolved the definition, a rule of every unresolved function, so no MIR body, Closure or default template of it exists
  and a call omitting its default takes an Opaque value. Its signature stays valid (`KeepsSignature`), so its calls are checked
  normally. A definition without parts is never bounded; Closures, templates and accessors share their definition's binders, so an
  adaptation inside a definition within the bound has at most 64 rows and no cap of its own. `MirValidator` rejects a body with
  more views (`Binder`, `InternalInvariant_Kd`) and the analyses trust it (R4-M11). R4-M4's pass-through of `OwnershipCaseLimit` no
  longer applies; the code is a Binding record. Deleted: the old analysis's check, `OwnershipFailure.CaseLimit` and
  `OwnershipAnalysis.CaseBound`, the `Ownership.CaseLimit` requirement, and the adaptation product check, 64-row cap and cause map.
  Rejected: a driver skip or assertion (a consumer deciding a producer's contract); leaving the parts unbound (design A's first
  form: it hides their independent errors, SPEC 23.3.6); MIR deciding the bound; a non-form failure (order-dependent caller
  records); a template guard on an unset Op (Binding.Check leaves no node unvisited before publication); a Koto-keyed count table.
  Diagnostic sets change only in already-rejected programs; acceptance does not change.
- **R4-M19** (owner O1, 2026-10-12; two designs compared, the first taken with two parts of the second). An explicit case
  adaptation (SPEC 8.9, 13.5.3), whose operation each Semantics case selects, is the one case-dependent rvalue
  `MirRvalueKind.Adapt` (KR1003: 9 → 10). Operand 0 is the by-value acquisition (a written Place's Copy, a temporary's Move);
  operand 1 is the TypeToken of Binding's source Type, since `ExplicitAdaptationPlan.Select` decides by interned identity and
  the operand Place's MIR Type may differ from Binding's in Origins. Place is the payload one implicit layer below the operand
  (`Follow(P, 1)`), so object borrows and borrowing upcasts borrow the whole object, never a base; Type is Binding's target;
  Op 1 is the Semantics shorthand. `MirCases.Adapt` resolves it per view through `TyCtxt.Adaptation` (Binding's `Select`,
  published like `CaseType`) to an ordinary rvalue: Identity and object creation a `Use` of the operand (a creation also
  allocates), Pointer and an upcast keeping a non-exclusive Semantics a Proven `Cast`, any other operation a `Ref`
  (`MirTypes.RefKind`) of the payload for an object reference, else of the operand's Place; analyses never see an Adapt
  under a view. Its descriptor row is CaseDependent with Allocation. `MirUses.ForEach(MirCases, …)` shares the walk of the
  body overload and reports the resolved form, a creation with its allocation; without a view an Adapt reports its operands,
  the acquired Place and the payload as CaseDependent (as PairAcquire's row does), and the allocation. `MirValidator(TyCtxt)` checks the operands' shape and the token against
  the operand, and under every view that an operation is selected and that a borrow or an address takes a Place of its
  referent Type. The builder reads the plan from its row and adds `ExplicitAdaptationPlan` only to mir_build's allow list.
  Rejected: the Unsupported record until R5 (owner); an encoding without a new member (PairLayer and PairAcquire cannot
  allocate, form a reference or upcast); an operand kind (a creation allocates, a borrow yields a Ref); per-case expansion or
  statement masks (plan §5.6.1); the source taken from the operand Place's MIR Type; a MirBody plan table (a table, adder,
  reset, range rules and folding); analyses querying MirCases per Adapt (every analysis would special-case it); moving
  `Select` into TyCtxt (it edits frozen call sites, D5); a MIR copy of `Select` modulo Origins (a second copy). Not temporary.
- **R4-M7a** (2026-10-12, main session; supersedes R4-M7's row shape): A3's fact is `MirBody.MustNotComplete`, rows
  `(Entry, End, Subject)`: a require's failure body, entered at Entry, whose end End (an Unreachable without a check
  target) no runtime path from Entry may reach; Subject the failure body. The builder adds a row where it finishes a
  require; the validator checks both blocks and End's terminator (`TargetRange`); the dump prints one line per row; a
  default copy has no rows (A3 judges the template once, SPEC 7.2.3). A3 (`MirLints`) walks runtime edges only with
  `MirReach`, the one reachability walk (the validator walks all edges), seeding each row's Entry from the last row to the
  first, so each verdict is exact and each block is visited once per body. Reason R4-M7's `(block, SourceRef)` no longer
  holds: a walk from the body's entry cannot assume entry to a failure body that only the checks reach (SPEC 14.11.2,
  14.10.3). Still rejected: inferring the end from Unreachable. Derived A3 problems (an end reached only through an Opaque
  site whose completion is undecided: an unselected `#switch`, a Binding failure whose Type may be Never) need a
  published fact and are left to U11/R6; rejected: a body-level `#switch` rule (syntax ancestry, SPEC 23.3.6.4; a KR1006
  row) and deriving every path through an Opaque site (drops written recovered failure bodies, DIAGNOSTICS §4.3).
  Rejected also: per-row walks (quadratic), U1's dataflow framework, copying rows into copies. No temporary structure.
- **R4-M20** (2026-10-12, planning session; design A of two independent U1 designs, with design B's three lanes) MIR dataflow.
  `MirDataflow` solves forward bit-set problems over flow heads: a block whose only edge is a runtime edge into a block with no
  other predecessor that is not the entry continues into it, so a chain of blocks shares one state, stored only at its head. One
  pooled arena of heads × stored words, word-major (the lanes of 64 paths per word group); only flow paths are stored, and
  chain-local paths (a root whose events all lie in one chain from its StorageLive, such as a statement's temporaries; a
  StorageDead in another chain is no event, since it kills whatever state arrives) live in the scratch. Reverse postorder once
  per body from the one traversal owner `MirReach`; a ring worklist seeded with every head in reverse postorder, with queued
  bits; union join. P1 follows runtime edges from the entry; P2 follows runtime and CheckTarget edges and writes only heads P1
  did not reach, P1's states frozen (a P1 head passes its state once along its CheckTarget); `Replay` gives each reached chain
  its converged state for the checks and later readers (A2). The backward direction (liveness, all edges) arrives with U7, its
  first reader. The arena is dense: linear in blocks at fixed flow paths and in flow paths at fixed blocks (V16's reading),
  quadratic where long-lived locals cross many heads (`MirBuildTest.BodiesScaleLinearly` prints its words). Rejected: a state
  per block (design B; O(blocks × paths/64) arena and time when calls split blocks while locals grow, quadratic in V16's
  straight shape and in `MirBuildTest.BodiesScaleLinearly`, which measures `BuildMir` and so the analysis); porting the old
  solver (OwnershipBody's five lanes, two slots per path, N7); one pass over all edges (SPEC 14.10.3 ¶4); gen/kill block
  summaries reused across views (a second arena; reverse postorder converges acyclic code in one sweep; views above one are
  rare); sparse per-path or chunked sets (long-lived locals stay quadratic, more code); a second or third block walk beside
  `MirReach`.
- **R4-M21** (2026-10-12, planning session; design A with design B's path keys, lanes and transparent PairLayer) Move paths and
  A1's state. `MirMoveData` keys a child path by (parent path, projection kind, argument) in a pooled table: Field, Base (one
  depth per step), ConstIndex and Downcast steps form paths; a PairLayer step is transparent (the Place itself in the owner
  case; a case-dependent access through a PairLayer that the view makes a reference ends at its outermost such step, as at a
  Deref, SPEC 13.5.5.1); Deref and Index end a path (an access through them is an access of the prefix's path; a Move through
  them has no Move Path, except through a raw pointer: an untracked Take, SPEC 5.2.3). Paths are numbered in pre-order without a
  stack, so a subtree is a range; a constructor's `_0` (`AdtDef.IsConstructor`) has a child per Field and its base. `MirInit`
  (A1) keeps three lanes, joined by union: MaybeUninit, MaybeMoved, EverInit (MaybeInit joins with A2, U5, its first reader). A
  definition needs each strict ancestor's own bit (its construction fact) initialized, reports ReassignedLet when a path
  admitting one placement was ever initialized in the binding lifetime (the paths of a `let` Var, and of a constructor's Field
  that `AdtDef.PlacedOnce` names: a `let` Field or one with a custom `set`, SPEC 11.3.1), then initializes the subtree; a Move
  uninitializes and marks it moved, a Drop uninitializes it, StorageLive and StorageDead start a fresh lifetime; a use needs the
  whole subtree, own bits included, initialized on every incoming path (a complete value has both the construction fact and
  current completeness, SPEC 15.1.2, 15.1.5; MovedPlace where it may have been moved, else UninitializedPlace). Every root is
  tracked (A2 reads temporaries' states), but these state problems are recorded only for the paths of Var, Arg and Return roots:
  a compiler temporary is never a Place of the program, and in check-only code a temporary that an expression whose value only
  the checks reach never wrote is no uninitialized Place. A Move without a Move Path is StaticMovePathRequired whatever its root
  (an operation check, SPEC 15.1.3: an operation that requires a Move Path is rejected when none exists; 15.1.5: a Place a
  function publishes never offers Take) and changes no state. A whole use of the return local is a delivery, checked only where
  the runtime edges reach it (SPEC 14.10.3): a constructor's object is complete at each exit it requests (SPEC 6.2.3.5), where
  the builder reads `_0` (FakeRead) once its body completes and at each return, before the Scope Exit whose defers cannot supply
  a Field; a Return checks nothing (every path into a constructor's Return passes an exit request, and an ordinary function's
  `_0` at its Return would show only a builder defect). A1 reads each view's uses through `MirUses.ForEach(MirCases, …)`, which
  resolves an Adapt per view (R4-M19); a body without a case-dependent access (`MirAccess.CaseDependent`: a PairLayer step, a
  PairAcquire, an Adapt) is analyzed once, for every view. PreparedInput (DefaultArgumentMove), Opaque-root and Inlined rules
  stay MirReport's (R4-M10, U11). Rejected: interning analysis projections as keys (an analysis must not intern, and a
  constructor's unplaced Fields have no chain); two slots per path and recursive child walks (the old path); covered paths whose
  own bit is left out of a use (design A: a whole use then skips the construction fact) and PairLayer paths (design A: the
  covered-path, layer-step and owner-view parent rules they need); a MirBody constructor flag or a new MirLocalRole member
  (vocabulary for a declaration fact Binding publishes); a constructor recognized by MIR shape (an empty struct's constructor
  writes nothing); MoveData per view (only PairLayer ends differ, resolved per access); collapsing views (R4-M11);
  DefaultArgumentMove decided in A1 (R4-M10's rule); reporting on temporaries (false problems in check-only code,
  `artifacts/verify/mir-diff/r4-u1-exp-temps`); a completeness check at the Return (after the defers, which cannot place a
  Field, and a second record of an early return's missing Field); a vocabulary member or a role for the exit request (a FakeRead
  is the analyses' read). The first-placement rule, the exit request, the reporting rule, the whole-subtree use and the delivery
  rule were added after U1's ledger and its independent review (main session, 2026-10-12).
- **R4-M15a** (2026-10-12, planning session; supersedes R4-M15's "A5's arena" scope) `OwnershipStorageLimit_Kd` stays the one
  Resource code; it is decided at the one place that sizes analysis arenas, `MirDataflow.Prepare` (A1's state arena now, A5's
  Loan × point arena in U7), by byte size against the old path's limit value, 64 MiB (IMPL 21.3.5's text changes in R6). An
  over-limit body records the code once at the body's subject and A1 does not analyze it; the driver owns that skip once A2 and
  A5 read the arena (U5, U7). Reason: the dense arena is O(heads × paths/64), and a body bounded only by the 65,536-statement
  expansion limit could otherwise exhaust memory.
- **Owner decisions, only when triggered:** O1 a MIR vocabulary member or `IntrinsicId`; O2 a total cap above 142,000;
  O3 an acceptance change outside plan Appendix B (C1–C6 and X1–X3 with a SPEC clause; T1–T4 belong to R7), any rejection
  of a milestone or library program, or a row reopening a SETTLED entry; O4 a general SPEC rule left Unsupported at the R4
  exit; O5 the unclassified count failing to fall in two sessions in a row (plan §18 P9).
