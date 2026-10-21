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

## Build artifacts

- **B1 Library build records** (2026-10-15, planning session; owner decision in IMPL §20.3.2). `Project.Build` selects product checks or Application emission by OutputKind. `NativeToolchain` owns invalidation and atomic record publication; Library success is `status: checked`, with input and selected compilation metadata, and invokes no native tools. All artifact-producing calls retain the single Windows x64 target rule. Library manifests are rejected because they cannot establish source-check success. Rejected: record writing in Project (duplicates publication ownership), and a new record component (moves an already shared responsibility without removing duplication). No compiler phase, IR or generation contract changes.

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
    records and was revised after review under SPEC 23.1); "the local patch and why it is rejected" presupposed
    rejecting correct owner-local fixes; shape-based sharing and generic tables can merge different contracts; path
    enumeration did not converge where a Type-level fix did (P26/G75).
  - Rejected: keeping counts as the first criterion; deleting the table-row rule (it is the target structure,
    COMPILER_ARCHITECTURE "Adding a feature"); appending a separate rule list instead of replacing the affected rows.
  - Not reopened: decisions of landed units. Decisions of units not yet on `dev` are re-checked only where the
    coherence comparison would change the choice.
- **Q5 No size caps; consistent, short and fast code; review weight** (2026-10-16, owner; AGENTS).
  - Adopted: code size has no caps. Removed: the area token caps, stage ceilings, final target and LLVM-template line cap
    (plan §10.4 and §12.2, Q1's size budget, CR-A1's conversion, R4-M25), unit forecasts with their 20% overrun rule and
    R4-M30's 200-token Binding allowance. Each compiler commit still states its net size change per area, and stage exits
    record area sizes. Structure limits, closed vocabularies and ratchets are unchanged.
  - Adopted: code is consistent, short and fast, in that order, replacing Q4's "coherence, then net size, then
    compilation speed" with the same order; "fast" covers generated code as well as compilation.
  - Adopted: two independent designs, a design review and a final review only for a change to a shared representation
    or across phases; any other change gets one review before completion, covering its design.
  - Reason: the caps, forecasts and per-unit allowances added owner gates without improving the code, and every
    non-local change paying two reviews slowed small units (R4-M33).
- **Q6 Lighter review forms and short commit records** (2026-10-17, owner; AGENTS "Review weight" and "Verification and
  Delivery").
  - Adopted for all work: (1) a change whose contract an owner-approved design recorded here already fixes gets one design
    and one review, and a departure, including a new representation, component or vocabulary entry, takes the full
    process; (2) the units of one group such a design names may share one review in place of their own, merging into dev or main
    only after it passes; (3) documentation that only records decided or verified facts (PLAN, STATUS, CODEMAP,
    PLAN_HISTORY, VERIFICATION evidence and tooling-matched procedures) needs no independent review. Every commit message
    stays within about 2,000 characters, including Q5's net size change.
  - Kept: R4's exit adds a final end-to-end review of A5 and A6 (R4-M35).
  - Supersedes: R4-M35's OD6 scope ("review and records as plan §9.3", A5 only), which the owner broadened, and Q1's
    deferral of lighter commit records. Q5's review rule stands, with these forms added.
  - Rejected: an A5-only exception (the owner judged the three forms independent of A5); applying them without
    conditions, since a shared review must not let unreviewed code reach dev, and SPEC, IMPL, AGENTS, DECISIONS, SETTLED,
    STYLE and proposals are authority, not records.
- **Q7 A mechanical one-argument propagation is not a change for the restructuring rule** (2026-10-19, owner; AGENTS
  "Restructure a file changed by three consecutive units before its next change").
  - Adopted: a unit that only adds one argument to a signature and passes it through, adding no branch, state or
    responsibility to the file, does not count as one of the three changes. Applied first to `MirInit.cs` and
    `MirBorrows.Activations.cs`, whose third change, 1c-1, only passed a problem's requirement (R4-M67): 1c-5 changes
    `.Activations.cs` and 1c-6 changes `MirInit.cs` without a split, and those changes count.
  - Reason: the rule catches a file that accumulates responsibilities; a propagated argument adds none, and a split without
    a responsibility boundary fails review (AGENTS).
  - Rejected: counting it, which forces a split or a ruling for files whose responsibility did not change.

## Code rules (2026-10-12)

CR-R1 supersedes only the analyzer enforcement, Report and approval requirements in CR-A0–CR-A9b below.
Underlying compiler decisions (including CR-A3a's HirTables ownership), token methodology, structure, size
and migration obligations remain in force; no numeric cap changes are authorized by retirement.

- **CR-R1 Analyzer workflow retirement** (2026-10-15, owner; independently reviewed).
  - Cause: `2e341155` deleted the analyzer, policy/baseline, its tests and CompilerSizeBudgetTest, but Verify
    still required KR9000, injected the deleted test, and commit association demanded retired Reports.
    A warning-free whole-solution build therefore failed its verification wrapper before tests ran.
  - Decision: remove all consumers of the retired producer, including code-rules scripts/tests and dead
    project properties. ArchitectureRulesTest is the sole common Unit test. Keep warning-as-error builds,
    actual-assembly selector/nonzero checks, the full Session suite, native selections, stable input
    manifests and exact committed-input association. Existing attributes remain source-only review records.
  - Rejected: restore ~8,495 deleted lines against the owner's removal direction; skip only the marker,
    leaving dead Report/approval paths and a nonexistent default test; conditionally bypass missing tools.
    Removing the retired workflow deletes roughly 900 script/test lines and adds no modes or fallback.
  - The owner explicitly authorized aligning verification and related rules with the deletion. Existing
    architectural limits stay unchanged and are reviewed; no automated analyzer/size coverage is claimed.
- **CR-R2 Constructors are exempt from the parameter-count limit** (2026-10-22, owner, direct, verbatim:
  「コンストラクタの引数上限は撤廃します。6個は、コンストラクタ以外に適用します。」).
  - Reading: the count of 6 parameters no longer applies to a constructor; it applies to every other member. The sub-limits (at most
    2 optional, 1 `out` and 1 `bool`) are unchanged (confirmed by the owner, 2026-10-22, to R4, relayed by R4:
    「「コンストラクタの引数上限の撤廃」について、推奨の内容でOKです。」; confirmed to BU directly the same day: 「OKです。」).
  - Reason (the owner's, as R4 relayed it): constructors tend to take many parameters, and many parameters there rarely lead to an
    inefficient structure. R4's recommendation, which the owner accepted: a constructor that wires many components is watched by the
    dependency rules and by review, which the count no longer signals.
  - Trigger: `BoundType`'s constructor and `TyCtxt.Intern` were both at 10 parameters when G160 added the raw Origin, which keeps one
    `origin` parameter at `Intern`, a method that stays limited: being over the limit, it may not grow.


- **CR-A0** (owner). Begin the adopted Code Rules Analyzer A0–A8 before R4. KR3003 covers only
  all-new-local `var (...)` deconstruction declarations with computed tuple elements. Existing-variable assignments
  are excluded: even uncaptured, syntactically independent locals change observably when a later RHS throws or reads
  a ref alias. Rejected: extending capture/dependency checks into an effect/alias proof; it adds machinery for a density
  rule. Compare diagnostics, semantic/HIR/MIR snapshots and generated IR, not DLL bytes (commit metadata differs).
  Intake changes architecture policy only; A1–A8 introduce implementation and enforcement.

- **CR-A1** (planning session, independently reviewed; its caps superseded by Q5). CompilerSizeBudgetTest owns one file scan for all area and
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
  Mark all direct and indirect Koto semantic forwarding properties (R7 since R5-M9) and the pipeline switch declarations for R6;
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
  `DefaultExpansionLimit_Kd` (IMPL §21.3.2, plan §16-10).
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
  records pass through uncompared. Its role in the R4 exit is superseded by R4-M35.
- **R4-M5** Compilations with an incomplete Binding are compared in their own ledger section. A new-only row there is (d)
  only if A6 shows no dependence on an Opaque site's roots; otherwise it is (b). That rule is superseded by R4-M35.
- **R4-M6** A2 writes a side table and does not rewrite MIR: a verdict per Drop and view class (Keep, Elide, Flag, Open)
  and the flag set and clear points; R5 code generation reads it. Rejected: rewriting MIR in place (verdicts differ per
  view, and insertions break ranges). Refined by R4-M6a.
- **R4-M7** A3's missing fact is `MirBody.MustNotComplete(block, SourceRef)`, written by the builder and range-checked by
  the validator; it is not a vocabulary member. Rejected: inferring it from Unreachable (SPEC 14.2.2 forbids pruning).
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
  `DefaultArgument*_Kd`. Its placement of that mapping in A6 is superseded by R4-M69 (the Move code is A1's) and R4-M72 (the
  Access and Borrow codes are A5-F9's), and its Inlined rule amended by R4-M69 and superseded by R4-M90 (for A5 by R4-M97; A1 and A2
  at 2g-6).
- **R4-M11** A1, A4's local facts and A5 run per `MirCases` view; A6 merges by (code, subject, Reason) with the view mask
  as the case fact. Views are never collapsed. The key is R4-M3a's until U11. R4-M71 keys `MirReport` by (code, subject, body) and
  places the merge across bodies at publication.
- **R4-M12** Library bodies are analyzed in every compilation. The library and the milestones produce no problems,
  except an (a) row whose library fix lands in the same unit. Suspended until the joint core lands (R4-M35).
- **R4-M13** Tests migrate per method kind (body presence, operations, cleanup verdicts, Loans, verification state, R5/R6
  residue). Ratchets: control-flow structure reads at most 1, `Ownership.Bodies` reads at most 110. The numbers are superseded
  by R4-M13a.
- **R4-M15** `OwnershipStorageLimit_Kd` stays the one Resource code, decided by the byte size of A5's Loan × point arena. The
  arena scope is superseded by R4-M15a.
- **R4-M16** The ledger's classification table is tracked (`tests/xUnitTest/TestData/mir-diff/classification.tsv`); raw
  rows, reproducers and ledgers are evidence under `artifacts/verify/mir-diff/`. R4-M35 adds a SPEC-clause column for (d) rows,
  which R4-M46 refines.
- **R4-M2a** (2026-10-12, planning session; design A of two independent U0b designs; supersedes R4-M2's stub clause, its
  layout stays). U0b creates only `MirAnalysis`, the driver whose contract fixes the pass order (A3 per body; per body and
  view A1, A2, A4's local facts; A4 over the program; A5 per view; A6 inside `MirReport`), and `MirReport`; each analysis
  unit creates its own files and adds one field and one pass row at its position. R4-M2's reason no longer holds: unused
  parameters and members are build errors (CR-A7), and AGENTS (2026-10-12) admits no structure without behavior; parallel
  units' driver edits are line-level, resolved at rebase. The three-consecutive-units restructuring of `MirAnalysis.cs` is
  deferred to the R4 exit (stage ledger). Rejected: stubs (unused parameters or fake uses, edited anyway); partial methods
  or a pass interface (declarations without behavior, one call shape forced on different analyses); the driver and record
  landing with the first analysis unit (parallel units would invent the same files). R4-M35 moves the restructuring to before the R5
  split.
- **R4-M3a** (2026-10-12, planning session; design B's record taken into design A; supersedes R4-M3's fields; its fields amended by
  R4-M67: the requirement each recording rule names, and by R4-M71: the body; its key superseded by R4-M71: (code, subject, body)).
  `MirProblem(DiagnosticCode Code, SourceRef Subject, ulong Views)`; `MirReport` merges by (Code, Subject), the canonical
  `Problem` key, OR-ing Views into the first problem, until U11 adds the Reason fact and Reason joins the key (R4-M11's
  (code, subject, Reason)); a merge keeps the first problem's facts. Construction records keep their own merge in
  `MirProgram` until R6 unit a folds them into `MirReport` (stage ledger).
  No Requirement: Mir may not name the legacy `OwnershipFailure` (KR1001), and moving that enum into a shared area would
  make old-path vocabulary a shared concept (this naming clause is superseded by R5-M24: Diagnostics owns the kinds as
  `OwnershipRequirement`); R6 unit a derives the published requirement from the code and the facts (this
  derivation clause is superseded by R5-M23: each recording rule names the requirement; revised ask A-5). No
  Related: related sites are facts, and fact rows (pooled, with their own index on the problem) are added by their first
  producer. The ledger fills its requirement column from the old side only (superseded by R5-M56: a MIR problem's row names
  its own requirement). Rejected: design A's (Code, Subject, Related, Requirement, Views) keyed by (requirement, code,
  subject); a Requirement through a moved legacy enum; an `int Fact` field before anything produces or reads it.
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
  and classifies signatures (section, kind, analysis, code, requirement, form; R5-M56 adds `side` after `kind`) by the
  tracked table into `ledger.tsv` and `summary.md` (P9: the unclassified signatures). The compared set (R4-M4) is a table in
  the fixture of requirements, each with every code it reports and the owning analysis: ownership failures except
  TransferRequired and Internal (A1, A4, A5; U for the Unsupported gates; - for the expansion limit), ControlFlow
  `RequireFallthrough_Kd` (A3), `Binding.VirtualEffectBound` (A4) and `Binding.OriginRelation` (A5); a MIR problem takes its
  code's analysis (superseded by R5-M56: its pair's analysis in the compared set), a MIR construction record is B.
  `Binding.IncompatibleImplementation` and the effect-limit `Binding.Unsupported` also carry non-effect records and are
  compared only from U6, which names how the effect subset is identified. The construction
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
  shipping only the hook in U0b (two parallel analysis units would invent the driver and record). R4-M35 ends its role as an exit
  gate, and tier 1 gains expected codes for the SPEC examples.
- **R4-M18** (2026-10-12, main session; two independent designs: A chosen with B's table-free report, publication rule and
  dependent adaptations; revised after the independent review for SPEC 23.1) The Semantics case bound (`PairBinder.CaseBound`,
  64; SPEC 8.15, 23.1.1 Resource) has one owner. `Binding.CaseLimits` decides it once per definition and pass, after the
  signature and before the parts are bound: a function or Property whose defaults, base initializer, body, initializer or accessor
  bodies would be verified over `PairBinder.CaseProduct` > 64 cases fails with CaseLimit (only as its first failure) and reports
  `OwnershipCaseLimit_Kd` once, a function at its signature, a Property at its declaration and a synthesized constructor at its
  structure, the count recomputed from its binders at report time (no table). Its parts are still bound, so their independent
  records stay visible (SPEC 23.1.2), but they are not verified per case: a case adaptation inside it is a dependent of the
  CaseLimit failure (no record, no row enumeration), and the HIR publishes a definition's parts (bodies and defaults) only when
  Binding resolved the definition, a rule of every unresolved function, so no MIR body, Closure or default template of it exists
  and a call omitting its default takes an Opaque value. Its signature stays valid (`KeepsSignature`), so its calls are checked
  normally. A definition without parts is never bounded; Closures, templates and accessors share their definition's binders, so an
  adaptation inside a definition within the bound has at most 64 rows and no cap of its own. `MirValidator` rejects a body with
  more views (`Binder`, `InternalInvariant_Kd`) and the analyses trust it (R4-M11). R4-M4's pass-through of `OwnershipCaseLimit` no
  longer applies; the code is a Binding record. Deleted: the old analysis's check, `OwnershipFailure.CaseLimit` and
  `OwnershipAnalysis.CaseBound`, the `Ownership.CaseLimit` requirement, and the adaptation product check, 64-row cap and cause map.
  Rejected: a driver skip or assertion (a consumer deciding a producer's contract); leaving the parts unbound (design A's first
  form: it hides their independent errors, SPEC 23.1); MIR deciding the bound; a non-form failure (order-dependent caller
  records); a template guard on an unset Op (Binding.Check leaves no node unvisited before publication); a Koto-keyed count table.
  Diagnostic sets change only in already-rejected programs; acceptance does not change.
- **R4-M45** (2026-10-17, tooling session; plan §8 of the Complete Polonius A5 Plan, R4-M35; amends R4-M17's MIR side) The
  SPEC-example oracle, which gives R4-M17's tier 1 its expected codes as an always-on theory beside `RequestedCorpus`.
  `tests/diagnostics/spec-cases.json` gives SPEC examples the Error records the SPEC gives them: the code DIAGNOSTICS names, else the
  catalog entry whose message states the rule, else a code with a written basis. A row names its block by section number and an
  anchor that one `kimi` block of the section contains, never by line; a prelude supplies what the example assumes (an indent places
  a body fragment under the prelude's header). Each variant (the block as written, or one commented line uncommented) has its records
  (a dependent one, whose report SPEC 23.1.2 leaves implementation-defined, may be absent), its decider (the analysis that decides its
  verdict: Binding, A1–A4 or an A5 family of plan §6.1), and the old path's or the MIR's records where they differ, with a class and
  the SPEC clause (plan §9.2); every error verdict of the block needs a record, a variant or a written omission, so moved lines keep a
  row and a changed block, anchor or verdict fails it. `VerifiedSpecExampleTest` checks every variant on the old path until R6-check
  and on the MIR once its decider is `Landed` (Binding, A1, A3, A4; each rule unit adds its family). The sides come from
  `MirDifferenceLedger.Sides`, the ledger's one split of a compilation's records: Binding's, which both paths publish; the old path's
  A-owned ones (R4-M4); the old analyses' others, which pass through; and the MIR's records, problems and validator findings, which the
  mir pipeline publishes as `InternalInvariant_Kd` (the ledger's MIR side now includes them). Rejected: marker comments in the SPEC (an
  explicit SPEC revision); block names by line (SPEC revisions move lines); tier 1 alone (it compares the paths, with no expectation);
  a second record reader beside the ledger's. The number follows the range the planning session assigned to this session.
- **R4-M46** (2026-10-17, tooling session; plan §9.2, R4-M35; refines R4-M16's row shape) The classification table names the SPEC
  sections that decide each row: a `clause` column after `category`, space-separated section numbers of the SPEC's numbered headings
  (`DiagnosticCorpus.SpecSections`), which `ledger.tsv` repeats for its signatures; references keep plan and DEC references only.
  An (a), (c) or (d) row needs a clause, since a row of plan §8's blocking kinds passes the R4 exit only as (a), (c) or (d) with a
  SPEC clause; a (b) or (e) row, which a repair unit resolves, may write `-` where no SPEC rule decides it (a primary location).
  R5-M56 (Q17, 2026-10-19) adds a `side` column directly after `kind`, the path whose records a difference reports: `old`, `mir`,
  `both` or `*`, matched by equality or `*` (`mir` does not match `both`), which `ledger.tsv` repeats.
  Rejected: clauses inside `reference` (unchecked, mixed with plan and DEC references); a second table keyed by row id (two tables
  for one row).
- **R4-M47** (2026-10-17, tooling session; plan §8, R4-M35; replaces PLAN10 §18 P9 in `summary.md`) The ledger's aggregation reports
  plan §8's metrics from the data the test assembly holds: the families landed on the MIR (`VerifiedSpecExampleTest.Landed`), the
  SPEC-example variants and those checked on the MIR (R4-M45; they stand in for "rule tests passing", whose pass or fail is the run's),
  and the open blocking rows, beside the unclassified and open (b) counts; `ledger.tsv` gains a blocking count per signature. A6's
  completion is listed in `Landed` when it lands, though A6 decides no SPEC example's verdict (it is no `Families` decider). A
  difference blocks the R4 exit when it is unsound: in a Binding-complete compilation, a missing row whose body keeps no unpaired MIR
  record (with one, it is a code choice; an acceptance difference only repeats its missing rows); when it lies in a library body
  (`LegacyRecords.Library`, analyzed in every compilation, R4-M12); or when it lies in a Binding-complete milestone program (a saved
  source holding a `tests/milestones` program's whole text). It is open unless its signature is classified (a), (c) or (d), with its
  clause (R4-M46). A Binding-incomplete compilation never blocks except in the library: its old records may rest on Binding's failures
  (R4-M5, R4-M10) and Binding rejects the program on both paths (unverified for its 259 missing rows at the step-0 base). Rules expressed
  and precision losses are counted from the step-0 audit tables and plan §10 OD13 (design A3 §9), which are draft records, not tracked
  test inputs (Verify's manifest excludes `draft/`). Rejected: blocking by kind alone (code and location choices would block); a
  milestone flag in the raw row (a second R4-M17 row shape); a tracked metrics table.
- **R4-M19** (owner O1, 2026-10-12; two designs compared, the first taken with two parts of the second). An explicit case
  adaptation (SPEC 8.14, 13.5.3), whose operation each Semantics case selects, is the one case-dependent rvalue
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
  holds: a walk from the body's entry cannot assume entry to a failure body that only the checks reach (SPEC 14.11,
  14.10.3). Still rejected: inferring the end from Unreachable. Derived A3 problems (an end reached only through an Opaque
  site whose completion is undecided: an unselected `#switch`, a Binding failure whose Type may be Never) need a
  published fact and are left to U11/R6; rejected: a body-level `#switch` rule (syntax ancestry, SPEC 23.1.2; a KR1006
  row) and deriving every path through an Opaque site (drops written recovered failure bodies, DIAGNOSTICS §4.3).
  Rejected also: per-row walks (quadratic), U1's dataflow framework, copying rows into copies. No temporary structure.
- **R4-M20** (2026-10-12, planning session; design A of two independent U1 designs, with design B's three lanes; its union join and
  backward clause superseded by R4-M34, its ring worklist by R4-M65) MIR dataflow.
  `MirDataflow` solves forward bit-set problems over flow heads: a block whose only edge is a runtime edge into a block with no
  other predecessor that is not the entry continues into it, so a chain of blocks shares one state, stored only at its head. One
  pooled arena of heads × stored words, word-major (the lanes of 64 paths per word group); only flow paths are stored, and
  chain-local paths (a root whose events all lie in one chain from its StorageLive, such as a statement's temporaries; a
  StorageDead in another chain is no event, since it kills whatever state arrives) live in the scratch. Reverse postorder once
  per body from the one traversal owner `MirReach`; a ring worklist seeded with every head in reverse postorder, with queued
  bits; union join. P1 follows runtime edges from the entry; P2 follows runtime and CheckTarget edges and writes only heads P1
  did not reach, P1's states frozen (a P1 head passes its state once along its CheckTarget); `Replay` gives each reached chain
  its converged state for the checks and later readers (A2). The backward direction (liveness; superseded by R4-M24 for liveness: a
  chain P1 reached joins its runtime successors only) arrives with U7, its
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
  Deref, SPEC 13.5.5); Deref and Index end a path (an access through them is an access of the prefix's path; a Move through
  them has no Move Path, except through a raw pointer: an untracked Take, SPEC 5.2.3). Paths are numbered in pre-order without a
  stack, so a subtree is a range; a constructor's `_0` (`AdtDef.IsConstructor`) has a child per Field and its base. `MirInit`
  (A1) keeps three lanes, joined by union: MaybeUninit, MaybeMoved, EverInit (MaybeInit joins with A2, U5, its first reader). A
  definition needs each strict ancestor's own bit (its construction fact) initialized, reports ReassignedLet when a path
  admitting one placement was ever initialized in the binding lifetime (the paths of a `let` Var, and of a constructor's Field
  that `AdtDef.PlacedOnce` names: a `let` Field or one with a custom `set`, SPEC 11.7), then initializes the subtree; a Move
  uninitializes and marks it moved, a Drop uninitializes it, StorageLive and StorageDead start a fresh lifetime; a use needs the
  whole subtree, own bits included, initialized on every incoming path (a complete value has both the construction fact and
  current completeness, SPEC 15.1.2, 15.1.5; MovedPlace where it may have been moved, else UninitializedPlace). Every root is
  tracked (A2 reads temporaries' states), but these state problems are recorded only for the paths of Var, Arg and Return roots:
  a compiler temporary is never a Place of the program, and in check-only code a temporary that an expression whose value only
  the checks reach never wrote is no uninitialized Place. A Move without a Move Path is StaticMovePathRequired whatever its root
  (an operation check, SPEC 15.1.3: an operation that requires a Move Path is rejected when none exists; 15.1.5: a Place a
  function publishes never offers Take) and changes no state. A whole use of the return local is a delivery, checked only where
  the runtime edges reach it (SPEC 14.10.3): a constructor's object is complete at each exit it requests (SPEC 6.3.5), where
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
  rule were added after U1's ledger and its independent review (main session, 2026-10-12). Its three lanes are superseded by
  R4-M6a (five lanes). Its rejection of DefaultArgumentMove decided in A1, and the PreparedInput rule left to MirReport, are
  superseded by R4-M69.
- **R4-M15a** (2026-10-12, planning session; supersedes R4-M15's "A5's arena" scope) `OwnershipStorageLimit_Kd` stays the one
  Resource code; it is decided at the one place that sizes analysis arenas, `MirDataflow.Prepare` (A1's state arena now, A5's
  Loan × point arena in U7), by byte size against the old path's limit value, 64 MiB (IMPL 21.3.2's text changes in R6). An
  over-limit body records the code once at the body's subject and A1 does not analyze it; the driver owns that skip once A2 and
  A5 read the arena (U5, U7). Reason: the dense arena is O(heads × paths/64), and a body bounded only by the 65,536-statement
  expansion limit could otherwise exhaust memory.
- **R4-M6a** (2026-10-12, main session; design A of two independent U5 designs, with design B's problem code; refines R4-M6's
  shape, supersedes R4-M21's "three lanes" and implements R4-M15a's driver skip for A2; its table amended by R4-M67: each body's
  completed views) Drop elaboration (A2). `MirDrops` judges
  each Drop the runtime edges reach from the state A1's checks replay holds before it and writes `MirProgram.Drops`
  (`MirDropTable`, per program version by body ordinal); MIR is not rewritten. A Drop only check edges reach has no row (plan
  5.6.3; the R4 rewrite of SPEC 14.10.3, plan §16-4, states that drop elaboration and its destructor rule apply to runtime paths
  only). A1's transfer keeps five lanes: for A2 MaybeInit and MaybePartial (a non-root path a Move or a Drop took out of its
  parent, cleared with the subtree by any event of an ancestor-or-self), so the state is exact under union joins for the programs
  A1 accepts; A1 hands A2 the paths its entry initializes (the arguments'; a constructing `_0`'s own bit has no subtree event,
  so no point, decision risk 6). Five lanes are kept for every body (decision 1): the 64 MiB arena limit (R4-M15a) now trips at
  3/5 of U1's heads × words (about 23% smaller N in the N-heads × N-paths shape); V17 shows no affected program; a per-body lane
  count is reconsidered at V18. Per view: Elide where the case Type needs no drop (`MirTypes.MayNeedDrop`) or nothing is initialized on any path;
  Keep where the whole is initialized on every path or the Place has no Move Path (a static, a Place through a reference or an
  index); Flag where it is complete or wholly absent on every path, the flag mirroring the path's own bit; Open where a part may
  be missing: the components without a child path follow the own bit, each child path its own verdict. A layer whose Type
  declares a `drop` and that may be incomplete at a Drop is `MovedPlace_Kd` at the Drop (SPEC 15.1.3, 16.3.2), any root (an
  UninitializedPlace variant at a Drop needs a producer of part Drops of a maybe-uninitialized layer; none exists (the
  replacement Drop's Define clears it; U5b's receiver parts are initialized at entry) — add it with that producer), replacing the old path's Unsupported gate on a partial Move through a drop-bearing
  ancestor; check-only code is not judged (plan Appendix B C3). Rows `MirDropRow(Block, Views, Verdict, Flag, Depth, Step, Arg, Path)`
  (Path, a part row's move path, -1 at Depth 0, added by U7 for A5, which reads it: A2 owns the fact),
  one group per Drop and view class (a group equal but for its views to the Drop's previous group is merged into it): the dropped
  Place, then under Open its child paths in pre-order with their steps (`MirMovePath` carries Kind and Arg). A body's rows are
  ordered by block, and `MirDropTable.Group(body, block, view)` is the one lookup of a Drop's group under a view, which R5 code
  generation, A4, A5 and the dump read. Flags are numbered in path order per body, so a subtree's flags are one range;
  `MirFlagPoint(At, First, Count, Set, Views)` where an ancestor-or-self is defined, moved, dropped or its storage begins or ends
  on a runtime path, an argument's flags set at the entry (Index -1), ordered by block. States are per solve, recorded as
  Type-free trees; the Type rule is per view from those trees (a view-dependent body records them per view). A2 has no arena:
  `MirInit.Run` returns false over the limit and the driver stops that body's views. Rejected: elision by every consumer (design
  B: one Type decision in A4, A5 and R5); a second forward problem or replay; four lanes with Open for any mixed subtree (correct
  drops, but the SPEC 15.1.3 rule undecided or false errors from joined bits); replaying or re-solving per view; storing Types in
  the record; rows on MirBody; flags per Drop; a separate event walk; removing the builder's copy-time rule; a new diagnostic
  code. Not temporary. Its input for a destructor body is refined by R4-M6b.
- **R4-M6b** (2026-10-12, main session; U5b, NEW-F9; refines R4-M6a's input) A destructor's `self` is no owner (SPEC 16.3.1). The
  builder registers its end cleanup as the layer's field cleanup (SPEC 16.3.2) through `Cleanup.Fields` (`FieldCleanup`): after the
  body's Scope Exit, on the normal end and in every return's drop tree, a Drop of each own Field whose Type may need drop, last to
  first, then of `_1.base1`, by the one presence rule `MirTypes.MayNeedDrop`; no Drop of the whole receiver. A1, A2, A4, A5 and R5
  read these as ordinary Drops (a moved Field is Elide or Flag; a Field whose own Type declares a drop and that may be partly moved
  is `MovedPlace_Kd` at its Drop). A struct that declares a drop is destroyed by its destructor body alone: R5's glue calls it and
  adds no field cleanup, and `MirTypes.Destruction` does not descend into such a struct (A4 enters the body). `InlineStorage<A>`
  releases nothing: its owner destroys what remains (its library contract in `Storage.kimi`, SPEC 22.2;
  `AdtDef.IsInlineStorage`, and the old path's `AggregateLayout.cs:353` until R6). Rejected: a parts-only Drop or a receiver local
  role (O1; a second meaning of Drop in every consumer); A2 recognizing the receiver (a consumer deciding what the builder
  publishes); field cleanup in R5's glue (cannot skip moved Fields). Not temporary.
- **R4-M13a** (2026-10-12, planning session; U3 design; supersedes R4-M13's numbers, not its migration kinds) A test method
  migrates once, at the stage of its latest assertion kind; lookups and failure messages migrate in every method. Control-flow
  structure reads: 1 (`LegacyRecords`, the harness's and the snapshot's one reader, until R6). `Ownership.Bodies` reads: 183
  after U3, about 174 after U10; the rest is R5 (emission pairing 40, defect injection into old plans and graphs 87, library
  reach 23: the MIR builds every library body and IR names are numbered, so reach has no public observable before R5's
  emission roots) and R6 (old internals, old storage and plan reuse, the probe tool and three class helpers, 24), each site
  tagged with its stage in the commit that leaves it. A ratchet row counts the test calls of the old control-flow entries
  (`AnalyzeControlFlow()`, `ControlFlowAnalysis.Analyze(`), so direct calls cannot grow; the A3 old-against-MIR helper
  `ControlFlowAnalysisTest.RequireFallthroughs` (R4-M7a) is the one shared reader, whose old half R6 deletes for its three
  callers. Reason R4-M13's numbers no longer hold: they assumed a residue of about 110, and the inventory shows that reaching it
  in R4 would delete old-Emission validation tests that still guard the path deciding acceptance until R6 (R4-M1, D5).
  Rejected: deleting the injection tests now; MIR presence for library reach (vacuous while every library body is built);
  `LegacyRecords` as the general reader of flow records (it moves reads out of the ratchet without migrating them).
- **R4-M25** (owner, 2026-10-12; area caps rebalanced within the R4 stage ceiling; superseded by Q5) Binding's token cap rises from 375,051 to
  377,051 and Core's from 8,167 to 8,187; Mir's cap (93,651) and the R4 total ceiling (894,362) are unchanged. Reason: the
  measured needs of the R4 analyses are published Binding facts (U6 effect obligations and call bounds, U7 Relate/Clauses and the
  callable signature, U9 universal relations) and U11's internal reporter in Core, against 2,997 Binding and 0 Core tokens of
  headroom at 046efcf1 (848,182 of the 894,362 ceiling). Gate: Mir is re-projected at the U8 and U9 designs; a projection above
  90,000 is consolidated first or returns to the owner. Per-unit Binding-line exceptions stay owner decisions (U6 about 240
  lines, R4-M23; U7 about 150 lines, R4-M9b). Rejected: cutting R4 scope (Q1 through-reference replacement, U9's preserves
  witnesses) to R5, which would leave (b) rows at the R4 exit; raising the stage ceiling (not needed by the projection).
- **R4-M22** (2026-10-12, planning session; design B of two independent U6 designs, with design A's classification of the
  Binding-rejected virtual functions) A4, `MirEffects`, decides `confined` (SPEC 8.11.2, 8.11.4, 8.11.6, 15.6.4). Per body view a
  union summary: the classes Environment, Unknown, Limit and Derived and atoms Drop(T) and Call(T, receiver) over the definition's
  abstract Types. Local facts come from `MirUses` (a mutable static's access, an environment effect, an integer made a pointer and a
  raw pointer read from an immutable static are Environment; an Opaque site's Unknown is Derived) and the terminators: a call of a
  function with a body (`MirProgram.Callee`: a default template, the function, its getter or setter) is an edge into each body the call
  may run (`TyCtxt.Implementation`, SPEC 8.13.3: the specialization a closed key selects, else the function and each specialization its
  open key may still select, the rule the old summary shares; a default template is always the original's), in the callee views its
  substitution selects (`MirCases.Views`; staticEnsure enters the static's initializer); a function without a body, and a Requirement
  callee that names a compiler function (a built-in witness), is what `TyCtxt.Bodyless` classifies for it and its Self (a built-in
  comparison of a composite Self Unclassified, a Limit: its witnesses are instantiated by the backend until R8), `ArrayClear` and
  `Replace` also destroying what lies below their first argument. A Requirement callee is either an unresolved requirement or Binding's
  resolved built-in target: a closed Self with a user witness is a Fn callee of the witness. A Requirement or dynamic Virtual call
  takes the bounds of the caller's own premises (`TyCtxt.DispatchBounds`, plan Appendix B X2, SPEC 8.11.4): X2 governs the
  requirement calls on a generic body's abstract Types that no published summary governs. A requirement of a closed Contract (Position,
  PositionRange) has no effect (SPEC 8.7: every bound); a SPEC 4.9.6 operation contributes its published row (`K` equality and
  destruction) through the call's substitution, never its body, which A4 does not do yet: it enters the body, so confined code calling
  Dictionary's published operations is rejected until U6b (ledger (b)). Without confined the call is Unknown, and it may destroy what
  is moved into it (rule 1); a call through a value calls its Callable input: a Closure or function item enters each body it may run
  through its substitution, a common Function Type is Unknown (erasure keeps no bound), an abstract input is nothing where its one
  Callable signature's premises declare confined (`TyCtxt.CallableBounds`), a Limit where it has several signatures (Binding chose
  among them by the call's arguments, which MIR does not keep), else an atom. A Drop destroys by A2's verdict group in the view
  (R4-M6a, `MirDropTable.Group`): Elide nothing, Keep and Flag its node's Type, Open no own layer (one that declares a drop is A2's
  `MovedPlace_Kd`) but each child row and each part without a row, which follows the own bit; a Drop only check edges reach runs no
  destructor (drop elaboration and the destructor rule apply to runtime paths only, SPEC 14.10.3 as R4-M6a reads it; the blocks the
  runtime edges reach are `MirReach`'s walk), while check-only statements' own effects count; a runtime Drop with no row (a body over
  the arena limit, R4-M15a) destroys its case Type. What a destruction does is the one walk `MirTypes.Destruction` (SPEC 16.3): an
  abstract part is an atom, a Function value Unknown, an owning handle its payload where proven Sealed and else Unknown, a struct that
  declares a drop its destructor body alone (`AdtDef.DestructorSymbol` under `TyCtxt.Synthesized`; the body's field cleanup destroys
  the Fields, R4-M6b), an `InlineStorage` nothing (its owner destroys its contents, `AdtDef.IsInlineStorage`), a part past the walk's
  depth bound a Limit; `MirTypes.MayNeedDrop` is that walk reporting any part. A part past the depth bound and an atom whose
  substitution fails (`TyCtxt.Apply`) are the located `Unsupported_Kd` of an instance this implementation cannot analyze (SPEC 8.15).
  Atoms compose through each edge's substitution under the caller's view: an atom still abstract there becomes the caller's (a call
  atom first asks the caller's premises), a concrete one is resolved there, as SPEC 8.11.6 composes calls through the actual
  arguments. One worklist over every body view, seeded in program order, re-evaluates a node when a summary it read grows (dependencies
  recorded on read): the lattice is finite and the summaries only grow, so it reaches the least fixed point with no bound and no
  instance context (V16: evaluations and cold allocation linear in calls). The obligations Binding publishes (R4-M23) are judged with
  their remaining atoms resolved by the premises of the row's definition (a destroyed abstract part proven Copy does nothing, any other
  atom is Unknown): every implementation of an owing definition (SPEC 8.4: specializations included, `TyCtxt.Implementation` with no
  substitution), its own body per view, a specialization, closed, and a function without a body in every view at once; a Callable
  binding as a call through its subject with the selected receiver (the classification a call through a value makes) under the premises
  of the use's definition, with no caller view (its open pair layers admit every case, `MirCases.Deselect`) and reported in every view;
  a failure the premises decide in every view. Environment or Unknown is the row's code at its subject, else Limit is the located
  `Unsupported_Kd`, else nothing (a Derived cause, R4-M10). A4 decides `confined` only: `preserves results` is A5-3's (U9), static
  access keys U8's and cause sites U11's, each added by its first reader. Driver: `Local` per body view after the body's A1 and A2
  (step 2, over its closed A2 table), `Run` over the program (step 3). Rejected: counting a Drop only check edges reach (the drop tree
  a `return`'s check edge leads to would destroy the values the runtime path moved: `return l@move` in a confined witness rejected);
  porting `EffectSummary` (instance contexts and their 1024 bound, order-dependent, X2); per-body summaries with Semantics cases
  composed like Type parameters (SPEC 8.15 never merges cases, and A2's verdict is per view); requirement calls as atoms everywhere
  (SPEC 8.11.4 bounds a generic requirement call by the premises at the caller); edges to every specialization (the body a closed key
  does not select never runs, SPEC 8.13); Tarjan SCCs (dependencies appear only while composing, when an atom becomes concrete or a call
  reaches a Closure); no atoms (rejects confined code calling generic helpers with harmless Types and breaks SPEC 8.11.6's
  composition); a literal callee-bounds reading of X2 for destruction (rejects every confined implementation that drops an
  `Array<i32>`, O3); a per-obligation reachability walk (quadratic); a call-bound field on `MirCallee` (dump and validator churn);
  finding Callable obligations in MIR (it lacks Type formations and argument subjects); a Mir copy of the compiler-function table;
  deciding Derived in `MirReport.Add` (the cause class is A4's lattice). Supersedes R4-M9 for `EffectBoundTable`, `ImplTable`,
  `VTableDef` and `CallableBounds`: not built, since their only reader needs who owes confined and the bounds at a call; R5 builds the
  slot tables when it reads them. Not temporary. Its SPEC 4.9.6 sentence, `ArrayClear`/`Replace` clause and composite Limit are
  refined by R4-M28.
- **R4-M23** (2026-10-12, planning session; owner approvals of 2026-10-12) The effect facts Binding publishes for A4
  (`Binding.EffectObligations.cs`, `TyCtxt.Effects.cs`). `EffectObligation(Subject, Code, Definition, Callable, Receiver, Owner)`,
  listed from the pass's final state into A4's list (`TyCtxt.EffectObligations`); its Definition is the definition whose body owes, or
  whose premises judge the Callable. Each witness of a conformance whose Contract or an ancestor declares confined for its requirement
  (`IncompatibleContractImplementation_Kd` at the `Self is C` clause; a virtual witness by its slot's bound), each virtual function
  whose slot declares it (`UnsatisfiedEffectBound_Kd` at the function), a conformance or function the old effect checks rejected
  included: the enumeration reads their violation tables (`effectViolations`, `virtualEffectViolations`), a Transitional coupling that
  R6 removes with those checks, after which an obligation follows the conformance's and the function's own validity. Each selected use
  of a Callable clause declaring it (`UnsatisfiedEffectBound_Kd` at the argument or Type argument), enumerated once per pass at the end
  of `ValidateEffectBounds` (`CollectCallableEffectUses`, which the old judge reads too): Binding decides only an abstract subject, by
  its Callable premises over the signature it selected (none where they declare confined, a row without a definition where they do
  not); every other subject is a row with the use's definition, the subject Type and the selected receiver, which A4 judges as a call
  through it, and Owner where an owner acquisition of a Closure with another receiver also destroys its environment. One premise scope
  per definition, `Binding.EffectPremises` (an override's inherited implementation premises where present), serves both bounds, the
  Callable premises, `Copy` and `Sealed` (the scope-taking `ProveCopy` and `ProveSealed`, which Binding's other Copy and Sealed proofs
  use too). No program tells it from the declaration scope: an override's implementation premises share its body scope's constraint
  environment (`PrepareVirtualOverridePremises`), so the inherited-premise test passes either way. `TyCtxt.DispatchBounds(definition,
  callee, self, contract)`: a requirement's or virtual slot's available bounds under those premises, every bound for a requirement of a
  closed Contract (SPEC 8.7, kept out of `AvailableEffectBounds`, which the old path shares), null for a callee that does not
  dispatch; a requirement function item dispatches through its own Contract reference (`TyCtxt.ItemContract`).
  `TyCtxt.CallableBounds(definition, input, receiver)`: the bounds of an abstract Callable input's one signature (`TryCallable`, the
  selection rule of the call through it), null where it has several, a located limit until MIR carries the selected signature (an open
  issue). The bound-validity filter is one predicate, `Binding.AppliesBound`. `TyCtxt.Bodyless(function, self)`: the compiler-function
  effect table moved from `Binding.ClassifyCompilerFunction` into `IntrinsicDisposition.Effect` (one table beside the intrinsic table),
  Unclassified for a built-in comparison of a composite Self (`ComparisonTypes.IsComposite`, Binding's decision), Environment for a
  foreign function, `Dependent` for a definition Binding rejected (its parts are not published, R4-M18), else Unclassified.
  `TyCtxt.Implementation(function, subst, index)`: the bodies a call may run, over `Binding.SelectSpecialization`'s key rule and
  `MaySelect`, which the old summary shares. `TyCtxt.Copy`, `Sealed` and `AdtDef.DestructorSymbol` complete the facts.
  `EffectObligation` and `CompilerFunctionEffect` join the mir allow list (owner approval, `Code-Rules-Approved` trailer). Owner
  exception (user, 2026-10-12): Binding ≈ +240 lines in U6, above the 30-line rule; raised from ≈ 110 after the measured design-B
  facts; F6 consolidation applied. The ledger compares Binding's `IncompatibleImplementation` and `Unsupported` records only where an
  effect check made them (`Binding.IsEffectRecord`, Transitional, removed in R6 with the old checks);
  `ValidateVirtualDestructionEffects` no longer clears the Binding pass's virtual violations (the pass resets them), so a Binding-pass
  virtual limit stays identifiable. A virtual function the Binding pass rejects with VirtualEffectBound has no published body (R4-M18),
  so A4 sees its target as Dependent: those ledger rows are intended (d), until R6/R7 remove Binding's effect judgment. Rejected: four
  view types (four allow-list entries and Mir re-pairing witnesses with slots); tuples instead of a row type (unreadable); rows
  snapshotted before the old checks (`Binding.cs` is at its file-size baseline, and the violation tables already name what the checks
  rejected); Binding pre-classifying every Callable binding (a Function, Closure, item or dispatching item: the classification A4 makes
  for a call through a value, a second copy judged under the target's premises instead of the use's); an agreement walk over a Callable
  input's premises (a third enumeration of the Callable facts beside `SelectValueCall` and `TryCallable`; carrying the selected
  signature in MIR is the general fix and changes a shared MIR form); one bounds function with a null-callee mode (two contracts in one
  signature). `Bodyless`' composite clause is refined by R4-M28.
- **R4-M24** (2026-10-12, main session; superseded by R4-M33; option P of two independent redo designs, `u7r-decision.md`: design B's positions and
  liveness with design A's core) A5-1, regions and Loans (SPEC 15.6), `MirBorrows` (`.Regions`, `.Liveness`, `.Flows`) per body and
  view in the driver's step 4, after A4:
  - Positions: a cell is a move path (R4-M21) whose case Type carries Origins in its own storage (a leaf all its Type's; an inner
    path those of each storage step without a child path and its Phantom Origins); a table's unwritten borrow-layer Origins are its
    one null atom. Holders are (definition, cell): the entry of an Arg's or the return local's cells, each whole write of the cell or
    an ancestor (def 0 the first), numbered cell-major in path order, so a subtree's holders and reaching bits are one range; a
    one-holder cell has no bit; the holders are placed in one pass as (cell, location) pairs (`MirDataflow.Index`), and so are the
    Loans' order. A static is a run of escape variables (K11). `Replaced` is the one location decoder (K3).
  - Precision rule (planning session): a Place that is its path covers the subtree [p, End(p)), through an Index too; through a
    reference, an object handle or a raw pointer, only p's own cell (no path lies under it).
  - Flows: only actual flows add edges: `TyCtxt.Relate` (variance by `ComponentVariance` and `ComposeVariance`) from the covered
    reaching holders into the holders a whole write defines, else into every covered reaching holder; a relation links the
    variables holding its longer end to those holding its shorter end, through one junction where both are several (N3); an end no
    variable holds is counted (`Unmapped`, 0 by test over library, milestones and corpus) unless it is `BoundOrigin.Static` (N2). A
    borrow relates its Place into the referent (equal unless shared), its Loan into the outer Origin, each parent's outer Origin into
    the Loan up to the first shared layer (S15:852). A call with a signature (`MirTypes.Parameter`; function items and Closures by
    `TyCtxt.Callable`) relates its arguments into fresh call variables, their well-formedness, the callee's clauses (a function
    item's under its own substitution) and its result into the destination, and forms a caller-side Loan on an Arg's referent for
    each unsupplied outer Origin (S15:881). A call without one relates every argument and the callee value into the destination and
    into every variable behind a writable layer of every argument, sound for any callee. An Upcast is invariant only where the
    handle is (`TyCtxt.InvariantTarget`, N1).
  - Liveness: backward on path numbers; a chain P1 reached joins its runtime successors only, a P2 chain every successor (SPEC 14.9:
    Loans follow runtime paths, continuations add none; 14.10.3 paragraph 4: their states never merge into reachable paths;
    supersedes R4-M20's "liveness, all edges"); a use makes live what its Place covers; a whole write, StorageLive, StorageDead and
    EndBorrow end what they cover (EndBorrow ends liveness only, F2); a Drop whose destruction may observe an Origin
    (`MirTypes.Observes`, a visitor of the one `MirTypes.Destruction` walk, R4-M22) uses what A2 keeps (each row's own path, the rows
    by `MirBody.Ordinal`): an Elide group nothing, Open its own cell then its child rows by their verdicts, any other verdict what it
    covers; a Drop without a group, which only the checks reach, nothing (plan 16-4: drop elaboration and destructors on runtime
    paths only). The driver skips a body A1 stopped (R4-M15a).
  - Active Loans: forward P1/P2 over [flow holder bits | Loans | chain-local holder bits]. Each Loan's count of reaching and live
    holders is exact by differences: a holder's status change adds ±1 over the Loans it carries; a chain's head takes the difference
    to the chain advanced last (reaching bits against that exit's, kept by `MirDataflow.Tail`; liveness against `AtEntry`). A Loan
    ends at count 0 unless it escaped (caller storage, a static), and where its borrowed storage ends (Loans ordered flow, then
    chain-local, each by the path it borrows, so a subtree's end is one range per group; never through a reference).
  - Bound (V16, planning session): Θ(size + holders + holder pairs + Σ status changes × |carried|) counted by `Work`, plus the dense
    word terms counted by `Dense`: heads × width/64, Σ storage ends × Loan words/64 and Σ whole writes and accesses × the holder words
    of the written or read cells; holders are Σ whole writes × the cells of the written subtree. Linear on V16's shapes 1-3, 3b, 5 and
    one local reassigned N times; Q4 and shape 4 are Θ(N²), the output (active Loans per location). Work beyond input size + output
    size is a defect for an analysis whose output is per location.
  - Interface and limits: `MirDataflow.Prepare` sizes the forward and backward arenas under one limit (R4-M15a); a view where no
    Loan can form is not solved (N7); `Replay<IMirLoans>` gives A5-2 the Loans active before each location.
  - Open: Q1-ref (a complete replacement through a reference keeps the referent's earlier Loans, 2 agreement rows; U8b after U8
    builds provenance, SPEC 15.6.2 :824). Conservative residue: aggregate parts sharing one atom (an aggregate's operands relate into
    every cell holding it), Q2, Q6. A5-closure (a Closure's internal call contract: receiver, captures, inferred relations; unit U7b,
    before U8).
  - Rejected: option L (whole-local holders, a holder unit later: a unit revising the previous unit's rule); liveness over every
    edge and a check-only Drop's whole use (reachable code kept Loans for continuation uses); A2 verdicts for check-only Drops (a
    second publication for a path that by SPEC uses nothing); a lazy per-chain recount with
    edge-death candidates (quadratic on shape 3b); design A's narrow through-reference strong update (one form of an open family);
    design B's claim that no IntrinsicId writes through a safe exclusive argument (a cross-component claim); cells without own
    parts, complete child paths, per-root liveness, bit rows, NLL regions per slot and region SCCs (design B's (c)-(h)); holder scans
    (`Alive`/`Recheck`, cubic on Q4); `Release` (unsound, F2); entry anchors before U8, their first reader (K7).
- **R4-M9b** (2026-10-12, main session; supersedes R4-M9's view list and placement; superseded by R4-M33) Binding views are built by their first reader
  (R2b-D3). U7 builds `TyCtxt.Relate` (`Binding/TyCtxt.Relations.cs`; U7a moved MIR's structural agreement there, U7 adds its Origin
  relations), `TyCtxt.ComponentVariance` and `InvariantTarget` (U7k: SPEC 15.3.5's one component rule, `obj` invariant, shared with
  `Binding.ComposeVariance` and Binding's local Origin inference), `TyCtxt.Clauses` (a callee's declared relations through
  `Binding.SubstituteClauseOrigin`; `OriginClauses.Of` folded in) and `TyCtxt.Callable` (U7c: a callable value's signature, which
  replaces the unused `Binding.ValueSignature`). `OriginAtom.Of` and `UniversalRelations.For` move to U9, their first reader. R7
  converges `FitsTypeCore`, `CheckTypeUse`, `CollectCallRelations`, `FailedOriginRelation`, the sign variance of
  `OriginInference`/`TypeOrigins` and `IsInvariantLayer` (no `obj`, Q3) on these views, and `Binding.CallableProofs` and
  `Binding.Constraints`' signature selection on `Callable` (stage ledger). Owner exception (user, 2026-10-12): the 30-Binding-lines
  guideline is waived for U7 up to about 150 lines (from about 70); measured +140 lines, +924 tokens (U7a +54, U7k +17, U7c +1,
  U7 +68). Its view list where it applies (`OriginAtom.Of`, `UniversalRelations.For`): the C1-F4 view of R4-M94 (2a0-2).
- **R4-M26** (2026-10-12, planning session; U6c) A selected call (`CallPlan`) and a substitution (`Subst`) hold one length entry per
  slot, parallel to their Type arguments, the Implementation Selection Key's form (SPEC 8.13.3, IMPL 21.3). Their constructors
  (`CallPlan.Set`, `TyCtxt.Intern`) establish it: a producer passes no lengths (the target has no length slot) or one per slot, and a
  span of another length is a broken contract (`Debug.Assert`). A function item's call context (`FunctionItemContext`), the
  Dictionary storage calls, the object factory call and the MIR builder's synthesized calls pass none, so a function item now runs
  the specialization its key selects. Replaces the per-producer padding of `InstantiateIntegerPosition` (e6d2c5ee). Rejected:
  padding in each producer (an open family; this was its second fix), a key rule accepting an empty span (two forms, and the entry,
  closure-context and default-call identity probes still split one instance), the empty form for calls and keys (two shapes for a
  per-slot fact), per-slot lengths in Item Types (every BoundType keeps LengthArguments empty without a length argument).
- **R4-M30** (owner, 2026-10-12; superseded by Q5) The missing-fact allowance counts tokens: a unit may add at most 200 non-trivia Binding tokens
  (A1 count; the former "30 Binding lines" at Binding's measured 6.57 tokens per line, CR-A1) for a fact the MIR side lacks; more
  needs an owner exception stated in tokens. Forecasts and overruns are stated in tokens per area, lines for reference. Reason: the
  budget has counted tokens since CR-A1, and lines overstated growth (R4 U6b: +107 lines, of which 72 were formatting with no tokens —
  KR3001 wrapping of changed long lines, braces, blank lines, comments). Exceptions granted in lines before this entry keep their
  recorded meaning (U6 R4-M23, U7 R4-M9b); units not yet landed restate theirs in tokens. Rejected: keeping the line rule.
- **R4-M28** (2026-10-12, planning session; design B of two U6b designs with design A's witness probe) SPEC 4.9.6's published rows
  for A4: `PublishedRow(Runs, On, Destroys)` catalog data (`KimiLibraryCatalog.Row`, `TyCtxt.Published`) replaces
  `PublishedSummary`; A4 adds a row through the call's substitution, never the private body. A row's requirement is a `Requires`
  atom on an abstract Type (nothing where the premises declare it confined, else composed at the callers like a destruction); any
  other Type runs what `TyCtxt.Witnesses` resolves (comparison-plan leaves, a built-in target, the shared
  `Binding.ConformanceWitness`'s witness), else a Limit. Ratified departure from design B: a Type with an implementation has its
  effects, not its premises' bounds (SPEC 8.11.4). A requirement call written in a generic body keeps X2 (SPEC 4.9.6 clarified).
  HIR publishes a body no instance applies (checked, never generated). Refines R4-M22 and R4-M23. Rejected: per-effect flags, X2 at
  the composing node, Witnesses over RequirementImplementation only, open Types by the premises, an inapplicable override owing
  nothing. Owner exception: Binding +561 tokens. Open: published bodies unverified against rows (R6); library generics outside the
  table keep X2; composites with an abstract part a Limit; duplicates RequirementImplementation and findEntry's two identifications.
- **R4-M31** (owner, 2026-10-13; completion budget option C; withdrawn by R4-M33) R4 caps are Mir 155,000, Binding 432,000, Core 15,000 and total
  1,020,000 non-trivia C# tokens, including conditional late coexistence. Supersedes R4-M25's numeric caps and 90,000 Mir
  planning trigger for the priced R4 scope: the revised forecast includes common producer/transfer contracts omitted earlier
  (946,606 central; 1,017,696 conditional late peak). Reforecast at each selected design and landing; an area/stage overrun
  requires consolidation or renewed owner approval. Keep the per-unit 20% overrun rule, M30 exceptions and technical gates.
  Other area/template caps and R5 onward ceilings stay unchanged; reforecast R5 before implementation. No vocabulary,
  baseline, frozen-path feature, scope deferral or per-unit Binding approval is granted. Rejected: old caps (no demonstrated
  complete fit), net-only option B (conditional late peak exceeds it), and crediting R6 deletions during R4.
- **R4-M32** (2026-10-13, planning session; independent C1a designs compared and reviewed; superseded by R4-M33) Binding.OriginProof owns
  `OriginFit`: the normalized fit answer and an optional initializer contribution, identified by its Local. Classification
  changes no inference, declaration or obligation state; canonical interning and proof scratch retain their lifecycle.
  Existing committed-fit and selected-collector adapters apply the contribution immediately, before the next relation;
  candidate observation applies none. The existing traversal and mutation order stays unchanged. Deleted: duplicated
  normalization/open-initializer classification and the collector-only bound wrapper. Reject whole-call deferred commit,
  a second shadow proof rule and unused publication state. This does not make contextual expression completion pure.
- **R4-M33** (owner, 2026-10-16; supersedes R4-M9b, R4-M24, R4-M31 and R4-M32) R4 keeps A1–A4 (U0–U6 with U1b, U3n, U4b,
  U5b, U6b, U6c and the T1 test migration) and discards the work after them: A5-1 (U7) with its preparatory units U7a, U7a2,
  U7k and U7c, C1a, the MIR probe tool and the completion plan and budget. Reason: the implementation stalled (P9 143 at
  every 2026-10-13 checkpoint, the O5 trigger); the MIR plan continues and R4's remaining scope is replanned. R4-M25's caps
  apply again; R4-M30 and the other R4 decisions stand. Removed with the units, for the replan: the validator's signature
  check of function-item and Closure calls (U7c) and the Q1-ref and A5-closure findings. Unlanded U8/C1b work is not carried.
- **R4-M34** (2026-10-17; design A3 §4.1 G1 of the [A5 plan](../../draft/Proposals/2026-10-16%20Complete%20Polonius%20A5%20Plan.md)'s
  step 0, owner approval of its §9 item 9 on 2026-10-17; supersedes R4-M20's union join and its backward clause; its backward ring
  superseded by R4-M65) MIR dataflow, forward and
  backward. `IMirFlow.Join(target, incoming)` is the transfer's join and says whether the state grew: A1 keeps union (`MirDataflow.Union`),
  A5's Loan sets will join by node union. `MirDataflow.Begin` decides each chain's phase once per body, P1 where `MirReach`'s runtime walk
  from the entry reaches it, else P2, and records each block's predecessor edges; `Forward` keeps R4-M20's phases, and A4 reads
  `Runtime` instead of its own runtime walk (deleted). `Backward` solves on the same chains, restating R4-M24's liveness rule (that entry
  went with R4-M33): a chain's state before its head is its blocks applied, last first, to the boundary state (`Entry`, the least state)
  joined with its successors' stored states, a P1 chain's runtime successors only and a P2 chain's every successor (SPEC 14.9, 14.10.3:
  uses on continuations never merge into runtime paths); a ring seeded in postorder queues a grown head's predecessors' heads.
  `ReplayBackward` gives each chain its converged state, last block first. `Prepare(backward, forward, width)` sizes one arena row per
  head, the backward words then the forward ones, under R4-M15a's one limit, so one body view can hold both problems; the shape of A5's
  replay is step 1's (unit 1b). The plan's step 0 lands it before its first reader, A5's holder liveness; `MirDataflowTest` checks it
  against a per-block fixed point. Rejected: a second arena or limit for the backward pass (two Resource decisions); per-block backward
  states (R4-M20's reason); sweeps without predecessor edges (rounds grow with loop nesting); a fixed union join (A5's node union is no
  word OR); one solve with a direction parameter. Not temporary.
- **R4-M35** (owner: the plan's direction and its step 0 on 2026-10-16, with OD1–OD7 and OD12, as the plan's status line records;
  OD8–OD11 and OD13's first list on 2026-10-17, as plan §10 records them, and A3 §9 item 10 later that day; supersedes the parts named
  below; plan §7's M39 Binding part void after OD8's and OD9's non-adoption: R4-M74 OQ6 (a), and A5-F9 landed without it, R4-M90)
  R4's remaining scope
  follows the [Complete Polonius A5 Plan](../../draft/Proposals/2026-10-16%20Complete%20Polonius%20A5%20Plan.md): the Binding facts A5 reads
  first, then A5 deciding the SPEC rules of plan Appendix A as facts and a fixed set of rules that a C# evaluator runs, then A6 and
  the R4 exit tasks.
  - Adopted (plan §10): OD1 the complete approach against the rules as simplified, with no whitelist, in the order of plan §6 (a needed
    SPEC change the owner rejects leaves its rule Unsupported only by an owner exception, O4); OD2 Binding publishes each fact A5 reads
    before A5 uses it, in R7's target shape and out of R7's scope (plan §7); OD3 plan §8 replaces the R4 exit, PLAN10 §18 P9, O5 and
    G-Diff's blocking rule, and R4-M12 is suspended until the joint core (step 1) lands; OD4 R5 starts in parallel under plan §9.1's
    conditions, instance collection running before acceptance (its entry, with the target pipeline's change, comes with its first
    implementing commit); OD5 SPEC changes are implemented on MIR only, with plan §9.2's departure from AGENTS "Diagnostics" until
    R6-check (owner exception); OD6 review and records as plan §9.3, in force once AGENTS "Review weight" is revised on the owner's
    instruction (until then AGENTS and Q5 govern); OD7 each rule the audit finds not finitely expressible gets its own SPEC proposal
    (O3 and O4 apply); OD10 R6 runs in two stages, R6-gen after R5 and R6-check after R4 and R6-gen, with `AnalyzeInstance` a
    check-only step until R6-check; OD11 confirmed: a complete write through a reference ends none of the referent's earlier Loans
    (S4); OD12 simple, then fast, then precise, never trading soundness, a precision loss being allowed only as a rule clause decided
    as plan §5.3 states, which is the owner's request for the resulting SPEC revisions; OD13 each list of precision losses is approved
    as one decision. Not adopted: OD8 (no Case terminator; case dependence stays per view, R4-M19) and OD9 (the inline default fold
    and the SETTLED entry stay; no closed defaults).
  - Step 0 (plan §6.2) produced design A3 (`draft/Sketches/A5 Step 0/design-A3.md`, confirmed by its third review). The owner chose the
    representation for the access-check family (A3's transit clause) and approved A3 §9 as recommended on 2026-10-17: A3's losses P1,
    P3, P4, P10 and P11, the optional P5, P6 and P9, and S1's `within` kill; relaxations (a) and (b); S1's additions; clarifications
    c1 and c2; V16 as A3 §6.3 restates it; the Origin boundary of A3 §7; the MIR contracts of A3 §4.1; the DEC and DIAGNOSTICS
    records. Not adopted: the root-most kill and relaxation (c). Later on 2026-10-17 the owner also approved A3 §9 item 10, loss P12
    (`desc(c)`, PLAN G113): 「承認します。」, relayed by the planning session. Where A3 departs from the plan (G3: no drop-row
    Path), A3 governs.
    Until the SPEC revisions land, before the R4 exit, a violation of an approved loss is a located `Unsupported_Kd`. Each
    representation, contract or phase move gets its own entry in the commit that first implements it (R4-M34 onward).
  - Superseded: PLAN10 §12.1's "R3–R6 run alone" and its single D5 end (now per stage), §12.2's R4 exit, R5 start condition and R6 row
    (the single switch), §12.3's stop rule, §13.1 G-Diff and §18 P9; R4-M2a's deferral of the `MirAnalysis` restructuring to the R4
    exit (now before the R5 split, with `MirDump`'s); R4-M4's compared set and R4-M17's harness as an exit gate (the ledger stays as
    information, plan §8), and R4-M17's tier 1, which only compares the two paths, gains expected codes for the SPEC examples; R4-M5's
    rule that a new-only row is (b) (a row blocks only as plan §8 states); R4-M12 until the joint core lands; R4-M16's classification
    table, which gains a SPEC-clause column for (d) rows (plan §9.2); O5, replaced by plan §8's stop rule: the owner is consulted after
    two consecutive working sessions on the plan that land no unit.
  - Elsewhere in DEC and ARCH, an "R6" item that only Emission concerns means R6-gen and any other means R6-check (for example R4-M1,
    R4-M3a, R4-M13a and R4-M17's unit a).
  - Superseded later in part (2026-10-18): plan §9.1's "both measured" by R5-M3 and its forwarding properties at R6-check by
    R5-M9. OD4's parallel run was suspended by R4-M77, restored by R4-M78 and suspended again by R4-M79 (all 2026-10-19), and restored
    by R4-M86 (2026-10-20).
- **R4-M36** (2026-10-17; design A3 §4.1 G2, owner approval of its §9 item 8 on 2026-10-17) The Type facts A5's holders and destruction
  checks read. `MirTypes.HoldsLoans(type, seen)` (A3 §3's holder rule): a part of the value is a borrow or pair layer, a Slice, a
  Function or Closure value or an abstract Type; its parts are its own storage (Fields and base, payloads) and its components (elements,
  an owning handle's payload, Type arguments); a raw pointer and a function item hold none. Two clarifications of A3's list, both needed
  for soundness: an abstract Type counts, since an instance may store a borrow its Origins do not show (SPEC 15.2.3's `pass(local)`;
  `BoundType.HasAbstractPart`), and a Type argument may be stored (library collections and their iterators hold their elements behind
  a raw pointer, and SPEC 3.2.2 and 15.3.5 analyze a Loan or a Weak as storing its argument); leaving either out loses the Loans an
  iterator or a T-typed result carries. `MirTypes.Observes(type, types, definition, seen, out deep)`, a visitor of the one Destruction
  walk (SPEC 15.6.6, 16.3.3): a struct that declares a drop, a Function value, an abstract part not proven Copy, or an owning handle
  whose payload is not proven Sealed, or is and observes (any rc or arc release may be final), under the definition's premises
  (`TyCtxt.Copy`, `Sealed`). Each walks a Type, or for Observes a payload below its handle, once per query through the caller's set: a
  repeating recursive Type ends exactly, a growing one (SPEC 15.3.5) at the bound of 32, where HoldsLoans answers yes and Observes sets
  deep, the caller's located `Unsupported_Kd` as A4's Limit is. The Destruction walk cuts no cycle through a sequence, so a recursive
  Type holding itself in an Array is deep (PLAN G91; superseded by R4-M58). Rejected: `CarriesOriginOrSlot` as the test (builder-made references carry no
  Origin); a depth bound alone, as HoldsRaw has (a repeating recursive Origin-free Type would become a holder and keep the Loans read
  into it live); one fact for both (two readers, two rules). Not temporary.
- **R4-M37** (2026-10-17; design A3 §4.1 G5, G6, the writeback and the guard row, owner approval of its §9 item 8 on 2026-10-17;
  G5's path clause and G6's value rule superseded by R4-M52 (implemented as R4-M54 and R4-M62); its `EndBorrow` meaning for guards
  replaced by A3 §12 E7, recorded with unit 1b-4; its Open "a pair-layer argument is acquired at its Call" superseded by R4-M66) The
  MIR builder contracts A5's reservations and guard-local Origins read. G5: every exclusive borrow on an invocation's preparation path
  is `TwoPhaseUniq` (SPEC 15.6.7): a written `@uniq`, also in parentheses or on the Place path of a receiver or an argument down to a
  value (`h@follow@uniq.c.bump(1)`; a call result or a getter activates at its own call), the Reborrow of a stored exclusive reference
  at an input (`Acquire`), and every synthesized receiver (`Borrow`, whose results are all synthesized calls' inputs). The call-input
  fact passes from `Operand` through `Value`, `Into`, a group's `Assign` and `Place`, never through control-flow results or nested
  calls, whose borrows are live when formed. G6: an invocation whose operand does not complete is still built where that operand's
  check-only code continues, with its later operands and defaults (`Defaults` no longer stops at a diverging default), so its
  activation is checked in the assumed state (SPEC 14.10.3). By the builder's rule that a node which does not complete assigns no value
  (`Set`), the call takes such an operand as `Never`: its own temp where it has that Type, else an unwritten temp, also where Binding
  gives it a Tuple with a `Never` element; a pending slot keeps its parameter's Type. The validator lets a `Never` argument agree with
  any parameter. A transfer ends the borrows of the calls it abandons on its runtime edge only, in the block that places its result.
  The continued calls are seen by A1 (their Moves and definitions: a later Move in the check-only code is `MovedPlace_Kd`, as SPEC
  14.10.3 requires) and by A4 (their callees' edges, which R4-M6a counts). Writeback: a compound update whose destination is written in
  place (a Place, or a standard set's storage) reserves it once located, `$w = TwoPhaseUniq(D)` at the update, and reads and writes
  `(*$w)`, so the write activates the reservation (SPEC 13.7.2). Guard: each guard candidate's cleanup registers an `EndBorrow` before
  its `StorageDead`, so its guard-local Origin ends on every path, a transfer's drop tree included (SPEC 14.8.3); candidates no longer
  sit among the pending operands. `EndBorrow` is restated as a use of the local after which every Loan issued into it must be dead, its
  carrying ended, and never an activation; `TwoPhaseUniq` activates at its Call or at the write through it. No vocabulary entry. Not
  done, for the planning session: A3's `RawSlice` backing operand (arity 2→3 breaks the written `Raw.slice(storage, length)` calls,
  which have no backing; a SPEC and library revision or an O1 intrinsic); C8, deferred to the unit whose `Retains` reads it (step 1):
  a Value callee's selected Callable signature needs a form there (a `TypeToken` constant or a `MirCallee` field), and a Virtual callee
  is named by its `Symbol` and `Subst` already, so `MirBody`'s "a Virtual one's slot in Arg" does not hold today. Open: A3's mode rule
  makes a reserved Loan `ref` only in its reservation holder, so a parent reservation reborrowed by a child one (the G5 Place path) is
  uniq in the child before the call, which `holder@uniq.items.append(x)` (SPEC 15.6.7: the same call as `holder.items.append(x)`)
  needs refined in step 1; which values exist on assumed paths (a continued call's result dropped by the `Never` operand; an update
  whose target or right side diverges reserves but never writes) needs one rule (PLAN); a custom setter's receiver is reserved only at
  the setter call, not from locate; a pair-layer argument (`PairAcquire`) is acquired at its Call, so it has no reservation interval.
  Rejected: a builder field marking the current input (hidden state); a second copy of the Borrow row in `Operand`; a writeback
  through setters now (the receiver would have to be acquired once at locate and passed to the setter: F8's unit 2d); the validator
  accepting every Type with a `Never` part (an open family; `Agree` also decides the builder's layers). Not temporary.
  The `RawSlice` backing and C8 landed later (R4-M40, R4-M41).
- **R4-M38** (2026-10-17; design A3 §4.2, owner approval of P5 and of its §9 item 8 on 2026-10-17; its `(Field, Write)` list and
  `bool` superseded by R4-M89 and R4-M91 (unit 2b-1: the key row per view, classes as R4-M74 OQ6)) A4's static keys for A5.
  `MirEffects.Keys(body, view, block, out unknown)`: the mutable static Fields a Call or a Drop may access through what it runs, each
  once and written where any access writes it, from the summaries the last `Run` solved; `unknown` where A4 does not classify what it
  runs (an Unknown effect, an atom left abstract under the body's premises, resolved at once as for an obligation, a Limit, or a
  dependent of an error). Under P5 every key of one Field is one key, so a key is the Field's `BindingSymbol`, never substituted, and
  keys read no class or atom: they stay out of the worklist. `Local` records each node's local keys; after the fixed point `Spread`
  joins them with those of every node the node read, as its dependencies record them (read sets only grow), once per strongly
  connected component of the read graph (an iterative Tarjan walk), into a bitset per node over the program's mutable static Fields
  (read and write bits). A query re-derives the site's terminator inputs as `Local` records them (the dataflow begun with the body)
  and composes them. `MirUses` names the static of a static effect in its `site` argument, and an exclusive borrow of a static is its
  write, so a writeback's reservation (R4-M37) is the write key and A4 classifies no access itself. A `staticEnsure` adds no key of
  its own static; its initializer's keys compose through its edge, which A5's effect rule (step 2b) refines if needed. Bound: the
  evaluations keep V16's linear bound; `Spread` is Θ((nodes + dependencies) × ⌈2F/64⌉) for F mutable static Fields and skipped
  without them, and the keys are nodes × ⌈2F/64⌉ words, quadratic where F grows with the program; a query costs its terminator's
  local facts and their composition. Under P6 there are no per-result anchors. Open, for the planning session before step 2b:
  `unknown` merges Unknown, Limit and Derived, which A5's effect rule must tell apart (a Limit is `Unsupported_Kd`, a Derived site
  reports nothing, R4-M10), so A3 §4.2's `bool` should become the classes; the key storage needs a measured check on a large
  program (PLAN G93). Rejected: keys grown inside the worklist (a chain whose callers come first gains one key per pass: 526,063 evaluations
  at N = 1,024 against 3,310); a stored record per site (+36–60% cold A4 allocation in `SummariesScaleLinearly` against +3–6% for
  the query); open keys and owner Type arguments on `MirStatic` (needed only without P5); a key list per node (union and growth
  checks per element); A4 re-deriving reads and writes from access flags (a second copy of MirUses' rule). Not temporary.
- **R4-M39** (owner: adopted on 2026-10-17, 「S1採用します」; its SPEC revision landed at `4372b058`; its ending rule, the base
  before the last dereference, is superseded by R4-M52) The A5 plan's S1,
  [Loan Identity by Base](../../draft/Proposals/2026-10-16%20Loan%20Identity%20by%20Base.md) as revised in `61b1abf2`, which states
  the additions from design A3 that the owner approved on 2026-10-17: a Loan is identified by its Borrow site and Semantics case, its
  Place named by its access path; a child Loan's base is the Place before the last borrow dereference of its path, and destroying in
  place (`=`, a storage end or a `replace` target) a Move Path that holds the base, with no borrow dereference between them, ends it,
  while a base reached through a reference never ends this way; a Move or a Borrow of a Place, shared Borrows included, reaches the
  Loans formed through the references stored in it (A3's P3); a Loan is checked where a value that carries it may be used, never
  excused by its identity. Per plan §5.3 its SPEC revision (§3.5.1, §13.5.5, §15.6, §15.6.2, §15.6.3, §15.6.6 and Appendix E) lands
  before the R4 exit. Until then a program valid under the current SPEC that S1 rejects is a located `Unsupported_Kd`, which becomes
  the Language code when the SPEC adopts the rule, and tests of the affected SPEC examples expect `Unsupported_Kd`; nothing is
  published before R6-check (shadow mode), and a library or milestone program that S1 rejects goes to O3. Not an integration: SPEC
  and INTEGRATED change only at that revision, SPECJ only on an explicit translation update, and the proposal stays open. Its
  companion S2 ([Preserves Results by Receiver Place](../../draft/Proposals/2026-10-16%20Preserves%20Results%20by%20Receiver%20Place.md))
  is not decided here.
- **R4-M40** (2026-10-17; design A3 §4.1's Slice row, owner approval of its §9 item 8 on 2026-10-17, implemented on the planning
  session's instruction of the same day; its handle copy and Open amended by A3 §12 E10, G123 and G94, recorded with
  their units) `RawSlice` takes a third operand, its backing, what the Slice retains under A5's intrinsic
  retention (every operand to the result; R4-M63: the intrinsic table's row for it), so a Slice cannot outlive or alias a write
  of what it views, which A3 found otherwise
  accepted. A range selection's backing follows SPEC 4.8.1: an array (fixed or `Array`) lends its whole Place, a shared borrow made
  with the call (`Borrow`); a Slice receiver's handle is copied first where it is located (SPEC 4.7.4), its length, element address
  and backing all read from that copy, so the reslice retains its source and borrows no handle variable. A written
  `Raw.slice(storage, length)` has nothing to back it (SPEC 5.6: the caller's obligation, a fresh anchor for its result), so its
  backing is Unit, added by `Extra`, the builder's one table of the operands an intrinsic takes beyond a written call's arguments
  (Fail's code is its other row). The backing is the last operand, not A3's first, so a written call's arguments keep their
  parameters' positions. R5's code generation ignores it. Open (PLAN G94): the backing is formed after the boundaries are
  evaluated, so a write inside a boundary meets no Loan (SPEC 4.7.4), as element indexing and updates do. Rejected: a SPEC and
  library revision giving `Raw.slice` a backing parameter (SPEC 5.6's fresh anchor already says nothing backs it); a second
  `IntrinsicId` for range selections (an O1 vocabulary entry); an arity of 2 or 3 (two forms of one row); the storage pointer passed
  twice by a written call (a raw pointer carries no Loan, and the copy would read as a backing). Not temporary.
- **R4-M41** (2026-10-17; design A3 §4.1's C8 row, owner approval of its §9 item 8 on 2026-10-17) A Value callee's `MirCallee.Arg` is
  the Callable signature its callee declares (`CallPlan.DeclaredSignature`), a TypeToken constant of the body, which A3's
  `Retains(callee or Callable signature, Subst)` (step 1a) reads over the signature's own Origins (SPEC 15.6.4: fresh regions for its
  abstract Origins), not the call-solved `Signature`, whose binder Origins are the arguments'. A default template's copy folds it like
  its other constants, the validator requires it (a Value callee without it is a `Callee` finding), and the dump shows it. A Virtual
  callee is named by its slot's function under its Subst, so `MirCallee`'s doc no longer claims a slot in `Arg`, which the builder
  never set, and the dump drops its `slot 0`. Open: the validator still checks a Value call's arguments against its operand's
  Function Type and skips Closures and function items; the published signature could be its one source. Rejected: a `BoundType?`
  field on `MirCallee` (a field for one callee kind on every terminator); the operand's Type (a Closure's or a function item's Type
  is not its signature). Not temporary.
- **R4-M42** (2026-10-17; PLAN G90, implemented on the planning session's instruction of the same day) A fact Binding publishes for
  a node that the builder finds missing is a broken HIR contract: `MirBuilder.Broken` records `InternalInvariant_Kd` at the node in
  `MirProgram.Records`, beside the Unsupported and expansion-limit records, and puts an Opaque value in its place, so the body stays
  valid and generation is refused; a node Binding rejected has its error instead, which the value is derived from, as for
  `Unsupported`. A call's plan (`Call`) and a subscript's selected call (`Referent`) are read so instead of being trusted with `!`,
  and an index's expansion without its selected call no longer falls back to the index's first published call (the indexer the
  HIR publish left unselected). `ExpansionPlan.Of(...)!` for a compound update, an index or an interpolation stays, since the accessor builds a plan
  for those Ops. Open (PLAN G96): more facts are trusted the same way (Formatting's `CallPlan.Of(...)!`, four sites; `Folded!`,
  `HirTables.Symbol(...)!`, `BoundClosure.Of(...)!`, an `ExplicitAdaptationPlan` cast, `ArgumentOperations[...].Source!` and
  `TypeArguments[0]!`); the shared-structure option is a check owned by the HIR producer at publish that each Op has its facts
  (an index's Op Call its selected call, a ConstIndex its folded position), after which the builder trusts them; the planning
  session decides. Rejected: a `MirViolation` (the validator verifies MIR; the broken contract is HIR's); an exception (AGENTS: a
  located internal invariant without recovery); a code parameter on `Unsupported` (one function steered by a parameter for two
  contracts). Not temporary. Its detection in the builder (`Broken`) is superseded by R4-M61: the publisher checks the facts.
- **R4-M50** (2026-10-17; the A5 plan's §9.2 under OD5) A test that expects a rule an adopted SPEC revision replaced carries
  `[Trait("OldRule", "<id>")]`, the id naming the revision's item as `draft/INTEGRATED.md` maps it (S1, S4, P1–P11, a, b),
  with a comment naming the affected rows. It marks what the default pipeline still decides; each such test migrates to the
  revised rule, dropping the trait, when MIR decides acceptance. Rejected: changing the expectations now (the default
  pipeline decides acceptance until R6-check); splitting mixed theories (churn for a marker); per-row markers (xUnit traits
  are per method). Temporary: removed at R6-check.
- **R4-M51** (2026-10-17; design A3 §4.3's C1+C8 view and §4.1's In place row, owner approval of its §9 items 8 and 9 on
  2026-10-17; A5 step 1, unit 1a, redone after its review) Call retention, the Binding view A5's Loan flow reads for calls
  (SPEC 15.6.4). `TyCtxt.Retains(callee, Subst, definition)` and `TyCtxt.Retains(calleeType, signature, definition)` (a Value
  callee's Type and declared Callable signature, R4-M41; `definition` is the calling body's, null at top level) give an
  immutable `CallRetention`: rows `Retention(Input, Kind, Output)` over the parameter slots `TyCtxt.Signature` numbers (Output
  `Retention.Result`, or j for a store into input j's writable storage) and `Untied`. Rule: an output retains input i exactly
  when the callee's body may move a value of i there under the premises it is checked under. The own part is instantiated:
  the Subst's Types, `Self` and declaring Type only, so the callee's Origins stay fresh regions (step 1) and retention is per
  argument; its inputs' and declaring Type's R2 (`AddTypePremises`), its result's R3 (`AddResultPremises`) and its clauses
  through `SubstituteClauseOrigin`. The enclosing part is the catalog's own walk, `ExtractOriginPremises` from the binder's
  parent outward (`callee.Accessor?.Binder ?? callee`; for C8 a Closure's or function item's declaration,
  `Binding.SignatureOwner`), after `CollectOriginResolvers`. Erased callables (owner decision 2026-10-17, DECISIONS R4-M52):
  every callable whose implementation the call cannot see, a Function Type value or a value of a Callable-bounded Type
  parameter with no concrete body (C8), each callable Type inside a C1 or C8 contract and each Callable bound of the
  callee's premises (`Binding.CallableBoundSignatures`, instantiated by the call's Subst), relates each fixed (non-static,
  non-per-call) Origin of its inputs to each fixed Origin of its outputs, its result and the storage it may write
  (`AddErasedRelations`), since its implementation was compared under premises no view reads (SPEC 15.6.4: an unproven
  absence of retention never permits access); its per-call Origins relate only by entailment. An abstract carrier's hidden
  Origins are fixed Origins for this rule (planning session 2026-10-17, review r3 B3: A3 §4.3 retains "with abstract
  carriers" and R4-M36 counts an abstract Type as hiding Origins). An erased C8 call reads no
  declaration's premises, only its signature's well-formedness, so its key is the calling definition's constraint scope
  (`DefinitionUse`), which decides only its pair layers' admitted cases; a Closure's or item's key is its declaration, so
  two computations reading different premises never share a key (review r2). This closes the Open on binder-null calls. An
  instance with an abstract part is normalized under the calling definition's premise scope (`EffectPremises`, read by
  `CallableContractType`), so a requirement on an abstract Self reads `I.Item`, not `I.LentItem(step)`, under `I is
  Iterator` (SPEC 22.2.3's `nextPair` stays valid); the C1 key is (callee, types-only Subst, that scope), and a concrete
  instance reads alike everywhere. Entailment is one `OriginPremiseClosure.Close` per output atom,
  every Semantics-case edge followed; its work is what it reaches, so the total is output-sensitive (rows plus closures).
  An abstract carrier (a Type parameter or pair target) has one unnamed closure node per computation
  (`OriginPremiseClosure.Node()`, nodes no Origin expression names), the Origins its instances hide, which outlive the
  layer above each occurrence (R2); occurrences share it, which replaces the identity scan (`MarkCarrier`) and its
  closure branch, and `AddErasedRelations` relates it like any fixed Origin.
  Kind: Value through anything but a borrowed input's referent, Content through that referent only (step 4); a borrowed
  input is a ref, uniq, objref or objuniq layer, a pair layer admitting only those (BU-3), a Slice or a UniqSlice
  (`BorrowReferent`, also the layer above a carrier;
  SPEC 4.8.1, 4.8.2: `toArray` keeps the dependencies inside `T` but not the outer borrow; A5 1a3). Stores:
  invariant positions (`ComponentVariance`) reached through storage the call may write: not past a shared layer unless a
  raw pointer is followed again (SPEC 5.2.1), nor past a non-exclusive pair layer, nor in a callable Type (BU-1 corrects
  the cut below a writable invariant position). Untied: a
  non-static result atom or carrier no input reaches. Caller relations among the caller's own Origins add no row. The memo
  lives until an edit: passes with no edit reuse its rows, so a warm rebuild allocates nothing, and `Binding.Invalidate`
  discards it at every edit, whether or not a pass finished, as it does the item contexts (A5 1a2; rejected:
  per-declaration invalidation, a dependency table only this view would use, and pooled recomputation per pass). A
  contract Binding's storage instantiation does not complete under the Subst (storage nested past its depth bound,
  which a growing recursive Type of SPEC 15.3.5 always reaches) gives `CallRetention.Limit` (`IsLimit`; no rows, and
  reading its `Rows` or `Untied` is a broken contract), memoized like any retention; A5 records a located
  `Unsupported_Kd` at the call, as A4 does for `Apply`'s null (A5 1a4; rejected: the exception it threw, every
  argument retained). The catalog's `PublishedRow` gains `InPlace`: `Replace` (`Destroys: 0, InPlace`) for `replace`,
  `Target` for the `clear`s (relaxation (c) not adopted); `TyCtxt.Published` adds it and A4 reads `Destroys` unchanged.
  Rejected: a graph of its own and premises only from the named declarations (unsound: a nested function's body uses its
  enclosing function's well-formedness, review B1); `Relate` (no two Types are fitted); `TyCtxt.Callable` (R4-M41 publishes
  the signature); the call-solved Origins (per Origin); rows per declaration without the Subst (review r1 finding 9); only
  top-level exclusive inputs as stores; an erased callable read under its writing site's or calling definition's premises
  (unsound, review r2 B2: its implementation was compared where it was converted); a located `Unsupported_Kd` for such
  calls (review r2 option 2). Open: the Callable bounds are all of the callee's premise scope, not only those of carriers
  its contract names (a sound superset); a C8 Closure's or item's bounds are read as declared, not instantiated by an item
  instance's Type arguments as C1's are (latent: Binding rejects such an item value, `UnprovenConstraint_Kd`, review r3); a
  pair-layer input admitting a Semantics other than a borrow (owner, an object owner or raw) gives Value through its target
  even in its borrow cases (narrowed by BU-3; step 4 would give Content), as do the other
  Loan-holding views (the internal Storage remainders; WriteWindow, Utf8Writer and FixedBuffer, whose `u8` holds no
  Origin), a sound over-approximation; A5 reads the view once per pass, its warm cost unmeasured. Not temporary.
- **R4-M60** (2026-10-17, tooling session; PLAN G111; refines R4-M50 for corpus rows) A corpus row that expects a rule an
  adopted SPEC revision replaced states the SPEC's verdict and, in an `Old` entry, what the default pipeline decides until
  R6-check, as `spec-cases.json` states a path's differences beside the SPEC's records (R4-M45); it is that table's class
  (d), named by the revision item instead of a class and clause. In `tests/diagnostics/pair-cases.json`, `Old` names the item
  as R4-M50's trait does (`Rule`) and holds the old verdicts of the Semantics cases that differ (`Expected`) and the old
  native run (`Run`, absent when it is the row's; a row whose program the SPEC rejects has none). `GenericOwnershipCaseTest`
  judges and runs such a row by its `Old` values, `Old` being the row's marker, and checks that `Old` names its item and
  changes verdicts of the row's cases, and that an overridden SPEC rejection is located in the source. First row:
  `B12-parent-move` (S1, SPEC 15.6.3: its `uniq` case Moves the base `value` while the child `r` is live). The SPEC verdicts
  that `Old` overrides are otherwise unchecked until the corpus is checked on the MIR (A5) or the old path is deleted. Rejected: the
  trait on the whole theories (it marks every other row); a per-row trait (`TheoryDataRow.WithTrait`: xUnit v3 does not
  pre-enumerate theories by default, so a row's trait reaches neither discovery, `-trait` selection nor the result file);
  the old verdict in `Expected` with a `Mir` entry (the corpora state the SPEC in `Expected`); `Run` on the row (the SPEC
  rejects its program). Temporary: removed at R6-check with R4-M50's traits.
- **R4-M52** (owner, 2026-10-17, choosing the recommended option of each of the planning session's questions on A5 step
  1: 「(b2) 折り畳み」, 「assumed completion + α」, 「(c) base なしで採用」, 「別セッションで実施」, and for unit 1a
  「固定 Origin を関係ありと扱う」; supersedes R4-M39's ending rule and parts of R4-M37) The choices of
  `draft/Sketches/A5 Step 1/choice-1b.md` §4, recorded as design A3 §9 item 11 and §12 E8–E9, from two independent
  designs per part, the case 23a proposal's independent review and a design review; prototype round 10 (246 cases) has
  no unsound acceptance and no unlisted rejection.
  - **Loan ending** (the [case 23a proposal](../../draft/Changes/2026-10-17%20Loan%20Termination%20Across%20Borrow%20Dereferences.md),
    option (c)): destroying in place a Move Path ends every Loan whose access path it exactly prefixes (A3 E8). It replaces
    R4-M39's base before the last dereference and its ending; S1's other requirements stand. Recovers 23a, 30d, 34c, 35a,
    35b; census (grep proxies): library, milestones and xUnit literals 0 verdict changes. Conditions from the review: the
    reservation-route check (A3 E1) and the guard-local marker (E7) land with or before it, and the SPEC revision amends the
    proposal's safety argument and maps SPEC 13.5.5's pair-layer cell. Not an integration: SPEC and INTEGRATED change at
    that revision. Rejected: keeping the last base (23a, 30d, 35a, 35b and 11 review cases stay losses); the term "base"
    (option (b), the same verdicts with one more concept).
  - **G92b, the fold:** a written exclusive borrow that an invocation's preparation path continues through is not formed;
    the path's Place is the Place it lends, so each input has one reservation at its lending point, as `holder.items.append(x)`
    (SPEC 15.6.7: "the same call"), on a shared input's path too. Supersedes R4-M37 G5's path clause and its Open note on a
    parent reservation's mode. Rejected: an A5 reservation chain (≈ +25 lines and a chain fact for G119; it rejects
    `let r = holder.count@ref; holder@uniq.items.append(1); inspect(r)`). Corrected by the planning session on
    2026-10-17: on a reserved input's path only; a written exclusive borrow on a shared input's path stays a live
    exclusive Borrow (SPEC 7.3.3; the planning session's earlier reading, review N7, contradicted it).
  - **G92a, assumed completion:** on a check-only continuation every node is built and makes its own value and accesses; a
    node of Type Never has no value, its consumer taking an unwritten Never temp that fits any operand position; a
    placement rvalue that has an input and whose every input is that value is not built, and the old value at `=` is then
    not dropped either (`Replace` takes a Never-typed temp when its right side has Type Never), so `x = stop()` leaves x
    unwritten, as today; a Call is still built with its Never operands. Prerequisite α: Binding gives a Never element of a
    literal its expected Type. Supersedes R4-M37 G6's rule that a node which does not complete assigns no value (`Set`),
    with its `Never` operands, and its validator exception (a Never operand's Type is unknown at any operand position).
    Rejected: keeping `Reached` with a located `Unsupported_Kd` until α.
  - **SPEC revision** (the owner's explicit instruction): P12, the Loan ending above and the reported gaps (SPEC 14.10.3's
    Never placement, 15.6.7's overlapping arguments read through Loans, the fold on a shared input's path) go to a
    separate session with its own review. Until then A5 runs in shadow and publishes nothing.
  - **Retention through an erased callable** (unit 1a, `TyCtxt.Retains`; asked after its second review round found
    missing premises again, with that round's three reproducers): a callable whose implementation a call cannot see, a
    Function Type value (compared at its conversion site under premises the call does not see), a Callable-bound value
    with no concrete body, and each callable Type inside a contract (or Callable bound of a carrier it names), relates
    every fixed (non-static, non-per-call) Origin at an input position to every fixed Origin at an output position, its
    result or the writable storage it may write (SPEC 15.6.4: an unproven absence of retention never permits access);
    per-call Origins are related by the Function Type's own clauses only; Closures and function items keep their binder
    chain. Not a loss. Rejected: a hold with a located `Unsupported_Kd`. Census: library 0, milestones 0 (grep proxies).
  The implementing units record their contracts in the commits that first implement them, including the planning
  session's decisions of A3 §12 (E1–E7, E10, E11): E7 replaces R4-M37's `EndBorrow` meaning for guards (unit 1b-4), and
  G123 and G94 amend R4-M40's handle copy and its Open.
- **R4-M53** (2026-10-17; design A3 §6.1 and §8, the A5 representation its §9 item 9 records, approved by the owner on 2026-10-17;
  A5 step 1, unit 1b-1) A5's Loan sets. `MirLoanSets` owns interned immutable sets of the ids A5 numbers (Loans and markers), the
  only Loan-set form in Mir: a set is a node id, node 0 the empty set, and a node's elements are a strictly ascending run in one
  pooled array, interned by its elements (a dictionary over node ids that hashes and compares their runs; a candidate run is written
  after the last node's and dropped when an equal node exists), so equal sets have one id and a holder's state, a join and an
  equality test are an int. `Add(set, element)`, `Union(a, b)` (keyed unordered) and `Remove(set, removed)`, set \ removed, are
  each memoized by their operands, so repeating one costs a lookup. `Remove` takes a set because the Loans a point ends are one set
  removed from every holder's (A3 §5's `ended` and `step`), each holder after the first with an equal state a memo hit. `Contains`,
  `Count` and `Elements` read a node; `Bytes` counts the element bytes of the nodes made, A3 §6.2's ν, which `MirBorrows.Work`
  reports. `Clear` empties the storage per body view and keeps its capacity, so a warm run allocates nothing; the memory limit stays
  the dataflow's (R4-M15a). Its first reader is `MirBorrows` (unit 1b-2); `Map`, for disturbance (F6), comes with unit 2f.
  Review fix B3 (2026-10-18): `Range(first, count)`, memoized, interns a run of consecutive ids (the Loans a Move Path's destruction
  ends, R4-M57), and `Remove` scans its smaller operand, returning the set itself when it removes nothing.
  Rejected (A3 §3, §6.1): dense holder × Loan bits (cubic: heads × holders × Loans); a per-Loan traversal (a check walks every
  Loan); persistent tree sets (no int identity, an allocation per insertion); `Remove` by element (one call per ended Loan and
  holder) or by predicate (no memo key, so every holder's set is scanned at every point). Not temporary.
- **R4-M57** (2026-10-17; design A3 §3, §5, §6 and §8 with its §12 errata E3, E5, E6, E8 and E12; A5 step 1, unit 1b-2, on the
  planning session's instruction of the same day; its check sets amended by R4-M66: a borrow's stored value only seeding and 1b-3's
  CS_call over live_in(ℓ) superseded; its 1b-3 activation codes amended by R4-M68; its τ_s elements and 1b-4's γ element superseded by
  R4-M70; 1b-4's guard code settled and its merge's unit moved to 2a by R4-M72) A5's Loan stratum, its joint core. `MirBorrows`
  (`MirBorrows.cs`, the pre-pass; `.Liveness`, `.Flow`, `.Checks`) checks a body per Semantics case view, once for a body no view
  decides (as A1), and reports each conflict as `ComparisonLoanConflict_Kd` at its access with the Loan's anchor and its witness as
  facts: the witness holder's next use in its chain, or the store whose value carries the Loan. `MirReport` gains pooled fact rows
  per problem (`MirFact`, kinds `Anchor`, `Use`, `Store`; a merge keeps the first problem's, R4-M3a); A6's code mapping is unit 1c's.
  The choices within A3:
  - Holder slots: E is slot 0, then the flow holders, then the chain-local ones. A holder is a flow holder when MirMoveData makes its
    root a flow root or a direct Loan names it, since content, refs and the closure read a named holder's state at any location;
    A3 §3's rule by MirMoveData alone would read a chain-local state outside its chain.
  - Elements: Φ_E is 0, τ_s is 1 + s, Loan i follows the sites; a state slot is one word holding a MirLoanSets node, and a head
    stores E and the flow holders; `Prepare(⌈(Hf + 1)/64⌉, Hf + 1, …)` sizes both problems under R4-M15a's one limit.
  - Loans: one table per view, their Places' steps in one pool (a Base of depth d as d steps, an owner-case pair layer as none, a
    Slice index a shared borrow Deref), grouped by root key (locals, then statics); a PairAcquire the view makes a Reborrow is a
    Uniq Loan of its Place's referent. E8's ended set of each location is built in the pre-pass and removed from every slot after
    the location's checks and transfer (A3's `step`, σ_out \ ended; corrected by 1b-3: removing it first made `replace` store its
    new value through its already ended target reservation into E).
    Review fix B3 (2026-10-18, the planning session within V16): the Loans are ordered by group, the Move Path of their Place's longest
    inline prefix (`MirMoveData.Find`; statics after the paths), not by root key, so a destroyed path K ends the run of K's subtree
    [K, End), made once by `MirLoanSets.Range`; the comparison with every Loan of the root (a second copy of Move-Path identity, ν =
    Θ(B²)) is deleted. A destroyed Place is a Move Path only when all its steps are inline under the view (a reference-case
    PairLayer is a Deref there). A static has no Move Path, so its write ends none of its Loans: over-strict, never unsound, latent
    until P6's static borrows (unit 2b), which must give them an ending. (ended points) × holders stays a Dense term (A3 6.3).
  - A location's effect is planned as entries on the state (clear, replace, join), then committed, so the checks replay reads the
    plan on `σ_in`; a borrow's stored value only seeds the closure (E5), as the prototype's chosen rule (its parents as candidates,
    `RefParentsInTransit`, is over-strict on 30b). Transit counts a store into a holder live after ℓ, made live by the closure, or E.
  - Liveness: each location's touches (use, kill) are built once per view; the backward replay records each touch's live-after bit,
    and the checks replay rebuilds the live holders from a head's stored state (`MirDataflow.Before`, added) and those bits.
  - Calls read `TyCtxt.Retains` (slots to argument positions by `MirCallee.Position`); an intrinsic's operands all reach its result
    (superseded by R4-M63: an intrinsic reads its declaration, and every row set goes through one loop).
    Temporary: a default template's call is an unknown callee until F9 (unit 2e; superseded by R4-M93's P9 rows), and a Value
    callee's environment (its value, or what a reference to it refers to) reaches the result until F7 reads `TyCtxt.Closure` (unit 2c).
  - The driver's step 4 is not wired: `TyCtxt.Retains` discarded its memo with every Binding pass (R4-M51), so A5 in `MirAnalysis`
    allocated on each warm rebuild (Milestone34: 35,584 B per pass, 0 B without the retention calls), against V11's warm-rebuild
    tests; the memo now lives until an edit (R4-M51 as amended), and the wiring is unit 1b-5's.
  Not in this unit: `γ` (1b-4); effects (2b); F6 (2f). Rejected: per-location live rows (N × H/64 words, outside the one arena
  limit); a per-Loan holder index for `ended` (a second carrying representation); pruning dead holders at heads (the same verdicts;
  until measured). Not temporary, but the two call rules above.
  Unit 1b-3 (A3 §5's err(activate), err(destroy), `obs` and the A6 table's Loan rows, with E1, E5 and E11; `.Activations` added):
  - A reservation is a two-phase Loan in its reservation holder t, the slot it was issued into, the one holder where its mode is
    shared. It activates where a Call takes t as an argument or a statement writes through t's Deref (the writeback, R4-M37): the
    reservations σ_in(t) carries in t, each checked on CS_call(ℓ, t), the holders live into ℓ (the replay's live-after bits with ℓ's
    touches applied) but t, and E, closed, seeded by those σ_in(t)'s direct Loans name (E5), with no transit; as an exclusive borrow
    of its Place, so the arguments of one call meet each other there.
  - E1: a write or destruction (StorageDead, a shallow Drop, a destroying activation) reaches a Loan found in its reservation holder
    through any prefix of its Place, across borrow Derefs but not a raw one.
  - destroying(site) is a Loan column (`Destroying`), set in the pre-pass for the two-phase Loans issued into the target argument
    holder of a call whose published row destroys its target in place (`replace`): its Ref reads the target, its activation writes
    it, and err(destroy) checks the same CS_call for the Loans an exclusive deep use of the target hits and the write does not, where
    its case Type observes. A destroying row that is not in place (the `clear`s) adds nothing: its activation is already exclusive
    and deep (E3 leaves `obs` no shared cut).
  - A Drop's access is the one rule `Dropping`, which liveness reads too: `Observe` (exclusive and deep) where its case Type observes
    (`MirTypes.Observes`) and its A2 group's verdict is not Elide (no group: the case Type alone), `Unclassified` past the destruction
    walk's bound, a located `Unsupported_Kd` at a Drop of a borrowed root or a destroying call, as A4's Limit; else a shallow destroy.
  - Codes: a Loan found in its reservation holder gives `CallReservationConflict_Kd`, whatever the access; else an activation at a
    Call `CallActivationConflict_Kd`, at the writeback `PlacementActivationConflict_Kd`, any other conflict and err(destroy)
    `ComparisonLoanConflict_Kd`, so SPEC 15.6.7's `tasks.append(tasks.remove(0))` is a reservation conflict and 4.9.6's `values.sort()`
    with a live view an activation conflict, as the SPEC-example table records. `MirFactKind.Reservation` is an activation's or a
    destruction's reservation anchor (A3 §6.4's other anchor); a CS_call witness is its holder's first use from the call on.
  - ret(i, Value) reads through a borrow argument, a reference or a view handle (`MirTypes.View`: a Slice shared, a UniqSlice
    exclusive), the rule by which a Slice's Index is a borrow Deref (E6); found by the planning session's Milestone27 analysis, with
    Binding's matching `Retains` fix (unit 1a3).
  Found, for the planning session: a reservation formed while an exclusive Loan of its Place lives gets the formation's
  `ComparisonLoanConflict_Kd` and the activation's `CallActivationConflict_Kd` on one Loan (`arr.clear()` with a live element borrow;
  the old path reports the activation only), A6's merge or a rule; `TyCtxt.Retains` threw for a Subst holding a growing recursive
  Type (`Nest<ref/i32>.init`), now its `CallRetention.Limit` (1b-4). Rejected: transit in CS_call (a call's retained result would bring its own reservation into its
  check); activations by a table of issue sites (σ_in(t) already says which reservations reach the call); the destroying mode on the
  access instead of the Loan (formation and activation are separate locations). Not temporary.
  Unit 1b-4 (A3 §12 E7 and E12; it replaces R4-M37's `EndBorrow` meaning for guards, as `MirStatementKind.EndBorrow`'s doc now says):
  - γ: a guard candidate H's (`MirLocalRole.GuardCandidate`) guard-local Origin is a marker beside Φ_E and τ_s, element
    1 + (Opaque sites) + H, so the Loans follow every local's γ slot. A value assigned to H's whole Place carries it, issued with H
    (born there, in no state before), and every carrying rule carries it as it carries Loans; no ending removes it, and it never
    conflicts: a check notes the holder each marker is first seen in.
  - err(ended): at H's `EndBorrow`, γ_H in CS(ℓ) (the values live after ℓ but H, transit included, with E5's seed) is a borrow that
    escapes the guard: `ComparisonLoanConflict_Kd` at the cleanup, anchored at the candidate, with the holder's next use, as
    DIAGNOSTICS' escaped borrows (no guard-local code exists; A6's merge, unit 1c, finalizes it, also where γ reaches `_0`). Another
    `EndBorrow` (an abandoned comparison or reservation) checks nothing. E12: the stratum keeps no `partition` clause (1b-2).
  - A call whose retention is `CallRetention.Limit` (R4-M51: a contract Binding's storage instantiation does not complete) is a
    located `Unsupported_Kd` at the call in a body view with a Loan or a guard candidate (amended by unit 1c-3: such a view runs
    its checks, since E7's γ needs no Loan), as A4's Limit; it retains nothing, with no fallback rule.
  Found: a value returned from a guard (A3's 108) is Binding's `UnsatisfiedOriginRelation_Kd`, so its MIR is Opaque and the
  stratum sees no escape (the Origin stratum's, unit 2a). Rejected: the Loan-based err(ended) (prototype round 10's `EndedByLoan`,
  unsound on 43a, 92g, 102 and 108: past the Subject's shared Deref the candidate forms no Loan, E3); a γ id table per candidate
  (the local index is the key).
- **R4-M61** (2026-10-17; PLAN G96, two independent designs compared and the chosen one reviewed independently, implemented on the
  planning session's instruction of the same day; supersedes R4-M42's `MirBuilder.Broken`; its "which the builder replaces whole" and
  "its children are not published as evaluated and a closure inside it is not built" are superseded by R5-M63 for the children
  Binding resolved) A published Op guarantees the Binding
  facts the MIR builder reads for it, which `Binding.HasFacts` (`Binding/Binding.Hir.Facts.cs`, whose header is the contract) lists:
  every call the builder builds for it complete (each argument with its Source, a direct base call with its implementation's
  symbol), and per Op the call it builds (an index's selected call), the Evaluated argument of a key resolution and of an update's
  setter, an assignment's or update's accessor plans and, for an update with one standard accessor, the Property's symbol, an
  interpolation's formatting calls, a for loop's entry, step, iterator symbol and decomposition (a counted loop's binding symbols),
  a match's current plan, a dictionary literal's lookup, a closure's definition, a conversion's binding (a case adaptation's plan),
  a Tuple or array literal's Type and an enum Case construction's plan, and a declared local's and a name's symbol; an index's
  selected call is `Binding.SelectedIndexCall`, which `ExpansionPlan` reads too. A ConstIndex keeps its position on its own plan row
  (`Folded`), written with its PlaceShape, not on its literal's. The publisher gives a node the Op its form calls for and publishes
  it only with these facts; without them the node is `HirOp.Error`, which the builder replaces whole. Where Binding resolved that
  node and did not reject it (no failure, recovery or `#switch` without an arm), the contract is broken: the publisher lists the
  node in `HirTables.Broken`, and `MirBuilder.Build` records `InternalInvariant_Kd` at it in `MirProgram.Records`, which the mir
  pipeline publishes and which refuses generation; its Opaque value is derived (`Rejected`), so each such node is recorded once. A
  resolved form that no HirOp member describes keeps `Unset`, which the builder reports as `Unsupported_Kd` (HirOp's contract),
  instead of a silent Error. The builder trusts every listed fact: `MirBuilder.Broken` and its two sites and the builder's
  missing-fact fallbacks are deleted (a direct base call without its implementation and an update's setter without its Evaluated
  argument reported Unsupported; an interpolation, a for loop, a match and a dictionary literal without their plans were a silent
  Opaque; a key resolution without its Evaluated argument dropped the length; ConstElement walked to its literal; `Named` reported a
  Storage symbol without its Property as Unsupported). An Unresolved node lacking its facts is an Error now, so its children are not
  published as evaluated and a closure inside it is not built, as inside any Error (PLAN G121). Rejected: `Broken` at each builder
  site (an open family, and a resolved node published Error stayed silent); checking only the Op arms that test a plan (leaves
  symbols, Sources, Type arguments and the adaptation plan to faults); a validator walk after the publish (a node it turns into an
  Error keeps evaluated children); a `HirOp` member (40 of 40); a Binding failure written at publish (publishing decides nothing,
  `PublishIsInvisible`); non-nullable fact types now (R7's CallPlan and declaration-table scope). Lasting: `HasFacts` and
  `HirTables.Broken` are the publication contract; at R7 the publisher walk goes and Binding's inline Op writes call `HasFacts`.
- **R4-M54** (2026-10-17; PLAN G92, implementing R4-M52's fold (owner) on the planning session's instruction of the same day;
  its pair-layer input amended by R4-M66)
  On the preparation path of an input that starts a reservation (`MirBuilder.Place` with `input`: `Operand` keeps it only for an
  input the call takes as an exclusive reference, by its expected Type, or without one, a value call's callee, by the reference it is
  or its acquisition forms), a written exclusive borrow whose value the path follows (PlaceShape `Value`, `Layers > 0`; also in
  groups, `Lending`) is not formed: the path goes on from the Place the borrow lends (`Lent`, the Place half of `Ref`: the follow to
  its referent and its base view), followed by `Layers - 1`. Each such input then has one reservation, at its lending point:
  `holder@uniq.items.append(x)`, `(holder@uniq).items.append(x)`, `holder.items@uniq.append(x)` and `holder.items.append(x)` build
  the same MIR (SPEC 15.6.7: the same call). A written borrow that is the input itself (`take(p@uniq)`, `h.c@uniq.bump(1)`) stays
  its reservation. On the path of an input that starts none (a shared or a copied one: `inspect(holder@uniq.items)`) and off input
  paths (a `let`, an operator's operand, an update's target, `tasks@uniq.length`) a written `@uniq` is a live `Uniq` borrow (SPEC
  7.3.3: it lends its operand exclusively, then the path reads it; R4-M52's "on a shared input's path too" was corrected by the
  planning session). A pair-layer input, exclusive only by case, starts none here. Supersedes R4-M37 G5's "or on the Place path of a
  receiver or an argument down to a value"
  and its Open note on a parent reservation's mode: no reservation is formed through another reservation's holder, so A3's mode
  rule stands. Rejected: an A5 reservation chain (R4-M52); a parent link on the Ref (an O1 vocabulary entry for a fact the Place
  shows). Not temporary.
- **R4-M55** (2026-10-17; PLAN G123, the planning session's design A3 E10, implemented on its instruction of the same day;
  amended by R4-M66: also an operand out of operand order, captures, and every pair-layer acquisition secured and marked) An
  operand is read where it is evaluated (SPEC 12.2): a Place operand (Copied, Moved or acquired by case) of an operation is
  secured into a temp where it is evaluated (`Secure`, as a transfer's result is) when a later operand of the same operation builds
  code. `MirBuilder.Builds` decides that from the HIR: every operand builds code except a literal's constant and a Place read as it
  is (Copied, Moved or through its referent) whose path from a local is stored Fields, Derefs and constant positions. The rule
  covers a call's callee value, receiver and arguments in evaluation order (a pending slot already secures its operand), a Tuple,
  fixed-array or enum construction's operands, a built-in binary operator's left operand, an Array literal's elements, and the
  receiver of a built-in selection whose value the selection reads, a Slice handle or a raw pointer (`Handle`), before an index or
  boundary that builds code. So `f(x, do => x = 5)`, `(x, do => x = 5)`, `x + reset(x@uniq)`, `[x, reset(x@uniq)]`,
  `v[touch(v@uniq)]` and a pair-layer argument before a call read their Place where it is evaluated. Amends R4-M40: a Slice
  receiver's handle is copied first only where a boundary builds code, else read in place, and its backing is the handle's
  value (a Copy), never a borrow. A pair-layer argument that later code follows is acquired where it is evaluated, not at its Call
  (R4-M37's Open); whether its exclusive case is a reservation there is A5's reading of the acquisition (step 1b). A written
  `@move` already moved into its temp where evaluated. Rejected: every Place operand through a temp (more MIR for no change of
  what is read); a predicate on the forms that may write (an open family); securing the pending operands at a later operand's
  first statement (hidden builder state). Not temporary.
- **R4-M56** (2026-10-17; PLAN G94, the planning session's design A3 E10 with its option 2 on review N8, implemented on its
  instruction of the same day; amends R4-M40's backing placement and closes its Open) SPEC 4.7.4: a built-in selection locates and
  checks its receiver once, before the index. A receiver whose value the selection reads, a Slice handle or a raw pointer
  (`MirTypes.SelectsByValue`, the one fact the builder and A1 share), is G123's (R4-M55). Other receivers are storage, which the
  selection lends shared where it locates it: an element selection's guard, `Borrow(Ref)` of the receiver into a temp with the
  selection's SourceInfo, ended by `EndBorrow` once the index, key resolution or `^x` is evaluated and before the element's own
  access (read, write, borrow, reservation, writeback) is formed; a range selection's `RawSlice` backing, now formed before its
  boundaries. Contract (builder to A1, no vocabulary entry): every runtime `Index` the builder forms on storage has its guard (or
  backing) where its receiver is located, always, so the rule is uniform; a default template's copy keeps its guards; the Index's
  position local is located at the selection too. A1 checks the receiver's initialization and Moves at the guard and again at each
  access through a runtime `Index` of storage, both under the selection's subject (the access's from its position local), so
  R4-M3a's one problem per code and subject makes an access into an uninitialized or moved array one problem, at the selection,
  whatever consumes the element (before, at the consumer), and a receiver Moved while its own index is evaluated is still
  `MovedPlace_Kd` at the selection (SPEC 23.1), independent of the Move's conflict with the guard (A5, step 1b). The receiver of a
  built-in index is no reservation; an index update reserves the element after the index. ConstIndex evaluates nothing and has no
  guard. A transfer inside the index leaves the guard dead on its runtime edge, so no runtime `EndBorrow` is needed. Rejected: a
  consumer subject passed down `Place` and `Operand` (option 1: partial, over the parameter limits); duplicate problems (option 3);
  skipping the access's check (first implemented: it lost the Moved read after a Move inside the index); a guard only where the
  index builds code (a gate; MIR cost measured instead). Not temporary.
- **R4-M58** (2026-10-17; PLAN G91, the planning session's design A3 E10 in design A's form, with the A1–A4 audit's finding 3,
  implemented on its instruction of the same day; supersedes R4-M36's "The Destruction walk cuts no cycle through a sequence") The
  Destruction walk enters each Type once per query, by interned identity: `IMirDestruction.First(type)` at the walk's entry for every
  Type, the visitor's set (`AnyPart` and `Observer`: the caller's `seen`, A4's `Glue`: the evaluation's `glued`). A Type met again was
  walked already, so a repeating recursive Type ends where it repeats, through a sequence (`Tree` with `Array<Tree>`) or a handle
  alike, and a Type several parts share is walked once: the walk is linear in the distinct Types it meets (nested pairs 25 deep,
  2^25 leaves as a tree, meet 26). A growing Type is new at each level and still ends at the bound of 32 (`Nest` stays deep). The
  visitors' own cuts go (Observer's payload set, A4's `glued` check in `Destroy`). `MayNeedDrop(type, seen)` takes the caller's
  scratch (the builder's and A2's). `HoldsRaw(type, seen)` gets the same per-query cut as `HoldsLoans` (its caller
  `MirBuilder.StaticRoot` passes the builder's scratch), so a repeating recursive Type is not raw-holding for its repeat. A chain
  growing through a destructor's composed atom (`D<T>` destroying `T` as `G<(U, U)>`) terminates without A4 threading the walk's
  depth: Binding's storage instantiation bounds the growing instance (`PrepareInstantiatedStorage`, depth 64), so `TyCtxt.Apply`
  gives the composed atom no Type and A4 records Limit at the first composition. Rejected: an `Enter` on reported parts only
  (design B: a struct of scalars reports nothing, so the DAG walk stays exponential); a cached fact per interned Type (state outside
  the query, R7's); a nullable set parameter on `Destruction`. Not temporary.
- **R4-M59** (2026-10-18; owner decision, option C, on the MIR lane's confirming review B2 — the second round on the family "a
  Never element's Type in a literal", AGENTS) G92a builds every node on a check-only continuation (R4-M62) and relies on α
  (R4-M52). A literal typed before its position's Type is decided (an `if` branch under SPEC 14.9.2's common Type; a Case payload
  bound in `BindEnumConstruction`'s typed pass) keeps a Never component in its Binding Type, which `FitsTypeCore` lets fit another
  Type against SPEC 3.8; built, it reached a position of another Type and the validator reported `InternalInvariant_Kd` on the mir
  pipeline (`let t = if c => (a, a) else => (a, (return 1))`). MIR construction builds no Aggregate for an aggregate literal whose
  Binding Type holds a Never element, a Never at a position the literal builds (a Tuple's component or an array's element, through
  those kinds only), and records a located `Unsupported_Kd` with an Opaque value in its place, as for G103-E9a's fill literal; such
  a literal never completes at run time (an element diverges or reads a Never Place; an empty `[]` of element Type Never completes,
  but the old path already reports it `Unsupported_Kd`), so only check-only code loses its checks. A
  Never elsewhere in the Type is a declared Type, and the literal is built: a Function's result (`(a, h)` with `h: () -> Never`), a
  Type argument (`Option<Never>`). Removal condition: PLAN G129's Binding unit (a literal fitted to its position's Type once that
  is decided; `FitsTypeCore` no longer fits a Never component; Never's Copy proof). Rejected: fixing Binding before the merge
  (option A: the lane, the core lane and R5's W1 would wait, and it is best designed with G130's SPEC 10.5.1 unit); holding the
  lane until G130 (B); the validator copying Binding's Never leniency (a second copy of a rule); the builder re-typing the aggregate
  at its consumer (a consumer deciding a fact Binding publishes); a walk of every component with a test of the HIR completion
  (the planning session: the published Type alone decides, through the positions a literal builds). (G130 U5 removed its
  Case-payload cause, BU-2: a payload literal is completed at its decided payload Type.) Superseded by BU-9 (G129 U5).
- **R4-M62** (2026-10-17; PLAN G92, implementing R4-M52's G92a (owner) as `draft/Sketches/A5 Step 1/choice-1b.md` §2 and A3 E9
  state it, with design review r1's B2 and r2's F3 and F4, on the planning session's instruction of the same day; amends R4-M37
  G6) Assumed completion in MIR construction (SPEC 14.10.3, 3.8, 3.1.3). On a check-only continuation every node is built and
  makes its own value and accesses as at run time. A node of Binding Type Never has no value: its temp, the Never value, is never
  written, carries no Loan and fits any operand position: every acquisition takes it as it is (`Acquire`), so a comparison's
  borrow of it forms no `ref/Never`. `MirBuilder.Set`, now the one assignment (`Make` merged into it), builds nothing that has no
  value: an rvalue that has an input (an operand, or the Place a Ref, Len or Discriminant reads) whose every input is the Never
  value, or a value assigned to the Never value (a `while` whose condition diverges completes with none), but for the Opaque value
  standing for a rejected or unsupported node of Type Never. An increment's one is of the old value's Type, not of its target
  node's, which is Never where the index diverges (`++a[(return 0)]`). `Overwrite` neither drops nor places when the value is
  the Never value, and `Replace` secures a right side of Type Never into a Never temp, so `let x: i32 = stop()`, `x = stop()` and
  `b = stop(); consume(b@move)` stay as before (x unwritten, b neither dropped nor overwritten). A Call is built with its Never
  operands (`Reached` goes), except where a part would borrow the Never value, which has nothing to borrow: a runtime Type test's
  handle, a verification's message and `$tryWrite`'s writer. The non-completion gates go: Set's, a range selection's bounds check
  and `RawSlice`, a `^x` boundary's check, an Array literal's appends, `testObserve`, a formatted write's result test and
  `FormatFinish`. Edge structure stays the HIR Completion's: Transfer, Branch, Join, Loop, a body's end, a recursive default's
  call, an Opaque site and a block's Unit. The validator reads the Never value's Type as unknown at every operand position (Use,
  operators, Casts, aggregate components, call arguments, a Value callee), replacing R4-M37's call-argument exception; a
  `ref/Never` operand is still a finding. Binding's alpha (a Never element of a literal takes its expected Type) landed first. So
  a continued call's result reaches its consumer (`both(keep(p@uniq, (return 1)), p@uniq)`), an update whose right side
  diverges writes through its reservation, and a construction around a Never form keeps its operands' Moves and Loans; A4 counts
  the effects of the statements this builds (R4-M6a). Rejected: keeping `Reached` with a located `Unsupported_Kd` (owner); an
  A5-side rule over operands the MIR does not build; a diverging Call writing its destination on its check edge (SPEC 14.10.3
  leaves it open). Not temporary. Amended by R5-M38: the Never value is a temp of Type Never; an unadapted acquisition of a named
  Place of Type Never goes into a fresh Never temp, which every consumer takes as it is (the placement rule unchanged), and an
  adapted one is built as Binding publishes it.
- **R4-M63** (2026-10-18; A3 §12 E14, decided by the planning session on the core lane's group review B4 and N2 and its design review
  of 2026-10-18; supersedes R4-M57's intrinsic rule and its separate unknown-callee loop, and R4-M40's "every operand to the result";
  its store target refs(val(a_j)) superseded by R4-M73's W) A Call retains as its callee's contract says (SPEC 15.6, 15.6.4).
  - An Intrinsic `MirCallee`'s `Symbol` is the library declaration of the compiler function it serves, under its Subst, whose
    parameters its first operands pass (a written call's target, `Serve`'s function); it is null for an intrinsic without one,
    those the builder synthesizes and the compiler-only `$abort` (SPEC 17.4), whose results hold no Loan. `MirValidator` reports
    `Callee` for a Symbol that is no library declaration of a compiler function the intrinsic serves; that the table's arity covers
    the declaration's parameters is a table fact (`MirValidatorTest`).
  - A5 applies every row set through one loop, by operand position (`MirBorrows.Rows`, a reused scratch): `TyCtxt.Retains` over the
    declaration and Subst, an intrinsic's included; the intrinsic table's `Retained` column, the operand the builder adds beyond a
    declaration that the result retains (Value; `RawSlice`'s backing only); a default template's call the unknown-callee rows until
    F9 (2e; superseded by R4-M93: P9's Content rows, no store row): every input to the result (Value) and to each exclusive input's
    referents, untied. A store row into a raw pointer reaches E by the store rule, so `Raw.initialize(p, x@ref)` stores `L_x` into E: a later `x = 2` errs, and so does x's end unless a write
    ended the Loan first.
  - Census of the IntrinsicIds MIR builds. Declared: MakeObj, MakeRc and MakeArc (value to result), Clone (Content only),
    RawInitialize (value into the storage; the pointer's own row carries nothing), RawSlice (untied, with its backing row),
    RawAllocate (untied raw result), ErasedWriter (destination to result), RawRelease, TryAllocateBytes, MoveBytes,
    TestTempDirectory and the written Fails (nothing a Loan reaches). Without a declaration: Abort, the synthesized Fail,
    StaticEnsure, Capacity, FormatBuffer, FormatHint, FormatFinish, TestMessage, TestRequireAbort, TestObserve and TypeTest, whose
    results (Unit, Never, bool, integers, string, HeapBuffer) hold no Loan. Never built: Stride, DestroyValues, ReinterpretLoan,
    WriteStdout, FormatFloat, ErasedReserve, FormatInteger128, TestRecordValue, TestPhaseEnter and TestPhaseLeave.
  Rejected: an intrinsic row storing a writing input into E (a second copy of the contract its declaration states); keeping
  "every operand to the result" (wrong for `clone`, which retains its payload's Loans only, so `let c = clone(owner@ref)` then
  `_ = owner@move` was rejected); every operand beyond a declaration reaching the result by rule (the table names the one
  operand); giving `$abort` parameter symbols for `TyCtxt.Retains` (its Never result retains nothing). Not temporary.
- **R4-M64** (owner, 2026-10-17, O3, choosing the recommended option 「書き換え＋記録」) Milestone 27 line 100,
  `references.sliceUniq().slice().toArray()` followed by `references = [copied]`, is an error by SPEC 15.6 and 15.6.4 as
  integrated at `4372b058` (S1, design A3's loss P4): the temporary UniqSlice's holder carries its own exclusive Loan on
  `references` beside the elements' Loans, a read through the Slice carries both, and the copy keeps that Loan; rustc
  accepts it. The program is re-spelled as `references.slice().toArray()` (a specification re-spelling recorded in
  `tests/milestones/README.md`; the output and the section's intent are unchanged), and A3 §2's P4 census is corrected
  (milestones 1). `4372b058`'s "no verdict changes" claim was wrong for this line. Found by A5 unit 1b-2; analysis in
  `draft/Sketches/A5 Step 1/m27-analysis.md`, which also found an implementation defect handled separately (a Slice
  input is a borrowed input for retention, SPEC 4.8.2: Binding's part is A5 1a3, merged at `0e50b868`; the MIR side,
  `MirBorrows.Flow`, is unit 1b-3's). Rejected: a precision refinement narrowing P4 ("a view holder
  stores no elements": prototype 255 cases sound, but a SPEC revision and the full process for one program; kept as
  PLAN G127).
- **R4-M65** (owner, 2026-10-18, on the core lane's re-review V1, the second V16 round on the lane: a representation chosen
  over a 64 MiB cap on the Loan-set pool and over restating V16; supersedes R4-M20's ring worklist and R4-M34's backward ring)
  The MIR dataflow's worklist order. `MirDataflow`'s worklist is a bit per head; a solve starts with every head pending and
  takes the pending head first in its direction's order: reverse postorder forward (the heads' numbering from `Begin`),
  postorder backward. A head whose state grows is pending again and lowers the scan's start word, so growth along a loop's
  back edge settles the loop before the code after it is applied again; acyclic code still converges in one sweep. Cause: the
  FIFO ring queued a grown head at its tail, so growth from loop j reached loop k only after k − j rounds: N loops that each
  write x, borrow it into r and read r applied loop k Θ(k) times, each with r's set one Loan larger, Θ(N²) chain visits and
  Θ(N³) work and node bytes (720 MB at N = 1024; R4-M15a's arena limit does not bound `MirLoanSets`), against V16's
  (passes − 1) × locations (design A3 §6.3). The least fixed point of a monotone transfer does not depend on the order: A1, A2
  and A5 give the same results (their tests, the problems of the MIR goldens' and milestones' 16,444 bodies, and the
  difference ledger, 223 signatures and 2,383 rows before and after), and A4 reads only `Begin`. On that shape at N = 128 …
  1024, chain visits fall from 26,851 … 1,581,859 to 2,336 … 9,504, A5's work from 3.03M … 1.28G to 206k … 5.74M and its node
  bytes from 1.47 MB … 720 MB to 36 KB … 2.1 MB (r's k Loans in loop k, the output); A5 takes 0.2 s instead of 4.9 s at 1024.
  On that corpus chain visits fall by 0.07 % and A1–A4 and A5 time is unchanged within noise. Rejected: the cap (it stops a
  body whose cost is the order's defect; owner); restating V16 to admit passes × node sizes (owner); a priority queue (a log
  factor and storage for an index set a bitset scans); a FIFO ring that favors loop heads (two orders). Not temporary.
- **R4-M66** (2026-10-18; design A3 §12 E13, decided by the planning session on the core lane's group review B1 and B2 with the
  owner's choices of 2026-10-18: A5 orders operands (option R1) and, after the second design round, one rule for every access;
  three design reviews, the last accepting with wording changes; prototype round 13, 332 cases; amends R4-M55 and R4-M54,
  supersedes R4-M37's "a pair-layer argument (`PairAcquire`) is acquired at its Call") The values an access takes (SPEC 12.2, 15.6),
  and the operand order MIR gives them. Unit E13a, MIR (E13 (2), (4)):
  - Operand order: a Place operand is secured into a temp where it is evaluated also when an operand evaluated after it precedes
    it in the operation's operand order: a named argument, a constructor's included (SPEC 10.1.1), an argument before an unbound
    reference's receiver supplied after it, which `MirCallee.Position` passes first (7.3.4), and a setter's value (13.7.1); `Call`
    keeps the least position of the arguments evaluated after each (`leastAfter`, a stack the nested calls share). A Closure's
    captures take G123's rule: a capture before one whose acquisition builds code (`Reads`, shared with `Builds`) is secured. Case
    payloads are positional (6.5.1). Evaluation order is a source fact the MIR does not record, so this is a lowering contract like
    R4-M55's, which MirValidator cannot check (MirBuildTest, `tests/mir/order`).
  - Pair-layer acquisitions: a PairAcquire is always the whole rvalue of an Assign to a local's whole Place (a temp, a local such
    as `var v = p`, or `_0`); where it is an operand of another operation (a call, an aggregate, a capture) it is secured into its
    own temp. Its Use's `Op` holds the `MirRefKind` of the Reborrow its exclusive case makes (`MirRvalue.Acquire`): TwoPhaseUniq
    where it produces an invocation's input, else Uniq. `Operand` tells the two facts apart: an input the call takes as a pair layer
    reserves where it is acquired, and its path is no preparation path, so R4-M54's "a pair-layer input, exclusive only by case,
    starts none here" gains: its own PairAcquire reserves in the exclusive case, and the fold still does not apply on its path. An
    Adapt's `Op` gains an input bit beside its shorthand bit (`MirRvalue.AdaptShorthand`, `AdaptInput`), which `MirCases.Adapt`
    reads instead of `input: false`; a default's copy re-acquires a PairAcquire by its marker, not with `input: false` (the same
    cause). The marker decides the reservation, not where the acquisition stands. No closed-vocabulary entry (`MirRefKind` gains no
    member). The validator admits a PairAcquire only as the operand of a Use assigned to a local's whole Place marked Uniq or
    TwoPhaseUniq, a marked Use only of a PairAcquire (`Operation` otherwise), and an Adapt's two bits (`UnknownKind` otherwise).
  - Rejected: R2's every Place operand through a temp (more MIR for no change of what is read, R4-M55's reason, which still
    holds); a reservation A5 derives from the MIR shape (prototype 134d's `ShapeReservation`, unsound); a quadratic scan of the later
    arguments per argument. Not temporary.
  Unit E13b, A5 (E13 (1), (3), (5), (6) and (4)'s A5 part; supersedes R4-M57's "a borrow's stored value only seeds" and its 1b-3
  CS_call over live_in(ℓ), and its "a Call takes t as an argument" reads "as an input"):
  - Each access at ℓ is checked on the closure of σ_in of the holders live after ℓ that ℓ does not overwrite and of E, with the
    values it takes: operand i's (MirUses's order, a Value callee first), the values of the operands up to it and the roots of the
    later ones; a reservation's activation in t and a destroying call's destroy, the operands' values but those found in t, seeded
    by σ_in(t), t left out (so `let r = y@ref; give(y@uniq, r@follow)` is valid); a Ref's or Reborrowing PairAcquire's borrow, its
    R2 parents within E3's bound whatever its destination's liveness, and E5's seed (without the parents prototype 41o, rf1 and rf2
    were accepted); an observing destruction, its value; a destination write, a store and an access that takes no value, transit
    and E5's seed as before. An operand's value is found under its root's holder, so a reservation there stays shared; Discriminant,
    Len and FakeRead take none; an Opaque site's accesses stay unchecked (R4-M10).
  - One gather per location (`MirBorrows.CheckSets`) builds every set: passes Based (the live holders, E), Stored (transit, E5),
    Parented, Observed, Seeded (each activation holder's seed), then each operand's value in operand order, so a Loan keeps the first
    operand that reaches it, and each operand's root in reverse, so it keeps the last; a pass reads a holder's state once. Candidates
    are per Loan and mode, tagged by their passes and listed by root, so an access scans its root's; an activation's set holds one the
    Based, Seeded or Valued passes found in a holder other than t (the first two such holders are kept). Its cost is the distinct
    (holder, node) entries by a per-location memo (PLAN G135, owner 2026-10-18): a pass takes a value once per holder it is found
    under, the Stored pass and the overwrite test read the location's entries listed by holder, the transfer and the checks read a
    set's content once per location (a call's rows each output's referents once), and a call reads an input's state only where a
    two-phase Loan is reserved.
  - Every reservation input activates at its Call (review B1, landed). A PairAcquire the view makes a Reborrow is issued at its
    Assign in its marker's mode (TwoPhaseUniq: a reservation in its temp; Uniq: live); a pair layer the view makes a reference or
    handle is a Deref step (review N3). A Loan found only through a value an access takes has the access's location as its Use
    witness; codes stay (unit 1c). Deleted: `loanOfOperand` and the operand-range `Reborrows`, the operands' own Loans in calls
    (`argOwns`), the activation's live-in holders (`LiveInto`) and its separate gather.
  - Not done: a call's effects, and the uniq mode of an operand's reservation at its own call's effects and cleanup (unit 2b; Binding
    rejects borrows of statics today). Found, for the planning session: `TyCtxt.Retains` gives a pair-layer input of an abstract
    Type (`inner11<s/T>(a: s/T, b: i32)`) a store row into its own referent (both name one carrier), so after
    `inner11(x@(s/T), peek(x@ref))` x carries its own Loan and x's observing end now errs (prototype 139c with a non-Copy T; the
    prototype and the old path accept it; with `T is Copy` it is valid); and 29b's s, whose new value carries p's Loan as its
    reborrow's parent (SPEC 15.6.3), errs at its own end too.
  - Rejected: a gather per access or per activation (quadratic in an operation's operands, E13 (5)); leaving out only t's
    reservations (a choice by Loan id, A3 §3). Not temporary.
- **R4-M67** (2026-10-18; planning session; R5's asks A-5 (3) and A-2; DECISIONS R5-M23 and `draft/Sketches/R5 Step 0/Q8-package.md`
  §3.6; units 1c-1 and 1c-2). A6's problems carry their requirement, and A2 publishes per-body completeness. `MirProblem` is
  `(Code, Requirement, Subject, Views)`. Each recording rule names its requirement: A1, A2 and A3 a constant each; A4 the obligation
  row's, which Binding's `EffectObligation` carries beside its code (`EffectBound`, `VirtualEffectBound`, `CallableEffectBound`; the
  analysis limit `Unsupported`); A5 `ComparisonLoanConflict` for its four Loan codes, `Unsupported` and `StorageLimit`.
  `MirReport.Add` keeps the key (code, subject) (superseded by R4-M71). A merge that names another requirement is a recording-rule
  defect, a contract break as in R4-M3a: it adds one `InternalInvariant_Kd` (`Ownership.Internal`) at the subject. There is no
  code-to-requirement table; this supersedes R4-M3a's naming clause, as R5-M23 records. `MirDropTable.ViewsOf(body)` gives the views
  A1 and A2 completed (R4-M15a): 0 past A1's arena limit or for a body not analyzed. A5's driver step reads it, and R5's C4a will. It
  replaces the driver's private `completed` array and amends R4-M6a's drop table. Rejected: the code-to-requirement table of the plan
  row (R5-M23's departure D7).
- **R4-M68** (2026-10-18; planning session after group 1c's two designs and their design review; A3 §5 err(activate), R4-M57 1b-3).
  An activation conflicts only with Loans whose mode is shared in the holder where it is found; an exclusive Loan that hits the
  reservation was met where the later Borrow was formed or activated: at the reservation's formation (`ComparisonLoanConflict_Kd`) or
  at its own formation or inner activation (`CallReservationConflict_Kd`), so each (Loan, reservation) is reported once (SPEC
  15.6.7). A destroying reservation's shallow write is treated the same way; err(destroy) keeps every mode. Representation: the
  conflicts relation's rows `Activate` and `ActivateInPlace`. Evidence: the prototype with the rule, 332 cases, 0 unsound, 16 cases
  lose only a duplicate; the corpus loses 54 CallActivation and 10 PlacementActivation records, each with a same-anchor survivor.
  Rejected: an A6 merge by (anchor, reservation), a merge after the fact; reporting at the activation only (unsound on 38j).
  Conditions for 2f: a Φ_c met at a receiver's formation is located at its later call (DIAGNOSTICS); a shared-receiver Φ_c takes the
  receiver's mode, or 2f reruns this rule's prototype variant with that case.
- **R4-M69** (2026-10-19; group 1c's owner-approved design, R4-M75, from the design review of 1c's two designs; unit 1c-6; its
  Inlined rule superseded for A5 by R4-M97 under R4-M90; A1/A2 at 2g-6, D15 staged). It
  supersedes R4-M10's placement of the PreparedInput mapping and amends its Inlined rule, and supersedes R4-M21's rejection of
  "DefaultArgumentMove decided in A1", whose reason, R4-M10's placement in A6, no longer holds: R5-M23 has each recording rule name its
  requirement, and `MirReport` has no Place and must not derive a requirement from a code.
  - A1's mapping: A1 codes a Move of a PreparedInput slot (`MirLocalRole.PreparedInput`) or of its owned contents
    `DefaultArgumentMove_Kd`, requirement `Ownership.DefaultArgumentMove` (SPEC 7.2.3: a default cannot Move that slot). The owned
    contents lie past the slot's Deref with no other Deref, no PairLayer the view makes a reference and no Index of a value (a Slice's
    or a raw pointer's); a Move past a borrow the slot's value holds keeps `StaticMovePathRequired_Kd`. A default's bare read of a
    preceding parameter becomes Binding's failure at R5's H1 (Q8-4, Q8-8); its MIR is then Opaque and has no Move, so the rule needs no
    condition.
  - R4-M10's Inlined rule, amended: a recording rule that finds a Language problem inside an Inlined scope adds `MirFactKind.Inlined`
    (the scope's call, from `MirBody.Inlining(scope)`), and `MirReport.Add` drops the problem: it is its template's check (SPEC 7.2.3).
    The recorders are A1's, A2's incomplete layer at a Drop and A5's Loan conflicts. Limit records found inside a copy (A5's
    `CallRetention.Limit` and the destruction walk's bound) get no Inlined fact and stay (SPEC 23.1.1, R4-M51): they depend on the
    concrete instantiation. MirLints needs nothing (the builder copies no MustNotComplete row), and A4's problems lie at Binding's rows.
  - Premise: a copy reaches its caller's state only through the prologue's shared borrows of the pending slots, formed at the call in
    the caller's scope, so a conflict with the caller is found at a caller-scope location (the design review's r01 and p15). A builder
    change that breaks this (W1, F9) revisits the rule. Mutable static storage is a second route: a default may write a group's `var`,
    and a caller's Loan on that static would be found at the copy's write (SPEC 15.6.4) and dropped, with no record in the template. It
    is unreachable while Binding rejects every borrow of group storage (P42, STATUS "Static members"), so Binding admitting such borrows
    (P42) also revisits the rule, then keeping a Loan conflict whose Loan lies outside the copy's Inlined scope (group 1c's final review,
    N1).
  - Rejected: the PreparedInput role on the copy's borrow temp (it changes the role's meaning, keeps the copy's duplicate, one problem
    per body under G125, and touches the held golden `tests/mir/defaults.mir`).
- **R4-M70** (2026-10-19; group 1c's owner-approved design, R4-M75, from the design review of 1c's two designs; unit 1c-7). For the
  Loan stratum it supersedes A3 §4's `val(Opaque s)`, A3 §5's τ_s sentence and R4-M57's τ_s elements, with 1b-4's γ element
  1 + (Opaque sites) + H, now 1 + H.
  - Opaque values (option P): an Opaque site's value is `{Φ_E}`; it carries none of its roots' Loans, and τ_s leaves the Loan sets.
    Every Opaque site rests on a recorded error (the builder makes one only for a node Binding rejected or an Unsupported form with its
    record), so a Loan problem found only through one never arises: R4-M10's "derived and dropped", by construction. A Loan another
    route reaches stays checked (SPEC 23.1.2; the design review's o7, j1 and op5). No `Opaque` or `Derived` fact kind and no drop rule
    in `MirReport.Add`; 2g gets no derived Loan records from Opaque sites, which DIAGNOSTICS suppresses beside their causes anyway.
  - Default taint (temporary; A5-F9, unit 2e, deletes it; deleted by R4-M90's R-a (template calls 2e-1, copies 2e-3); template calls by R4-M93,
    copies by R4-M96):
    a value a default's evaluation places into its pending slot D carries D's marker: a default template's Call result, or an inline copy's store into D, a pending slot outside the copy's Inlined scope (the
    slots of a call inside the copy lie inside it, and an explicit argument's slot is no default's). A Loan conflict whose Loan only
    sets with a taint hold in the access's check set ("every set") is A5's located `Unsupported_Kd` at D's source, the omitting call,
    requirement `Ownership.Unsupported`, with no fact, never a Language problem. A Loan that a set without a taint also holds stays the
    Language problem, and an access reports such a Loan before a tainted one. A holder that joins a tainted and a clean value is
    tainted (T2's join imprecision): its Language error becomes `Unsupported_Kd` until F9, accepted.
  - Representation (the `PendingSlot` row and the two tag sets deleted by R4-M96): the per-local marker range (element 1 + L for local L)
    has its contract as a role table, `MirBorrows.MarkerOf`: `GuardCandidate` → γ_H (E7), `PendingSlot` → the default taint. A candidate
    keeps its tags apart for the sets with and without a taint, so the check "found only through tainted sets" reads the table once and a later taint kind adds a row (G148's P13 interim,
    R4-M76). err(destroy) is the conflicts relation's row `ObservePast` (what a deep access hits and the destroying activation's write
    does not, in every mode), so it too reports a clean Loan before a tainted one; its own candidate loop, a second copy of the rule that
    let the first holder decide, is deleted (group 1c's final review, B1).
  - Rejected: the first-found witness set (it hides o7); T2 for τ_s (it hides j1); one taint per template Call (no element for a
    copy); a taint by role alone (it would taint an explicit argument's slot).
  - Limit: err(ended) reads γ_H without the taint check (the rule names Loan problems); the closure reads a holder a tainted set names
    clean too (R4-M73's default-taint records: right per value for an inline copy).
- **R4-M71** (2026-10-19; group 1c's owner-approved design, R4-M75, from the design review of 1c's two designs; unit 1c-8; PLAN G125
  and G157 (8)). It supersedes R4-M3a's and R4-M67's key (code, subject) and places R4-M11's merge across bodies at publication.
  - Representation: `MirProblem(Code, Requirement, Subject, Body, Views)`. `Body` is the ordinal of the body whose case views `Views`
    numbers, `MirProblem.NoBody` (−1) where no body describes the problem: A4's obligation rows, which lie at Binding's rows (a row an
    owing definition's own body judges per view keeps that body's view bits; A4's codes take no case fact). `MirReport` keys a problem
    by (code, subject, body), so a subject built into several bodies (a Field initializer in each constructor, the design review's
    r05; a limit record of an inline default copy and of its template) is one problem per body, each with its own body's views. Every
    recording rule sets it: A1, A2, A3 and A5 the body they analyze, A4 `NoBody`, and `MirReport`'s disagreement record its problem's.
    The ledger reads each problem (R5-M56), so such a subject has one MIR row per body. `MirProblem` grows from 24 to 32 bytes.
  - Publication: R4-M11's merge by (code, subject, Reason) happens across bodies where the problems are published, one record per
    (code, subject, Reason), a problem with facts at its subject; R4 publishes none (R4-M1), R5's H3, their first publisher, does it.
  - Location-only records (PLAN G157 (8); R5-M65's rule at R4's site): `Compilation.ReportAnalysis` publishes the MIR's records,
    codes without facts (construction records, validator findings and the checked set's records), keyed by their location through
    `DiagnosticCollection.ReportLocation`, so the records of a `for` expansion's synthesized siblings (one span, depth and kind) are one
    problem by the owner's merge of equal keys instead of an `UndefinedOrder` fault. No construction rule records at those siblings at
    check today; the fault was latent (R5's C0b1 review).
  - Reason: views number one body's case enumeration, so OR-ing two bodies' views loses which body's case a bit names (r05: G's two
    constructors, of two and nine cases, gave one problem with `0x1FF`).
  - Rejected: the Inlined rule alone (r05 has no Inlined scope); per-body view lists inside one problem (a list per problem for the
    same facts).
- **R4-M72** (2026-10-19; decided by the planning session on 2026-10-18, adopting the design review of 1c's two designs (its verdicts
  4 and 5 and its R4-M72 draft) with the 1c design, and by the owner through the step-2 plan's PD7 and PD11, adopted with the plan
  (R4-M74, OQ1); with group 1c's final review's records; recorded by unit 1c-9; A3 §12 E15). The A6 table of A5's Loan problems
  (DIAGNOSTICS, MIR Loan problems; it gains a row, R4-M90) and the placements group 1c left. It supersedes A3 §5's A6 rows for
  effects, `PreparedInput` roots
  and `ended`, A3 §6.1's view skip and the unit E7 and E12 name for their merge, R4-M10's placement of the Access and Borrow codes in
  A6 (completing R4-M69's) and R4-M57's 1b-4 statement that unit 1c does the E7/E12 merge, and settles R4-M57's guard code.
  - Effects (clarified by R4-M89: a Loan found only in a reservation holder that the call does not activate is
    `CallReservationConflict_Kd` (A6 row 1); implemented by R4-M92; "none
    arises" overlooked implicit static reservations, such as a receiver reserved while an argument's call runs, R4-M92's
    `counter.add(g())`): err(effect), a static or unknown-environment
    effect against a live Loan (SPEC 15.6.4), is `ComparisonLoanConflict_Kd` at
    the call or the Drop whose effects meet it (A4's keys are a Call's or a Drop's, `MirEffects.Keys`; an erased destruction's are
    unknown, SPEC 16.3.3), as the spec case `15.6.4-static-anchors` expects at its call; `CallEffectConflict_Kd`, whose catalog message
    and DIAGNOSTICS rule are the requirement-call rule's, is A5-F6's requirement-call effect Φ_c at the later call (SPEC 8.11.4). Both
    supersede A3 §5's Effect row, which coded every effect `CallEffectConflict_Kd`. No rule reports either on the MIR yet: unit 2b
    implements the first (none arises while no static Loan exists, since Binding rejects every borrow of group storage, PLAN G100) and
    2f the second, under R4-M68's condition for a Φ_c met at a receiver's formation.
  - Defaults (superseded by R4-M90; its producers are R4-M98's): `DefaultArgumentAccess_Kd` and `DefaultArgumentBorrow_Kd` have no
    Loan-stratum producer: in a
    template the preceding
    parameters a default reads are the template's own parameters, whose callers' Loans lie outside its body, and a copy reaches its
    pending slots only past the prologue's shared borrows, where no Loan forms (E3; R4-M69's premise). They are A5-F9's (unit 2e), as
    `DefaultArgumentMove_Kd` is A1's (R4-M69).
  - Guard: err(ended) (E7) is `ComparisonLoanConflict_Kd` at the candidate's cleanup, its `EndBorrow`, anchored at the candidate, as
    DIAGNOSTICS' escaped borrows; no guard-local code exists (A3 §9 item 9's DIAGNOSTICS record).
  - Merge: E7's and E12's merge (the `destroy` conflict on a Loan `_0` carries whose Borrow the Origin stratum refuted, and err(ended)
    where γ_H reaches `_0`) moves from unit 1c to 2a (2a-2, completed by 2g-5; the step-2 plan's PD11), since the Refuted relations it
    reads come from the Origin stratum (A5-F4). Restated by R4-M94 (2a-2).
  - View skip (unit 1c-3; one pre-pass flag since R4-M98 D5): `MirBorrows.Run` skips a body view only when it has no Loan and no guard
    candidate, since E7's γ needs no
    Loan; A3 §6.1's skip of a view without a Loan accepted a guard value escaping in a Loan-free view (the design review's r02, an
    unsound acceptance). The root flags only select the accesses checked.
  - Publication: R4-M71 places the merge across bodies by (code, subject, Reason) at publication. R4 publishes no MIR problem (R4-M1),
    so R4 asks that H3, the first publisher, does it, and hands it to H0 through AR-4 (the step-2 plan's X-3 list).
- **R4-M73** (2026-10-19; owner, on the independent review of two designs and its completion, PLAN G148, R4-M76; P13's interim by the
  owner after three review rounds, below; A3 §12 E16). A Call's store row reaches every holder its argument reaches: ret(i, κ) joins
  every holder of W(val(a_j)) (A3 E16, rule G: SPEC 15.6.4's letter, no SPEC change), with no exclusion. It supersedes R4-M63's store
  target, refs(val(a_j)) (A3 E14), and A3 §5's Calls line.
  - Representation: `MirTypes.HoldsLoans`' one walk, each Type once per mode, also gives what a call may write past a value
    (`MirWrites`): the guard, passed concretely (`Through`; a callable and a part past the bound count so) or only through a struct's or
    enum's Type argument, a pair layer or an abstract part (`ThroughArgument`), and for a value that holds no Loan whether a raw pointer
    past no reference leads to storage that may (`Raw`); no vocabulary entry. One function, `MirBorrows.Writes` beside `Refs`, serves
    Binding's rows, an intrinsic's declared rows and the unknown-callee rows, one W per output, its first pass from the holders that
    pass the guard concretely, its second from the ThroughArgument ones and what only they reach. A5-F7's capture stores (2c) and
    A5-F6's desc(c) stores (2f, G113) call it, and no refs-only store path remains.
  - P13's interim (owner R4-M76, PD30; until a step-2 unit makes the guard read each part, before R4's exit, else OQ9's list), the
    owner's body hold: a (body, view) is held when a Call's W stores a non-empty value into a target reached only past a ThroughArgument
    holder, and every Loan problem the held view owns (the four Loan codes, err(destroy), err(ended)) is A5's located `Unsupported_Kd` at
    its access (`Ownership.Unsupported`), never a Language problem. Complete by construction: A5 is intraprocedural, so a conflict that
    depends on such a store is found in the view whose Call makes it. It never accepts: it reclassifies problems already found.
    - The fact has one owner: it is set wherever `Writes` returns a target past its clean count with a non-empty value, for every store
      path (a Call's rows today; 2c's capture stores and 2f's desc(c) stores inherit it). It is set in the forward solve, never in the
      checks replay, reset per run, on the σ_in a Call plans with: the solve applies each reached chain last at its converged state, so
      the fact is a may-fact that includes the fixpoint's, and the replay reports with it known.
    - The grain is (body, view): a view's store changes only that view's states, `MirReport` keys a problem by (code, subject, body)
      so a Language problem and an `Unsupported_Kd` never merge, and the stratum runs each view's solve and replay before the next.
    - Every Loan-problem producer reports through `MirBorrows.Reports`' common `Report(code, anchor, via, at, reservation)`
      (`Checks.cs` at an access and at err(ended), `Activations.cs` at an activation and err(destroy)), so the hold is one rule there,
      and a future producer (err(effect), 2b; the E7/E12 merge, 2a-2; F6's Φ_c) must report through it too.
    - R4-M69 governs inline default copies (superseded by R4-M97: the hold applies to every failure a body owns, a copy's at the
      omitting call; the taint by R4-M96): a Loan problem inside an Inlined scope is
      its template's and dropped whatever the hold
      says; the hold reclassifies only problems the held view owns. R4-M70's default taint is unchanged (superseded by R4-M96: no taint;
      a copied conflict is the held caller's own, the hold's `Unsupported_Kd` at its access): a Loan only default-tainted
      sets hold stays the `Unsupported_Kd` at the omitting call, also in a held view (one record per problem).
    - Cost, accepted by the owner: a held view's Language problems that do not depend on the store become `Unsupported_Kd` too
      (MirBorrowsTest's cleanRoute append, g1's array end, hold<C>, p139d), in E and every holder alike, until the per-part guard.
      On the mir Functional ledger at the unit's merge (`b57ae7dc`, against dev `1a50cb79`), 82 (body, view)s in 77 sources are held
      and 10 library bodies in every compilation (view 0: Array's appendAll and initRepeating, Dictionary's indexUniq, tryInsert,
      insertOrReplace and tryGet, DictionaryStorage's removeEntry and findEntry, Slice's toArray and contains), none with a Loan
      problem. The hold makes 10 records `Unsupported_Kd`: 4 depend on the store (B6-for's `var arr` in its two variants, `count<S>`,
      MemberThroughLayersTest's `exclusive`) and 6, the cost, depend on none (W without the by-argument step reports them): B6-for's
      `v@follow = x` twice (G147 (8i)'s wrong record), MemberThroughLayersTest's `views = [a@uniq]` and `a@uniq`, and
      BlockEscapeLoanTest's two `show<C>` `let n` (classification a5-p13, a5-old-loans).
  - The owner's answers on the interim (verbatim): after the final review's B1 (the check's closure and err(ended) read no taint),
    「汚染を経路で追跡 (推奨)」 (route taint); after that round's review (a write through a reference and E5's seed pass none),
    「本体単位で保留 (推奨)」 (the body hold). Before them the planning session ruled (B) on the implementer's finding (taint every
    store past a ThroughArgument holder, not only a step along a shared Loan). Rejected: a taint per marker on shared steps only, on
    shared steps and Φ_E, or on every store (B), each leaving Language errors on routes the check does not follow; route taint
    (prototype round 16) and its every-step form (round 16's review's R16RouteT), the third round in one family; the per-part guard now
    (R4-M76 places it in step 2); accepting the missed routes as limits (a conservative Language error on a P13 program, which AGENTS'
    Diagnostics rule forbids). Prototype round 17: 425 cases, every conflict that depends on a by-argument store `Unsupported_Kd`, every
    view not held unchanged, every (Loan, access) rule G reports reported once; 26 held, 11 of them losing an independent Language
    error (draft/Sketches/A5 Step 2/body-hold.md).
  - Default-taint records (R17-N3; superseded by R4-M90 (2e-1 for template calls, 2e-3 for copies): R4-M93 and R4-M96): R4-M70's closure route is of
    the same class as its
    recorded err(ended) limit. An inline copy is
    precise and its result or slot is tied to the explicit argument by Origin, so its Language errors through that route are right per
    value (SPEC 15.6.4); template calls (recursive defaults) over-approximate, and every attempt to make a conservative error through one
    was rejected by Binding (unverified for an accepted recursive default).
  - Why rule G: `TyCtxt.Retention`'s Output j is input j's writable storage past every exclusive or raw layer and behind nominal
    Fields, which only A5's holders can map; SPEC 15.6.4 decides per argument, with 15.6.2's shared-layer cut read off the holder's Type.
    Rejected: Binding publishing a depth (unsound behind nominal and abstract Types, no contract for the unknown rows, a second storage
    walk); exclusive-mode descent (r14A-h: a `uniq` made from a shared input reaches its holder); designer a's self-exclusion X and
    designer b's route (dominator) exclusion, each with its SPEC sentence (unsound: s1, s1c, s4; r14A-q, s2e); no guard (Milestone13
    and Milestone29 err; R4-M12); GXS (X where the target's Type cannot refer into itself: sound in the prototype, recovers nothing of
    the library, milestones or goldens; PLAN candidate).
  - Losses: P13 (listed, A3 §2). The letter-consistent rejections (SPEC 15.6.4 per argument and E's "used after every point"; rustc
    and the old path accept) are no loss: a self-row that returns a route's Loan to its holder (twoLayersRead, p03), an exclusive
    iteration over Loan-holding elements, E as one node (L3r), route cycles at depth ≥ 3 (s2f).
  - Evidence (the review at `8a7632d9`; the unit): no A5 problem in the library, the 44 milestones or the 31 goldens (MirCensusTest;
    R4-M12 holds); corpus work +0.11 % and set bytes +0 % (review); N calls through a four-layer chain are dense, the walk the same
    order as A5's access scans on the states it reads (V16; MirBorrowsTest's shape: work 121596, 204840, 513216 at N = 32, 64, 128); a
    warm run allocates nothing.
  - Found, not this unit: rule G's raw clause misses Loans stored through raw pointers that W does not name (PLAN G160).
- **R4-M74 A5 step 2 plan** (owner, 2026-10-19, relayed by the R4 planning session; plan unit 2x-A0) The owner's answers to all
  thirteen questions of the [A5 Step 2 Implementation Plan](../../draft/Proposals/2026-10-18%20A5%20Step%202%20Implementation%20Plan.md),
  packet 2 (OQ7–OQ10, OQ13) before its triggers. Its unit and decision names (2x-T1, PD18) are used as there; each
  planning-session decision PD1–PD31 is recorded by the unit that first implements it (plan §7).
  - OQ1 (a): the plan is R4's formal plan for A5 step 2, A6 and R4's exit, with its exit gate (§5.4), which refines the A5
    plan's §8 exit (R4-M35); step 2's design records go to `draft/Sketches/A5 Step 2/`. VERIFICATION names the census (2x-T1)
    for every unit the plan marks +cen.
  - OQ2 (a): each family's design package (2a0-D, 2b-D, 2c-D, 2e-D, 2f-D, 2g-D) goes to the owner once, after its design review;
    its approval makes it the owner-approved design of DEC Q6 form 1 for its Binding views and form 2 for its group.
  - OQ3 (a), conditional: S2 ("Preserves Results by Receiver Place", revised by the 2f-S1 sketch) is adopted if prototype round
    2x-P6 shows it sound; otherwise (b): its conformance half first, and caller rule 3 stays a located `Unsupported_Kd` at R4's
    exit (an O4 item). The proposal's revision and SPEC integration are a separate session (2f-S2); until then PD30 applies.
  - OQ4 (a): when Binding binds receiver-dependent closure-call results (2c-5), the default pipeline keeps its verdict through a
    gate in the old Analysis at the same call (`Ownership.Unsupported`), a D5 exception deleted at R6-check.
  - OQ5 (i)(a), (ii), (iii)(a) ((iii) superseded by R4-M80): R4 exits with no blocking row and no open (b) row naming an R4 unit
    or R4 G issue; a (b) row naming another owner needs the owner's approval. The proof is §5.4's: the mir ledger at the exit
    head (0 open blocking rows, 0 unclassified signatures, Landed = 14 families with A5-F5's recorded (e) variants, A6's
    contract test), stand-in tests for the codes the ledger does not compare, the census and the final review. The restated kind
    as PD18 narrows it is adopted; 2x-T3 implements it and supersedes R4-M47's clause.
  - OQ6 (a): the departures from owner-approved texts are confirmed: A4's Keys classes replace A3 §4.2's `bool` (R4-M38); C1-F4's
    row tuple gains Equality, Source, Clause and destination Type (A3 §4.3); A5P §7's U8p is superseded by R4-M40's `RawSlice`
    backing operand; A5P §7's M39 Binding part is void. The fifth, E5's cause site on the problem instead of keys in `MirOpaque`
    (A3 §4.3), holds only once R5's planning session accepts H3's contract change (AR-7, at 2g-D's review).
  - OQ7 (a): a step-2 unit that rejects a library or milestone program (O3), or changes default-pipeline acceptance outside
    PLAN10 App. B as a SPEC-conformance fix whose commit names its clause (2a0-3b, 2c-1, 2x-E4a, 2x-E6, 2x-E7; 2a0-3a only
    under OQ11 (a)), takes SPEC's letter with a re-spelling of the program (R4-M64's pattern).
  - OQ8 (a): a new loss from a prototype round, or a reading that needs a SPEC revision, is approved as a SPEC clause,
    integrated in one SPEC session per family before the unit that reports its Language code; until then PD30 applies. The
    owner's G148 answer (PLAN G148) applies it to losses with no library or milestone use; any other is O3 (P13: R4-M76).
  - OQ9 (a): at X-2 the SPEC rules left as a located `Unsupported_Kd` at R4's exit and the removal stage of each remaining MIR
    `Unsupported_Kd` producer go to the owner as one list (O4).
  - OQ10 (b) preferred: a G106 signature still mixed after X-1's re-triage is classified whole with per-row notes; the D5 exception
    (the old gate's identity in the raw row, X-1a) only where a row's verdict stays unknown.
  - OQ11 (b): until R6-check, Binding keeps its records of relations through local regions or inferred ends, adds their rows and
    labels those records with A5's requirement `Ownership.UnprovenOrigin` (code and span unchanged) until H11; the ledger
    compares each record with A5's problem. Assigned by R4-M94 to 2a0-3a (position rows at recorded body chains; the twin's Opaque
    limit recorded).
  - OQ12 (a): S3's clarification of SPEC 15.3.6 ("A variable's bounds are followed through other variables to fixed or finite
    Origins and `static`. If none is reached, the variable has no candidate") is made as a SPEC revision before 2a0-3b.
    R4-M94 assigns it to 2a0-3b at the plan's letter; a `let` slot as a region (D4) is BU's later unit (OQ-1 (c), PLAN G194).
  - OQ13 (a) (superseded in part by R4-M76): S5 is adopted: an effect atom on an associated projection that grows under
    polymorphic recursion widens to an unclassifiable effect. 2x-S5 still writes the proposal and its review; a review finding
    that changes the rule returns to the owner. Its SPEC integration is a separate SPEC session after that review, as 2f-S2 is for
    S2 (planning session).
- **R4-M75 Group 1c's design is owner-approved** (owner, 2026-10-19; DEC Q6 form 2; adopted by the R4 planning session on
  2026-10-18 after two independent designs and their design review; 1c-6's Inlined drop and 1c-7's default taint superseded by
  R4-M90) Units 1c-5 to 1c-8 implement one owner-approved design and
  share one final review before the group merges into dev; each unit is still verified and pushed, and a unit that departs from
  the design leaves the group and takes the full process. The design: 1c-5, an activation conflicts only with Loans whose mode is
  shared in the holder where it is found (R4-M68); 1c-6, A1 codes a Move of a PreparedInput slot or its owned contents
  `DefaultArgumentMove_Kd`, and a Language problem found inside an Inlined scope carries `MirFactKind.Inlined` and is dropped
  while a copy's limit records stay (R4-M69); 1c-7, until A5-F9 a value a default's evaluation places into its pending slot
  carries that slot's default taint, a Loan problem found only through tainted sets is a located `Unsupported_Kd` at the omitting
  call, and an Opaque site's value is `{Φ_E}` (R4-M70); 1c-8, `MirProblem.Body` with the key (code, subject, body) and R4-M11's
  cross-body merge at publication (R4-M71). Each unit's entry records its decision as implemented; 1c-9 (records) keeps its own
  review.
- **R4-M76 Owner answers on G148 and the step-2 drafts** (owner, 2026-10-19, relayed by the R4 planning session; on G148's
  completed design review and the reviewed drafts 2x-S1, 2f-S1 and 2x-S5 in `draft/Sketches/A5 Step 2/` and the S5 proposal
  `draft/Proposals/2026-10-19 Effect Atoms Widen.md` on branch `claude/a5-2x-s5`) Library and milestones count 0 under each
  answer.
  - G148: rule G, no SPEC change (R4-M73 records it with G148's unit). Its loss P13 (the Type guard is per holder, so a store
    follows a shared Loan past a holder whose Type passes the guard only through a Type argument, a pair layer or an abstract
    part) is recovered by a representation, not a SPEC clause (OQ8 (b), for P13 in place of the earlier G148 answer's OQ8 (a)):
    until a step-2 unit makes the guard per part (it reads a generic Type's Fields and takes no Type argument as an exclusive
    reference), before R4's exit (else OQ9's list), a conflict that depends on such a store is a located `Unsupported_Kd` (PD30).
    A precision refinement such as GXS stays an unscheduled candidate, as G127.
  - S5 (OQ13 made precise, superseding its "grows"; 2x-S5's question Q1): form (c). A parameter-dependent effect on a projection
    one of whose bases the premises of the summary's declaration prove to conform to a recursive Contract widens to unknown
    environment effects (a destruction counts only when its Type is not proven Copy). A Contract `C` is recursive when `T is C`
    supplies (SPEC 8.3.2), directly or through the Constraints of the Contracts so supplied, a conformance of a projection of `T`
    to `C`'s declaration, with any Type arguments, or to a Contract that refines it. This departs from OQ13's "grows": it also
    widens effects that do not grow, only in code no concrete Type can instantiate. Rejected: form (a) (it needs a SPEC definition
    of summary dependencies, their cycles and a self-map rule); a list of Constraint forms (two blocking rounds on that
    definition, the review's B2 and B5); a hold. Its SPEC session follows the proposal's review.
  - S1-a (2x-S1, the `g5` form): (a). A3's approved P1 is completed in SPEC: storage the body does not own that a part carries
    follows its holder, as Loans do. A5 is unchanged. Until the sentence is integrated in OQ12's SPEC session, A5's Language code
    for the form stands, an exception to PD30 and to R4-M35's interim (a violation of an approved loss is a located
    `Unsupported_Kd`). Rejected: Φ_E per part (b); a hold (c). (Integrated 2026-10-19, INTEGRATED; the exception has ended.)
  - G1 (2f-S1): (ii′). A conformance's earlier result is taken from its body (SPEC 8.11.3's "its results"), and an unclassifiable
    destruction, one in a concrete callee's summary included, conflicts with every Loan the earlier result keeps. Eight test rows
    rejected today become valid; every other rejection stays.
  - G2 (2f-S1): (a). Values of one abstract Type share its Origin atoms for rule 1 (SPEC 8.11.4, 8.11.5's `Merge`), and c24's
    answer also sets the conformance carrier granularity, one rule for callers and conformances. `EffectBoundCallerTest`'s rows
    `zip` and `both`, valid today, become errors; their unit cites this answer. c24's own verdict stays open (the sketch's §9).
- **R4-M77 R5 and the Binding units paused; R4 continues alone** (owner, 2026-10-19, choosing option A of the R4 planning
  session's comparison, verbatim 「A案. 両方止める に決定します。推奨の「きりの良いところ」で、BUとR5を止め、引き継ぎ文書を作成し、再開プロンプトを書いてください。R4はそのまま継続してください。」; and,
  asked of R5 directly as a question that R5 took as the owner's direction, refining R5's stopping point, verbatim
  「「新しい単位は開始せず、現在の作業を継続。完了したら、push/マージまで行って終了」というのはいかがか？」,
  in place of the recommended point (C0c and K11 pushed, unmerged); superseded by R4-M78 the same day) BU stops after G147 (2b)'s
  merge. R5 starts no new unit; its units in flight (F18, F7 with R4's sign-off, K11, C1a, C0c, E13) finish their reviews and
  merge into dev, and a unit whose review blocks a second time or whose fix needs a new design stops on its pushed branch,
  unmerged, its handoff carrying AGENTS's two-round choice to the owner at the resume (R5's bound). Each then writes its handoff
  and resume prompt in `artifacts/handoff/2026-10-19-pause/` (main checkout), releases its PLAN §3 holds and stops its agents; R4
  continues alone. R5-M1's parallel run (OD4) is suspended until the owner resumes it. The comparison's terms for option A: gains,
  waits and machine load nearly vanish, R4 may use the builder files W1 held, and measurements get a quiet machine; losses, R5's
  work stops, R6 is delayed, and R4 takes over builder work such as F18's (under the refinement F18 itself merges first).
  Consequences (planning session): R4 may edit the files R5 and BU held, under COMPILER_ARCHITECTURE's ownership rules, and lists
  each such change for their resume; it re-plans, as their units come due, the step-2 units the plan gives to BU (PD6: 0c, 2b-6,
  2x-E4a, 2x-E6, 2x-E7) and its asks of R5 AR-1, AR-2 and AR-8 (G147 (2)'s validator half), with reviewers that wrote neither
  design nor code in place of AB-5's BU reviewers and AR-2's R5 reviewers. AR-7's acceptance stays R5's (the owner's condition,
  R4-M74 OQ6) and waits for its resume or goes to the owner; AR-6 is met (R5-M63).
- **R4-M78 R5 and the Binding units resumed** (owner, 2026-10-19, verbatim 「すみません。まだトークンに余裕があったため、BUとR5を再開させてください。」; supersedes
  R4-M77; superseded by R4-M79 the same day) The pause of R4-M77 is lifted the same day: R5 and BU resume their work, R5-M1's
  parallel run (OD4) is restored, and the PLAN §3 hold protocol applies as before, and BU's step-2 units (PD6) and the asks of
  BU and R5 (AB-1–AB-6, AR-1–AR-8) stand as before R4-M77. The owner's reason: token budget remains. During the pause R4 changed
  no file R5 or BU held; BU's handoff in `artifacts/handoff/2026-10-19-pause/` stays as the record of its stop.
- **R4-M79 R5 and the Binding units paused again; R4 continues alone** (owner, 2026-10-19, verbatim
  「R5/Binding unitsのエージェントを一時停止します。新規作業の開始は行わず、現在の作業のMergeまで（難しい場合は、きりの良いところまで）行ってください。作業が停止したら、引き継ぎ文書を作成してください。そして、再開時のプロンプトを書いてください。R4はそのまま継続してください。」;
  supersedes R4-M78; superseded by R4-M86 (2026-10-20)) R5 and BU start no new unit. Their units in flight finish through their
  dev merges; a unit that cannot reach its merge stops at a clean point on its pushed branch, unmerged. Each session then releases
  its PLAN §3 holds, writes its handoff and resume prompt in `artifacts/handoff/2026-10-19-pause-2/` (main checkout) and stops its
  agents; R5-M1's parallel run (OD4) is suspended until the owner resumes it. Consequences (planning session), as R4-M77's: R4's
  sign-off reviews of the units in flight (R5's F19, BU's BU-15) continue; a unit cannot reach its merge after a second blocking
  review, when its fix needs a new design or when much of its work remains, and its handoff then carries any owner choice. R4 may
  edit the files R5 and BU held, under COMPILER_ARCHITECTURE's ownership rules, and their PLAN §3 lines and §7 rows where it
  completes the owner's PLAN compression of 2026-10-19 for a session that did not start its share; it lists each such change in
  that folder's README for their resume. It re-plans BU's step-2 units (PD6) and its asks of R5 (AR-1, AR-2, AR-8) as they come
  due, with reviewers that wrote neither design nor code; AR-7's acceptance stays R5's and waits for its resume or goes to the
  owner.
- **R4-M80 2x-T3: no restated kind (OQ5 (iii) (b))** (owner, 2026-10-19, relayed by the R4 planning session, verbatim
  「作らない (推奨)」 to the option of no restated kind (OQ5 (iii) (b): the 79 rows stay blocking under 2x-T2's owners and the ledger
  shape is unchanged); supersedes R4-M74's OQ5 (iii) answer; unit 2x-T3 stopped before code under its row's own condition) The
  ledger adds no `restated` kind and keeps R4-M17's row shape. Finding: PD18's test (the old row's related borrow site equals a
  fact site of a paired MIR record in its body, or the same A1 subject Place) has no input. The raw rows carry no related site
  on either side, and on the live records of 2x-T2's 79 candidates (`draft/Sketches/A5 Step 2/step2-base-ledger.md` §6, base
  `6e969f94`; one fresh compile per source reproduced all 79) `OwnershipIssue.Borrow` is set on 0, 52 rows carry `LoanSource`
  (the holder, the value retaining the Loan), 5 only a reservation's lending point and 22 nothing, while a MIR problem's facts
  name its Loan's anchor (the borrow), its witness and a reservation's anchor, and an A1 problem has none. Exact site equality
  would move 9 rows, an equal start offset 30, span containment 49; the looser readings let a coincidence exempt a row from
  blocking, unsound for the exit gate. Consequences: the 79 rows stay blocking under the owners 2x-T2 assigned in its "if left
  missing" column (X-1 66, 2x-T4 11, 2a 2); the restated-row parts of 2g-4b, X-1 and X-5 have no rows. X-1 classifies each of
  its 66 as the same failure at another location or a missed independent error, the latter a (b) defect of its family with a
  regression test (planning session). R4-M47's clause stays as it is: 2x-T3 does not supersede it. 2x-T3's `BodyOf` part is
  void: source a229d8f4dca0's node, which the plan took for one outside its tree, is in the tree with a broken Parent link (PLAN
  G169), which a ledger mapping would hide. Rejected: a `sites` column in the raw row (an R4-M17 row-shape change PD18 avoids,
  and a fresh ledger run; it tests the holder or reservation against the MIR's sites, not the same Loan); a Place-identity
  reading by start offset or containment (not PD18's predicate, and unsound as above).
- **R4-M81 2x-E1: `MirAggregateKind.Fill`, every fill literal one aggregate of its value** (owner, 2026-10-19, relayed by the R4
  planning session, verbatim: O1 「承認する (推奨)」 to the new member (DEC O1); Q2 「持つ (推奨)」 to a constant zero-length fill
  carrying its value's Loans. Full process: two independent designs and a design review, which took design B with its
  corrections; PLAN G103 E9a) SPEC 4.3.3: `[L of v]` acquires v once, also for L = 0, and copies it into every element.
  - The new member `MirAggregateKind.Fill` has exactly one operand, v's acquisition.
    - Its length is its Type's (`[N of T]`, `[(N + 1) of T]`), which an instance closes (`InstanceCollector.TypeOf`,
      `FunctionCodegen.InstanceType`).
    - Every fill literal, constant or generic, is one Fill (`MirBuilder.Aggregate`).
  - `FixedArray` keeps one operand per element, or none (noinit; an Array's or a Dictionary's empty collection).
  - The validator's Arity:
    - a Fill is 1, at a fixed-array Type;
    - a `FixedArray` is its constant length L, or 0;
    - at a generic length only 0, which replaces the rule that let an instance count it, where any count passed;
    - `Component` reads a Fill's element.
  - A5's `val` is the union over the one operand, so a Fill holds v's Loans at every length.
    - A generic body is checked for every instance (SPEC 4.4.2), and the old path keeps one payload, including N = 0.
    - The SPEC sentence that states Q2 goes to the next SPEC session (PLAN).
  - Code generation (R5's file, under R4-M79): the Fill row is one counted i64 loop over the instance's closed length, and stores
    nothing at 0.
    - Its text does not grow with L.
    - At O2 a byte fill becomes one `memset`, a 4096-element i32 fill vectorized stores, and a 4-element one a single vector store.
    - Any other aggregate kind keeps its located `Unsupported_Kd`.
  - Deleted:
    - the builder's exclusion of generic lengths;
    - its `once` temp and its L-fold operand loop, with an `int` counter against a `long` length;
    - the O(L) MIR of a constant fill (`[1048576 of 0@u8]`: 1,048,580 operands, now as many as `[4 of 0@u8]`; about 100 bytes
      of analysis allocation per element), which a valid `[2147483647 of 0@u8]` would have exhausted (not run).
  - The tests that used the generic fill as the unbuilt form use PLAN G167's position of a Type parameter until G167 lands:
    `PipelineSwitchTest.Source`, `UnsupportedFormsAreOpaqueAtOneRecord` and `tests/mir/opaque.kimi`.
  - Rejected:
    - a `FixedArray` with one operand as a fill. This adds one more case to an open family, which every consumer decodes by
      shape; an unaware generator writes silent wrong code; and the validator cannot tell a fill from a literal left with one
      operand.
    - the builder's loop over noinit, `Len` and `Index`. This gives an Abort effect per `Index` with no source check, needs `Len`
      and `Index` generation, which is not written yet, and adds a loop per fill.
    - a length operand. It duplicates the length the Type carries and needs a `MirConstKind` member.
    - the new form for generic lengths only, which makes two forms for one concept.
    - unrolled stores or a length threshold in the generator.
  - Open: `FixedArray`'s count 0 means noinit at a fixed-array Type and the empty collection at a handle Type, so the validator
    cannot detect a literal that lost all its operands. Neither design introduced this gap.
- **R4-M86 R4, R5 and the Binding units resumed on a reset dev** (owner, 2026-10-20; supersedes R4-M79's pause) On 2026-10-20
  dev and main were reset to `b1a9c8a5` (R4's pause record), and the owner's commit `e307e9f9` added seven 2026-10-20
  proposals. The history of that day's resumed R4 session (the validator split, 2x-E1's merge, 2b-2, M69-1, the G170, G172, S3
  and M69 units and records, DECISIONS R4-M82–R4-M85, PLAN G171–G174, an edit of SPEC 8.5.5 and 23.1.2) is kept on
  `origin/backup/rollback-20261020-*` and on the unit branches that pushed it (`codex/*`; `claude/a5-2x-e1`'s tip `0ccf13ce`),
  and is not part of dev. Each planning session was restarted with its resume prompt: R4 with
  `artifacts/handoff/2026-10-19-r4-pause/R4-resume-prompt.md` and the owner's addition on five of the proposals (M69, S3,
  Canonical Premise Supply and Transferable, G170 PlanB, G172), verbatim 「これらの文書は、実装上の問題点の修正計画書です。どのような内容か確認し、有用ならR4、R5、Binding Unitへ通達し、共有・利用してください。実装完了後は、通常通りdraft/Changesに移動してください。有用でなければ、理由を表示して、Obsoleteに移動してください。」
  (the other two, Accessor Result Contracts and As Naming Syntax, are not part of it); BU with
  `artifacts/handoff/2026-10-19-pause-2/BU-resume-prompt.md`, which the owner pasted verbatim; R5 after asking the owner whether
  to resume on that base, verbatim 「再開する」. No reason for the reset is recorded. Consequences (planning sessions):
  - R5 and BU work in parallel with R4 again; R5-M1's parallel run (OD4) is restored; the PLAN §3 hold protocol applies as before
    R4-M77, and BU's step-2 units (PD6) and the asks AB-1–AB-6 and AR-1–AR-8 stand as the step 2 plan (R4-M74) states them.
    R4-M79's licence for R4 to edit files R5 and BU held ends: R4's pause-time changes on dev stand as the pause README lists
    them (its rolled-back rows excepted), and 2x-E1's builder, validator and code-generation edits land under R4's PLAN §3 holds
    on those files until it merges.
  - The rolled-back history is not on dev: its units are redone from dev under their own reviews (2x-E1 is re-landed from its
    reviewed commits `136718f9` and `58fd2095` after a fresh MirValidator split; its R4-M81 lands with it, before this entry), and
    its owner decisions (for example the plan adoptions, R4-M83's hold, R4-M85's clarification) are not recorded on dev and so not
    in force there; the planning session asks the owner again for any it still needs. R4 evaluates the five proposals anew, and
    until it has evaluated the Canonical Premise Supply and Transferable proposal it starts neither 2b-4 nor 2b-5.
  - Numbering: R4-M82–R4-M85 (assigned in the rolled-back history; the proposals cite R4-M83 and R4-M84) and PLAN G171–G174 (the
    proposals cite them) are not reused for other meanings; a decision taken again keeps its old number. Any session's new G rows
    start at G175 until R4 records G171–G174. The pause README (`artifacts/handoff/2026-10-19-pause-2/README.md`) marks its rows
    for the rolled-back work.
- **R4-M87 S5 implemented: the recursive-base view and A4's widening** (2026-10-21; unit 2b-S5, one design and one review under the
  owner's form (c), R4-M76 and R4-M74 OQ13, with BU's Binding review under PD6 (AB-5); SPEC 8.3.3 and 15.6.4 since `4595bbfc`; the S5
  proposal's §8; placed after R4-M86)
  - View (a Binding fact; step 2 plan PD6): `TyCtxt.RecursiveBase(definition, type)`, whether one of a projection's bases is proven by
    the proof rule (`ProveConstraint`) under the definition's `EffectPremises`, the scope of `Copy` and `Sealed`, to conform to one of
    the pass's recursive Contract declarations. A declaration C is recursive when the premise supply of `T is C` (`AddContractPremises`,
    SPEC 8.3.2's row, C's Self as T), expanded through each available supplied conformance of T or of a projection of T, supplies a
    conformance of a projection of T to C's declaration with any Type arguments or to a refinement of it (`IsRefinement`); an `or`
    Constraint supplies nothing. Each declaration is expanded once at T and once at a projection of T: finite by construction (at most
    twice the declarations) and exact for what a Contract supplies through Self, whose supply at one projection is that at another,
    shifted. The list is found on a pass's first query and stamped with the pass's version (`storageVersion`, as Binding's
    comparison-plan and formatting memos), in a pooled scratch scope: nothing runs in Bind, the view is immutable after Bind, and a
    warm rebind with its queries allocates nothing; `TyCtxt.ResetViews` gains no line. G170 PlanB (selected, not implemented) plans that at its
    cutover (C4) the search moves onto the ContractSolver's enumeration and its recursive-Contract prohibition reads this classification.
  - A4: `AddAtom` resolves at once, as at an obligation, an atom whose Type has a recursive base: a destruction proven Copy under the
    summary's premises does nothing (SPEC 16.3.3, 8.11.2), anything else is Unknown, the unknown environment effects SPEC 15.6.4 puts
    in its place; other atoms are kept as before. No MIR vocabulary or diagnostic code; growth through a recursive base the view finds
    no longer reaches G128's Limit.
  - Rejected: a memo kept until an edit (a `ResetViews` line in R5's K lane); a per-Contract state reset in `PrepareContracts` (grows
    the over-limit `Binding.Contracts.cs`); a declaration graph read off clause syntax (a list of Constraint forms, the proposal
    review's B5); scanning the definition's premise facts in A4 or the view (a second copy of `ProveConformance`'s premise rule);
    keeping a recursive-base atom proven Copy (no effect, and a kept one would still grow); a cut by base chain (a declaration
    reached again through its own Type argument repeats at Types that are not bases of each other, so that cut is not finite by
    construction; review B2).
  - Limits, not fixed here: an argument-dependent clause, a Constraint on a Contract's own Type parameter or a projection of it, which
    can supply something new only at a declaration's later expansion, is not followed (an under-approximation; Binding rejects every
    projection-form program found; the plain-parameter form is unverified). A base G170
    hides keeps its atom: a growing one ends at G128's bound (a located `Unsupported_Kd`), a non-growing one is accepted where SPEC
    15.6.4 rejects, in uninstantiable code. G170's false Copy for `T.(Tree).Child.(Tree).Child` under `T is CopyTree` now widens the
    callee's own summary, so a caller whose premises classified the kept atom is rejected on the mir pipeline (`h<T>(t:
    T.(Tree).Child.(Tree).Child)` with `T is CopyTree`, called from a confined `visit` under `U is CopyTree`, `U is Copy`,
    `U.(Tree).Child is U`: `IncompatibleContractImplementation_Kd`), in uninstantiable code only; it is G170's. A layered projection
    (a pair layer over a projection, `s/T.(Tree).Child`) is not tested; no path was found that reaches `AddAtom` with one.
- **R4-M88 An operation Binding left unresolved is an Error node that keeps every operand it evaluates** (2026-10-21; unit 2x-E2's
  HIR publication rule, PLAN G103 E9b and G95; design B of two independent designs, chosen by the planning session after R4's and R5's
  reviews; R5, R5-M63's owner, agreed to the R5-M63 text below; replaces the blocked draft of `1babec22`) SPEC 23.1.2, R4-M10, R4-M70
  option P, R4-M61. `HasFacts` listed no Type for an operation although its building reads Binding's selection for the node, so the
  publisher gave one Binding left unresolved its form's Op and the builder formed it from `TypeOf`'s `()` fallback: Type and
  Projection findings (`'A' == 65`, `x + 128` of `Wrapping<T>`, `value is Dog`, a stray `s is ref or uniq`, G95's `values[r]`), each
  of which stops every analysis of the program; and a construct Binding left untyped was held at `()` though its position fixes its
  Type (`return if c => 1 else => missing`).
  - Rule 1 (`Binding.Operation`, `Binding.HasFacts`): an operation, a form whose building forms its value or Place from Binding's
    selection for the node (Literal, Field, Deref, Index, PrimitiveOp, Logical, Borrow, Convert, RuntimeTypeTest), lists the node's
    Type among its facts, standing for that selection, so the publisher's rule publishes one Binding left unresolved as Error. A new
    HirOp member decides its membership in `Operation` (HirOp's documentation names the members). Constructs, statements, calls and
    names Binding left unresolved keep their Ops where they have their facts. Name is not an operation: a resolved name can lack a
    Type (a parenthesized callee `(f)(1)`, a capture entry), which would be a broken contract (8 tests failed with it).
  - Rule 2 (`HirPublisher.Keeps`), R5's text: "an operation (Literal, Field, Deref, Index, PrimitiveOp, Logical, Borrow, Convert,
    RuntimeTypeTest) Binding left unresolved (no failure of its own, not a recovery, not broken) is an Error node and keeps every operand
    its form evaluates — resolved, unresolved or Error — each evaluated as its own value, never acquired, its root named at the site; an
    Error operand is evaluated as its derived Opaque with its own kept operands; this holds also when the operation is Error for another
    missing fact; it supersedes 'an operand is kept only when its own Op is published with its facts' for these nodes and decides
    R5-M63's deferred unresolved compound Places for Field, Index, Deref and `@follow`. Unchanged: rejected compound Places keep only
    resolved operands; failed or unresolved calls, statements and constructs, and tuple/array/interpolation/dictionary/for forms stay
    under G121 (owner); Logical's kept right operand is evaluated unconditionally", and (planning session with R5) it keeps every
    operand its form evaluates "except an unresolved construct whose value has no Type, which is replaced (G121)": a construct
    (`Binding.Construct`: Block, Unsafe, Do, If, Match, Try, Loop, the forms whose parts, bodies or exits deliver their value) Binding
    left unresolved with no Type, also in parentheses or labeled, since no destination Type holds a kept operand (built at `()`,
    `return v[if c => i else => missing]`, `v[(if …)]` and `v[label l: do … if c => exit to l i … exit to l missing]` got a finding
    and lost the record of a move before it; under a unary operator, `v[-(if …)]`, the operator stays kept and replaces the `if`). The
    labeled do is the owner's choice (2026-10-21, verbatim 「置き換えて取り込む（推奨）」, the question relayed by R5's planning session; confirmed
    to R4's planning session by the owner, 2026-10-21, 「了承します」): an untyped unresolved labeled do is replaced too, its body's problems
    lost (`other + label l: do … exit to l missing`), PLAN G121's failed-construct residual. `Loop` joins `Construct` by meaning, a
    planning-session decision: a loop's exits deliver its value (SPEC 14.6.3; a `for` or `while` gives Unit, 14.6.1), so the same
    replacement follows for an untyped loop, labeled or not (`other + label l: loop … exit to l 1` is no longer a finding). A
    kept-construct design the owner chooses for G121 removes the exception. `Keeps` reads the node's binding state first and the
    parent's form Op once per parent (the walk's `unresolvedOperation`). Its kept right operand evaluated unconditionally, a Logical's
    moves there are definite rather than possible; A1 reports a later use as `MovedPlace_Kd` either way.
  - Part 3 (`MirBuilder.Value`, from `Operand`): a value Binding gave no Type, a construct it left unresolved or a derived value, is held
    at the Type its consumer expects. It is one rule with `Unsupported(node, derived, expected)`, which held a derived value so already:
    SPEC 14.9.2 makes a construct's Expression Type its Target Result Type, the fixed expectation, which Binding does not publish for
    one it left unresolved. Temporary: the builder's choice goes, with residual R1, when Binding publishes that Type (option C). Golden:
    `tests/mir/opaque.mir`'s returned `@follow` temp, `()` to `i32`.
  - Kept: `MirBuilder.Into`'s clause for an assignment or update whose target Binding rejected (an Assign keeps its Op).
    `MirBuilder.Rejected`'s "a value without a Type" and "a name of an Error local" terms, whose removal 9cf3bf26 staged for this rule:
    `x = 1` with `x: Missing`, a name Binding left unresolved, still needs the first; their removal needs a published fact for such a
    name (PLAN G103).
  - Cost: one predicate test and one Type read per published node and one form Op per Error node; no allocation. Measured (mir
    Functional with `KIMI_MIR_DIFF`, the implementation against dev `c526ad30`, `artifacts/verify/mir-diff/2x-e2b`): b-finding's rows
    (BinaryKoto 44, IsKoto 12, ReturnKoto 4) gone; two new A1 rows, the SPEC 14 example's own `use(p)` and `pass(x@move, x)` errors,
    which the `animal is Dog` finding had hidden; no other row.
  - Residual R1 (PLAN G103): a discard of a construct Binding left untyped (`_ = if c => 1 else => missing`) is held at `()`, a
    validator finding where a source has a Type, as on dev. A construct under an unresolved operation, also in parentheses or
    labeled (a labeled loop included), is replaced: no finding, its parts' problems lost (G121). Owners: Binding's Expression Type (SPEC
    14.9.2 rule 2, option C) or G121's kept-construct design. Opens (G121, owner): an un-annotated failed `let`, a failed match, a
    failed Place over a failed Place (`make(s@move).missing.a`), and failed or unresolved calls with moving arguments.
  - Rejected: every node Binding left unresolved on a recorded error an Error (`1babec22`: constructs and statements lost children, an
    Assign's Opaque read its target, `FailureCauses` allocated per node); Binding publishing the Expression Type now (option C:
    owner and BU scope, and rule 1 is still needed: `'A' == 65` has no operator for any Type); a MIR marker for a Type Binding did not
    form (a vocabulary entry that weakens the validator; still needs rule 1); the builder deciding derived operations (a second copy of
    the publisher's decision); rule 2 keeping only resolved operands (loses records the base reported:
    `once(s@move) + missing`, `1 + (missing + once(s@move))`); keeping a labeled construct operand (a finding at `v[label l: do …]`,
    a regression from dev; the owner's choice above); Name as an operation (above); building every untyped construct's parts
    for effect (drops the Loan a typed destination receives: `let r: ref/i32 = if c => x@ref else => missing`); a new-Error test by
    node state (reaches unresolved calls, G121); building an untyped construct operand's parts for effect (G121's kept-construct design,
    owner-reserved).
- **R4-M89 A5-F5's design package (2b-D) approved** (owner, 2026-10-21, relayed by the R4 planning session, verbatim
  「推奨どおり承認 (推奨)」
  to the package with OQ-F5-1 to OQ-F5-6 at their recommendations; R4-M74 OQ2) The package is
  `draft/Sketches/A5 Step 2/2b-D-package.md`, from two independent designs, a design review that chose a combination per item and a
  confirmation round whose two blocking items (the staticEnsure rule's may-set proof, e10f; the layer obligation's Unknown) were
  fixed as the reviewer worded them (`2b-D-review.md`). Round 18's record (`draft/Sketches/A5 Step 0/prototype.md`) gains e10f and
  the R18EnsureClosed/Keyed/Exact/ByField UNSOUND rows, amending finding 4, with unit 2b-3's prototype update (deferred from this
  records commit). It is the owner-approved design of DEC Q6 form 2 for G:A5-F5, form 1 for the Transferable view units (R4-M74
  OQ2): G:A5-F5's units take the group review (2b-1, 2b-3, 2b-8a), its other units their own (2x-R13, 2x-R14, 2x-P12, 2b-4a, 2b-4,
  2b-5, 2b-7 and BU's 2b-6), and 2b-8b, a records unit, none (Q6 form 3). In short:
  - err(effect) is an access in `MirBorrows` (`AccessKind` rows `EffectRead`, `EffectWrite`; three effect fact kinds); the Call's
    set is `Takes(n−1)` with no holder left out (a reading of E13 (1)(b)), the Drop's is `TakesValue`; W6; e1's code is
    `CallReservationConflict_Kd` by the A6 table's row 1 (a clarification of R4-M72's effect row); R3 issues no Loan and runs
    through the solve; staticEnsure is PD12 (ii) as written for every key; a static's Loan names E (G136 (i) replaces PD12 (iv)'s
    lemma); a copy-found effect conflict is dropped until 2e-4 (G171; shadow only, no regression).
  - A4's `Keys` returns the key row with a per-view query (R4-M38's list and `bool` superseded; the storage unchanged, 2b-2).
  - Transferable: one per-declaration component summary shared with Owned (fixing G174), a three-valued layer obligation, the
    static's container premises as its anchor, read by A4 through `TyCtxt.Transferable(staticField)` with no `MirStatic` field;
    `HoldsRaw`, its walk and `RawStaticRead` go (2b-5). The container anchor departs from the Transferable plan's T2/§5.3, and a
    generic static's unproven verdict is Limit, not Environment, until P42.
  - Owner answers: OQ-F5-1 (a) a whole static write ends its Field's Loans (a static Field read as a Move Path; a clarifying SPEC
    sentence goes to the next SPEC session); OQ-F5-2 (b) the shared summary (BU's 2b-4a, R4 reviewing; the growth guard deleted
    after a census); OQ-F5-3 (a) declaring `Kimi.Transferable` and Owned's change are SPEC-conformance fixes on both pipelines with
    evidence, a located `Unsupported_Kd` guard for any form the old emission does not generate; OQ-F5-4 (a) 2b-4/2b-5 do not wait
    for G170; OQ-F5-5 (a) the departures confirmed as one list (the package's §6); OQ-F5-6 (a) SPEC 15.6.4's "a live Loan … proves"
    reads liveness as a may-property, reported to the next SPEC session ("a Loan that every path to the access carries"), the
    ensure's located `Unsupported_Kd` registered at X-2 meanwhile. A third SPEC gap goes to the next SPEC session (the package's
    §4): 15.3.5's fixed point for projection families, which SPEC states as terminating only for Type-argument recursion.
  - Numbering: R4-M88 is reserved for 2x-E2's HIR publication rule (redesigned after its review).
- **R4-M90 A5-F9's design package (2e-D) approved** (owner, 2026-10-21, relayed by the R4 planning session, verbatim
  「推奨どおり承認 (推奨)」
  to the package with OQ-F9-1 and OQ-F9-2 at their recommendations; R4-M74 OQ2) The package is
  `draft/Sketches/A5 Step 2/2e-D-package.md` (design A with parts of B; review and confirmation in `2e-D-review.md`, whose two
  blocking text items were applied). Units: 2e-0 (R4's own builder unit after 2x-E2), then G:A5-F9's 2e-1, 2e-2, 2e-3, 2e-4, 2e-5;
  optional draft round 2x-P13. DEC Q6 form 2 for 2e-1 to 2e-5 (one group review; a departing unit leaves the group); 2e-0 and
  2x-P13 take their own reviews (PD29). PQ-1 (BU agreed, 2026-10-21): 2b-6's qualified part lands as planned; its unqualified
  in-group and implicit-receiver part lands after G:A5-F9's dev merge, re-expecting the caller-owned witnesses (and any of 2b-3's
  tests that use those forms) as Binding's `Unsupported_Kd`, with a P42 row in PLAN. This amends the 2b-D package's §1.5 and 2b-6 row:
  2b-8a waits for the qualified part only. In short:
  - A template's PreparedInput Deref is an Inline step (A3 erratum E17 (numbered at adoption in 2e-2's entry)); R26-borrow and
    R26-access share one slot-rooted definition up to the first raw step; template calls take P9's Content rows, a raw slot
    `MirWrites.Raw` the raw row (the raw row replaced by r3's `Unsupported_Kd` for every slot that holds raw, R4-M93's ruling), any
    other slot whose Type holds raw, at a call whose inputs' Contents hold a Loan, a located `Unsupported_Kd` (r3, registered under
    PD28, removed by G160's representation); R-a ends the inspection Loans of every moved
    pending slot before the omitting call's checks and deletes R4-M70's default taint.
  - The copy disposition (PLAN G171): a failure found in a copy is its template's only when every participating event lies in the
    current evaluation of one copy (round 20's `R20Own` with `R20Entry`: a Loan live into the copy's entry from outside moves the
    boundary outward), decided per candidate before selection on every failure path; `Loan.Location` replaces `Loan.Site`;
    `MirBody.Common`. A caller-owned problem inside a copy is recorded at the outermost omitting call with an `Evaluated` fact;
    `Evaluated` replaces `Inlined` if D15 lands in 2e-4, else both coexist until 2g-6. R5's validator rule AR-9 (a copy is entered
    only at its first location) is a prerequisite of G:A5-F9's dev merge.
  - Owner answers: OQ-F9-1 (a) a reservation's activation is not checked against its own Loan when a recursive default's value
    carries it (a clarifying SPEC 15.6.7 sentence goes to the next SPEC session); OQ-F9-2 (a) the departures confirmed: R4-M69's
    Inlined rule replaced for A5 (A1/A2 follow in 2e-4 if 2x-R7 and a `MirDrops.cs` response are ready, else in 2g-6), R4-M73's
    "R4-M69 governs inline default copies" sub-bullet and R4-M72's Defaults bullet superseded, and R4-M72's A6 table gains the row
    above; the rolled-back M69 plan's exclusion of that relocation is not in force.
- **R4-M91 A4's key row and its per-view query** (2026-10-21; unit 2b-1 of G:A5-F5 under R4-M89, the package's §2.1 and OQ-F5-5
  item 1; group review; supersedes R4-M38's `(Field, Write)` list and its `bool`, the classes as R4-M74 OQ6 (a) confirmed)
  - `MirEffects.Keys(body, view, block, out classes)` returns the site's key row: ⌈2F/64⌉ words over the version's F mutable static
    Fields, bit 2f a read and 2f + 1 a write of the Field A4 numbers f, a read bit beside its write bit read as the write (R4-M38's
    rule). The row is A4's scratch, valid until the next query or `Run`. `MirEffects.Field(symbol)` gives f, −1 for a static that is
    not mutable or that no body of the version accesses; a borrowed mutable static always has one (its Ref is a static effect `Local`
    records), so the layout stays A4's.
  - `classes` are the site's `Unknown`, `Limit` and `Derived` bits, kept apart and now internal constants: for A5's effect rule
    (2b-3) Unknown meets every key, a Limit is a located `Unsupported_Kd`, a Derived site reports nothing by itself (R4-M10).
  - The query selects the view it is given, so a body's views are never collapsed (R4-M11); a caller reselects its own afterwards.
  - Deleted: the `accessed` list, its enumeration loop and its result buffer, and the list of each number's Field, which only that
    loop read (`fieldIds` counts the Fields). A query costs Θ(terminator inputs + composed views × ⌈2F/64⌉), no longer Θ(keys
    reached) (2b-2: 2,098,176 listed keys over 4,826 sites at ring N = 2,048, F = 1,024). The key storage is unchanged (2b-2).
    `Benchmark --static-keys` before and after: sites, classified sites and the keys the rows name equal in all 60 cells, Run bytes
    equal, Local bytes 296–16,568 B lower where F > 0, the queries' bytes 0 (were up to 32,920 B).
  - Rejected: the list (A5 would pay Θ(keys reached) per site and match keys by symbol); a stored per-site record (R4-M38: +36–60 %
    cold A4 allocation); a union over a views mask (collapses views, the 2b-D review's B-2). Not temporary.
  - Recorded here for lane C (the planning session, 2026-10-21): 2x-R13 moved `MirBorrows.Checks.cs`'s location driver (`Check`,
    `Accessing`, `Accessed`, `Dropping`, `Destruction`, `Borrowing`) to `MirBorrows.Locations.cs`, behavior-preserving, `.Checks.cs`
    keeping the conflicts relation; and ruled that `MirProblem.cs`, due by count (`Inlined`, 1c-6; `Body` and `NoBody`, 1c-8;
    reworded comments), has no split: the problem and fact records with their fact kinds are one responsibility.
- **R4-M92 A5's effect rule: err(effect), R3 and the initialization proof; G136's static items** (2026-10-21; unit 2b-3 of G:A5-F5
  under R4-M89, the package's §1; group review, whose B1 the planning session ruled within R4-M89 (below); clarifies R4-M72's effect
  row as R4-M89 reads it; G136 (i) replaces PD12 (iv)'s lemma; G136 (ii) is OQ-F5-1 (a))
  - Rule (SPEC 15.6.4, 15.6.2): at a Call or a Drop, each mutable static a Loan of the view is on (an effect root) with a candidate in
    the effect set is accessed whole and shallow through two rows of the conflicts relation, `EffectWrite` (a write key or A4's Unknown,
    any mode) and `EffectRead` (a read key, a unique Loan). A Loan past a raw or shared Deref is not reached; in safe code a static's Loan
    has neither (E3; no `uniq` static, 15.2.3), so this is 15.6.4's per-Field granularity. A4's Limit without Unknown, where a candidate a
    write would meet is met by no key, is a located `Unsupported_Kd` at the site (a limit record, kept in copies); Derived reports nothing
    by itself (R4-M10), keys beside it stay checked. Keys are queried per view (R4-M91): a run that stands for several views records each
    problem with the views whose keys give it (R4-M11).
  - Sets (a reading of A3 E13 (1)(b)): a Call's effect set is `Takes(n − 1)`, the live holders it does not overwrite, E and every
    operand's value, no holder left out (round 18: `R18LiveOnly` is unsound on e4, e4m, e6); a Drop's is `TakesValue` (val(X) at every
    Drop). One effect access is listed after the location's own accesses, so the one gather takes its values; the rule runs last.
  - W6 (SPEC 15.6.7): at a call's effect rows a reservation the call activates is unique. One predicate, `Unique(candidate, kind)`, is
    read by the mode test and by `Code`, which takes the access kind; the operands' own accesses and the activations still see a
    reservation shared in its holder. The effect rule stamps the reservations a location activates once, so the predicate is O(1).
  - Codes: an effect problem is `ComparisonLoanConflict_Kd` at the call or Drop, except one whose Loan is found only in a reservation
    holder the call does not activate: `CallReservationConflict_Kd` by the A6 table's row 1, through `Code` (e1, an argument's call while
    the receiver is reserved: `counter.add(g())`). An activation's conflict and an effect conflict on one Loan are two problems (e4c).
    Round 18's e1 program, `State.count += g()`, secures `g()` before it reserves the target (SPEC 13.7.2, `MirBuilder.Update`), so it is
    valid; the prototype's row is corrected as an evaluation-order error of the model.
  - Facts (PD31): `MirFactKind` gains `EffectRead`, `EffectWrite` and `EffectUnknown` (not an ARCH-closed vocabulary), one per problem,
    what decided it (a write key, else Unknown, else the read key), sited at the met Field's declaration (its StaticInit body's source);
    `Report` takes it as an argument, a fifth fact slot.
  - R3 (SPEC 16.4 with 16.3.1, 16.3.3): a Drop of a mutable static whose own read or write key on it, or Unknown, is
    `ComparisonLoanConflict_Kd` at the Drop, anchored at it, with the effect fact and no witness; Limit alone, a located `Unsupported_Kd`.
    No Loan is issued. It reports through `Report`, so the view skip becomes "no Loan, no guard candidate and no Drop of a mutable
    static", and the hold stays one rule.
  - Initialization proof (SPEC 15.6.4, 22.4.3; PD12 (ii) as written; the planning session's clarification of R4-M89 on the group
    review's B1, 2026-10-21): "a live Loan anchored to a Field proves that Field initialized" holds wherever a first access is
    composed, since every first access is an ensure and A4 composes each initializer into every site that may run it (R4-M38). So at
    every Call and Drop, ensures included, an R1 or R3 conflict where every effect that would decide it (a write or read key, or
    Unknown) lies in the summary of the initializer of a static d with a candidate in the effect set, in any mode, is the located
    `Unsupported_Kd` at the site; the first effect outside it decides the Language problem (`Unproved`; the group review's R2-B1:
    `multiImm`, a proved initializer's write beside an unknown call, stays the error with `EffectUnknown`). The site must be one that may
    first-access d: d is immutable (its accesses carry no key), the site's row has a key of d, or the site is d's own staticEnsure
    (`MirProgram.StaticInit`; an ensure carries no key of its own static, R4-M38, so without this e10a, e10e and e10f would be errors).
    A site that never reaches a mutable d keeps a direct conflict as the error (`direct`). A key the site may produce both directly and
    through a proved initializer counts as proved, since keys do not record their source (P5). The set is a may-set, so the proof
    decides nothing exactly (e10f; OQ-F5-6 (a)). A4 publishes `MirEffects.Summary(body, view, out classes)`, a body view's row and
    classes over its existing storage, an atom it keeps counted as Unknown (warm 0 B). Before the review the proof was consulted only at
    the body's own ensure of the static (Language errors on SPEC-valid pA, pB, pQ: a call that reads x, the ensure of x2 whose
    initializer reads d, a call that reads an immutable static); that special case, its slot decoding and `Met`'s bool are deleted.
  - G136 (i): a direct Loan on a static names E (`Holders`; W's step from E is `MirWrites.None`), storage the body does not own (SPEC
    15.6), so a call's store through a static's borrow lands in E as a parameter's does (`refs.append(y@ref); y = 2` errs; it went nowhere
    before); check sets do not change, since σ(E) is in each.
  - G136 (ii) (OQ-F5-1 (a)): a whole write of a static ends its Field's Loans (`Ending`), a part write none; e16b reports once.
  - G171 interim (ended by R4-M97): an effect conflict found in an inline default copy carries the Inlined fact and is dropped, as
    every copy-found conflict, until 2e-4 (unsound for a caller's Loan, shadow only, no regression); Limit and proof records stay in copies.
  - Unsupported producers registered for X-2 (PD28): A4's Limit at a site with a live mutable-static Loan no key meets (and R3's with
    the Limit alone), until A4 classifies the effect; the initialization proof, until OQ-F5-6's clarification and a must-carry fact (P42
    alone does not decide it). A static without its initializer is a broken builder contract, `InternalInvariant_Kd`.
  - Limit of the proof (the group review's R2-N1): for an immutable d the proof applies at every site, since A4 keys no immutable
    static and A5 cannot tell whether the site reaches d; so `let lm = lim2.view(); let ll = log.view(); let v = e3.n`, whose ensured
    initializer writes log and never reaches lim2, is U where SPEC errs (sound: U still rejects). Removed when A4 keys immutable-static
    accesses, or at P42; registered under X-2's proof producer.
  - Tests: Binding accepts the implicit static Loans of a group's own members only (a receiver reserved or shared with a retained
    result, an argument's adaptation), so 2b-3's programs are group members, which R4-M90's PQ-1 re-expects when 2b-6's unqualified part
    lands; the qualified receiver and argument forms are `NoApplicableOverload_Kd`, a `for` over a static moves it, a `match` on one is
    Opaque. Census (`MirCensusTest`): no A5 problem in the library, the milestones (42's `Registry.replaced` Drop runs no key) or the
    goldens.
  - Driver: `MirBorrows` takes `MirEffects` (`MirAnalysis`); the effect access is listed by `MirBorrows.Locations` (`Effecting`).
  - PD21 (the planning session, 2026-10-21; the plan's 2x-R9 expected it): `MirBorrows.Activations.cs` has one responsibility, the
    activation and observing-destruction checks of a location, and 2b-3's change is an argument at its two `Code`/`Report` call sites, so
    no split exists; 2x-R9 keeps its 2f-D condition (a split if 2f-D adds the caller relation there). `MirBorrows.Loans.cs`,
    `MirBorrows.Values.cs` and `MirAnalysis.cs` reach their third change with 2b-3: their next editors split first (PLAN §3.7).
  - Cost: per checked site one `Keys` query per view where an effect root has a candidate or R3 applies, Θ(effect roots + their
    candidates) tests and scans, and on a conflict Θ(the body's statics + their initializers' views × ⌈2F/64⌉) for the proof; `Work`
    counts the row's words, the bit tests, the reservations stamped and the statics the proof scans; Field numbers per view and the
    stamps, pooled; warm 0 B. The query's own composition, Θ(terminator inputs + composed views × ⌈2F/64⌉), is A4's and uncounted (A4
    has no work counter; 2b-2 measured it). V16 "F5 effects" lives in `MirBorrowsTest.EffectsScaleWithCallsAndStaticLoans` (VERIFICATION
    has no V16 table): linear in N calls at M = 4, K = 8; within the M × sites term in M at N = 32.
  - Rejected: W6 as a gather pass marking activated reservations unique (a new pass and taking, and the bucket order would decide
    `EffectWrite`'s candidate); a site row `Effect` in `AccessKind` (not a relation row); a deep reach (equivalent in safe code); R3 as an
    issued Loan (no value carries it); the closed-key ensure (`R18EnsureClosed`, `R18EnsureKeyed`: unsound on e10f); keeping `Code`'s and
    `Report`'s signatures (a second statement of row 1 outside `Code`, an optional parameter); the proof at any site regardless of
    whether it may first-access d (the error of a site that never reaches d would become a U, `direct`). Not temporary, except the
    proof's and the Limit's `Unsupported_Kd` (OQ-F5-6, P42; AGENTS Diagnostics).
- **R4-M93 A5-F9's template calls: P9's rows and r3** (2026-10-21; unit 2e-1 of G:A5-F9 under R4-M90, the package's §3 (D8, D9,
  D16), with the planning session's ruling within R4-M90 that replaces D9's raw row by r3 (AGENTS Diagnostics); supersedes R4-M57's and
  R4-M63's "unknown callee until F9" and R4-M70's default taint for template calls)
  - Rule (SPEC 7.2.3): a default template's call (`MirCalleeKind.Fn`, `Arg > 0`) has one row (input i, Content, Result) per input,
    untied, and no store row. Its inputs are the prologue's shared borrows of the slots (`MirBuilder.Defaults`), so Content(input i) is
    slot i's value: the arguments' Loans, never the inspection Loans that the omitting call's Moves of the slots met (the `Unsupported_Kd`
    at the call of RecursiveDefaultTest's generic, Function-typed and reference programs, the 1c design's p06 and dc-reserved-rec).
    `Flow.Placed`'s template-call disjunct is deleted: a template call's value carries no default taint; inline copies keep it until
    2e-3. The unknown-callee branch (Value rows into the result and each exclusive input, an N(N + 1) scratch) is deleted.
  - r3 (registered for X-2, PD28; removal stage: G160's representation): in the checks replay, a template call whose inputs' Contents
    hold a Loan and one of whose slots' case Types holds raw authority (`MirTypes.HoldsRaw`: a raw pointer, an rc handle or a Weak of
    one, at any depth) is a located `Unsupported_Kd` at the omitting call, `Ownership.Unsupported`, no fact, recorded from `Calls.Call`
    (`Checks.cs` unchanged). The default may write through it by raw authority (SPEC 5.2.1), which no row follows. It covers
    `MirWrites.Raw` slots and hidden pointees (dc-raw-hid, G160 (a)'s shape, accepted before) alike and over-covers rc handles and
    Loan-free pointees (a valid twin gets the U). Census: none in the library, milestones, goldens or spec cases.
  - Ruling on D9 (planning session, 2026-10-21): D9's raw row ("a `MirWrites.Raw` slot also stores every input's Content into E") never
    landed: it made SPEC-valid programs Language errors. In `putz(p: raw/(ref/i32 during a), x: ref/i32 during a, z: ref/i32 during c,
    n: i32, y: i32 = <writes x into p, recursive>)`, `putz(rp, v, w@ref, 3); w = 5` is valid (the default never stores z), and the row
    stored w's Loan into E (`ComparisonLoanConflict_Kd` at `w = 5`); it added no verdict on any probed raw-only case. r3's U is the one
    rule for every raw slot.
  - X-2 candidate (a), not adopted: no U for a raw-only slot, resting on the default's raw writes being bounded by the signature's Origins
    (Binding rejects `p[0] = x` with unrelated Origins, `UnprovenOriginRelation_Kd`) and on the omitting call's own contract row reaching
    E by rule G's raw clause. Probes (2e-1): putr, a generic `putg<T>(p: raw/T, x: T, ...)` and `puto` with `origin c outlives a` each err
    at `v = 5` without the row. Unshown: the window between the template call and the call (other defaults, raw writes, G160's raw
    authority); a full-process unit must show it sound before r3 leaves raw-only slots.
  - D16 (OQ-F9-1 (a)): dc-reserved-rec is valid; P9 puts v's reservation Loan into the default's value, and its activation meets only
    Loans shared in their holder (R4-M68).
  - Package tests not reachable: "a direct raw write through a raw-only slot errs" (no Binding-valid program makes a raw row the sole
    route, above); dc-raw-rec with `y: i32` errs at `v = 5` before and after (a slot that holds no Loan is never tainted, so the
    prototype's U at the call does not arise; r3's U now joins the error).
  - Ledger: a5-defaults loses its four template-call sources (d86084138ec3, 61f211d25ef3, 9f9cf92f515d, 72ee8d850134); the inline-copy
    taint source 48f5bd793398 stays until 2e-3 and the `CallRetention.Limit` rows stay (G128). No `u-defaults` row exists.
  - Cost: N rows per template call (scratch N); r3 one `HoldsRaw` walk per input, in the replay only, where the Contents hold a Loan.
    V16 "F9 template calls" (`MirBorrowsTest.ATemplateCallsWorkStaysDense`; VERIFICATION has no V16 table): the package's Work ∝ M·N (H)
    does not hold for N preceding parameters × M calls in one body, before or after: a call's N prologue borrows scan the temps live
    across them and its endings the body's holders (existing dense terms), so the test asserts the dense bound; the shape's Work fell
    2-6 %. Warm 0 B.
  - Rejected: D9's raw row (above); (a) until shown sound; reading a slot's Type through `lent` (an input's Type is a shared reference
    to its slot, which `HoldsRaw` walks).
- **R4-M94 A5-F4's design package (2a0-D) approved** (owner, 2026-10-21, relayed by the R4 planning session, verbatim
  「推奨どおり承認 (推奨)」
  to the package with OQ-3 (a), and to the other questions verbatim: OQ-1 「(c) 別の BU 単位で後日 (推奨)」, OQ-2 「(a) R6-check で削除
  (推奨)」, OQ-4 「(b) 条件を足す (推奨)」; R4-M74 OQ2) The package is `draft/Sketches/A5 Step 2/2a0-D-package.md` (design A as
  the base with parts of B; the design review, with real-compiler probes, and the independent confirmation's two rounds, whose
  blocking items P-1 to P-7 were applied, are in `2a0-D-review.md`). Units: the Binding view units 2a0-2, 2a0-3a and 2a0-3b take
  DEC Q6 form 1 (one BU review each, PD6); G:A5-F4's 2a-1a, 2a-1b, 2a-2, 2a-4 and 2a-5 take form 2 (one group review; 2a-3 is
  folded into 2a-1a); 2a0-4 and the new 2x-R15 (A5's Place steps, behavior-preserving) take their own reviews. In short:
  - Rows (2a0-2): a relation is a body chain when an end, resolved at its use, is not fixed (`Binding.BodyChain`, the call test);
    rows `(Site, Longer, Shorter, Equality, Source, Clause, Destination, CaseMask)` with `ResultFit` set by Binding where it
    validates a body result; each row owned by the executable body Binding binds it in (function, Closure, accessor, default
    template by Binding's default index, static initializer), recorded at formation, never from `Use.Parent` (the parentless
    `$for.iterator` Use); resolution at use instead of alias rows, meets keyed by their canonical operand set; T1 by construction.
  - `OriginAtom.Of`: SPEC's three kinds by a first-match precedence table (the package's §3): the body's contract → Fixed; meets
    by operands; the body's roots, default templates' prepared inputs included → Finite; environment bindings Finite until 2c-6;
    binders in an enclosing body → Fixed; static Borrows formed in the body by mutability; `Inference`, `Anchor` → Inferred; an
    `Unbound` end or another declaration's universal atom (PLAN G195) → a located `Unsupported_Kd`. Slots are decoded only by the
    view (an `ArchitectureRulesTest` row, G163). One owner of fixed–fixed entailment: the premise catalog and
    `OriginPremiseClosure`, read per body by `UniversalRelations.For`, with an agreement test until R7.
  - Boundary: 2a0-3a forms position rows at the body chains Binding records (non-call fits; a local's clause in `JudgeLocalClause`)
    and labels the twin `Ownership.UnprovenOrigin`; a new default-pipeline record stops the unit; the twin's Opaque limit (a Borrow
    formed inside the failed node) is recorded; at H11 non-call rows become verdict-independent. 2a0-3b at the plan's letter
    (no-candidate variables are body regions; "no principal candidate" is the clause's relation judgment). 2a0-4 splits the
    ledger by owner.
  - `MirOrigins` (2a-1a, 2a-1b), a canonical component: step 4 per body runs `Judge` (all classes), then `MirBorrows` per view,
    then `Record`; containment per borrow layer through the shared classifier `MirSteps` (2x-R15), up to the first shared layer,
    stopping at a raw Deref; MeetNode; Refuted by finite-source and Unknown by fixed-source bitsets; Reason `(Longer, Shorter,
    Equality)` on `MirProblem` and in `MirReport`'s key, facts `Anchor`, `Relation`, `Result`; `MirDataflow.Within` decides the
    byte limit. G161 is decided by 2a-1a's Refuted rule.
  - Merge (2a-2): rule 1 drops a storage-end conflict with the `Result` fact in the views where its Anchor's atom is refuted at a
    `ResultFit` row (PD11's first half, per atom); rule 2 (OQ-4 (b)) drops a guard-sourced `UnsatisfiedOriginRelation_Kd` whose
    last edge is a `ResultFit` row in the views where its candidate has an err(ended); nothing else is dropped. Recorded limits:
    `cf-g-reassign` loses an independent report (X-2/OQ9 list), and a guard escape into storage the body does not own reports both
    codes.
  - Owner answers: OQ-1 (c) a `let` slot as a region is a separate BU unit after G:A5-F4 (PLAN G194); OQ-2 (a) the
    preserves-results containment skip is deleted with the old path at R6-check (PLAN G193); OQ-4 (b) as above; OQ-3 (a) the ten
    departures confirmed: finite-source bitsets for A3 §7's one-witness BFS; GuardReverse at a `ResultFit` edge for PD11's second
    half and A3 E7's merge sentence; A3 §7's fixed-atom containment read against Binding's encoding, with MeetNode; three atom
    kinds for the plan's four; `UniversalRelations.For` per body with per-class conditions; T1 by construction; 2a-3 folded;
    rows partitioned at formation, no alias rows; PD11's first half per atom; 2a0-3a's non-call rows only where Binding records a
    failure until H11. Closed without an answer: OQ-B1 (T1 holds by construction), OQ-B3 (the twin's Opaque limit follows from
    OQ11 (b)), OQ-B5 (static Borrow atoms are per site on dev).
  - PLAN rows: G192 (a local-layer Reborrow accepted on the default pipeline), G193, G194, G195 (a callee's meet judged with its
    own atoms), G196 (a `Slice` handle Borrow named by the handle's Origin: unsound on both pipelines, BU's repair) and G197 (SPEC
    11.8's valid `limitRef` rejected).
  - Rejected: B's unconditional guard drop (`guard-annot` accepted on the MIR); twin rows from Binding's chain facts instead of the
    fit's positions; a `NoBody` slice, a body taken from `Use.Parent` or from the Site; every `let` slot a region now (OQ-1 (a));
    MIR reading `Binding.Obligations` itself; a phase switch in `ProvesOriginOutlives` and a MIR-side `Entails` over copied
    premises; environment atoms Inferred before 2c-6 (`env-store` accepted on the MIR); the static row before the enclosing-binder
    row, and a callee's atoms classed Fixed; a merge by Loan membership or by Borrow alone; GuardReverse without the `ResultFit`
    condition (OQ-4 (a)).
- **R4-M95 G160 and G184: raw reach and raw Origins** (owner, 2026-10-21, in a conversation with the planning session; verbatim
  「推奨の仕様で結構です。」 and 「残りの判断事項はすべて推奨どおりで確定します。」, on R4's exit 「案aで決定します。」, on the spelling
  `during self` 「OKです。」; the owner rejected the G160 plan's declaration statement, 「unsafe storage p of T via loan なんて、私の言語的
  許容範囲をオールト越えしている。」; PLAN G160, G184, G147 (1)) The design is `draft/Changes/2026-10-21 G160 Raw Reach and Raw Origins.md`
  (its D1–D20, C1–C3 and units; one independent review round with a re-check, `draft/Sketches/G160/raw-origins-review-1.md`),
  integrated into SPEC §3.3.2, §3.8, §5 (new §5.7), §8.1.2, §8.11.4, §13.5.1, §13.5.3, §15.2–§15.6, §22.6 and Appendix E, with
  LIBRARY's `Raw.slice` row (INTEGRATED, 2026-10-21). In short:
  - Reach (D1, D2): a value carries the holders it addresses beside its Loans. `P@raw` addresses `P`'s root holder when `P` is
    reached through no reference, raw pointers included, and otherwise carries what a Borrow of `P` would without its Loan (past a
    raw pointer, what that pointer reaches); a pointer read from a `during self` Field reaches what `@raw` of its storage would;
    Copies, displacements and raw conversions keep addresses. An address conflicts with nothing and grants no authority; a holder
    an address names is used wherever the address is (no address/content distinction); §5.2.1 condition 4 counts raw pointers. A
    raw pointer that reaches no holder (a `Raw.allocate` result) reaches storage the body does not own.
  - Raw Origins (D3–D5, D11, D13–D18): `raw/T during o` addresses storage that the borrow `o` belongs to covers (a slot's keeping
    Fields, an input's borrow, static storage); `raw/T during self`, only in a struct's or enum's inline storage, the holding
    value's own storage (an own step; the Type is not Copy and counts the pointee as stored content); `raw/T` is unstated. A raw
    Origin in a signature belongs to an input's borrow, a slot or `static`. Typed paths give Origins: `P@raw` and a `during self`
    Field read take the Origin of the last reference the path dereferences (a borrow's, or the one a raw pointer states).
    Dropping or narrowing an Origin is a fit (`raw/T during o <: raw/T`, §3.8); any other change is a claim, only the grouped
    `E@(raw/U during o)` in an unsafe context (§13.5.1 admits it), else a Refuted Origin relation. A write, Take, in-place
    destruction or exclusive Borrow through a pointer whose Origin keeps only shared Loans is an error. A raw Origin is no slot use,
    and a slot it states must not be Phantom; raw Place borrows and `Raw.slice` lie within it; no intersection; the pointer's uses
    add no point to its region.
  - Calls (D6, D10, D15): through a raw layer with an Origin a call may store what the Referent Type of that Origin's borrow
    admits, retained as the borrow would, without a Loan; a raw parameter without one admits its pointee Type; a raw layer without
    one in a result Type (a Tuple element, an Option payload or a Type argument too) may retain every argument and reaches storage
    the body does not own; §8.11.4 rule 1 covers raw layers, inside abstract parts too.
  - Authority (D7, D8, D12): a raw pointer read past a shared layer, or past a raw pointer whose Origin keeps only shared Loans,
    has no write authority, so a call writes through no raw layer reached past one and Binding needs no rows from Fields for
    shape (d); own-step authority (object payloads, `during self` storage) lasts while the owning value stays in its holder (an
    obligation); condition 5: an access that reads or writes a Loan-carrying value acts in a holder the pointer reaches or in
    storage the body does not own, which covers integer-restored pointers and pointers hidden in a nominal result's Fields.
  - C1 (D12's wording: no write authority past a shared layer, in place of 「Origin が uniq のときだけ書ける」; its cost under
    either wording: `Dictionary.insertOrReplace` and `DictionaryStorage.removeEntry`, which G160-3 rewrites), C2 (condition 5 as the
    general rule, an obligation on clients with no diagnostic) and C3 (D11's error covers exclusive Borrows) are the planning
    session's correction, addition and extension, confirmed by the owner (2026-10-21, answering the planning session's questions
    verbatim: C1 「承認 (Recommended)」, C2 「承認 (Recommended)」, C3 「エラーにする (Recommended)」), as is D15's definition after
    review B4 (the borrow an Origin belongs to, the signature restriction and `placeEntry`'s typed handle: 「承認 (Recommended)」).
  - Representations (D19; "Closed vocabularies"): A5's address, a non-conflicting set element naming a holder (a row of the
    conflicts table), and Binding's optional raw Origin with `self` are approved. A new `BindingFailure` entry or diagnostic code
    is decided in its unit, with the owner's approval beyond the existing limits.
  - Units: BU's G160-0 (the located `Unsupported_Kd` for the new forms, in place of the parser's `BorrowOriginSemantics_Kd` and
    `AdaptationOrigin`; the ungrouped `E@raw/U during o` keeps its syntax error `AdaptationDuring`), G160-1 and G160-2 (one design,
    full process), G160-3 (library annotations, `dictionaryStorage`'s `raw/Dictionary<K, V> during value` and `placeEntry`'s
    `raw/Dictionary<K, V>` handle for G147 (1), `Dictionary.tryInsert`, `insertOrReplace` and `DictionaryStorage.removeEntry`
    rewritten to search first and take the exclusive handle afterwards (C1; review R2-B1), the compiler-supplied constructions'
    contracts); R4's G160-4 (A5, full process) and G160-5 (records). R4's
    exit waits for G160-0 to G160-5 (D20, option (a); COMPILER_ARCHITECTURE's stage ledger). Until then G160 and G184 stay recorded
    unsound acceptances, the default pipeline keeps its verdicts until R6-check (D9), and the parser's Language errors for the new
    forms are a recorded limit (STATUS).
  - G160-4's entry supersedes R4-M36's "a raw pointer … hold[s] none", R4-M73's `Raw` bullet and A3 E16's raw clause. The G160 plan
    (`draft/Obsolete/2026-10-21 G160 Raw Storage Kept by Declarations.md`, 完了) is answered: Q1 raw Origins, Q2 the SPEC rule
    above, Q3 a STATUS limit, Q4 confirmed, Q5 addresses carried by values, which removes the widening class its interim was for;
    until G160-4 the read side stays a recorded limit (its option (iii)).
  - Rejected (the design's §7): X-decl (the owner), K′, U, marker presence, a generic marker, integrated storage Types, an implicit
    fit, E-only raw results, liveness that ignores address-only uses, an exposure rule, Binding rows from Fields, a static Move check
    now.
- **R4-M96 A5-F9's R-a: an omitting call's inspection Loans end before its checks; the default taint deleted** (2026-10-21; unit 2e-3
  of G:A5-F9 under R4-M90, the package's §4 (D10) with the design review's root set; supersedes R4-M70's default taint for inline copies
  (template calls were R4-M93's), its role-table row `PendingSlot` → taint and the candidate's two tag sets, and R4-M73's sub-bullet on
  R4-M70's taint and its "Default-taint records" bullet)
  - Rule (SPEC 7.2.3: "Inspection Loans end before callee entry"; 15.6.7): for a Call that moves a PendingSlot s (an operand `Move` of the
    whole root s), the Loans of s's Move Path subtree end before the Call's plan and checks, in the forward solve and the replay. Every
    argument of an omitting call is its moved pending slot (`MirBuilder.Call` routes each explicit argument through `Pending`, a reserved
    one's reservation formed in or moved into its slot; each default's value is `Move(slot)`), so these are exactly the prologue's
    inspection Loans. A template call's inputs are prologue temps (role None), so P9's Content rows still read σ(slot). The root set is
    PendingSlot roots only (the design review's choice; a reservation-holder clause adds no root), pinned by a test of the root kind in
    ReceiverDefaultTest's ReservedMethod shape.
  - Representation: one set per block (`MirBorrows.Endings`' `inspected`: E8's `Subtree` and `MirLoanSets.Range`, one run per moved slot,
    their union), beside E8's per-location `ended`; `Carry` removes it from every holder before the terminator's `Plan` through the one
    removal E8 uses (`End`). No vocabulary entry, no per-head state.
  - Deleted: `Marker.Taint` and `MarkerOf`'s PendingSlot row (the role table keeps Guard); `Flow.Placed` and its calls in `Flow.Plan` and
    `Calls.Call`; the candidate's second `Tags`, its `Taint` and the scan's taint; `Checks.Conflict`'s tainted fallback (an access reports
    its first conflicting candidate); `Reports.Report`'s taint branch, the only `Unsupported_Kd` at a pending slot's source; DIAGNOSTICS'
    taint row.
  - Verdicts (SPEC 7.2.3, 15.6): a copy's value keeps the Loans its slots' values carry (`Issued`'s parents), so a copied existing borrow
    keeps its caller Loan and a conflict on it is the caller's Language problem where R4-M70 gave the located `Unsupported_Kd` at the
    omitting call: round 19's dc-j1, dc-joined and dc-copied, the 1c design's `copied`, `copiedGeneric` and `called`, and an unknown effect
    against a copied static Loan (at the call). A value that keeps a caller Loan only through an ended inspection Loan comes only from an
    invalid template, which R26-borrow reports at its declaration (dc-p15, dc-reserved-bad, dc-gen2-bad-u, `reborrowed`): the omitting call
    adds nothing. Inspecting copies stay valid (dc-reserved-inspect, dc-fe, dc-p13, dc-fa). A held caller's copied conflict is the hold's
    `Unsupported_Kd` at its access (`heldDefault`'s `k = 2`, before at the call). A Loan found in several holders is witnessed by the first
    found (the 1c design's `copiedClean` now names `inspect(y)`, a valid witness; the preference for a clean holder left with the taint).
  - Left: `MirBorrows.Activations.cs`' err(destroy) comment "so a clean Loan is reported first (R4-M70)" is vacuous now; the file is due
    (PD21), so 2e-4 drops the clause under its ruling. PLAN G136 N7 closes with 2e-5's records.
  - Ledger (mir Functional with `KIMI_MIR_DIFF`, against a base run at the unit's start): BorrowedDefaultTest's
    `CopiedExternalLoansRemainLiveAfterTheCall` (48f5bd793398) pairs: the copy taint's `Unsupported_Kd` at `f(n@ref)` goes and the old
    path's `n = 9` becomes both; no other row moved (3,061 rows → 3,060; a5-defaults' new U signature 4 → 3 rows, the
    `CallRetention.Limit` records of G128 left; open blocking rows 239 → 239).
  - Cost: per Call one operand scan in the pre-pass, per moved slot one memoized `Range` and union; per omitting call with inspection
    Loans one memoized `Remove` per holder in each solve pass and in the replay (E8's dense term); deleted: two scope walks per pending-slot
    store and the second tag set per candidate. Corpus (the library, the 44 milestones, the 31 goldens) Work +0.33 %, set bytes −0.04 %
    (Release, against the unit's base). V16 "F9 omitting calls" (`MirBorrowsTest.AnOmittingCallsWorkStaysDense`, N omitting calls in one
    body, each moving a slot its default inspected; VERIFICATION has no V16 table): dense, as E8's ended points × holders, 92,830,
    108,960, 165,923 at N = 16, 32, 64 (91,673, 104,859, 149,790 with the taint). Warm 0 B. Census: no problem.
  - Rejected (the package's §4, round 19): `R19Taint` (the interim: `Unsupported_Kd` for Language errors, records at invalid copies'
    calls); `R19InlinedDrop` (unsound on dc-j1, dc-joined, dc-copied); `R19NoEnding` (second records); a builder `EndBorrow` (E8 ends only
    Loans rooted at what is destroyed); reservation-holder roots (dead code).
  - PD21: `MirBorrows.Solve.cs` and `MirBorrows.Reports.cs` take their third changing unit (2b-3, 2e-2, 2e-3); Reports.cs's ruling or
    split is due before 2e-4 (the package), Solve.cs's before its next change.
- **R4-M97 A5-F9's copy disposition: a failure in an inline default copy is its template's only within one evaluation** (2026-10-21; unit
  2e-4 of G:A5-F9 under R4-M90, the package's §5 (D11–D14) with the planning session's rulings of 2026-10-21; supersedes R4-M69's
  Inlined rule and R4-M10's Inlined sentence for A5, R4-M73's sub-bullet "R4-M69 governs inline default copies" and R4-M92's G171
  interim; closes PLAN G171's defect, whose PLAN record is 2e-5's)
  - Rule (D11; SPEC 7.2.3, 15.6): a failure found in an inline default copy is its template's when every event taking part lies in one
    evaluation of one copy. `MirBorrows.Covered(scope, loan, reservation)`: B, the innermost Inlined scope holding the operation's scope
    and the issue scopes of its Loan and reservation (`MirBody.Common`), moves outward (`Inlining(Outer(B))`) while the Loan or the
    reservation is in Entry(B); it is covered while a copy remains. Entry(B) holds the Loans whose issue lies in B that the state before
    B's entry carries in E and the holders live into it (the replay's live bits before `Advance`), closed as a check set closes: an
    earlier evaluation's (a loop). The conflicts relation's selection (`Checks.Conflict`, now given the operation's scope and its
    reservation) skips a covered candidate and goes on, so a covered first candidate hides no later one; access conflicts, activations,
    err(destroy) and err(effect) select through it. R3 at a Drop takes no Loan (covered inside a copy). Limit records and the
    initialization proof's `Unsupported_Kd` stay at their operation (R4-M69, R4-M71).
  - Entries (the package's implementation note): a per-view pre-pass (`MirBorrows.Entries`, nothing beyond one scan of the scopes for a
    body without an Inlined scope) marks each copy a Loan of the view is issued in, sets every Inlined scope's entry location, its first in
    `MirDataflow.Order` (the replay's order: reached heads in reverse postorder, a chain's blocks in order), which dominates the copy
    (`Inline` maps the template's block 0 into the caller's block; R5-M90's `MirValidator` Entry rule), and marks those locations. In the
    checks replay `Carry` runs one gather at a marked location, before R-a's ending and the plan, shared by the copies entered there; a
    copy that issues no Loan gets none. Representation: by scope an entry location and a `MirLoanSets` set, by location one bit; no
    vocabulary entry, no per-head state.
  - err(ended) (D12): nothing is recorded where `Inlining(Common(EndBorrow's scope, the guard local's declaration scope)) ≥ 0`; no entry
    test (γ_H outlives an evaluation only after its template's own err(ended)).
  - Record identity (D13; SPEC 15.6, 23.1.1): a Loan problem a body owns whose operation lies in a copy has as subject the omitting call of
    the outermost copy (`MirBody.OmittingCall`) and the default's operation as its last fact, `MirFactKind.Evaluated` (the owner-approved
    kind of R4-M90); the witness of a taken value stays the operation. `Record`'s hold applies to every owned failure: a held caller's is
    the located `Unsupported_Kd` at the omitting call. Two calls give two subjects (two records); one record per (code, call) remains,
    U11's residue.
  - Loan identity of the issue (D14): `Loan.Location`, the issuing statement's or terminator's location, replaces `Loan.Site` (a
    `SourceRef`); the anchor is its subject (`MirBorrows.Site`, unchanged), the issue scope its scope.
  - Two ownership forms until 2g-6 (D15 staged; planning session ruling, 2026-10-21: neither 2x-R7 nor a `MirDrops.cs` response had
    landed): A1 and A2 keep `MirFactKind.Inlined` and `MirReport.Add`'s drop; `Evaluated` is A5's only kind for the default's operation.
    Removal condition: 2g-6, after 2x-R7 and a `MirDrops.cs` response; 2e-5 records it in PLAN.
  - Deleted: `Reports.Report`'s `Inlining` call and A5's Inlined fact; `Record`'s held exception for it; `Loan.Site`; the vacuous "so a
    clean Loan is reported first (R4-M70)" in `MirBorrows.Activations.cs`; the Inlined clauses of `MirBorrows.cs` and `.Reports.cs`
    (`MirProblem.cs` names Inlined as A1's and A2's).
  - Verdicts (`MirBorrowsTest.Defaults`; R4-M69 dropped each): the caller's in a copy, at the omitting call: G171 (`State.add(counter@uniq)`,
    the copy's poke activation against the caller's reservation, `CallReservationConflict_Kd` with Reservation and Evaluated facts), its
    effect form, the access in a copy nested in an explicit argument's (at that argument's call), two calls (two records), a covered
    candidate scanned first, a two-view generic caller (both views), a held caller (U at the call); the outer template's own failure met
    in a nested copy (round 20's dt20-f2: f2's record at its `State.g()`). Kept: the disjoint, read and explicit twins, a copy's Limit at
    its operation, a guard across a copy; template-owned omissions (n5, a caller loop around a copy, local Loans only too, R3 in a copy, a
    guard escaping in a copy) with their templates' records.
  - Binding-unreachable rows (SPEC 15.6: a Borrow of mutable static storage is bounded by the body, so no default value or raw store keeps
    one; `UnsatisfiedOriginRelation_Kd`): round 20's dc20-n3, -n3top, -n4, -sib, -twice, -indep, dc20-loop and -loop-nested, and the review's
    dc20r-loop-e, -fx, -slot, -closure, -nested-entry and -loop-guard; a raw Place's anchor is never compared (SPEC 5.2.2). A copy's own
    safe Loan cannot outlive its evaluation on Binding-valid source, so Entry(B) is A5's soundness net for Binding's gaps and G160-4's
    raw holders. Its witnesses are hand-built MIR bodies (planning session ruling (b)): dc20-loop (fail-before), its template form
    (dt20-loop), dc20-loop-nested's omission, dc20-loop-own (the gather reads only holders live into the entry), and the candidate order
    both ways (dc20-first fail-before, -first-rev).
  - Ledger (mir Functional with `KIMI_MIR_DIFF`, against a base run at the unit's start, d39d83f5): no row moved (3,088 raw rows equal
    after normalizing the run and path prefixes; `summary.md` identical; open blocking rows 239 → 239): no Functional program has a
    Loan problem in a copy, as round 20 found of its 506 earlier cases.
  - Cost: per Loan-issuing copy per view one gather (a check's Based pass and the live words) and an `Add` per Loan carried in from an
    earlier evaluation; per view with a copy one pass over the Loans and over the locations (an `Inlining` walk each); per conflicting
    candidate O(depth) walks and a binary search. Corpus (the library, the 44 milestones, the 31 goldens) Work +0.33 % (6,965,058 →
    6,987,845), set bytes unchanged (Release, against the unit's base). V16 "F9 copies" (`MirBorrowsTest.ACopysEntryWorkStaysDense`, N
    Loan-issuing copies at depth 2 in a loop; VERIFICATION has no V16 table): 122,791, 211,975, 548,359 at N = 16, 32, 64, dense as E8's
    ended points × holders (each copy adds a holder); Entry's own terms (against the same build without entries) 1,284, 2,276, 4,452.
    Warm 0 B. Census: no problem.
  - Rejected (the package's §5.2): R4-M69's access-scope rule (unsound, G171); R20Own alone (unsound on loops); B's γ entry test (a second
    record); a copy-on-a-cycle rule; per-evaluation Loan twins; template certificates; copies without Loans; dropping on the template's
    validity. Here: staging Entry(B) until a source witness exists (a departure, and unsound on A5's model where Binding has a gap); one
    entry set per location of every Loan found (an `Add` per caller Loan live there).
  - PD21 (planning session rulings, 2026-10-21): `MirBorrows.Loans.cs`, due (four changing units), split first (behavior-preserving, the
    Loans' group order to `MirBorrows.Groups.cs`; a second consecutive no-split ruling refused), then D14's edit; `MirBorrows.Solve.cs`
    (three units since 2x-R1) no split, being the forward solve and replay driver into which 2e-4 adds the pre-pass and gather calls; its
    next changing unit restructures it first (`Holders()` and its fields the candidate); `MirBorrows.Activations.cs` no split (one
    responsibility; its next changing unit restructures it first; 2x-R9 keeps its 2f-D condition); `MirBorrows.Reports.cs` no split (one
    responsibility; its next changing unit restructures it first); `MirBorrows.Checks.cs` and `.Locations.cs` take their third change;
    `MirProblem.cs` counted under R4-M91's ruling; `MirBody.cs` (R5's hold) gains only `Common` and `OmittingCall`.
- **R4-M98 A5-F9's template rules: the slot step (A3 E17), R26-borrow and R26-access** (2026-10-21; unit 2e-2 of G:A5-F9 under R4-M90,
  the package's §2 (D1–D7) and its precedence table, with the planning session's rulings of 2026-10-21; supersedes R4-M72's Defaults
  bullet and its view skip's form)
  - E17 (SPEC 7.2.3): in a default template a Deref directly on a `PreparedInput` root is the caller's pending slot itself,
    `StepClass.Inline`: one row of `MirBorrows.Walk`, decided by the local's role. A copy's `p_i` are prologue temps (role None), whose
    Deref stays Shared. A Borrow of the slot is a Loan of the template (direct, naming `p_i`, whose state is {Φ_E}); a read through the
    slot gives σ(p_i) = {Φ_E}, as an ordinary parameter's (the shared step gave σ(E)), so a stored slot read is a non-empty value for
    P13's hold as a parameter's is (a held template gives g1's records). A write to the slot itself (`Flow.Store` into `p_i`, `Through`,
    `Ending` as an inline path) arises only on R26-access's error paths.
  - Slot-rooted (D2): a Place rooted at a `PreparedInput`, past its slot step, through no raw pointer's referent (SPEC 15.6.2, 5.2.1: a raw
    Place has its own anchor); both rules read `MirBorrows.SlotRooted` (new `MirBorrows.Defaults.cs`).
  - R26-borrow (D3; PD15 (iv)): at each Return of a template view in the checks replay, one gather with the checks' Based pass and
    closure from E and `_0`'s holder; each slot-rooted Loan found is `DefaultArgumentBorrow_Kd` at its site, once per Loan and view.
  - R26-access (D4): (1) a write, an Assign's or a Call's destination, to a slot-rooted Place, found in `Accessing.Place` before the
    borrowed-root filter, at the write; (2) after the replay, an exclusive (`Uniq`, `TwoPhaseUniq`) slot-rooted Loan that R26-borrow did
    not report, at its site: one code per failure. Every Loan's issue is replayed (`MirValidator` rejects an unreached block, so the
    forward solve reaches every head), so nothing tracks it. Moves stay A1's (R4-M69); ordinary conflicts in a template are independent
    (SPEC 23.1.2); 2g-5 drops an Origin-stratum problem whose chain end R26-borrow reported (the package's precedence table).
  - The view skip (D5): one pre-pass flag, set by each producer's scan in `Loans()`: a Loan, a guard candidate, a Drop of a mutable
    static (R3, 2b-3's term) or a write rooted at a `PreparedInput`; a later producer adds its scan's term.
  - Records (D6, D7): the subject is the Loan's site or the write; the one fact is the parameter, `Anchor` at the `PreparedInput`'s
    location (`Ownership.DefaultArgumentBorrow` or `.DefaultArgumentAccess`; no witness, no new fact kind). D6's reading (planning
    session, 2e-0): a parameter has no node of its own (`FunctionParameterKoto` is not a Koto; a parameter symbol's Declaration is the
    function), so its node is `FunctionParameterKoto.Type`: the written annotation, the `self` shorthand's InferredType on its name, or an
    ErrorKoto with an empty span where the annotation failed. Rejected: parameters as Kotos (node-id churn); a span-carrying SourceRef (a
    new representation). A record whose subject is that Type node shares its SourceRef with any other record there; A6 merges by (code,
    subject, body), so only same-code records merge. `MirBorrows.Record(code, requirement, subject, facts)` applies R4-M73's hold once
    for every producer (the Loan conflict's `Report` builds its facts and calls it), except a problem with an Inlined fact (R4-M69, until
    2e-4; the exception removed by R4-M97): a held template's R26 records are the located `Unsupported_Kd`.
  - Kept: R4-M93's ruling (no raw store row); D2 excludes raw writes (dt-raw-copy valid).
  - Tests: Binding rejects round 19's dt-cap-uniq `[x@uniq]` of an i32 slot (`InvalidAssignment_Kd`), and dt-raw-borrow's `p[0] = x@ref`
    and dt-twice's `x@follow = z@ref` of an i32 slot (`UnsatisfiedOriginRelation_Kd`); `MirBorrowsTest.Defaults` checks the Binding-valid
    forms of the same rule (`[x@follow@uniq]`; a reserved input `z: uniq/i32 during a` stored or written), as the planning session
    confirmed. DefaultAccessTest :28 (`x@follow@ref`) is valid on the MIR (PD15 (i)).
  - PD21 ruling (planning session, 2026-10-21): `MirBorrows.Loans.cs` was due (1c-3, 1c-7, 2b-3 since 8213c83f) and is not split: it has
    one responsibility, the view's pre-pass (its Loans in group order and the per-view facts the skip and the effect rule read; 216
    lines); each of the three added one fact of that pre-pass, and this unit's edit simplifies it (two flags become one, plus the
    slot-write term). Rejected: splitting the one pass mechanically; moving the effect rule's facts, which would set one flag in two files.
    Its next changing unit restructures it first. `MirBorrows.cs` has three changing units (2b-3, 2e-1, 2e-2): a response before 2e-3.
  - Ledger (mir Functional, against a base run at the unit's start): 4 of a5-old-default-access's 5 rows and 10 of
    a5-old-default-borrow's 11 pair (14 old rows become both, e0ffa5a60934, 9fcd0c360b5d, b29228117a54 and dc7ace954713 among them);
    the fifth access row, DefaultAccessTest :26 (6a5c8d8bb5e4), becomes a location row (the old path at `x`, the MIR at the write;
    non-blocking, A6's); the one blocking row left is :28 (c52d8d51a301, `x@follow@ref`), valid by PD15 (i), whose OldRule and (d) row
    are 2e-5's; one new
    non-blocking record in an incomplete compilation (CallReservationTest's `bad(c)`, c4b1e403567a: the default's `c.value = 2`, a failure
    independent of the call's Binding error, SPEC 23.1.2). Open blocking rows 254 → 239; no other row moved.
  - Cost: one role compare per Deref walked and per write; per template view one gather per Return and one pass over its Loans. Templates
    now form their slot Loans, so their views run the existing dense terms (V16 "F9 prepared inputs",
    `MirBorrowsTest.APreparedInputsWorkStaysLinear`: the rules' own work is linear in N; the shape's is dense, as its twin function's).
    Census: no problem in the library, milestones, goldens or accepted spec variants.
  - Rejected: slots as non-references in the builder (the package: a W1 change for an A5 classification); a mode on `Gather` seeding
    `_0`; reading `_0` through the live-before bits at the Return; a per-Loan replayed stamp (dead under the validator); a bool on `Record`
    for the Inlined exception.
- **Owner decisions, only when triggered:** O1 a MIR vocabulary member or `IntrinsicId`; O2 a total cap above 142,000;
  O3 an acceptance change outside plan Appendix B (C1–C6 and X1–X3 with a SPEC clause; T1–T4 belong to R7), any rejection
  of a milestone or library program, or a row reopening a SETTLED entry; O4 a general SPEC rule left Unsupported at the R4
  exit; O5 the unclassified count failing to fall in two sessions in a row (plan §18 P9; superseded by R4-M35).

## Language changes (2026-10-16)

- **PR-1 Place results removed; a subscript follows its selected call's reference** (2026-10-16, planning session; owner
  approved the removal, its timing before R6 and the D5 exception for it; proposal
  [Place Result Removal](../../draft/Changes/2026-10-16%20Place%20Result%20Removal.md), two independent designs compared in
  its 2026-10-15 edition).
  - Adopted: a function result is a Type (SPEC 7.1). `index` returns `ref/Element during self` and `indexUniq`
    `uniq/Element during self`; `receiver[key]` through a conformance designates the referent of the reference the
    selected call returns (SPEC 4.7.1), and a written `index`/`indexUniq` call is an ordinary call returning that
    reference. Every call publishes its plan's result Type; the subscript owner (`Binding.Indexers`, `Binding.Views`)
    types the element once from the selected call's plan (`LentElement`) and publishes it on the subscript, HIR
    `HirPlaceKind.CallResult` has that one producer, and MIR `Referent` reads the selected plan.
  - Deleted: `FunctionResultMode`, `BoundType.ResultMode` and its interning, identity, relation and pass-through sites,
    `CallPlan.ResultMode`, `ResultModeOf`, `PlaceExpected`, `CheckPlaceResultOperand`; `PlaceResultKoto`,
    `KotoKind.PlaceResult`, `SyntaxForm.PlaceResultSuffix` and the `place` contextual keyword; `BindingFailure.PlaceRequired`,
    `PlaceRequired_Kd` and their requirement entries; `ExpectedAdaptationKind.ExclusiveBorrow` (Place publishing only;
    `HirAdaptation.ExclusiveBorrow` stays); the result-mode filters of identification, overrides, closures and Callable fits
    and the Place exclusion of associated-Type evidence; Binding's general Place-call branches, the old paths' general
    Place-call branches (`ReferenceTemporary`, the unacquired Place statement, writes and updates through a Place call)
    and the MIR result-temp branch.
  - Rejected: keeping Place results with a shared Follow inside their owners (result modes stay in Callable, matching and
    inference); Place results only for the index requirements, or as compatibility syntax (+100 tokens; modes stay in
    function Types, CallPlan and matching); `CallPlan.ResultMode` kept for synthesized index calls (the two Types and the
    MIR branch stay); Place results as sugar for a reference plus an implicit Follow at every call position; implicit
    Follow of every reference result (`let r = f()` must keep the reference, SPEC 3.3.3, 3.6); subscripts as ordinary
    calls followed by the user (breaks SPEC 4.7.2 parity); a synthesized Follow node or Deref publication per subscript
    (selection happens after binding and capability comes from the receiver path: two forms of one concept). SETTLED
    records the language-level ones.
  - Timing: the proposal defaulted to after R6; R4 paused, the owner chose to land it now with a D5 exception limited to
    deleting Place-only branches and keying the old paths' subscript branches on the selected call. No temporary
    structure was needed: the old paths recognize the selected call (`ElementAccess.IsIndexerCall`) and read the element the
    subscript publishes (`ElementAccess.DesignatedType`), which goes with them at R6.
  - Earlier reason that no longer holds: the 2026-09-25 Shared Place Results proposal compared Place results with a value result
    whose Move is forbidden, not with a reference result that the index expression follows; the caller's choice it wanted
    (Copy, borrow or replace, as for Array) is what following the returned reference gives, and its §7 had already fixed
    the ABI to that of the reference result.

## Binding units (2026-10-18)

Units of the BU planning session (PLAN G129, G130 and the retention gap found by R4's E14 implementer), run in parallel with R4
and R5 on the owner's request of 2026-10-18; paused by R4-M77, resumed by R4-M78 and paused again by R4-M79 (all 2026-10-19), and
resumed by R4-M86 (2026-10-20).

- **BU-1** (2026-10-18; the BU planning session; correction of R4-M51's Stores clause, found by R4's E14 implementer at
  `f6b365bb`; R4-M51's contract stands) Stores of call retention (SPEC 15.6.4): an Origin occurrence is writable storage of
  its input when `RetentionWalk` has reached an invariant position through storage the call may write (an exclusive or raw
  referent, or an invariant position of a value the call may write, whose nominal Type may hide an exclusive layer) at or
  above it; every occurrence below that position is in it, past any layer, since a value stored there brings its whole
  Type. A shared layer or a non-exclusive pair layer stops the walk from reaching writable storage only before such a
  position (an rc payload held by value and a shared input's referent stay read only, SPEC 15.6.2, 5.2.1); a callable Type
  stores nothing. Why: rc and arc have no outer Origin, so the cut lost their payload's Origins (`Array<rc/S>.append`
  stored nothing, so A5 accepted writing `x` while the Array held `x@ref`), nor has a pair layer admitting only owning
  handles (`s is rc or arc`), so its concrete target had no R2 edge to the writable slot; a borrow layer's writable outer
  Origin, which its referent's Origins outlive (R2), hid the cut for borrows. Supersedes R4-M51's "not past a shared layer … nor past a
  non-exclusive pair layer" below a writable invariant position. Rejected: removing rc and arc from the shared layers (a
  case per form; it would also make an rc payload held by value writable); a store row per handle input. Not temporary.
- **BU-2** (2026-10-18; the BU planning session; PLAN G130, design B with its review's resolutions; the G130 entry, which its
  later units extend) Argument kinds (SPEC 10.5.1) have one owner, the canonical component `Binding.ArgumentKinds`: the kind of
  every argument part (a call argument and, recursively, an element of a Tuple or array literal argument, a written boundary
  of a mixed range and a payload of a contextual `.Case(...)`) is Literal, Composite, Waiting or Typed, and consumers read it
  through one query, `KindOf`; the component prepares a call's arguments and a Composite's parts and walks a Composite for
  slot evidence and for its fit to a candidate. It replaces `Binding.NestedCalls` and `Binding.ArrayArguments`.
  Representation: the kind a pass records for a part, keyed by syntax identity (`SourceRef`), replaces the Koto-keyed
  `waitingNestedCalls` set; the first unit records the nested calls the argument probe defers, and `KindOf` otherwise
  derives the kind from the part's syntax and binding state, as the consumers did. `SlotSolution` is the SPEC 10.8 solve
  state of one candidate (its Type, length and Origin slots and input Origins, scope, Self, declaring Type, explicit Type
  arguments, the fixed-construction flag and the Origin inference its evidence begins) with its own operations: an argument
  part's evidence, substitution of the solution and the judgment of the candidate's Constraints over it. It replaces the
  parallel arrays `TryCandidate` and its local evidence functions took; a Case construction is to supply its own data to it,
  with no branch on a missing function. Owner decisions (2026-10-18, in the BU planning session), implemented by G130's later
  units U2–U5: SPEC 10.5.1 is followed as written, so a contextual `.Case(...)` argument is Waiting and never probed per
  candidate, and a control-flow argument is Literal only when all its result sources are Literal; SPEC 10.2.1's Literal
  timing holds, so a Literal is no evidence during applicability and comparison and its default applies after selection,
  with the ambiguities that follow. Why: each consumer decided an argument's
  kind from syntax, and the element, payload and top-level paths decided it differently (PLAN G130). Rejected: fixing the
  walkers in place (design A's option (i), and fixes inside each walker: the dispatch copies and the two leaf rules for
  elements and top-level arguments stay); a Case construction as a synthesized constructor candidate through `BindCallCore`
  (design B's option (iii): a `FunctionKoto` per Case without syntax and HIR and MIR changes for a single-candidate solve); a
  Koto-keyed kind table (ARCH Code rules, Syntax identity). Open: a construction's unbound slot reports `MissingType`
  (`UnresolvedBinding_Kd`) while a call's reports `UnboundTypeArgument_Kd`. Not temporary.
  - U2, Literal timing (SPEC 10.2.1, 10.5.1; owner decision (2)): a Literal part is no evidence during applicability and
    comparison. At a position that holds an unbound slot it is checked only by the slot-free shape its default would have and
    leaves the slot unbound, so step 2 of 10.4 makes such candidates incomparable. For the selected candidate whose slot no
    evidence binds, `SolveSelected` applies the defaults to its Literal parts at positions that hold the slot (12.3; 4.6.2 rule 3,
    while rule 2, the other boundary's integer Type, stays range typing during candidate evaluation), records each as the part's
    fact (`ArgumentFact.Default`, keyed by `SourceRef`) and runs the candidate's checks once more. A failure is that candidate's
    own record and never reselects: a literal its default does not fit is its own `InvalidNumericLiteral_Kd`, a refuted
    Constraint keeps its fact. Under the argument probe a nested call or construction waits when another candidate is applicable
    or its result holds a Type or Semantics slot no evidence binds (a constructor's result holds all its slots; Origin slots
    never count). Arithmetic operands have their own probe (SPEC 13.3.4), which waits only on an ambiguity or a slot no default
    binds, so `identity(1) + identity(2)` stays typed. Gaps the timing exposed: a Contract argument holding an unbound slot leaves
    its clause unresolved instead of judging the callee's own reference; a pair's target projection `T` mentions its pair slot.
  - U3, one evidence walk and one fit walk (SPEC 10.5.1, 10.2, 10.8; owner decision (a) of PLAN G130): an element of a Tuple or
    array literal argument and a payload of a contextual Case are parameter positions. `PrepareArgument` prepares every part by
    its kind under the argument probe, which now covers elements, so a nested call there waits and a function name stays unbound
    for its position (`AtArgumentPosition`). `SlotSolution.InferArgument` is the one evidence dispatch for arguments and
    elements: a typed part through the common adaptation (a value read, erasure) and, at a Function pattern, its known call
    signature; an anonymous function's written header; a Never-typed part none, and a literal holding one no Type at a bare slot
    (D6). `FitArgument` is the one fit dispatch for call arguments, elements, contextual payloads and value calls
    (`ProbeValueCall`). A waiting element at an `F` of the candidate binds it (`WaitingBindsSlot`) and is completed with `F`'s
    signature after selection (`CompleteWaiting`), as a waiting argument is. Deleted: `InferAggregateCall`,
    `InferAggregateInputs`, `ProbeAggregateArgument`'s leaf and literal branches, `PrepareAggregateArgument`'s binding without
    expectation and `IndependentAggregateType`'s Never evidence. R11 is split (planning session, 2026-10-18): U3 lands its
    expected-result half, so an expected result Never is ordinary evidence (`let x: Never = identity(stop())` gives `T = Never`);
    the structural `Infer` skips a Never component until U5, whose commit removes the skip as it puts Case payloads on
    `InferArgument` (then `h(u)` with `u: (Never, i64)` gives `T = Never`, and `P.Pair(1, ((return 1), a))` and its swapped
    form give `T = i64`: U5's regression rows), since before U5 a Case payload bound whole would make its Never element
    evidence. Departure from the designs' expected record: `g((identity(true), a))` is `NoApplicableOverload_Kd` at `g`, not
    the inner call's error, because `identity(true)` is independently typable (`bool`) and that Type does not fit `i64`.
  - U4, control-flow arguments (SPEC 10.5.1, 14.9.1-14.9.2; owner decision (1), review R5): an `if`, `match`, `loop` or `do`
    part is Literal exactly when every result source is a literal, as ControlFlow's one source enumeration finds it
    (`SurveyResult`, the walk `FindResultEvidence` already makes, now also telling Never sources and fitting each literal
    source to a Type): it fits a candidate when every source does, takes its first literal source's default after selection
    and is completed at the decided Type. Literals beside only Never or unknown sources (`if c => 1 else => stop()`, `if c =>
    identity(1) else => 2`) need an expectation or a default, so the part waits. Any other control flow is bound under the
    argument probe, which reaches its result sources (`ResultTarget`, the inverse of the survey's sources); a source call
    that waits there makes the part wait (`if c => identity(1) else => identity(2)`). `KindOf` reads the recorded Literal or
    Waiting of an unbound part (`WasWaitingPart` replaces `WasWaitingNestedCall`). Arithmetic operands use the same
    predicate (`ResultSurvey.AllLiteral`), so a Never source no longer makes a right operand Literal: `x + (if c => 1 else
    => stop())` with `x: i64` is now `TypeMismatch_Kd`, as the left-operand form already was (accepted by the planning
    session with owner decision (1): SPEC 12.3.3 lets only literal-only expressions, never control flow, fit a provider's
    counterpart, 13.3.4, and 14.9.2 gives the `if` no candidate but its literal's default). Limit (open): the survey binds no
    source, so an unknown typed source beside literals makes the part wait rather than be typed (`keep(if c => foo.bar()
    else => 2)` leaves `T` unbound, where SPEC 14.9.2 gives `foo.bar()`'s Type); it goes with G129's late fit (BU decision
    E13: a join propagates its decided Type into its literal sources, after which such a part can be bound and is Typed).
  - U4b, the parts of a call that completes none (SPEC 23.1.2, 10.1; planning session): a Literal or waiting part is bound
    only once its call selects a candidate. A call that ends unresolved, with no selected candidate or one that failed before
    completing its arguments, binds each argument part still unbound on its own under the argument probe
    (`CompleteUnselected`, from `BindCall`), so the part's own problems are reported beside the call's record, once, and none
    that only the missing expectation causes: a Literal or waiting control-flow part's literal sources wait under the probe
    (`WaitsUnderProbe`) and its source calls defer, and the parts of a Tuple or array literal, a contextual Case and a
    waiting nested call are completed alike; a control-flow part already bound in the pass is not bound again. No candidate
    is selected again; anonymous functions stay under PLAN R1e. Rejected: binding the part before selection (a node bound
    in a pass is not bound again, so its literal sources would keep their defaults, and binding a `match` twice reports its
    arms as covered).
  - U5, Case construction on the walks (SPEC 6.5.2 item 2, 10.8.1, 10.5.1; design B section 5, D7, R1, R9, R11): the slots its
    qualifier and expected Type fix come first; a payload whose payload Type they settle is bound only at it, and every other
    payload is prepared as an argument is and is evidence by its kind through `SlotSolution.InferArgument`, which now serves a
    construction as data (the enum is binder and slot owner; `Binder`, `SlotOwner`, `OriginCount` and `Constructor` are fields,
    and only a candidate's operations read its `Function`). Under the argument probe a construction with an unbound slot waits
    (`takeOpt(Option.Some(1))`); otherwise the Literal defaults apply (`DefaultPayloads`) and are evidence; an unbound slot is
    the construction's own `MissingType` record (Q4 open), on which its waiting payloads depend (`MarkWaitingHeaders`; a part
    that still waits is no prerequisite its call consulted, so `let o = Option.Some(identity(1))` reports that root record).
    Each payload in source order then fits its decided payload Type (`FitArgument`; a misfit is the construction's
    `TypeMismatch_Kd`: `P.Pair(1, [a, a])`) and is completed there (`CompleteArgument`, G129's re-fit point, also used by the
    call path; `ErasePart`, the call path's erasure step) and adapted. Owner decision (1), R1: a contextual `.Case(...)` argument
    is Waiting and never probed per candidate, so `f(.Some(128))` over `Option<i8>` and `Option<i32>` is `AmbiguousBinding_Kd`.
    R11's at-depth half: `Infer` no longer skips a Never component (`h(u)` with `u: (Never, i64)` gives `T = Never`; a Never
    element is still no evidence, so `P.Pair(1, ((return 1), a))` gives `T = i64`). Deleted: the construction's three-pass
    loop and second adaptation loop, `PrepareContextualEnumInputs` and `ProbeContextualEnum`.
  - Completion (U6): G130 closes with U0 (SPEC 10.5.1 text), U1 (the component, behavior unchanged), U2 (Literal timing), U3
    (one evidence and one fit walk), U4 and U4b (control-flow arguments; the parts of a call that completes none) and U5 (Case
    construction; contextual Case waits). Rejected across the units: fixing each walker in place, a synthesized constructor
    candidate per Case, a Koto-keyed kind table, a temporary per-part evidence loop in the construction before U5, binding a
    Literal or waiting control-flow part before selection, and a second source walk for control flow. Open: a construction's
    unbound slot reports `MissingType` while a call's reports `UnboundTypeArgument_Kd` (above); the survey's limit (PLAN
    G152), which G129's late fit removes.
  - U7a, the final review's redo, restructuring (B1, B4, N6; behavior unchanged): `SlotSolution` holds one datum, its
    `Binder`; `SlotOwner`, `Constructor` and `OriginCount` derive from it, and `CallSlotOwner`, `CallSlotCount`,
    `CallMemberPattern` and `CallType` take any binder. Invariant: a construction supplies data, and no operation of the
    shared walks (`Pattern`, `Type`, the evidence walk, the defaults, the waiting-binds-F rule) reads `Function` (B1 was
    `Pattern` reading it). The waiting-binds-F rule (SPEC 10.5, 10.5.4) has one F test (`CallableSlotType`) and one walk
    (`TakeWaiting`) over a part and its Tuple and array literal elements at their positions; its consumers (`IWaitingTaker`)
    are `WaitingSlots`, which removes the slots the callables bind (`UnsolvedSlots`, `WaitingPartsBind`), and
    `SlotSolution.Take`, which completes a callable with S. Replaced: `WaitingBindsSlot`, `WaitingAt`,
    `CompleteWaitingElements`, `SlotSolution.CallableSlot(int)` and the F tests of `TryCandidateCore`, `CompleteWaitingPart`
    and `ExpectedCallSignature`. `CompleteUnselected` keeps its own walk: it binds the parts of a call without a candidate,
    with no position or F. Rejected: a skip-indexed search per waiting callable (quadratic in a literal's waiting
    elements); recording F bindings during the evidence walk (state carried across its passes).
  - U7b, the redo's behavior (B1, B2, B3, N5, N7): a Case construction takes the waiting-binds-F rule from the shared walk.
    `UnsolvedPayloadSlots` leaves out an F that a waiting callable binds (`WaitingSlots`), so the Literal defaults skip it as
    a call's do; once every other slot is bound, the callables are completed with S (`SlotSolution.Take`) and are evidence:
    `let x = Option.Some(func (v: i64) -> i64 => v)` and `W.Wrap(func (v: i64) -> i64 => v, 1)` are typed, and
    `Option.Some(identity)` is the reference's own `UnboundTypeArgument_Kd`. Under the argument probe a construction with any
    unbound slot still waits (U5). The construction owns its payloads' fit: a payload is bound at its decided Type with no
    record of its own (`BindNode`), and a misfit is the construction's one `TypeMismatch_Kd` (B3), as at the merge base;
    `CompleteArgument` is deleted, and the call path keeps `RequireType`, the owner of a completion misfit there. Every
    Literal that its default does not fit is its own record (`DefaultArguments`, `DefaultPayloads`, `DefaultPart`; N5, SPEC
    23.1.2), and `DefaultPayloads` reads the construction's unsolved slots. N7: `Two.Both(1, 2.0)` stays the construction's
    record, as `two(1, 2.0)` is the call's `NoApplicableOverload_Kd`; the merge base's record at `2.0` came from rebinding the
    literal at the decided Type. Rejected: judging a payload in its completion and dropping the construction's check (a
    settled or typed payload's misfit would leave the construction, unlike a call's). Open: under the argument probe a
    construction whose only unbound slots waiting callables bind waits, while such a nested call is typed; SPEC 10.5.1 makes
    the call Waiting when a callable needs S (`takeF(wrap(func (v) => v))` reports `UnresolvedBinding_Kd`, while
    `takeF(Option.Some(func (v) => v))` is accepted), so the call path's deferral is the one to change.
  - U8, one rule for calls and constructions (final review round 2, B5; owner decision 2026-10-18: 「共通の規則にして下さい」;
    SPEC 10.5.1-10.5.4, 10.8 Unbound slots; D4). Under the argument probe a nested call or construction is typed without its
    outer expectation unless its result holds a slot that neither evidence nor a waiting callable fixes. A slot that only a
    waiting callable binds is fixed exactly when the callable needs no S (`FixesWithoutExpectation`, the one predicate both read
    through `OpenWaitingSlots`: a reference that does not need S, SPEC 10.5.3; an anonymous function whose header writes every
    parameter Type and its result Type), or when its F's Callable Constraint gives S, the callee's own (SPEC 10.5.2), which no
    outer expectation supplies. Otherwise the callee waits and its outer candidate types it. So `takeF(Option.Some(func (v:
    i64) -> i64 => v))` and `takeF(wrap(...))` are both typed and both `NoApplicableOverload_Kd`, `keep(Option.Some(...))` and
    `keep(wrap(...))` both valid, and `takeF(Option.Some(func (v) => v))` and `takeF(wrap(func (v) => v))` both wait and are
    valid; this resolves U7b's Open. The call path reads it in `ResultHoldsUnsolved`, now asked of every selected candidate with
    a waiting part (`SolveSelected`), the construction in `HoldsOpenSlot`, replacing U5's deferral on any unbound slot. Selection
    keeps SPEC 10.8's rule that an F a waiting argument binds is no unbound slot (`UnsolvedSlots`, `WaitingSlots`). The three
    older F tests (`ComparableCallableSlots`, `CallableOriginFailure`'s argument lookup, `CheckCallShapeContracts`' argument
    evidence) have that contract and read `CallableSlotType`. Open: the call path still binds a function-reference argument on
    its own when no candidate takes a callable there (`PrepareArgument`'s `TakesCallableContext`), so `wrap(identity)` reports
    the reference's `UnboundTypeArgument_Kd` even at `let o: Option<(i64) -> i64> = wrap(identity)`, while a payload's waits
    (`Option.Some(identity)` there is valid).
- **BU-3** (2026-10-18; the BU planning session; found by R4's E13 implementer at `157bc073`; amends R4-M51's Kind rule and
  narrows its Open note) A pair-layer input in call retention (SPEC 15.6.4 step 4, 8.1.2): a pair layer whose binder admits
  only borrows (ref, uniq, objref, objuniq) is a borrowed input in every admitted case, so `BorrowReferent`, the one
  predicate for a borrowed input, returns its target: its slot (`OuterOrigin`) gives Value and its target Content, which the
  call may write, invariant, when an exclusive borrow is admitted (the pair's `IsInvariantLayer`). The admitted set is the
  binder's, from its declaring scope, read by `LayerAdmitted` for this and `InvariantAdmitted`. A pair admitting owner, an
  object owner or raw is no borrow in that case and keeps the by-value walk. Why: walked by value, `inner11<s/T>(a: s/T, b:
  i32)` under `s is ref or uniq` stored input 0's own Value into its referent (`0 Value store 0`: both name one carrier), so
  after `inner11(x@(s/T), peek(x@ref))` with a non-Copy T A5 made x carry its own Loan and x's scope-end drop erred (A5
  prototype case 139c and the old path accept it); a concrete `uniq/T` input gives `0 Content store 0`, which A5 accepts,
  and now the pair does too. Rejected: no store row at all (the exclusive case may write the referent, and a concrete
  `uniq/T` input keeps its row: dropping it would be a second rule for self-stores); a retention per Semantics case (one
  view per instantiation, R4-M51). Not temporary.
- **BU-4** (2026-10-18; the BU planning session; PLAN G129, whose later units extend this entry) G129 U1: Never's
  capabilities per SPEC 3.1.3. Never is Copy and Owned, Proven by `TryLeafCapability`'s Primitive row (its Never row, which
  answered Unknown, is deleted), so a Type storing Never is decided by its structure (`(i32, Never)`, `[2 of Never]` and
  `Option<Never>` Copy and Owned, `Array<Never>` Owned and Non-Copy by SPEC 4.5); it stays not Sealed, ObjectPayload,
  PrimitiveInteger or Callable, and every declared Contract is refuted for it (`ResolveConformance` drops Never's exemption
  from a primitive's fixed environment). Transferable has no implementation (STATUS Async row). The old analysis's
  `CopyProof` loses its Never parameter (its primitive classification now agrees with Binding); `Place` keeps `neverResult`
  in its Unsupported guard, so every Place of a Type holding Never stays the old path's `Unsupported_Kd` until R6-check.
  Why: Unknown for a closed Type was the old analysis's `InternalInvariant_Kd` (`var t = (1, stop())`, an `Option<Never>`
  local or parameter) and Binding's Move for SPEC 3.1.3's own `let b = a; let c = a` (`TransferRequired_Kd`, A1
  `MovedPlace_Kd`). The rest of G129 (Never fitting at the root, the late fit, R4-M59's removal) follows G130. Rejected:
  design A's MIR Copy for every Never Place (it would hide `g(x@move, x)`; the MIR Never-Place acquisition moved to R5,
  PLAN G139); keeping Never's Unknown leaf (SPEC 3.1.3). Open: generic uses with a Never-holding Type argument that now
  pass Binding under `T is Copy` (`let r = id<Never>`, `f<Option<Never>>(o)` with `o: ref/Option<Never>`) stop at the old
  Emission's existing `GenerationFailed_Kd` rather than a located `Unsupported_Kd`, as the same programs without the
  Constraint already did, until R6. Not temporary.
  - U2, the components of G129's chosen design (E1, E3; restructuring, behavior unchanged): `Binding.ValueFits` owns the
    value-fit entries (`FitsType`, `FitsTypeAt`, `FitsStructurally(At)`, `FitsStructuralPart`, `FitsVerifiedTypeAt`) and the
    use-site fit `CheckTypeUse` with `AddOriginFit`, whose recursion is `CheckTypeUseCore`; the Type relation `FitsTypeCore`
    stays in `Binding.TypeRelations`. `Binding.ResultPropagation` owns a literal's Type at its position's Type, so far α's
    `FittedTupleType`, which `BindTuple` reads. The 14.9.2 survey (`SurveyResult`, `InferResultExpected`, `ResultEvidence`
    and its walks) moves from `Binding.ControlFlow`, which keeps the join, to `Binding.ResultSurvey`, and SPEC 13.5.4's explicit
    numeric conversions from `Binding.Conversions` to `Binding.NumericConversions`, so the late fit and E5's survey rule fit
    the structure limits. Deleted: the unused `Join`.
  - U3, the late fit (E3 with S2 and S5; E13, decided at the pause: a literal result is not fixed before outer constraints, SPEC
    14.9.2 step 3). `Binding.ResultPropagation` owns the Type an expression takes at its position once that is decided. An
    expression awaits its position (`AwaitsPosition`) when it is a numeric literal-only expression, a Tuple or array literal holding
    a part of Type Never or one that awaits, or a result target every value source of which awaits or is Never, through
    parentheses, labels, expression bodies and an Identity or Transfer `@move`/`@copy` (the `@move`/`@copy` clause superseded by
    BU-9). Such a source is no candidate of a 14.9.2
    join (`ResultCandidates`; when none supplies one, the sources' own Types compete), no element evidence of an array or
    Dictionary literal beside an element that is (alone, the awaiting elements' common Type), and at a call evidence only as a
    Literal's default is (late pass, for slots no other evidence binds); it takes the decided Type by its late Type (`LateTypeOf`:
    an array literal its target's element Type only when every element fits it, S2, every other part its own Type; a literal
    waits instead, BU-9), written only when that fits (`TryPropagate`), at the join's decision (`DecideResult`, `PropagateSources`),
    `BindTuple`, the array and Dictionary literals, a call's `CompleteArguments` (S5) and `CompletePayloads`. While a join collects
    its sources after its survey without a Target Result Type (`ResultContext.Collecting`), a numeric literal-only source, directly
    or as a part of a Tuple or array literal source, is not bound (`WaitsForPosition`, BU-9), nor is an array literal source no element of
    which establishes a Type; a selection, `do` or `loop` all of whose sources await, one of them unbound, waits when it is itself
    such a source (`ResultContext.Waiting`; a function's value is itself, not its result) and is decided when its position binds
    it (`DecideWaiting`); otherwise the join binds each waiting source once, at its decided Type, so a misfit is the literal's own
    record (`if c => p.y else => 300` with `p.y: i8`: `InvalidNumericLiteral_Kd` at `300`), or, without a candidate, at its default.
    The completion of a body the parser supplied decides no inferred Target Result Type (a guess, DIAGNOSTICS §4.4).
    `ResultContext.SourceNodes` records each source's node. Effect: R4-M59's joins (`if`, `match`, `loop`, nested, `@move`-wrapped (superseded by BU-9),
    array elements, Dictionary values, S5's call argument) publish their decided Type, and `if c => p.x else => 4000000000`,
    `if c => (a, a) else => (a, 3000000000)` and `let x = if c => identity(a) else => 2` are valid. Rejected: re-typing a literal
    bound at its default (a literal only the join's Type fits is rejected first, and a literal-only operation keeps its default's
    operation); a survey that binds its sources (step 2 compares the independently typable ones). Open: a Dictionary literal source
    of a join takes no late Type (`if c => ["k": (a, a)] else => ["k": (a, (return 4))]`: mir `InternalInvariant_Kd`, as on dev); a
    join under `@move` (superseded by BU-9) or a label does not wait for an outer join, so its literals take their defaults first; a
    control-flow
    argument whose literals sit beside an unknown typed source still waits (PLAN G152).
  - U4, the relation (E1, E2 with S4, S1, S3, E8 with S9). `FitsTypeCore` relates Never only to itself (SPEC 3.8: a Type holding
    Never is an ordinary Type). Never fitting is decided once, at the root of the value-fit entries (`Binding.ValueFits`: `FitsType`,
    `FitsStructurally(At)`, `FitsTypeAt`, `FitsVerifiedTypeAt` and `CheckTypeUse` read `NeverFits`), and `CheckTypeUseCore`'s
    recursion calls only the relation (S1: `let t: Sink<Never> = s@move` with `s: Sink<i32>`, `struct Sink<T>` holding `(T) -> ()`,
    also through `Option<…>`, and SPEC 3.8's `let g: (i32) -> i32 = handler@move` are `TypeMismatch_Kd` on both pipelines).
    Type-to-Type comparisons call the relation's entries (`Binding.TypeRelations`: `IsSubtype`, `IsStructuralSubtype`, `IsSubtypeAt`
    and `FitsStructuralPart`, now relation-only), sorted by contract (S3): slot unification (`Calls`), ranking (`CandidateEvaluation`:
    SPEC 10.4 step 2 is structural identity, so the subtyping `better` branch is deleted and `f(x: Never)` beside `f(x: i32)` for
    `f(stop())` is ambiguous), identity tests (`CompleteTypeIdentity`, `ConstructorValidation`, `FunctionTypes`' written and bound
    Types, `ShapeEvidence`, `CallableSelection`'s signature equality), `CommonOriginType`, `SelectCommonType`, `PropertyMatching`,
    `PairLayers`, `Elements`' key and `Conversions`' borrowed referent; every other caller is a value position and keeps the value
    entries. SPEC 10.7 has one judgment (S4): an implementation is related by subtyping to the required signature whose result is
    Never when the implementation's is (`CompatibleResult`, `CompatibleSignature`; `CallableSignatureFits`, now a `Binding` member
    beside the relation), read by erasure, Callable proofs, a Function Item's contract (`ItemContractFits`, whose inputs now relate
    by subtyping alone) and contract implementation matching (`CallableContracts`). The 14.9.2 join checks an awaiting source by its
    late Type with or without a fixed Target Result Type (no Never fitting below the root); a literal or join waits through @move,
    @copy and labels as through parentheses (`DeliveredOperand`, read from the syntax, is the one walk) and is bound at the decided
    Type operand first (`BindWaiting`) (the @move and @copy clauses superseded by BU-9); without a candidate, the defaulted literal sources supply the candidates
    (`if c => (a, (return 1)) else => (a, 5)` is `(i64, i32)`). E8 (the owner is told): the FunctionReference tests premised on `(i32) -> Never <:
    (i32) -> i32`, which SPEC 3.8 excludes, take premises SPEC 10.4 supports: a generic reference ranks below a non-generic one
    (step 3), an ambiguity between two generic references with identical parameters (steps 2 and 3 tie), an unknown condition of
    the non-generic best reference. Open: `let u: (i64, i64) = (a, (return 2))@move` is `TypeMismatch_Kd`, since @move binds its
    operand without an expected Type (`Binding.Conversions`), so α does not reach the literal (superseded by BU-9: the SPEC verdict); a Callable bound over a Function Item
    whose result is Never stays `NoApplicableOverload_Kd` (E10(3), as on dev); a join whose every source holds Never compares their
    own Types (E10(2)); a Dictionary literal source of a join takes no late Type (U5 decides its MIR side).
- **BU-5** (2026-10-18; the BU planning session; G129 E5, before R4-M59's removal) An anonymous function's result sources
  (SPEC 14.9.1, 14.9.2) and the survey of an operator's result (SPEC 13.3.4). (1) Without a declared or expected result, an
  anonymous function with a single-item body is a result target exactly as one with an indented body: `BeginResult`
  surveys the body's value and every self-targeted `return` operand, the body takes the surveyed Type as an `if` branch
  does, and `FinishResult` joins the operands with the body's value (an indented body: its structural end Unit) and checks
  each. A declared or expected result stays the Target Result Type. (2) The 14.9.2 survey (`ResultEvidence`) gives binary
  `+ - * / %` and unary `-` their operands' terminal Type only on the built-in route, selected when every operand is
  literal-only or has a surveyed Type ending, past safe reference layers, in a built-in numeric Type; a user provider (its
  result is its conformance's Output) or an unknown operand gives no evidence. Comparisons, logical operators and
  assignments are route-independent; bitwise, shift and unary `+` are built-in only and keep their operands' Type. Why: (1)
  the single-item path only bound its body, so a `return` operand was no evidence, no source and unchecked (`func [] (c:
  bool, a: i64) => if c => a else => (return 1)` kept an i32 `1` in an i64 function: `GenerationFailed_Kd`, mir
  `InternalInvariant_Kd`; `(return true)` was accepted; `func [] () => (return 1)` was Never; `try` in a single-item body
  was `InvalidTryReturn_Kd`); (2) the survey typed these operators by `Left ?? Right`, so `return x - y` (Value's
  `Subtractable<Value>.Output is i32`) or `if c => x - x else => -x` took Value and `s * v` (Vec2's
  `LeftMultipliable<f64>`) f64, and (1) would have exposed every single-item body to it. Census (both pipelines, Session
  default and mir, MilestoneSourcesTest, library unchanged): those forms valid; an open-result closure argument also
  reports its own sources' conflict beside `UnboundTypeArgument_Kd` (SPEC 23.1.2). Rejected: a separate return check for
  single-item bodies (a second copy of 14.9); keeping `Left ?? Right` for unknown operands (wrong evidence for an unknown
  user provider). Open: (N1) closed for joins by G129 U3, with which (1) and (2) land (BU-4: a join's literal sources and
  leaves wait for its decided Type, the single-item body included as its function's source):
  `if c => (a, a) else => (1, 2)`, `(return (1, 2))` beside `(a, a)`, `if c => p.x else => 1` and `if c => p.x + n else => 1`
  are valid; a control-flow argument whose literals sit beside an unknown typed source still waits (PLAN G152).
  (N3) `if c => a else => true` as a result source reports `TypeMismatch_Kd` twice, at the `if` and at its target (one per
  cause required); pre-existing in indented bodies, and (1) extends it to single-item bodies. Not temporary.
- **BU-6** (2026-10-18; owner decision, 「推奨の通りでお願いします」, on the BU planning session's question from R5's F12 finding;
  an explicitly authorized SPEC revision of 14.9.3 and 14.10.3, not of SPECJ, GUIDE or history) Every expression of Type
  Never, a read or an explicit acquisition of a Place of Type Never included, supplies no normal completion (SPEC 14.9.3),
  and on an assumed path it continues after it is evaluated with its explicit acquisition taking effect (SPEC 14.10.3:
  `x@move` Moves `x`, so `pass(x@move, x)` with `x: Never` is an error; the new example lines). Binding: StructuralCompletion
  already reads completion from the node's Type, so the HIR Completion of `x@move` is Diverges. One rule (`IsNeverValue`,
  Binding.Conversions): a conversion is Abrupt for a Never value, an operand of Type Never that is no Place, and for a
  conversion that converts the value (`@follow`, `x@i32`, an object creation, `@wrap`, `@bits`); an explicit acquisition of
  a Place of Type Never keeps its operation and its effect: `@move` a Transfer, `@copy` and `x@Never` an Identity (Never is
  Copy, BU-4), `x@ref`, `x@uniq`, `x@(ref/Never)`, `x@(uniq/Never)` and `x@raw` a Borrow or Address of the slot, which
  `@ref`, `@uniq` and `@raw` take whatever it stores, Never included (`BorrowsWrittenSlot`); `AdaptInput`'s shortcut, a
  noncompleting argument forms no borrow, is for implicit adaptations only. So the HIR publishes the inner Place of `x@move`
  acquired by Move and a later bare read as a Copy, and `pass(x@(ref/Never), x@move)` is a Loan conflict on the assumed path.
  The old analysis reports `MovedPlace_Kd` and the Loan conflict where it analyzes the body; MIR's A1 does too since R5's F12
  (PLAN G139, DECISIONS R5-M38). Why: Never has no values, so no
  expression of that Type completes at run time; one rule read from the expression's Type replaces a list of forms, and
  explicit acquisitions stay checked where code cannot run. Principles: one canonical form (one completion rule for every
  Never expression, one acquisition rule for every Place), local reasoning (completion follows from the expression's own
  Type), explicit semantics (`@move` and the borrows keep their meaning on assumed paths). Rejected: SPEC as written (option
  1: a read or explicit acquisition of a Never Place completes structurally, 14.9.3 listing only divergence, Never calls and
  Abort), which would need a second, form-based completion rule beside the Type's and would leave the read after `x@move`
  reachable only by that list. Open: the old analysis has no route for an exclusive reference argument whose referent is
  Never (`ReferenceTypes.IsStorage` excludes Never), so `pass(y@(uniq/Never))` on a `var y` gives its `InternalInvariant_Kd`
  beside its `Unsupported_Kd`, as a `uniq/Never` parameter passed on already did (`h(p: uniq/Never) => g(p)`), on both
  pipelines until R6-check (frozen path; STATUS). Not temporary.
- **BU-8** (2026-10-18; the BU planning session; PLAN G147 (2), design A approved by the planning session the same day, its
  predicate redone after its review) A user index's choice between `index` and `indexUniq` (SPEC 4.7.1) is decided once, by the HIR
  publisher, from the capability of the element Place's use. `ExclusiveUse` (`Binding.Hir.Adjust.cs`) holds exactly when the
  consumer's operation needs exclusive capability of the node, or the node is an assignment's or update's target or the operand of a
  borrow whose Type needs it, or an exclusive use reaches it as the receiver of a Field or an element, as a group's operand or as the
  reference, handle or pair layer a follow selects through (SPEC 13.5.5, 15.6.2), unless that use is of a raw Place, whose capability
  its path does not decide (SPEC 5.2: `*p`, `p[n]`). An operation's need is its target's (`Exclusive`): an exclusive reference or
  object view, or a pair layer admitting one, read from a call receiver's, argument's, Case payload's or writer's adapted or
  parameter Type (SPEC 7.3, 10.2), whatever its adaptation (a PayloadProjection or BaseBorrow states no mode), and from an Adjust
  that is an exclusive borrow, or a Reborrow or pair-case acquisition of a Type that needs it (SPEC 4.7.2, 8.14). The publisher keeps
  these per consumed child (`claims`, by SyntaxId, cleared per module), written where Consume takes the child, the one place the
  operation is known, and read when the child is visited. The plan row's Mode records the choice; `ExpansionPlan` and the builder
  follow it. `Take`: Binding's in-place read of a selection (`ElementAccess.IsSynthesizedAccess`, which `ReceiverElement` records
  when a member's qualifier is bound, before its receiver mode is selected) no longer overrides a consumer needing exclusive
  capability; the Adjust states that operation, as the builder already built it from the expected Type. A Field's, element's or
  follow's operand keeps the recorded read: such a consumer projects in place, neither acquiring it nor reading the read, and its
  capability is the projection's own. Why: Binding binds both calls and selects neither (its capability checks only ask whether
  `indexUniq` is bound, also speculatively), and the old path selects per consumer in OwnershipAnalysis; a list of adaptation kinds
  missed follows, Reborrows, receivers over the recorded read and object payload receivers, so MIR wrote or borrowed exclusively
  past `index`'s shared Deref and A5 accepted conflicts the old path rejects (`let view = entries@ref; change(entries[1]);
  inspect(view)`). The publisher owns the decision until R7 writes the columns inline in Binding. Rejected: the `ExclusiveUse`
  clauses alone (receivers keep the cause); a receiver clause reading the grandparent call (one form; the Adjust keeps misstating
  the acquisition); re-deriving each child's operation at its visit (a second copy of Consume); a Binding set of exclusively used
  subscripts marked at each committed consumer (about seven sites and a second path walk beside `PathAuthority`, a cross-phase
  representation change ahead of R7). Open: a pair-layer element read bare selects `indexUniq` when its binder admits an exclusive
  case, while SPEC 13.5.5 applies each case's rule (the ref case needs only `index`), since Mode is one per node;
  `Binding.EffectBounds.PlaceAccess` drops to Ref past a follow, the same miss on the old effect checker that R6-check deletes (A4
  reads the MIR); Binding accepts an exclusive Reborrow through a `let`-owned subscript (`change(entries[1])`, which SPEC 4.7.1
  denies), rejected only by the old path's TransferRequired; Binding rejects an element receiver through two exclusive layers
  (`values[0].bump()` with `uniq/(uniq/Meter)` elements, `NoApplicableOverload_Kd`, while a local `r.bump()` binds; SPEC 3.4.3 and
  7.3 to be read) and an exclusive base receiver even on a local (`Unsupported_Kd`), so neither reaches the selection;
  `entries[1].inner.mutate()` with `uniq/Outer` elements copies `inner` into a temporary before its `&uniq` on the MIR (that
  receiver is published as a Temporary), so the mutation is lost, while `entries[1]@follow.inner.mutate()` builds correctly, and
  the default pipeline faults there (`CheckFaulted_Kd`, `OwnershipAnalysis.Borrows.cs` 409), fixed by BU-10. Not temporary.
- **BU-9** (2026-10-19; the BU planning session; PLAN G129 design E7, unit U5; supersedes R4-M59, whose removal condition holds)
  MIR builds a literal whose Type holds a Never element at the Type Binding publishes, with no record (`MirBuilder.Aggregate`;
  `HoldsNeverElement`, its branch and its header clause are deleted). Binding publishes that Type through G129's three components:
  the relation (E1, `Binding.TypeRelations`: `FitsTypeCore` relates Never only to itself, SPEC 3.8; Type-to-Type comparisons call
  `IsSubtype`, `IsStructuralSubtype`, `IsSubtypeAt` and `FitsStructuralPart`; SPEC 10.7's one result rule, `CallableSignatureFits`
  with `CompatibleResult`), value fitting (E2, `Binding.ValueFits`: Never fitting only at a value position's root, `NeverFits`), and
  the late fit (E3, `Binding.ResultPropagation`: an expression that awaits its position, `AwaitsPosition`, takes the Type its join,
  its literal's element evidence or its selected call decides, `LateType`, `TryPropagate`, `PropagateSources`; a literal waits
  for it, `WaitsForPosition`, `BindWaiting`). A literal that no position types keeps its own Type, which holds Never only
  where the literal is a fresh value (`let t = (a, (return 1))` is `(i64, Never)`); such a literal never completes at run time. U5
  closes E3's family of aggregate literals: a dictionary literal awaits as Tuple and array literals do (`EntriesAwait`,
  `LateDictionaryType`), and one whose keys or values only literals type waits for its join (`IndependentDictionaryType`), so
  `if c => ["k": a] else => ["k": (return 1)]`, `["k": (a, (return 1))]` beside `["k": (a, a)]` and `["k": 4000000000]` beside
  `["k": p.x]` take the join's Type (before: `UnresolvedBinding_Kd`, `TypeMismatch_Kd`, `InvalidNumericLiteral_Kd`; Ua's mir:
  `InternalInvariant_Kd`). Why: R4-M59 was temporary until Binding published these Types; MIR builds what Binding publishes.
  Rejected: keeping the record for a fresh literal (a valid program, SPEC 3.1.3); `Unsupported_Kd` for a dictionary literal source
  (a third aggregate literal under the same late-fit rule, one row each in `AwaitsPosition` and `LateType`). Open (PLAN G159): a
  join whose every source awaits compares their own Types; 10.7 for a closure declaring `-> Never` and a `Callable` bound over a
  Never-result item; Never-holding Places on the old analysis.
  Final review r1 (2026-10-19; redone, not patched): a literal is bound once. It waits, unbound, wherever its position's Type is
  decided later, found by one walk (`DecidedLater`: through the forms that deliver it and the Tuple, array and dictionary literals
  it is a part of, to an array or dictionary literal collecting its element evidence, `collectingElements`, a join collecting
  without a Target Result Type, or the call deciding a Literal or waiting control-flow argument, `WaitsForCall`); one predicate,
  `WaitsForPosition`, replaces `WaitsForJoin` and `WaitsUnderProbe` (BU-2). Once bound it is decided: `AwaitsPosition` holds only
  while it waits, so the late fit writes no literal (`LateLiteralType` deleted) and a source bound at its default is never
  propagated or checked again (`if c => [1, 4000000000] else => [2]`: the literal's record only, as on dev). `Awaiting` tells a
  part holding a literal, which supplies a default, from one holding only Never parts, which takes the Type the defaults supply
  (`if c => [1, 2] else => [(return 1)]`, rejected on dev, is valid). So `[(a, a), (a, 2000000000 + 2000000000)]`,
  `[(a, a), (a, 4000000000)]`, `[[a], [4000000000]]` and `[a, if c => 1 else => 4000000000]` take the element Type (SPEC 4.3,
  12.3; dev rejects them), and `g(1@move)` with `g(t: i64)` is `NoApplicableOverload_Kd` again, as on dev.
  B1 (the owner, 2026-10-19, choosing the SPEC as written over a SPEC clarification: 12.3.1 propagates expected Types into
  collection elements and selection branches, 13.5.2 lets only a target guide literals): an `@move` or `@copy` operand takes no
  Type from its position, neither an expected Type nor one decided later. @move and @copy are not delivered operands
  (`DeliveredOperand`: parentheses, labels, expression bodies), so neither the late fit nor the deferral passes them, and
  `BindWaiting` binds the source alone. So `if c => (a, a) else => (a, (return 1))@move` is `TypeMismatch_Kd`, with or without
  an expected `(i64, i64)`, and `if c => p.x else => (4000000000)@copy` is `InvalidNumericLiteral_Kd`; written in parentheses
  both take the join's Type. This supersedes the @move and @copy clauses of BU-4's U3 and U4 entries.
- **BU-10** (2026-10-19; the BU planning session; PLAN G147 (2b): designs 1 (B) and 2 (E) compared, an independent design review
  chose E with corrections (E′), adopted by the planning session; the owner's D5 exception and guard the same day; the final review's
  corrections, the owner's whitelist after its second round and its extension after the third) `r[k]` designates the referent of
  its selection reference (SPEC 4.7.1, 4.7.2); a member below it continues through the
  element's own safe value-reference layers (3.4.3), as below a local of Type `sel/E`: its Origin is 10.2's one shared reference, its
  authority the meet of the selection and each layer (15.6.2), never a Take (15.1.5). A recorded in-place read of an element is one
  shared reference to the Type declaring the selected member: for an element storing safe layers, the selection's reference continued
  through them (`ElementRead`), which every writer of an element's in-place read applies: `ReceiverElement` for a user index,
  `CompareInPlace` for a compared user-index element and `SharedElement` before its one-layer collapse (all in
  `Binding.PlaceAuthority.cs`, split from `Binding.ArgumentOperations.cs` with behavior unchanged). The read is a representation,
  never a layer: `ElementAccess.IsSynthesizedAccess` recognizes a SharedBorrow of the node's own Type or of the terminal its layers
  reach (both shapes: one comparison against `ComparisonReferent` alone would also reclassify the `ref/E` read of an element whose
  terminal is no struct or Tuple, such as `uniq/Array<i32>`); `PathAuthority`'s member arm reads the root Place's own Type
  (`LayerAuthority`), its `IsUserIndex`, `IsSynthesizedAccess` and `AccessType` reads deleted; `AccessType`'s exclusive upgrade is
  refused when the Place's own Type has a shared layer and taken when the path continued through the Place's own layers is Uniq
  (`ReferentAuthority`; the design review's correction: an owned `Array<uniq/S>` element, `var` or `let`, is Uniq through its stored
  reference). Why: `ReceiverElement` recorded `ref/E`, the selection, for any E, and every reader peeled one layer from it, so with
  E `uniq/Point` `entries[1].inner` was no Place (the MIR copied `inner` before its `&uniq`: a lost mutation), `PathAuthority`
  answered Owner (Binding accepted `keep(entries[1].inner@move)` and a write through a `ref/Point` element), `entries[1].0` was
  `Unsupported_Kd` and the old path faulted; `CompareInPlace` wrote the selection again for a compared part (`entries[1].name ==
  "a"`: the MIR copied the string out and dropped it); `SharedElement`'s collapse was read as a shared layer (`vals[0].n = 5` on
  `Array<uniq/Point>` rejected). D5 exception (owner, 2026-10-19, "Exception + old-analysis guard"): the old paths accept the forms
  the correction makes valid where the old Emission generates them (checked natively at O0 on both pipelines: parts below one stored
  layer of a user index's element as receivers, writes, updates, Tuple elements and returned borrows, and shared reads and compared
  strings below an element); the guard in `OwnershipAnalysis.Receiver` reports the located `Unsupported_Kd` at the element for a
  recorded read through more than one stored layer, or through one outside the owner's whitelist (2026-10-19, after the final
  review's second round; extended to shared reads by the planning session after its third; `LowersElementRead`, an evidence-based
  list). Below a user index's element: an exclusive read when the receiver path is a local, parameter or capture, a stored Field of
  such a path, a Tuple element reached through a reference (the referent of a `uniq/` parameter) or another user index's element
  on such a path; a shared read whatever the receiver path (shown on those paths, a local's Tuple element and a built-in element).
  Below a built-in element: only a shared read whose sequence is a local, parameter or capture. A Field or Tuple element read in
  place for a comparison (`h.ptr.name == "a"`) is no element: as before G147 (2b), only its layers count. Outside it, for example:
  `vals[0].n = 5`, `vals[0][1].n = 5`, `t.0[1].n = 5`, and the reads `t.0[0].n`, `h.vals[0].n`, `vals[0][0].n`. The guard goes
  with the old analysis at R6-check. Rejected: A, `IsBarePlace` reading the node's Type for a synthesized read
  (one form; `PathAuthority` stays Owner, so Take and writes through `ref/` stay accepted); B, no read for a layered element (drops
  the selection's dependency: `return entries[1].inner@uniq` through `uniq/Dictionary<i32, uniq/Point during a>` accepted `during
  a`, unsound); C, Binding's predicates no longer reading a recorded read (rewrites ten `ElementAccess` readers that the old path
  shares; the direction for R6-check and R7); D, a separate published element-Place fact (R7). Open: `let`-owned subscripts (BU-8; fixed by BU-13); a
  direct receiver through a built-in element's stored `uniq/` (`vals[0].bump()`) passes checking and fails generation, pre-existing
  and not a recorded read; a receiver through two stored layers stays `NoApplicableOverload_Kd` (BU-8); PLAN G147 (2) lists an `rc/`
  handle's PathAuthority and a false `InvalidAssignment_Kd` below `Array<Dictionary<i32, Point>>` (both fixed by BU-13). Temporary: the guard (R6-check).
- **BU-11** (2026-10-19; the BU planning session; PLAN G153 (2), found by R5's K6 review; option A1 of the BU6 design, chosen by
  the planning session the same day; R5-M37's Layout memo is the precedent) Binding's storage bound (PLAN G128: `TyCtxt.Apply`
  refuses an instance whose stored Types nest more than 64 levels, R4-M51's located `Unsupported_Kd`, the capability row
  StorageNesting) is a property of each instance. `StorageHeight(type, budget)` (`Binding.Structs.cs`) gives the levels the
  storage walk reaches below a Type (0 for a
  scalar; 1 + its highest base, Field, Case payload Tuple or Type component), or -1 when a part does not substitute or the height
  exceeds the budget; `PrepareTypeStorage` admits a height of at most 64 and is the one entry. New state:
  `AdtDef.InstanceStorage.Height`, valid while `Prepared` is the current pass. A struct or enum is stamped at height 0 when the
  walk enters it, so an instance reached again through its own storage counts 0; it keeps its height only once its base, Fields,
  Cases and Type components all passed; one that fails keeps nothing and fails the query. A later query compares the kept height with
  its own budget. Why: the stamp recorded success at the depth of the first walk and survived a failing Type component, so
  `h<Box^62>` was the gap alone and an instance beside `h<Box^61>` in either order, a Type stored twice passed or failed by Field
  order (`AB<Box^57, Box^4<Box^57>>`), and `Tag<Array^61<i32>>`, whose `T` no Field stores, was refused once, then admitted.
  Known residual (pre-existing, as before BU-11; PLAN G153 (2)): a member of a storage cycle keeps the height computed with the
  cycle cut at 0 where its first walk entered, so cyclic storage still depends on order: near the bound (`Option<rc/L<Box^61>>`
  after `L<Box^61>`), and at any depth when the cycle's entry then fails, since the member's kept height counted the failed entry
  as 0 (with `R<T> { x: X<T>, y: Box^200<T> }` and `X<T> { r: Option<rc/R<T>> }`, `h<X<i32>>` is the gap alone and passes after
  `h<R<i32>>`); A2 removes both. The bound counts levels through handles, so a ring of 17 non-generic structs linked by
  `Option<rc/…>` is refused. Rejected: clearing the stamp when a Type component fails, alone (the memo still ignores depth); no
  memo below the root (exponential over shared parts, `Pair^25`); not keeping cycle members (exponential over mutually recursive
  Types); heights shared per strongly connected component with a growth cap on the walk (A2) and inline-only preparation (B),
  which change what G128 and PD30 define the bound to mean. Not temporary (G128's bound goes at R7). Superseded by BU-12 in its
  representation (`InstanceStorage.Height`, `Prepared`) and its residual; its rule for acyclic storage stands.
- **BU-12** (2026-10-19; the BU planning session; PLAN G153 (2); design A2 (BU6b), whose direction, cap, reporting and residual
  the owner chose on 2026-10-19, approved with changes by its design review; supersedes BU-11's representation and residual, whose
  rule for acyclic storage stands; R5-M37's memo is the precedent) Binding's storage preparation (PLAN G128) decides a Type by the
  strongly connected components of its storage. The nodes are the Types a storage walk meets (a struct's or enum's base, Fields and
  Case payload Tuples, every Type's components); an edge between two members of a component counts 0, an edge to a non-member
  1 + its height, and every member has its component's height, its largest exit. `PrepareTypeStorage` admits a Type whose height is
  at most 64 and every Type it reaches substitutes, so the verdict depends on the Type alone: the failed-entry case
  (`R<T> { x: X<T>, y: Box^200<T> }`, `X<T> { r: Option<rc/R<T>> }`) is refused in every order, `Option<rc/L<Box^61>>` is decided
  with `L<Box^61>` (height 62), and a ring of structs linked by `Option<rc/…>` has height 1. A growing Type never closes a
  component, so the walk's path holds at most 256 Types, the root included (the owner's value); a query that passes it is refused.
  A capped walk and a height over 64 are G128's implementation limits: `TyCtxt.Apply` gives no Type, which is R4-M51's located
  `Unsupported_Kd` (the capability row StorageNesting) at the requesting site by R5-M40's rule (the R5/R6 plan's PD30 for the
  collector's mapping) and R5-M67's Limit (K6, K6b) when `PrepareTypeStorage(Self)` fails. IMPL 21.3.2 makes an exceeded limit
  Resource: `Unsupported_Kd` is the owner's interim choice until R7 removes storage preparation from `Apply`. The walk
  (`Binding.Structs.cs`, Tarjan): one per-pass table of marks by Type (`storageMarks`: a finished Type's height or Refused, an open
  Type's stack position), cleared in `ResetPass`, and one stack, empty at every query's entry (a query during a walk is a broken
  contract: `Instance` substitution never queries storage); structural Types are marked too, so the walk is linear in the distinct
  Types it meets; a refusal marks every open Type Refused (each reaches the refusing part), a capped walk unmarks them. Corpus (the
  suite with the library and the 44 milestones; 5.1 M default and 22.6 M mir queries): no verdict differs from BU-11's; the largest
  cyclic component holds 4 Types. Rings of 17–42 structs build; from 43 the collector's glue-depth limit gives
  `GenerationResourceLimit_Kd` at the destroyed value (BU-11 also gave the gap at the call). Cost: warm Bind unchanged with 0 B;
  without a growing Type, cold +4 KB (Bind) to +44 KB (mir check) for the table; a growing Type's first walk retains about 230 KB of
  interned Types for the compilation (BU-11 about 22 KB) in 0.65–0.84 ms, a re-walk takes 0.07 ms in the same pass and 0.45 ms in
  the next. Residual (accepted by the owner): past the cap the verdict can depend on which other Types the program prepares, a pure
  ring too: a 63-ring behind `h<Box^4<S1>>` is refused alone and twice, and admitted when `h<S1>` or `h<Option<rc/S1>>` appears
  anywhere. Rejected: Tarjan over struct and enum instances only (a growing Type's Tuple arguments walked as trees within the cap);
  a cap on the Types one query meets, or on a component's size (order-dependent; growth never closes); keeping a capped walk's
  Types refused (order-dependent); counting hops inside a cycle (BU-11); a component height counting its internal distance (the
  longest simple path); a declaration-level expansive-cycle check (it cannot see growth through associated projections);
  inline-only preparation (B: changes G128 and breaks R4-M58, whose growing composed atom `Apply` still refuses). Deleted:
  `InstanceStorage.Prepared` and `Height`. Not temporary (G128's limits go at R7).
- **BU-13** (2026-10-19; the BU planning session; PLAN G147 (2)'s Open; option A of the BU7 diagnosis, chosen by the planning
  session, with R4's regression rows) A selection that reaches its element through `indexUniq` (a user index or an exclusive view;
  an owned `Array` element, SPEC 4.7.1) needs its receiver exclusively acquired, which storage rooted in a `let` binding or an owned
  parameter, or reached through a shared layer, cannot be (SPEC 15.1.5, 15.6.2). One predicate decides it, `ExclusivelyAcquirable`
  (`Binding.PlaceAuthority.cs`): the receiver's path continued through its own layers is not shared (`ReferentAuthority`) and its
  binding permits it (`AcquirableByBinding`: an exclusive reference, also held in a `let`; a writable Place; an inline part of a
  writable root, `ElementAccess.WritableRoot`; a Temporary Value). The `indexUniq` binding (`Binding.Indexers.cs`) and the exclusive
  view (`Binding.Views.cs`, whose copy goes) select by it; `PathAuthority`'s user-index and owned-`Array`-element arms apply its
  binding half and let the rest of their walk decide the path, so a denied selection shares the element (Ref). `PathAuthority`'s
  member arm reads every member's receiver through all layers of the receiver's own Type (`LayerAuthority`: rc, arc and objref
  views shared, a pair layer by its weakest admitted case, SPEC 13.5.5), replacing `BorrowedPathRoot`, whose miss of `ref/(rc/T)`
  and similar receivers fell to Owner; `IsBarePlace` finds a member's declaring Type below every layer (a handle's View Target), so
  `p.inner@move` through `ref/(rc/Point)` is a Place without Take. The walk returns the authority its layers grant and, apart,
  whether a binding denies a selection's receiver: `PathAuthority` bounds the path to Ref then, while a Take, which acquires
  nothing exclusively, reads the layers alone (`OffersTake`), so its diagnostic is unchanged (`ExclusivePathTake_Kd` below a user
  index, `StaticMovePathRequired_Kd` for an `Array` element). `AccessFailure` names a binding's denial `InvalidAssignment_Kd` (as
  `let pair; pair[0] = 3` and `let slot = entries[1]@uniq` were) and a shared layer `SharedPathAccess_Kd`; a receiver or Reborrow
  fails overload resolution (`NoApplicableOverload_Kd`). Why: Binding accepted writes, exclusive borrows,
  receivers and Reborrows through `let`-owned or owned-parameter subscripts and owned `Array` elements (the MIR wrote past
  `index`, the old path reported `TransferRequired_Kd` or `Unsupported_Kd`), and writes and Takes below an rc, arc or obj view
  behind a reference or an element; `t.0[1].n = 5` and `vals[0][1].n = 5` below a `var` root selected `index` (wrong code on the
  MIR, a false `InvalidAssignment_Kd` for an owned element). BU-10's guard still excludes those `var` forms; the old Emission
  generates them correctly once they select `indexUniq` (O0, both pipelines). Rejected: B, a fourth `PathAuthority` outcome for a
  binding's denial, read by about 25 consumers, with a handle view recorded by `ReceiverThroughLayers` (shared structure for one
  diagnostic distinction). Not temporary.
- **BU-14** (2026-10-19; the BU planning session; PLAN G100's Subject item, A5 step 2 plan 2x-E7: designs 1 and 2 compared, R4's
  independent design review adopted design 2 with corrections F1–F4 and N2–N4, N6; owner decisions of 2026-10-19 below) Every
  Pattern position of a match arm or a `for` decomposition reaches its Place by one layer step, `Binding.SelectLayers`
  (`Binding.Patterns.Layers.cs`, a canonical component), which carries SPEC 13.5.5's state as `PatternReach` (the mode reached,
  the dependency, and whether the Place may lie in the owned Subject or item). A structural position selects every safe
  value-reference layer that lacks its structure; a Wildcard (a `for` slot `_` included) and a grouping select nothing; a Binding
  selects nothing, except the qualifying pair layer of a Place it reaches with owned access, which offers no Take: it binds `ref/T` (ref admitted) or `uniq/T` of the layer's selected Place (13.5.5's mode
  row; `try` extracts a value, SPEC 17.3: `BoundMatch.ExtractsValue`). The pair step's dependency is `PairSlot`'s, the one case
  split `PairOrigin` also uses: the Place reached (at an owned Subject or item reached by no layer, its Subject Place's atom,
  `OriginAtom(match, Projection, 0)`) or the layer's outer slot met with the borrows reached. `BoundPattern.OwnedPart` publishes
  whether a position's Place may lie in the owned Subject or item (true at the root of a ByValue Subject or an owned item, kept only
  through pair layers admitting owner). Its readers: Binding's 11.5 check (an `Exclusive` binding with `OwnedPart` in a match on a
  getter result is `InvalidAssignment_Kd` at the binding, SPEC 15.1.6's example), `EffectBounds.LeftParts` (a part borrowed past such
  a layer stays in the Subject) and the old-path guard. The guard candidate is built from the selected Type (14.8.3: the body
  binding's Place). `Binding.Sequences` decomposes an item through the same step (its copy of the walk, which selected no pair
  layer, is deleted). HIR: a Pattern binding's PlaceShape publishes its follows (`Layers`, R5's consent; the builder lends the
  binding by its Type through `Lent`, no MIR change). Owner decisions: (b) SPEC is clarified (14.8.1: a Binding reached with
  by-value access at a qualifying pair layer binds a reference to the selected Place in the mode past it; 3.4.3's single-name
  binding is a `let` or `var` declaration or a single `for` slot); (d) a pair layer that is not qualifying, such as one admitting
  Object Semantics, binds its slot by value (13.5.5 and 15.1.6 scoped to qualifying layers; no code change); D5 exception until
  R6-check: the old analysis (`OwnershipAnalysis.SupportsMatch`) reports the located `Unsupported_Kd` at the match or loop for a
  Binding position with `OwnedPart` and access other than Owned (F1: the old analysis has no Place for a borrow into the owned
  Subject; a structural position only tests, a Wildcard selects nothing), and for a Binding that selects a pair layer (the old Emission cannot generate it: `GenerationFailed_Kd` for the uniq-only
  admitted sets F1's predicate leaves); the guard extends a pre-existing cascade, `UninitializedPlace_Kd` beside the
  `Unsupported_Kd` when every arm returns (N1, PLAN). Second owner decision (2026-10-19, re-approval of the D5 exception on a
  corrected premise): forms the default pipeline accepted and ran on dev become its located `Unsupported_Kd` until R6-check: a
  Binding at a pair-layer slot of an owned Subject or item, which dev bound by value (`match pair@move` `(let x, _) => use(x)`,
  `s is owner or uniq` or `s is uniq`; `for (x, _) in arr@move`), and a structural follow from an owned Subject past a layer
  admitting owner (`match o@move` `.Some(let n) => n@follow = 5`, `o: s/Option<i32>`, owner or uniq: dev accepted it and its O0
  build ran correctly in both instances; the design review's statement that dev rejected it was wrong); no library or milestone
  program has such a form. The guard as built is F1's predicate at Binding positions (`OwnedPart` and access other than Owned)
  plus a Binding that selects a pair layer (the final review's correction: a Wildcard at a pair layer and a structural test with
  no binding below, `(_, let n)`, `.Some(_)`, `_`, `for (_, n)`, stay judged and generated as on dev); the second part's reproducer: `match pair@move` `(let x, _) => x@follow = value` under `s is uniq` (no
  `OwnedPart`), whose old-path build gave `GenerationFailed_Kd` ("Selected composite acquisition does not follow its Pattern
  tree", instance `set<uniq/i32>`) before that part. Why: a Binding at a pair layer took its slot by value, so the SPEC 15.1.6
  example was accepted, an owned Subject's pair step had no Origin (false `NoApplicableOverload_Kd`) and the `for` copy rejected
  pair-layer Tuple items (false `TypeMismatch_Kd`). Rejected: design 1 (a builder follow beside `Lent` in held files, a shared
  column's contract changed across phases, the getter check through `BorrowableTemporary`'s `spelled`); binding the slot
  (`uniq/(s/T)`); an `OwnedPart` query re-walking the followed layers; teaching the old Emission the follow (D5); the Subject's own
  mode for pair-typed non-bare Subjects (PLAN, S6: needs a borrowed temporary Subject). Not temporary, except the guard.
- **BU-15** (2026-10-19; the BU planning session; A5 step 2 plan units 2x-E4a and 2x-E6, PLAN G100; the shared-structure design,
  adopted by the planning session and endorsed by R4, narrowed after its independent review blocked the construction grant (R4's
  variant (a)); R5 consented to the two `Binding.Expressions.cs` hunks) What the receiver `self` of a constructor or destructor
  grants is one decision, `Receiver` (`Binding.Structs.cs`), a table with a row per receiver that every Place check reads. Every
  special receiver writes its own initialized Fields (`Writable`), no part of it gives up a Non-Copy value (`CompleteTransfer`, nested
  parts included, SPEC 6.3.4) and no capture entry takes it (`Binding.Closures`). Columns: `Parts`, the authority its path gives every
  part (`PathAuthority`); `Whole`, whether the general rules judge a use of the whole receiver (`BindReference`); `LendsFields`,
  whether its writable Fields are lent exclusively (`BorrowablePlace`). Destruction (16.3.1): access equivalent to `uniq/Self`, so
  no part offers Take, a Copy one included (15.1.3, 15.1.5), and of the whole receiver only a shared borrow is valid: a Move is
  `ExclusivePathTake_Kd`, a bare owned argument `TransferRequired_Kd`, `@copy` `NonCopyOperand_Kd`, `@uniq`, assignment and
  exchange `InvalidAssignment_Kd`, a `uniq/Self` argument `NoApplicableOverload_Kd`; its Fields are lent exclusively. Construction
  (6.3.4): an owner's path, the whole receiver never named, and its Fields not lent exclusively although SPEC lends them: no
  analysis models those Loans (the review's B1 to B3: conflicts accepted by the old path, a false conflict at a parameter read, an
  internal fault, an escape the MIR accepts), so `AccessFailure` names an exclusive borrow of a writable part of a receiver whose
  row does not lend it a located `Unsupported_Kd` (explicit `@uniq`, `exchange`, `replace`, Exclusive closure calls); a Receiver
  Expression stays overload resolution's `NoApplicableOverload_Kd` (BU-13). Temporary: `LendsFields` goes when PLAN G166 grants
  the construction row. Readers, each replacing a clause that re-decided part of the rule: `PathAuthority` (no destruction root:
  Moves out of drop's `self` passed), `BorrowablePlace` (a destructor-only clause), `Writable` (`!IsSpecialField`),
  `CompleteTransfer` (direct Fields only), `BindReference` (both receivers' whole uses rejected) and `Binding.Closures`' own
  identity test; `IsSpecialField` keeps the identity for the construction rules of 6.3.4 and 11.7. Default-pipeline verdicts
  against dev 2aec6b83: drop: a Copy Field's `@move` (accepted) and other part Moves (`Unsupported_Kd`) become `ExclusivePathTake_Kd`;
  `self@ref` and a `ref/Self` argument (`InvalidAssignment_Kd`) the old path's `Unsupported_Kd`; constructor: an exclusive Field
  borrow `InvalidAssignment_Kd` becomes `Unsupported_Kd` (6.3.4's example, spec-cases row 6.3.4), a nested Non-Copy Move
  (`Unsupported_Kd`) `InvalidAssignment_Kd`. Library, milestones and the diagnostic corpus unchanged. 2x-P10's d09 (`self@move`)
  gets `ExclusivePathTake_Kd`, not its model's `InvalidAssignment_Kd` (R4's 2g-D). Rejected: one `PathAuthority` arm (leaves the
  other copies); typing drop's `self` as `uniq/Self` (`self@ref` would borrow the slot; changes MIR, the old path and generation);
  extending the constructor clause of `CompleteTransfer` to the destructor; Uniq for the construction row (refuses a Copy Field's
  `@move`, which 6.3.4 forbids only for Non-Copy values); granting the construction row now (the review's B1 to B3). Open: PLAN
  G163 (capture of the helper borrow; every borrow in a `drop` body has the Origin `Projection[drop#0]`), G164 (writes below a
  receiver's Field, `ElementAccess.WritableRoot`), G165 (old-path acceptance of Field borrow conflicts in `drop`), G166.
- **BU-16** (2026-10-21; the BU planning session; PLAN G147 (2)'s generic pair parameter; design v3, after the implementer stopped
  v1 and the review blocked v2's Take half) A Place selected through a layer of a receiver's own Type gets each capability by every
  case the layer admits (SPEC 15.1.5, 13.5.5), decided by the capability's existing owner. Take: no path past such a layer (a
  reference, an object handle or view, a pair layer) offers Take. `PathAuthority`'s walk reports the cases of the layers it passes
  (`LayerCases`: a pair layer's admitted set, any other layer's Semantics; set by the Follow, PayloadFollow and PairFollow arms and
  for a member's receiver; the user-index and Array/Dictionary arms already grant Uniq or Ref); `OffersTake` offers Take on an
  Owner path that passed none, and `AccessFailure` keeps each layer kind's code: past a pair layer admitting owner (only a pair
  layer adds that case) `ExclusivePathTake_Kd`, below an object payload `InvalidAssignment_Kd` (as `h@follow@move`), past a
  reference its authority's. The authority itself is unchanged. Write: `Writable`'s owned-root Field clause applies when the
  receiver's own Type adds no exclusive or shared layer (`LayerAuthority` null) and the Type below its layers is a struct: an owned
  struct, an owning obj handle or a pair layer whose owner case adds none (no member is selected through a raw pointer). Exclusive
  borrow: `BorrowablePlace`'s stored-Property clause lends a Field exclusively past an exclusive layer of the receiver's own Type, a
  uniq-only pair layer included, shares it past another reference or view, and otherwise asks the receiver Place. Why: an
  owner-admitting pair layer and an owning obj handle have no authority (they keep the root's write and borrow capability), so the
  walk offered Take past them (`p.inner@move`, `p.v@move`; `var h = P.init(1)@obj; let y = h.v@move` accepted end to end, and
  `h.inner@move`, `h@follow.inner@move`, `consume(h.inner@move)` rejected only by the old path's `TransferRequired_Kd`), and both
  clauses read the receiver Type's shape, so a pair-layer receiver skipped the owned-root rule (`p.v = 5`, `p.v++`, `p.inner.w = 5`,
  `let q = p@move; q.v = 5`, `x.item = y@move`, `p` and `x` owned parameters in the owner case; unsound on the MIR) and a uniq-only
  one lent no Field exclusively (`let r = p.v@uniq` refused). Deleted: `OffersTake`'s syntactic Follow, PayloadFollow, PairFollow
  and guard-candidate exclusions and `CompleteTransfer`'s `PairFollow` failure rewrite, which decided explicit follows alone by
  syntax. Kept: the `PairFollow` clauses of `Writable` and `BorrowablePlace` (`PairCapability`, the follow node's own rule).
  Rejected: v1, BU-13's binding denial for the owner case (a binding fact about an inline Place is no shared layer: every consumer
  that follows a `uniq` reference stored below the step, `Writable`'s Follow clause, `PairCapability`, `TryPairReceiver`,
  `PairSubject`, `ReceiverThroughLayers` and `ExclusivelyAcquirable`, refused forms the concrete owned twin accepts, such as
  `x.item@follow = 5` with `x: s/Box<uniq/i32 during b>`); v1-(i), skipping that denial by the queried Place's own Type (one Place
  answered by its consumer; `x.item = y@move` stays accepted); v1-(ii), every follow consumer asking a referent question (larger,
  no extra form once the Field clause closes `x.item = y@move`); v2's pair-only Take flag (one case of the open family: object
  payloads kept it); a fourth `PathAuthority` outcome (BU-13's B). Default pipeline: these forms get Binding's code in place of the
  old path's `Unsupported_Kd` or `TransferRequired_Kd`; `let r = p.v@uniq` (`s is uniq`) passes Binding and gets the old path's
  `Unsupported_Kd` (D5). Tuple and fixed-array elements through a pair layer stay Binding's `Unsupported_Kd` (`Binding.Elements`).
  Library, milestones and the diagnostic corpus unchanged. Open: PLAN G164 (valid element writes below a pair-layer receiver) and
  G179 (a user index through a pair layer). Not temporary.
- **BU-17** (2026-10-21; the BU planning session; PLAN G163 under the full process: designs A and B, the chosen design, a design
  review (APPROVE WITH CHANGES), two revisions after the implementer's robustness rows, design review round 2 (APPROVE WITH
  CHANGES); the owner's answers of 2026-10-21) SPEC 16.3.1's helper borrow, a shared borrow of the whole destruction receiver, is
  never captured. Representation: its Origin is `H = Projection[drop#HelperSlot]` (`HelperSlot = int.MinValue + 1`, beside
  `CallResultSlot` and `ClosureValueSlot`; the binder is always a destructor, never a value or a Closure, so no reader of an
  environment or closure-value slot meets it). The receiver table decides it: `HelperOrigin` (`Binding.Structs`) answers for the
  bare receiver of a row with `Whole: true`, which now also means "a borrow of the whole receiver is the helper borrow"; `PlaceOrigin`
  and `SlotOrigin` ask it first, so every value that carries the helper borrow (SPEC 15.6: Copies, retaining call results such as
  `self.me()` and `Audit.same(self)`, aggregates, Closures, Pattern bindings of `match self`, Borrows through it such as
  `w.buffer@ref`) carries H, while the receiver's parts keep `Projection[drop#0]` (`self.buffer@ref` stays capturable). Display: H
  shows as `self`. No new `OriginKind`, code, requirement or `BindingFailure`. Rule: a Closure created in a drop body cannot capture
  a binding whose Type carries H, read on Origins without flow (`CarrierOf` in `Binding.LocalRegions`: an Origin the Type names, or a
  local region with H among its sources through `RegionContains(OriginAtUse(o, use), H)`). Judged where its facts are decided:
  (1) where the capture is made, over the facts recorded so far: an explicit entry fails its Closure at the entry, as `[self]` does
  (`FailHelperEntry`), and an omitted list's use fails at the use, one record per use, as contextual `self` does
  (`Binding.Captures.FailHelperUse`); (2) once the destructor's body is bound (`JudgeHelperCaptures`, from `BindFunction`), only a
  `var` region that gained an H-carrying source after an accepted capture: that source fails, the value whose bound enters H into the
  region graph that reaches the binding, relating each capture it reaches (`capture`) and the declaration; the Closure stays
  resolved. Both judge only where H is interned for that destructor (a lookup, never interning to check), but step (1)'s test
  itself runs at every explicit entry and implicit use of every Closure in the program (a walk to the enclosing destructor,
  allocating nothing; the unit's Bind A/B within noise). `LocalRegionSourceUse` became the evidence form of `LocalRegionSources` (each parent read through
  `OriginAtUse` at its bound, meet operands expanded), so a carrier `CarrierOf` finds always has a source use; step (2)'s
  fallback is `InternalInvariant_Kd` without recovery. Step (1) relates that use as `borrow` when it is not the binding's own
  initializer. The owner's readings: OQ1 a Borrow reached through the helper borrow cannot be captured; OQ2 judged on Origins,
  flow-insensitively, so a `var` whose region the helper borrow bounds, even by a later assignment or in a loop, cannot be
  captured; OQ3 a local aggregate (`arr.append(self@ref)`, `let t = (self@ref, 1)`) is not "stored", which means storage that
  outlives the body, already a Refuted Origin relation, so no store rule; N5 a Closure value a helper returns holding the helper
  borrow (`let g = mk(self@ref); g()`) is a local holder and valid, and `func [g] …` is rejected. A Closure argument rejected at its
  capture is not judged again at its erasure (one failure per node). R4's boundaries: no A5 part (A5 reads no atom slot; a rejected
  capture is the rejected node's Opaque, R4-M70 option P); a store of the helper borrow or a Field borrow into the receiver's own
  storage stays G161 (R4's A5 rule); 2a0-D's asks (PLAN G161: an atom-identity row for `self@ref` in a drop, and no A5-F4 rule
  reading a Projection's slot as a symbol slot); `let x = self` and `(self, 1)` are R4-M4's TransferRequired gap (H1). Rejected: a new
  `OriginKind` (a copy of Projection's meaning in every reader, the frozen old path's among them); per-Borrow finite Origins (changes
  the old path's identity, A5-F4 and every meet); an A5 Loan rule (capture legality is Binding's, SPEC 7.7.2); a syntactic
  initializer test (misses call results, meets, regions, aggregates); a store rule (OQ3); a capture-time judgment alone (misses a
  `var` assigned after the capture, OQ2); giving the receiver symbol its own slot (parts reach the same root; constructors collide
  with parameter 0, G178); failing the Closure at the body's end (round 1: other nodes had consumed the resolved Closure, so
  `Binding.Check`'s re-proof of a resolved call's Callable constraint through the Resolved-only `BoundClosure` added an
  `InvalidConstraint_Kd` at `Audit.see(f@ref)` and the MIR an `Unsupported_Kd` at `let h = f`); failing a capture-site node at the
  body's end (an explicit entry has no node: `CaptureKoto` is a struct, and an unused entry `func [w] () => ()` has no use either).
  Not temporary.
- **BU-18** (2026-10-21; the BU planning session; A5 step 2 plan unit 2b-6 (a) under R4-M89 PD12 (i) and R4-M90 PQ-1, PLAN G100; one
  design, one review (ACCEPT WITH NON-BLOCKING; its follow-up below)) A written Borrow that SPEC lends of a static Field reached
  through its qualified name, or of a Field or element below it (SPEC 15.6.2's static root, 15.6.4, 11.2), is Binding's located
  `Unsupported_Kd`, in the forms routed through `BorrowFailure`: `@ref`, `@uniq`, typed (`@uniq/i32`), `@raw`, `@objref`, `@objuniq`,
  an object upcast (`State.circle@objref/Shape`) and a case adaptation (`State.counter@s`), as an argument and inside the group
  (`State.counter@uniq`, `State.point.x@uniq`, `State.pair.0@uniq`, `State.values[0]@uniq`). Representation
  (`Binding.PlaceAuthority`): `LendsPlace`, the former `BorrowablePlace`, decides as SPEC lends, a static Field being a root Place
  lent as its accessors allow, and reports that root (`staticRoot`, a node because the member's one bool parameter is the mode);
  `BorrowablePlace`, which every other consumer reads, lends only without one, so nothing newly binds; `BorrowFailure` is the one
  diagnostic for a written Borrow it denies, read by those arms in place of their ternaries and literals: `Unsupported_Kd` for a lent
  static root, else `AccessFailure` (exclusive) or `InvalidAssignment_Kd` (shared). A case adaptation's shared case gets the lending
  check its exclusive case had, and `BorrowableTemporary` decides that a function reference or an anonymous function is a Temporary
  Value (formerly passed in by one caller), so `g@s` binds in every case as `g@uniq` does. A Borrow SPEC denies keeps its Language
  error (`State.limit@uniq`, a private or custom setter's `@uniq`, a Field of a `let` static). Changed codes: a typed exclusive
  borrow gets the shorthand's (`v@follow@uniq/i32` through `ref/i32` `SharedPathAccess_Kd`, a constructor's `self.total@uniq/i32`
  BU-15's `Unsupported_Kd`; both were `InvalidAssignment_Kd`), as does a typed object upcast (`r@follow@objuniq/B` through
  `ref/(obj/D)`); `g@s`, `s` admitting `uniq`, binds (was `InvalidAssignment_Kd`); a case adaptation reports `Unsupported_Kd` only
  when no case violates SPEC: a case returns `null` (`bool?`) when it holds but for a Borrow `BorrowFailure` names `Unsupported_Kd`,
  recording nothing, and the mode loop of `CheckAdaptationRows` stops at the first failed case and keeps a `null` in a local,
  reported once by its caller; an upcast case judges its Types (`ObjectUpcastFits`, split from `BindObjectUpcast` with
  `ObjectUpcastResult`), then its Borrow, then its result, so `State.limit@s` with `s is ref or uniq` keeps the uniq case's
  `InvalidAssignment_Kd` and `State.counter@s` is `Unsupported_Kd`. Exceptions: below a static object handle or a pair layer the
  walk asks `BorrowablePlace` and reports no root, and a Dictionary element's index borrows its static receiver implicitly (PLAN
  G100 (b)); a parenthesized operand and a shared element part, which bound as a Temporary Value (PLAN G189), are covered by BU-21.
  Rejected: an `AccessFailure` arm (it also names writes and Takes and has no scope for 11.2's accessor access, so a private
  setter's `@uniq` would become `Unsupported_Kd`); a clause per Borrow arm; a mode parameter on `BorrowablePlace`; for the case
  order, a scratch field naming the conversion (state for one feature, saved around nested adaptations; ba3dd966, replaced), a `ref`
  output (`CheckAdaptationRows` is over the parameter limit) and reordering the cases. Temporary: the `staticRoot` denial goes when
  P42 models static Loans; part (b) extends it to the unqualified name in the group after G:A5-F9.
- **BU-19** (2026-10-21; the BU planning session; PLAN G140 and G153 (2)'s Tuple chains; one design and one independent review,
  APPROVE WITH CHANGES; R5 consented to BU holding `Binding.ClosureInstances.cs`) An interned Type is a DAG (identity is equality),
  and a struct's or enum's parts reached through `AdtDef` add sharing; a walk that re-visits a shared part costs one visit per path,
  2^N on `L{i} { a: L{i-1}, b: L{i-1} }`, a Tuple instantiation chain (`f_i<T>` calling `f_{i+1}<(T, T)>`) or inferred shared
  Tuples (`let a{i} = (a{i-1}, a{i-1})`). Rule: every walk answers a function of the part and of its query's fixed inputs, so each
  distinct part is decided once per query: by an exact whole-subtree summary where one decides it (no visit), else by a per-query
  answer table. Component: `TypeMemo<T>` (`Binding.TypeMemo.cs`, its contract at the file's head), the answers of one walk by part
  within one query: `Begin` stamps a query (no other query of the table has its stamp, so an entry of another query, pass or edit is
  stale and never read; a nested entry call only evicts the outer query's entries), the table drops its Types when a new pass begins
  a query and keeps its capacity (a warm pass allocates nothing). Its users: `SubstituteType` (an outermost call is a query, its
  binder, arguments and lengths fixed; the FamilyCase arm continues it), `CloseClosureTypes` (a call is a query, its instance fixed;
  a closure signature's own instantiation begins its own query) and the existence walks of `Binding.ConstraintOperands` (the parts
  met): `OperandContexts`, one walk for both declaration contexts, replaces `InvalidConstraintType` and `UnresolvedConstraintType`
  (each run in full at every atom judgment and capability request), and `ContainsAssociatedProjection`, which `InternConstraint`
  passes to `BoundConstraint` for `HasAssociatedProjection`, and `Binding.LocalRegions`' `HasRegionBounds` over a Type, which the old
  analysis's loan flow asks for every Place (reached by Origin-carrying shared Tuples, letr). Exact gates: without length arguments `SubstituteType`'s image of a
  part with neither an abstract part nor an Origin is the part (every changing arm is an abstract slot, a length or an Origin);
  `ContractType`'s likewise (the environment substitutes slots and Origins, Self is a Parameter, normalization rewrites abstract
  kinds only); a projection is abstract. The existing per-query or per-pass Type sets keep their form (EffectBounds' `destroyed`,
  `capabilityTypeDepths`, BU-12's `storageMarks`, `MirTypes`' seen sets, Layout's memo): each already decides a part once.
  Open (PLAN G140, U3): walks with the same shape that no reproducer reaches with a shared part, measured flat on the eight shapes
  (Bind did not grow): in Binding `ContainsRawPointer`, `RetainsBorrow`, `CExchangeable`, `HasZeroStride`, `VisitInlineLayout`'s
  Tuples, `CheckTypeConstraints`, `AddTypeClauseObligations`, `TypeAccessCovers`, `ComparisonProof`, EffectBounds' `Reachable`,
  `Conflicts`, `CollectItemParts`, `MentionsItemPart` and `Comparison`, `RetentionWalk`, `Witnesses.Leaves`, the predicates
  `DependentType`, `ContainsUnspellableType`, `IndependentInputPattern`, `IsConservative`, `HasRuntimeTypeDependency`, `Closed`,
  `StoresTypeArgument`, `MentionsSlot`, `OwnFunctionBinder`, `CollectShapeDependencies`, `CollectRequirementEdges`,
  `ProveTypeLengths`, the ungated Origin walks (`StaticOnly`, `OverInputs`, `FixedInBody`, `HasUnsubstitutedInput`,
  `HasEnvironmentOrigin`, `ContainsOrigin`, `ReceiverDependentPart`, `CallLocalOrigin`, `MentionsOrigin`, `FirstUnownedOrigin`),
  the summary-gated ones over shared Origin-carrying or open parts (`SubstituteStoredOrigins`, `SubstituteIdentity` among them) and
  the pairwise walks; each adopts the table or an exact gate in the unit whose reproducer first reaches it. Rejected: new `BoundType`
  summaries as the fix (a held shared representation; they cannot decide the state-dependent declaration contexts or rewrite shared
  open parts, so the table is still needed); a memo across queries (one `Apply` still walks its DAG as a tree); a cap on a Type's
  tree size (narrows accepted programs); waiting for R6 and R7; a per-query `Clear` (very many small substitution queries would pay
  the table's capacity); one stamp shared by nested calls through a field (exact only if every nested call closes in the same
  instance); a table for `ContractType` (its `normalizingAssociated` guard is path-dependent; the gate suffices on every shape).
  U2, the old analysis, under the owner's D5 exception of 2026-10-21 (the owner's choice 「例外を認める (推奨)」: performance fixes of
  its walks W1 and W5 with identical verdicts, the Origin-carrying shared Tuples of letr included; nothing else of the frozen path;
  they go with the old analysis at R6-check): `CleanupObservesBorrows` keeps one query per Place, its source and context fixed; its
  answer at a depth is monotone in the depth (an observer is reachable, or a path from that depth reaches the bound of 64), so a part
  keeps, within the query, the greatest depth it answered false at and the least it answered true at, and a depth between them is
  computed: exact, at most 66 answers a part. `HasProjection` reads Origins only, so a part without `CarriesOrigin` has none, and
  visits a part once per query; `AggregateCanComplete` keeps the Tuples it found complete for the analysis; `VerifyBorrows`'
  `AddType` visits a (part, bound) once per Place, since a repeat records only dependencies `RecordDependency` already holds (it only
  strengthens) and so recurses nowhere. Rejected for U2: a per-analysis table for parts without an abstract part (instance storage
  keeps projections unnormalized, so such a part can still reach `CopyProof` in the Place's context; PLAN G187).
  Cost on ordinary programs (recorded under ARCH's performance rule; interleaved runs pinned to one core, the final review's and the
  implementer's): warm Bind stays 0 B and takes +0.5% (Milestone27) and +1.3–1.7% (Milestone32); cold allocation +33–66 KB in Bind
  and +107–134 KB after it; a checked compilation retains about 100 KB more (+1.2%). Skipping the table for parts whose components
  are all leaves recovered nothing (the review's variant), and no cheaper exact form is known, so the cost is accepted as recorded
  against exponential time on shared Types.
  Not temporary.
- **BU-20** (2026-10-21; the BU planning session; A5 step 2 plan 2x-R14 under R4-M89, package 2b-D §3.4 and §4) The
  intrinsic Contract table is a canonical table. `IntrinsicContract.Of` (`Binding/KimiLibraryKinds.cs`) has one row per
  `IntrinsicKind` and the columns Judged, Declaration, SelfRequirement and ConformanceJudged. `ProveConstraint`, a
  Contract's Self clause, `Register`, the conditional Self clause and `VerifyConformance` read it, replacing five `is`
  chains (review A-3's open family). A new intrinsic adds a row (2b-4: Transferable). Outside it: the capability engine's
  leaves, the `not ObjectPayload` opt-out and Copy's derivation. Open: the conditional Self clause keeps its own failure
  code (PLAN G190). Rejected: per-column pattern methods (a new intrinsic extends each) and a flags enum (more code).
- **BU-21** (2026-10-21; the BU planning session; PLAN G189, unit bu15; one design, one review, redone on its blocking finding B1) A
  Borrow's operand is a Temporary Value exactly when its root, read through parentheses and inline parts (stopping at a getter
  result), is no Place (SPEC 3.4, 3.6): `BorrowableTemporary` (`Binding.PlaceAuthority.cs`) refuses a root that `IsBarePlace` or
  `IsStaticField` names a Place and admits any other (a call result, a Case construction, a callable value, a getter result, a length
  parameter), lending a getter result only shared (SPEC 11.5); its refusal of an exclusive borrow through an inline part
  (`pair().0@uniq`) is an implementation limit reported as a Language error (PLAN G203), not part of the rule. `IsStaticField` (a
  group's Property read through its standard getter, qualified or in its group) is the one static-root predicate; `LendsPlace`'s
  static root (BU-18) reads it. `IsBarePlace` takes a user index as a Place whatever its key. Why: the clauses read the written node,
  so `take((State.limit)@uniq)` built, borrowing a copy, and a static's parts and a parenthesized computed getter were temporaries.
  Rejected: a static Field in `IsBarePlace` (its Subject consumer would borrow `for v in State.values` in place, an implicit static
  Borrow that fails with `PrerequisiteUnavailable_Kd` until 2b-6 (b)); a declaration-symbol test (an open family; it rejected
  `(Color.Red)@ref`, B1). Open: `IsStaticField` joins `IsBarePlace` after 2b-6 (b), with a Non-Copy static's bare acquisition, which
  binds as a transfer (PLAN G100).
- **BU-22** (2026-10-21; the BU planning session; A5 step 2 plan 2b-4a under R4-M89 (OQ-F5-2 (b), OQ-F5-3 (a)), the contract of
  `draft/Sketches/A5 Step 2/ComponentSummaryPrototype.md` revision 3; PLAN G174; DEC Q6 form 1, R4 reviewing) The per-declaration
  component summary is a canonical component (`Binding/Binding.ComponentSummaries.cs`, its contract at the file's head): for each
  struct or enum, what its storage adds, in its own formals, to an instance's Owned beyond the instance's Type and Origin arguments
  (SPEC 15.2.3, 15.3.5): ExcludesOwned (`TryLeafCapability`'s Owned leaves), Error (an Invalid stored Type, 8.7.3's ill-formed object
  forms among them; F6: it propagates through components), Unresolved (Unknown, as before), the projections of its formals, and the
  sites of a located `Unsupported_Kd`; the least fixed point over every declaration one solve reaches, never instantiating storage,
  for one capability epoch (`ClearCapabilityResults` ends it), a warm rebind allocating nothing. Owned of D⟨A, o⟩ reads it: Error;
  else ExcludesOwned, Unresolved, every Type argument, every Origin argument `static`, every projection substituted; Owned's stored
  walk is gone (`StoredCapability` keeps Copy's derivation). Rules: the owner's choice for 2x-P12 (2026-10-21, verbatim
  「案2: その場で U (推奨)」): a component's obligation that would name outer(X) or target(X) of D's plain formal, projection or pair
  target passed whole is not recorded and D's stored Type is a site (for Owned unreachable while Binding rejects such a Field, PLAN
  G201); the planning session's rulings: P < U < L < R < E (N3-1), L an Unknown carrying its site; F5 (a): a projection link on a cycle
  of the projection-link graph stops its target's projections being mapped and makes the stored Type naming it a site; F3, F4, the
  core reduction (resting on SPEC 8.7.3's Object Target well-formedness: Binding rejects ill-formed object forms) and R2-1's
  owner-known split are Transferable's and move to 2b-4 with ExcludesTransferable and the PairU site (R4's Q1, 2026-10-21: their first
  reader lands there); the rest of the prototype's "What 2b-4a implements" lands here. Ruling (the planning session and R4, round 2,
  2026-10-21; it withdraws round 1's "the outermost capability request answers L as Error" and "a summary judgment marks its sites
  needed whatever reads it", which broke N3-1) on the limit's answer: L is a value of every proof that combines it. A proof scope
  (`BeginProof`/`EndProof`: `ProveConstraint` and so each connective's operand, a clause list (`CheckConstraints`,
  `CheckTypeConstraints`, `CheckClosedDeclarationConstraints`, `ProveConditionalPremises`), a candidate's applicability, the
  disjunction of conformance paths (`ResolveConformance`), a capability request, inside which `CapabilityWork.Cause` carries it) keeps
  the limit its own answer rests on; only where the answer is no longer combined (the outermost scope) and is L does it answer Error,
  the site in the rested-on-cause channel (`ICheckCause`, generalized from OCC-X's conformance path in a behavior-preserving commit,
  the planning session's Q2), which about 20 consumers already derive from; a check that fails on it takes it (`TakeFailedCause`),
  which publishes the site once, after `Binding.Check`'s loop (`PublishSites`, the one publication path); an exact refutation or proof
  elsewhere decides. A summary's Error rests on the stored Type that decided it (the summary is its `ICheckCause`). Readers that record
  their own failure on Owned rest on the channel too: a common Function conversion (`FailOwnedConversion`) and a result Origin's
  Owned inputs (`MissingOrigin`); `AssociatedFormation` reads only abstract Types, which no summary decides. A call selection whose
  candidate reads L rests on the site and selects no other candidate. Bound (R4's Q4 within R4-M89's F5 (a), round 2: length, so a wide
  declaration stays exact): a projection of more than 64 steps (a conformance binding can lengthen a projection with no projection
  link) makes its summary a site; it only yields the located `Unsupported_Kd`, never a verdict; the census of the default suite
  (22,164 tests, 186,760 solved summaries) found no summary holding a projection. Growth (R4's Q3, round 2): a proof can re-enter a
  declaration with ever larger arguments through Copy's conditional premise over a projection and through Owned's projection
  normalized at the use by a conformance binding that uses itself (review N1; round 1's census probe had no Owned requirement on the
  instance), so the engine's growth guard covers every intrinsic at the use, as before, its second growth step Unknown; removal
  condition: SPEC 8.3.3's proof-depth limit is a resource failure (PLAN G200). The walk resolving a concrete projection through a
  conformance binding it is already resolving through, at a deeper instance, is that conformance using itself (SPEC 8.3.3, review
  N2), which has its own record: the summary is Error resting on the stored Type; a deeper instance of a declaration reached through
  another conformance is no such growth and stays exact. Acceptance (OQ-F5-3 (a)), both pipelines: Owned of a non-regular recursive
  Type is exact (Unknown → Proven for `Chain<i32>`, `Nest`, `Nest15`, `Ping`/`Pong`, `Grow`, `Fold`); a requirement resting on a
  projection family gets one located `Unsupported_Kd` at the family's stored Type, its use derived, in place of
  `UnprovenConstraint_Kd`; no old-path guard (R4's Q6): such a value meets the old Emission's existing generation depth limit.
  X-2 register (PD28): the family site, the bound and the option-2 site. Open (PLAN G207): a reader that combines two scoped proofs
  itself settles each. Rejected: L as an Unknown that `RequireConstraint` takes (the other consumers), a fifth `ConstraintProof`, a
  second channel, publishing a site where its proof settles whatever the reader does (an explanation such as
  `CandidateConstraintFailure` reads a clause whose L decides nothing), a solve per strongly connected set (a conformance binding can
  name a declaration no written storage of the set names), option 1 (the owner's X-2 candidate). Not temporary; the guard and the
  bound keep their conditions above.

- **BU-23** (2026-10-22; the BU planning session's ruling on bu20's verify-first S1, R4 agreeing; PLAN G178; unit bu20 C0c) A
  constructor's or destructor's receiver `self` is a Place of its own: its symbol takes the reserved slot `ReceiverSlot`
  (`int.MinValue + 2`, beside `CallResultSlot`, `ClosureValueSlot` and `HelperSlot`), so its Place's Projection is
  `Projection[init#ReceiverSlot]` or `Projection[drop#ReceiverSlot]`, never a parameter's (it was slot 0: the first parameter's in a
  constructor, `Projection[drop#0]` in `drop`; 2a0-D §3 row 3, Finite, renamed). No reader takes the slot as a parameter index:
  `ParameterVisible` and `DefaultParameters.InLaterDefault` compare it unsigned, so no default sees the receiver (SPEC 7.2.3); every
  other reader of `BindingSymbol.Slot` compares unsigned or never meets a receiver (audit in the commit). Diagnostics show the atom
  as `self`, as the helper borrow's. G160-1's `during self` in a special receiver's body names this atom (design §13 A3). Reason:
  two holders shared one Origin (F4's lesson), so the old analysis gave a receiver Field borrow the first parameter's Loan (G178).
  Rejected: numbering parameters from 1 (moves every parameter atom and Input index); a receiver `OriginKind` (BU-17's reason: a copy
  of Projection's meaning in every reader, the frozen old path's among them). BU-17's rejected "own slot for the receiver symbol" was a
  way to separate the helper borrow, which keeps `HelperSlot`; it stands.

- **BU-24** (2026-10-22; the BU planning session after the full process: designs A′ and B′, design review 2, design-final §14 "B′+";
  unit bu20 C1a; CR-R2) A raw layer's Origin (SPEC 5.7) is a role of its own: `BoundType.RawOrigin` beside `Origin`, one slot whose
  role the Semantics decides (0 B; a pinned interleaved A/B of the getters against C0d's build gave warm Bind minima 35.70 against
  35.41 ms over three milestones, inside the baseline's own 3% spread, and equal cold bytes, so B′'s +8 B field is not taken). `Origin`
  never reads a raw layer's; `RawOrigin` only a raw layer's. `TyCtxt.Intern`'s one `origin` parameter writes the slot (ruling (a):
  `Intern` and the constructor were at 10 parameters; rejected (b) an owner exception and (c) splitting `Intern` as its own unit;
  CR-R2 since exempts constructors, `Intern` stays limited); `Pointer(referent, rawOrigin)` and `Rebuild`, the one keep-rebuild,
  write the raw role. The constructor throws on an Origin given to a raw layer until G160-1's formation, then checks kinds per role.
  Summaries: `CarriesRawOrigin`, included in `CarriesOriginOrSlot`; `CarriesAnyOrigin` at every gate that asks whether a use must
  instantiate, wait for or compare written Origins at all (`CallResultEvidence` and the survey's expected Type, the comparison's
  right expectation, `PerCallSignature`/`PerCallInputs`, `ClosureExpectation`, `HasUnsubstitutedOrigin`, the setter-input reuse, an
  accessor witness's input, `FitsVerifiedTypeAt`, `MirTypes.Agree` with R4's agreement). The gates of `HasLocalRegion`,
  `RewriteOrigins`, `SubstituteStoredOrigins` and `SubstitutePart`/`SubstituteTypeCore` take the raw bit with G160-1's raw arms; the
  other 41 summary reads in Binding are the borrow role (inference and matching, open Origins, premises, regions, effects,
  construction premises), and the old analysis never sees a raw Origin (Q-O3 is moot). Identity: `BoundType.SameExceptOrigins`
  (every part of the layer but `Origin` and Origin arguments, `RawOrigin` compared exactly: no identity treats it as open, omitted or
  inferred) is shared by `SameCompleteType`, `SameModuloOpenOrigins` and `SameExceptOmittedOrigins`. `ArchitectureRulesTest` pins
  the self-rebuilds and the raw role's reads and writes by site. Rejected: A′ (the same field and a borrow view): a gate and the
  rewriter it guards read different meanings (a body-local region escapes through `ResolveLocalResultOrigins`), `JudgeCallFit` skips
  an unstated argument, its reader list misses `StoredValues`, and the frozen old analysis still sees raw Origins. G160-1 (C1b)
  appends §0's vocabulary (`Holder`, `Shared`, `Unstated`) and the owner's decisions.

## R5 and R6 (adopted 2026-10-18)

The owner's answers to the plan's questions OQ1–OQ17, given on 2026-10-18 and relayed by the R4 planning session. "Plan"
is the [R5 and R6 Implementation Plan](../../draft/Proposals/2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md); its
unit and decision names (S1, E2, PD12) are used as there. Each planning-session decision PD1–PD32, component and temporary
structure is recorded here by the unit that first implements it.

- **R5-M1 Adoption** (owner, 2026-10-18; OQ1, OQ4, OQ10, OQ11 and the start) The plan is the formal plan for R5 (MIR code
  generation, shadow), R6-gen and R6-check: its units (§4), order (§3), metrics and stop rules (§5.1–§5.2, R5-M8) and three exit
  gates (§5.3–§5.5), which replace PLAN10 §12.2's R5 exit and give R6-gen and R6-check the gates R4-M35 left out. R5 runs now, in
  parallel with R4 (OD4 stands; suspended by R4-M77 and R4-M79, restored by R4-M78, all 2026-10-19, and restored by R4-M86,
  2026-10-20), in its own planning session. The design packages S2–S4 (OQ4), Q8 (OQ10) and H0 (OQ11) each go to the owner once,
  after their design review, and are approved then, not in advance; each approval makes the package the owner-approved design of
  DEC Q6 forms 1 and 2 for the units and groups it names. The S2–S4 summary highlights PD3 (no `MirBodyKind.Shim`; shims are
  Backend mono items) and PD11 (temporary codegen coverage rows reporting `Unsupported_Kd` under sel, outside the three reporters
  ARCH "Closed vocabularies" names, until X2), which depart from owner-approved texts. Rejected: approving the packages before
  their review.
- **R5-M2 R5 start conditions** (owner, 2026-10-18; OQ2, as recommended; supersedes the owner gate of
  [Pre-R5 Work Items](../../draft/Proposals/2026-10-12%20Pre-R5%20Work%20Items.md) §2, which assumed that R5 starts at R4's
  exit) The A5 plan §9.1 timing baselines (ARCH R0 row: the O2 corpus and the compiler pipeline series at `49cb2b5d`) with
  E6 and E7 replace B1 and B3; B4 is met once the native audits fail closed (E2), their test-coded counts becoming the
  PLAN10-P2 baseline; B5's command rows are added to VERIFICATION; RawPointers is not restored. The A=B variance is retaken
  on a fresh `49cb2b5d` checkout kept at a named location with a backup (E6): the evidence directory PLAN §3 cited
  (`artifacts/benchmarks/o2-corpus/20261017-111950-pre-r5-baseline`) exists in no checkout (checked 2026-10-18). R5 code
  units may start.
- **R5-M3 One measured codegen design** (owner, 2026-10-18; OQ3; supersedes A5 plan §9.1's "both measured") The measured
  design-A spike is waived: design B (PD1, a direct `FunctionCodegen` from validated MIR) and an independent review of
  design A on paper are the two designs, and B's first slice is measured against the legacy generator instead (E13's
  per-function O2 metrics and E6's baseline at C1a's group review). S6 does not run. Reason: the writer stage is
  0.016–0.066 ms of a 21–43 ms compilation, so a spike cannot discriminate (PD1).
- **R5-M4 Publication from G2** (owner, 2026-10-18; OQ5, every sub-item as recommended) From G2, `check` on the default
  pipeline publishes the MIR construction records, validator findings and collector records (G2 writes the entries
  superseding R3-1, R4-M1 and OD10's split for them) and reports instance-stage `Unsupported_Kd` and Resource records for
  the checked set (SPEC 23.2.2 leaves it implementation-defined; IMPL 21.3.3 requires a report for every instantiation of
  the checked set), so a Library with an unbuildable generic instance fails `check`. Between G2 and H5 a PD12 refusal of a
  program the old analysis accepted (an A1 or A2 problem, or A1's arena record) is reported at generation as a typed
  Emission-partition record: no duplicate of the old path's own `OwnershipStorageLimit_Kd`, so no suppression rule. Every
  default Analyze runs the shadow MIR driver (A3, A1, A2, A4 and A5 once landed, unpublished) from G2 until R6-check, its
  time, allocation and retained-memory cost measured in G2. Collector records go in the ControlFlow partition with R3-1's
  MIR records (PD7), so PLAN10 §7's "P11–P12 → Emission" yields for them (K4's OD4 entry). Under sel, collector records
  publish from K4 (MIR construction and validator records already do under `mir`, R3-1). Rejected: publishing PD12 refusals
  at `check` before H5.
- **R5-M5 `AnalyzeInstance`** (owner, 2026-10-18; OQ6, option (e)) The two definition-level holes `AnalyzeInstance` covers,
  PLAN10-X1 (abstract destruction) and abstract temporaries, are reproduced (Q1) and fixed in the old definition run as D5
  miscompile fixes (Q2, Q3; DEC O3 applies if a library or milestone program is rejected); then Q4 writes the entry
  superseding OD10's "check-only step" clause (R4-M35) with the ARCH R6 row, and G7 deletes `AnalyzeInstance` with its
  callers. R6-gen therefore also waits for A5 step 1 through Q2. Since R5-M18 (ask A-2), C4a and with it the R5 exit also wait
  for A5 step 1, so the fallback (d) below loses its meaning. Reason: `check` never runs it (`Project.cs:502-515`), and its
  only callers are in the Emission R6-gen deletes. Rejected: (b) a check-only Subst→CallPlan bridge (a transitional structure
  of difficulty 4, +150–200 lines); (d) R6-gen waiting for R4's exit, which stays the fallback if A5 step 1 has not merged by X3.
- **R5-M6 Native deviations at R5's exit** (owner, 2026-10-18; OQ7; extends PLAN10 §12.2's "match or (e)") A native outcome
  of MIR code generation may differ from the legacy run's only as (e) a form not ported under D2, (a) a legacy-wrong outcome
  or (d) an intended change, each with its item, reason and SPEC clause (the plan's examples: the static cycle code at the
  accessing Place, SPEC 22.4.3 and 22.5.2; inline closure environments, IMPL 21.1.3; Abort caller locations, SPEC 22.5).
  Rejected: reproducing wrong legacy output on MIR, or D5 fixes of the frozen legacy path to make the two agree.
- **R5-M7 Hold lines beyond Mir** (owner, 2026-10-18; OQ8; PD20) ARCH's hold protocol for files both streams change also
  covers (paths under `src/Kimi/Compiler` unless given in full) `Core/Compilation.Pipeline.cs`, `Binding/TyCtxt.cs`,
  `TyCtxt.Effects.cs`, `TyCtxt.Retention.cs`, `BindingModel.cs`, `Binding.ClosureInstances.cs`, `Binding.RequirementInstances.cs`,
  `KimiLibraryCatalog.cs`, `src/Kimi/Checking/Hover/HoverBuilder.cs`, `src/Kimi/Diagnostics/DiagnosticRequirement.cs` and
  `tests/xUnitTest/MirDifferenceLedger.cs`.
  R5's new Binding views go into new partial files, and its shared MIR edits are batched into windows W1 (the builder's call
  and control files, the `$abort` site, the Q16 sites and the goldens) and W2 (`MirProgram.cs`), each recorded in PLAN §3 as
  a hold. Reason: both streams edit these files, and the protocol covered only Mir. Amended by R5-M18: W1 also covers
  `Mir/MirValidator*.cs` (F11), and C0b1 waits for W1.
- **R5-M8 R5 metric, stop rules and verification** (owner, 2026-10-18; OQ9, as recommended) Adopted: the plan's metrics
  (§5.1), M1, the sel pending ledger, being R5's progress measure, and its stop rules (§5.2): the owner is consulted after two
  consecutive R5 working sessions that land no unit (a planning- or record-only session lands none); a unit not landed within
  two working sessions is re-split or becomes an owner question; from C0b2, if M1 does not fall across three consecutive C
  units, the session states why, and a missing input becomes a fact or builder unit; an O2 kernel regression beyond the A=B
  variance blocks its family's merge, fixed in codegen, never by a tolerance. VERIFICATION gains R5's rows, a sel Session
  beside the default one at the end of every R5 working session and the pending-ledger gate, both removed at G4. Ask A-10
  goes to R4: from C0b2, R4's MIR-builder merges run the sel pending check and record any rise; from G3, they select the
  affected native families and O2 kernels. Rejected: a CI sel lane (full sweeps run at family group reviews, X1 and G3).
- **R5-M9 Binding syntax reads and synthetic Koto at R7** (owner, 2026-10-18; OQ12, OQ13; a later removal stage) Binding's
  reads of the 36 Koto forwarding properties, the properties themselves and ARCH's extension of Syntax's restriction to the
  whole Binding layer move from R6 to R7 (superseding CR-A7's R6 marking of the properties, whose attributes change at
  H14, and A5 plan §9.1's R6-check clause, R4-M35); Parsing and root-helper reads go at R6-check (H14). The synthetic Kotos
  (`EvaluatedKoto`, `SyntheticKoto`, `FormattingKoto`, `RequirementCalleeKoto`, `TryKoto` arms) go at R7, after the CallPlan
  pipeline gives syntax-free selection. Reason: no speed gain, since the properties already forward to `HirTables` (so PLAN
  §3's expected recovery of the R2a Bind cost is not expected from them, H), and R7's inline writes and CallPlan pipeline
  redo these sites. Rejected: R6-check (for the synthetic Kotos, pulling that CallPlan entry in, about +400/−300 at
  difficulty 4).
- **R5-M10 Syntax data in the MIR builder** (owner, 2026-10-18; OQ14, as recommended) ARCH's two rules on syntax-class
  decisions in Mir/Build ("reach 0 by the end of R6"; "no Koto type test or KotoKind dispatch in Mir/Build after R6") are
  amended: an operator token or form read under its published `HirOp`, and a cast to read a child, are syntax data; after R6
  Mir/Build has no Koto type test or `KotoKind` dispatch that chooses an operation no published `HirOp` decided. The 17 sites
  that do are fixed with existing facts (Q16, in W1); this amendment covers the 63 others (34 child casts, 29 operator or
  form identities), which H13 only confirms. Rejected: keeping the rule and replacing every site with facts or a HIR primitive-operation
  vocabulary (a full-process design and a vocabulary request for sites that choose nothing).
- **R5-M11 SETTLED "Instance-evaluated generic defaults" at H15** (owner, 2026-10-18; OQ15) The entry's mechanism text is
  rewritten at H15 as an explicit, formal revision. The R4 planning session's note, relayed with the decision: write it
  against the mechanism H15 has (the inline fold, `MirBuilder.Defaults`), not PLAN10 §11's 2026-10-10 wording; it gets its
  own independent review (AGENTS requires one for SETTLED). This resolves PLAN10 §11 (a rewrite at R6) against R4-M35's
  non-adoption of OD9 (the fold and the entry stay): the fold and the entry stay, and the entry's mechanism text is
  rewritten at H15.
- **R5-M12 IR-assertion classes** (owner, 2026-10-18; OQ16; PD25) An IR-text assertion in a test is R (required by SPEC or
  IMPL), P (a behavior proxy), I (incidental) or S (old-Emission structure). The rules are approved once: each codegen family
  unit deletes the I assertions and the P assertions its native checks cover in the tests it makes pass, and lists them in
  its commit; E1 classifies up front only what can still pass vacuously after E2. Rejected: an upfront table of about 450
  rows on the critical path.
- **R5-M13 No-LINQ in tests, static shutdown, NativeAOT** (owner, 2026-10-18; OQ17, (b) and (c) changed from the plan's
  recommendation)
  - (a) ARCH's no-LINQ rule for Mir analyses and the Backend does not extend to tests (the owner first agreed, then withdrew).
  - (b) Static shutdown is not pulled into R5. P42's remainder (static `var`, aggregate and drop-needing statics, generic
    storage keys, first-write initialization, replacement destruction, shutdown and its order, `KIMI_E_STATIC_SHUTDOWN`) is
    the first feature work right after R6-check's acceptance flip (H5), with Milestone 42 as its completion condition. PD19
    stays; C7's slot state follows SPEC 22.4.3's four states (Not started, Initializing, Initialized, Finished), so P42
    needs no representation change. Reason: the deciding old analysis accepts only immutable nongeneric static scalars
    (`StaticScalar`), so no accepted program can exercise shutdown before R6-check (`check` probes, 2026-10-18). Corrected by
    R5-M18: it also accepts immutable static scalars in generic containers' groups (deferred under PD27, X33 (a)); "no
    representation change" holds for the slot state only (P42's generic keys need the container's Type arguments on each access's
    `staticEnsure` operand, and P42 narrows the static constant fold or restores its state for shutdown); the decision stands.
  - (c) No NativeAOT publish at R6-gen's exit (plan §5.4 item 7 and G11 drop it); instead the NativeAOT build is published and
    run once before the next distribution, for example VS Code extension packaging.
- **R5-M14 `LlvmModuleText` and `LlvmConstantPool`; promotion (PD2)** (2026-10-18, planning session; plan PD2 and P0, refined
  by the chapter C package §3.1 and §3.4) Backend is `src/Kimi/Compiler/Backend/`, namespace `Kimi.Compiler.Backend` (as Mir,
  R4-M2); the old Emission imports it until R6-gen deletes it. PD2: the old Emission's syntax-free module-level writers and
  records are promoted, never copied, into Backend components that the old writer and MIR code generation both call during
  the shadow. A promoted member takes generator-neutral inputs only (LLVM type text, `TypeLayout`/`ValueLowering`, SSA names,
  labels, a writer, records such as `FunctionAbi`), has no per-generator branch and ends within ARCH's structure limits, split
  by responsibility while moved; each move is behavior-preserving (VERIFICATION: fixtures byte-identical, diagnostic snapshot
  unchanged). P0, the first, creates `LlvmModuleText`, the module frame: header with the pooled constants, the runtime template
  with its ABI splice and test variant, the external declarations and their record `ExternalFunction`, the overflow and memory
  declarations, the footer, `ExternalName`/`SymbolFromName` and number formatting; `LlvmConstantPool` moves beside it. The
  formatting runtime splice and `Reason` stay with their helpers (P1d, P1a). ARCH's no-LINQ rule holds in Backend from P0.
  Both components are permanent (package §3.1). Until P0b (R5-M15) the Backend read the old Emission's `WindowsLowering` and
  `FunctionAbi`; it still reads Emission's `WindowsProfile` (target, data layout, CPU, features, entry symbol, `_fltused`),
  which no unit of the plan moves; its owner is open.
  Rejected: copying the frame into code generation (a second copy of each rule); the namespace `Kimi.Compiler` (no `using`
  in the old consumers, which R6-gen deletes, but no phase namespace as Mir has); forwarding members in the old writer
  (wrappers without semantics; its partials import `LlvmModuleText` statically).
- **R5-M15 `RuntimeAbi`, `AbortReasons` and the ABI records** (2026-10-18, planning session; plan P0b, package §3.1 and §3.4,
  promoted under R5-M14) Three permanent Backend components, separate from `LlvmModuleText` because each owns a different
  decision: `AbiRecords.cs`, the physical representation records `TypeLayout`, `ValueLowering`, `AbiParameter` with its kind,
  and `FunctionAbi`, whose `NoReturn`, `ResultSlot` and `CallerLocation` became init-only properties so that its constructor
  is within ARCH's parameter limits; `RuntimeAbi`, the windows-x64-v1 runtime ABI: the Unit, string and string-handle
  representations, the static release kind, the zero-sized buffer address, one `FunctionAbi` per runtime routine and
  `RuntimeDefinitions`, the routines `LlvmModuleText`'s splice defines from them; `AbortReasons`, the closed SPEC 22.5.2 table
  (`All`, `Expand` without LINQ, constants named without the `Reason` suffix). As promoted members they take no
  `BoundType`, layout pool, `EmissionModule` or syntax type, so the Type-keyed decisions stay in the old Emission until their
  successors land: `WindowsLowering.GetValue` and its scalar table (P7a, `Layout.Of`); `WindowsLowering.GetCompilerFunction`,
  which falls through to the formatting table (P1d); `FunctionAbi`'s `Supports`, `GetValue`, `HasResultSlot` and
  `ResultType`, now `FunctionAbiPool`'s (its Type dispatch goes to `Layout.Of`, P7a/P7b, and its signature part to `Abi.Of`, P8;
  the `Supports` gate stays in the old pool until R6-gen). Rejected: one
  component with `LlvmModuleText` (a text writer, not records); the Type-keyed table in Backend now (`Layout.Of`, a new Backend
  component, owns scalar representation from P7a, so moving the table first would make a second Backend rule or a second move); a runtime-only
  `GetCompilerFunction` in Backend with an Emission fallback (a try-then-fallback chain); keeping the Type-keyed statics callable
  as `FunctionAbi` members through C# extension members (a Backend name defined in Emission).
- **R5-M16 `LegacyRuntimeHelpers`, temporary until R8** (2026-10-18, planning session; plan PD2 and P1a, package §3.1 and §3.4,
  S2-S4 X4; promoted under R5-M14) A Backend component that owns the Array, Dictionary, formatting and string-comparison runtime
  text until R8 replaces it with Kimigayo library code (PLAN10 §5.8), so its declarations carry `[Transitional("R8")]`. It holds
  a module's helper requests, which leave `EmissionModule`: the Array and Dictionary helper records, the `Needs*` flags, the
  seven `DictionaryStorage` ABIs and the `FunctionReferences` (every source call the compiler requested, destructors included,
  which the Dictionary helpers call through), the formatting wrapper lists. Each module holds one instance (the old one as
  `RuntimeHelpers`, checked before writing). It also holds the element descriptor `StoredValue` (`ValueLowering`, drop routine
  name, text flag), which replaces the helpers' `AggregateLayout`; the Option payload offset and the remainder Field offsets
  travel in the helper record, the entry offsets in `DictionaryEntryLayout`. The stored-value kernels and the string-comparison
  routines live here too (`Reason`'s template number text joined `LlvmModuleText.Number` at P1b). A value travels as a scalar
  exactly when its argument form is its computation form (SPEC 21.3.1), so the descriptor has no scalar flag;
  `StoredValue.TravelsAsScalar` decides it once, for the helpers and the old lowering's requests (P1b). The old Array,
  Dictionary and formatting writers join at P1b-P1d, the storage kernels at P2a/P2b; each Body-disposition kind then becomes one
  row that declares its library callees and type-driven needs, which the collector reads (X4, package K §4.5). The Array rows
  (P1b) are `ArrayHelperRow`, one static row per `ArrayHelperKind` holding the name prefix, the ABI shape, the prologue, the
  writer and the `[Flags]` `HelperNeeds` (`ArrayRuntime` for every kind but BorrowStorage, `ElementGlue` for Clear and Drop; no
  Array row declares a library callee, since none calls one), read by kind through `LegacyRuntimeHelpers.ArrayRow(kind)`, which
  fails closed; `ArrayHelper.Create` names and shapes a helper from its row. Rejected: requests left in `EmissionModule` with
  the Backend reading it (a non-neutral input); `AggregateLayout` in the records (the old pool as a Backend input); a scalar
  flag beside `ValueLowering` (a second statement of SPEC 21.3.1's argument form); a needs flag on the built helper (the
  collector reads rows by kind before any `ValueLowering` exists); two bool needs (ARCH's one-bool parameter limit). The
  Dictionary rows (P1c) and the count of the `DictionaryStorage` functions are refined by R5-M27, the formatting rows (P1d) by
  R5-M30, the inline storage kernel rows (P2a) by R5-M45 and the rows' Array runtime needs by R5-M46.
- **R5-M17 `LlvmScalarText`** (2026-10-18, planning session; plan P4a, P4b, package §3.4, S2-S4 X36, promoted under R5-M14) A
  permanent Backend component owning the LLVM text of scalar arithmetic, numeric conversions and their checks: the instruction
  (`WriteOperation`), the overflow intrinsic (`WriteOverflowChecked`), the signed quotient with its zero and minimum / −1 checks
  (`WriteSignedQuotient`), the zero-checked unsigned quotient or remainder (`WriteZeroChecked`), the wrapping quotient
  (`WriteWrappingQuotient`), the count-checked shift (`WriteShift`), IMPL 21.3.3's literal −1 rules and SPEC 13.3.1's divisor
  substitution, the conversions (P4b): the cast alone (`WriteConversion`), the range-checked integer or floating-to-integer
  conversion (`WriteCheckedConversion`, KIMI_E_INT_CONVERSION), the floating narrowing that keeps infinities and NaN
  (`WriteFloatingNarrowing`, KIMI_E_FLOAT_CONVERSION) and SPEC 4.7.1's position conversion, −1 without a branch
  (`WritePositionConversion`), and each check's own failure block (`WriteFailure`: the branch, `abort<Id>:`, the Abort call
  with the check's reason and location operands, `unreachable`, the success label). One method per check: the checks are
  SPEC's closed set, and the old `ArithmeticCheckKind` stays the old IR's decoding. Inputs are generator-neutral:
  `ScalarOperation` (operator and Type text, operand spellings, result name), `ScalarConversion` (cast or null, source and
  target Type text, input spelling, result name, `ScalarBounds`: per side the outside predicate or null and the bound's
  spelling) and `ScalarCheck` (the number naming the check's temporaries and failure label, the success label's local name,
  the Abort's location operands: address and byte-length spellings of a pooled constant or of the forwarded caller location,
  so no parameter name is fixed), stack-only `ref struct`s, so writing stays allocation-free. Stated preconditions (X36): an
  operand is its LLVM spelling, a local name (`%…`) or a constant; an integer constant is the decimal of its signed value as
  `WriteNumber` writes it (LLVM's unsigned `i8 255` would skip the literal −1 rules); a floating constant is LLVM's
  hexadecimal double; a checked conversion has a bound, a position conversion a cast and a bound, a floating narrowing both
  bounds over a double source. Widths come from the `iN` type text; the signed minimum is computed at that width in `Int128`,
  so no width precondition remains (128-bit division stays `Unsupported_Kd` upstream). The old `WriteScalar` and
  `WriteConversion` spell their operands through `Spell`, which `WriteOperand` writes through (one spelling), and select the
  method by their check kind; bounds and argument checks reach `WriteFailure` through the old `WriteArithmeticFailure`, which
  maps their check kind to the reason. Rejected: a Backend check-kind enum (a second copy of the old vocabulary);
  `EmissionOperand` as input (old IR) or a Backend operand record (a copy of its kinds); operands as strings (warm writing
  would allocate); `ValueLowering` inputs for widths (the type text carries them); a result name and success label fixed by
  the component as `%v<Id>` and `b<n>` (codegen names values and blocks by MIR location, package §2.10); typed bound values
  (a second operand form beside spellings, and the hexadecimal float spelling copied into the Backend); a ≤ 64-bit width
  precondition or failure (the `Int128` minimum is correct at every width without a branch).
- **R5-M18 The R5 design package S2–S4** (owner, 2026-10-18; OQ4, R5-M1;
  「S2〜S4パッケージを承認します。X33は(a)、GenerationFailed_Kdの後継はArtifactPublicationFailed_Kd、F11はX30′のとおりで進めてください。」)
  The step-0 package in `draft/Sketches/R5 Step 0/`
  — `K-package.md` (S2), `F-package.md` (S3) and `C-package.md` (S4), with `S2-S4-decisions.md` governing where they differ (X1–X37)
  — went through three independent design-review rounds (lenses K, F, C and cross-cutting; X33 and F11's rule, each after two
  rounds on one family, went to the owner as choices) and is the owner-approved design under which
  R5's units take one design and one review (DEC Q6 form 1) and the groups it names share one review (form 2): G:K-core (K1, K3, K4a,
  K4b), G:K-edges (K5, K7, K10a, K13b), G:K-capability (K9a, K9b, K13a), G:compare (F6, F7), G:call (F5b, K6, C3a, C3b), G:C-frame
  (E10, E15, C0a, C0b1, C0b2, K11), G:C-scalar, G:C-aggregate, G:C-drop (C4s, C4a–C4c, C3c), G:closure, G:object (F4, K8, K13c,
  C6a–C6c), G:library-1, G:library-2, G:format, G:test, G:R6-gen-switch (G2, G2b, G3) and G:R6-gen-del (G4–G10). A unit that departs
  from the package, including a new representation, component or vocabulary entry it does not name, takes the full process.
  The package's unit and PD changes override the plan's §2.1 and §4 rows where they differ (among them F11, C3c, C7a/C7b, G2b,
  K4a/K4b, G9a/G9b, PD15 and PD23). It includes OQ4's two ARCH dependency notes (the Backend reads Core's runtime-location text
  and the Testing catalog's symbol column) and K §6's R6-gen switch contract, which G0 records.
  - Owner choices: X33 (a) — generic static storage keys stay P42's (R5-M13(b)); a static in a generic container's group is
    deferred under PD27 as a D2 (e) row at G2 if no library, milestone or corpus program uses it (K7 confirms by search), recorded
    in STATUS and PLAN and approved in the PD27 list at R5's exit. The successor of `GenerationFailed_Kd` for output and publication-time native-configuration
    failures is a new Input code `ArtifactPublicationFailed_Kd` (net 0 with `GenerationFailed_Kd` deleted at G9b; contract
    failures become `InternalInvariant_Kd`); `DiagnosticPartition.Emission` and the `Emission` requirement name stay through R6.
    F11's validator rule is X30′: a Temp whose only write is a diverging Call's destination is read only in check-only blocks.
  - Departures from owner-approved texts, approved with the package: PD3 (no `MirBodyKind.Shim`; shims are Backend mono items);
    PD11 (temporary codegen coverage rows reporting `Unsupported_Kd` under sel until X2, beside the three reporters ARCH "Closed
    vocabularies" names); A3b in the Backend collector's body scan before acceptance (D-A); instance-stage `Unsupported_Kd` causes
    beyond operators as capability rows (D-C); KD1 (the instance key's Subst is nullable, null exactly when the binder does not
    instantiate, against PD5's `Instantiation(MirDefKey, TyCtxt.Erase(Subst))`); ask A-2 required before C4a (R4 lands it in unit
    1c), so C4a, the drop chain (C4b–C6c, C3c) and the R5 plan's units X1–X3 wait for A5 step 1 and the plan's §3.2 fallback (d)
    (R5-M5) loses its meaning; PD23 reversed (`transferBytes`
    keeps `memcpy`; C8a's `MoveBytes` comment says the ranges do not overlap, against PLAN10 §5.8's overlap-allowed
    `Storage.moveBytes`, until R8); PD15 refined (F7 reads `CallPlan.TupleOperator` and `TyCtxt.Comparison`; `ComparisonPlan(CallPlan)`
    goes at H11, not R7); F6's signature, order and group; the legacy comparison composition suspends A5 plan §7's one-producer rule
    for comparison leaves until A-12 or R7; window W1 also covers `Mir/MirValidator*.cs` (F11) and C0b1 waits for W1.
  - New representations approved: `MonoKind` (nine members, `LegacyRow` `[Transitional("R8")]`), the capability rows, the
    intrinsic rows' `Target` column, `Layout.Of`'s OK/Limit/CLayout result (Oversized added by R5-M52) and its Never rows, the
    chapter-F facts (F-package §5), `TyCtxt.Witness`'s `(Kind, Target, Subst, Access)`. Each component, representation and temporary
    structure is still recorded by the unit that first implements it.
  - Temporary sel-only behaviors: K10b's double report of a written by-value C-layout instance until G2b (grouping, which SPEC
    23.1.2 leaves implementation-defined), and a generation-gate refusal that may hide independent coverage gaps until the R5
    plan's unit X2 (a temporary SPEC 23.1.2 limit).
  - Acceptance changes, from G2: collector records publish at `check` (R5-M4); K10b's `InvalidCLayout_Kd` (Language) at check for
    inferred C-layout instances; K10a's layout-depth limit at check; K10c's `OversizedLayout_Kd` (Language) at check (R5-M52); at
    G2b written borrow-only C-layout instances become valid (an (a) correction). Facts recorded: R5-M13(b)'s premise is corrected (the
    old analysis also accepts immutable static scalars in generic containers' groups); "no representation change" holds for the slot
    state only: P42's generic storage keys need the container's Type arguments on each access's `staticEnsure` operand, and since the
    static constant fold keeps no slot state, P42 narrows the fold or restores the state for SPEC 22.4.3's shutdown rule.
  - Rejected: the alternatives each decision row names (`S2-S4-decisions.md`); for X33, (b) a constant-fold whitelist (a KD1
    exception) and (c) carrying Type arguments before G2 (MIR representation work in W1 for a form no library or milestone uses);
    for the successor code, reusing `ProjectPreparationFailed_Kd`, recategorizing `GenerationFailed_Kd`, or
    `MissingNativeRequirement_Kd` for the missing-supply cause only.
- **R5-M19 `Layout`** (2026-10-18, planning session; plan P7a, package §3.5 and §7, S2-S4 X18, X31, X34 and O9, under R5-M18;
  promoted under R5-M14) A permanent Backend component that owns the storage layout of a closed Type for every generator:
  `Layout.Of(BoundType)` (static here; an instance method per compilation since R5-M37) returns its `ValueLowering` (size, alignment,
  stride, Field offsets, LLVM representation). P7a's rows:
  IMPL 21.1.3's scalars (the table that was `WindowsLowering`'s), Unit, string, Never (zero-sized, laid out as Unit), borrows,
  object borrows and raw pointers (one pointer, `RuntimeAbi.StringReference`), object handles (one word), function values
  (16 bytes), `Loan<T>` (the record of a struct without stored Fields), and the Slice, Array and Dictionary handles (2, 3 and 7
  pointer-aligned words; these three records are `[Transitional("R8")]`, since R8 declares the handles in the library). Every
  row is a static record: no call allocates and equal representations share one record. A borrow, pointer, handle or sequence
  is laid out without reading its referent or element Type (X34); a Type without a row is an internal error. P7b adds struct,
  Tuple, fixed-array, enum, Closure and Function Item placement (the old pool's `Place`) with the OK/Limit/CLayout result (X18)
  and the base-prefix invariant (X1), and deletes the `Loan<T>` row once zero-Field struct placement yields the identical record;
  `Weak<S>` comes with its operations (P41), and until then a consumer reports a valid closed Type without a row as a located
  `Unsupported_Kd` rather than calling `Layout.Of`; the nonnull `Option<R>` stays a STATUS limit until PLAN U8 (O9). The old
  pool calls `Layout.Of` for handles and for sequences (whose field count is the handle's word count). `WindowsLowering.GetValue`
  stays the old Emission's scalar-value gate (a value Type, Unit or string) over `Layout.Of` until G8, read by its callers in
  the old lowering, by the old pool's scalar Fields and by `FunctionAbiPool.GetValue`, so no second scalar table or copy of the
  gate remains. The package's "explicit Never guard" in the old pool is this existing gate: it gives Never no record, so the old
  pool keeps its null for Never (X31; STATUS), as `GetCore`'s comment and `LayoutTest` state. Rejected: re-pointing the gate's
  callers to `Layout.Of` (frozen lowering that relies on the gate's null for every other Type); a per-Type memo (no P7a row
  depends on more than the Type's kind; placement brings it — superseded by R5-M29, which stages the memo to P7c); `Loan<T>` as
  Unit's record (P7b's placement would then change the old pool's `Loan<T>` record and argument slot); a separate Never test in the
  old pool beside the gate.
- **R5-M20 Runtime location text in Core** (2026-10-18, planning session; plan P5, package F §4.10 and §5, OQ4) A permanent
  Core component, `SourceLocationTable` (`Core/SourceLocationTable.cs`), one per compilation as `Compilation.Locations`:
  `Text(SourceRef)` resolves the site through `Compilation.SyntaxOf` and renders SPEC 22.5.1's `path:line:column` with IMPL
  21.4.2's escaping, or returns null for a site without a source document, a broken contract its reader reports. It replaces
  the old Emission's two tables (BodyLowering's and the static initializers'), and BodyLowering no longer threads the project
  directory. Both generators read it, the Backend by `SourceRef` (the ARCH dependency note of OQ4); which site an Abort
  reports stays its generator's rule, and the test manifest's location (`TestCatalog`, SPEC 20.6) stays separate. The memo
  stays keyed (document, offset), never by `SourceRef` (ids renumber on edit). It is cleared when the project directory
  changes and in `Compilation.InvalidateSourceAnalysis` (every edit, parse, reload or mode change), so it holds only the
  current program's texts; before, the texts of replaced documents stayed until the directory changed. A fully qualified
  source path is shown relative to the project directory, and a project without a directory is the current directory's
  (Project's rule for its TestSources), so a location never shows the checkout path (IMPL 20.1: project-relative). Rejected:
  clearing on the Binding pass version (`HirTables.Publish`), which every warm rebind advances, so a warm Bind and WriteIr
  would allocate the texts again (asserted 0 B, `NoInitTest`); a `SourceRef` memo key; one table per generator.
- **R5-M21 `TyCtxt.Instances` and `Instantiates`** (2026-10-18, planning session; plan F3, package F §4.11, K §4.1, X8, X26, F 8;
  under R5-M18) The partial `Binding/TyCtxt.Instances.cs` owns instance identity; K3 adds `Erase` there. `TyCtxt.Instantiates`,
  given a definition's `BindingSymbol`, is true when the definition or a declaration enclosing it on its scope chain has a Type,
  Semantics or length slot, even an unused one (SPEC 6.1.3), and no specialization lies between them; Origin slots never count (SPEC 8.13.3); a
  scope owner counts only as the declaration it is, never as a symbol it names. It is the instance key's null rule (KD1) for every
  `MirBodyKind` key symbol. Its per-definition memo lives until an edit: `Binding.Invalidate` calls `ResetInstances` beside
  `ResetRetentions` (R4-M51), so a warm rebind allocates 0 B. Both readers are re-pointed (`Binding.ClosureInstances` and the legacy
  `GenericStoragePlan.IsGeneric`, D5's named follow-up), and `FunctionKoto.RequiresInstantiation`, a Syntax property reading
  Binding, is deleted. Reproducer (F fact 7): the old property classified a named function nested two levels in a generic
  function, in a block of one, in a generic struct's method or in a closure as closed. No definition in the test corpus or the
  milestones changes class (a temporary trace), so fixtures and the diagnostic and semantic snapshots are identical. Outside the
  corpus such programs stay refused, with a different `GenerationFailed_Kd` Note visible on build or run only; accepted ones whose
  nested function ignores the slots get per-instance entries with the same behavior; a two-level nested call that selects a
  specialization stays wrong in legacy, which forwards one level. F3 is therefore not a D5 miscompile fix.
  - Departure (temporary; superseded by R5-M34): a specialization stops the walk, so it and everything nested in it count as closed, although SPEC
    8.13.1 closes only its arguments and SPEC 6.1.3 makes a specialization in a generic container generic in the container's
    slots. Reason: it is the only behavior-preserving rule for the re-pointed legacy readers. Both SPEC-literal variants (the walk
    passing an enclosing specialization; no specialization rule) make legacy refuse a closure in a specialization of a generic
    struct's Type function (`GenerationFailed_Kd`), a program it runs correctly today. Removal: at K1 `Instantiates` follows SPEC
    6.1.3, and the legacy readers keep the old specialization behavior as a temporary until G7.
  - Rejected: keeping the property (Syntax reading Binding); a legacy copy of the old rule beside the view now (two rules for one
    fact); walking the Koto parent chain (the design names the scope chain, which carries conformance-path and accessor scopes); a
    SPEC-literal specialization rule in F3 (it changes legacy output for a program legacy runs correctly).
- **R5-M22 The requirement-witness core** (2026-10-18, planning session; plan F5a, package F §4.4 and §9, S2-S4 X24, under R5-M18)
  A permanent Binding component, `Binding.ResolveRequirementWitness(requirement, self, contract, scope)`
  (`Binding/Binding.RequirementWitness.cs`): the one rule from a requirement, a Self that is not abstract, the Contract reference
  the call selected and a premise scope to what the requirement runs (SPEC 8.6.1, 8.11.4, 11.9.2). Its result
  `RequirementWitness(Kind, Target, Declaring, Function)` with `Path` and `Property` has the kinds Builtin (the compiler target),
  Conformance (the witness, its declaring instance and its verified mapping with the base path), Property (the verified path and
  operation), FixedArrayEntry and IntegerPosition (the Kimi member), Refused (a base-path witness whose requirement receiver is
  not `ref`, OCC-X; the check moved here from `ProjectWitnessCall`, which now only projects) and None; the kind enum is local,
  not an ARCH vocabulary. Each caller keeps its scope. The CallPlan materializer `InstantiateRequirementCall` uses its call's
  `ConstraintScope` and keeps SPEC 8.15's abstract-Self forwarding, which runs before the core, and all materialization (Origin
  translation, receiver projection, the standard bridge, CallPlan storage) (X24). `Binding.Witnesses`, which A4 reads through
  `TyCtxt.Witnesses`, uses its definition's `EffectPremises` and keeps its contract (R4-M28) and results: Builtin and Conformance
  as before (a built-in comparison through its plan leaves); Property, FixedArrayEntry, IntegerPosition, Refused and None give
  false. The built-in target symbols (comparison, formatting and the six arithmetic ones) take their requirement's name,
  declaration, result Type and receiver slot once per pass after headers (`PrepareBuiltinTargets`; F fact 11a) instead of on
  every call, so the core writes nothing beyond interning, proof caches and instance storage preparation. F5b's `TyCtxt.Witness`
  will call the core. Refused is unreachable today: conformance verification rejects an inherited witness with an exclusive
  receiver first (`ProjectedReceiverProof`), the receiver match excludes an object receiver (`ProjectRequirementReceiver` projects
  only a `ref` or `uniq` receiver of Self to the base), and no test exercises it. Rejected: built-in targets created lazily by
  the core (a view would create symbols); the arithmetic built-ins left with per-call writes (the same cause); the refusal left in
  the materializer (F5b resolves without a CallPlan); extracting `InstanceReference`'s matching half before F5b, its second caller.
- **R5-M23 The R6-check prerequisite package Q8** (owner, 2026-10-18: 「Q8を承認」, answering the request to approve the package
  with Q8-6 and Q8-7 as proposed; OQ10, R5-M1) `draft/Sketches/R5 Step 0/Q8-package.md`
  with `Q8-decisions.md` (Q8-1a–Q8-8), after two independent design-review rounds, is the owner-approved design of DEC Q6 form 1
  for Q7, Q16, Q17, Q21 and H1a, and of form 2 for the lint group G:L (Q9–Q12); H1 and the validator rule take it with the final
  review in G:R6-check-flip. The owner approved with it the two proposals that reached two review rounds: Q8-6 (one renderer:
  the Semantics-case formatting moves into Binding beside `FailingCaseNote` and serves both forms; the old Analysis calls it until
  H11; DIAGNOSTICS :166's record text stays) and Q8-7 (a closure whose creation failed keeps its own `Consume` as a Closure (its
  defaults and its expression body's result), only its creation Opaque; in-place writes apply to the operands of failed compound
  Places only).
  - It decides: Ownership requirement kinds owned by Diagnostics (Q7); one HIR lint component (12 rows, PD31's record form,
    condition = row, one walk at the end of `Bind(Final)`); the jump binding writing the target row, with the HIR contract that a
    null row has a row 9 or 10 record (Q11; ask A-8); FunctionFallthrough judged in Binding's result validation (PD32); Q16's two
    views; MIR problems naming their requirement (`MirProblem.Requirement` set by each recording rule, `MirReport.Add` checking that
    merged problems agree; revised ask A-5, restoring PLAN10 §7); TransferRequired judged by a scan of the published columns with
    three sources after an eager Final publication (H1a), PreparedParam-rooted Places recorded by Binding as `DefaultArgumentMove_Kd`
    with no repair and requirement `Binding.TransferRequired` (so A1's PreparedInput mapping serves only an explicit `a@move`), the
    Semantics case fact carried through Q8-6's renderer and the omitted-capture-list record kept; kept children of failed nodes
    (Q21, closing PLAN G121; in R5's window W1, agreed with R4 as ask A-14); `MirValidator`'s rule that a `Copy` operand has a Copy
    Type (Binding's published proof, `PairAcquire` and per-view `Adapt` exempt); `Binding.ClosureInstances` and
    `Binding.RequirementInstances` at R7; Q13 closed and Q14 comparing full records.
  - Departures from owner-approved and recorded texts, approved with it: PLAN10 §5.5/§9 (PD32); the plan's rows Q11, Q13, Q14,
    Q17, H0, H1, H3 and ask A-5; R4-M3a's naming and derivation clauses; R4-M61's "replaced whole" for kept children; R2b-D2's
    lazy publication (eager at the end of each Final pass, twice when H1 failed a node); R4-M4's pass-through of TransferRequired
    (ends at H1); the R0 dispositions of the two instance files; the public requirement names of the 12 lint codes (`ControlFlow` →
    `Binding`), `Ownership.TransferRequired` → `Binding.TransferRequired`, a default's bare read (`Ownership.DefaultArgumentMove`
    → `Binding.TransferRequired`, code unchanged), and at H5 A4/A5's `Binding.*` requirements becoming `Ownership.*`. Each component,
    contract change and superseded entry is recorded again, with its back-pointer, by the unit that first implements it; R4-M3a's
    derivation clause, a plan statement this entry overrides now, carries its pointer already.
  - Temporary structures, with the stage that removes them: the old analysis's reads of `LintErrors` and `FallthroughKey`, and
    ControlFlowAnalysis's call to Binding's guess query (H10); the old Analysis calling Q8-6's renderer (H11).
  - No new vocabulary entry, diagnostic code or requirement name (PD21; no new `BindingFailure` member). New: the HIR-lint
    canonical component; the
    reassignability view and Tuple-element `PlanRow.Folded` rows (Q16); the jump target row's writer; `MirProblem.Requirement`
    (R4's record under A-5). Rejected: restructuring `HirPublisher.Consume`; a judgment at the publisher's visit; node failures
    for lints; a new `BindingFailure`; a code → requirement table (kept only as the fallback if R4 declines A-5).
- **R5-M24 Ownership requirement kinds owned by Diagnostics** (2026-10-18, planning session; plan Q7, package Q8 §3.1, under
  R5-M23; supersedes R4-M3a's naming clause) `OwnershipRequirement` (`Diagnostics/DiagnosticRequirement.cs`) is the kind
  enumeration of the Ownership partition: the 18 members of the former `OwnershipFailure` in the same order, so every Kind,
  requirement name and requirement-table row stays. `DiagnosticRequirement.Ownership`, `All` and `NameOf` take it; the old
  analysis records it as its failure kind (`OwnershipIssue.Failure`), and Mir may name it. `OwnershipFailure` is deleted.
  R4-M3a's reason no longer holds: the names were already public diagnostic vocabulary (the requirement table), not old-path
  state. Invariant: one kind enumeration per partition (Binding's is `BindingFailure`). Prohibitions: no copy or mapping in an
  analysis; a member leaves with its last producer (`TransferRequired` at H1, the others at H10/H11), and no member is
  added (PD21). Rejected: a parallel enumeration mapped from `OwnershipFailure` (a second copy); moving
  `OwnershipFailure` unchanged (its name describes the old analysis); one cross-partition requirement enumeration now (R7's
  replacement of `BindingFailure`, PLAN10 §7).
- **R5-M25 Backend roots and externs named by symbol** (2026-10-18, planning session; plan F2a, package F §4.10 and fact 8,
  S2-S4 decision 4, under R5-M18) Rule: a Backend root or extern names its definition the way `MirDefKey` does. A Koto column is
  replaced where every reader can derive the Koto from the symbol; otherwise the symbol is added, and the Koto column goes with
  its last syntax reader. F2a applies it to startup: `StartupResult.Symbol` (`Binding/Binding.Startup.cs`) holds the explicit
  `main`, and `Kind == Implicit` means the startup body is the generated function of top-level statements, `MirDefKey(null, Fn,
  0)`: the startup module is always ordinal 0 (`SourceModules[0]` is `Compilation.Kotonoha`), so no module column is published.
  The Implicit body has no symbol, so not every reader can derive the Koto from `Symbol`: by the Rule's otherwise clause
  `StartupResult.Function` stays and goes with its last syntax reader, `LlvmEmitter`, at G7; tests read `Symbol` and `Kind`.
  F2b applies it to library imports: `LibraryImport.Decl` (`Binding/BindingModel.cs`), the imported function's symbol, replaces
  `LibraryImport.Function`, since every reader (the old emitter, `EmissionArtifacts`, tests) derives the Koto as `Decl.Declaration`;
  P8's C-convention `Abi.Of` takes the import's Types, which the old emitter reads through `Decl.Declaration`, so the column's
  first Backend reader comes with the MIR generator's import calls and declarations. F2c applies it to the legacy Dictionary
  rows' callees (R5-M28).
  Rejected: a module ordinal column (plan row F2a; always 0); replacing `StartupResult.Function` by `StartupResult.Symbol` now
  (F2a: for the Implicit body a reader would derive the Koto from `Kind`, not from the symbol); `LibraryImport.Decl` beside
  `LibraryImport.Function` (F2b: every reader derives the Koto as `Decl.Declaration`).
- **R5-M26 `AdtDef.IsCLayout`** (2026-10-18, planning session; plan F1, package F §4.9 and §9, S2-S4 X18 and X26, under R5-M18; R5-M55
  adds `AdtDef.CLayoutRejected` for a declaration rejected for a direct zero-sized Field)
  Binding's published fact that a struct's C layout validated (SPEC 21.1). Producer: `ValidateLayoutFragments`
  (`Binding/Binding.Attributes.cs`), true exactly for a struct whose selected mode is `#Layout("C")` with valid fragments and that
  is neither open, derived nor empty. It is written true or false for every struct in every pass; on a first pass the shape is
  created there (`Adt ??= new()`) and `PrepareStorage` reuses it, and no reader in between distinguishes an empty shape from a
  missing one. Readers: `ValidateCLayoutFields`, which filters the storage declarations by it (the pass-local `cLayouts` list, a
  second representation, is deleted), and `CExchangeable` (SPEC 22.6), which reads it with the prepared Field Types, since
  `ValidateLibraryImports` runs after `ValidateCLayoutFields` (PLAN G132, BU commit `a779b529`). The reader that still uses the
  attributes: `Binding.Types`, which queues `CheckCLayoutInstance` by the attributes (`HasCLayout`) whenever it binds a written
  generic C-layout instance, in headers and bodies alike, until G2b. First consumer outside Binding: P7b's `Layout.Of`, the
  one owner of the zero-sized C-layout rule (X18), which deletes the old Emission's attribute walk and `Open` re-check.
  Rejected: a C-layout instance view in `TyCtxt.Instances` (X26); keeping `cLayouts` beside the fact.
- **R5-M27 Legacy Dictionary helper rows** (2026-10-18, planning session; plan P1c and its follow-up P1c2, PD2, S2-S4 X4;
  refines R5-M16; refined by R5-M28, by R5-M30, which adds the formatting rows and the `HelperNeeds` member
  `FormattingRuntime`, and by R5-M46, which narrows the rows' Array runtime needs) `DictionaryHelperRow` is one static row per `DictionaryHelperKind` (CheckKey, Place, Drop), read by
  `LegacyRuntimeHelpers.DictionaryRow(kind)`, which fails closed: `Kind`; `Shape`, the ABI over the generator's name suffix and
  the key and value descriptors, with the kind's name prefix inside it; `Write`, the body's writer over the helper and the ABIs
  of its two callees; `Entries`, the `DictionaryStorage` function generic over the entry Types, which a helper reaches through
  its `Related` index (findEntry for CheckKey, clearEntries for Drop); `Storage`, the function the module resolves once
  (requireAbsent for CheckKey, append for Place); `Needs`. `DictionaryCallee` names the `DictionaryStorage` functions by the
  `KimiLibrary` declaration the old emitter resolves each from, until F2c's symbol view; the emitter maps a row's `Entries`
  callee to its generic function exhaustively and fails closed on any other. `CallsEntries(destroysEntries)` is the one decision
  whether a helper calls its `Entries` function (always, or under `ElementGlue` only when an entry needs destruction), read by
  BodyLowering's `Related` and `LegacyRuntimeHelpers.HasUnresolvedReference`. `HelperNeeds` is shared by the Array and
  Dictionary rows: `ArrayRuntime` (every Dictionary kind), `DictionaryRuntime` (P1c2; CheckKey, whose duplicate callback reports through the
  runtime's adapter) and `ElementGlue` (Drop). A request's runtime flags come from its row (`Request`, through one `JoinNeeds`
  for both families), and every requested helper is written after the runtimes its row declares: before P1c2 a Drop helper
  requested only by a Field's drop glue set no flag and was never written (PLAN G133). The row callees are exactly the functions
  the helper text calls: requireAbsent and append, and findEntry and clearEntries through `Related`. The Dictionary runtime (the
  duplicate-key adapter and the initialization bridge, which calls `initialize`) has no row yet: the old lowering also requests
  it for a Dictionary's construction, and the Backend component declares its need and callee by C8e at the latest, never as a
  collector-side rule (X4). Count: legacy text calls five `DictionaryStorage` functions (the four row callees and `initialize`),
  which corrects plan row P1c's "seven library ABIs" (R5-M16's "seven `DictionaryStorage` ABIs", the seven the module resolves)
  and F2c's "nine callees" (every `DictionaryCallee` but None): `appendSlot` and `clearLinks` are called only by compiled
  sources, and the initialization bridge checks that they compiled; `reserve` is resolved only for two tests' compiled-name
  reads, and `shrinkToFit` is resolved but read by nothing. Rejected: callees as delegates (unreadable by kind); the name prefix
  as its own field (a seventh parameter, over ARCH's six); declaring `reserve`, `shrinkToFit`, `appendSlot` or `clearLinks` as
  row callees (no helper calls them); the Dictionary runtime for every Dictionary request (Place and Drop use neither the
  adapter nor the bridge); helpers written only under the runtime flag, or flags set by their requesters (G133).
- **R5-M28 The `DictionaryStorage` symbol view** (2026-10-18, planning session; plan F2c, package F §4.10 and §5, S2-S4 X4,
  under R5-M18; applies R5-M25, refines R5-M27) A `[Transitional("R8")]` Binding view,
  `KimiLibrary.DictionaryStorage(DictionaryCallee)` (`Binding/KimiLibrary.DictionaryStorage.cs`), gives the `BindingSymbol` of each
  `DictionaryStorage` function the Backend resolves by name, naming its definition the way `MirDefKey` does. `DictionaryCallee`
  moves from the Backend into Binding as the view's key, and the legacy Dictionary rows declare their callees in it (X4); None, a
  row position without a callee, names no function. Members: the five functions legacy text calls (R5-M27) — findEntry and
  clearEntries, generic over the entry Types, which a helper reaches through its `Related` instance; requireAbsent and append,
  which the helpers call; initialize, which the Dictionary runtime's initialization bridge calls (the runtime has no row yet; C8e
  declares its callee) — and reserve, kept because two tests read its compiled name and the old emitter has no other path from a
  source function to its ABI (it goes with the old emitter, or when those tests read codegen item names). No catalog row and no
  `KimiDeclarationId`: a cataloged function (findEntry, append, reserve) keeps its catalog symbol, and each other one gets its
  symbol at library construction, registered like the catalog's so that Binding's indexer declares that same symbol
  (`KimiLibraryShapeTest`). By the Rule the `FunctionKoto` columns of these six functions are replaced, since every reader (the
  old analysis, the emitter, `Binding.DictionaryStorageCall`, tests) derives the Koto as the symbol's `Declaration`.
  `Binding.DictionaryStorageCall` takes the callee and fails closed on any but the two generic functions; the emitter's mapping
  goes, and the emitter resolves every view function into the module (the generic ones stay null). appendSlot and clearLinks,
  callees of compiled sources that compile with their callers, are no longer resolved: the bridge checks initialize only.
  shrinkToFit's resolution, read by nothing, goes with its column (tests read the catalog symbol). The unlink, appendSlot,
  clearLinks and compact columns stay for the old analysis and tests, until their last syntax reader goes. Rejected: catalog rows
  (a temporary view in a held file); one symbol property per function (each consumer would map a row's callee to its property);
  a key beside `DictionaryCallee` (two names for one concept); reading the symbol from the declaration's HIR column (a forwarding
  property, removed at R6); keeping appendSlot and clearLinks (no legacy text calls them).
- **R5-M29 `Layout.Of` placement and its result** (2026-10-18, planning session; plan P7b, package C §3.5, S2-S4 X1, X18, X31 and
  X34, under R5-M18 and R5-M19) `Layout.Of` returns `Layout.Result(Value, CLayout)`: OK with the shared `ValueLowering`; CLayout
  with the C-layout instance, at any depth, that stores a zero-sized Field (a stored Never counts, IMPL 21.1.3); Limit, neither, when
  a size or offset exceeds 2^31 − 1 (superseded by R5-M37's outcomes and window). `Backend/Layout.Placement.cs` holds the old pool's
  `Place` and enum placement, promoted:
  Kimigayo mode's alignment-descending groups in written order, C mode's written order (from `AdtDef.IsCLayout`; the attribute walk
  and the open, derived and empty re-check are deleted, since Binding rejects those), the `i32` tag before the payload union,
  zero-sized parts kept, and the complete base with its tail padding as the prefix (X1; a derived struct has no C layout, SPEC
  6.7.1). The `Loan<T>` row is deleted: struct placement gives the identical record. Each representation has one record, shared by
  every Type, compilation and thread (a concurrent table keyed by storage text, size, alignment and offsets; per-thread scratch;
  superseded by R5-M37: records and scratch per compilation, records per program version), so
  a call that finds its records allocates nothing. The old pool keeps its own recursion (the Origin guard, Never's null through the
  scalar gate, destruction, collection Fields, the count and depth bounds) and calls `Layout.Of` where it placed, after its shape
  interning, mapping CLayout to its refusal and Limit to its resource failure.
  - Declared behavior change (planning session): the old pool now visits every Field of a C-layout instance before `Layout.Of`
    reports its zero-sized Field. For `#Layout("C") struct P<T>` with `var value: T`, then `var big: [2147483648 of u8]`, and an
    inferred `f(())` writing `Option<P<T>>`, the parent reported `GenerationFailed_Kd` and P7b reports
    `GenerationResourceLimit_Kd` (`InlineLayoutTest`): only which of two real failures an invalid program reports, and K10b's
    `InvalidCLayout_Kd` supersedes both. The visit also runs the pool's side effects before the refusal: where a refusal is
    tolerated, as for a Sealed object handle's payload (`SealedObjectHandle` falls back to dynamic cleanup), a later collection
    Field's `CollectionDrop` registers its element drop helper. A probe (`#Layout("C") struct P<A, B>` with `var a: A` and
    `var b: B`, `Option<obj/P<A, B>>` in an inferred `f((), values@move)` with `values: Array<i32>`) then emits the collection
    Field's descriptor and the Array helpers it calls, unreferenced, and the later aggregate numbers shift by one; execution is
    unchanged.
  - Staged gap (new unit P7c; closed by R5-M37): `Layout` keeps no Type memo, against package C §3.5's "memoization by interned Type"
    and R5-M19's
    "placement brings it". Binding keeps interned Types across passes (`Binding.ResetPass`) and re-substitutes their stored Types
    when `storageVersion` advances (`Binding.Storage.cs`); a nominal Type reaches its compilation (`AdtDef.Instance`) but a
    structural one does not, so a static memo keyed by Type would go stale and retain every compilation's Types. The old pool
    memoizes per body. P7c takes the full process before any MIR caller of `Layout.Of` (C2a, C3c) and owns the memo, the depth and
    cycle guard, the count bound (`[2147483648 of ()]` gets OK today), a Limit that is not `default(Result)`, and the scope of the
    process-wide record table (its lifetime in the LSP server).
  - Rejected: a per-compilation `Layout` instance (R5-M19 fixes the static `Layout.Of`, and rows and placement would split;
    superseded by R5-M37);
    `Layout.Place(fields)` fed by the old pool's records (a second entry beside `Of`); comparing records instead of the layout-mode
    flag in the old pool's shape interning (a struct of a raw pointer and an `isize` would stop sharing the Slice handle's shape);
    keeping the per-Field C-mode check in the old pool (a second owner of X18's rule); the C-mode refusal of an alignment above 16
    (unreachable: no row is more aligned, and placement takes the maximum).
- **R5-M30 Legacy formatting rows** (2026-10-18, planning session; plan P1d, PD2, S2-S4 X4, K package §4.5 row 2 and K6;
  refines R5-M16 and R5-M27) `FormattingHelperRow(Kind, Abi, Needs)` is one row per formatting compiler function, the 28
  `CompilerFunctionKind`s TextFixed through BuiltinFormat (the 26 Body-disposition kinds and the routines of the ErasedWriter
  intrinsic and the BuiltinFormat shim), read by `LegacyRuntimeHelpers.FormattingRow(kind)`, which returns null outside that
  range and throws on a missing row. `Abi` is the routine's ABI in the formatting runtime: the fixed symbol every call names and
  the runtime splice defines (rows sharing a symbol share its routine, defined by the first of them in kind order). A row declares
  only what its text calls or needs: every row's `Needs` is the new `HelperNeeds` member `FormattingRuntime` (the UTF-8 buffer,
  writer, formatting and floating-point runtime and the memory declarations), and no row declares a library callee, since the
  formatting runtime calls none. The formatting witness that WriterWrite, TextToString and TextTryFormat run is already
  published once: their `KimiLibraryCatalog` rows carry SPEC 4.9.6's `Formats` and `FormatsValue` rows (Runs `Utf8Format`, On
  parameter 0 or 1), read through `TyCtxt.Published` as A4 reads them (`MirEffects`). K6 takes the witness target of these
  Body-disposition calls from `TyCtxt.Published` (refining K row 2 and K6); TextWriter's is the ErasedWriter intrinsic's target
  (K row 6) and BuiltinFormat's leaves are its Builtin item's. The wrapper requests (`RequestFormattingWrite`, for WriterWrite's
  row; `RequestFormattingConversion`, for TextToString's or TextTryFormat's) join their row's needs through the `JoinNeeds` the
  three families share; the planner (`GenericStoragePlan`) requests each wrapper once and only for those kinds, and
  `WriteFormatting` writes every requested wrapper after the runtime, no longer under its flag (G133's shape; a full-suite probe
  at 70f06c4b found no module with a wrapper but no flag, so no output changed). The old lowering still sets the flag for its
  direct calls of formatting routines and for interpolation: calls, not helper requests, like R5-M27's direct capacity calls.
  The rows are per compiler function because the collector reads a Body-disposition call by its compiler function (K row 2)
  and C9a calls the routines through their row ABIs; the wrappers are codegen's, derived from the witness target. Rejected: no
  rows (C9a would read the old Emission's table); rows per wrapper kind (the collector never sees a wrapper); a
  `FormattingWitness` row bit (a third statement of the published `Formats` rows, beside `IntrinsicDisposition.Effect`'s
  `Formatting` class, naming neither the requirement nor the parameter; and no formatting routine's text calls a witness, unlike
  an `ElementGlue` helper's); wrappers written only under the runtime flag (G133).
- **R5-M31 HIR lints** (2026-10-18, planning session; Q8 package §3.2 and §6 Q9, under R5-M23 and PD31) A permanent canonical
  Binding component, `Binding/Binding.Lints.cs`: Binding's checks that never fail a node, one row per code in a lint table (code;
  derived from `FailureCauses`; rests on a guessed completion), the row number being its records' condition; a row whose check
  rests on a Binding failure or a guess records nothing when its code is a warning and is derived from those causes when it is an
  Error (DIAGNOSTICS §4.3, Later phases). One walk of the source modules' evaluated syntax is the last step of `Bind(Final)`
  (Types, attributes, Origin clauses, effect items, a function's signature and a for loop's bindings are not evaluated; an
  excluded test and a function part holding an unselected `#switch` are not linted, as before). Its records `(node, row, root)`
  live in a list cleared by `ResetPass` and are published by `ReportDiagnostics` at Final (`ReportNonFailingRecords`, beside the
  alias, pattern and position warnings) under requirement `Binding` with `BindingFailure.None`; duplicates and SPEC 17.5's
  priority are resolved by sorting at report time, so a warm rebind allocates nothing; the walk keeps its own structural completion
  for its descents and end arrivals, cleared once per walk (Binding's shared `ResultStructure()` clears on every query, since
  binding-time facts change while it runs). Q9 lands rows 1-6 (`DiscardedTail`, `DiscardedResult`, `UnusedTrySuccess`, `DiscardedValue`,
  `OwningWriteArgument`, `StaticWhileTrue`) with their repairs; Q10-Q12 add rows 7-12. `Koto.ReportDerived` takes the condition of
  a derived lint Error. The guess query moves into Binding as `GuessedCompletion` over `ResultStructure`, counting an unresolved
  expression as a guess besides a supplied body (Q8 design 2 §2.4); the old control-flow analysis's fallthrough checks call it
  until H10. The six codes' requirement becomes `Binding` (was `ControlFlow`). Removing them from `ControlFlowAnalysis` is an
  R6-check item landed early under D5.
  - Q10, rows 7-8 (`UnsafeBlockRequired`, `UnnecessaryUnsafeBlock`; SPEC 14.3): an operation needs the permission when it forms
    a raw Place, does pointer arithmetic, is `noinit`, or one of its calls (`CallPlan.Of`) selects an unsafe function, so a
    synthesized call is judged at its source node and a function reference never; the walk keeps the innermost block's used and
    uncertain bits. `LintErrors` counts the lint Errors, which the old analysis adds to its own (earlier rejection, verification)
    and `LegacyRecords` lists as Binding records, until H10. The old analysis's copy of `UnsafeFunctionValue_Kd` goes: Binding
    rejects every form at the reference (its four sites), and the copy only repeated it at a requirement item's inner name.
  - Q11, rows 9-11 (`InvalidJumpTarget`, `UnlabeledYieldTarget`, `OverlappingLabel`; SPEC 14.5.2, 14.7, 14.4): one rule,
    `KotoHelper.IsValidTransfer` (a target exists; an unlabeled yield's selection uses its result), decides a transfer; the jump
    binding writes the target row (`PlanRow.Plan`) only for a valid transfer, so the HIR publisher no longer resolves targets, and
    the walk records rows 9-10 by the same rule. HIR contract: a null target row of a published Jump or Continue means Binding
    recorded row 9 or 10 there (ask A-8), or, the lints being dropped there, that the jump lies in a function part whose `#switch`
    selected no arm, which `NonExhaustiveCompileTimeCase_Kd` rejects (amended by R5-M42's unit). The table's guess column names
    what a row reads that a recovery may have guessed: a completion (rows 1 and 12) or a target (rows 9-11, which rest only on a
    guessed label or a recovered label).
  - Rejected: one shared `ResultStructure()` per walk (any query inside the walk clears it; per-query clearing composed a nested
    selection's completion 8,256 times instead of 528); a node failure per lint (the old analysis would skip the program; PD29, PD31); a Koto-keyed set of warned nodes
    (ARCH Syntax identity, warm allocations); a copy of the old walk's synthesized-evaluation switch (rows 1-6 judge source
    syntax only: a synthesized call's parent is its source node, a Value Context); a reporter in each owning file (Binding.cs and
    Binding.Patterns.cs are over the token limit).
- **R5-M32 FunctionFallthrough is result validation** (2026-10-18, planning session; plan Q12, Q8 package §3.4, PD32, under
  R5-M23 and R5-M31) SPEC 14.9.3 judges a Block body's structural end by Structural Completion: the end supplies Unit, which a
  declared result other than Unit cannot take (SPEC 7.1). `Binding.EndArrival`, beside `AddBodyResult` in `Binding.ControlFlow.cs`,
  decides it once per function and accessor from the HIR lint walk (row 12 at the body, Error), with the walk's structural completion and the
  declared result (`Binding.DeclaredResult`: the written result, else the bound contract; Unit for a discarded body or a setter),
  which moves from the old control-flow analysis; the old analysis still asks it whether a contract is pending. The record rests
  on a guessed completion (`GuessedCompletion`), which replaces the old check's pending gate: an unresolved expression is a
  guess whose Binding causes the record is derived from. `Binding.FallthroughKey` gives the record's key, on which the old
  analysis's missing result at that body's end rests until H10. The MIR records nothing at that end (Q8 probe p9, p10; R4-M21),
  so nothing on the MIR side derives from it and the ledger's `a1-result` row stays. The requirement becomes `Binding` (was
  `ControlFlow`). This departs from PLAN10 §5.5/§9, which gave the check to A3 (owner-approved with R5-M23).
  - Rejected: an A3 rule on the MIR (a second completion rule beside Structural Completion); a check in `BindFunction`,
    `BindAccessorBody` and `BindClosure` (three sites and records from tentative passes); keeping the old pending gate (a
    second guess rule).
- **R5-M33 A diverging Call's destination is read only where the checks continue** (2026-10-18, planning session; plan F11,
  PLAN G131, C-package §7 row F11 and row 33, S2-S4 X29, X30 and X30′, owner-approved with R5-M18; R4's independent review)
  Rule, in `MirValidator` (the producer's one validator; `MirViolation.Diverged`): a Call without a runtime successor writes no
  destination (`MirUses`), so a Temp whose only writes are diverging Calls' destinations holds no value at run time, and only
  check-only blocks, which no runtime edge reaches from the entry (`MirReach` without CheckTarget edges), read, move or borrow
  it. Its Drop, like its StorageDead, reads nothing: a runtime Drop of storage nothing initialized is valid MIR, which
  `MirDrops` elides, so the rule excludes `DropUse` as it excludes `Kills`. Any other place is valid wherever it is read: a
  Never-typed parameter, Field or payload (SPEC 3.1.3, 14.9.3), a Var or the return local, and a Temp also written on another
  path (an `if`'s arms sharing a destination). The tests build three valid programs that Binding accepts:
  `func absurd(x: Never) -> i32 => x`, an `if` arm (`let x: i32 = if c => stop() else => 1`, then `return x`) and a diverging
  argument (`take(n, stop())`). A read of a Never payload binding (`.Err(let e) => e` on `Result<i32, Never>`,
  `.Some(let e) => e` on `Option<Never>`) is valid, but Binding rejects it today (TypeMismatch_Kd, NoApplicableOverload_Kd;
  PLAN records it), so no MIR of one is built yet. A runtime read would be lowered as a read of storage nothing initialized; R5
  relies on the rule from C0b1 and C2b, which lower Never temps. The Checker records each local's writes while it walks a body
  (a diverging destination, which `MirUses` does not report, and any write it reports); only a body with such a Temp walks its
  runtime blocks once more. Neither reach rule (Unreachable, Diverged) runs on a body some location of which `MirUses` cannot
  walk; `Unreachable` formerly ran when only the successors were in range. The rule finds nothing in the library, the
  milestones, the corpus, the goldens or the whole suite under the mir pipeline: no builder fault. Cost: R4 measured
  validation 8–17% slower in a warm A/B (0.08–0.18 ms per compilation). Rejected: a Never temp read only in check-only blocks
  (X29: it rejects valid reads of Never parameters and payloads); "the destination of a diverging Call" (X30: it rejects an
  `if`'s arms sharing a destination) and "the builder's unwritten temp" (it misses an ordinary call-result temp,
  `_4 = stop() -> ! check bb1`); a MIR representation of the fact (only the validator reads it).
- **R5-M34 `Instantiates` follows SPEC 6.1.3; the old generator keeps its view** (2026-10-18, planning session; plan K1's
  prerequisite, S2-S4 X40, package K §4.1, PLAN SP; under R5-M18; supersedes R5-M21's departure clause) `TyCtxt.Instantiates`
  is true when the definition or a declaration on its scope chain has a Type, Semantics or length slot. A specialization declares
  none (SPEC 8.13.1) and no longer stops the walk, so a specialization in a generic container and every declaration nested in it
  are generic in the container's slots, their instance key holding the container's arguments (SPEC 8.13.3); a top-level
  specialization stays closed. One walk classifies a definition as Closed, Generic or GenericThroughSpecialization (a slot reached
  past a specialization), memoized as before.
  - Temporary structure, removed by G7 with its reader: `[Transitional("R6-gen")] TyCtxt.InstantiatesInLegacy`, true for Generic
    only (R5-M21's rule), read by the old generator's `GenericStoragePlan.IsGeneric` alone, the one rule under which it
    generates a closure in a specialization of a generic struct's Type function, as it does today.
  - Binding's closure contexts (`CloseClosureTypes`, which `TyCtxt.Apply` delegates to) read `Instantiates`: a context is made only
    for a substitution whose target is the closure's nearest named enclosing function, and the old generator forms none whose
    target its view counts closed, so its output is unchanged, while MIR's `Apply` gives a closure in such a specialization the
    container's context, which the collector's closure edges key by (K5). The effect-bound check's specialization call plan
    (`Binding.EffectBounds.cs` `Specialization`, resolving body Types through `InstantiateStorageType`) and `MirEffects` through
    `Apply` reach the same contexts on both pipelines, a move toward SPEC that no corpus case shows (X40 review). The "old
    readers" of X40 are the old generator's: `Binding.ClosureInstances` takes the SPEC view (K5 keys closures by it).
  - Evidence: no definition among the 456 snapshot cases (library, milestones, corpus) changes class (temporary probe); the 17
    F3 reproducers and three new probes give the parent's old-generator output; the program R5-M21 named still builds
    (`GenericClosureDispatchTest`). Legacy-only sites keeping the old treatment: `OwnershipModel.IsConcrete` (R6-check) and
    `LlvmEmitter`'s body filter (G7).
  - Rejected: both readers on the SPEC rule (the old generator refuses that program, `GenerationFailed_Kd`); a copy of the old
    rule in Emission (two rules for one fact); a mode parameter on `Instantiates`; the old view in `CloseClosureTypes` (the
    collector would key a generic closure as closed).
- **R5-M35 `InstanceCollector`, `CheckedSet` and the code generator selector** (2026-10-18, planning session; plan K1, package K
  §4.3-4.5, §5 and §9, S2-S4 X7, X15, X27, PD4, PD5; under R5-M18) A permanent Backend component, `InstanceCollector`
  (`Backend/Instances/InstanceCollector.cs`, contract entry), owns the checked set of a program version (SPEC 8.15.3, IMPL 21.3.1) and
  the one resolution of each call site's callee that code generation reads. It publishes `CheckedSet` (`Backend/Instances/CheckedSet.cs`):
  items in admission order, the roots first in `MirProgram.Bodies` order, then a FIFO worklist, one item per `MonoKey`; each item's
  targets `MonoTarget(Item, At, Runtime)` in its body's location order, a range of one array; `Find(MonoKey)`. The body-instance key is
  `Instantiation(MirDefKey, Subst? Erased)`, null exactly when the definition does not instantiate (PD5, KD1). Roots: each body of a
  root module (`Source.Module < rootModules`, an input range, not a mode) whose definition does not instantiate. Templates: once per
  body and version, the Call sites that may carry a target (Fn, Requirement and Value callees and the intrinsics whose `Target` is not
  None; a Virtual call none, X5), each with its runtime bit from the collector's own `MirReach` walk without CheckTarget edges. K1's
  replay admits closed callees: a Fn callee's body by `TyCtxt.Implementation(..., 0)` and `MirProgram.Callee` (a default template's,
  the function's, getter's or setter's) and a Closure of a closed definition, which is called through a borrow (`TyCtxt.Layer`).
  `Compilation.BuildMir()` runs it after `MirAnalysis.Run`, whatever that found, under MIR code generation only until G2; it records
  nothing until K4b and decides no acceptance; a warm run allocates nothing.
  - `IntrinsicRows` (`Backend/IntrinsicRows.cs`), the Backend's row per `IntrinsicId`, with the `Target` column (X15): ErasedWriter
    Witness, StaticEnsure StaticInit, MakeObj, MakeRc, MakeArc, Clone and TypeTest Descriptor, DestroyValues Glue, every other None; an
    id without a row, None included, throws. The template builder is its first reader; C0b1 adds the writer column.
  - Staged as rows of the same representation: `MonoKind` has only `Body`; the other kinds, `MonoKey`'s Symbol, Type and Variant, and
    `MonoItem`'s representative, view, parent, depth and site come with the units that produce them (K4a-K8).
  - Temporary structures: the `KIMI_CODEGEN` selector (`CompilerCodegen`, `Compilation.DefaultCodegen`, `CodegenOf` and the
    per-compilation `Codegen`), read once per process, `mir` valid only with `KIMI_PIPELINE=mir` (any other value or combination is a
    configuration error), a compilation's code generator following its pipeline, marked in `CompilerIdentity` (` [mir, codegen mir]`)
    and the test artifact manifest; G4 removes it. The sel generation refusal (`Compilation.RefusesCodegen`), read by `WriteIr`,
    `Validate` and `EmissionArtifacts.Publish`: one `Unsupported_Kd` in the Emission partition at the body of each output root (an
    Application's startup, a test build's tests, a Library's roots), never the old generator, which `Project.Emit` leaves in place; C0a
    moves it into the generation entry and C0b1 deletes it.
  - Rejected: a process-wide selector only (no compilation could select MIR code generation in a test); sharing the driver's `MirReach`
    (an edit of the driver); the refusal in the old generator's `TryPrepare` or `ReportFailure` (it is not that generator's failure).
- **R5-M36 Closure contexts as `Subst`** (2026-10-18, planning session; plan K2 (A5P-C1), package K §4.2 and §9, S2-S4 X10, under
  R5-M18) `BoundType.ClosureContext` is the `Subst` of the instance that closed the closure. `CloseClosureTypes(type, instance)` sets it to
  exactly the substitution `Binding.InstantiateStorageType(type, subst)` instantiates under, its conforming Self included (PLAN G142,
  closed: `TyCtxt.Apply` passes its substitution whole; the CallPlan form instantiates under `TyCtxt.StorageSubst(call)`, the Self-less
  substitution the old synthetic CallPlan's `SubstOf` gave and the old paths have always applied), so one instance's closures share it,
  and interning replaces the never-cleared list of synthetic CallPlans and its linear scan; the
  closure's result in the instance stays the failure check, while the CallPlan's ReturnType, a function of the rest, is dropped.
  `SubstOfClosure` reads the field, `ClosureSignature` is `Apply(signature, context)`, and Hover identifies a context by
  `SubstIdentity` (`Checking/Hover/HoverBuilder.Substitutions.cs`; `HoverBuilder.cs` is over the token limit): its binder and every
  binding. The rebuild sites and the reference comparisons of `ClosureContext` are unchanged.
  - Temporary structure, deleted by G7 with its readers: `[Transitional("R6-gen")] Binding.ClosureCall(closure)`, the CallPlan the old
    generator keys a closure's instance by (`GenericStoragePlan.PrepareClosures`, `BodyLowering`'s value callees). There is one per
    (closure symbol, context), as the old scan gave, so two closures of one instance keep two (X10). It is kept with the function item
    contexts until an edit (`ResetCallContexts`, which keeps `Binding.cs` from growing; G7 inlines it); the planning session reads the
    package's "for the pass" as their lifetime, kept across unchanged passes, so a warm rebind and generation build no new CallPlan, as
    with the old list (not measured: no warm test rebinds). Bridge CallPlans carry the context's padded Lengths, not the caller's, and
    no conforming Type: the bridge drops the context's Self, which is harmless, since no source instance gives a body with a closure a
    Self (SPEC 8.6.1) and the bridge's readers instantiate under `TyCtxt.StorageSubst`, which has none; G7 deletes the bridge.
  - Temporary structure, removed at R7: `[Transitional("R7")] TyCtxt.StorageSubst(call)`, a second substitution per call beside
    `SubstOf`, which the CallPlan form `Binding.InstantiateStorageType(type, call)` instantiates under. It goes when that form's readers
    apply `SubstOf` instead: the old generator's (Emission) at G7, the old analyses' (`Analysis/Ownership*`) at R6-check and Binding's
    own (requirement instantiation under `SelectedRequirements`, forwarded calls, formatting implementations, and the effect-bound
    summary unless R6-check deletes it with the old effect checks) with R7's CallPlan pipeline.
  - Evidence: the semantic snapshot (HIR and MIR), the diagnostic snapshot and the fixture IR of the closure emission classes are
    identical to the parent's, with the native Closure, GenericClosure, GenericCapture, NestedGeneric, GenericStorage and GenericDefault
    families; `GenericClosureIdentityTest` adds the two-closure regression.
  - Rejected: a memo per Subst (two closures of one instance would share one CallPlan: wrong code); a memo cleared per pass (each warm
    rebind would rebuild the old generator's CallPlans); a canonical CallPlan per context (A5P's C2: a fact outside R7's shape); a
    comparer modulo Origins (D1's Design B).
- **R5-M37 `Layout` per compilation: memo, bounds and outcomes** (2026-10-18, planning session; plan P7c, full process,
  `draft/Sketches/R5 P7c/P7c-package.md`; package C §3.5 and §3.13, K §4.3 and §4.8, S2-S4 X18; supersedes R5-M19's static
  `Layout.Of`, and R5-M29's rejected per-compilation instance, its records "shared by every Type, compilation and thread" and its
  Limit "neither")
  - `Layout` is one instance per compilation, `Compilation.Layout`. `Of(BoundType)` gives a closed Type of that compilation a
    `Layout.Result(Outcome, Value, Instance)`: Ok with the record; SizeLimit (a size or offset over 2^31 − 1); CountLimit (a fixed
    array of more than 2^31 − 1 elements, zero-sized ones included); DepthLimit (placement height over 64: a row is 0, a struct,
    Tuple, Closure, Function Item, fixed array or enum 1 + its highest base, Field, component, element or Case payload Tuple);
    CLayout with the C-layout instance storing a zero-sized Field; Rejected with the struct, enum or fixed array whose storage
    Binding rejected (an inline storage cycle, a stored Type it did not resolve, an unresolved length). An unresolved base, which
    Binding publishes as no base, is laid out without it.
  - Precedence: CLayout, then DepthLimit, a limit (parts, then own count), Rejected, the Type's own size; within each, the first in
    placement order. No limit hides a CLayout the walk sees within the 64-level window (SPEC 23.1, IMPL 21.3.2); one placed at level
    64 or below, or whose zero-sized Field's layout reaches past the window (a placed one, as the 0-Tuple, makes that level 62),
    stays behind DepthLimit, as in legacy: the one remaining narrowing of X18's "at any depth".
  - The collector (`InstanceCollector`) is the one owner of the outcome mapping and treats Rejected as derived (K §4.3, R4-M10's
    pattern); codegen after acceptance treats any outcome but Ok as a broken contract.
  - `AdtDef.InlineCycle`: Binding's fact that a struct or enum declaration lies on an inline storage cycle, written by
    `ValidateInlineLayouts` for every storage declaration in every pass (R5-M26's pattern); Layout detects no cycle itself.
  - Memo: (Result, height) per placed Type by interned reference, and for a Type deeper than its remaining budget one entry per
    (Type, budget) with its first CLayout in the window (at most 65 entries per Type), so an answer depends on the Type alone and
    placement is linear per program version (P7b's re-placement was exponential over shared parts: `emit` of a 24-level DAG took
    9.3 s against 4.2 s before P7b; with the memo `emit` takes as long as `check`, whose own cost on such DAGs is PLAN G140). Memo
    and records are cleared by `Compilation.InvalidateSourceAnalysis` (R5-M20), on R4-M51's premise; a warm call allocates 0 B, and
    both hold only the current program's Types and representations. One record per representation within a program version; one
    thread at a time; the concurrent table and thread-static scratch are deleted.
  - IMPL 21.1.3's rows stay static in `Layout.Row`, read only by `WindowsLowering.GetValue` until G8 (then private).
  - The old pool takes the instance through `BodyLowering`, maps Size and Count to its size failure, Depth to its depth failure,
    CLayout to its refusal and Rejected to an internal error. Declared change: a Type of placement height over 64 that the old pool
    accepted only because the same body had resolved its inner Types first now reports `GenerationResourceLimit_Kd` (a chain of
    one-component Tuple locals, `InlineLayoutTest`; a trace found no other in the suite, fixtures and milestones). MIR's bound counts
    placed levels only, so a Type of height 64 whose leaf legacy counts (the chain `S0 { v: i32 }` … `S63`) is accepted by MIR and
    refused by legacy: an R5-M6 (d) row at the first MIR observation. (R5-M44: the first observation found none.) The outcome list and
    the precedence are superseded by R5-M52 (Oversized).
  - Rejected: a static Type-keyed memo (stale after an edit, retains every compilation's Types); a static weak table with an edit
    epoch (couples compilations); a memo reached through the Type (structural Types unmemoized); clearing per pass (re-placement on
    each warm rebuild); records kept for the compilation's life (grow across edits; the old pool's interning compares child shapes);
    a path depth (order-dependent); a larger bound or none (legacy disagreement, stack); a cut that keeps walking unmemoized parts
    (exponential above 64 levels); DepthLimit before every other outcome (hid a CLayout the walk sees); a cycle as DepthLimit (a
    Resource record for a Language error); first-failure precedence (a limit hid a Language failure); a Result of independent
    fields (invalid combinations).
  - R5-M29's reason no longer holds: rows stay one static owner and placement needs a per-program owner for its memo.
- **R5-M38 A named Never Place takes the acquisition Binding publishes** (2026-10-18, planning session; plan F12, PLAN G139,
  R4's consent to amending R4-M62's acquisition clause, after BU-4; R4's review choice (a), which keeps R4-M62's placement rule;
  the owner's choice on B2, selecting R4's option 「借用は公開どおり作る（推奨）」: 「Never 用の分岐は、借用などの変換が付かない
  読み出しだけに使います。参照による束縛では、Binding が公開した借用を `ref/Never` 型のまま作ります。こうすると、その束縛が r
  に対する Loan を持つので、SPEC の文言どおり競合を検出できます。あわせて MirValidator に「Never 用の一時変数へ書けるもの」の
  許可一覧（変換なしの Never Place 読み出し・Never 型の Opaque・発散する呼び出しの戻り先）を加え、この系統を閉じます。R5 側で
  実装と再レビューが必要です。」; amended by F17: any acquisition without adaptation) Rule (`MirBuilder.Values.cs`, `MirValidator`): the Never value is a temp of Type Never
  (`IsNeverValue`); every acquisition takes it as it is and `Set` places nothing of it, as R4-M62 has it. A named Place of Type
  Never (a parameter, a binding, a payload) is not the Never value: any acquisition of it without adaptation (the Copy of a
  bare read, since Never is Copy, BU-4, a Move, or the in-place path's ByCase, which a Copy Type takes by Copy) goes into a
  fresh Never temp, which `Acquire` gives, so whatever takes it places nothing; an adapted acquisition (a pattern binding of a
  Never payload, which borrows the subject in place) is
  built as Binding publishes it, a `ref/Never` that keeps its Loan, so a write to the subject while the binding lives is a
  conflict, even in an arm that never runs. `MirValidator`'s `NeverWrite` row whitelists a Never temp's writers: an unadapted
  Copy or Move of a Place of Type Never, an Opaque value, and a diverging Call's destination (R5-M33); any other is a finding.
  So `func absurd(x: Never) => g(x, x)` copies x twice and has no MovedPlace, `g2(x, k())` secures the acquired temp, and since
  a read of a Never Place supplies no normal completion (SPEC 14.10.3, Binding's Diverges; BU-6), its assumed completion
  initializes nothing it is placed into: `let y: i32 = x`, `y = x` and `var y: Never = x` leave y uninitialized, each later read
  of it an `UninitializedPlace`. `IsNever` (any Place of Type Never) stays for the sites that would borrow or view it (a runtime
  Type test's handle, a verification's message, `$tryWrite`'s writer). Per BU-6 (pending, owner decision 2026-10-18, G129) an
  explicit `x@move` on a Never Place is a Move; Binding publishes it today as a Convert (Diverges) whose name it reads by Copy,
  so MIR moves nothing there; MIR builds whatever Binding publishes, so once Binding publishes the Move (after G129), the moved
  read in `g(x@move, x)` is A1's `MovedPlace`, whose row R5 adds then (BU adds the Binding and HIR rows). No library, milestone or
  corpus program changes its MIR. Rejected: every Never-typed Place as the Never value (R4-M62's clause: a Copy Place read twice
  became two Moves); `Set` placing a named Never Place's value (an earlier follow-up's exemption: a written and an unwritten
  Never temp were one spelling, and `let y: i32 = x` initialized y, against SPEC 14.10.3); the owner's rejected options
  「変換せず Move/Copy にする」 (R4 reviewer's (A), the published kind never adapted: a binding in an arm whose payload is Never
  would hold no Loan) and 「F12 を保留する」.
- **R5-M39 `TyCtxt.Erase`** (2026-10-18, planning session; plan K3, package K §4.1 and KD1-KD3, S2-S4 X20 and X26, under R5-M18)
  `TyCtxt.Erase(BoundType)` and `TyCtxt.Erase(Subst)` (`Binding/TyCtxt.Instances.cs`, X26's placement) give the Origin-erased form, the
  one identity rule of instance keys, descriptors, glue and layouts (SPEC 3.3.1, 8.13.3). Every Origin becomes `BoundOrigin.Static`: a
  reference's (`ref`, `uniq`, `objref`, `objuniq`) or a Slice's, written or inferred, any other written one, every Origin argument and a
  substitution's Origins and Inputs; Kind, Semantics, symbols, lengths, arities, Self and the declaring Type are kept, their Types erased;
  a closure context (R5-M36), whose binder instantiates, erases to its erased bindings. `Erase(Subst)` is null when its Target does not
  instantiate (KD1's null rule). Interned and idempotent; it only interns (no storage preparation, X20) and never rewrites a value; two
  reference-keyed memos beside the interning, never cleared like it, since erasure is structural (the null rule reads `Instantiates`,
  discarded at an edit, outside the memos); a warm erasure allocates nothing. No proof runs on an erased value.
  - Verified (`TyCtxtTest`): calls whose substitutions differ in Origins only have one erased substitution, and the Types of their
    instances, the closure's context included, erase alike; idempotence; a Type without Origins erases to itself; every struct and enum
    instance a MIR local of the library and the milestones holds stores the erasure of what that instance stores (the property `Layout.Of`
    will rely on, K10a).
  - Rejected: a separate `TyCtxt.Erasure.cs` (X8, X26); the null rule inside Type erasure for closure contexts (it would tie the
    structural memo to `Instantiates`); a second Origin sentinel (KD2).
- **R5-M40 Admission, the location rule and `Capability`** (2026-10-18, planning session; plan K4a, the K package's first half of K4,
  §4.3, §4.5-4.7, §4.9, KD3, KD9, KD10, KD12, S2-S4 X19, PD30; under R5-M18) The instance collector (R5-M35) composes each site's
  substitution with its item's representative (`TyCtxt.Compose`) and admits generic callees, keyed by definition and `Erase` (R5-M39).
  The first composed substitution is the representative; the item's one view of its body is `MirCases.Views` on the representative, and
  none or several is `InternalInvariant_Kd`. `Admit`, one function: a known key is reused, so Origin-only variants and a recursion under
  one substitution share an item; a new key whose composition fails or whose body's local Types `TyCtxt.Apply` cannot make under the
  representative is the capability table's storage-nesting gap (`Unsupported_Kd`, PD30), so each later Type of the item exists; a depth
  past 128, 16 items of the definition on the admission chain, or 1,024 items of the definition are `GenerationResourceLimit_Kd` (X19;
  every item is a Body item so far); a representative that is open or leaves a local Type of its body open is `InternalInvariant_Kd`
  (K §4.3: representatives are closed). A refused edge is dropped and collection continues. The location rule (KD12): the cause's site,
  moved out of inlined default templates outside the root modules to the outermost omitting call, else the root-module site of the
  item's first admission, which each item keeps as `Site`. Records live in `CheckedSet.Records`, one per code and subject, unpublished
  until K4b; `Work` counts replayed sites and admissions.
  - Enclosing instances (group review B1, B2): `TyCtxt.Compose` takes the declaring Type from the inner substitution only, and a call
    carries the instance of the function enclosing its callee only as that declaring Type, which Binding writes one level into a generic
    function. A call of a function nested in an instantiating function (two levels in, in a block, in a method or a specialization of a
    generic container) whose substitution does not carry that function's instance (`TyCtxt.EnclosingFunction`, a new view in
    `TyCtxt.Instances.cs`) is therefore the capability gap `EnclosingInstance` (`Unsupported_Kd` at the call), never an admitted item:
    one key for every enclosing instance and an open representative would accept such a program at check (SPEC 8.13.3, R5-M34). A
    function nested in a closed function has no enclosing instance to carry and is admitted by its own substitution.
    Removal condition: PLAN G149, a representation of the enclosing-instance chain in a call's substitution (full process, scheduled
    inside R5 before its exit, so the gap's `[Transitional("R5")]` stage stands; R5-M43 states why K5's pure `SubstOfItem` does not carry
    it); the F3 reproducers in `InstanceCollectorTest` then expect two items each.
  - Departure from KD3, owned here: a closed item's representative is stored as null, which every reader treats as the identity,
    instead of `Identity(Decl)`; an accessor's executing function would need its own binder for an identity.
  - A permanent Backend component, `Capability` (`Backend/Capability.cs`): the one table of what the backend does not generate on
    windows-x64 (IMPL 21.3.3, SPEC 23.1.1), the capability check of ARCH's closed vocabularies; its first row is `StorageNesting` (PLAN
    G128) with its code. OCC-X's refusal (K6), the generic static storage key (K7) and the operation rows (K9a) join it.
  - Verified (`InstanceCollectorTest`): instances keyed by erased substitutions, erased exactly when the definition instantiates;
    Origin-only variants and finite recursion reuse one item; a growing key stops at 16 items, depth at 128 and a definition at 1,024
    items, each with the record at its call; `pick<Option^31<ref/i32>>` and `pick<Array^64<i32>>` are instances while `Option^32<ref/i32>`
    and `Array^65<i32>` are the gap at the call; the F3 reproducers: one level into a generic function two items, the other shapes the
    `EnclosingInstance` gap, a generic function nested in a closed one one item, every admitted representative closing its body, and a
    forged open representative `InternalInvariant_Kd`; with a second, non-root module the location rule moves a
    record out of its inlined default and out of its body; the work doubles and quadruples with the instances; a warm run allocates
    nothing; the library (as roots) and the 44 milestones give no record.
  - Rejected: the storage check by `Erase` (erasure never fails); limits per call site (X19 keys them by definition and chain).
- **R5-M41 Collector records under MIR code generation (OD4)** (2026-10-18, planning session; plan K4b, the K package's second half of
  K4, §4.4, §4.9, §5 and §10 D-A and D-B; under R5-M18; meets R5-M4's "under sel, collector records publish from K4" with K4b) Under
  MIR code generation (sel), `Compilation.MirRecords` counts and lists the records of the checked set of the program version (R5-M40)
  with the MIR's construction records and validator findings: analysis Errors in the ControlFlow partition (R5-M4, PD7), so they reject
  at check, count in `Analysis` and block generation. On the default pipeline and under `mir` alone the collector does not run (PD4)
  until G2.
  - D-A: the collector runs in `Compilation.BuildMir`, after the MIR analyses and before acceptance, not as a Mir pass; ARCH's pipeline
    table moves the definition-stage capability check from P7-P9 to it (A3b joins its body scan at K9a).
  - D-B (P5): the bundled library is no root in user compilations. `KimiLibraryShapeTest.TheLibraryCollectsWithoutRecords` collects it
    with every module a root module and asserts no record. Preconditions: no library declaration under a compile-time Condition (SPEC
    19.2) and one target profile (windows-x64); a change to either reopens P5. Library instances user code requests are collected by
    edges and located by R5-M40's rule (KD12).
  - Cost (`Benchmark --analyze-cost`, new; `artifacts/benchmarks/r5-k4b-analyze-cost-*`; Generic shape 1/32/64/128 on a machine shared
    with other builds, so only allocations are exact): a fresh check under sel allocates 0.15, 1.29, 2.31 and 4.61 MB more than under
    `mir` for 16, 450, 898 and 1,794 collected items, linear in the items; a warm Analyze allocates the same under sel as under `mir` (the
    collector's warm run is 0 B); the checked compilation retains 0.10, 0.97, 1.79 and 3.56 MB more under sel than under `mir` (the
    checked set, the `Erase` memos and the instances' interned Types, about 2 KB per item), and its retained heap grows by the same few
    KB over warm Analyzes under every configuration (none from the collector); the timings vary more between repeated sizes than the
    configurations differ and await G2's quiet-machine measurement (R5-M4).
  - Staged sel coverage: until K5-K8 the collector follows Fn calls and calls of closures of closed definitions only, so requirement
    calls, closures of instances, function items, virtual calls and the descriptor, glue and static-initializer intrinsics add no edge
    and no record; sel's absence of records over the library and the milestones covers only the edges collected.
  - Verified: under sel a storage-nesting gap rejects the program at check at its call (`PipelineSwitchTest`); the library and the 44
    milestones analyzed under sel give no collector record (`InstanceCollectorTest`).
- **R5-M42 A jump without a target row is derived from Binding's record** (2026-10-18, planning session; plan F13, PLAN G143, R4's
  ask A-8's jump part (G103-E9b's), handed to R5; after Q11, R5-M31) Rule (`MirBuilder.Transfer`): the builder reads the target
  row the jump binding publishes. A null row means Binding rejected the jump (R5-M31's HIR contract: row 9 or 10 at the jump, or
  `NonExhaustiveCompileTimeCase_Kd` at a `#switch` in its function part that selected no arm, which need not enclose it), so the
  jump is derived from that record (SPEC 23.1.2): its operand is built with no expected Type, keeping its own records and
  accesses, and the jump is an Opaque value naming what the operand gave, with no record, ending its block for the checks only as
  a jump does. A non-null row is built as before; one that no construct around the jump opened is a broken HIR contract (every
  target construct opens one), recorded as `InternalInvariant_Kd` at the jump, the jump an Opaque value. So an unlabeled `yield`
  into a discarded selection, a jump with no target in scope and one behind a defer or a default lose MIR's second record, and
  `loop => yield helper(a)` builds its call. Rejected: a located `Unsupported_Kd` at the jump (a second record of an invalid
  program, the operand unbuilt); an Opaque value of the jump's whole syntax (the operand's records lost and its accesses unbuilt);
  a target lookup in MIR (the jump binding owns targets since Q11).
- **R5-M43 Closure, item and erasure edges; the pure item substitution** (2026-10-18, planning session; plan K5, the K package's §4.3
  and §4.5 rows 5 and 11-13; under R5-M18) `TyCtxt.SubstOfItem` gives an ordinary function item's substitution from its Item Type alone:
  its function's own Type arguments (`Binding.CopyItemArguments`), the declaring Type its last Component holds and its length arguments,
  without Origins, the substitution `FunctionItemContext` built, without its cache or storage preparation; a requirement Item's stays its
  requirement call's until K6b (superseded by R5-M67, which moved it from K6). The collector's templates gain statement sites, in
  location order with the calls: an `Aggregate(Closure)`
  is the Closure's body (row 11), a `Use` of a `Const(FnItem)` (the builder's one form of that operand) the Item's (row 12), a
  `Cast(Erasure)` a `ClosureErasure` or `ItemAdapter` item keyed by the erased Closure or Item Type (row 13); a Value callee's Closure or
  Item in the item's instance (`Apply`) is its body (row 5). A Closure's body is keyed by its definition and the erased substitution of
  the instance that closed it (`SubstOfClosure`, exact since G142, R5-M36), an Item's by its function's implementation under the Item's
  substitution; the Item of a function whose calls dispatch (`TyCtxt.Dispatches`, new, over `CallPlan.KindOf`, the one rule that also
  gives a call's `Kind`: a requirement, K6; a virtual slot, K8) and a Function Type value have no target. A type-driven item holds its
  representative Type (`MonoItem.RepresentativeType`, closed, an open one being `InternalInvariant_Kd`) and key Type (`MonoKey.Type`), no
  body (`BodyOrdinal` -1), takes its target at admission (the body its Closure or Item runs, at the default site,
  runtime; K7 adds the environment's glue) and past depth 128 is `GenerationResourceLimit_Kd`. `TyCtxt.EnclosingFunction` walks through
  anonymous functions, and a substitution carries the enclosing instance as its binder (a Closure's context) or its declaring Type.
  - The `EnclosingInstance` gap stays (planning session): a call's substitution carries the enclosing instance only as `Declaring =
    SelfType(owner)` when the callee's direct scope owner has slots (`Binding.CallDeclaringType`), and `SubstOfItem` is never read for a
    Fn call (probe at 2c0371da: the nested calls of the F3 shapes are Fn callees, with `Declaring` null two levels in, in a block, in a
    method or a specialization of a generic struct), so a pure item substitution cannot carry it, and two slot-bearing levels fit in no one
    `Declaring`. The fix, a representation of the enclosing-instance chain in a call's substitution, changes shared structure under the full
    process: PLAN G149, R5-M40's removal condition, scheduled inside R5 before its exit, so the gap stays `[Transitional("R5")]`.
  - Rejected: widening `CallDeclaringType` to the nearest slot-bearing enclosing declaration (two levels stay unrepresentable, and legacy
    CallPlans change); the collector testing `IsRequirement` or `IsVirtual` (syntax in the Backend; `Dispatches` publishes it); targets per
    operand of every statement (a site would hold several).
  - Verified (`InstanceCollectorTest`): Closures of two instances, a Closure in a Closure and a Closure never called are bodies of their
    instances, a function item constant and an Item called through a Callable parameter their function's, an erasure of each its adapter
    item with the body as its target, a call of the Function Type value none, a requirement's and a virtual slot's Item none, an open
    adapter Type `InternalInvariant_Kd`, no records otherwise; every target at a call or a rows 11-13 statement;
    the F3 expectations unchanged; the library (as roots) and the 44 milestones under sel give no record; `TyCtxtTest`'s one length entry
    per slot holds for the pure item substitution, and `MirEffectsTest` passes on it. Staged sel coverage: requirement calls and Items,
    virtual calls and Items, and the descriptor, glue and static-initializer intrinsics still collect nothing until K6-K8.
- **R5-M44 Layout obligations of the checked set** (2026-10-18, planning session; plan K10a, the K package's §4.8 and §4.3, S2-S4 X18;
  under R5-M18 and R5-M37) The collector is the one owner of `Layout.Of`'s outcomes (`Compilation.Layout`, passed at its construction).
  Each item's by-value uses (its body's locals but drop flags, its aggregates, and the Type argument of an intrinsic whose row's new
  `LaysOutType` column says so: `RawAllocate`, `Stride`, `DestroyValues`) in its instance, and each type-driven item's Type, take
  Layout's verdict, memoized per program version and equal for Types that differ in Origins only (Layout reads no Origin, R5-M37), so one
  verdict per erased Type: a size, count or depth limit is the erased Type's one `GenerationResourceLimit_Kd` at its first
  use, located by R5-M40's rule (IMPL 21.3.2: the declaration, call or token; a parameter's at its function, since MIR records no
  parameter syntax), so a library instance's limit is reported where user code requests it, not at the project file as the old
  generator does; Rejected storage derives from the Binding error that rejected it (no record); CLayout is K10b's `InvalidCLayout_Kd`;
  a `ClosureErasure` or `ItemAdapter` item is admitted only for an Ok verdict. The uses are template sites beside the target sites, in
  location order; locals are laid out per item before its sites. PLAN G146 (2^63 - 1) stays out of scope, and so do a collection's
  element Types (X34): an Array's or Dictionary's own layout is its handle words, so the element verdict comes with its glue item (K7).
  - The height-64 (d) row (R5-M37, P7c's Q6; the final review's N6) does not arise: a struct chain and a one-component Tuple chain, read
    down to the scalar leaf, are accepted by both MIR and the old generator at 64 placed levels and refused by both at 65, since the old
    pool takes `Layout`'s depth verdict before counting its own (`InstanceCollectorTest.TheDepthLimitCountsPlacedLevels`); no R5-M6 row is
    recorded.
  - Rejected: a record at every use of a failing Type (one Type would report at each local, temporary and erasure holding it); the
    intrinsic Type arguments by a collector-side switch (the row table owns per-intrinsic facts); `Layout.Of(Erase(type))` (the erasure
    memo of every by-value Type cost 1.5 MB allocated and 1.0 MB retained at Generic 128, against 0.47 and 0.25 MB).
  - Cost (`Benchmark --analyze-cost`, Generic 1/32/64/128, against K5): under sel a fresh check allocates 8, 94, 226 and 472 KB more and
    the checked compilation retains 6, 52, 121 and 248 KB more (Layout's memo and records, which generation reuses); legacy and `mir`
    unchanged; a warm Analyze allocates the same.
  - Verified (`InstanceCollectorTest`): size and count limits at an unused function's parameter, a generic instance's `Raw.allocate`
    Type argument at the call in the generic body, a Closure local whose environment is too large (no adapter item for its erasure), no
    record for a struct Binding rejected or for `Option<Never>`; the library (as roots) and the 44 milestones under sel give no record.
- **R5-M45 Legacy storage kernel rows** (2026-10-18, planning session; plan P2a, PD2, S2-S4 X4, package C §3.4; refines R5-M16)
  `StorageKernelRow(Kind, Write, Needs)` is one row per `StorageKernelKind`, the storage-boundary text a Body-disposition storage
  operation writes inline in its caller's body (SPEC 22.2): DictionaryLayout (StorageDictionaryLayout), EntryAddress (StorageKeyAt,
  StorageValueAt), BorrowFixed (StorageBorrowFixedShared and Exclusive), BorrowDictionary (StorageBorrowDictionary and Exclusive) and
  OwnDictionary (StorageOwnDictionary), read by `LegacyRuntimeHelpers.StorageRow(kind)`, which fails closed. `Write` takes one
  generator-neutral, stack-only input, `StorageKernelInput`: the spellings of the address the kernel reads and of its result (the
  record it writes, or the address EntryAddress defines), its number (the entry stride, the array length or the entry offset), its
  result record's Field offsets in the order its kind lists them, and the number naming its temporaries (role + number, as
  `LlvmScalarText`'s). Every row's `Needs` is None and no row declares a callee, since the text is loads, stores and address
  arithmetic, so the old lowering requests nothing for them; the column keeps the collector's read of a legacy row's needs uniform
  across the families. The old writer decodes its Sequence instruction into the input (`LlvmModuleWriter.WriteStorageKernel`), so
  `WriteDictionaryIteration`, a per-instruction serializer of about 112 statements, is deleted. Promoted with the kernels: the library
  record Field addressing (offset 0 is the record's own address, any other offset `{name}{id}_{offset}`; `LegacyRuntimeHelpers.DefineField`
  and `WriteField`), which the old writer's remaining record reads call with the record's spelling (`SpellSlot`, `SpellStorageAddress`,
  through which `WriteSlot` and `WriteStorageAddress` now write), and the entry-address text the Place helper shares. The rows are keyed
  by kernel kind; X39's map from a Body-disposition `CompilerFunctionKind` to its row joins them later. FixedStorage moves here rather
  than in P2b: it was part of the serializer and is a Body-disposition kind; P2b keeps the intrinsic kernels (RawSlice, capacity), whose
  rows are `IntrinsicRows`'. Rejected: plain kernel methods with per-kind parameters (unreadable by kind, one decoding arm per kind); a
  row per `CompilerFunctionKind` (X39's map); an owning flag (the owning remainder is its own row, and both share the borrowed
  remainder's text); a second copy of the Field addressing rule in the Backend.
- **R5-M46 Legacy rows declare the Array runtime only for growth and copies** (2026-10-18, planning session; PLAN G134, S2-S4
  X4; refines R5-M16 and R5-M27) `HelperNeeds.ArrayRuntime`, the Array capacity routines with the memory declarations, is declared
  by a row whose text grows a buffer or copies or moves elements: the Array rows Append, Insert, Pop, Remove, Place, Swap and
  OwnFixedStorage and the Dictionary row Place. The Array rows Clear and Drop declare only `ElementGlue`, OwnStorage nothing, the
  Dictionary row CheckKey only `DictionaryRuntime` and Drop only `ElementGlue`: they destroy elements, release through the main
  runtime (`__kimi_array_free`), or load and store the handle's words. The old lowering's Array operation dispatch
  (`LowerArrayOperation`, `CompilerArrayEntry`) sets the Array runtime flag by hand only for the direct capacity calls,
  ArrayReserve and ArrayShrinkToFit, which have no row until X39; every helper kind joins its row's needs (R5-M27), so a call of
  `clear` no longer brings the capacity routines either. A module whose only collection helpers are these holds no grow, reserve
  or shrink text (about 120 lines). A literal keeps its own request (its direct `ArrayGrow` call; a Dictionary construction
  requests the Array runtime with the Dictionary runtime until C8e), so CheckKey's narrowing changes no output today. Open: the
  rows that copy but never grow (Pop, Remove, Place, Swap, OwnFixedStorage and the Dictionary row Place) still bring the
  capacity routines, because one member names them with the memory declarations; splitting it adds a vocabulary entry. Rejected:
  that split here (beyond G134's rows); keeping the hand-set flag for every Array operation (G133's cause, a flag set by its
  requester instead of its row).
- **R5-M49 A virtual receiver borrows the whole payload** (2026-10-18, planning session; plan F14, PLAN G147 (4) from R4's 1b-5
  ledger; SPEC 6.4.5) Rule (`MirBuilder.Dispatched`, call assembly): a dispatched virtual call (`CallPlan` Virtual, not direct)
  borrows its receiver as the view of the receiver payload's own Type, in the slot view's Semantics and Origin, and upcasts that
  view to the slot's (`Cast` Upcast, SPEC 13.6.1), so the Loan lies on the whole payload and an update of a derived Field
  conflicts with the call's result. A receiver whose payload is the slot's own Type, a direct base call and a nonvirtual call keep
  the borrow `Lent` forms (a base view's subobject). Rejected: widening the base subobject's Loan in A5 (an analysis rule for a
  builder fact); borrowing the payload under the slot's view Type (the validator's Ref Type agreement).
- **R5-M50 A transfer delivers its result after the Scope Exit** (2026-10-18, planning session; plan F15, PLAN G147 (5) from R4's
  1b-5 ledger; SPEC 16.2.2; amended by F17: the result temp lies in the target's scope) Rule (`MirBuilder.Transfer`): a valued
  transfer that leaves a scope with a cleanup secures its result
  and places it into its target's result temp (no storage of its own) before the Scope Exit, and the target's delivery block,
  after the drop tree, moves it into the target's destination: the target's Destination and Break become that temp and that block
  at its first such transfer (`Target.Delivers`), so every later transfer to it takes them as it shares its drop tree. A transfer
  leaving no cleanup places its result directly, as before. The result temp lies in the target's scope, the MIR scope the target
  opened in (`Target.Scope`; a defer's is the scope that registered it, a function's return target's scope 0), not the jump's. So
  a block value borrowing a departing local conflicts with that local's storage end even when nothing reads the value later; a
  return's `_0` gains the delivery too (unobservable there, since the return reads `_0`). Rejected: delivering per transfer after
  a drop tree of its own (one drop tree per target); a result temp for every construct (MIR on every path, not only where a scope
  is left); the delivery block, temp and scope as new Target parameters (the record already has seven; its Destination and Break
  take the first two, an init property the scope).
- **R5-M56 The difference ledger compares the requirement** (2026-10-18, planning session; plan Q17, Q8 package §3.6, under R5-M23
  and R4-M67; supersedes R4-M17's "a MIR problem takes its code's analysis" and R4-M3a's `-` requirement on the MIR side) A MIR
  problem's ledger row carries its own requirement (R4-M67); a MIR construction record or validator finding names none (`-`) until
  H3. `MirDifferenceLedger.Compared` gains each row's MIR requirement, the one its code's recording rule names: the old requirement,
  or for the decisions the old path reports under `Binding.*` (VirtualEffectBound, IncompatibleImplementation, the effect limit's
  Unsupported, OriginRelation) their Ownership requirement (DIAGNOSTICS rule 1, renamed publicly at H5); none for the expansion
  limit. These MIR pairs are PLAN10 §7's consistency set, each an image of `OwnershipIssue.Code` for its failure or ControlFlow's
  require fallthrough (tested with R4's program of one problem per recording rule); a MIR problem takes its pair's analysis. Each MIR
  problem is one ledger row: `MirReport.Add` owns the merge per code and subject (R4-M67), so the ledger merges nothing; R4's 1c-8,
  which changes that key under G125, adapts the ledger (agreed with R4). MirReport's disagreement record (`Ownership.Internal`) and
  any pair outside the set are contract-break rows (B), never an exception. A matched pair whose requirements differ is a
  `requirement` difference, its requirement column `old>mir`; a MIR side that names none agrees.
  - The side (2026-10-19, the owner's decision, R4's shape; review round 2), verbatim:
    「Q17 も E10 も (A) で進めてください。Q17 は side 列を加える形（R4 承認の形）、E10 は sel で完全なテスト名を使う形。」
    Before Q17 a MIR row's `-` requirement was the only mark of its side, so once the MIR names its requirement, old-side rows of
    kind `*` met MIR rows and old-only and MIR-only `acceptance` rows shared one signature. Each difference carries its side,
    computed once by `MirDifferenceLedger.Differences` as the signature's third field: `old` for missing, `mir` for new, `both` for
    location and requirement, and for acceptance the side that holds the event's records; no consumer derives it again. The table
    gains a `side` column after `kind` (R4-M46), matched by equality or `*` (`mir` does not match `both`).
  - The table stays R4's. With R4's approval its rows that read `-` on the MIR side name the requirement each recording rule names
    and take side `mir`, which reproduces their pre-Q17 matches (Q21's `a5-kept-closure` too); the A4 requirement difference is
    (d) (`a4-requirement`); the dead `a1-result` row goes; `a4-x2` (d) and `a4-preserves` (b) stay two rows, since the side
    separates them exactly (X2's new and acceptance rows on mir, F6's missing and acceptance rows on old). Every other row takes
    `*` (R4's migration condition (3)), except the nine U rows, which take `old` with R4's approval as the table owner, amending
    condition (3) for them ("this reproduces exactly and keeps them from absorbing a future MIR `Unsupported_Kd`"): each matches
    only old missing and acceptance rows, and with `*` `u-never-loop` would take a MIR-only Unsupported_Kd it never matched (at
    `let nest = Nest<ref/i32>.init(value: n@ref)`). The other rows of an old cause (`b3`, `a4-callable-preserves`, `a5-old-*`)
    stay `*`. The classification equals the one before Q17 apart from the `requirement` rows.
  - Rejected: a code-to-requirement lookup on the MIR side (R5-M23 D7); throwing on a pair outside the set (the ledger would lose the
    run); comparing requirements in `location` pairs (their subject is the difference); keeping `-` on the MIR side (the comparison
    the package asks for); the ledger merging problems over bodies itself (a second copy of `MirReport.Add`'s rule, review round 1);
    one `a4-preserves` row for X2's and F6's rows (review round 2: X2's new rows are (d)); the owner's (B), a whitelist of the
    signatures each old-side row may meet (a second list beside the table, one more entry per MIR requirement), and (C), holding
    Q17 with `-` on the MIR side (the comparison waits); splitting `acceptance` into two kinds (the side would hold for one kind
    only); `mir` also matching `both` (an implicit second value); `*` for the U rows (`u-never-loop` would classify a MIR
    rejection (a), and the others would meet the next MIR `Unsupported_Kd` of their form).
- **R5-M53 The capability table's operation rows and the definition-stage check (A3b)** (2026-10-18, planning session; plan K9a, the K
  package's §4.7, §9 row K9a and D-A; implements R4-M8; under R5-M18) `Capability.Of(rvalue, operand)` gives the row of an operation the
  backend does not generate (IMPL 21.3.3's 128-bit subset), or none: `WideDivision`, a `BinaryOp` `Div` or `Rem` of i128 or u128 (a
  compound form builds as the same operation), and `WideFloatConversion`, a `Cast(Numeric)` between i128 or u128 and f32 or f64; both
  are `Unsupported_Kd`. A direct literal conversion is a constant (Binding fits the literal), never a Cast, so the row needs no exception.
  A3b runs in the collector's template walk, once per definition and program version: every operation of a root-module body whose Types
  are closed, in every block (unused and check-only ones included), is checked against the table and reported once per location (R5-M40's
  rule; in a root-module body the site itself or the omitting call of an inlined library default); an operation whose Types depend on slots
  is the instance stage's (K9b). Binding still refuses the closed forms at the node until H7, and MIR builds a refused node as an Opaque
  value, which has no row, so the two never both report.
  - Rejected: a Mir pass (D-A: the table is the Backend's and the driver R4's); checking library bodies per compilation (P5: the library
    test collects them as roots); an exception for literal conversions (no such Cast exists).
  - Verified (`InstanceCollectorTest`): the closed division, compound remainder and both conversions Binding refuses give no collector
    record; literal conversions give none; each table row appended as an operation in an unreachable block is `Unsupported_Kd` at its own
    location and its 64-bit twin none; an open division in a generic body is no definition-stage record; the library (as roots) and the
    44 milestones under sel give no record. `InstanceCollector.Template` also brings the template walk within the nesting limit.
- **R5-M54 The instance-stage capability check** (2026-10-18, planning session; plan K9b, the K package's §4.7 "Instance stage" and §9
  row K9b; under R5-M18 and R5-M53) An operation whose Types depend on slots (its first operand's or its result's Type is open) is a
  template site; replaying an item applies the item's representative to those Types (`TyCtxt.Apply`, the storage-nesting gap where it
  cannot) and checks `Capability.Of` on the instance's Types, so a division or remainder of an i128 or u128 instance is `Unsupported_Kd`,
  for every instance of the checked set and in every block (IMPL 21.3.3), once per location. The location is R5-M40's rule, the requesting
  call versus the body as K13a expects: an operation of a generic body in a root module is reported at itself, as the old generator
  reports it (`GenericGenerationDiagnosticTest`'s `value / 2` and `value % 2`, also when a forwarding call reaches the instance), and one
  of a library body at the user's request. Closed operations stay A3b's (R5-M53), so no operation is checked at both stages.
  - Rejected: the record at the requesting call (K13a takes the old generator's location, and a user generic body is where the user can
    act); a site per operation of every kind (only BinaryOp and Cast can have a row).
  - Verified (`InstanceCollectorTest`): division and remainder of i128 and u128 instances at their generic-body locations, once for two
    instances and for a forwarded call, also after a return; independent operations at their own locations; the i64 instance none; the
    library (as roots) and the 44 milestones under sel give no record.
- **R5-M55 C-layout instances in the checked set** (2026-10-18, planning session; plan K10b, the K package's §4.8 and §9 row K10b, S2-S4
  X18; under R5-M18, R5-M37 and R5-M44) The collector maps `Layout.Of`'s CLayout outcome, a C-layout instance (`AdtDef.IsCLayout`)
  storing a zero-sized Field at any depth in the 64-level window, to `InvalidCLayout_Kd` (Language; SPEC 6.7.1, 8.15.3) at each by-value
  use (one record per code and subject: SPEC 8.15.3 places a failed layout obligation at the use), through one table beside R5-M44's,
  `RecordOf(outcome)` (Ok and Rejected none, the limits Resource, CLayout Language); R5-M44's one record per erased Type stays for the
  Resource code. Inferred instances are rejected at check under MIR code generation, which the old generator refuses only at emission. The
  record joins `Compilation.MirRecords` with the others, so its requirement stays ControlFlow until H3 (K10c aligns with it); `Generates`
  now speaks of error records.
  - Declared overlap, removed at G2b: Binding's eager report of a written instance (`Binding.Attributes.cs`) stays, so under MIR code
    generation a written by-value use is reported at the written Type by Binding and at the use by the collector; an inferred one only
    at the use (`InstanceCollectorTest.BindingsEagerReportOfAWrittenInstanceStaysBesideTheUse`).
  - Derived in the owning component (review B1): a C-layout declaration Binding rejects for a direct zero-sized Field
    (`ValidateCLayoutFields`, which reports at the Field and the struct and keeps `IsCLayout`, R5-M26) is published as
    `AdtDef.CLayoutRejected`, written in every pass like `InlineCycle` (R5-M37); `Layout` lays such a struct out as Rejected storage, so
    its by-value uses add no record and the collector never repeats Binding's declaration check (DIAGNOSTICS rule 1). Binding's
    declaration check is lasting (X26).
  - Waiting: a collection's element (X34) is laid out by its glue item, so an empty `Array<P<()>>` local's record comes with K7.
  - Rejected: one Language record per Type (SPEC 8.15.3 places it at the use; R5-M44's rejected record per use concerns the Resource code);
    a second outcome table beside R5-M44's.
- **R5-M52 A layout over 2^63 - 1 bytes: `Layout.Outcome.Oversized` and `OversizedLayout_Kd`** (2026-10-18, planning session; owner,
  2026-10-18, on PLAN G146: 「新しい言語エラーのコードを追加して下さい」; plan unit K10c after K10b, full process, package
  `draft/Sketches/R5 K10c/K10c-package.md` at `2158a768`; supersedes R5-M37's outcome list and precedence, R5-M18's X18 and K §4.8 mapping
  ("OK, Limit or CLayout; any other failure `InternalInvariant_Kd`"), K §11's "No new diagnostic code" and P7c's Q3) IMPL 21.1.1 makes a
  size, stride or Field offset over 2^63 - 1 a Language error at the use that needs the layout (SPEC 8.15.3, 23.2.2), never only a
  Resource record (23.1.1); `Layout`'s 2^31 - 1 SizeLimit preempted it.
  - `Layout` measures each placement exactly in 128 bits and keeps each memo entry's size as `ulong`, 2^63 standing for every larger size:
    exact (`Entry.Exact`, alignment nonzero) when every part was laid out within the 64-level window and resolved by Binding, else a lower
    bound (such parts add no bytes; every arrangement is monotone). Sizes stay private. New outcome Oversized: the size exceeds 2^63 - 1 or a
    part is Oversized (a zero-length array of one included). `Result` publishes `Oversized`, derived from the entry's size where the entry
    is built, true for Oversized and for a CLayout whose size is over, so a use reports both Language errors (SPEC 23.1.2). One decision
    function of the fold (`Fold.Decide`) owns the precedence and the own size; `Exceeded` and the three early exits go (commit 1, behavior
    unchanged).
  - Precedence: CLayout, Oversized, DepthLimit, a limit (parts, then own count), Rejected, the own size; within each, the first in placement
    order. An oversize that needs bytes past the window stays behind DepthLimit, reported only as a Resource record (R5-M37's residue
    extended; STATUS). Declared: a C-layout instance storing an exactly zero-sized, count-limited Field (`[2147483648 of ()]`) is CLayout,
    no longer CountLimit (KC7); K10b landed first, so it reports `InvalidCLayout_Kd` at once.
  - `OversizedLayout_Kd` (Error, Language, no facts, a Message and a Label): the collector maps `Oversized` to it at each by-value use
    (R5-M44's uses, R5-M40's location rule, one record per code and subject; R5-M44's per-Type rule stays for the Resource code),
    requirement `ControlFlow` like every collector record and K10b's `InvalidCLayout_Kd`, until H3 lets the collector's recording rules name
    their requirements (a new requirement name is then a separate owner question). Acceptance change at check: under MIR code generation
    from this unit, on every pipeline from G2.
  - The old pool never reaches Oversized (accepted parts, its own count check) and treats it as a broken contract like Rejected; until G2
    the default pipeline reports such a program at build as `GenerationResourceLimit_Kd` (STATUS). No R5-M6 row: both refuse.
  - Correction to the package (L3, H there): `(P<()>, [4611686018427387903 of u16])` is 2^63 + 4 bytes in Kimigayo grouping, its tail
    padding included, so it is an oversized CLayout; the test's near-bound row, a CLayout that is not oversized, uses
    `[9223372036854775800 of u8]` (2^63 - 4 bytes).
  - Rejected: a flag on SizeLimit (a different rank); an exact-size view outside `Layout`; 64-bit record sizes; exact sizes only (a
    DepthLimit or Rejected part would hide a visible oversize); one record for CLayout and Oversized (omits an independent failure);
    per-operation saturation; a published size or an Instance for Oversized (no consumer); one Language record per Type (SPEC 8.15.3
    places it at the use); K10c before K10b (check under sel would accept the moved C-layout form); a requirement for this code before H3;
    a legacy fix (R5-M6).
- **R5-M58 `Abi`** (2026-10-19, C lane; plan P8, C design §2.6, package C §3.5, under R5-M18 form 1; promoted under R5-M14) A
  permanent Backend component that owns the physical signature of a closed function instance for every generator: `Abi.Of(layout,
  convention, flags, result, parameters)` returns a `FunctionAbi` without a name, which its definition names (`FunctionAbi.Named`:
  the old pool `__kimi_f<n>`, the old imports their external symbol, ModuleCodegen its items), and `Abi.Matches` whether an existing
  signature is that one, without allocating. Inputs: the result Type and the parameter Types in slot order, laid out by `Layout.Of`
  (any outcome but Ok is a broken contract), the convention (`Internal`, IMPL 21.3.1; `C`, SPEC 22.6) and `Abi.Flags`, the facts the
  Types do not give (an erased or concrete closure environment, a constructor's or destructor's result slot, the caller location);
  `NoReturn` follows a Never result. The internal rules are `FunctionAbiPool.Build`'s and `Get`'s, moved: the order [environment]
  [ret] a0..an [context] [location, location_length], a `ref/string` SharedReference, a slot Type OwnedSlot, zero-sized parameters
  omitted, the result slot and result Type (`Abi.HasResultSlot`, `Abi.ResultType`, which the old pool's statics also answer with).
  Under C every parameter is a Value of its argument Type and nothing is omitted or a slot; on SPEC 22.6's table this is the old
  `LlvmEmitter.CreateImportAbi`, so it, `ImportType`, the import `Matches` and the unreachable failure "A foreign import needs an
  unsupported parameter or result representation." are deleted. `Abi` has no gate: the old callers keep `FunctionAbiPool.Supports`
  and `GetValue` until R6-gen (every Type they admit is laid out Ok) and pass `AggregateLayoutPool.Layout`. Deleted from the old
  pool: `Build`, the static `Matches`, `Shape`, `ParameterShape` and `Signature`; `Get` and the new `Import` (C) reuse the previous
  signature by `Matches`, keeping no Type or syntax. Not ported: `PreparedSlot` (`DefaultGenerationPlan` keeps its own ABI). Staged:
  C design §2.6's memo keyed by (convention, flags, interned signature Type) comes with ModuleCodegen (C0b1; R5-M65 replaces it with
  ModuleCodegen's per-item reuse by `Abi.Matches`); the old callers reuse by physical shape across compilations. Rejected: a name or ordinal input to `Of` (naming is the definition's); a gate in `Abi`
  (ARCH "Capability gates"); a separate C-convention component (the order, records and reuse are one rule with two conventions);
  keeping the per-ordinal `Signature` record, whose logical shapes also compare zero-sized parameters that have no slot.
- **R5-M61 `TyCtxt.Witness`, a requirement call's target under a closed substitution** (2026-10-19, planning session; plan F5b,
  package F §4.1, §4.5, §5 and §6, S2-S4 X1, X16, X28; under R5-M18 and R5-M22) A permanent view of the dispatch facts
  (`Binding/TyCtxt.Dispatch.cs`): `TyCtxt.Witness(requirement, contract, subst)`, given a requirement, the Contract reference its
  call recorded (`MirCallee.Contract`) and the call's closed substitution, returns `RequirementTarget(Kind, Target, Subst, Access)`,
  whose kind enum is local, not an ARCH vocabulary. Body: a conformance witness (inherited ones included) under its declaring
  instance with the call's Types and lengths and its Origins in the call's terms, an accessor-call witness's executing accessor under
  its implementing Type's instance, or a fixed-array or integer Position member under its own slots. Builtin: the compiler function
  under the call's own substitution, whose bound reference instantiates the requirement's signature (the CallPlan form drops it, its
  CallPlan already carrying that signature). Storage: a standard property witness's implementing Field under the call's
  substitution, `Access` its `PropertyWitnessKind` (AccessorCall for every other kind). Refused: OCC-X. Limit: the conforming Self's
  storage does not complete (`PrepareTypeStorage`, the depth bound of R4-M51's Limit), for a Self no `Apply` instantiated, such as the
  Self-bearing `Synthesized` factory's (F6). None: no answer, a broken contract for a closed accepted Self that its reader records as
  `InternalInvariant_Kd`; an abstract Self has None, since the materializer's SPEC 8.15 forwarding keeps such a call. The view calls the
  core under the library root's premises (`EffectPremises(null)`) with the Contract reference `Binding.InstanceContract` matches,
  the matching half R5-M22 deferred, now one function for both forms: from the recorded reference, Self and the bound reference in
  the instance (the materializer instantiates it in its caller's CallPlan; a substitution holds it as its declaring Type, F fact 5)
  to the conformance of Self's Type that names it. Both forms share Binding's per-kind rules: `WitnessOrigin` (a witness Origin in
  the call's terms), `RentFixedArrayArguments` and `IntegerPositionSlot`; the core gives a Property witness its implementing Type's
  instance (`Declaring`). Memo per (requirement, contract, subst), kept until an edit; `TyCtxt.ResetViews`, the one reset of every
  view's memos (retentions, instance classes, requirement targets), replaces `Binding.Invalidate`'s two calls (R4-M51, R5-M21), so
  `Binding.cs` shrinks and later views add a line there instead (R4's step-2 unit 2a0-2); a warm call allocates 0 B. Twin
  (`TyCtxtTest.WitnessIsTheForwardedRequirementCall`): each requirement call in a generic function's body under each closed call of
  that function, over the library and the milestones (37 calls: Body 34, Builtin 2, Storage 1; 3 bound references) and a direct
  program reaching every kind but Refused (16), has the target `InstantiateForwardedCall` materializes: a Body the forwarded call's
  function and `SubstOf`; a Builtin its compiler function, and Storage the Field its bridge reads with the bridge's operation, under the
  call's own substitution, which binds the same Self, Types, lengths and Origins; `Subst.Declaring` is the instantiated bound
  reference wherever the reference is bound (fact 5 holds, so the reference needs no Subst slot of its own); the views leave Binding's
  records and the HIR version unchanged. Rejected: the match re-expressed over `Subst.Declaring` in TyCtxt (a second copy); a Builtin
  without the bound reference (the substitution needs it for the signature); a twin over collector sites (no mapping from a MIR site
  back to its CallPlan pair exists); reading a None of the core as Limit (an Unknown proof is no depth limit).
- **R5-M64 The generation entry** (2026-10-19, planning session; plan C0a, package C §3.8 and D2 §2.11, S2-S4 X7 and X17, PD12's gate
  step 1; under R5-M18 and R5-M35) A permanent Core component, the generation entry (`Core/Compilation.Generation.cs`, contract entry), owns
  the one way the latest analysis becomes module text: `Compilation.Generate(TextWriter)` passes the gate, runs the compilation's code
  generator and writes the complete module, or writes nothing and records why in the Emission partition; it returns no failure text.
  `EmissionArtifacts.Publish` generates only through it, and `Project.PublishArtifacts`, the one publication path of `kimi build` and
  `kimi test`, renders the Emission result, so `kimi test` reports located records where it reported failure text. Outside the gate until
  G7 deletes `LlvmEmitter` (and `Compilation.Emission`): its direct readers in tests (G5 deletes them) and four `src/Benchmark` stage
  measurements run the old generator without the gate, under sel too.
  - Gate, step 1: no MIR records of the version (`MirRecords`: construction records, validator findings and, under MIR code generation, the
    checked set's records). They are analysis Errors that check publishes, so a refusal adds no record; `Publish` skipped this gate before.
    C4a adds step 2 (A2's per-body completeness fact, R5-M18) and K11 the Generated mark in this file only.
  - Records now: under MIR code generation R5-M35's refusal, moved here (one `Unsupported_Kd` per output root until C0b1); under the old
    generator its own record (`Emission.ReportFailure`: an instance's located records, `GenerationResourceLimit_Kd`, `GenerationFailed_Kd`
    until G9b, R5-M18). A publication failure keeps `GenerationFailed_Kd` until G9b's `ArtifactPublicationFailed_Kd`.
  - Publication order: `Publish` checks its inputs (adopted backend, paths, native libraries and supplies, archive hash, `LlvmBin`) before
    it generates into the temporary IR file. A program that fails both now reports the configuration failure, and a failed generation can
    leave a created output directory without files.
  - Temporary structures: the out-string adapters `WriteIr` and `Validate` (`[Transitional("R6-gen")]`, G9b): the same gate with its text,
    then the old generator's text without its record, for their test callers. `verify.ps1 -Codegen legacy|mir` sets `KIMI_CODEGEN` for the
    run and records `codegen` in `summary.json`; `-Codegen mir` without `-Pipeline mir` is rejected before building (PD4); both go with
    the selector at G4.
  - Rejected: an entry returning the prepared old module (Core would publish `EmissionModule`, which MIR code generation replaces);
    buffering the module before the publication checks (an allocation the size of the module); a writer opened on first write (a wrapper
    kept only for the old order); reporting publication failures in `Generate` (publication is `EmissionArtifacts`', G9b's record).
- **R5-M66 The pending ledger and its report** (2026-10-19, planning session; plan E10, PD10, R5-M8's M1; case identity by the owner,
  2026-10-19: 「Q17 も E10 も (A) で進めてください。Q17 は side 列を加える形（R4 承認の形）、E10 は sel で完全なテスト名を使う形。」; a
  temporary structure removed with the selector at G4) The pending ledger is the union of
  `tests/xUnitTest/TestData/codegen-pending/<group>.tsv`, one file per review group named by the group (`C-frame.tsv`), with the rows
  `test<TAB><type>.<method><TAB><full name>` and `fixture<TAB><stem>.ll<TAB>1`. A test row names one case by its full xUnit display
  name, its `type.method` with any argument list: under `-Codegen mir` only, Verify's test steps pass `-printMaxStringLength 0
  -printMaxEnumerableLength 0`, so xUnit cuts no argument ("···") and the names a run executes are distinct (a sel suite on
  2026-10-19: 21,281 names, distinct by ordinal comparison, as some differ only in format characters; longest 2,649 characters). Rows
  are keyed by that name, not by `type.method` as the plan's E10 row says. A malformed row (a name other than its `type.method`,
  optionally followed by an argument list), a file other than `.tsv` or a row listed twice in any files is refused, and no directory
  is an empty ledger. `scripts/codegen-pending.ps1`, dot-sourced by Verify, owns the comparison of a sel run with it: a case fails
  when it ran (`Get-KimiExecutedCases`, verification-selection's one executed rule) and did not pass, and a name the run executed
  twice fails the report and so the run; a fixture fails when a native run of it did not pass at O0 and O2. A failing case or
  fixture is `pending` with a row and `added` without one; a row that ran and passed is `passesNow`; a row that did not run is
  `noLongerExecuted` when the run covers it (a Session every test; a Unit run its selected methods and any method its fresh assembly
  lacks; once any native step wrote results, every fixture matching any `-Fixtures` pattern), else `outOfScope`. Totals of ledger,
  failing and each state's cases and fixtures, per group too, go into `codegen-pending.json`; Verify records them as `summary.json`
  `codegenPending` under `-Codegen mir` only, in a step after the tests, native steps and milestones that the failing set does not
  fail (a malformed ledger, a repeated name or missing evidence does; E11 adds the gate and `-UpdatePending`). Limits: a renamed
  case, including a theory case whose data changed, shows as `noLongerExecuted` plus `added`, which the gate treats conservatively; a
  test that neither ran nor has a row (a partial or crashed run) is invisible to the report, so E11 compares the executed methods
  with the selected and discovered ones. Rejected: a failing count per `type.method` (a deleted failing theory case passed now, and a
  fixed case beside a new failure stayed pending), also with executed counts per method (keeps that masking); rows keyed by
  `type.method` and the cut display name with a count for colliding names (a removed failing case beside passing cases of its name
  passed now); the print options in default runs (only sel reads names, and the default path stays unchanged); one ledger file (lanes serialize, PD10); a reason column (the group file groups, the
  commit explains); a standalone script reading `summary.json` (written after the steps, so Verify could neither record the report as
  a step nor its totals).
- **R5-M60 Drop glue, destructor and static edges of the checked set** (2026-10-19, planning session; plan K7, the K package's §4.5 rows
  7, 9, 10 and "DropGlue", §4.7 and §4.8, S2-S4 X3, X23, X27, X33 (a) and X34; under R5-M18, R5-M35 and R5-M44; DEC Q6 form 1) Every Drop
  (A2's verdicts do not narrow the checked set) and every `DestroyValues` call is the `DropGlue` item of its Type in the item's instance,
  keyed by the erased Type, when `MirTypes.MayNeedDrop` holds. A glue item's targets, at its default site, are the glues of
  `DropGlue.Parts`, a new Backend component (`Backend/DropGlue.cs`; C4a writes glue from the same enumeration, X27): a closed Type's
  direct parts in destruction order (SPEC 16.3.2, 16.4): a declared destructor alone, its Body in the struct's instance
  (`TyCtxt.Synthesized`, R4-M6b); else Fields last to first, then the base; Tuple and fixed-array elements by decreasing index; each Case's
  payloads last to first; a Closure environment's captures in reverse initialization order (X23); an Array's or Dictionary's element
  Types (its legacy Drop row's ElementGlue need, X4); an `obj`, `rc` or `arc` payload when `TyCtxt.Sealed` holds (else the descriptor's
  finalizer runs the actual Type's glue, K8). A `ClosureErasure` also targets its environment's glue. A destructor without a body is one
  Binding rejected, so its edge derives from that error.
  - X34: a glue item lays out its collection element Types, also those that need no drop, and their verdicts join its admission (a
    type-driven item needs an Ok verdict), so an element that fails refuses the glue: a Language record at each request (an empty
    `Array<P<()>>` local's declaration), a Resource record once per erased Type.
  - Row 7: a `staticEnsure` targets the StaticInit body `MirProgram.StaticInit` names (F10); none is `InternalInvariant_Kd` at the access.
    A static of a generic container's group, whose storage the MIR keys by its symbol alone, is the capability row `GenericStaticKey`
    (`Unsupported_Kd` at each access; X33 (a)). Search (K7's condition): over the library, the 44 milestones, the mutation, syntax and pair
    corpora (456 cases) and the 63 spec-case variants, only Milestone 42 uses the form, P42's completion program, which the old analysis
    rejects at the same accesses; no accepted program does. An immutable scalar `let` there, which the old path accepts and builds, is
    refused under MIR code generation from this unit and at `check` from G2: the (e) row the PD27 list approves at R5's exit. Superseded
    by R5-M82 (2026-10-19): such a static is the `StaticStorage` row once at its declaration, and its accesses carry no record.
  - The plan row's "through a `MirTypes.Destruction` visitor" is `MayNeedDrop` (presence) with `DropGlue.Parts` (the parts), as X3 and
    X27 decided.
  - Rejected: laying out the elements after admission (an admitted glue with a failed element); a target for every part whatever
    `MayNeedDrop` says; Parts derived from `MirTypes.Destruction` (a deep forward walk, and Mir does not read the Backend); a
    collector-side element table (X4).
- **R5-M62 `VTableDef` and `TyCtxt.Applies`, a struct family's dispatch shape** (2026-10-19, planning session; plan F4, package F
  §4.1–§4.3, §5 and §6, S2-S4 X5; under R5-M18 form 1 and R5-M61) A permanent declaration table (`Binding/VTableDef.cs`, R2b-D3's
  deferred view) kept on the struct's `AdtDef` (`VTable`), and a view beside `TyCtxt.Witness` (`TyCtxt.Dispatch.cs`).
  `PrepareVirtualOverrides` fills the table at its end, once slot selection is complete, for every struct in every pass, into the same
  arrays: a struct's table is its base's table, then its own virtuals in member order (SPEC 6.2.1: fragments merge in logical
  declaration order; a conditional-conformance Block's members in its place), with no path or span sort; `Implementations[slot]` is
  the struct's own override, else its base's entry, else the original; `BaseCount` counts the base's slots. `VTableDef.Of(struct)` is
  null for a struct with no slot and for one whose declaration, base table, virtuals or overrides had failed by then, a dependent of a
  failed part (`CompleteDependent`) included (R4-M18);
  `VTableDef.Slot(original)` searches the original owner's own range, the same index in every table of the family. A failure later in
  the pass (an override condition, an effect bound, the objuniq receiver's `Unsupported_Kd`) keeps the table and leaves Binding's result
  incomplete; such an entry names a definition without a MIR body (R4-M18), which a reader meets as any unresolved callee. Consumer
  rule (K8, C6c): slot i of a closed T runs `Implementations[i]` in the first layer on T's `AdtDef.Base` chain that declares it, under
  `TyCtxt.Synthesized(implementation, layer, [])`; where `TyCtxt.Applies` of the slot's original at the layer that declares the original
  is false, the entry is null and the body is checked only (R4-M28); a Virtual callee loads `Slot(original)` (X5).
  `TyCtxt.Applies(original, declaring)` gives `Binding.VirtualApplicability`'s proof under the public premises: Proven true, Refuted
  false, else null, which for a closed declaring Type is a broken contract its reader records as `InternalInvariant_Kd`. One answer
  per (original, declaring), discarded at an edit by `ResetDispatch`, which `TyCtxt.ResetViews` already calls; a warm rebind refills
  the same arrays and a warm `Applies` allocates 0 B. `VirtualGenerationPlan` keeps its own sort and slots until G7 (D5). Rejected:
  a path or span sort (member order is the logical order, F fact 6); filling after the pass's later checks (the package places the
  fill after slot selection, and nothing reads a table during a pass); a table per closed instance or a layer per slot (one table per
  declaration with the consumer's Base walk serves every instance); applicability in the table (a closed instance's proof, the view's);
  a per-pass side set of filled structs (the table carries its pass's storage version, as `AdtDef` and `InstanceStorage` do).
- **R5-M65 The frame of MIR code generation: `ModuleCodegen` and `FunctionCodegen`** (2026-10-19, C lane; plan C0b1, package C §3.2,
  §3.3 and §3.9, D2 §2.2, §2.3 and §2.12, PD11; under R5-M18 form 1 and R5-M64) Two permanent Backend components (`Backend/Codegen/`,
  contract entries), one `ModuleCodegen` per `Compilation`. Under MIR code generation `Compilation.Generate` passes gate step 1, then
  needs the check's acceptance `Compilation.Accepts` (SPEC 23.2.2; `Project` reads it instead of its own copy; the old generator keeps
  its `CheckInputs`), and runs `ModuleCodegen.Write`: every item of the checked set in admission order (K11's Generated mark narrows
  them), a body item `__kimi_f<item>` under `Abi.Of`'s internal signature from local 0 and the argument locals in slot order
  (`CallerLocation` from `KimiLibraryCatalog.RequiresCallerLocation`; the old constructor and destructor result-slot flag is the old
  generator's ABI choice, which local 0's Type replaces), reused per item while `Abi.Matches` holds; an Application's `__kimi_start`
  (the startup item, then `__kimi_exit(0)`), a Library without an entry; the frame through `LlvmModuleText` in the old order; the module
  all or nothing. `FunctionCodegen` writes a body item: one aligned alloca per local of nonzero size (bool `i8`), Value arguments stored,
  pointer-passed arguments and the result slot in place; exactly the blocks `MirReach.Walk(body, 0, checks: false)` reaches; the
  no-op statements, a `Use` into an unprojected local from an unprojected Copy or Move or a Bool, Integer or Unit constant (zero-sized
  writes nothing, a slot Type's copy is C2b's), Goto, SwitchInt, Return and Unreachable. PD11's coverage rows are the two components'
  dispatch arms (MIR vocabularies; body kinds Closure (C5a), DefaultTemplate (C3a) and StaticInit (C7b: a constant initializer folds
  into its slot and is never a function, package C §3.3); `MonoKind`s DropGlue (C4a), ClosureErasure and ItemAdapter (C5b), Builtin
  (C8f, C9b) and PropertyWitness (C3c) of R5-M67, K8's ObjectDescriptor and VTable (C6a, C6c); a test
  build's TestEntry (C10a)): an arm not written yet records `Unsupported_Kd` once per subject, located by the collector's one rule
  (`InstanceCollector.Subject`, K 4.9, now internal), a broken input contract `InternalInvariant_Kd`; generation runs over every item and
  then discards the text. A record is a code without facts, `Unsupported_Kd` (location-only, DIAGNOSTICS §2 rule 3) or
  `InternalInvariant_Kd` (a broken contract at its site), so `Generate` publishes it in the Emission partition keyed by its location,
  through the Diagnostics owner's `DiagnosticCollection.ReportLocation`,
  whose key's subject is the location: the records of two nodes at one location, such as a `for` statement and the siblings its
  expansion synthesizes with its span (`$for.iterator`, `$for.some`, `$for.none`, which the order of node keys cannot tell apart), are one
  problem by the owner's merge of equal keys. An accepted program without its MIR or checked set, or a module neither written nor
  explained by a record (an Application's startup without a generated item), is `InternalInvariant_Kd` at the project. Types come from
  `InstanceCollector.TypeOf`, the collector's own instance rule (`Apply` under the representative), which its edges now read too.
  R5-M35's and R5-M64's sel refusal is deleted.
  - Moved here from C0b2: module assembly, the Application entry and the Library form (a frame fixture needs a linkable module).
    Staged to their first readers: the intrinsic rows' writer column (C3a or C8a, PLAN G151), the zero-sized address symbol (C2b),
    view selection (C2a), Float constants (C1b, which promotes the old writer's hex spelling). R5-M58's staged nameless memo is replaced
    by the per-item reuse (a nameless memo would still allocate each name). The entry's call is written by `ModuleCodegen` until C3a,
    whose call writer it then goes through (one ABI and attribute writer, package C §3.2).
  - P8's review: ARCH's Backend row lists the root Type predicates `SlotTypes` and `ReferenceTypes`; `Abi` throws for flags or a
    parameter without an argument value under C; `VirtualGenerationPlan` names its entry with `FunctionAbi.Named`.
  - Rejected: a coverage table or predicate beside the dispatch (a second copy); streaming functions to the caller (a partial module on
    a gap); a destructor fact in Binding for the old result-slot flag (the MIR signature needs none); records at library sites (the
    collector's rule is the one location rule); a consumer-side table of published spans (records dropped after the fact, beside the
    owner's own merge); a Diagnostics tie-break or a per-record condition for synthesized siblings (it would show identical records, and the
    order fault guards every reporter); moving synthesized sites in the location rule (no published fact names an expansion's source
    node).
- **R5-M69 `ObjectRuntime`** (2026-10-19, P lane; plan P3b, PD2, package C §3.1 and §3.4, PLAN10 §5.8; promoted under R5-M14) A
  permanent Backend component that owns the object runtime text every generator writes (IMPL 21.1.3, SHARED_OBJECT_RUNTIME), moved
  from the old writer's `Objects` and `SharedObjects` partials after P3a's split: `WriteRuntime`, the payload-independent runtime once
  per module that uses objects (the dynamic Type test through a descriptor's base map, the descriptor's free entry, finalization, and
  the rc and arc clone and drop with their release transitions); `WriteObject`, an object's private constants (Runtime Type key,
  metadata, descriptor, virtual table, base map), payload destructor and `__kimi_make_object<N>` factory from its row
  `ObjectCreation`, which moved from `ObjectGenerationPlan` with it: its number, pooled key, payload `ValueLowering`, Copy, factory
  `FunctionAbi` and Type token in the constructor, and the descriptor's optional entries (the payload destructor's symbol, the base
  tokens, the virtual slots) init-only, so the constructor is within ARCH's parameter limit (as R5-M15's `FunctionAbi`);
  `WriteSealedDrop`, a Sealed handle's direct release, from the handle's `ObjectCountingStep` (`BoundType.HandleMode`'s; it stays in
  the held `BindingModel.cs`) and the symbol of the payload's drop routine as a span, empty for none, so the old aggregate destructor
  names `__kimi_destroy_string` or formats `__kimi_drop_aggregate<N>` on the stack (`PayloadDrop`) and codegen passes its glue's
  symbol. The parts' order is the component's; the old `WriteObjects` only decides from `EmissionModule` whether a module uses
  objects. `LlvmModuleWriter.Name` joined `LlvmModuleText` (numbered names). The move is behavior-preserving; the Runtime Type key
  constant, which nothing reads (PD17), moved unchanged, and its deletion with `ObjectCreation.TypeKey` and the pool parameter stays
  a separate unit that changes the IR. C6a builds the codegen's rows (`B/Codegen/ObjectDescriptors.cs`, the factory ABI with them).
  Rejected: `ObjectHandleMode` as input (payload authority is not the runtime's); the old aggregate ordinal as input (the old
  writer's naming in a permanent component); a `string` symbol (a warm write would allocate a payload aggregate's drop name, and
  `SealedObjectFinalizationTest` holds warm emission at 0 B); the caller writing the payload call between a release member and a
  free member (the release order would leave the component); a copy of `Name` (a second copy); the row left in Emission (a Backend
  dependency on the old Emission, which R6-gen deletes); the per-part members internal (each generator would repeat their order).
- **R5-M68 `TyCtxt.Comparison`, a comparison's composition by Type, and the legacy comparison twin** (2026-10-19, planning session;
  plan F6, package F §4.1, §4.6, §4.7, §5 and §6, S2-S4 F 2, F 5 and X25; under R5-M18 form 1 and R5-M61) A permanent view of the
  dispatch facts (`TyCtxt.Dispatch.cs`): `TyCtxt.Comparison(contract, self, operators)`, given Equatable or Comparable, a Type and the
  mode (source operators, which compare floats by IEEE, or the Contract's mapping, whose float `equals` is NaN-reflexive; SPEC 13.4,
  13.4.1, 12.4.3), returns a `ComparisonNode(Type, Kind, Target, Subst, Parts)` tree, whose kind enum is local, not an ARCH vocabulary:
  Composite (a Tuple or a safe `ref` layer) over its parts; Builtin; Witness, a closed leaf: the Body `TyCtxt.Witness` gives for the
  Contract's own requirement on the leaf, under its declaring instance (`TyCtxt.Synthesized(witness, declaring, [])`, its Origins the
  analyses'); Requirement, a leaf with an abstract part: that requirement on the leaf in every instance, under the Self-bearing factory
  `TyCtxt.Synthesized(requirement, self)` (amended by R5-M70: `Synthesized(requirement, self, reference)`), beside `Synthesized` in
  `TyCtxt.cs`, which K's rows 2 and 6 also use; None: no answer, a
  broken contract for a closed accepted Self that its reader records as `InternalInvariant_Kd`; null for a Limit (Witness's: the leaf's
  storage does not complete). One rule decides the kinds, `Binding.ComparisonKindOf(self, contract, operators)`
  (`Binding.ComparisonPlans.cs`), which the legacy plan and `CompilerRequirementTarget` (a comparison's built-in target: Composite or
  Builtin under the Contract's mapping) read too, so no copy of the composition remains; the Contract's own requirement is
  `BoundContract.OwnRequirement`, shared with `PrepareBuiltinTargets`. One node per (contract, self, operators), kept until an edit
  (`ResetDispatch`, under `TyCtxt.ResetViews`); a warm call allocates 0 B. Readers: F7 (operators), C8f (the Contract's mapping: the
  BuiltinEquals and BuiltinCompare shims and the Dictionary key-equality row), K6's Builtin items; A4 only through A-12.
  - The legacy comparison twin (package §4.7), a temporary structure marked `[Transitional]`: the private `ComparisonPlan` overload,
    `BoundComparison`, `comparisonPlans` and `RequirementImplementation` with `requirementCalls` stay for A4's `Witnesses` until A-12,
    else R7; `ComparisonPlan(CallPlan)` and `DictionaryComparison` go at H11 after their readers (H5, G7), and `RequirementImplementation`'s
    CallPlan overload with `TryResolveFormattingCallback` at H11. While it lives A4 and codegen resolve comparison leaves with two
    resolvers (X25). Twin (`ComparisonCompositionTest.TheViewHasTheLegacyPlansLeaves`): each comparison call of the library, the
    milestones and four direct programs (with witnesses of conditional conformances, one of a Type with an Origin slot and one nested in
    itself, package §10) has, for a closed Self, the legacy plan's leaves (Types, kinds, witness and declaring instance) and, for a Self
    with an abstract part, the requirement on each leaf with one; the views leave Binding's records and the HIR version unchanged. Every
    closed comparison has a plan: an inherited witness, which the plan refuses, implements no comparison requirement, whose `other` is
    `ref/Self` (`MissingContractImplementation_Kd`), so package §4.6's inherited case has no instance.
  - Departure: group G:library-2, reviewed per unit, instead of G:compare (planning session). Not shared: `TyCtxt.Published`'s "its one
    requirement" (`Requirements[0]`, `TyCtxt.Effects.cs`, R4's), equal to `OwnRequirement` for every catalog row's Contract.
  - Rejected: a view mapping the legacy plan's tree (null at every abstract leaf, F fact 1, so F7 could not expand a generic tuple
    comparison); a mode on the legacy overload choosing the leaf resolver (a mode steering one function; its CallPlan leaves and
    per-version retention are the legacy readers'); the core called directly for a Witness leaf (a second copy of Witness's Limit and
    premise scope); Witness's Body Subst as the leaf's (its Origins are the requirement's own atoms, foreign to a synthesized call);
    `ComparisonProof` on the same rule (an acceptance proof under the caller's premises, which proves built-in Types through the
    constraint solver: another contract).
- **R5-M67 Requirement edges of the checked set** (2026-10-19, planning session; plan K6, the K package's §4.3 and §4.5 row 3 with its
  witness results, package F §4.5, S2-S4 X14, X16, X28; under R5-M18, R5-M40 and R5-M61; DEC Q6 form 1; `MonoKind.Builtin`,
  `MonoKind.PropertyWitness` and the capability rows are approved with the K package, §11, `ExclusiveInheritedWitness` being KD10's and
  §4.7's OCC-X row) A Requirement call of an item's
  body is, under the call's substitution composed with the item's representative, the target `TyCtxt.Witness(requirement,
  MirCallee.Contract, composed)` gives: Body, the item of the implementation its substitution selects (`TyCtxt.Implementation`,
  `MirProgram.Callee`; none without a body), admitted as a Fn callee's; Builtin, a shim item keyed by the erased Self and the compiler
  function (new `MonoKind.Builtin`, `MonoKey.Variant`; superseded by R5-M70: the compiler function in `MonoKey.Symbol`); Storage, a
  shim item keyed by the Field, the erased Self and the
  `PropertyWitnessKind` (new `MonoKind.PropertyWitness`, `MonoKey.Symbol`, X14), admitted only when the requirement's signature in
  the instance (`TyCtxt.Signature`) is the key's up to Origins (a `ref/Self` or `uniq/Self` receiver, a result `F`, `ref/F` or Unit,
  a set input `F`, `F` the Field's Type in Self by `AdtDef.FindField`), else `InternalInvariant_Kd`, and a set's item targets the
  Field Type's glue; Refused, the capability row `ExclusiveInheritedWitness` (OCC-X, `Unsupported_Kd` at the call); Limit, the
  storage-nesting row; None, a broken contract (`InternalInvariant_Kd` at the call). Both shims are type-driven items: the Self's
  layout verdict joins their admission (R5-M44). A composed substitution whose Self does not instantiate is null (`TyCtxt.Compose`,
  PLAN G157 (4)), the storage-nesting gap at the call as for the other parts. Locations: R5-M40's rule.
  - Staged to unit K6b (after F6; before C3b, C8f and C9b, which read these targets; K6b landed them, R5-M70):
    requirement Items (rows 5 and 12), whose substitution needs a witness form of `TyCtxt.SubstOfItem` (this supersedes R5-M43's "until
    K6": they now wait for K6b); row 6's writer witness and row 2's formatting witnesses (`TyCtxt.Published`, R5-M30) with its Shim
    disposition's Builtin item, which need F6's Self-bearing `Synthesized` factory (package F §4.5's input); a Builtin item's leaves,
    from F6's `TyCtxt.Comparison` (BuiltinFormat's too, R5-M30). Until K6b these sites carry no target and no record; C3b, C8f and C9b
    depend on K6b, which keeps that safe (under sel `build` is still refused by C0b1's frame). Refused has no reproducer: Binding
    rejects every inherited exclusive witness at its conformance (`Unsupported_Kd` at `Self is C`, ObjectCompatibility Unknown), so the
    row is reached once OCC-X proves one.
  - Rejected: Self's layout skipped for the shims (a shim reads Self's storage); the signature agreement left to C3c (the F package
    places the check at K6, where the key is made); a requirement edge resolved from the forwarded CallPlan (the collector reads no
    plans).
- **R5-M63 A failed node keeps the children Binding resolved** (2026-10-19, planning session; plan Q21, PLAN G121, package Q8 §3.8
  and Q8-7 under R5-M23 (DEC Q6 form 1), design `r5/w1-q21-design.md` accepted on review with the two amendments below, ask A-14
  with R4's reviewer's sign-off; supersedes, for the children below, R4-M61's "which the builder replaces whole" and its "so its
  children are not published as evaluated and a closure inside it is not built") SPEC 23.1.2: a node's failure hides no independent
  error in the children Binding resolved; the Error node's own value stays a derived Opaque with no record. R4-M61's reason concerned
  only the Error node's own value, whose facts are missing; each kept child is published with its own facts (`Binding.HasFacts`) and
  checked like any node (`HirTables.Broken`), so the reason does not reach it.
  (1) A closure definition: `BoundClosure.Of` is the definition of the closure's latest binding once it bound the inputs, the result
  and the environment (`Signature` set; `Binding.BindClosure` clears it first), whatever its creation's outcome. An anonymous function
  Binding bound in the pass (Resolved or failed), at an evaluated or a replaced position, with its definition keeps its parts as a
  Closure (`HirPublisher.Parts`): its own `Consume` (an expression body's result) and its body's positions. This reaches beyond Q8-7's
  "a closure whose creation failed" to a closure inside a parent Binding rejected (`let g = <closure>` without a Type, a failed call's
  argument), because Q8 §3.8's required meaning is that the children Binding resolved stay published, and AGENTS accepts no narrow
  subset of a form one rule covers; a closure that is a failed Place's kept operand is evaluated there (its creation built in a temp)
  and its body built, where the design had predicted it replaced. Only the creation follows its position: built, a failed creation's
  Opaque site naming the captures' sources, or part of a replaced parent's Opaque. The MIR builder builds each definition whose value
  node the HIR publishes (`MirBuilder.Published`, equal to the Body and Closure Ops before this entry: 0 differences over 740 table
  definitions of the goldens and the library and 863 of the milestones and the library). Amendment (review): the definition must also
  be one the publisher reached (an Op written), since a synthesized executing function shares its accessor's body.
  (2) A failed compound Place (a Field, an Index, a Deref, `*p` or `p@follow`, the latter recognized by Binding's target rule
  `TypeSemanticsKoto.Names`, or a group, such as a list item the parser recovered (a parameter default, a call's last argument), that
  Binding rejected or did not resolve, not a broken contract) keeps each operand Binding resolved at its position, written in place
  `(None, None)`, which under an Error node marks a kept operand (`HirPublisher.Keeps`). Amendment (review): an operand is kept only
  when its own Op is published with its facts, so a Type or group name (Syntax) and a name both of whose paths resolve (an Error)
  stay replaced. The builder evaluates an Error node's kept operands in syntax order as their Places (a value in a temp) before its
  Opaque value, never acquiring them; the site names their roots and the roots of the rest of the node's syntax, which root
  collection skips (it walks the Error node's own children; no parent is read), so a moved operand is not used again at the site (A1
  checks each root at the site's subject). Any other child stays replaced.
  Finding A: a kept body keeps what Binding refuted. A closure rejected because its inferred result borrows the call's own storage
  (`CallLocalOrigin`, SPEC 15.8.2) builds the refuted borrow, so A5 adds `ComparisonLoanConflict_Kd` where the borrowed parameter or
  local ends: on the failed closure (`FunctionKoto`) or on a `let` inside its body (`FieldKoto`), not on the failed node. All seven
  such rows of the Q21 ledger are derived: the only Loan on the ending storage is the one issued at the refuted borrow. They are
  classified (b) open (`classification.tsv` row a5-kept-closure; not (d), which at signature level would absorb an independent
  conflict of the same form) until R4's A5-F7 unit 2c-6 judges closure results on the MIR, one report per refuted relation, and
  R6-check's H11 deletes `CallLocalOrigin`; R4's condition: H11 lands before the MIR becomes the default pipeline, or 2c-6 reports
  each refuted relation once while `CallLocalOrigin` exists.
  Residuals (PLAN G121): an assignment or update whose target Binding rejected (`MirBuilder.Values.cs:143`) is one Opaque value, so
  its target's kept operands are published but never evaluated; the operands of other failed forms (calls, statements, conversions
  other than `@follow`) stay replaced; a failed Place's operand that Binding did not resolve stays replaced (a nested failed Place is
  not published Error until R4's 2x-E2 decides unresolved compound Places). Rejected: in-place writes for a closure (Q8-7: an
  expression body's result and a by-case Copy of a capture); a `Kept` member of `HirAcquire` or `HirPosition` (a vocabulary entry;
  `HirAcquire.None` already means used in place, and under an Error only this rule writes it); building closure bodies from Binding
  state in the builder (it reads the HIR only); a walk clause naming an Error closure (one form). Lasting. R4's 2g-2a adds each
  Opaque root's access kind at `MirBuilder.Root`, which the kept operands and the root collection use, and the completion-undecided
  flag at `Site`; it also routes the two producers that still write the roots directly (the derived jump, `MirBuilder.Control.cs`,
  and an omitted default's site, `MirBuilder.Defaults.cs`), files AGENTS' three-units rule makes due for restructuring before their
  next change.
  - R5-M84 extends the kept-operand rule: an Opaque site names a part already built by its Place's root, never by its syntax.
  - R4-M88 decides its deferred unresolved compound Places and amends its kept operands: an operation Binding left
    unresolved keeps every operand its form evaluates but an unresolved construct without a Type, also labeled.
- **R5-M70 Requirement Item, formatting, writer and comparison-leaf edges of the checked set** (2026-10-19, K lane; plan K6b, the K
  package's §4.3 Builtin row and §4.5 rows 2, 5, 6, 12 and 13, package F §4.5-§4.6, R5-M30; under R5-M18, R5-M61, R5-M67 and R5-M68;
  DEC Q6 form 1; the planning session accepted the Item reference rule and granted K6b `Binding.RequirementInstances.cs`) What a
  compiler function, a requirement's function item or a shim runs on a user Type is `TyCtxt.Witness` of the requirement there, acted on
  by R5-M67's kind table, one function (`InstanceCollector.Requirement`, given the requirement and its `RequirementTarget`, whose Builtin
  and Storage substitution is the call's) for rows 2, 3, 5, 6, 12 and 13. The call on a Self is F6's factory, now
  `TyCtxt.Synthesized(requirement, self, reference)`: a bound Contract reference is its declaring Type (F fact 5, as `SubstOf` holds a
  call's), a Contract's own Type binds nothing.
  - Rows 5, 12, 13: a requirement Item records its Contract reference as a Type (its Components: Self, reference), so
    `Binding.InstanceContract`, the one matching half, reads a recorded Contract declaration with a reference of it in the instance as
    that reference; `TyCtxt.Witness(item)` (new) is Witness of the requirement's call on the Item's Self through that declaration, null
    for another function's Item. A requirement Item's constant, its call and its ItemAdapter target that witness's item. `SubstOfItem` is
    unchanged (Binding's Item context: the witness's call for a closed Self, which A4's `MirEffects` enters), and the witness's Body
    substitution equals it; the materializer passes no reference for an unbound recorded Contract, so its matching is unchanged.
  - Row 2: a Fn callee without a MIR body whose published row runs a requirement (`TyCtxt.Published`: TextToString, TextTryFormat and
    WriterWrite run Utf8Format's `format` on the Type below parameter On, under the composed substitution) targets that witness, a
    built-in formattable Type's being the BuiltinFormat shim, a `Builtin` item. A Fn callee of a Shim compiler function does not occur: a
    built-in target's declaration is its requirement (`CallPlan.KindOf`), so MIR builds a Requirement callee (row 3).
  - Row 6: an `ErasedWriter` call targets its Type argument's BufferWriter `reserve` witness (SPEC 22.10.1;
    `TyCtxt.OwnRequirement(KimiDeclarationId)`, new, a library Contract's own requirement), none for a `reserve` without a MIR body
    (FixedBuffer's, HeapBuffer's: their legacy rows, C8*).
  - Builtin leaves: a BuiltinEquals or BuiltinCompare item's targets, at its default site, are the Witness leaves of
    `TyCtxt.Comparison(its Contract, Self, operators: false)` depth first, each the leaf witness's Body under the leaf's substitution;
    Composite recurses and Builtin has none; a Requirement or None leaf is `InternalInvariant_Kd` and no tree (Limit) the storage-nesting
    gap, at the item's site; BuiltinFormat and BuiltinArithmetic, over scalars, call no witness.
  - Departure (for the owner's departures list at R5's group review): the K package's §4.3 Builtin row and R5-M67 key a Builtin item by
    the erased Self and its `CompilerFunctionKind` (`MonoKey.Variant`). Two designs compared: (A) that key with a kind-to-Contract table
    in the collector, a second copy of `PrepareBuiltinTargets`' rule, leaving every arithmetic requirement on a Self in one item for C8f;
    (B) the shim's compiler function in `MonoKey.Symbol`, the existing field, which supplies F §4.6's input (the comparison's Contract is
    the scope owner of the shim's compiler function) and gives one arithmetic item per operation Contract. The independent review's
    comparison found B more general and not hold-driven, and the planning session adopted B on 2026-10-19. The kind is the function's
    `CompilerFunction`, so a Builtin key has no `Variant` (one source); Equals, Compare and Format have one function per kind, so their
    items do not change.
  - Rejected: a call-form `SubstOfItem` (A4 reads a closed requirement Item's as the witness it runs, and it drops the Item context's
    input Origins); the template's `ItemContract` for rows 5 and 12 (null for a closed Item, and an ItemAdapter has no template); a bound
    symbol from Binding's `BoundContractReference` (private, creates and rebuilds bound symbols); the comparison's Contract mapped from
    the `CompilerFunctionKind` in the collector (a second copy of `PrepareBuiltinTargets`' table); a column naming BufferWriter on the
    intrinsic rows (one Witness row, whose value already means the writer's witness); a published row for `Text.writer` (A4 would read
    creating the adapter as running `reserve`).
  - Verified (`InstanceCollectorTest`, each new row failing before): `T.equals` Items in instances over a struct (its `equals`) and `i32`
    (the BuiltinEquals item) where made and called; a `T.tag` Item through `T is Tag<bool>` on a Type conforming to `Tag<i32>` and
    `Tag<bool>`, made, erased (its ItemAdapter) and called, each the `tag(x: bool)` body, whose substitution is `SubstOfItem`'s; a virtual
    slot's Item none; the format witnesses of generic `toString`, `tryFormat` and `write` calls and of `format` itself, a user Type's body
    and `i32`'s BuiltinFormat item without targets; an erased writer over a user sink its `reserve` body and over a fixed buffer none;
    tuple comparisons' Builtin items targeting their leaves' `equals` and `compare` bodies, a nested tuple twice, and a generic instance
    sharing its erased Self's item; i32's addition and subtraction two Builtin items, one per Contract; the library (as roots) and the
    44 milestones under sel give no record; the witness twins
    (`TyCtxtTest`, `ComparisonCompositionTest`) unchanged.
- **R5-M71 A staticEnsure's operand is its static's slot** (2026-10-19, planning session; F10's follow-up, R4's step-2 plan AR-8
  split by the planning session the same day) Rule (`MirValidator`, `MirViolation.StaticSlot`): the builder names the static a
  `staticEnsure` initializes by one operand, an Integer constant holding the static's index in the calling body's `Statics`
  (`MirBuilder.Places.Static`, re-indexed when a default template is folded), the form `MirProgram.StaticInit` decodes and the
  collector reads (R5-M60); any other operand (another constant kind, an index outside `Statics`, a Place) is a validator finding,
  `InternalInvariant_Kd` at the access, so consumers read the slot unchecked. A constant id out of range stays the Const finding;
  whether the program has the static's initializer stays `StaticInit`'s null, a consumer's broken contract (F10), since the
  validator sees one body. No library, corpus or mir-sweep body breaks it. AR-8's other two rules wait for a design with R4: a
  write, Move or exclusive borrow past a shared Deref (G147 (2)) and one Ref per argument temp (G136) fail on valid programs today
  (G147 (3)'s `&uniq (*_1).0` past `ref/closure`; a conditional argument's two Refs in one argument temp, so
  `replace(if c => a@uniq else => b@uniq, with: 3)` gives A5 `lent = b` on both paths) and meet forms other components own (A1's
  Move Paths, Binding-rejected Moves, default templates' prepared `ref` inputs). Rejected: the `Arity` row (a count rule); a Type
  requirement on the constant (`StaticInit` reads only its value).
- **R5-M72 An input that reserves has one write where a Ref writes it** (2026-10-19, R4 and R5 planning sessions; G136's `lent`
  validation, R4's step-2 ask AR-8, option (a); redone after its first review's B1) Rule (`MirValidator.Inputs.cs`,
  `MirViolation.Lent`): A5 reads the Place a Ref lends into a local flow-insensitively (`MirBorrows.lent`) where a Call takes the local
  as an input, at an in-place target and its destroying reservations, so a Temp a Call takes as a whole as an input (an argument, a
  Value callee's operand) that reserves and that a Ref or an Adapt writes has no other write; a finding is `InternalInvariant_Kd` at the
  Temp. "Reserves" is one predicate, `MirCases.Reserves`: an exclusive reference or a pair layer of the body's binders (SPEC 15.6.7, A3
  E13 (4)), which `MirBuilder.Operand` decides an input's `reserves` by and the rule checks the input temp's Type by. The builder owns
  the shape: as such an input, each body of an `if`, a `match` or `try` and a `do` gives its value through a temp of its own, moved
  into the input temp where its container completes (`MirBuilder.Result`), as an exit's secured value already is; before, the bodies'
  Refs wrote the input temp itself, so `replace(if c => a@uniq else => b@uniq, with: 3)` gave `lent = b` on both paths. A Block reaches
  `Into` only as a body (a block expression is no argument; Unsafe and Deferred Blocks are statements, `BlockStatementInExpression_Kd`)
  and gives Unit or nothing (SPEC 14.2.2), so `Into`'s Block and Unsafe rows take no input. A5 keeps its flow-insensitive `lent`. Not
  checked: an input that does not reserve (a shared reference's two Refs; an `obj/Base` conditional's two Adapts, which the first
  version counted, B1), whose `lent` no consumer reads, and an Opaque site's roots (a rejected default's prepared slots), whose writes
  are derived. R4 confirmed (sign-off, 2026-10-19) that the rule covers every read A5 makes today: `MirBorrows.Loans` records `lent`
  for each non-raw Ref, and A5 reads it only for the in-place-destroying argument through its two-phase reservations. Scope condition
  (R4): if a later A5 unit reads `lent` for a shared Ref, the rule widens first, and that unit's design names it; candidates are A5-F6's
  receiver-place relation (2f) and A5-F7's capture stores (2c). The library, the corpus and the mir sweep break the rule nowhere.
  Rejected: (b) a flow-sensitive `lent` in A5 (the producer owns the shape); counting an Adapt by its case views alone (a second
  predicate beside the builder's); the construct's own result temp moved into the input (its Refs stay a stale `lent`); a temp per
  body for every construct value (MIR changes beyond inputs); a decision at the Call by the argument's HirOp (a consumer deciding by
  syntax); a second flag carrying non-reserving inputs to the bodies (a mode steering them, for no reader).
- **R5-M73 The console `writeLine` row, string constants and the first pending files** (2026-10-19, C lane; plan C0b2, package C §3.2
  and §7, audit rows WriteLine and Const String; under R5-M18 form 1, R5-M65 and R5-M66) `FunctionCodegen` writes a Call whose Fn
  callee is a compiler function of the Body disposition (the published `IntrinsicDisposition`; a Shim is the shim items' row) by its
  legacy runtime row, until R8, the console `writeLine` first; C9a's formatting rows come from one Backend table beside
  `LegacyRuntimeHelpers.FormattingRow`. `writeLine` is `RuntimeAbi.WriteLine` called with the call's operands at its parameters'
  slots and the call site's location text (`Compilation.Locations`, SPEC 22.5.1, a pooled `Location` constant; a site without text
  is `InternalInvariant_Kd`). In a caller-location body the row forwards the body's own location, C3b's row; the other kinds are
  C8b-C8e's and C9a's. A `Use` of a string constant into an unprojected `string` local stores the whole `%kimi.string`: its UTF-8
  bytes a pooled `Text` constant, the Static release kind (the string representation, IMPL 21.1.3), the empty string `null` and 0.
  The entry stays C0b1's (R5-M65).
  - First program: `Console.writeLine("x")` also needs C2b's `Ref` of the literal's temporary and C4a's Keep Drop with the string's
    DropGlue item, which PD12's gate step 2 guards (R4-M6a's verdicts are exact only for programs A1 accepts; the per-body fact waits
    for R4's G125). So no console-output program generates under MIR code generation yet: the `Hello*` fixtures of
    `FunctionCodegenTest` run on the old generator and are pending under sel. Bringing those two rows forward is an open question.
  - Pending files: one full sel suite's failing cases, each filed under the review group (the plan's §4 Review column without `G:`;
    a unit reviewed alone by its ID, `C7b.tsv`) of the first unit, in plan order C0a to C10b then K13a to K13c, whose verification
    column names its class exactly, else of the first whose `X*` family prefixes the class's simple name, else `C-frame.tsv`, the
    group of the ledger's own units (C0b2 writes it, E10 reports and E11 gates it). K13a-K13c name their classes in the Change
    column, which is read for them (`OwnershipStorageLimitTest`, there only as marked for G7, is not named, so `Ownership*` files it
    under C-drop); a nested class is filed by its outermost class; `FunctionCodegenTest` falls under C3a's `Function*` (call). A
    fixture has a row only when it is written and fails natively; a case that fails only under load (PLAN G138) has none. A new row
    follows the same rule, which E11 implements.
  - `InstanceCollectorTest.TheDepthLimitCountsPlacedLevels` asserts the old generator's agreement, so it selects the old generator
    whatever the process's; `verify.ps1` normalizes `-Mode` as it does `-Pipeline` and `-Codegen`, since `codegen-pending.ps1`
    compares the spelling.
  - Rejected: a call writer for `writeLine` alone (the legacy rows share the parameter mapping); grouping by the first coverage
    record a test meets (no record names its test); a catch-all file named by no group (R5-M66); pending rows for the depth test (the
    old generator it asserts still runs under sel).
- **R5-M74 Object descriptors and VTables of the checked set** (2026-10-19, K lane; plan K8, the K package's §4.3 ObjectDescriptor and
  VTable rows and §4.5 rows 4, 8 and 12, package F §4.2-§4.3, S2-S4 X5 and X15; under R5-M18, R5-M60 and R5-M62; DEC Q6 form 1;
  `MonoKind.ObjectDescriptor` and `MonoKind.VTable` are approved with the K package, §11; round 2 after the independent review's B1)
  Only a creation (`MakeObj`, `MakeRc`, `MakeArc`, the intrinsic rows whose `Target` is Descriptor, and an Item of their functions)
  targets the `ObjectDescriptor` item of its payload in the item's instance, the substitution's Type argument (`Types[0]`, the one
  operand form every Type-reading row reads, as `LaysOutType` does), keyed by its erasure and admitted with its layout verdict (a
  type-driven item, R5-M44). A clone and a runtime Type test carry no target: a test acquires nothing (SPEC 13.6.4) and neither use
  needs a layout (SPEC 8.15.3); a clone is payload-independent (IMPL 21.1.3). A descriptor's targets, at its default site, are its
  payload's glue (the finalizer's destruction of the actual Type, SPEC 16.3.3, which a non-Sealed handle's glue leaves to it) and,
  when the payload's struct has a `VTableDef`, its `VTable` item; a VTable's targets, in slot order, are the bodies F4's consumer rule
  names: `Implementations[slot]` under `TyCtxt.Synthesized(implementation, its owning layer, [])`, through `Implementation` like any
  callee (limits, growing keys), where `TyCtxt.Applies` of the slot's original at the layer owning it holds; refuted, no target and
  the override's body stays checked only (a closed root, or a generic body no instance applies; R4-M28, K11); undecided, or a function
  off the representative's base chain, a broken contract, `InternalInvariant_Kd` at the VTable's site. A Virtual call and a virtual
  slot's Item have no target (X5). F4's two outcomes of a failed override: slot selection failed, no table, so no VTable item; a later
  failure, a table naming a definition without a MIR body, so that slot has no target; neither records anything (R4-M18). Same cause
  found by the search for "what a function runs": an Item of a function without a MIR body reached only its implementation's body,
  while its call reaches the published or the intrinsic row; `Implementation` falls back to `Bodyless`, which reads both, and the Fn
  row is a default template (the original's, SPEC 8.13.2) else `Implementation`, so an Item of `makeObj`, `Text.writer` or
  `Text.toString` targets what its call does. `ModuleCodegen.Item` reports the two kinds as coverage rows (C6a, C6c).
  - C6a's contract: it numbers the erased payloads of the Generated descriptors and their base chains (`AdtDef.Base`, each base Type
    under the payload's substitution, generic arguments kept, Origins excluded), one table per module; a descriptor's base map holds
    its chain's numbers; a type test compares the object's numbers (`__kimi_object_supports`) with its target's,
    `Erase(TypeOf(item, its token's Type))`, and where no Generated chain holds the target it is the constant false, the operand still
    evaluated (SPEC 13.6.4: "A test with a statically known result is still valid and evaluates its operand"), since every runtime
    object comes from a Generated creation; C6a decides whether the module needs the object runtime (a Generated descriptor or a
    runtime-reached handle operation). C6c pairs slots with a VTable's targets by the implementation's definition.
  - Departure (for the owner's departures list at R5's group review): the K package's §4.5 row 8 names all five handle operations as
    descriptor sites; Clone and TypeTest leave it, because the package's §4.5 admission ("admitted only for a Type whose layout verdict is
    OK") with row 8 put a layout obligation, and the glue and VTable closure of a Type never created, where SPEC 8.15.3 puts none (the
    review's reproducers: `a is Huge` and a clone of `ref/(rc/Huge)` reported OversizedLayout_Kd; `a is Leaf<i32>` collected Leaf<i32>'s
    slot bodies). X15's whitelist of target-bearing intrinsics shrinks from eight to six.
  - Rejected: an identity-only item for a type test (a MonoKind the package does not name, while C6a numbers the chains anyway);
    descriptors targeting their bases' descriptors (lays out Types never created); a TypeToken operand column on the intrinsic rows
    (round 1's; nothing reads the token now) or on MirDescriptors with a validator arm; a slot index in `MonoTarget.At`.
  - Verified (`InstanceCollectorTest`): closed tables of a generic hierarchy (one descriptor per erased payload for obj, rc and arc; slot
    bodies per owning layer); the factory Item made, called and erased in an instance; Items of Text.writer and Text.toString; a finite
    cycle reused and a growing key located at the creation; an original a table replaces adds nothing; refuted conditional slots (Block
    and clause) without a target or the override's instance; both failed-override outcomes without a record; type tests and a clone
    without a target or record (an oversized never-created struct, a never-created Leaf<i32>); warm collection 0 B. Under sel the
    library roots and the 44 milestones give no new record.
- **R5-M75 A tuple operator is expanded in MIR over its leaves** (2026-10-19, W1; plan F7, PD15 as package F item 5 and §4.8 refine
  it, S2-S4 row 7; under R5-M68; DEC Q6 form 1) A comparison whose `CallPlan.TupleOperator` is set is built by `MirBuilder.Expand`
  (`MirBuilder.Calls.Comparisons.cs`, which `==` now reaches too) from `TyCtxt.Comparison(the operator's Contract, call.ConformingType,
  operators: true)` instead of its whole-tuple call (SPEC 13.4, 13.4.1, 12.4.3), the Contract SPEC 13.4.1's for the operator from the
  declaration table (`Library.GetSymbol`; no Binding internal or Koto slot, ARCH's dependency table). Operands: the call's two inputs
  as Binding adapted them, the `ref/Self` views, the left pending and secured as a built-in operator's, never moved; the leaves are
  Places below them, depth first (a Tuple part its Field, a `ref` layer its referent; the Never value its own leaf). A Builtin leaf's
  pair is its Places Copied, a string's (the one built-in leaf Type that is not Copy) shared borrows, Copied: a `Binary` as a direct
  string comparison builds, which code generation lowers to the string comparison call (the package's "string call"; MIR has none);
  a Witness leaf is a `Fn` callee, a Requirement leaf a `Requirement` callee naming the Contract, each on fresh shared borrows; an
  Equatable leaf's bool is its equality, a Comparable leaf's i32 is paired with 0. Each leaf but the last tests `Eq` of its pair (an
  Equatable leaf its bool) and branches: equal goes on, unequal decides by the operator on the pair (for `==` and `!=` the test's bool
  or its negation); the last leaf decides by the operator on its pair; each path writes the destination once and every `Binary` is
  proven. IEEE makes an unordered pair unequal and its ordering false (SPEC 13.4). A null `Comparison` (a Limit) is one located
  `Unsupported_Kd` with an Opaque value, the operands not built; a None leaf is `InternalInvariant_Kd` with a derived Opaque result
  (R5-M68). `ComparisonPlan(CallPlan)` stays for its old readers (H11). A1–A5 see two shared Loans live to the join, Copies through
  them, reborrows into each leaf call and an edge per Contract leaf: A4 no longer meets a composite built-in comparison's Limit for a
  tuple operator (`MirEffectsTest`'s generic row), nor the collector a Builtin shim (its shim test retargeted to generic instances'
  Contract-mapping comparisons, the shims' remaining producers). Rejected: a string leaf as a call (F6's Builtin leaf names no callee;
  a second form of one comparison); testing the last pair too and deciding all-equal by a constant (a redundant test, two string
  passes); deciding `==` by recomputing the pair (a second comparison); the legacy four-valued relation code (a new form).
- **R5-M76 The operator rows of MIR code generation** (2026-10-19, C lane; plan C1a, package C §3.2 I5 and I9, §3.9, audit row
  "Rvalue BinaryOp (16), UnaryOp (2)"; under R5-M18 form 1, R5-M17, R5-M20 and R5-M65) `FunctionCodegen.Arithmetic` writes a BinaryOp
  or UnaryOp into an unprojected local through `LlvmScalarText` by one table, `Rows`: a row per `MirBinaryOp`, then per `MirUnaryOp`.
  The row's entries are keyed by the left operand's closed Type (`TypeOf`): signed integer, unsigned integer, signed wrapping and
  unsigned wrapping integer (SPEC 3.1.1: the argument's signedness). Each entry is an LLVM operator (an instruction, an icmp predicate
  or an overflow intrinsic's stem) and SPEC 13.3.1's check, one `LlvmScalarText` writer (none, overflow, signed quotient, zero,
  wrapping quotient, shift). bool and char take the unsigned entries, whose operators (equality, char's order, `not`) check nothing.
  A unary row fixes its other operand: Neg is `0 - x`; Not is `x xor -1`, logical on i1 and bitwise on an integer as the vocabulary
  says. A row's Reflexive column gives a comparison of zero-sized operands (Unit's one value, SPEC 13.4) without an instruction. A new
  Type class adds a column and an operator adds a row; the writer has one arm per check.
  - Proven: the builder's `MirRvalue.Proven` (IMPL 21.3.3) selects the wrapping entry without its check (`WriteOperation`, with its
    literal -1 rules): an operation that cannot fail computes its wrapping result. Codegen never decides Proven itself.
  - Abort: a check Aborts through `WriteFailure` at its statement's location (`Location`, the one rule the writeLine row now reads
    too: `Compilation.Locations.Text(body.Subject(source))` as a pooled Location constant; in a caller-location body a record until
    C3b forwards the body's own location, SPEC 22.5.1). Its names reuse the result's number (`%checked<n>`, `abort<n>`, success label
    `c<n>`). No nsw, nuw or inbounds (I9).
  - Rows not written yet (located `Unsupported_Kd`, PD11): floating-point operands (C1b, with Float constants), raw-pointer equality
    and displacement (C8a, whose raw memory rows bring the stride addressing; no raw pointer reaches a generated program before
    them), string borrows (C8c), and checked 128-bit add, subtract, multiply and negate, whose `WideOverflowDeclarations` are
    `ModuleCodegen`'s module-level request (open issue; `ModuleCodegen.cs` was held by K11). 128-bit division is the collector's
    capability gap and never reaches codegen.
  - Rejected: arms per operator (the old `BodyLowering.Graph` shape); deriving the check from (operator, signedness, wrapping) in
    code beside the table (a second copy of SPEC 13.3.1's rule); a Proven test in codegen (it would re-derive the builder's fact);
    writing the wide declarations from `FunctionCodegen` (module-level text, duplicated per function); a raw-pointer column now
    (not verifiable before C1b, C2b or C8a produce a pointer value).
- **R5-M77 The Generated mark at emit** (2026-10-19, K lane; plan K11, the K package's §4.4 and §4.10, package C §3.3 and §3.8, design 2
  §2.9; under R5-M18, R5-M35, R5-M64 and R5-M65; DEC Q6 form 1) MIR code generation writes exactly the items of the checked set the
  output requires (IMPL 21.3.1), the closure of its output roots over runtime targets. The generation entry
  (`Compilation.Generation.cs`), after gate step 1 and acceptance, names the roots: an Application's startup body (`Binding.Startup`,
  `main`'s Fn body, else the root module's top-level statements), a test build's tests (the Test body of each `Tests.Cases` entry's
  `TestCase.Symbol`: the catalog `Project` discovers after acceptance, which the entry reads and never discovers again) and a Library's
  every root but the checked-only ones (a `public` declaration is no export, the plan's "public roots" being the package's "every root");
  a root without an item is `InternalInvariant_Kd` at the project. `CheckedSet.Mark(roots)` sets the set's Generated column, one bool per item: the roots and,
  transitively, every item a runtime target of a Generated item names, so an item only the checks reach is checked and never generated
  through that target; a mark replaces the previous one, a new collection has none (`IsGenerated` reads it), and a warm mark allocates
  nothing (a pooled worklist). `ModuleCodegen` writes the Generated items in admission order (names stay item-indexed) and the output
  form around the roots it is given (an Application's entry calls its startup root; a test build's TestEntry row is at each test root);
  `Signature`, the one request that names an item, records `InternalInvariant_Kd` at the item's site for an item outside Generated.
  - Checked-only roots (K §4.4, §4.10, R4-M28): `InstanceCollector.Generates(root)` leaves out the body of a closed virtual or override
    whose slot `TyCtxt.Applies` refutes at its struct's layer owning the original, its struct being its receiver's Self; it reads the one
    slot rule K8's VTable targets read (`Applies(type, table, slot)`), and an undecided slot is a broken contract, `InternalInvariant_Kd`
    at the root's site (as K8 records it at a VTable's). Such a body is accepted at check when its condition is refuted without contradiction (a non-conforming Type under
    `T is Marker`); the old generator skips it too (`IsInapplicableVirtualBody`). An Application or a test build reaches none: a refuted
    slot has no target and a call of it is a Binding error.
  - Rejected: a Generated field on `MonoItem`, the collector's row (the mark is emit state, replaced per mark); a side table of marked
    items; discovering tests in the entry, as the old generator's `TryPrepare` does (a second discovery per generation; `Project` owns
    it after acceptance); TestEntry roots from the root module's Test bodies (a second copy of discovery's selection); the mark in
    `ModuleCodegen`, which decides no generation (package C §3.3).
  - Verified: an Application generates its startup closure only (an unused function is neither written nor recorded; its Library
    twin is both); a test build generates its discovered tests, not its top-level statements or their instance; a Library every root
    but the override of a refuted slot (generated where the slot applies); the mark follows runtime targets only and is replaced by the
    next mark and cleared by a new collection; writing an unmarked body records `InternalInvariant_Kd` at its site; warm generation 0 B.
- **R5-M78 The first program's forms brought forward: `Ref`, the Keep and Elide drops, the string glue and gate step 2's stand-in**
  (2026-10-19, C lane; plan C0c, package C §3.2, §3.6 and §3.8; owner, 2026-10-19: 「C0b2 の次の作業順を (a) 前倒しする」, choosing
  (a) "bring C2b's unprojected-local `Ref` and C4a's string Keep/Elide Drop forward with a stand-in for gate step 2, a temporary
  structure removed by C4a" over (b) "keep the package order"; under R5-M18 form 1, R5-M65 and R5-M73) Brought forward, each a whole
  row of its table: C2b's `Ref` over the place writer (`FunctionCodegen.Rvalues.cs`; every `MirRefKind` is one pointer, IMPL 21.1.3;
  the place writer's one covered Place is a local without projection that has storage, its home); C4a's Drop verdict rows
  (`FunctionCodegen.Drops.cs`): a Keep destroys exactly when `MirTypes.MayNeedDrop(TypeOf(item, body.TypeOf(place)))` holds, the rule
  the collector targets a Drop's glue by: refuted (a Type parameter instanced at a Type that owns nothing), nothing is written; else it
  calls the collector's target, the Type's DropGlue item, on the Place's address with the Drop's location text, and a missing target is
  `InternalInvariant_Kd` (package C 3.2 I8); Elide writes nothing. PD18's string row of the glue table: `DropGlue.Release` (the file K7
  created) gives a Type's routine, the runtime release for a string, and every other Type is that table's located `Unsupported_Kd`
  (C4a); ModuleCodegen names and signs the item (`__kimi_drop<item>`, `void (ptr, ptr, i64)`, `DropGlue.Parameters`, package C 3.3),
  and `FunctionCodegen.WriteGlue` writes its function, the routine called with its own parameters through the call writer the
  `writeLine` row uses (PD3).
  - Why a stand-in: under sel `Accepts` reads the old analysis (`MirRecords` holds construction, validator and collector records
    only), so the MIR analyses' A1 and A2 problems refuse nothing. Ledger row a1-whole-incomplete's `let a: [2 of string] = ["a",
    "b"]` / `_ = a[0]@move` / `a.indices` is accepted with A1's `MovedPlace_Kd` at `a.indices` and a Keep row in its body. R4-M6a
    makes A2's verdicts exact only for programs A1 accepts. Rule (temporary): a Drop row is written only for a program whose MIR
    analyses recorded no problem whose requirement is in the Ownership partition: A1 with its arena record, A2 and, conservatively, A4
    and A5. A3's control-flow problems do not gate (PD12). The check runs once per program version. Otherwise the Drop is one
    location-only `Unsupported_Kd` at its location in the Emission partition. A runtime Drop without a group in a program it passes
    is `InternalInvariant_Kd` (PD12). Removal: C4a's per-body gate step 2, which records the problem's own code at its subject (package
    §3.8) once R4's 1c-8 publishes `MirProblem.Body` (G125). It must attribute an inline copy's problem, which R4's 1c-6 reports as its
    template's, to the caller body.
  - Kept: C2b keeps the projected and zero-sized `Ref`s (the zero-sized address symbol) and the other rvalues; C4a the glue of the
    other Types (from `DropGlue.Parts`), the per-body step 2 with its refusal record and the entry superseding R4-M1, and this
    stand-in's removal; C4s its matrix except the string rows' IR checks. Every C0c string is Static, so E2's audit sees no free; heap
    strings come with C8c and C9c.
  - `StringEmissionTest.WriteAuditedFixture` finds the audited constants among the audited module's own definitions (both generators'
    `LlvmConstantPool`), not through the old generator's module: under sel the old module's numbering named other strings, so two
    audit fixtures failed natively while the code was right.
  - Departure (for the owner's departures list at R5's group review): rows of C2b and C4a (G:C-aggregate, G:C-drop) land in a
    G:C-frame unit ahead of the package order. Gate step 2 is a program-wide stand-in over the Ownership partition, wider than PD12's
    per-body A1/A2 rule. It records `Unsupported_Kd` at the Drop rather than the problem's code at its subject, because that record
    needs G125 and byte facts A1's arena record does not keep. Glue bodies are written by `FunctionCodegen.WriteGlue` through the one call writer and
    named by ModuleCodegen, so `Backend/DropGlue.cs` keeps the glue table (`DropGlue.Release`), not package 3.6's glue writing;
    C4a extends that table (round-2 review note).
- **R5-M79 Mir/Build's syntax-class decisions read published facts** (2026-10-19, W1; plan Q16, Q8 package §3.5 and D6; under
  R5-M10 and R5-M23; DEC Q6 form 1) The 17 sites R5-M10 left, where a Koto type test chose an operation, read the fact that decides
  it: `HirOp.Continue` (a transfer), `HirOp.Group` (a parenthesized Place), `Binding.WritesText` (an interpolation's text bytes, the
  text read from its literal), `BoundFormatting.Acquisition` (an owning interpolation's calls before its writes), the Type kind (a
  FixedArray aggregate, a Tuple's being any other, Unit included), `TyCtxt.Signature`'s count (a call's and a body's parameters), the
  symbol kind (a definition's body: Function or none, PropertyAccessor, a static Property), `AdtDef.IsConstructor`, and the two views
  of Q8 D6: `Binding.Reassignable(symbol)` (`Binding/Binding.Reassignable.cs`, which owns the rule with `IsMutableDeclaration`;
  computed, no state) for every local, Pattern binding, iteration slot and static, replacing three copies in Binding (the package named
  two; `Binding.Closures`' exclusive capture is the third), and a Tuple element's position on its Field node's own row
  (`PlanRow.Folded`), which the publisher's `Fold` writes beside a ConstIndex's. ElementAccess.cs, due by AGENTS' three-units rule,
  gets no split: its one change replaces a copy of the expression by a call to its owner, no logic added (by analogy with the
  propagation form DECISIONS Q7 exempts); its old-path members leave at R6.
  - Decided beyond the package: (a) the closure fact in Mir/Build is the published closure definition, `BoundClosure.Of`: the Opaque
    roots walk and the default-edges walk, the default templates (a closure has none, SPEC 7.2.2) and `Module`'s body kind, which
    `Body` and `Bound` then read (`MirBodyKind.Closure`). The default-edges walk reads the call plans of the calls the template builds
    only: none at a node published `HirOp.Syntax` (a replaced or covered part; it goes on to the parts below, which may be evaluated),
    `Error` or `Unset` (an Opaque value, no call built), so a closure Binding did not define, published Error with its body replaced
    even where Binding bound that body first (a concrete closure without an expected Type, SPEC 23.1.2), adds no edge, and the call
    that omits the default takes an Opaque value built at the call (review rounds 1 and 2, B1; `MirBuildTest.AFailedClosureInADefaultIsNoEdge`). (b) Sites with the same cause, replaced too:
    `MirBuilder.cs:108` (defaults of a closure), `:222` (a body's parameter count), `MirBuilder.Blocks.cs:135` (a local's mutability)
    and `MirBuilder.Formatting.cs:31` (an interpolation's form); `Bind`'s loop parameter is gone. (c) `Binding.WritesText` is the one
    classification of a text write, which `Binding.HasFacts` (an interpolation's write needs a Type argument unless it writes text) and
    the builder's estimate read.
  - Not sites (syntax data, R5-M10): `Published`'s value-child switch, a constructor's base call, the literal forms under
    `HirOp.Literal` (G122).
  - Rejected: a stored `BindingSymbol.Mutable` (Q8 open point 6: computed); `HirOp.Closure` for the walks (a closure whose creation
    failed is published Error, and one in a replaced part Syntax, though its body is built, R5-M63); a Type-kind test for Tuple (an
    empty Tuple literal's Type is Unit); a constructor view (`AdtDef.IsConstructor` exists); `HirOp.Literal` for a text write (the
    interpolation's facts are judged before its parts are published); stopping the edges walk at a Syntax node (a covered callee's
    receiver and a `$tryWrite` text's values lie below one).
- **R5-M80 The conversion rows and floating-point operands of MIR code generation** (2026-10-19, C lane; plan C1b, package C §3.2 I5,
  I7 and I9, §3.9, audit rows Cast Integer to IntToPointer and Const Float; under R5-M18 form 1, R5-M17 and R5-M76) `FunctionCodegen.Casts`
  writes a Cast into an unprojected local through `LlvmScalarText`'s conversions (P4b) by one table, `ConversionOf`: a switch keyed by
  the `MirCastKind`, the source's and the target's class (a signed or unsigned integer, a wrapping one by its argument; floating point; a
  raw pointer) and the sign of the target's width less the source's (Layout). A row is the LLVM cast, null where the representation is
  kept (same-width integers, a raw pointer to a raw pointer), and its check, one P4b writer: Range (SPEC 13.5.4: each side where the
  source holds a value the target cannot, `slt` and `sgt` or `ugt`, against the target's extreme at the source's width;
  `WriteCheckedConversion`, KIMI_E_INT_CONVERSION), Truncation (float to integer: the minimum's predecessor where the significand holds
  it, `ule`, else the minimum, `ult`, and 2^(N - sign), `uge`, unordered so NaN and the infinities fail; the same writer) and Narrowing
  (f64 to f32: the f32 maximum plus half an ulp, ordered; `WriteFloatingNarrowing`, KIMI_E_FLOAT_CONVERSION, so NaN and the infinities
  convert). A Proven Cast writes no check; a Range whose closed Types leave no side (an instance of a generic conversion, which the
  builder cannot bound) writes the cast alone. A new form adds a row. Upcast (C6a) and Erasure (C5b) have none: a located
  `Unsupported_Kd`, as is a Cast of a raw pointer's null (C8a); 128-bit to or from floating point stays the collector's capability gap.
  - Floating-point operands: R5-M76's table gains its fifth column (f32, f64): fadd, fsub, fmul, fdiv and fneg, and the fcmp
    predicates oeq, une, olt, ole, ogt and oge (SPEC 13.4: a NaN operand makes `!=` true and the others false; +0.0 == -0.0); SPEC
    defines no floating remainder, bitwise or shift operator, so those entries are empty. A unary operator's fixed operand belongs to its
    entry (Neg's integer entries subtract from 0, Not's xor with -1; fneg has none and keeps the sign of zero), and a Proven operation
    takes its column's unchecked column (`Unchecked`: an integer's wrapping column, any other its own). `Rows` is checked at type
    initialization against both operator enums and the column count (C1a's review, item 5).
  - Floating constants: a Value of prefix `x` is LLVM's hexadecimal double; an f32 constant is widened exactly in code, so a NaN keeps
    its payload and signaling bit, which a hardware widening sets (the old generator's did until R5-M86's D5 fix; both generators
    now take their bits from `LlvmModuleText.Hexadecimal`). `WriteValue` spells through the one `Spell`, and C1a's failure operands are one
    `Failure` for both tables.
  - Rejected: a second operator table for floating point; a row-level fixed operand (`fneg` takes none, and `fsub 0, x` loses -0.0);
    per-form arms or a plan computed beside the table (the old `BodyLowering.PlanConversion` shape); a Proven test in codegen; a check
    written with no test (`WriteCheckedConversion` needs a bound); the runtime's float-to-double conversion for constants.
- **R5-M81 The pending ledger's gate** (2026-10-19, E lane; plan E11, PD10, OQ9; under R5-M8, R5-M66 and R5-M73; removed with the
  ledger at G4, OQ9) Under `-Codegen mir` a run passes only when its failing set equals the ledger within its scope (R5-M66's
  coverage): an entry that passes now, is no longer executed or is added fails it. The verdict is the `codegen pending` step's, after
  the tests, native steps and milestones (a fixture's state needs every native step); the sel test and native steps fail only on
  missing evidence (no executed case, a selector executing none, a run error the results record, a selected fixture without a result),
  so listed fixtures run. A sel Session discovers its methods too, and a selected method the results record no case of (a partial run)
  fails the step without a report. `verify.ps1 -UpdatePending` (sel only) writes the ledger from the run's `codegen-pending.json`
  after the input-stability step, and only when the gate is the run's one failed step: pending and out-of-scope rows stay in their
  files, rows that pass now or are no longer executed leave, and an added row is filed by R5-M73's rule, whose one owner is
  `Get-KimiCodegenGroup` (`scripts/codegen-pending.ps1`); an unchanged file is not written, an emptied one is removed, and that run is
  not commit evidence. A case that fails only under load (PLAN G138) is never listed: it fails the gate and is re-run.
  - Rejected: a verdict per test or native step (a fixture two patterns run would be judged twice); writing the ledger before the
    input observation (the evidence would name a ledger it did not compare); re-filing kept rows; an allowlist of load-dependent cases.
- **R5-M82 Unsupported static storage is a capability row at its declaration** (2026-10-19, K lane; unit K11b, the planning session's
  decisions on R4's sel pending check and on K11b's review; under R5-M18, R5-M40, R5-M53 and R5-M77; the capability rows approved with
  the K package, §11) Since K11 MIR code generation reaches only what an output requires, so a form a codegen coverage row refused only
  when every item was written is accepted when nothing reaches it; for a form no common rule covers, that accepts a narrow subset of it
  (AGENTS Diagnostics). Static storage is one such form (P42; STATUS "Static members"). The capability check decides it from facts the
  Backend reads, MIR code generation's own contract: a verified static of a root module is the `StaticStorage` row, `Unsupported_Kd` at
  its declaration (its StaticInit body's source), used or not, when it can be set (`BoundProperty.Setter.IsPresent`: a `var`), when its
  StaticInit body's result Type is not a scalar (`ScalarTypes`: an aggregate, borrowed or drop-needing Type) or when its definition
  instantiates (`TyCtxt.Instantiates`: a static of a generic container's group, whose storage the MIR keys by its symbol alone, a literal
  `let` included). It comes from the collector's definition-stage scan of root-module bodies (A3b), so under MIR code generation it rejects
  at check (R5-M41). An unverified static is Binding's error; an immutable scalar of a closed group, also one with a first-access
  initializer, is no record. Removal condition: P42, not C7b.
  - One record per cause: this row supersedes R5-M60's `GenericStaticKey`, which recorded each access of a static of a generic
    container's group; such an access admits nothing and records nothing (the declaration is the record), and the row is deleted. The
    bundled library declares no such static (its rooted check, P5, has no record). Changed expectations: `AStaticAccessIsItsInitializer`
    (one record at `public let count: i32 = 0` for the two accesses) and Milestone 42 (five declarations, no access records).
  - Pending rows (R5-M73's rule applied by hand): the sel cases whose programs declare unsupported static storage now reject at check:
    `CallableEffectBoundTest.FunctionItemsUseTheirTransitiveEffects` (closure, C5a's Callable*), `EffectBoundReportTest.ReportsTheViolatingEffect`
    x3 and `MilestoneSourcesTest.AuthoredProgramsMatchTheirRecordedStageAndDiagnostic` (C-frame, no unit names them),
    `Utf8ReserveEffectTest.ClearingBorrowedDictionariesChecksSourceDestruction` x2 (format, C9a's Utf*); their fixtures are unchanged.
  - Search for other forms that relied on every item being written: the remaining coverage rows (Closure, DefaultTemplate and StaticInit
    bodies, MIR forms, mono-item kinds, TestEntry) name forms the frame will write, so an unreached one is no accepted subset.
  - Rejected: the old pipeline's `StaticScalar` predicate (it decides by syntax, the Backend may not read it, and a `Supports` wrapper over
    it would be a `Supports*` gate; editing the frozen old generator to share it is outside D5); a pending row for the unused static (it
    would ledger the unsound acceptance); a row for every StaticInit (it refuses statics C7b supports); a `var`-only row (one rule for
    P42's remainder); coverage records for items outside Generated (codegen requests only Generated items, R5-M77).
  - Verified: an unused and a read static `var`, an aggregate `let` and a literal `let` of a generic container's group are the gap at the
    declaration; an immutable scalar `let`, literal or first-access, is none; `ModuleEmissionTest`'s unused-dependency `var` is refused
    under both pipelines.
- **R5-M83 The place writer of MIR code generation** (2026-10-19, C lane; plan C2a, package C §3.2 I5, I7 and I9, X1 and X13; under
  R5-M18 form 1, R5-M65, R5-M78 and R5-M80) `FunctionCodegen.Places` is the one owner of a Place's address. It starts at the root's
  home and takes each step of the interned chain, root first, through one switch over the seven `MirProjectionKind`s, with offsets from
  `Layout.Of(TypeOf(item, prefix))`:
  - Field: `FieldOffsets[arg]`. Base: no offset (X1, the complete base is a prefix). ConstIndex: `arg` × the element's stride, without a
    check (the validator proved it in range). Downcast: the enum record's payload offset (`FieldOffsets[1]`).
  - Deref of a reference or a raw pointer loads the address it stores (IMPL 21.1.3). An object handle's or object borrow's payload is
    C6a's row.
  - PairLayer: `MirCases.Layer` under the item's view. `FunctionCodegen` selects that view once per item, with its own `MirCases`, from
    the collector's `View(item)` (X13). The owner case keeps the address; any other case takes its case Type's Deref row.
  - Index: the isize position local is compared unsigned (`icmp uge`) with the length, so a negative position fails too. The length is a
    fixed array's closed Length, or the length word of an Array's or Slice's handle record (`FieldOffsets[1]`); the buffer word is the
    element base. The failure goes through `LlvmScalarText.WriteFailure` with KIMI_E_INDEX_BOUNDS at the position local's location,
    which R4-M56 places at the selection (SPEC 22.5.1). The element is then at `mul` by the stride and `getelementptr i8`, the old
    generator's text. A zero-sized element keeps its check and takes no address of its own. No `inbounds` (I9).
  - Every operand Place (Copy, Move), every Assign destination (Use, operators, casts, Ref, the aggregate), a Ref's Place and a Drop's
    Place go through it. An Assign computes its rvalue, then locates its Place (SPEC 13.7.1), so a right-hand side's Abort precedes
    the target's bounds check.
  - Moved from C2b (planning session, 2026-10-19; the same group G:C-aggregate, so no departure from the owner-approved package beyond
    the order inside the group): the `Aggregate(FixedArray)` row for the operands the frame covers. Each element is stored at its index
    × the stride once the Place is located; `noinit`, from no operand, stores none. A native program has no other indexable value
    before C2b. An element of a slot Type and Tuple, struct, enum Case and Closure aggregates stay C2b's; an Array or Dictionary from
    none is C8d's and C8e's.
  - Restructuring first (behavior unchanged): `FunctionCodegen.cs` is split into `.Places.cs`, `.Rvalues.cs` and `.Calls.cs`, and the
    item's `FunctionAbi` is a field. `ModuleCodegen` passes the compilation's `TyCtxt` to `FunctionCodegen` (Q7's one-argument form).
  - Left to W1 (F19, PLAN G157 (20)): a position wider than isize is still the builder's checked Integer Cast, which Aborts with
    KIMI_E_INT_CONVERSION where SPEC 4.6.1 asks for KIMI_E_INDEX_BOUNDS. F19 builds it from existing forms (proven compares, then -1
    or a proven Cast). A new `MirCastKind` for it was rejected: it would be a vocabulary entry for a meaning existing forms express.
  - Rejected: an arm or plan per projection form beside the switch; promoting the old `WriteElementAddress` (its operands are the old
    IR's, and its shared part, the failure block, is already `WriteFailure`); deciding a PairLayer from the closed prefix and step Types
    (a second copy of `MirCases`' case decision); the consuming statement's location for an Index check (`f(a[i])` would report the
    call, not the selection); a Proven bit on Index (a vocabulary change; a range's redundant element check is O2's, measured at the
    group review).
- **R5-M84 A position's conversion** (2026-10-19, W1; plan F19, PLAN G157 (20); C2a's design option B, the planning session's
  decision) SPEC 4.6.1, 4.6.3, 22.5.1-22.5.2: the MIR builder converts a position to the isize its selection reads, its value where
  isize holds it, else -1, which the selection's one bounds check (the Index's, a range's chain) rejects with KIMI_E_INDEX_BOUNDS at the
  selection; a position has no check of its own (the old IR's `PositionConversion`). `MirBuilder.Position` decides by the Type's range:
  isize (and Never) is used; a Type whose range lies within isize's (`Within`, the rule a Conversion's Proven shares, IMPL 21.3.3) is a
  Proven Integer Cast; another (i128, u64, usize, u128; i64 and u32 on a 32-bit target) is first compared with isize's extremes in its
  own Type, the minimum only where it is signed, each failing test leaving for a block that writes -1 (`Unresolved`), and is otherwise
  the Proven Cast. A Type parameter's range is its instance's, which no builder rule decides: a located `Unsupported_Kd` (open issue),
  whose Opaque names the built value's root, not the position's syntax (R5-M63's rule: an Opaque site names a part located already by
  its Place's root), so a root the value moved is not used again there.
  A written `^x` is `L - x` where `0 <= x` (a literal in range proves it), else -1 by the same join, so every boundary is evaluated
  before the selection fails, at the selection (SPEC 4.7.4, 22.5.1). MIR has no select form, so the shape is a diamond (a select at
  O2); the position local stays the selection's (R4-M56), written on both arms. No vocabulary entry.
  - Rejected: `MirCastKind.Position` (option A: a vocabulary entry); failing at the conversion (option C: an Abort before later
    boundaries are evaluated); a branchless shift-and-or encoding (not a MIR form, opaque to review); keeping Wrap for a same-width
    unsigned position (a second encoding of an unresolved position).
- **R5-M85 The codegen deviation form** (2026-10-20, E lane; plan E12 and K13a, PD10, OQ7; under R5-M8, R5-M66 and R5-M73; DEC Q6
  form 1; temporary: the stage that makes an `Expect`'s target the default reduces it to the target, G2 for acceptance-time records
  such as this entry's (the collector runs on the default pipeline from G2, R5-M41) and G3 for generated output; G4 removes the form
  and the catalog) Where MIR code generation (sel) differs from the old generator by a decided item, the expectation states its
  target and keeps the legacy value: `Codegen.Expect(target, legacy, deviation)` (`tests/xUnitTest/Codegen.cs`) gives the legacy value
  unless the selector's one reading (`Compilation.DefaultCodegen`) is MIR's, and fails when the two are equal. Refining PD10's
  `Expect(target, legacy, item, class)` (package C row 22), the item is one `Deviation(Id, Class, Item, Clause, Reason)` of the
  `CodegenDeviations` catalog: Class OQ7's (a), (d) or (e) (R5-M6), Item a DECISIONS entry, Clause SPEC sections; `CodegenDeviationTest`
  checks each and that an `Expect` names it (M7 counts them). First: `checked-set-at-check` (d; R5-M41; SPEC 23.2.2), sel's check-time
  records of the checked set, R5-M54's capability records and (K13b) R5-M40's and R5-M44's resource limits, which the old generator
  reports at generation. Its tests (K13a's `GenericGenerationDiagnosticTest` rows, `PrimitiveIntegerTest`'s wide division,
  `PipelineSwitchTest`'s test command, K13b's limit rows) run check and generation in every run; the old generator's own refusal facts
  are checked on a compilation pinned to it and go at G2 with the legacy values. The pin needs the `Codegen` setter G4 removes, so every
  test pinned to the old generator (R5-M73's depth test `InstanceCollectorTest.TheDepthLimitCountsPlacedLevels`, K13a's and K13b's
  pins) goes by G4 at the latest. The native half lands with its first native consumer (C5a or C7b): the fixture writers write `<stem>.deviation`, byte-equal
  in both runs, the JSON of the entry and the legacy and target stdout, exit and stderr; `compare-fixtures.ps1` counts a differing
  fixture as deviating only when both runs carry the same `.deviation` and each run's output matches its side.
  - Rejected: per-test pipeline branches and xfail markers (PD10); item and class as arguments (declared at each use); the native
    half before a consumer; null targets for the old generator's own facts.
- **R5-M86 The owner's decisions of 2026-10-21: R5's departures list and PLAN G157 (19)** (owner, 2026-10-21, on the R5 planning
  session's explanation; verbatim 「departures list：K6b（R5-M70）、K8（R5-M74）、C0c（R5-M78）の 3 件を、すべて承認します。」 and
  「G157は、(a) 旧生成器を直す（推奨）でお願いします」, then, asked about R5-M6, the answer 「(a) のまま（R5-M6 の例外）」; the
  consequences are the planning session's) The three departures from owner-approved texts that R5 recorded for the owner's
  list are confirmed: K6b's Builtin key by the shim's compiler function in `MonoKey.Symbol` (R5-M70; against the K package's §4.3
  Builtin row and R5-M67's `Variant` key); K8's Clone and TypeTest without a descriptor target, which shrinks X15's whitelist to six
  (R5-M74; against the K package's §4.5 row 8); C0c's rows of C2b and C4a brought forward with gate step 2's program-wide stand-in
  until C4a and the glue written by `FunctionCodegen.WriteGlue` (R5-M78; against the package order, PD12's per-body rule and package
  C §3.6). They leave the list; R5's group reviews check the code against those entries. PLAN G157 (19), the old generator's f32
  signaling-NaN constants (SPEC 13.5.4: `@bits` keeps NaN payloads), takes option (a): a D5 miscompile fix, the exact widening one
  rule that both generators write through, so no deviation is recorded and E12's native half waits for C5a or C7b. This is the owner's
  exception, for this item only, to R5-M6's rejection of D5 fixes that make the two generators agree: the old generator's output is
  wrong code on the default pipeline and the fix is a few lines; R5-M6 stands for every other item. Rejected: (b) a decided OQ7 (a)
  deviation until G3, which would keep a wrong value on the default pipeline.
- **R5-M87 The rvalue rows of MIR code generation and the copy form of a value** (2026-10-21, C lane; plan C2b, package C 3.2 I4, I7
  and I8, 3.3, §4, row 10, X1 and X13; the planning session's decisions on C2b's design; under R5-M18 form 1, R5-M78 and R5-M83)
  - Copy form (IMPL 21.1.3, 21.3.1; SPEC 3.5: a Copy and a Move write the source's bits): a value is in memory when its Type is a slot
    Type of nonzero size (`Abi.HasResultSlot`) whose representation is not one first-class LLVM value. The place writer's one predicate
    (`InMemory`) excludes the string's record (`RuntimeAbi.String`, three Fields), the one slot Type with a first-class representation,
    which is loaded and stored whole, its Fields only, as the old generator's MoveString moves it; no Layout or Abi fact says "first-class",
    so that row is its one reference. `Read` gives a value in memory as its address and `Put` copies it into a destination with
    `llvm.memcpy` of its size, its alignment on both pointers (the old generator's aggregate copy text; package C §4); any other value is
    loaded and stored in its storage representation, and a zero-sized one writes nothing. The ranges never overlap: every assignment
    moves a fresh temp (`MirBuilder.Replace`, `Overwrite`) and every other Use, aggregate and Fill initializes a fresh Place (searched over
    every `Use`, `MirRvalue.Use` and aggregate site of `Mir/Build`); locating a destination writes nothing, so the bytes copied are the
    value secured first (SPEC 13.7.1).
  - Rows, one switch per closed vocabulary, each now complete, a kind without an arm a thrown internal error: a Use of any Place or
    covered constant; PairAcquire as a Copy of its Place in the item's case Type; the Tuple, struct (its base first, at 0, X1) and enum
    Case aggregates, each operand put at its part from `Layout.Of`'s Field offsets (a Case's payload at Field 1 plus its payload Tuple's
    offset, after the Case ordinal stored as the tag); the fixed-array aggregate and the Fill (R4-M81's loop) through `Put`, so elements
    in memory are copied too; Discriminant, the tag loaded and zero-extended to isize; Len of a fixed array (its closed length, after its
    Place is located), an Array or a Slice (its handle's length word, Field 1, the one `Length` C2a's Index reads); an Adapt by the
    ordinary rvalue `MirCases.Adapt` returns under the item's view, written by that rvalue's row (a view that selects none is
    `InternalInvariant_Kd`); a zero-sized local's home and so its borrow and its parts' address, the module's one 16-aligned symbol
    `__kimi_zero_sized` (package C row 10).
  - `Layout.Tag`: an enum's tag, the Case ordinal as the i32 row at offset 0, published by Layout (its due split first, e25c876f) and read
    by `PlaceEnum`, the Case aggregate and Discriminant: the enum record publishes only its Field offsets.
  - Refinement of the accepted design: the module frame (`ModuleCodegen.Frame`) writes the memory declarations and the zero-sized
    symbol only when a definition requested them (`ModuleCodegen.Request`, design 2's request form), not unconditionally:
    `ScalarEmissionTest.EmitFixture` returns the whole module, and an unconditional declaration would fail
    `StringEmissionTest.TransfersAndCleanupExecuteOnce`'s `DoesNotContain("llvm.memcpy")` for the string moves this unit writes.
  - Left as located `Unsupported_Kd`: a Closure environment (C5a); an Array or a Dictionary from none (C8d, C8e); a Dictionary's Len, its
    count being `DictionaryStorage.length`, word 3 (C8e); a string's Len (C8c); an Adapt's object creation, which allocates (C6a);
    upcasts and object borrows' payloads stay C6a's (the Cast table and the place writer). The plan's native multi-view body from
    `tests/mir/cases.kimi` needs calls: it moves to C3a; C2b tests both views of `keep` and of a case adaptation at the IR level.
  - Departure (for the owner's departures list at R5's group review): design 2 §2.2's dispatch says "a PairAcquire uses
    `MirCases.Reborrows`"; code generation does not ask it, because both cases write the same bits: a Reborrow of `uniq/T` is the pointer
    the Place already holds (IMPL 21.1.3), and the other case Copies the Place, so the case changes only the Type, which `TypeOf` gives.
  - Rejected: memcpy for the string too (one predicate, but it copies the record's padding where the old generator moves its Fields,
    and the twelve StringEmissionTest rows this unit makes generable would fail their class-I assertion); a copy-form column published by Layout (a shared-representation change with no second consumer
    before G8); a constant address for zero-sized homes (it departs from package row 10); code generation spelling the tag's i32 (a second
    copy of Layout's rule); asking `MirCases.Reborrows` (above); unconditional frame declarations (above).
- **R5-M88 `AnalyzeInstance` is deleted at R6-gen** (2026-10-21, planning session; plan Q4 under R5-M5, the owner's OQ6 (e);
  supersedes R4-M35's OD10 clause "with `AnalyzeInstance` a check-only step until R6-check") The clause kept the instance run
  because the definition run missed a hole it covered, PLAN10-X1 (an abstract part's destruction observing; A5 plan §9.1), to which
  the R5 plan's OQ6 added abstract temporaries as Loan roots. That reason no longer holds: Q1 reproduced both and Q2 and Q3 fixed them in the old definition run as
  D5 miscompile fixes (dev `4eb19789`; regressions in `BorrowingIterationTest` and `BlockEscapeLoanTest`; no library body or
  milestone program rejected, DEC O3). `check` never runs the instance run: its only callers are the old Emission's
  (`LlvmEmitter.cs:380`, `LlvmEmitter.Defaults.cs:15`; a search of `src` finds no other), so R6-gen's G7 deletes
  `OwnershipAnalysis.AnalyzeInstance` (`Analysis/OwnershipAnalysis.Instances.cs`) with them. Rejected: keeping it until R6-check
  (once the old Emission is gone nothing calls it, and acceptance never read it).
- **R5-M89 The call rows of MIR code generation, the signature owner and copy slots** (2026-10-21, C lane; plan C3a, package C 3.2 I8,
  3.3 and 3.9, plan 5.6.2, IMPL 21.3.1 and 21.4.7, SPEC 3.5, 14.10.3 and 22.6; the planning session's decisions on C3a's design; under
  R5-M65, R5-M77 and R5-M87)
  - Signature owner (a responsibility moved from `FunctionCodegen.LayOut` to `ModuleCodegen`): `ModuleCodegen.Signature(item)` gives a
    body item's signature from its own body, local 0's and its Arg locals' Types through `TypeOf(item, ·)` in its kind's order and the
    caller-location flag (`KimiLibraryCatalog.RequiresCallerLocation`), through `Abi.Of` under the internal convention, computed once
    per Write and reused while `Abi.Matches` holds; the definition and every call read the one `FunctionAbi`, so a caller may be written
    before its callee.
  - Calls (I8): a Fn callee by the collector's target at the call, read by one forward cursor (`Target`, also Drop Keep's, which replaces
    `Drops`' binary search: the targets are in location order and the frame writes the sites in that order); a body item is called as
    `__kimi_f<item>` under its signature, each operand at its parameter (a Value in its computation representation, a bool narrowed; a
    string borrow with its attributes; nothing for a zero-sized parameter; an OwnedSlot a Move's Place itself or a copy slot), the
    destination's address as the result slot, a Value result widened and put into the destination, then `br` to the target or
    `unreachable` after a call without one (SPEC 14.10.3). One call writer (`WriteCall`) writes the legacy rows', the glue items' and
    the module entry's calls too (`__kimi_start` calls its startup root through it, `WriteEntryCall`). A call inside a caller-location
    body is written when its callee takes no location: only a CallerLocation callee forwards one (SPEC 22.5.1), which C3b's row writes.
  - Copy slots (SPEC 3.5 with IMPL 21.3.1, the planning session's decision): a Copy of a value in memory (R5-M87's copy form) that a
    runtime-reached Call passes by pointer, which the callee owns from the call on, is copied into a copy slot `%c<k>` (R5-M92 replaces the
    copy slots' spelling, `%s<k>`, and narrows their scan to OwnedSlot parameters), an aligned
    alloca of the caller's entry block, so the callee never sees its source change (`f(s, s@uniq)`); one pre-scan of the reached blocks'
    Calls, after `MirReach.Walk` and before the prologue, numbers them; it covers every reached Call's in-memory Copy operands, whatever
    its callee's row, until a later call row (C3b, C5a, C6c, C8a) uses these slots or narrows the scan (its stated condition). A string
    constant passed to a string parameter is written into a copy slot as its record (the Static release kind, so the callee's drop
    releases nothing); its store takes an address since bp2 (`Text`, with the place writer's stores). No builder change: a Copy argument
    stays a Copy operand.
  - Disjointness (R5-M87's claim across calls): the result slot is the call's destination, never a pointer argument. `MirBuilder.Invoke`
    is the one Call constructor with a destination (the Fail intrinsic's is Unwritten); an assignment's call goes through
    `MirBuilder.Replace`'s fresh temp, a Move argument's Place is moved out and a Copy's is a copy slot (searched over `Mir/Build`'s
    `MirTerminator.Call` sites; `AResultSlotIsNeverAnArgumentsPointer` and the native `CallSlots`).
  - Body kinds: `FunctionCodegen.OrderOf`, the one switch over the closed `MirBodyKind` vocabulary that `Write`, `LayOut` and
    `Signature` read, gives the order of a kind's Arg locals: Fn, Getter, Setter and Test the receiver first (`MirCallee.Position`), a
    DefaultTemplate its prepared inputs in slot order (plan 5.6.2), so a recursive default calls its template; Closure (C5a) and
    StaticInit (C7b) bodies stay located. `ArgumentPosition`, the one rule from a callee's key (its declaration and body kind; a
    `#LibraryImport` function's as a Fn of its declaration), gives a definition, its signature, its calls and the legacy rows the same
    order. No MIR change.
  - `#LibraryImport` (SPEC 22.6, IMPL 21.4.7; no plan unit named the row, `ForeignEmissionTest`'s rows): a Fn callee without a target
    that is one of `Binding.LibraryImports` is called by its C signature (`Abi.Of` under the C convention), named by
    `LlvmModuleText.ExternalName`, one signature per symbol, computed once per Write as `Signature` is, and its call requests the
    declaration (`ModuleCodegen.Import`, design 2's request form, R5-M87), which the frame writes once per symbol through
    `WriteExternals`, skipping the names the runtime declares. Any other Fn callee without a target is `InternalInvariant_Kd`.
  - Departure (for the owner's departures list at R5's group review): the old generator also declares a program's own imports that no
    call reaches (a Library import only when used); the module declares what its definitions call, so an uncalled import has no
    declaration, and `ForeignEmissionTest.PointerSignaturesDeclareOpaquePointers` and `RebindingWithoutTheImportForgetsItsDeclaration`,
    which assert one, stay pending; declaring every validated import would add the Library's `QueryPerformance*` imports to every
    module (`TimeLibraryTest.UnusedClockHasNoNativeReferencesOrInitializer`).
  - Restructure: `FunctionCodegen.Rvalues.cs` was restructured in this unit (bp2, `FunctionCodegen.Aggregates.cs`), not by C5a, because
    the string record's store changed. Header correction: TypeToken constants are intrinsic operands (C6a's TypeTest, C9a), not
    C3a's. R5-M87's native multi-view body from `tests/mir/cases.kimi` is `CallViews`.
  - Left as located `Unsupported_Kd`: a requirement's call and a caller-location callee (C3b); a value's call and FnItem constants
    (C5a); a virtual slot (C6c); an intrinsic (C8a, C10a); the Builtin shims (C8f, C9b); property witnesses (C3c). A Fn call reaches a
    Builtin shim today only as the target of a compiler function with a legacy row (`Text.toString`, R5-M70), which that row records
    (C9a), and a property witness only as a requirement call's target (C3b): no source reaches the Fn arm of either, so no test does.
  - Rejected: a signature from the call's operand Types (a second copy of the frame's rule; their Origins differ); the MIR builder
    securing every Copy argument of a slot Type (a held builder change for an ABI concern); passing a Copy's source address (wrong code
    under `f(s, s@uniq)`); a MIR member or a clause in `LayOut` for a template's order (the body kind is the published fact); the old
    generator's split between the program's and the Library's imports (a consumer deciding by a declaration's source what a call decides).
- **R5-M90 A region is entered only at its first location** (2026-10-21, R5 planning session; plan AR-9 in W1, R4's ask (ii) of
  the 2e-D package §7 under R4-M90, a prerequisite of G:A5-F9's dev merge) Rule (`MirValidator.Blocks.cs`, `MirViolation.Entry`):
  a region is the body (scope 0) or an Inlined scope's locations, a default's copy (R4-M69). An edge, from a location to the next
  in its block or a terminator's runtime or check edge to a block's first location (MIR has no other edge), enters each region that
  holds its target but not its source, and the body's entry enters each region holding (0, 0). Every edge entering a region goes to
  one location, its first, and no edge from inside the region targets the block that location lies in; a finding is
  `InternalInvariant_Kd` at the edge's source. For the body, region 0, the second clause is §5.7's "the template's block 0 has no
  predecessor" (self-edges included); a copy's first location is where `Inline` continues the caller's current block with its
  template's block 0, after the pending slot's StorageLive, so it is entered only there, dominates the copy and is its first location
  in any reverse postorder (§5.3's pre-pass), and the copy never returns into that block (the caller's image of a predecessor of the
  template's block 0, which would also re-run the caller's statements before the copy). Reason (§5.7): A5's copy disposition (D11)
  gathers Entry(B) at a copy's entry and rests on the copy's correspondence to its template; an edge entering elsewhere would carry
  Loans the gather misses (an unsound omission), and an edge back into the entry's block would over-include (extra records).
  `Inline` keeps both, but nothing validated them. The rule is wider than §5.7's template wording: it holds for every body, since
  the body is region 0 and the builder targets no entry block anywhere, so one rule states both clauses with no body-kind branch.
  The validator walks the raw scope table, building its own region tree, rather than `MirBody.Common` (2e-4) or `Inlining`: it is
  the one owner that checks that table and must not rest on a query that trusts validated IR, so 2e-4's query is no second copy of
  this rule. A scope chain that does not lead out or a location outside the scopes, each a `Scope` finding, skips this walk alone
  (`Checker.Unscoped`); every other rule still reports. Census: no finding in the library, the 44 milestones, the `tests/mir`
  goldens, the SPEC variants, a probe of nested copies (depth 3, in a `while` condition and a `loop`) and the suite under the default
  pipeline and `-Pipeline mir` (4.48 million body validations, 28,625 copies, 1,492 templates). Cost: one pass over the locations and
  edges where the body has a copy and one over the edges, scratch per scope; warm 0 B. Rejected: a block-0 clause for
  DefaultTemplate bodies only (a body-kind branch for a fact every body holds); the first location by layout, the least block and
  index in the region (a fact of block allocation, not of control flow); validating templates only and trusting the fold (A5 reads
  the caller's MIR; a fold defect would go unseen); publishing each copy's entry in `MirScope` (a new column the edges determine);
  A5 checking the shape in its pre-pass (a consumer revalidating producer IR).
- **R5-M91 The owner confirms C2b's and C3a's departures** (owner, 2026-10-21, on the R5 planning session's explanation; verbatim
  「2件とも了承します。」; the consequences are the planning session's) The two departures from owner-approved texts recorded for the
  owner's list since R5-M86 are confirmed: C2b's PairAcquire written as a Copy of its Place in the item's case Type without asking
  `MirCases.Reborrows` (R5-M87; against design 2 §2.2's dispatch sentence; both cases write the same bits, IMPL 21.1.3), and C3a's
  module declaring only the `#LibraryImport`s its definitions call (R5-M89; against the old generator, which also declares a
  program's own uncalled imports, while declaring every validated import would add the Library's `QueryPerformance*` imports to every
  module; `ForeignEmissionTest`'s two declaration rows stay pending, and by R5-M85 and R5-M6's class (d) G3 reduces them). They leave
  the list; R5's group reviews check the code against those entries.
- **R5-M92 The raw memory and failure rows of MIR code generation and the intrinsic writer column** (2026-10-21, C lane; plan C8a,
  package C 3.2 and §2 rows 2 and 24, SPEC 5.1, 5.3, 5.6, 17.4, 21.4.3, 22.2 and 22.5.2; the planning session's decisions on C8a's design;
  under R5-M18, R5-M65, R5-M76, R5-M80 and R5-M89)
  - Writer column (R5-M18's approved column, which R5-M65 staged to the first unit writing an intrinsic call): `IntrinsicRows` is one
    row array in `IntrinsicId` order, checked once, each row with a closed `IntrinsicWriter` and its runtime `Routine`; code generation
    switches over the writer, never over `IntrinsicId`. Rows: Routine (RawRelease's `__kimi_free`, Abort's `__kimi_abort_message`,
    noreturn) through the one call writer (`Invoke`), whose parameter loop now passes the call's location for the Location kinds, so the
    console writeLine row is an `Invoke` too (its own loop deleted); TryAllocate (tryAllocateBytes, the same, its definition requested);
    Allocate (Raw.allocate: the count, the element's stride from `Layout.Of`, the location); Fail (`__kimi_abort` with the reason
    `AbortReasons.Of` gives the code MIR carries, its one operand, and no OS error, `RuntimeAbi.NoOsError`, the runtime's -2, which
    `LlvmScalarText`'s check failures read too, its own copy deleted; the runtime template's and the old Emission's literals stay, D5;
    a code outside the table is `InternalInvariant_Kd`); Initialize (the
    value `Put` at the storage, R5-M87's copy form); Slice (the storage and length put at the Slice record's Fields 0 and 1, the Unit
    backing ignored); Copy (transferBytes as `llvm.memcpy`: R5-M18 reversed PD23, the declaration rules out overlap, so `MoveBytes`'
    comment is corrected). Every other row is `Unwritten`, its unit named in the table: C4a DestroyValues; C6a MakeObj, MakeRc, MakeArc,
    Clone, TypeTest; C7 StaticEnsure; C8d Capacity; C9a WriteStdout; C9b FormatFloat, ErasedWriter, ErasedReserve, FormatInteger128; C9c
    the format buffer rows; C10a and C10b the test rows. Stride and ReinterpretLoan have no MIR producer yet (no compiler function's
    disposition serves them; an open PLAN row), so their rows are written with their first producer. Allocate and Fail call through
    `Invoke`, which takes the values of a routine's slot-less parameters (the stride; the reason and the OS error) in order, the runtime
    Abort's location parameters being its Location kinds; an `IntrinsicRow` and the operator table's `Entry` state every column but the
    unary operand (ARCH: at most two optional parameters).
  - The nullable Alloc (decision 2C; package C row 2): `__kimi_try_allocate`'s definition is the runtime's, `LlvmModuleText.TryAllocate`,
    its marker spliced from `RuntimeAbi.TryAllocateBytes` by the checked runtime splice and defined in a module only where a body calls it,
    by both generators: the old writer's storage byte primitives write it, byte-identical (e1025e1b), and MIR code generation requests it
    (`ModuleCodegen.Needs.TryAllocate`). Not inline, a second copy, nor `LegacyRuntimeHelpers`' (R8-transitional). G151 closes: RawSlice's
    permanent writer is the Slice row; `WriteRawSlice` remains the old generator's and goes with it.
  - Raw pointers (staged here by R5-M76 and R5-M80): the operator table's sixth column, `==` and `!=` as `icmp` on `ptr`, `p + n` and
    `p - n` a displacement entry (`mul` by the pointee's stride with its sign, then the place writer's `Offset`; a zero stride keeps the
    pointer, null included; unchecked, SPEC 5.3); Binding admits no other pointer operator. `null` is a constant (`Value` form `n`), which
    R5-M80's null Cast and comparisons read.
  - Layout invariant (decision on the alignment check): no Layout alignment exceeds 16, the heap's (SPEC 21.4.3): every row is at most 16
    (the primitives', a borrow's, a raw pointer's, a handle's, a function value's, the sequence handles') and a placement takes its parts'
    maximum (IMPL 21.1.2), C mode too; Layout's contract states it. Raw.allocate's row relies on it in place of the old
    generator's refusal of a larger alignment (`BodyLowering.Raw.cs:47`, unreachable on windows-x64-v1), and
    `LayoutTest.NoLayoutAlignmentExceedsTheHeapAlignment` pins it at Layout's layer. A profile whose Layout could exceed it records SPEC
    5.6's Unsupported at compile time.
  - Locations: Abort, Fail and the routines report `Location(call.Source)`; in a caller-location body that stays a record until C3b
    (R5-M93). The copy slots' condition (R5-M89) is met by narrowing the scan: the operands a Call passes to an OwnedSlot parameter, every
    in-memory operand of a body callee (a slot Type is passed by pointer) and an intrinsic's only through its row's routine, so Abort's
    constant message has one and a Fail's code or Raw.initialize's value none.
  - Copy slots are spelled `%s<k>`, superseding R5-M89's `%c<k>`: a check's success label is `c<id>`, so a body with both could define
    one name twice (invalid IR in `CopyOperationValues` and the `InheritedPlans*` fixtures, which C8a's rows made generable); the
    regression is `FunctionCodegenTest.CopySlotsAndCheckLabelsNeverShareAName`.
  - Restructures first, behavior unchanged: `IntrinsicRows.cs` as the indexed table, `LlvmModuleText.cs`' spellings split out,
    `LegacyRuntimeHelpers`' storage byte primitives moved to `.Storage.cs` (d063f5ad); `LlvmScalarText`'s conversions moved to
    `.Conversions.cs` before its abort writer reads `RuntimeAbi.NoOsError`. `RuntimeAbi.cs`'s due restructure is a keep (the planning
    session's ruling, as R4's 2x-R10 for `MirLoanSets.cs`): it has one responsibility, and its routines read its representations at
    static initialization, whose order a partial split would leave unspecified; it gains the routines' operand slots and Location kinds,
    which no generated text reads (e1025e1b and the review fold).
  - Rejected: a switch over `IntrinsicId` in code generation (a second table of the vocabulary); `memmove` (R5-M18); an inline nullable
    Alloc or `LegacyRuntimeHelpers`' text (above); a code column in `IntrinsicRows` for Fail (it would copy `IntrinsicDisposition`'s codes);
    copying the old 16-byte check (the invariant above makes it dead code).

- **R5-M93 The requirement calls and the caller location of MIR code generation** (2026-10-21, C lane; plan C3b, package C 3.2 I5 and
  I8, SPEC 8.6.3-8.6.4 and 22.5.1, IMPL 21.3.1; the planning session's decisions on C3b's design; under R5-M61, R5-M67, R5-M70, R5-M89 and
  R5-M92)
  - Requirement row: a Requirement callee is called by the collector's target at the call, as a Fn callee is (`FunctionCodegen.Function`):
    a Body witness (a conformance's, an inherited one, a generic Type's instance, one through a bound Contract reference, an accessor's
    Getter or Setter, a fixed array's entry member, Position's i32 specialization) under its own signature (`ModuleCodegen.Signature`),
    its operands in its Arg-local order (`ArgumentPosition`): SPEC 8.6.3's compatibility inserts no conversion, so the witness's
    signature is the call's. A Builtin shim stays C8f's and C9b's and a property witness C3c's, each a located `Unsupported_Kd`, now
    reached. A requirement call without a target is a located `Unsupported_Kd` (decision 3): a bodiless witness has none (R5-M67; PLAN
    G157 (10), FixedBuffer's and HeapBuffer's `reserve`); a Fn callee without one stays a `#LibraryImport` function or
    `InternalInvariant_Kd`.
  - Caller location (SPEC 22.5.1): `Location(source, out Site site)` is the one rule and `Site` its result, a new representation: IMPL
    21.3.1's pair of the text's address and its byte length, two `Value`s. In a caller-location body (`FunctionAbi.CallerLocation`) it is
    the body's own last two parameters (Abi.Of), which it forwards, so every location it reports is the one it received: a check's Abort
    (`Failure`: the operators, the conversions, an Index's bounds), a glue call, a runtime routine's or legacy row's location parameters
    and a caller-location callee's; elsewhere it is the site's pooled Location constant. `Invoke` keeps R5-M92's location kinds and passes
    the `Site`; C3a's records for a caller-location callee and for a caller-location body's locations go. The old generator does the same
    (`EmissionOperandKind.CallerLocation`), so OQ7 expects no deviation.
  - Parameter spelling (decision 2, A): a parameter `Value` (prefix `p`) is a physical index into the function's `FunctionAbi`, spelled by
    its `AbiParameter.Name`, so the prologue, a pointer-passed argument's and the result slot's home (one `Parameter` home), a glue
    function's arguments and the forwarded location read Abi.Of's and DropGlue's names instead of a second copy (`%a<slot>`, `%ret`).
    `Spelled` writes a named value (a pooled constant, a parameter) into text, shared by `WriteValue` and `Failure`.
  - Restructure first (b2194242, behavior unchanged): `FunctionCodegen.Places.cs` keeps the place writer and `FunctionCodegen.Stores.cs`
    takes the loads, stores and copies through it; the parameter spelling. C3b does not change `ModuleCodegen.Frame.cs`. After C3b,
    `FunctionCodegen.cs`, `.Calls.cs`, `.Frame.cs`, `.Arithmetic.cs` and `.Casts.cs` have had three consecutive units, so the next unit
    changing one restructures it first.
  - MIR's Location constants are TestObserve's operands (C10a's row), not C3b's. Requirement calls use the copy slots (R5-M89's condition,
    met by R5-M92's narrowed scan).
  - Rejected: a "forwarded" sentinel that each location writer tests (a sentinel and a branch per consumer); hidden location operands
    from the MIR builder (a held builder change, where package C names them code generation's); `%a<slot>` kept with a forwarded form of
    its own (option B, a second naming rule).
