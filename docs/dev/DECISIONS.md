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
  `DefaultArgument*_Kd`.
- **R4-M11** A1, A4's local facts and A5 run per `MirCases` view; A6 merges by (code, subject, Reason) with the view mask
  as the case fact. Views are never collapsed. The key is R4-M3a's until U11.
- **R4-M12** Library bodies are analyzed in every compilation. The library and the milestones produce no problems,
  except an (a) row whose library fix lands in the same unit. Suspended until the joint core lands (R4-M35).
- **R4-M13** Tests migrate per method kind (body presence, operations, cleanup verdicts, Loans, verification state, R5/R6
  residue). Ratchets: control-flow structure reads at most 1, `Ownership.Bodies` reads at most 110. The numbers are superseded
  by R4-M13a.
- **R4-M15** `OwnershipStorageLimit_Kd` stays the one Resource code, decided by the byte size of A5's Loan × point arena. The
  arena scope is superseded by R4-M15a.
- **R4-M16** The ledger's classification table is tracked (`tests/xUnitTest/TestData/mir-diff/classification.tsv`); raw
  rows, reproducers and ledgers are evidence under `artifacts/verify/mir-diff/`. R4-M35 adds a SPEC-clause column for (d) rows.
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
  backward clause superseded by R4-M34) MIR dataflow.
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
  R4-M6a (five lanes).
- **R4-M15a** (2026-10-12, planning session; supersedes R4-M15's "A5's arena" scope) `OwnershipStorageLimit_Kd` stays the one
  Resource code; it is decided at the one place that sizes analysis arenas, `MirDataflow.Prepare` (A1's state arena now, A5's
  Loan × point arena in U7), by byte size against the old path's limit value, 64 MiB (IMPL 21.3.2's text changes in R6). An
  over-limit body records the code once at the body's subject and A1 does not analyze it; the driver owns that skip once A2 and
  A5 read the arena (U5, U7). Reason: the dense arena is O(heads × paths/64), and a body bounded only by the 65,536-statement
  expansion limit could otherwise exhaust memory.
- **R4-M6a** (2026-10-12, main session; design A of two independent U5 designs, with design B's problem code; refines R4-M6's
  shape, supersedes R4-M21's "three lanes" and implements R4-M15a's driver skip for A2) Drop elaboration (A2). `MirDrops` judges
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
  U7 +68).
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
  step 0, owner approval of its §9 item 9 on 2026-10-17; supersedes R4-M20's union join and its backward clause) MIR dataflow, forward and
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
  OD8–OD11 and OD13's first list on 2026-10-17, as plan §10 records them; supersedes the parts named below) R4's remaining scope
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
    records. Not adopted: the root-most kill and relaxation (c). Where A3 departs from the plan (G3: no drop-row Path), A3 governs.
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
  Type holding itself in an Array is deep (PLAN G91). Rejected: `CarriesOriginOrSlot` as the test (builder-made references carry no
  Origin); a depth bound alone, as HoldsRaw has (a repeating recursive Origin-free Type would become a holder and keep the Loans read
  into it live); one fact for both (two readers, two rules). Not temporary.
- **R4-M37** (2026-10-17; design A3 §4.1 G5, G6, the writeback and the guard row, owner approval of its §9 item 8 on 2026-10-17) The
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
- **R4-M38** (2026-10-17; design A3 §4.2, owner approval of P5 and of its §9 item 8 on 2026-10-17) A4's static keys for A5.
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
