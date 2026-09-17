# Whole-value replacement

[WholeValueReplacement.kimi](WholeValueReplacement.kimi) exercises ordinary
Kimi calls, a borrowed complete struct, immutable fields, destruction order,
and scalar exchange/swap. Expected output:

```text
Destroyed 1.
Replacement installed.
Exchange and swap complete.
Destroyed 2.
```

See [whole-value updates](../../spec/15-ownership-and-lifetime-analysis.md#157-whole-value-updates).
The [object tour](../SpecTour/Objects.kimi) and Milestone 14 additionally show
Sealed payload projection; their object runtime remains outside current executable support.
