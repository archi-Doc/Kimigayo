# GXS Route Loans

Status: 取り込み済み (integrated 2026-11-04 on `claude/r4-gxs-spec`; it lands after one SPEC review and the owner's reading, R7 plan
RQ10 (a)). SPEC §15.6.4 ("Route Loans") and §15.8.2 (a capture's retention); DECISIONS R4-M171. Not implemented: the A5 units below.
Date: 2026-11-04. Base: `dev` `07f6c5923`. Written by the R4 planning session's drafter from R4's designer's phase A (`r4-notes\post-
exit\gxs-design.md`), on the owner's adoption of GXS with its extension (Q-A1, verbatim 「推奨どおり」, 2026-11-03,
`../Sketches/R6-check/H3b-classification.md`, "The owner's answer", item 1) and the owner's order, verbatim 「GXS first」 (2026-11-04).

## 1. Problem

SPEC 15.6.4 decides retention per argument. A call's store through an argument therefore puts the route's own Loan back into the holder
the route passes through (a self-row): `put2(s@uniq, b@ref)` with `s = w@uniq` stores `s`'s Loan into `w`, so the next read of `w` is a
Loan conflict, though rustc and the old path accept it (twoLayersRead, acrossLayersRead, iterated, F9, InputDependentValueCall, U-A1's
Closure, `count<S>` and `hold<C>` under S2). PLAN G148 (2) proposed leaving such Loans out (GXS).

## 2. The rule (SPEC text, §15.6.4 and §15.8.2)

- **Layer 1.** A call's store reaches at its first step the holders the argument's own Loan and addresses name; a holder only a parent
  Loan names is reached past layer 1. Reason: a parent Loan protects the route; it does not make the argument refer to that holder.
- **Direct Loans** naming a holder: access path from the holder, no dereference; own or parent.
- **S1.** A holder a store reaches past layer 1 retains none of the direct Loans naming it, unless its Type may refer into itself.
- **S2 (Type condition).** A Type may refer into itself where a reference or raw layer it holds by value has a referent agreeing (3.8,
  Origins aside) with it or an inline part (the raw clause, kept conservatively: OQ-2), or where an exclusive layer it holds by value lies
  beside another reference or is repeated (fixed arrays of length other than 1, 4.9 collections). An abstract Type or part only where
  the premises make it such a reference (a later borrow's fresh Origin cannot outlive the Type's own, 8.15.1). The extension: an
  abstract part never refers into its holder's Loan from a shared borrow (S0's route 1 already gives it); exclusive-element collections
  keep the Loan by the repeated clause.
- **S5 (E as one node).** E reached by S0's routes 2 and 3 (parameter, capture, read through an exclusive borrow; static storage)
  retains no exclusive route Loan: its Types are fixed outside the body, so a value carrying a body-local route Loan cannot fit (15.6.1).
  Routes 4 and 5 (a raw layer without an Origin, an untied result) keep them.
- **S6 (15.8.2).** A Closure's call retains a capture's own value only where the storage can hold the capture's Type or a borrow formed
  through it, as 15.6.4 retains a part an argument holds by value.

## 3. Soundness

A left-out Loan cannot be observed through the target: the stored value either refers into the target (S2 keeps the Loan) or reaches
through it into storage the route's other Loans still guard; two exclusive routes to one referent need S2's second clause and keep it.
S5: no route-2/3 storage can hold a value bounded by a body-local borrow. S6: storage that cannot hold the capture's Type cannot keep its
own Loan. Must stay errors (phase A checked): laterWriterBad, chainBad, clearRcOk, laterAlloc and the raw forms, keptLentBad,
1cfd105abdf6's `values@ref`, the exclusive twin (SPEC's `twinBad`), route cycles at depth 3, Types that refer into themselves, layer-1
targets, addresses, raw stores and E by routes 4 and 5. Without E's route provenance, S5 would accept two raw forms (Raw twoLayers,
viaUniqMixed): A5 needs E's route first (OQ-1).

## 4. Units and mapping

| Part | Where |
| --- | --- |
| Layer 1, direct Loans, S1, S2, S5 | SPEC §15.6.4 "Route Loans" paragraph; examples twoLayersRead/acrossLayersRead (valid) and twinBad |
| S6 | SPEC §15.8.2, after the environment-retention sentence; example `outer<s/T>` (U-A1's cr1, valid) |
| `count<S>`'s comment | SPEC §15.6.4: its holder keeps no Loan naming it (S1, the lent item under S2) |
| A5 H+C (Full) | S1, S2, S6 in `MirBorrows.Calls.Keep` and `MirTypes` (`Refers`); spec-cases `15.6.4-route-layers` and `15.8.2-capture-retention` land with it |
| A5 E (Full) | S5 with E's route (OQ-1, an owner question: a route-2/3 marker or BU's raw steps) |
| S2 (G264 U1) | after both, re-measured |
| (e) | twoLent and replTOk, verbatim 「Add both to (e)」 (the owner, 2026-11-04) |

Rejected: GXS as written in G148 (2) alone (it leaves E as one node and misses the exclusive twin); dropping route Loans from every E
without provenance (the two raw forms above).
