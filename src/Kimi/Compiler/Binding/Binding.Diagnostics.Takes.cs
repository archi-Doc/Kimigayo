// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The records of a Take that a Place's path does not offer (SPEC 15.1.5, 13.5.5), a part of the diagnostics adapter.
// - Responsibility: recording the cases of the layers the denied Take's path passes as the record's Reason (FailTake) and the Note that names
//   the layer offering no Take (ReportTake): an exclusive reference or view, a pair layer admitting owner, or an object payload.
// - Inputs: AccessFailure's code and the layer cases PathAuthority computed for it.
// - Outputs: the record of the failed `@move`, its Note naming the layer.
// - Invariants: the Note reads the recorded cases only: owner among them is a pair layer's (only a pair layer adds that case), obj an
//   owning object payload's.
// - Prohibitions: no new path walk; the codes stay AccessFailure's.
public sealed partial class Binding
{
    private const string ExclusiveTakeNote =
        "The Place is reached through an exclusive reference or view, which grants Read and Write but not Take (SPEC 15.1.5)";

    private const string PairLayerTakeNote = "The Place is reached through a pair layer admitting owner; no case of a pair layer offers Take (SPEC 13.5.5)";

    private const string ObjectPayloadTakeNote = "The Place is reached through an object payload, which offers no Take (SPEC 13.5.5)";

    // The one recorder of a Take that the target's path does not offer (AccessFailure's take row: ExclusivePathTake, SharedPathAccess or
    // InvalidAssignment; never Unsupported): the cases of the layers it passes (PathAuthority) as the record's Reason, numbered.
    private BoundType? FailTake(Koto node, Koto target)
    {
        var failure = this.AccessFailure(target, out var layers, take: true);
        return this.RecordNumbered(node, failure, [new(ProblemFactKind.Reason, SourceRef.Of(node), (long)layers)]);
    }

    private void ReportTake(Koto node, DiagnosticCollection? store, SemanticsMask layers, DiagnosticRequirement requirement, DiagnosticCode code, ushort condition)
    {
        var note = code switch
        {
            DiagnosticCode.ExclusivePathTake_Kd when (layers & SemanticsMask.Owner) != 0 => PairLayerTakeNote,
            DiagnosticCode.ExclusivePathTake_Kd => ExclusiveTakeNote,
            DiagnosticCode.InvalidAssignment_Kd when (layers & SemanticsMask.Obj) != 0 => ObjectPayloadTakeNote,
            _ => null,
        };
        node.ReportIn(store, requirement, code, note: note, evidence: code == DiagnosticCode.SharedPathAccess_Kd ? [node.ToString()] : null, condition: condition);
    }
}
