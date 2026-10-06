// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    private static bool MayReplaceCallable(in BoundArgumentOperation argument, Koto path)
        => argument.ParameterType?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq && argument.Source is { } source && CallablePathPrefix(source, path);

    private static bool CallablePathPrefix(Koto prefix, Koto path)
    {
        path = CallablePath(path);
        while (!SameCallablePath(prefix, path))
        {
            if (path is not MemberAccessKoto { BoundSymbol.Property.IsStored: true } member)
            {
                return false;
            }

            path = CallablePath(member.Left);
        }

        return true;
    }

    private static bool SameCallablePath(Koto left, Koto right)
    {
        left = CallablePath(left);
        right = CallablePath(right);
        return left.BoundSymbol is not null && ReferenceEquals(left.BoundSymbol, right.BoundSymbol) &&
            ((left is IdentifierNameKoto && right is IdentifierNameKoto) ||
                (left is MemberAccessKoto { BoundSymbol.Property.IsStored: true } a && right is MemberAccessKoto b && SameCallablePath(a.Left, b.Left)));
    }

    private static Koto CallablePath(Koto syntax)
    {
        var path = KotoHelper.UnwrapParentheses(syntax);
        while (path is ConversionKoto { ConversionBinding: ConversionBinding.Borrow or ConversionBinding.Follow } conversion)
        {
            path = KotoHelper.UnwrapParentheses(conversion.Left);
        }

        return path;
    }

    // SPEC 8.4.10.7: sharing a borrow root does not prove two callable values identical. Keep the complete stored Field
    // path, and exclude no earlier result when that path can be replaced, moved or exposed to another exclusive call.
    // This proof is deliberately structural: aliases and control-flow-sensitive replacement require value-flow evidence.
    private bool SameUnreplacedCallable(in OwnershipValueIdentity left, in OwnershipValueIdentity right)
    {
        if (left.Root < 0 || left.Root != right.Root || left.CallablePath is not { } first || right.CallablePath is not { } second || !SameCallablePath(first, second))
        {
            return false;
        }

        for (var i = 0; i < this.Operations.Count; i++)
        {
            var source = this.Operations[i].Source;
            switch (source)
            {
                case BinaryKoto assignment when assignment.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals:
                    if (CallablePathPrefix(assignment.Left, first))
                    {
                        return false;
                    }

                    break;
                case ConversionKoto { ConversionBinding: ConversionBinding.Transfer } transfer:
                    if (CallablePathPrefix(transfer.Left, first))
                    {
                        return false;
                    }

                    break;
                case ConversionKoto { ConversionBinding: ConversionBinding.Borrow, BoundType.Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq } borrow:
                    if (borrow.Parent is not InvocationKoto { BoundValueCall: { } own } || !ReferenceEquals(own.Receiver, borrow))
                    {
                        if (CallablePathPrefix(borrow.Left, first))
                        {
                            return false;
                        }
                    }

                    break;
                case InvocationKoto { BoundCall: { } call }:
                    if (MayReplaceCallable(call.ReceiverOperation, first))
                    {
                        return false;
                    }

                    for (var a = 0; a < call.ArgumentOperations.Length; a++)
                    {
                        if (MayReplaceCallable(call.ArgumentOperations[a], first))
                        {
                            return false;
                        }
                    }

                    break;
                case InvocationKoto { BoundValueCall: { } callable }:
                    for (var a = 0; a < callable.Arguments.Length; a++)
                    {
                        if (MayReplaceCallable(callable.Arguments[a], first))
                        {
                            return false;
                        }
                    }

                    break;
            }
        }

        return true;
    }
}
