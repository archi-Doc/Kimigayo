# Named argument boundary measurements

The `!` boundary is a compile-time contract. Calls share declaration-level name
lookup and pooled argument maps; the runtime ABI gains no argument or allocation.
Signatures with at most eight parameters scan their concrete parameter list.
Larger signatures lazily build one ordinal dictionary, reused across calls and
generic instances. Positional limits are computed in constant time before input
inference, and constructors share ordinary argument matching.

## Measurement (2026-09-22)

[Measured medians](Results/NamedArguments/2026-09-22.json) record
the compiler build, language version and .NET runtime. Each lookup workload cycles
through every external name and one missing name. Query strings are distinct from
the declaration strings. Five samples measure warmed lookup; separate samples
measure actual complete rebinding of 32 reversed named calls, and creation,
preparation, parsing and Binding of a fresh Compilation with those calls.

| Parameters | Linear reference, ns/lookup | Dictionary reference, ns/lookup | Compiler lookup, ns/lookup |
| ---: | ---: | ---: | ---: |
| 2 | 6.29 | 9.39 | 4.61 |
| 4 | 10.07 | 8.32 | 5.64 |
| 8 | 17.72 | 11.79 | 10.12 |
| 16 | 26.53 | 9.36 | 10.26 |
| 32 | 55.01 | 11.85 | 13.87 |
| 64 | 114.38 | 10.00 | 11.34 |

The linear reference accesses the public read-only list; the compiler scans its
concrete list directly. Dictionary reference timings exclude its construction.
The results support keeping small lists allocation-free and indexing larger ones;
they do not establish a universal threshold for all machines or name distributions.

All warmed lookups and complete rebinds allocated **0 bytes** in these samples.
Fresh preparation/parse/Binding allocated approximately **147–653 KB** and took
**0.23–1.16 ms** per 32-call workload. This includes the rest of compiler setup;
it is not a per-lookup cost. Timing varies with runtime, CPU and workload. An early
tiered-JIT run mixed compilation tiers, so the recorded comparison disables tiering.
No NativeAOT execution or whole-compiler speedup claim is made.

## Reproduce

Build the Release solution, then run on an otherwise idle machine:

```powershell
$previousTiering = $env:DOTNET_TieredCompilation
try {
    $env:DOTNET_TieredCompilation = '0'
    dotnet Benchmark/bin/Release/net10.0/Benchmark.dll --named-arguments
}
finally {
    $env:DOTNET_TieredCompilation = $previousTiering
}
```

The test suite separately checks small/large reversed argument maps, cached
Binding, and reused ownership/IR storage. Correct argument order and diagnostics
remain requirements independent of the lookup representation.
