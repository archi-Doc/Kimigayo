// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The declaration owning structural inference is independent of the function owning call Origins.
    private static Koto CallSlotOwner(FunctionKoto function)
        => function.IsConstructor ? function.BoundSymbol!.Scope.Owner : function;

    private static IReadOnlyList<TypeKoto> CallOwnSlots(FunctionKoto function)
        => function.IsConstructor ? ((StructKoto)CallSlotOwner(function)).GenericParameterNodes : function.GenericArguments;

    private static int CallSlotCount(FunctionKoto function)
        => function.IsConstructor ? CallSlotOwner(function).BoundSymbol!.Schema!.GenericSlots.Count : function.GenericArguments.Count;

    // Constructor slots already include their fixed enclosing environment. Apply structural
    // bindings once, to declaration patterns; never substitute inside an already-bound argument.
    private BoundType? CallMemberPattern(BoundType type, FunctionKoto function, BoundType? declaringType)
        => function.IsConstructor && declaringType is not null
            ? this.SubstituteStoredOrigins(type, CallSlotOwner(function), (BoundOrigin[])declaringType.OriginArguments)
            : this.MemberType(type, declaringType);

    private BoundType? ConstructionType(FunctionKoto function, BoundType template, BoundType?[] slots)
    {
        var count = CallSlotCount(function);
        for (var i = 0; i < count; i++)
        {
            if (slots[i] is null)
            {
                return null;
            }
        }

        return this.InternType(template.Kind, template.Symbol, template.Semantics, ((BoundType[])(object)slots).AsSpan(0, count), template.Length, template.Origin, (BoundOrigin[])template.OriginArguments, template.LengthExpression, template.ClosureContext, template.LengthArguments, template.ResultMode);
    }

    private void InitializeCallSlots(InvocationKoto call, FunctionKoto function, BoundType? declaringType, BoundType?[] slots, bool fixedConstruction = false)
    {
        var count = CallSlotCount(function);
        slots.AsSpan(0, count).Clear();
        if (!function.IsConstructor || declaringType is null)
        {
            return;
        }

        var infer = !fixedConstruction && call.Method is MemberAccessKoto member && this.inferredConstructorTargets.Contains(member.Left);
        var own = CallOwnSlots(function).Count;
        var owner = CallSlotOwner(function);
        for (var i = 0; i < count; i++)
        {
            var fixedType = declaringType.Components[i];
            // An outer slot is always a fixed binding, even when it is itself a parameter.
            if (!infer || i >= own || fixedType.Kind != BoundTypeKind.Parameter || !ReferenceEquals(fixedType.Symbol?.Scope.Owner, owner))
            {
                slots[i] = fixedType;
            }
        }
    }
}
