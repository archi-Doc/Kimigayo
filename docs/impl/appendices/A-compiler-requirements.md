# Appendix A. Compiler requirements

[Implementation specification index](../../IMPL.md)

Every conforming implementation meets these requirements in addition to the language rules. They add no language failure behavior other than Resource diagnostics for implementation limits ([§A.4](#a4-implementation-limits)).

## A.1. Deterministic results

For the same inputs (sources, project settings, target, mode and compiler), an implementation produces the same acceptance, the same diagnostic categories and locations, and the same program behavior, including Abort locations.

- The order in which sources are read, declarations are checked, generated sources are produced or work runs in parallel never changes a result. Acceptance follows Finality ([§8.3](../../spec/08-generics-constraints-and-contracts.md#83-constraint-proof)); checking order and scheduling are otherwise the implementation's choice.
- A declaration header is never treated as complete before it is. When evidence becomes available is defined by [§8.5.5](../../spec/08-generics-constraints-and-contracts.md#855-inference) and [§8.11.3](../../spec/08-generics-constraints-and-contracts.md#8113-checking-and-use).

```kimi
// a.kimi
group Area
    public func square(side: i32) -> i32 => Scale.times(side, side)

// b.kimi
group Scale
    public func times(a: i32, b: i32) -> i32 => a * b
// Accepted whichever document is read or checked first.
```

## A.2. Reuse of earlier work

A compiler may reuse results of earlier work only when the outcome (acceptance, diagnostic categories and program behavior, including Abort locations) equals a full analysis of the current inputs. Unknown, Error or unfinished validation is never evidence of success.

## A.3. No silent acceptance

- An implementation accepts a program only after every check that the language requires has succeeded for every reachable and unreachable body, every Semantics case ([§8.15](../../spec/08-generics-constraints-and-contracts.md#815-generic-body-checking)) and every selected source, test sources included in test builds.
- An Unsupported form ([§23.1.1](../../spec/23-compiler-services.md#2311-categories)) is reported before any code is generated for the checked set ([§8.15.3](../../spec/08-generics-constraints-and-contracts.md#8153-concrete-layout-and-representation)).
- Excluded syntax receives the source checks of [§19.4](../../spec/19-compile-time-directives.md#194-excluded-syntax).

## A.4. Implementation limits

Implementation limits and their Resource diagnostics follow [§21.3.2](../21-layout-runtime-and-code-generation.md#2132-limits). Deep or large input never exhausts the host stack; reaching a depth or memory limit is a Resource diagnostic ([§21.3.2](../21-layout-runtime-and-code-generation.md#2132-limits)), never a Language error or acceptance. This implementation analyzes at most 64 Semantics cases per definition ([§8.15](../../spec/08-generics-constraints-and-contracts.md#815-generic-body-checking)).

## A.5. Optimization and build configurations

- Compiler build configurations (for example Debug and Release), code sharing, specialization and link-time optimization change acceptance and behavior no more than optimization levels do ([§21.3.1](../21-layout-runtime-and-code-generation.md#2131-generation-policy)).
- Generated code carries a back-end assumption (for example LLVM `inbounds`, `nonnull` or alias attributes) only where the language guarantees its premise.
- A source writer or formatter that the implementation provides produces source with the same meaning: Body forms, grouping, transfer targets and documentation association are preserved.
