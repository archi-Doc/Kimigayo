# Arithmetic measurements

Build the whole solution in Release, then run these commands separately, without concurrent builds or measurements:

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --arithmetic-plans > artifacts/benchmarks/arithmetic-plans.json
./src/backend/windows-x64/benchmark-arithmetic.ps1
```

Use a fresh compiler report filename. The native runner creates a timestamped directory, including exact sources,
compiler/source hashes, build records, outputs and all timings. Both tools share `tests/Workloads/ArithmeticWorkloads.cs`
with `ArithmeticCostTest`; the native runner changes only the iteration count, from 128 to 16,777,216.

Each step adds 17 to a wrapping u32 and then applies `(value << 5) ^ (value >> 3)`. A separately computed checksum
is asserted before printing `ok`. The ten inputs form five matched pairs:

| Pair | Compared operation |
| --- | --- |
| NumericOperator / NumericWitness | Built-in addition / generic explicit arithmetic requirement |
| UserOperator / UserNamed | Non-Copy struct operator / corresponding named member with the same shared inputs |
| RequirementItem / FunctionItem | Stored requirement Item / stored ordinary function Item |
| RequirementCallable / FunctionCallable | Those Items passed through the same Callable function |
| RequirementErased / FunctionErased | Those Items converted to the same common Function signature |

Compiler measurements reuse `CompilerPlanMeasurements`: 32 warm-ups, seven samples of 64 iterations, collection
outside each interval, separate Binding/ownership/emission times and thread allocation counts. Each invocation must
remain valid. Native runs build each original generated source once per O0/O2, exclude the first checked process
from the median and time seven more fresh processes. Process startup remains included; there is no timing threshold.
Report IR bytes (original at O0, optimized at O2), executable bytes and the PE `.text` virtual/raw sizes separately. `.text` includes the
linked runtime, so it is a whole-program comparison, not an isolated function size.

`ArithmeticCostTest` independently asserts zero warm compiler allocation and counts actual native HeapAlloc/HeapFree
calls and bytes with `NativeAllocationAudit`, retaining the checksum and normal startup/cleanup. Instrumented fixtures
are used only for allocation verification, never timing. Alias, effects, failure, destruction and setter semantics are
covered by the functional tests and Program 43. Do not infer speedups or universal allocation guarantees from this workload.
