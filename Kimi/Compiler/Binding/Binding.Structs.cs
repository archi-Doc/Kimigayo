// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<FunctionKoto, BindingSymbol> specialReceivers = new(ReferenceEqualityComparer.Instance);

    internal BindingSymbol? SpecialReceiver(FunctionKoto function) => this.specialReceivers.GetValueOrDefault(function);

    internal BoundType? InstantiateStorageType(BoundType type, BoundCall call)
        => this.MemberType(type, call.DeclaringType) is { } member
            ? this.SubstituteType(member, call.Target.Declaration, call.TypeArguments, call.LengthArguments) : null;

    private static bool IsSpecialField(Koto node, out FunctionKoto function)
    {
        if (node is MemberAccessKoto { Left: IdentifierNameKoto { BoundSymbol: { Name: "self", Declaration: FunctionKoto receiver } } } &&
            (receiver.IsConstructor || receiver.IsDestructor))
        {
            function = receiver;
            return true;
        }

        function = null!;
        return false;
    }

    private void IndexSpecialReceiver(FunctionKoto function, BindingScope scope)
    {
        if (!function.IsConstructor && !function.IsDestructor)
        {
            return;
        }

        if (!this.specialReceivers.TryGetValue(function, out var receiver))
        {
            this.specialReceivers.Add(function, receiver = new("self", BindingSymbolKind.Parameter, function, scope));
        }

        receiver.Scope = scope;
        receiver.Type = scope.Parent?.Owner.BoundSymbol?.Type;
        if (!scope.Values.TryAdd("self", receiver))
        {
            Fail(function, BindingFailure.Duplicate);
        }
    }

    private BindingSymbol? ConstructorMember(MemberAccessKoto member, BindingScope scope)
    {
        if (member.Parent is not InvocationKoto invocation || !ReferenceEquals(invocation.Method, member))
        {
            Fail(member, BindingFailure.NotCallable);
            return null;
        }

        var qualifier = this.TypeName(member.Left, scope, false);
        var type = qualifier is null ? null : this.EnumQualifierType(member.Left, qualifier, scope, null);
        if (type is not { Semantics: SemanticsKind.Owner, Symbol.Declaration: StructKoto declaration } || declaration.Bases.Count != 0 ||
            !this.scopes[declaration].Values.TryGetValue("init", out var constructor))
        {
            Fail(member, BindingFailure.Unsupported);
            return null;
        }

        member.Right.BindingState = BindingState.Resolved;
        member.Right.BoundSymbol = constructor;
        member.BoundSymbol = constructor;
        return constructor;
    }

    private BoundType? ConstructorType(InvocationKoto call, FunctionKoto function, BindingScope scope)
    {
        var owner = (StructKoto)function.BoundSymbol!.Scope.Owner;
        var type = ((MemberAccessKoto)call.Method).Left.BoundType!;
        var count = owner.OriginNames.Count;
        if (count == 0)
        {
            return type;
        }

        var origins = this.originScratch.Rent(count);
        var used = this.flagScratch.Rent(function.Parameters.Count);
        Array.Clear(origins, 0, count);
        Array.Clear(used, 0, function.Parameters.Count);
        try
        {
            var next = 0;
            for (var i = 0; i < call.ArgumentNodes.Count; i++)
            {
                var slot = -1;
                if (call.GetArgumentLabel(i) is { } label)
                {
                    for (var j = 0; j < function.Parameters.Count; j++)
                    {
                        if (function.Parameters[j].ExternalName == label)
                        {
                            slot = j;
                            break;
                        }
                    }
                }
                else
                {
                    while (next < function.Parameters.Count && used[next])
                    {
                        next++;
                    }

                    slot = next++;
                }

                if ((uint)slot >= (uint)function.Parameters.Count || used[slot])
                {
                    return null;
                }

                used[slot] = true;
                if (function.Parameters[slot].Type.BoundType is { } pattern && call.ArgumentNodes[i].BoundType is { } actual &&
                    this.AdaptInput(call.ArgumentNodes[i], pattern, actual, scope, null, null, out var adapted, out _, out _))
                {
                    this.MatchInputOrigins(pattern, adapted, owner, origins, []);
                }
            }

            for (var i = 0; i < count; i++)
            {
                if (origins[i] is null)
                {
                    return null;
                }
            }

            return this.WithOrigins(type, null, origins.AsSpan(0, count));
        }
        finally
        {
            this.flagScratch.Return(used);
            this.originScratch.Return(origins, clearArray: true);
        }
    }
}
