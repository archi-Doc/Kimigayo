// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    /// <summary>Gets the facts of a try payload that does not fit its use: the payload Type, when known, and the note that
    /// forwarding the complete operand may be meant.</summary>
    /// <param name="propagation">The try expression.</param>
    /// <param name="result">The Type its enclosing result expects.</param>
    /// <returns>The evidence of <c>TryPayloadMismatch_Kd</c>, or <see langword="null"/> when the payload is unknown, and the Note.</returns>
    internal static (object?[]? Evidence, string? Note) TryPayloadFacts(TryKoto propagation, ControlFlowType? result)
    {
        var operand = propagation.Expression.BoundType;
        var payload = operand is { Components.Count: > 0 } ? operand.Components[0].Name : propagation.BoundType?.Name;
        var note = ReferenceEquals(result, operand) ? "Forwarding the complete operand without try is another candidate when it supplies the function result." : null;
        return (payload is null ? null : [payload], note);
    }

    // SPEC 17: the requirement of try that failed, as one code per requirement with its evidence.
    private (DiagnosticCode Code, object?[]? Evidence, string? Note) TryFailure(Koto node)
    {
        var propagation = (TryKoto)(node is TryKoto ? node : node.Parent!);
        var operand = propagation.Expression.BoundType;
        if (operand?.Kind != BoundTypeKind.Constructed || operand.Semantics != SemanticsKind.Owner ||
            (operand.Symbol != this.Library.Option && operand.Symbol != this.Library.Result))
        {
            return (DiagnosticCode.InvalidTry_Kd, operand is null ? null : [operand.Name], null);
        }

        var target = KotoHelper.ResolveTransferTarget(propagation.Failure);
        var result = target is PropertyAccessorKoto accessor ? Accessor(accessor).Result : target?.BoundSymbol?.Type;
        if (node is ReturnKoto)
        {
            return (DiagnosticCode.InvalidTryReturn_Kd, result is null ? null : [operand.Name, result.Name], null);
        }

        var (evidence, note) = TryPayloadFacts(propagation, result);
        return (DiagnosticCode.TryPayloadMismatch_Kd, evidence, note);
    }
}
