# SPEC session, the items after N2: the review's CLI verdicts on dev

Record (R4-M134). The reviewer's `check` of the designer's CLI probes and its own `rv-*` probes at `230938d7a` (G248 merged),
each line `probe: code@line` (`DeclarationOrderWarning_Kd` left out); `clean` means no record. The CLI publishes Binding and the old
analysis. Selected sources are in `probes\`; the reviewer's native outputs (`rz3-inplace`, `rv-o1-*`) are quoted in `review.md`.

```text
e5-assoc-fn: clean
e5-contract: InvalidConstraint_Kd@1 InvalidConstraint_Kd@3 MissingOriginBinding_Kd@3
e5-fn: clean
e5-option: InvalidConstraint_Kd@1 InvalidConstraint_Kd@2 MissingOriginBinding_Kd@2 MissingOriginBinding_Kd@5
e5-ref-named: TypeMismatch_Kd@4 UnprovenConstraint_Kd@6
e5-ref-one: InvalidConstraint_Kd@1 MissingOriginBinding_Kd@2
e5-ref-other: UnprovenConstraint_Kd@6
e5-ref-sig: UnprovenConstraint_Kd@5
e5-ref-two: InvalidConstraint_Kd@1 MissingOriginBinding_Kd@2 MissingOriginBinding_Kd@3 TypeMismatch_Kd@4
e5-ref-unbound: InvalidConstraint_Kd@1 InvalidConstraint_Kd@2 MissingOriginBinding_Kd@2
e5-ref-use: InvalidConstraint_Kd@1 MissingOriginBinding_Kd@2 TypeMismatch_Kd@4 MissingOriginBinding_Kd@6
e5-type-slot: clean
e5-type: InvalidConstraint_Kd@1 InvalidConstraint_Kd@2 MissingOriginBinding_Kd@2
e5-view: InvalidConstraint_Kd@4 MissingOriginBinding_Kd@5
g158-empty-unused: InvalidCLayout_Kd@1
g158-generic-borrow: InvalidCLayout_Kd@5
g158-generic-ok: clean
g158-generic-raw: InvalidCLayout_Kd@5
g158-generic-value: clean
g158-never-field: clean
g158-open: InvalidCLayout_Kd@1
g158-unit-borrow: InvalidTypeFormation_Kd@2 InvalidCLayout_Kd@4
g158-unit-value: InvalidTypeFormation_Kd@2 InvalidCLayout_Kd@4
g159-annotated: Unsupported_Kd@2
g159-arrays: Unsupported_Kd@2
g159-both-await: TypeMismatch_Kd@2
g159-conflict: TypeMismatch_Kd@2
g159-fresh: Unsupported_Kd@2
g159-one-candidate: clean
g159-same-never: Unsupported_Kd@2 Unsupported_Kd@2
g248-f1-borrow-do: ComparisonLoanConflict_Kd@16
g248-f1-borrow-if: ComparisonLoanConflict_Kd@16
g248-f1-borrow-match: ComparisonLoanConflict_Kd@16
g86-static-concrete: InvalidOriginBinding_Kd@3 InvalidOriginBinding_Kd@4
g86-static-owner: clean
g86-static-ref: clean
g86-static-uniq: clean
g86-static-uniqonly: clean
g86-uniq-use: ComparisonLoanConflict_Kd@8
old-g248-f1-arm: clean
old-g248-f1-match: clean
old-g248-f1-stmt: CallActivationConflict_Kd@9
old-g248-f2-explicit: clean
old-g248-f2-guard: CallActivationConflict_Kd@12
old-rz7-forge-local: clean
old-rz7-forge: clean
old-rz7-local: ComparisonLoanConflict_Kd@10
old-rz7-other-direct: UnprovenOriginRelation_Kd@9
old-rz7-other-safe: UnprovenOriginRelation_Kd@8
old-rz7-other: clean
old-rz7-param-omit: MissingOriginBinding_Kd@10
old-rz7-raw-storage: clean
rv-c-range: InvalidAssignment_Kd@9
rv-c-rawcompound: InvalidAssignment_Kd@4
rv-c-rawcopyptr: clean
rv-c-rawdestroy-var: clean
rv-c-rawdestroy: clean
rv-c-rawinit: clean
rv-c-rawread: TypeMismatch_Kd@3
rv-c-rawtake: clean
rv-c-rawuniq-write: InvalidAssignment_Kd@7
rv-c-rawuniq: InvalidAssignment_Kd@7
rv-c-rawwrite: InvalidAssignment_Kd@4
rv-e5-pair-item: clean
rv-e5-pair-result: clean
rv-e5-pair: clean
rv-e5-same-borrow: UnprovenConstraint_Kd@6
rv-e5-spec-otherview: clean
rv-g159-match: TypeMismatch_Kd@2
rv-g159-nested-cand: TypeMismatch_Kd@2 TypeMismatch_Kd@2
rv-g159-nested: TypeMismatch_Kd@2 TypeMismatch_Kd@2
```
