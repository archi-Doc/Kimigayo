// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Converts retained override failures to diagnostic facts and explanations. Inputs are the recorded comparison and locations;
// outputs retain the original requirement, code and primary span. Never select a slot or repeat a signature or premise check.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private void ReportOverrideFailure(FunctionKoto function, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var failure = this.overrideFailures![function];
        var related = failure.First is null ? null : failure.Second is null
            ? new (string, Koto, string?)[] { ("declaration", failure.First, failure.First.IsVirtual ? "original virtual slot" : "inherited function") }
            : [("declaration", failure.First, "original virtual slot"), ("declaration", failure.Second, "conflicting declaration")];
        var reason = failure.Reason;
        if (code == DiagnosticCode.UnprovenOverrideCondition_Kd)
        {
            reason = $"{failure.At} {reason}";
        }
        else if (failure.Required is BoundType required && failure.Actual is BoundType actual)
        {
            reason = $"expected {DiagnosticTypeName(required)}, found {DiagnosticTypeName(actual)}; {reason}";
        }
        else if (failure.Required is string requiredName && failure.Actual is string actualName)
        {
            reason = $"expected '{requiredName}', found '{actualName}'; {reason}";
        }

        function.Report(requirement, code, at: failure.At, span: failure.Span ?? (failure.At is null ? function.DispatchModifierSpan : null), evidence: [reason], related: related);
    }
}
