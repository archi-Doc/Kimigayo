# N2 SPEC session: probes behind DECISIONS R4-M131

Date: 2026-10-25. Base: dev `50adba54d` and `ac14e8fde`. The reviewer's throwaway prototype (`prototype.diff.txt`, plus mode AN
below) and its harness were applied, run and reverted; nothing was committed. Sources: `probes\g20-owning.kimi`,
`probes\g21-self.kimi`, with the reviewer's g1–g18 and prelude (`options.md` §3, `verdicts-rev.md`). Modes:
- `dev`: no prototype;
- `A2`: options §4 A;
- `A2+R′` (AR2): no unstated-layer rule behind a result borrow except the raw inputs;
- `AN`: AR2, plus the written-through input's chain (`kinds[j] ??= Content`), never its own value.

Later additions:
- `spec-review.md` is the SPEC edit's independent review, ACCEPT WITH FIXES; its probes are `probes\g30-transfer.kimi`.
- `prototype-at.diff.txt` is the whole throwaway Binding prototype: modes AR2, AN and AT, with an env-gated debug line. Apply it
  with `prototype.diff.txt`'s `Formatting.kimi` hunk.

## Owning iteration (the stop; answered by the owner's (i), then by the transfer rule)

An owning iterator's item is in the result, not behind a borrow (`ArrayOwningIterator<T>.next(self: uniq/Self) -> Option<T>`). So the
unstated-layer rule retains the receiver and the item keeps its Loan, against SPEC 22.2.3 and LIBRARY §2.1. R′ covers only
`iterate()`'s `ref/T during source` items.

| Program | dev | A2 | A2+R′ |
| --- | --- | --- | --- |
| ownRaw (two items of `Array<raw/u8>@move.intoIterator()` kept) | err | err | err |
| forMoveRaw (`for p in a@move => kept.append(p)`) | err | err | err |
| ownBox, forMoveBox (the same over `Array<Box>`, `Box { p: raw/u8 }`) | acc | err | err |
| ownInt (`Array<i32>`) | acc | acc | acc |
| removeRaw (`let x = a.remove(0); a.append(null); usep(x)`) | err | err | err |
| removeBox (the same over `Array<Box>`) | acc | err | err |

Every error is A5's `ComparisonLoanConflict_Kd`, from `next`'s (or `remove`'s) `0 Value result`, the receiver's Value.

## The written-through argument: two wordings

- **Verdicts.** AR2 ("every other argument") and AN agree on all 125 programs: the reviewer's 109, g20 and g21. B3's programs
  (gTOk, gRFOk, putOk, litTupOk, arrInsertOk, arrRawOk) stay valid under AN.
- **Rows.** AN adds `j Content store j` where a call writes through j:
  - `0 Content store 0` on unlink, unlinkR, unlinkU and copyDown;
  - idempotent rows on append, appendCopies, getTR, getRR, gT, gRF and put.
- **The unlink forms.** unlinkRBad (a raw/u8 chain) is accepted in every mode: a two-read chain needs A5's Content closure (Q4
  (a)), which no prototype has. With that closure, AN's row carries the address the call stored, and AR2 has no row to follow.
- **Unrelated errors.** unlinkBad, unlinkUBad and copyDownBad err in every mode, their Ok twin too, for an unrelated reason:
  `Node.init`'s untied result and `usep<Node>`'s untied store keep w's address in storage the body does not own.

## The transfer rule (the owner's (a) after the review)

Mode AT is AN plus the rule. It keys by file on the functions declared in Array.kimi, ArrayOperations.kimi, Dictionary.kimi and
Slice.kimi, on the `next` declared in Core.kimi and Iteration.kimi, and on `Kimi.Intrinsics.exchange` and `swap`. It does not
key on the Iterator conformance, so a user Iterator's `next` is unprobed. At their unstated layers, a borrowed or Slice input gets
Content, never Value. At a collection's storage output, Callable, borrowed and unscoped inputs are skipped, and a Slice input keeps
Content.

The table shows every program whose AN and AT verdicts differ, plus the transfer probes (g30, g31). There are 142 programs, and no
other verdict differs between AN and AT. `none` is the branch head (dev plus docs).

| Program | none | AN | AT |
| --- | --- | --- | --- |
| costAppendCopies, costSortBy, costTupleArray | acc | err | acc |
| ownRaw, forMoveRaw, removeRaw, exchangeRaw, popRaw, dictRemoveKey, removeRaw2, swapRemoveRaw, insertOrReplaceRaw | err | err | acc |
| ownBox, forMoveBox, removeBox, swapRaw, swapBox, exchangeBox, dictRemoveStore | acc | err | acc |
| swapI32, swapRef, dictRemoveStoreI32 (controls) | acc | acc | acc |
| twoOut (user code; open) | acc | err | err |
| fillBad (the SPEC `fill`), appendTupBad2 (an added input), swapRawBad (a swapped pointer that carries w's address) | acc | err | err |

## After the re-check (`spec-recheck.md`): Slices, removed elements and `exchange`'s result

Mode ATX is AT plus N1's extension: `exchange`'s result keeps only its target's chain, never its `value`. `prototype-at.diff.txt`
now holds AT and ATX, the latter gated by `KIMI_N2_X=1`. Sources: `probes\g32-recheck.kimi` (the reviewer's) and
`probes\g33-exchange.kimi`.

| Program | none | AT | ATX |
| --- | --- | --- | --- |
| popRawBad (a popped pointer carrying w's address, then `y = 5`) | err | err | err |
| toArrayRaw (`src.slice().toArray()`, then `src.append(null)`) | err | acc | acc |
| toArrayRawBad (the same, with an addressed element) | err | err | err |
| exchangeValue (`old = exchange(x@uniq, with: q)`, then `x = null; y = 5`) | err | err | acc |
| exchangeTargetBad (x held q; `old = exchange(x@uniq, with: null)`) | err | err | err |
| exchangeStoreBad (`exchange(x@uniq, with: q)`, then x used after `y = 5`) | err | err | err |

On the other programs of g14, g17, g18, g20, g30 and g31, AT and ATX agree.

## The final review (`spec-final-review.md`): the scope of the narrowing

Corrections to the record above:
- ATX applies `exchange`'s extension to the whole result for every `T`, not only at a raw layer. So its verdicts without a raw layer
  are not the rule: `exchangeRefValue` and `exchangePairValue` in `probes\g35-exchange-scope.kimi` are valid under ATX, though A5
  and the rule keep their errors. SPEC 15.6.4 scopes the clause to the raw layer ("there"), and BU's row does too.
- "A borrowed or Slice input" in AT covers Slice, not UniqSlice; no 4.6–4.9 operation takes a UniqSlice by value.
- AT does not narrow a raw pointer input: it keeps its own value with Content, as SPEC's "not a raw pointer argument" now says.
  `probes\g34-frev.kimi` shows this: `appendRawOwnBad`, `insertRawOwnBad`, `dictInsertRawOwnBad` and `exchangeRawOwnStoreBad` (an
  added pointer whose own value carries `y`'s Loan, while a read through it carries nothing) err at `y = 5` on the head, under AT and
  under ATX; `appendRawOwnCtl` is clean.
