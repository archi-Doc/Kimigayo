// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BoundMemberPath? Parent, Koto Base, BoundType Type), BoundMemberPath> memberPaths = new();
    private readonly HashSet<BindingSymbol> memberLookupVisiting = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MemberAccessKoto, MemberSelection> memberSelections = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BindingSymbol, byte> inheritanceStates = new(ReferenceEqualityComparer.Instance);

    private void ValidateBaseDeclarations()
    {
        this.inheritanceStates.Clear();
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is StructKoto structure)
            {
                this.ValidateBaseDeclaration(structure);
            }
        }
    }

    private bool ValidateBaseDeclaration(StructKoto structure)
    {
        var symbol = structure.BoundSymbol!;
        if (this.inheritanceStates.TryGetValue(symbol, out var state))
        {
            return state == 2;
        }

        this.inheritanceStates.Add(symbol, 1);
        var valid = structure.Bases.Count <= 1;
        var scope = this.scopes[structure];
        for (var i = 0; i < structure.Bases.Count; i++)
        {
            var syntax = structure.Bases[i];
            var type = this.BindType(syntax, scope);
            if (type is not { Kind: BoundTypeKind.Nominal or BoundTypeKind.Constructed, Symbol.Declaration: StructKoto parent } || (parent.Modifier & ModifierKind.Open) == 0 || !this.Accessible(parent.BoundSymbol!, scope) || !TypeAccessCovers(type, symbol, symbol) || !this.ValidateBaseDeclaration(parent))
            {
                Fail(syntax, BindingFailure.InvalidTypeFormation);
                valid = false;
            }
        }

        this.inheritanceStates[symbol] = valid ? (byte)2 : (byte)3;
        if (!valid)
        {
            Fail(structure, BindingFailure.InvalidTypeFormation);
        }

        return valid;
    }

    // Access and namespace select the layer. Receiver/argument/accessor checks never reopen it.
    private MemberSelection LookupTypeMember(BoundType type, string name, BindingScope use, BoundType? receiver = null, BoundMemberPath? path = null, bool typeRole = false)
    {
        if (type.Symbol is not { } symbol || !this.scopes.TryGetValue(symbol.Declaration, out var scope))
        {
            return default;
        }

        if ((typeRole ? scope.Types : scope.Values).TryGetValue(name, out var member))
        {
            for (var candidate = member; candidate is not null; candidate = candidate.Next)
            {
                if (this.Accessible(candidate, use, receiverType: receiver))
                {
                    return new(candidate, type, path);
                }
            }
        }

        if (symbol.Declaration is not StructKoto structure)
        {
            return default;
        }

        if (!this.memberLookupVisiting.Add(symbol))
        {
            return new(null, type, path, true);
        }

        try
        {
            MemberSelection result = default;
            for (var i = 0; i < structure.Bases.Count; i++)
            {
                var syntax = structure.Bases[i];
                var baseType = syntax.BoundType ?? this.BindType(syntax, scope);
                if (baseType is null || this.StoredType(baseType, type) is not { } substituted)
                {
                    return new(null, type, path, Pending: true);
                }

                var key = (path, syntax, substituted);
                if (!this.memberPaths.TryGetValue(key, out var next))
                {
                    this.memberPaths.Add(key, next = new(path, syntax, substituted));
                }

                var candidate = this.LookupTypeMember(substituted, name, use, receiver, next, typeRole);
                if (candidate.Ambiguous || candidate.Pending)
                {
                    return candidate;
                }

                if (candidate.Member is null)
                {
                    continue;
                }

                if (result.Member is not null)
                {
                    return new(null, type, path, true);
                }

                result = candidate;
            }

            return result;
        }
        finally
        {
            this.memberLookupVisiting.Remove(symbol);
        }
    }

    private readonly record struct MemberSelection(BindingSymbol? Member, BoundType? DeclaringType, BoundMemberPath? Path, bool Ambiguous = false, bool Pending = false);
}
