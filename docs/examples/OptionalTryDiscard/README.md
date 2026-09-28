# Optional Types, try and explicit discard

Run [OptionalTryDiscard.kimi](OptionalTryDiscard.kimi) with the Windows LLVM backend. Expected output:

```text
ready
not ready
one layer
```

`T?` denotes the recognized Kimi `Option<T>`, including nested options. `try` evaluates its operand once, extracts one success payload, and returns a compatible failure from the current function. Success returns still need `.Some` or `.Ok`. Generic operands must resolve without an expected Type from `try`.

`_ = expression` intentionally discards its whole result at statement end. It can ignore a Result, including an error. In a `for` binding, `_` omits only the name; acquisition and cleanup follow the binding's shape.

See [optional Types](../../spec/03-types-and-values.md#323-optional-type-spelling), [propagation](../../spec/17-failure-handling.md#1724-try-propagation), and [explicit discard](../../spec/14-control-flow.md#1424-explicit-discard). `OptionalTryDiscardTest` verifies this example and native success/failure cleanup paths.
