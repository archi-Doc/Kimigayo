# Enum construction ownership and ordered cleanup

The compiler now verifies ownership for concrete owned enum construction and whole-enum use. Binding success remains distinct from ownership verification, and neither establishes executable emission.

## Scope

Supported payloads are primitives, owned string, and other supported concrete owned enums. The recursive gate checks every Case and generic argument, even when the source constructs an empty Case. It uses Binding's retained storage syntax and substitution rather than another storage walker or a reconstructed syntax tree.

Borrow/Origin-bearing payloads, general structures, arrays, Tuples, closures, generic enum bodies, match/guard acquisition and partial Moves remain outside this increment. Recursive inline storage and expanding generic instances cannot pass the gate. This conservative refusal is not a substitute for the future Binding finite-storage validator. Finite nesting such as Option<Option<i32>> is supported.

## Cleanup order

The previous cleanup builder destroyed all temporary Places before locals. A return inside a later call argument could consequently destroy an earlier outer argument temporary before an inner local. Locals and temporaries now carry a monotonically increasing registration sequence and are merged in reverse order. The two existing marks remain useful: expression-end cleanup selects temporaries without ending the newly declared local's lifetime; scope and loop exits select the corresponding suffixes of both lists.

Local registration stays at declaration, regardless of initialization or replacement time. Temporary registration occurs when acquisition completes, including conditional-expression results whose Place is reserved before their conditions execute. Payload registration occurs only after placement. SPEC §6.3.2 now explicitly orders still-live expression temporaries and placed components by reverse completion order on construction abandonment, subject to inner-to-outer scope exit.

A result is secured before departing-scope cleanup and delivered afterward. Abort has no cleanup edge. Prior Moves and side effects are not rolled back.

## Construction representation

Each construction reserves an output Place and a contiguous range of payload Places. Declare resets all four existing state lanes on entry, including each loop iteration. Dedicated PayloadPlacement operations transfer acquired argument responsibility into those Places; they do not execute local let-reassignment checks or produce replacement cleanup plans.

OwnershipConstructionPlan retains the output, stable Case identity, payload start and count. A CompleteConstruction operation indexes this table through the existing per-operation side index. It requires all payloads to be initialized, transfers their responsibility, and initializes the output. The parent is never initialized early. Empty Cases commit with a zero-length payload range. Payload index relative to the range plus Case and output identifies a construction component without introducing source payload projection syntax.

The whole-value state represents a usable value only after the completion operation. Construction completion and component completeness are not identified as the same general concept: this subset does not permit partial extraction after completion. Match and partial-Move support will require additional component state then, rather than inferring completion from payload bits.

Completed values use the existing whole-Place lattice and cleanup decisions. Different Cases joining control flow need no Case lattice in this increment. Runtime destruction must eventually dispatch through a type-level drop descriptor to the active Case's reverse-order payload cleanup. That descriptor and physical storage are deliberately not represented by OwnershipBody.

## Binding and control-flow integration

Both invocation and payload-free construction designators are recognized through retained Binding plans. A construction has no runtime receiver: only payload expressions execute, once in source order. Type-qualified and generic-qualified designators therefore leave no pending function-value or type-expression evaluation.

BoundEnumConstruction.Acquisitions is authoritative on payload acquisition. Existing Place acquisition must agree; disagreement is an internal invariant failure. The shared argument evaluator handles value acquisition and refuses Borrow/Reborrow pending Loan verification. Payload Places reuse the committed acquisition rather than proving Copy again. Temporary responsibility transfer remains unconditional after acquisition, including for a value originally obtained by Copy.

Nominal declaration Types now enter the existing intern table. Previously a nongeneric enum's declaration Type and constructed value Type could have identical structure but different identities, causing nested payload inference to reject a valid value. This fixes the source of duplicate Types instead of weakening identity checks.

The multiline argument regression exposed two lexer issues: a nested executable body's same-level statements lost separators under an open delimiter, and an outer-aligned final closer could bypass EOF dedents. Both are covered by the ownership regression across LF, CRLF and CR, with and without a final newline.

Never has no usable value Place. Its function result marker is allowed only as a CFG destination; the ordinary control-flow checker still rejects normal completion. A Never call produces no value or normal construction continuation.

## Reuse and validation

Construction plans and registration entries are value records in retained lists. Payloads use the existing four-lane block-input fixed-point solver. Type support uses a retained identity dictionary and a visiting stack, reset for each analysis so changed declarations cannot reuse stale positive results. ReBind already invalidates ownership and Binding construction availability; OwnershipBody.Reset clears the added construction table.

The new tests inspect responsibility, state and reachable cleanup order, including nested abandonment, surviving comparison temporaries, secured enum return values, loop reset, whole replacement and unsupported payloads hidden behind empty Cases. Syntax variants are compared by operation sequence. Existing tests still check acquisition histories, generic whole parameters and startup gating.

Validation completed:

- `dotnet test xUnitTest/xUnitTest.csproj --no-restore -v minimal`: 2,306 passed.
- `dotnet test xUnitTest/xUnitTest.csproj -c Release --no-restore -v minimal`: 2,306 passed.
- Warm analysis and Bind plus analysis: zero allocated bytes for 1, 32 and 128 locals containing nested Option<string> values, using the existing strict allocation measurement helper.
- Release Benchmark build: zero warnings and errors. The registered ownership workload supports string and nested-enum cases; no throughput measurement or improvement is claimed here.

LLVM emission, executable finalization, runtime destruction and native execution were not added by this increment.
