# Milestone27:100–101 under A5 1b-2 (analysis for an O3 decision)

Date: 2026-10-17. Base: `claude/a5-1b-core` 93d103c1. Analyst: a subagent (read-only; probes in a throwaway worktree,
removed). Evidence (planning scratchpad, not tracked): the probe test and output, `m27.rs` (rustc 1.97.1), the prototype
variant `ViewElem` (255 cases). (H) marks an unverified claim.

## Program and Loan path

`Milestone27.kimi:98-102`: `var references: [_ of ref/i32] = [number@ref]`;
`let retainedReferences = references.sliceUniq().slice().toArray()`; `references = [copied]`;
`require retainedReferences[0]@follow == 42`. Retains rows: `FixedArray.sliceUniq` (0, Value, result), (0, Content, 0);
`UniqSlice.slice(self: ref/Self) -> Slice<T> during self` (0, Value, result); `Slice.toArray(self: Self) -> Array<T>`
(0, Value, result). In MIR the temporary UniqSlice `_6` carries its own uniq Loan L1 on `references` beside the element
Loan L0; the Slice `_5` carries L2 (its borrow of `_6`), L1 and L0; `toArray`'s result carries all three.

## Two causes

1. **Defect (fix needed under every option).** A Slice passed by value is not a borrow input for retention
   (`Binding.Retention.cs:115`: `IsBorrow` covers Semantics layers only; `MirBorrows.Flow.cs:427` reads through only
   `MirTypes.IsReference`). So `toArray` keeps the Slice's outer borrow, against SPEC §4.8.2 ("keeps the dependencies
   inside `T` but not the outer borrow"); it also rejects `references.slice().toArray(); references = [copied]` and the
   xUnit case `SliceToArrayTest.cs:21` "References". Retention must classify Slice and UniqSlice inputs as borrow layers
   (their outer Origin → Value, element Origins → Content), as A3 E6 classifies their Index.
2. **P4 (approved loss, A3 §2).** With (1) fixed, a read through the Slice reaches the holder `_6`, whose one set holds
   both its own outer Loan L1 and the elements' L0 (SPEC 15.6: a holder carries the Loans of every part; 15.6.4: retention
   per argument, "whichever of those Origins it names"), so the copy keeps L1 and `references = [copied]` conflicts. This
   is SPEC's literal verdict since 4372b058 (whose "no verdict changes" claim was wrong for this line); P4's census row
   (milestones 0) missed it. rustc accepts the program and its variants.

## Census

The view-of-a-view shape: library 0, milestones 1 (M27:100–101), xUnit 0 (H). The Slice-by-value defect: xUnit 1 verified.

## Options

- (a) Re-spell M27:100 as `references.slice().toArray()` (SPEC-valid; same output; the section's intent kept), recorded
  under the milestones README's specification re-spellings; needs defect (1) fixed first.
- (b) Refinement "a view holder stores no elements" (an element read through a direct Loan naming a Slice/UniqSlice
  holder contributes what that holder's own Loans reach; every store into a view holder also stores into what its direct
  Loans name): prototype 255 cases, 0 unsound, 0 unlisted, recovers 130–130d; both halves are needed (dropping either is
  unsound on 131/132). Needs a SPEC revision narrowing P4 and the full process; ≈ +60–90 lines (H).
- (c) (a) plus records: correct A3 §2's P4 census, note 4372b058's claim, optionally a §4.8.2 example.

Analyst's recommendation: (a) + (c), defect (1) fixed in step 1; (b) recorded as an open precision candidate.
