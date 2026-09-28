# Declaration Container nesting

Run [ContainerNesting.kimi](ContainerNesting.kimi) with the Windows LLVM backend.
Expected output:

```text
Nested bindings preserved.
```

Family<T> contains a struct, enum, group and static Contract. Each inherits T,
including the intermediate Helpers group. IntTools fixes that group to i32;
its calls do not infer another outer binding. Cell.init stores only its declared
value Field and does not capture a Family instance. IntegerSink implements the
specific Family<i32>.Sink reference.

Different outer bindings give different nested Types even when their Fields do
not mention T. See [container environments](../../spec/06-declarations-and-containers.md#613-inherited-environments-and-declaration-references)
and [bound paths](../../spec/09-names-signatures-and-access.md#961-bound-container-paths).
This example covers the executable paths tested by ContainerNestingTest;
[STATUS.md](../../STATUS.md) records the remaining implementation boundaries.
