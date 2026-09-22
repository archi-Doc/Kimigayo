// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private bool BindSequenceMember(MemberAccessKoto source, BindingScope scope, out BoundType? result)
    {
        result = null;
        if (source.Right is not IdentifierNameKoto name || name.IdentifierName is not ("indices" or "length" or "isEmpty" or "start" or "end"))
        {
            return false;
        }

        var receiver = this.BindNode(source.Left, scope);
        if (ReferenceTypes.IsArray(receiver))
        {
            receiver = receiver!.Components[0];
        }

        if (receiver?.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.ResolvedRange or BoundTypeKind.Slice))
        {
            return false;
        }

        var range = receiver.Kind == BoundTypeKind.ResolvedRange;
        var valid = name.IdentifierName switch
        {
            "indices" => !range,
            "length" => true,
            "isEmpty" => receiver.Kind != BoundTypeKind.FixedArray,
            _ => range,
        };
        if (!valid)
        {
            result = Fail(source, BindingFailure.MissingName);
            return true;
        }

        result = name.IdentifierName == "indices" ? BoundType.ResolvedRange : name.IdentifierName == "isEmpty" ? BoundType.Boolean : BoundType.ISize;
        Complete(name, result);
        Complete(source, result);
        return true;
    }

    private BoundType? BindIteration(ForKoto source, BindingScope scope)
    {
        var iterable = this.BindNode(source.Iterable, scope);
        var element = iterable?.Kind == BoundTypeKind.FixedArray ? iterable.Components[0] : iterable?.Kind == BoundTypeKind.Slice
            ? this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [iterable.Components[0]], origin: iterable.Origin) : BoundType.ISize;
        var result = this.BeginResult(source, scope, BoundType.Unit);
        for (var i = 0; i < source.Bindings.Count; i++)
        {
            var name = source.Bindings[i];
            var slot = source.IsTupleBinding && element.Kind == BoundTypeKind.Tuple && i < element.Components.Count ? element.Components[i] : element;
            name.BoundSymbol!.Type = slot;
            Complete(name, slot);
        }

        this.BindNode(source.Body, scope);
        if (source.IsTupleBinding && (element.Kind != BoundTypeKind.Tuple || element.Components.Count != source.Bindings.Count))
        {
            return Fail(source, BindingFailure.TypeMismatch);
        }

        if (iterable?.Kind is not (BoundTypeKind.ResolvedRange or BoundTypeKind.FixedArray or BoundTypeKind.Slice))
        {
            return Fail(source, BindingFailure.Unsupported);
        }

        return this.FinishResult(source, result);
    }
}
