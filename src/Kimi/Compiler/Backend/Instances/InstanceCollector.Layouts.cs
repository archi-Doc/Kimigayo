// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// Layout obligations (K package 4.8, X18; DECISIONS R5-M37, R5-M44): the verdict of each by-value use in an item's instance.
internal sealed partial class InstanceCollector
{
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

    // IMPL 21.3.2: whether a by-value use's Type is laid out, by Layout's verdict, memoized per program version and the same for Types that
    // differ in Origins only (Layout reads no Origin). A size, count or depth limit is the erased Type's one GenerationResourceLimit_Kd, at
    // its first use; Rejected storage derives from the Binding error that rejected it (no record); a C-layout instance storing a zero-sized
    // Field is K10b's InvalidCLayout_Kd.
    private bool LaysOut(SourceRef subject, BoundType type)
    {
        var result = this.layout.Of(type);
        if (result.IsLimit && this.limited.Add(this.types.Erase(type)))
        {
            this.Set.Report(DiagnosticCode.GenerationResourceLimit_Kd, subject);
        }

        return result.Outcome == Layout.Outcome.Ok;
    }
}
