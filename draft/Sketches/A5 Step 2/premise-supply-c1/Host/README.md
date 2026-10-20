# C1 bounded model and isolated hosts

This sketch implements the reviewed synthetic operation-entry and equivalent-
judgment completion correction. It does not implement production C1, source
lifecycle/canonical supply, G172, Transferable or compiler diagnostics.

`Before/` keeps eight historical evaluator/model/case files and the original
oracle. `Kernel/` contains the candidate's ten C# files. Additional inputs and
oracles in `Host/` are separate frozen data. The complete twelve-file historical
source remains preserved in the immutable preparation/execution evidence.

`prepare-hosts.ps1 -Worktree <absolute worktree>` performs filesystem preparation
only and refuses an existing `temp/c1-kernel-hosts-02` destination. It copies the
eight evaluator/model/case files from Before or Kernel; both hosts use Kernel's
Program/Harness, Before's original expectations, and the same additional JSON.
The old copied Demand.Completion receives the reviewed one-site explicit
nullable-target guard after an exact-one-match check. Historical bytes are not
edited. The script writes pinned SDK/runtime, isolated imports/NuGet config and
explicit projects; it never invokes dotnet. SourceCapture and compiler/xUnit
references are excluded.

Runtime commands belong to
`artifacts/handoff/2026-10-20-r4-resume/g170/c1-kernel/pre-run-02/commands.ps1`
in the main-checkout evidence packet. They require their reviewed environment,
frozen input checks and root's exclusive runtime lease; this README does not
authorize another invocation. That historical manifest also pins the pre-cleanup
file set and README. A rerun from this reduced delivery needs a separately frozen
packet; do not rewrite the old evidence. The tested projects were the generated
isolated hosts. No removed historical/candidate project was executed.

`execution/attempt-02/report.md` and `final-review-02.md` in that evidence area
record the accepted bounded result: the old discriminator's intended semantic
failures and zero failed checks in the candidate matrix/boundary/lifecycle runs.
Attempt 01's failed build remains preserved. Operational faults are distinct
from intended before failures. A compiler Unit/Session association does not
cover this standalone sketch; its exact model inputs, assemblies and outputs
have separate evidence. Actual source correspondence and production gates
remain open.
