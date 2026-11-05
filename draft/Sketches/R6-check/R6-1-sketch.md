# R6-1: A3's require fallthrough at a recovery's guess (H3b round 1; a design sketch)

R5's sketch for R4 (A3 and the builder), BU (Binding's guess rule) and R5 (the publisher). Not authority: each owner's design decides
its part, and one review covers the unit (Q13).

## The row

H3b round 1 (`H3b-classification.md`, ruling 2) found that at the flip head, A3's `RequireFallthrough_Kd` publishes direct where the old
control-flow check derived it. The rows affected are:

- T4A: `RequireRecoveryDiagnosticTest.AGuessedFailureBodyRestsOnTheSyntaxError` ×6;
- S3: 7 `syntax.json` cases and the mutation `symbolic-and`.

In each, the parser supplied the failure body, or a recovery guessed what its completion reads. DIAGNOSTICS §4.3 ("Later phases"): "A
completion check rests on the bodies the parser supplied for missing ones … the Error is derived from their keys." Before the flip,
`ControlFlowAnalysis.GuessedBy` asked `Binding.GuessedCompletion(node, node)` and derived the record. At the head, A3 cannot see the
guess: it must not read syntax, and nothing in MIR records the guess.

T63's `AFailureBodyInARejectedExpressionIsJudgedOnItsOwn` is a separate cause, so it is not part of this sketch. There the head reports
no `RequireFallthrough_Kd` at all beside `TypeMismatch_Kd`, which is a lost independent record. R4 reads it with the T63 rows.

## Where each decision belongs

- **Binding** owns the guess rule (`GuessedCompletion`, `IsGuess`). It publishes the guessed nodes, not only their keys.
- **The MIR builder** reads Binding as it reads every Binding fact. It records the guessed nodes of a require's failure body on its
  `MustNotComplete` row.
- **A3** copies the row's guessed nodes into the problem as `Cause` facts with Value −1, the Binding-cause form (MirFact, H3-2). It
  still reads no syntax. MirReport's rule makes the problem derived.
- **The publisher's** Binding-cause arm (`Compilation.Publication.cs`, R5-M184) names the causes Binding gives for each site.

## Proposal

1. **Binding (BU).** Split `IsGuess` into two parts:
   - a node predicate;
   - one per-node cause query, `RestingCauses(node)`. For a supplied body, it returns the recovery's cause (`CodeContext.RecoveryCause`);
     otherwise it returns `FailureCauses(node)`.

   `GuessedCompletion` collects the guessed nodes, and its key form maps them through `RestingCauses`, so the lints' callers are
   unchanged. A node form, `GuessedCompletionNodes(node, root)`, serves the builder. Each fact has one owner, and the guess rule stays
   one rule.
2. **Builder (R4).** `MirBuilder.Control.cs:96` already reads `require.ElseBody`. It adds the guessed nodes of
   `GuessedCompletionNodes(require.ElseBody, require.ElseBody)` to the row. The row needs a site range, or the body's fact table gains
   the sites; R4 decides the representation. A default copy's rows are not copied (`MirBuilder.Defaults.Copies.cs:92`), so a copy needs
   nothing.
3. **A3 (R4).** `MirLints.Run` adds the row's sites as `Cause` facts (Value −1, Site = the guessed node). The header's "must not decide
   that a problem is derived" still holds: the builder publishes the fact, and MirReport's rule derives.
4. **Publisher (R5).** The Binding-cause arm calls `Binding.RestingCauses(site)` instead of `FailureCauses(site)`. For a failed node
   both give the same causes, so 2g-1's later rows are unaffected. A site with no causes stays a broken contract.

## Tests and size

- The T4A and S3 rows pass again unchanged: no test is re-pointed and no expectation weakened.
- Fail-first rows:
  - in `MirLintsTest` (or the class that tests A3), a guessed failure body's problem carries a −1 Cause at the supplied body;
  - in `MirPublicationTest`, a Binding cause at a supplied body publishes `PrerequisiteUnavailable_Kd` with the syntax key.
- An A3 warm run still allocates nothing.
- Size (H): Binding ±20, builder +10, A3 +5, publisher ±2, tests +30.

## Landing

This is a dev unit (§5.5: "each fix is a dev unit"). It is verdict-neutral before the flip, because A3's problems publish only from H5.
Its MIR-side rows are tested on dev; its published form is tested on the branch after it merges dev. Steps:

- One design review by an agent who wrote neither part.
- R4 implements the builder and A3; BU implements the Binding split under RP17. One unit, one commit per owner, one merge in R5's window.

## Revision 1: the planning session's rulings on the review (2026-11-03)

The one review (`C:\Users\bwff1\AppData\Local\Temp\claude\r5\h3b\r6-1-review.md`) returned REVISE with B1-B3. The cause analysis and the
split stand. The same three authors fix their parts, and the same reviewer re-checks once (Q13).

- **B1 (BU).** `RestingCauses` is defined on the guess rule's domain, never as a catch-all:
  - a supplied body gives its recovery cause;
  - any other node gives `FailureCauses(node)`;
  - an expression the guess rule counts as unresolved (IsGuess's second branch) falls back to the unresolved mark when it has no
    failure causes, as the lints do today;
  - any other node returns null.

  So a −1 cause at a node Binding resolved still reaches the publisher's broken-contract arm. `MirPublicationTest`'s
  `ABindingCauseIsBindingsFailureCauses` and the three `sited: false` rows stay green, unchanged. `IsGuess` and `RestingCauses` share
  their two predicates, each written once (N3). The publisher's arm asks `RestingCauses(site)`, and that is its only change (R5).
- **B2 (R4, with R5's record).** `Binding.GuessedCompletionNodes` becomes a new published Binding view for Mir/Build, which ARCH allows
  "with a reason in its commit". The reason: the guess rule reads the pass's completion structure and Never evidence, which no HIR
  column carries. Publishing every require's guess into the HIR would run the walk for requires that never fall through.

  The unit updates three places:
  - ARCH's dependency row, naming the view;
  - `MirBuilder.cs`'s header, adding it to what the builder reads;
  - a DECISIONS entry. R5 drafts it, and it lands in the commit that first implements the view and `MirBody.GuessedSites` (N2).
- **B3 (BU, R4).** The view allocates nothing:
  - BU: it fills a caller-supplied pooled list, or returns a span over Binding's scratch that is valid until the next call;
  - R4: the builder copies the nodes into `MirBody.GuessedSites`, which is pooled with the body;
  - R4's fail-first rows include a warm rebuild of a program with a guessed failure body at 0 B (`MirBuildTest`).
- **Non-blocking items taken in the unit:**
  - N1: DIAGNOSTICS §4.3 and the headers of `MirLints.cs`, `Compilation.Publication.cs` and `MirValidator.cs` (Auto).
  - N4: BU's equality check also covers the supplied bodies that 2g-1 will site.
  - N5: two more rows, one for the unresolved-expression branch (`require a == 1 else => missing()`) and one on dev for a guessed site
    that differs from the subject.
  - N6: a scaling row for nested requires in an incomplete pass. If it shows quadratic cost, BU keeps the walk's per-node cache across
    queries within a pass.
  - N7: the commit names the mechanical edits in `MirValidatorTest` and `MirBorrowsTest.Rooted`, and the MIR dump and ledger rows that
    move.
- **Order.** BU's commit (restructuring and the view, snapshot 0) comes first on the unit's branch from dev. R4's builder and A3 commit
  follows. R5's publisher arm comes last. The unit is one merge in R5's window.
