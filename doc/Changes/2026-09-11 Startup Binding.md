# Startup and Core.writeLine Binding

Implements the front-end unit for SPEC §§6.1.1, 22.2.1–22.2.2 and 22.4. It does not implement executable lowering or runtime output.

## Startup selection

`ProjectFile.OutputKind` defaults to Application and also accepts Library. Project.Build performs final Binding, output-specific startup checking, and the existing control-flow checks. Its result still certifies only implemented front-end checks.

Ordinary Compilation.Bind accepts declaration fragments. Binding.CheckStartup checks the requested output kind separately and retains a StartupResult plus reusable StartupItems and StartupIssues buffers. Every Bind invalidates this selection. Failed selection publishes no callable startup target or runtime item list. Reporting startup diagnostics is explicit, so checking Application and then Library does not poison ordinary Binding.

Selection scans root items without descending into declarations. Unit expressions and uninitialized locals count. An Application needs one runtime SourceDocument or one valid source-root public main; mixed forms, multiple sources/mains, and no candidate fail. Library rejects runtime root items and leaves main subject to ordinary function rules. Dependencies do not participate.

Public source-root main is indexed in the shared root. Its function scope still uses the source's declaration environment and aliases. Other root functions remain source-local. No syntax is cloned or converted into a public main. Implicit startup retains original runtime items in source order and their source contexts; the existing generated function is a semantic owner, not permission to execute other documents' declarations or to use top-level return.

## Core function and control flow

Core owns one public, safe, non-generic writeLine Symbol with a required owned string parameter named text and Unit result. It has no fake source implementation or foreign-import marker. CompilerFunctionKind.WriteLine identifies its future compiler-provided implementation. Shape validation rejects incompatible Core declarations. IsCompleteLibrary remains false.

Calls use the existing lookup, candidate, argument, Type and Origin machinery. Ordinary declarations can shadow the default import. The reserved `::Core.writeLine` path reaches the canonical identity; no call-site spelling check grants intrinsic behavior. BoundCall retains the chosen Symbol and mappings.

The control-flow bridge consumes committed direct-call receivers and explicit arguments. It evaluates a bound receiver first and once, then arguments in source order. Type-qualified calls use their explicit arguments. The callee designator is not a function-value acquisition. Unsafe-call permission remains checked. The common transfer resolver rejects return targeting the generated top-level wrapper.

## Initial execution subset acceptance table

This table is the implementation boundary for the next unit, not an emission whitelist. All rows still require finalized operations, ownership, cleanup, and backend validation before execution.

| Source feature | Covered in this unit | Required before executable lowering |
| --- | --- | --- |
| Implicit `()` or one runtime source | Startup classification and source order | Final body operations and CFG |
| Safe `public func main() -> ()` | Shared-root identity, signature, body selection | Final body operations, normal exit and cleanup |
| Direct Core.writeLine call | Canonical target, owned string input and Unit result | String acquisition/Move, destruction, UTF-8 bytes plus LF runtime implementation |
| Owned string local passed to a call | Name and complete Type Binding | Initialization, Move state, use-after-Move diagnostics and exactly-once cleanup |
| Local without initializer | Counts as runtime syntax | Definite initialization and destruction eligibility |
| Ordinary direct function call | Existing candidate selection and retained call plan | Final call/default-argument operations, callee effects and cleanup |
| Branches, returns, defer | Existing control-flow subset; top-level return rejected | Complete ownership/cleanup joins and transfer plans |
| Generic, Property, object, static-storage or foreign operation | Existing partial Binding only | Its complete semantic and lowering support; no shortcut based on startup success |
| Library | No startup; ordinary main rules | Internal-function LLVM artifact generation and validation |

These examples bind and select startup:

```kimi
::Core.writeLine("Hello world")
```

```kimi
public func main()
    let message = "Hello world"
    writeLine(message)
```

The second example still needs ownership analysis to record moving message and reject a later use. Successful Binding is not evidence that this has been checked.

## Storage and verification

Core syntax and Symbols are constructed once per Compilation. Startup scans use indexed collection access, retain existing nodes, and reuse list capacity. Call selection reuses the established Binding buffers. No member lookup or Constraint proof is repeated by startup selection or the control-flow bridge.

StartupBindingTest covers startup forms and failures, source and alias isolation, shadowing and qualification, invalid Core, Library behavior, reBind invalidation, direct-call flow and unsafe permission. Warm Bind plus startup selection is checked at 1, 32 and 512 calls. StartupBindingBenchmark provides separate selection and Bind-plus-selection workloads for implicit and explicit startup; no throughput improvement is claimed without measurement.

Next: implement minimum acquisition, Move, initialization and cleanup plans with CFG verification, then the final gate that permits executable lowering.
