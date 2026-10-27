// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The declaration owning structural inference is independent of the binder owning the Origins: a function owns its slots and a
    // constructor's structure owns them; an enum constructing a Case owns its own (SPEC 10.8.1).
    private static Koto CallSlotOwner(Koto binder)
        => binder is FunctionKoto { IsConstructor: true } constructor ? constructor.BoundSymbol!.Scope.Owner : binder;

    private static IReadOnlyList<TypeKoto> CallOwnSlots(FunctionKoto function)
        => function.IsConstructor ? ((StructKoto)CallSlotOwner(function)).GenericParameterNodes : function.GenericArguments;

    private static int CallSlotCount(Koto binder)
        => binder is FunctionKoto { IsConstructor: false } function
            ? function.GenericArguments.Count : CallSlotOwner(binder).BoundSymbol!.Schema!.GenericSlots.Count;

    // The Origin slots a binder binds: a function's, or a declaration's.
    private static int CallOriginCount(Koto binder) => binder is FunctionKoto function ? function.Origins.Count : binder.BoundSymbol!.Schema!.Origins.Count;

    // Constructor slots already include their fixed enclosing environment. Apply structural
    // bindings once, to declaration patterns; never substitute inside an already-bound argument.
    private BoundType? CallMemberPattern(BoundType type, Koto binder, BoundType? declaringType)
        => !ReferenceEquals(CallSlotOwner(binder), binder) && declaringType is not null
            ? this.SubstituteStoredOrigins(type, CallSlotOwner(binder), (BoundOrigin[])declaringType.OriginArguments)
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

        return this.types.Rebuild(template, ((BoundType[])(object)slots).AsSpan(0, count), template.Origin, (BoundOrigin[])template.OriginArguments);
    }

    private void InitializeCallSlots(InvocationKoto call, FunctionKoto function, BoundType? declaringType, BoundType?[] slots, bool fixedConstruction = false)
    {
        var count = CallSlotCount(function);
        slots.AsSpan(0, count).Clear();
        if (!function.IsConstructor || declaringType is null)
        {
            return;
        }

        var infer = !fixedConstruction && this.InferredConstructorTarget(call.Method) is not null;
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
