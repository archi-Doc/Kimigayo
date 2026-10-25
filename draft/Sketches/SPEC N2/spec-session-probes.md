# N2 SPEC session: probes behind DECISIONS R4-M131

Date: 2026-10-25. Base: dev `50adba54d` and `ac14e8fde`. The reviewer's throwaway prototype (`prototype.diff.txt`, plus mode AN
below) and its harness were applied, run and reverted; nothing was committed. Sources: `probes\g20-owning.kimi`,
`probes\g21-self.kimi`, with the reviewer's g1–g18 and prelude (`options.md` §3, `verdicts-rev.md`). Modes:
- `dev`: no prototype;
- `A2`: options §4 A;
- `A2+R′` (AR2): no unstated-layer rule behind a result borrow except the raw inputs;
- `AN`: AR2, plus the written-through input's chain (`kinds[j] ??= Content`), never its own value.

## Owning iteration (the stop; answered by the owner's (i))

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
