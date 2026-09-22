// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private string DescribeTryFailure(Koto node)
    {
        var propagation = (TryKoto)(node is TryKoto ? node : node.Parent!);
        var operand = propagation.Expression.BoundType;
        if (operand?.Kind != BoundTypeKind.Constructed || operand.Semantics != SemanticsKind.Owner ||
            (operand.Symbol != this.Library.Option && operand.Symbol != this.Library.Result))
        {
            return $"try requires an independently resolved owned Kimi Option or Result operand; received {operand?.Name ?? "an unresolved Type"}. Use explicit Type arguments or an annotated intermediate when inference is incomplete. Calls and member selection bind before try.";
        }

        var target = KotoHelper.ResolveTransferTarget(propagation.Failure);
        var result = target is PropertyAccessorKoto accessor ? Accessor(accessor).Result : target?.BoundSymbol?.Type;
        if (node is ReturnKoto)
        {
            return $"The failure path of try {operand.Name} cannot return to {result?.Name ?? "this position"}. Use a compatible Option/Result return Type and ordinary error fitting; explicitly wrap normal success values where needed.";
        }

        var wrap = operand.Symbol == this.Library.Option ? "Some" : "Ok";
        var forward = ReferenceEquals(result, operand) ? " Forwarding the complete operand without try is another candidate when it supplies the function result." : string.Empty;
        return $"The normal value of try has payload Type {operand.Components[0].Name}, which does not fit this use. At an Option/Result return, consider .{wrap}(try ...) and an appropriate return annotation.{forward} Recheck Type inference, ownership and cleanup after changing the expression.";
    }
}
