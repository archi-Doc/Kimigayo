// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 15.3.2: the projections of a slot their Type does not declare, with that Type, and the first such projection in each
    // function's clauses, on which the function's result and returns rest. Recorded only on failure and reused across passes.
    private Dictionary<Koto, BoundType>? absentSlotProjections;
    private Dictionary<FunctionKoto, Koto>? absentSlotFunctions;

    private static bool HasUngroupedBorrowSuffix(Koto syntax)
    {
        while (syntax is OptionalTypeKoto optional)
        {
            syntax = optional.Type;
        }

        return syntax is TypeSemanticsKoto { Type: not null, HasOrigin: true, IsTransparentWrapper: false };
    }

    private static bool ContainsOrigin(BoundType type, BoundOrigin origin)
    {
        if (ReferenceEquals(type.Origin, origin))
        {
            return true;
        }

        foreach (var component in type.Components)
        {
            if (ContainsOrigin(component, origin))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 15.3.2: the struct or enum whose storage (an instance Field Type, an enum payload or a base) writes this Origin name;
    // null for any other position, such as an accessor signature or an attached relation.
    private static DeclarationContainerKoto? StorageOriginType(Koto use)
    {
        var owner = OriginOwner(use);
        if (owner is PropertyKoto { DeclarationKind: PropertyDeclarationKind.Let or PropertyDeclarationKind.Var, Parent: StructKoto structure } property)
        {
            return IsWithin(use, property.TypeKoto) ? structure : null;
        }

        // A Case's payload list is an inner form of the same kind as the Case.
        while (owner is { Akind: KotoKind.EnumCase, Parent: { Akind: KotoKind.EnumCase } outer })
        {
            owner = outer;
        }

        if (owner is { Akind: KotoKind.EnumCase, Parent: EnumKoto enumeration })
        {
            return enumeration;
        }

        if (owner is StructKoto { Bases: { } bases } declared)
        {
            foreach (var parent in bases)
            {
                if (IsWithin(use, parent))
                {
                    return declared;
                }
            }
        }

        return null;

        static bool IsWithin(Koto node, Koto? root)
        {
            for (var current = node; current is not null; current = current.Parent)
            {
                if (ReferenceEquals(current, root))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // The Name of a struct or enum in its first declaration line, the related location of a Type without a header.
    private static SourceSpan TypeNameSpan(DeclarationContainerKoto type)
    {
        if (type.CodeContext.SourceDocument is not { } document || (uint)type.Span.Start >= (uint)document.SourceText.Length)
        {
            return type.Span;
        }

        // The declaration's span may stop at its keyword; its first line holds the Name.
        var text = document.AsSpan()[type.Span.Start..];
        var lineEnd = text.IndexOfAny('\r', '\n');
        text = lineEnd < 0 ? text : text[..lineEnd];
        var keyword = text.IndexOf(type is EnumKoto ? "enum " : "struct ", StringComparison.Ordinal);
        var offset = keyword < 0 ? -1 : text[keyword..].IndexOf(type.Name, StringComparison.Ordinal);
        return offset < 0 ? type.Span : new(type.Span.Start + keyword + offset, type.Name.Length);
    }

    // Run only while publishing failures. These checks neither bind new names nor
    // change a syntax decision, and require no state on successful uses.
    private string? BorrowOriginHint(Koto node)
    {
        if (node is TypeSemanticsKoto { IsLegacyBorrowCandidate: true } legacy &&
            (legacy.BoundSymbol?.Kind == BindingSymbolKind.SemanticsParameter ||
            (legacy.BoundSymbol is null && legacy.BindingFailure == BindingFailure.MissingType && CompilerHelper.TryParse(legacy.Identifier, out _))))
        {
            return "This may be a removed brace borrow annotation. Use 's/T during a' in a Type; an adaptation's outer Origin must be inferred with 'x@s/T'.";
        }

        if (node.BindingFailure == BindingFailure.InvalidOrigin && node is IdentifierNameKoto
            { BoundSymbol: { Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture, Type: { } valueType } } &&
            !IsBorrow(valueType.Semantics))
        {
            return "This value's name does not denote an outer borrow Origin. Use a declared schema slot such as 'x.slot', or borrow local storage with 'let r = x@ref' and infer its Origin. Borrowing does not extend its lifetime.";
        }

        if (node.Parent is AndKoto conjunction && ReferenceEquals(conjunction.Right, node) &&
            HasUngroupedBorrowSuffix(conjunction.Left) && this.IsExistingOriginForHint(node))
        {
            return "An Origin intersection requires parentheses: 'during (a and b)'. Here 'and' separates constraint requirements.";
        }

        if (node.BindingFailure == BindingFailure.TypeMismatch)
        {
            var returned = node as ReturnKoto ?? node.Parent as ReturnKoto;
            var function = returned is not null ? KotoHelper.ResolveTransferTarget(returned) as FunctionKoto : node.Parent as FunctionKoto;
            var syntax = function?.ReturnType;
            while (syntax is ParenthesizedTypeKoto group)
            {
                syntax = group.Type;
            }

            if (syntax is TypeSemanticsKoto { HasOrigin: false } && function?.BoundSymbol?.Type is
                { Origin.Kind: OriginKind.Static, Components.Count: 1 } expected)
            {
                if (IsExcludedInputOrigin(returned?.Expression?.BoundType ?? node.BoundType) ||
                    (this.resultContexts.TryGetValue(node, out var context) && context.Sources.Exists(IsExcludedInputOrigin)))
                {
                    return "The omitted result Origin is static; Origins inside Option inputs are not elision candidates. Write an explicit result 'during' annotation using the required named input Origins, then recheck lifetime and Type fitting.";
                }

                bool IsExcludedInputOrigin(BoundType? actual)
                {
                    if (actual is not { Origin.Kind: not OriginKind.Static, Components.Count: 1 } ||
                        actual.Semantics != expected.Semantics || !IsBorrow(actual.Semantics) ||
                        !ReferenceEquals(actual.Components[0], expected.Components[0]))
                    {
                        return false;
                    }

                    foreach (var parameter in function.Parameters)
                    {
                        if (parameter.Type.BoundType is { } input && input.Symbol == this.Library.Option && ContainsOrigin(input, actual.Origin))
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }
        }

        return null;
    }

    // SPEC 15.3.2: a storage name that no declaration of any role matches is reported once, at the name. The Reason says that own
    // slots are declared only in the header; the Advice gives both repairs with their consequence for the public API; the header,
    // or the Type name without one, is the related location. `during self` is explained by the storage rule of SPEC 11.3 instead.
    private bool ReportUndeclaredStorageOrigin(Koto use, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        if (use is not IdentifierNameKoto { IdentifierName: var name } || StorageOriginType(use) is not { } type)
        {
            return false;
        }

        if (name == "self")
        {
            const string SelfNote = "A stored value cannot borrow from the value that stores it: self creates no self-borrowing storage contract (SPEC 11.3)";
            const string SelfAdvice = "Store an owned value, or declare a header slot for the lifetime of the borrowed referent and bind the Field to it";
            use.Report(requirement, code, note: SelfNote, evidence: ["self is not a storage Origin"], advice: SelfAdvice);
            return true;
        }

        // The Origins the name may have meant: the Type's own slots and the slots of the enclosing Types, nearest first.
        var known = string.Empty;
        for (var current = (Koto?)type; current is DeclarationContainerKoto container; current = current.Parent)
        {
            for (var i = 0; i < container.OriginNames.Count; i++)
            {
                known = known.Length == 0 ? container.OriginNames[i] : $"{known}, {container.OriginNames[i]}";
            }
        }

        var slots = type.OriginNames;
        var header = slots.Count == 0 ? $"{{{name}}}" : $"{{{string.Join(", ", slots)}, {name}}}";
        var advice = (known.Length == 0 ? string.Empty : $"If an existing Origin was intended ({known}), write its name instead; the slot declaration does not change. ") +
            $"If a new slot was intended, add {name} to the header, as {type.Name} {header}; this changes the public API, and a member signature that uses {name} as a universal Origin is then bound to the new slot";
        var note = $"{name} is declared neither in the header of {type.Name} nor as a visible enclosing Origin; a Type declares its own Origin slots only in its header (SPEC 15.3.2)";
        var related = slots is OriginNameList { HeaderSpan.Length: > 0 } written
            ? ("header", (Koto)type, written.HeaderSpan, "the Origin header")
            : ("type", type, TypeNameSpan(type), "the Type, which writes no Origin header");
        use.Report(requirement, code, note: note, evidence: [$"{name} is not a declared Origin slot"], advice: advice, relatedSpans: [related]);

        return true;
    }

    private void RecordAbsentSlot(Koto projection, BoundType type)
    {
        (this.absentSlotProjections ??= new(ReferenceEqualityComparer.Instance))[projection] = type;
        if (OriginOwner(projection) is FunctionKoto function)
        {
            (this.absentSlotFunctions ??= new(ReferenceEqualityComparer.Instance)).TryAdd(function, projection);
        }
    }

    // SPEC 15.3.2: a function whose clauses project a slot their Type does not declare leaves its result slot unbound and its
    // returned values without the intended contract; those failures rest on the projection, not on independent causes.
    private bool RestsOnAbsentSlot(Koto node)
    {
        if (this.absentSlotFunctions is not { Count: > 0 } functions || node.BindingFailure is not (BindingFailure.MissingOrigin or BindingFailure.TypeMismatch))
        {
            return false;
        }

        var returned = false;
        var inResult = false;
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is FunctionKoto function)
            {
                // A returned value is affected only where it differs from the result in its bindings, not in its Type.
                if (!functions.TryGetValue(function, out var projection) ||
                    !(node.BindingFailure == BindingFailure.MissingOrigin ? inResult
                        : returned && this.mismatches?.TryGetValue(node, out var mismatch) == true && mismatch.Actual is BoundType { Symbol: { } actual } &&
                            mismatch.Expected is BoundType { Symbol: var expected } && ReferenceEquals(actual, expected)))
                {
                    return false;
                }

                this.partPrerequisites[node] = projection;
                return true;
            }

            returned |= current is ReturnKoto;
            inResult |= current.Parent is FunctionKoto owner && ReferenceEquals(owner.ReturnType, current);
        }

        return false;
    }

    // SPEC 15.3.2: a projection of a slot that its Type does not declare, once per projection at the slot name, relating the
    // Type's header and listing the declared slots.
    private bool ReportAbsentSlot(Koto node, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        if (this.absentSlotProjections?.TryGetValue(node, out var type) != true || node is not MemberAccessKoto { Right: IdentifierNameKoto slot } ||
            type!.Symbol is not { Schema: { } schema } symbol)
        {
            return false;
        }

        var declared = string.Empty;
        for (var i = 0; i < schema.Origins.Count; i++)
        {
            declared = declared.Length == 0 ? schema.Origins[i].Name : $"{declared}, {schema.Origins[i].Name}";
        }

        var note = declared.Length == 0 ? $"{symbol.Name} declares no Origin slot" : $"The Origin slots of {symbol.Name} are {declared}";
        var advice = declared.Length == 0
            ? $"Remove the projection, or declare the slot in the header of {symbol.Name}, which changes its public API"
            : $"Write a declared slot of {symbol.Name}, such as {schema.Origins[0].Name}, instead of {slot.IdentifierName}";
        (string Role, Koto In, SourceSpan Span, string? Label)[]? related = symbol.Declaration is DeclarationContainerKoto declaration
            ? [declaration.OriginNames is OriginNameList { HeaderSpan.Length: > 0 } written ? ("header", declaration, written.HeaderSpan, "the Origin header") : ("type", declaration, TypeNameSpan(declaration), "the Type, which writes no Origin header")]
            : null;
        node.Report(requirement, code, note: note, at: slot, evidence: [$"{symbol.Name} has no Origin slot {slot.IdentifierName}"], advice: advice, relatedSpans: related);
        return true;
    }

    private bool IsExistingOriginForHint(Koto node)
    {
        var name = node is IdentifierNameKoto identifier ? identifier.IdentifierName :
            node is MemberAccessKoto { Left: IdentifierNameKoto left, Right: IdentifierNameKoto } ? left.IdentifierName : null;
        if (name is null)
        {
            return false;
        }

        for (var scope = this.ConstraintScope(node); scope is not null; scope = scope.Parent)
        {
            BoundType? carrier = null;
            var found = false;
            if (scope.Origins?.ContainsKey(name) == true)
            {
                return node is IdentifierNameKoto;
            }

            if (scope.Values.TryGetValue(name, out var value))
            {
                carrier = value.Type;
                found = true;
            }
            else if (this.originDeclarations.GetValueOrDefault(scope.Owner)?.Sets.TryGetValue(name, out var set) == true)
            {
                carrier = set.BoundType;
                found = true;
            }
            else if (scope.OriginSets?.TryGetValue(name, out var localSet) == true)
            {
                carrier = localSet.BoundType;
                found = true;
            }

            if (!found)
            {
                continue;
            }

            if (node is IdentifierNameKoto)
            {
                return carrier?.Origin is not null && IsBorrow(carrier.Semantics);
            }

            while (carrier is { Kind: BoundTypeKind.Semantics } && IsBorrow(carrier.Semantics))
            {
                carrier = carrier.Components[0];
            }

            if (carrier?.Symbol?.Schema is { } schema && node is MemberAccessKoto { Right: IdentifierNameKoto slot })
            {
                for (var i = 0; i < schema.Origins.Count; i++)
                {
                    if (schema.Origins[i].Name == slot.IdentifierName)
                    {
                        return carrier.Kind == BoundTypeKind.Slice ? carrier.Origin is not null : i < carrier.OriginArguments.Count;
                    }
                }
            }

            return false;
        }

        return false;
    }
}
