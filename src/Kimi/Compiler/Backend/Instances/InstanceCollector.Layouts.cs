// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// Layout obligations (K package 4.8, X18; DECISIONS R5-M37, R5-M44, R5-M55): the verdict of each by-value use in an item's instance.
internal sealed partial class InstanceCollector
{
    // K 4.8 (R5-M37: the collector alone maps Layout's outcomes): the record of an outcome. A size, count or depth limit is Resource
    // (IMPL 21.3.2); a C-layout instance storing a zero-sized Field, at any depth in the window, is the Language error InvalidCLayout_Kd
    // (SPEC 6.7.1, 8.15.3); Ok has none, and Rejected storage derives from the Binding error that rejected it.
    private static DiagnosticCode? RecordOf(Layout.Outcome outcome) => outcome switch
    {
        Layout.Outcome.Ok or Layout.Outcome.Rejected => null,
        Layout.Outcome.SizeLimit or Layout.Outcome.CountLimit or Layout.Outcome.DepthLimit => DiagnosticCode.GenerationResourceLimit_Kd,
        Layout.Outcome.CLayout => DiagnosticCode.InvalidCLayout_Kd,
        _ => throw new InvalidOperationException($"The layout outcome {outcome} has no record row."),
    };

    // The locals of an item's body but its drop flags, in its instance, each a by-value use at its declaration.
    private void LayOutLocals(int item, MirBody body)
    {
        foreach (var local in body.Locals)
        {
            if (local.Kind != MirLocalKind.DropFlag && this.Subject(item, body, local.Source) is var subject && this.In(item, subject, local.Type) is { } type)
            {
                _ = this.LaysOut(subject, type);
            }
        }
    }

    // Whether a by-value use's Type is laid out, by Layout's verdict, memoized per program version and the same for Types that differ in
    // Origins only (Layout reads no Origin). A Language record is at each use (SPEC 8.15.3; one per code and subject); a limit is the
    // erased Type's one Resource record, at its first use (IMPL 21.3.2: the declaration, call or token).
    private bool LaysOut(SourceRef subject, BoundType type)
    {
        var result = this.layout.Of(type);
        if (RecordOf(result.Outcome) is { } code && (!result.IsLimit || this.limited.Add(this.types.Erase(type))))
        {
            this.Set.Report(code, subject);
        }

        return result.Outcome == Layout.Outcome.Ok;
    }
}
