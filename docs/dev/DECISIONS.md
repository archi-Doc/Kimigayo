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
- **R4-M3a** (2026-10-12, planning session; design B's record taken into design A; supersedes R4-M3's fields).
  `MirProblem(DiagnosticCode Code, SourceRef Subject, ulong Views)`; `MirReport` merges by (Code, Subject), the canonical
  `Problem` key, OR-ing Views into the first problem, until U11 adds the Reason fact and Reason joins the key (R4-M11's
  (code, subject, Reason)); a merge keeps the first problem's facts. Construction records keep their own merge in
  `MirProgram` until R6 unit a folds them into `MirReport` (stage ledger).
  No Requirement: Mir may not name the legacy `OwnershipFailure` (KR1001), and moving that enum into a shared area would
  make old-path vocabulary a shared concept (this naming clause is superseded by R5-M24: Diagnostics owns the kinds as
  `OwnershipRequirement`); R6 unit a derives the published requirement from the code and the facts (this
  derivation clause is superseded by R5-M23: each recording rule names the requirement; revised ask A-5). No
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
  below) R4's remaining scope
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
    R5-M9.
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
  CS_call over live_in(ℓ) superseded) A5's Loan stratum, its joint core. `MirBorrows` (`MirBorrows.cs`, the pre-pass;
  `.Liveness`, `.Flow`, `.Checks`) checks a body per Semantics case view, once for a body no view decides (as A1), and reports each
  conflict as `ComparisonLoanConflict_Kd` at its access with the Loan's anchor and its witness as facts: the witness holder's next use
  in its chain, or the store whose value carries the Loan. `MirReport` gains pooled fact rows per problem (`MirFact`, kinds `Anchor`,
  `Use`, `Store`; a merge keeps the first problem's, R4-M3a); A6's code mapping is unit 1c's. The choices within A3:
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
    Temporary: a default template's call is an unknown callee until F9 (unit 2e), and a Value callee's environment (its value, or
    what a reference to it refers to) reaches the result until F7 reads `TyCtxt.Closure` (unit 2c).
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
    located `Unsupported_Kd` at the call in a body view with a Loan, as A4's Limit; it retains nothing, with no fallback rule.
  Found: a value returned from a guard (A3's 108) is Binding's `UnsatisfiedOriginRelation_Kd`, so its MIR is Opaque and the
  stratum sees no escape (the Origin stratum's, unit 2a). Rejected: the Loan-based err(ended) (prototype round 10's `EndedByLoan`,
  unsound on 43a, 92g, 102 and 108: past the Subject's shared Deref the candidate forms no Loan, E3); a γ id table per candidate
  (the local index is the key).
- **R4-M61** (2026-10-17; PLAN G96, two independent designs compared and the chosen one reviewed independently, implemented on the
  planning session's instruction of the same day; supersedes R4-M42's `MirBuilder.Broken`) A published Op guarantees the Binding
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
  (the planning session: the published Type alone decides, through the positions a literal builds).
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
  leaves it open). Not temporary.
- **R4-M63** (2026-10-18; A3 §12 E14, decided by the planning session on the core lane's group review B4 and N2 and its design review
  of 2026-10-18; supersedes R4-M57's intrinsic rule and its separate unknown-callee loop, and R4-M40's "every operand to the result")
  A Call retains as its callee's contract says (SPEC 15.6, 15.6.4).
  - An Intrinsic `MirCallee`'s `Symbol` is the library declaration of the compiler function it serves, under its Subst, whose
    parameters its first operands pass (a written call's target, `Serve`'s function); it is null for an intrinsic without one,
    those the builder synthesizes and the compiler-only `$abort` (SPEC 17.4), whose results hold no Loan. `MirValidator` reports
    `Callee` for a Symbol that is no library declaration of a compiler function the intrinsic serves; that the table's arity covers
    the declaration's parameters is a table fact (`MirValidatorTest`).
  - A5 applies every row set through one loop, by operand position (`MirBorrows.Rows`, a reused scratch): `TyCtxt.Retains` over the
    declaration and Subst, an intrinsic's included; the intrinsic table's `Retained` column, the operand the builder adds beyond a
    declaration that the result retains (Value; `RawSlice`'s backing only); a default template's call the unknown-callee rows until
    F9 (2e): every input to the result (Value) and to each exclusive input's referents, untied. A store row into a raw pointer reaches
    E by the store rule, so `Raw.initialize(p, x@ref)` stores `L_x` into E: a later `x = 2` errs, and so does x's end unless a write
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
    Based, Seeded or Valued passes found in a holder other than t (the first two such holders are kept).
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

Units of the BU planning session (PLAN G129, G130 and the retention gap found by R4's E14 implementer), run in parallel with R4 and
R5 on the owner's request of 2026-10-18.

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

## R5 and R6 (adopted 2026-10-18)

The owner's answers to the plan's questions OQ1–OQ17, given on 2026-10-18 and relayed by the R4 planning session. "Plan"
is the [R5 and R6 Implementation Plan](../../draft/Proposals/2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md); its
unit and decision names (S1, E2, PD12) are used as there. Each planning-session decision PD1–PD32, component and temporary
structure is recorded here by the unit that first implements it.

- **R5-M1 Adoption** (owner, 2026-10-18; OQ1, OQ4, OQ10, OQ11 and the start) The plan is the formal plan for R5 (MIR code
  generation, shadow), R6-gen and R6-check: its units (§4), order (§3), metrics and stop rules (§5.1–§5.2, R5-M8) and three
  exit gates (§5.3–§5.5), which replace PLAN10 §12.2's R5 exit and give R6-gen and R6-check the gates R4-M35 left out. R5
  runs now, in parallel with R4 (OD4 stands), in its own planning session. The design
  packages S2–S4 (OQ4), Q8 (OQ10) and H0 (OQ11) each go to the owner once, after their design review, and are approved
  then, not in advance; each approval makes the package the owner-approved design of DEC Q6 forms 1 and 2 for the units and
  groups it names. The S2–S4 summary highlights PD3 (no `MirBodyKind.Shim`; shims are Backend mono items) and PD11
  (temporary codegen coverage rows reporting `Unsupported_Kd` under sel, outside the three reporters ARCH "Closed
  vocabularies" names, until X2), which depart from owner-approved texts. Rejected: approving the packages before their review.
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
  R5-M30.
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
    intrinsic rows' `Target` column, `Layout.Of`'s OK/Limit/CLayout result and its Never rows, the chapter-F facts (F-package §5),
    `TyCtxt.Witness`'s `(Kind, Target, Subst, Access)`. Each component, representation and temporary structure is still recorded by
    the unit that first implements it.
  - Temporary sel-only behaviors: K10b's double report of a written by-value C-layout instance until G2b (grouping, which SPEC
    23.1.2 leaves implementation-defined), and a generation-gate refusal that may hide independent coverage gaps until the R5
    plan's unit X2 (a temporary SPEC 23.1.2 limit).
  - Acceptance changes, from G2: collector records publish at `check` (R5-M4); K10b's `InvalidCLayout_Kd` (Language) at check for
    inferred C-layout instances; K10a's layout-depth limit at check; at G2b written borrow-only C-layout instances become valid (an
    (a) correction). Facts recorded: R5-M13(b)'s premise is corrected (the old analysis also accepts immutable static scalars in
    generic containers' groups); "no representation change" holds for the slot state only: P42's generic storage keys need the
    container's Type arguments on each access's `staticEnsure` operand, and since the static constant fold keeps no slot state, P42
    narrows the fold or restores the state for SPEC 22.4.3's shutdown rule.
  - Rejected: the alternatives each decision row names (`S2-S4-decisions.md`); for X33, (b) a constant-fold whitelist (a KD1
    exception) and (c) carrying Type arguments before G2 (MIR representation work in W1 for a form no library or milestone uses);
    for the successor code, reusing `ProjectPreparationFailed_Kd`, recategorizing `GenerationFailed_Kd`, or
    `MissingNativeRequirement_Kd` for the missing-supply cause only.
- **R5-M19 `Layout`** (2026-10-18, planning session; plan P7a, package §3.5 and §7, S2-S4 X18, X31, X34 and O9, under R5-M18;
  promoted under R5-M14) A permanent Backend component that owns the storage layout of a closed Type for every generator:
  `Layout.Of(BoundType)` returns its `ValueLowering` (size, alignment, stride, Field offsets, LLVM representation). P7a's rows:
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
  depends on more than the Type's kind; placement brings it); `Loan<T>` as Unit's record (P7b's placement would then change the
  old pool's `Loan<T>` record and argument slot); a separate Never test in the old pool beside the gate.
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
  - Departure (temporary): a specialization stops the walk, so it and everything nested in it count as closed, although SPEC
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
  will call the core. Refused is unreachable today: conformance verification rejects an inherited witness with an exclusive or
  object receiver first (`ProjectedReceiverProof`; R1g), and no test exercises it. Rejected: built-in targets created lazily by
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
  F2b and F2c apply the rule to library imports and the legacy Dictionary rows' callees. Rejected: a module ordinal column (plan
  row F2a; always 0); replacing `Function` by `Symbol` now (for the Implicit body a reader would derive the Koto from `Kind`,
  not from the symbol).
- **R5-M26 `AdtDef.IsCLayout`** (2026-10-18, planning session; plan F1, package F §4.9 and §9, S2-S4 X18 and X26, under R5-M18)
  Binding's published fact that a struct's C layout validated (SPEC 21.1). Producer: `ValidateLayoutFragments`
  (`Binding/Binding.Attributes.cs`), true exactly for a struct whose selected mode is `#Layout("C")` with valid fragments and that
  is neither open, derived nor empty. It is written true or false for every struct in every pass; on a first pass the shape is
  created there (`Adt ??= new()`) and `PrepareStorage` reuses it, and no reader in between distinguishes an empty shape from a
  missing one. Reader: `ValidateCLayoutFields`, which filters the storage declarations by it; the pass-local `cLayouts` list, a
  second representation, is deleted. Readers that still use the attributes: `Binding.Types`, which queues
  `CheckCLayoutInstance` by the attributes (`HasCLayout`) whenever it binds a written generic C-layout instance, in headers and
  bodies alike, until G2b; `CExchangeable` in `ValidateLibraryImports`, which runs before `PrepareStorage` (PLAN G132). First
  consumer outside Binding: P7b's `Layout.Of`, the one owner of the zero-sized C-layout rule (X18), which deletes the old
  Emission's attribute walk and `Open` re-check. Rejected: a C-layout instance view in `TyCtxt.Instances` (X26); keeping
  `cLayouts` beside the fact.
- **R5-M27 Legacy Dictionary helper rows** (2026-10-18, planning session; plan P1c and its follow-up P1c2, PD2, S2-S4 X4;
  refines R5-M16; refined by R5-M28 and by R5-M30, which adds the formatting rows and the `HelperNeeds` member
  `FormattingRuntime`) `DictionaryHelperRow` is one static row per `DictionaryHelperKind` (CheckKey, Place, Drop), read by
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
- **R5-M33 A diverging Call's destination is read only where the checks continue** (2026-10-18, planning session; plan F11,
  PLAN G131, C-package §7 row F11 and row 33, S2-S4 X29, X30 and X30′, owner-approved with R5-M18) Rule, in `MirValidator` (the
  producer's one validator; `MirViolation.Diverged`): a Call without a runtime successor writes no destination (`MirUses`), so a
  Temp whose only writes are diverging Calls' destinations holds no value at run time, and only check-only blocks, which no
  runtime edge reaches from the entry (`MirReach` without CheckTarget edges), read, move, borrow or drop it. Any other place is
  valid wherever it is read: a Never-typed parameter, Field or payload (SPEC 3.1.3, 14.9.3), a Var or the return local, and a
  Temp also written on another path (an `if`'s arms sharing a destination). A runtime read would be lowered as a read of storage
  nothing initialized; R5 relies on the rule from C0b1 and C2b, which lower Never temps. The Checker records each local's writes
  while it walks a body (a diverging destination, which `MirUses` does not report, and any write it reports); only a body with
  such a Temp walks its runtime blocks once more. Neither reach rule (Unreachable, Diverged) runs on a body some location of which
  `MirUses` cannot walk; `Unreachable` formerly ran when only the successors were in range. The rule finds nothing in the
  library, the milestones, the corpus, the goldens or the whole suite under the mir pipeline: no builder fault. Rejected: a Never
  temp read only in check-only blocks (X29: it rejects valid reads of Never parameters and payloads); "the destination of a
  diverging Call" (X30: it rejects an `if`'s arms sharing a destination) and "the builder's unwritten temp" (it misses an
  ordinary call-result temp, `_4 = stop() -> ! check bb1`); a MIR representation of the fact (only the validator reads it).
