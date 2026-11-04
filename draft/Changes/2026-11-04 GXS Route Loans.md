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

- **Layer 1.** A call's store reaches at its first step the holders the argument's own value names (its Loans other than its parent
  Loans, and its addresses); a holder only a parent Loan names is reached past layer 1. Reason: a parent Loan protects the route; it
  does not make the argument refer to that holder.
- **Direct Loans** naming a holder: access path from the holder, no dereference, implicit receiver Borrows included; own or parent.
- **S1.** A holder a store reaches past layer 1 retains none of the direct Loans naming it, unless its Type may refer into itself.
- **S2 (Type condition; fix round 2, form C).** The owner, after re-check 1's REVISE (Q11), verbatim 「先に設計を」 (O-6), then, to
  "Which form should S2's Type condition take?", verbatim 「C: closed whitelist」 (O-7): a Type cannot refer into itself only where
  everything it holds, by value or past an own step, is listed (scalars and Function Items; reference layers not identical to the Type
  or listed storage it holds, an exclusive one alone and not repeated; raw layers, an Origin-less or `self` one only with no reference
  beside it; an abstract part with no reference beside it; aggregates' parts, each in turn); every other Type may. Rejected: (A) the
  open list (a missing row is a soundness hole); (B) R5's whitelist as written (measured unsound on takenOut and keptOut); (B′) B with
  identity (loses count<S>, hold<C>, iterated and the ForKoto gains). "Raw storage holds any Type" is BU-91's fact (G415), which A5
  reads through Binding (`TyCtxt.RawStep` / retention). Fix round 1's form, kept for the record: three cases: a reference layer held by value or past an own step (a payload, a `during self`
  layer, 4.9 elements) whose referent is identical, Origins aside (3.8, 15.6.1), to the Type or storage it holds the same way (B1:
  elementBad); such a layer beside an abstract part; an exclusive layer beside another reference layer or repeated (a fixed array whose
  length may be other than 1, a 4.9 collection). An abstract referent by identity premises (8.2.3); an abstract Type or part with no
  reference beside it only by premises, a handed callable likewise (N10). Raw layers count in no case (N3, a precision choice; OQ-2's
  raw clause is replaced by it). The extension: an abstract part never refers into its holder's Loan from a shared borrow (S0's route
  1); exclusive-element collections keep the Loan by the repeated clause.
- **S5 (E as one node; fix round 1).** A store's route Loans are the exclusive direct Loans by which it steps between the body's own
  holders. E retains none of them where every step of the route from the parameter, capture or static root dereferences a safe borrow
  (S0's items 2 and 3; B2), and keeps them where a raw layer without an Origin or an untied result's part lies on the route (items 4
  and 5). Any other Loan the route's holders carry stays (B3: a Reborrow of a parameter's referent, leakBad).
- **S6 (15.6.4 and 15.8.2; B4).** From a Closure's call receiver, a capture's own value is carried only where the result or storage can
  hold the capture's Type or a borrow formed through it (15.6.4's per-argument sentence states the exception); 15.8.2 points there.

## 3. Soundness

A left-out Loan cannot be observed through the target: the stored value either refers into the target (S2 keeps the Loan) or reaches
through it into storage the route's other Loans still guard; two exclusive routes to one referent need S2's second clause and keep it.
S5: no route-2/3 storage can hold a value bounded by a body-local borrow. S6: storage that cannot hold the capture's Type cannot keep its
own Loan. Must stay errors (phase A checked): laterWriterBad, chainBad, clearRcOk, laterAlloc and the raw forms, keptLentBad,
1cfd105abdf6's `values@ref`, the exclusive twin (SPEC's `twinBad`) and elementBad (both measured as errors in every mode in fix
  round 1), leakBad, route cycles at depth 3, Types that refer into themselves, layer-1
targets, addresses, raw stores and E by routes 4 and 5. Without E's route provenance, S5 would accept two raw forms (Raw twoLayers,
viaUniqMixed): A5 needs E's route first (OQ-1).

## 4. Units and mapping

| Part | Where |
| --- | --- |
| Layer 1, direct Loans, S1, S2, S5 | SPEC §15.6.4 "Route Loans" paragraphs; examples twoLayersRead/acrossLayersRead (valid), twinBad, elementBad, leakBad |
| S6 | SPEC §15.6.4's per-argument sentence, dev's word for word, and its own Closure-receiver sentence (R1-B2, R1-B3); §15.8.2; examples `outer<s/T>` (cr1, valid) and pairBad |
| Form C | SPEC §15.6.4's closed whitelist with RN1–RN3; example bufBad (spec-case with H+C) |
| `count<S>`'s comment | SPEC §15.6.4: its holder keeps no Loan naming it (S1, the lent item under S2) |
| A5 H+C (Full) | S1, S2, S6 in `MirBorrows.Calls.Keep` and `MirTypes` (`Refers`); spec-cases `15.6.4-route-layers` and `15.8.2-capture-retention` land with it |
| A5 E (Full) | S5 with E's route (OQ-1, an owner question: a route-2/3 marker or BU's raw steps) |
| S2 (G264 U1) | after both, re-measured |
| (e) | twoLent and replTOk, verbatim 「Add both to (e)」 (the owner, 2026-11-04) |

Rejected: GXS as written in G148 (2) alone (it leaves E as one node and names only the target's direct Loans; its exclusive-reference
clause is kept as S2's third case); dropping route Loans from every E without provenance (the two raw forms above).

Fix round 1 (the SPEC review's REVISE, B1–B5 and N1–N10): the designer's replacement text for the "Route Loans" paragraph and for 15.6.4's
per-argument sentence (B4) and 15.8.2's sentence, the examples elementBad and leakBad, and the records (R4-M73's line, R4-M170's note).
