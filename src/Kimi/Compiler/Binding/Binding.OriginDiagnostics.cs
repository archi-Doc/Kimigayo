// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 15.3.2: the projections of a slot their Type does not declare, with that Type.
    // Recorded only on failure and reused across passes; dependent checks use the failed Origin contract's cause.
    private Dictionary<Koto, BoundType>? absentSlotProjections;

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

    // SPEC 10.6, 15.3.6: a reference slot that only per-call Origins of the fixed expected call signature S would satisfy, at the
    // reference. The Reason names the slot and the parameters of S, the Note shows S, the declaration and the written parameters of S
    // are related. No candidate is offered.
    private static void ReportPerCallSlot(Koto use, PerCallSlotFact fact, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var slot = fact.Declaration.GenericArguments[fact.Slot].Identifier;
        var inputs = fact.Signature.Components[0].Components;
        var count = System.Numerics.BitOperations.PopCount(fact.Parameters);
        var related = new (string Role, Koto At, string? Label)[count + 1];
        related[0] = ("declaration", fact.Declaration, null);
        var relatedCount = 1;
        var builder = default(IndentedStringBuilder);
        try
        {
            // The ordinals of the parameters, as "1st" or "1st and 2nd", and each with its Type for the Note.
            var shown = 0;
            for (var i = 0; i < inputs.Count && i < 64; i++)
            {
                if ((fact.Parameters & (1UL << i)) == 0)
                {
                    continue;
                }

                builder.Append(shown == 0 ? string.Empty : shown == count - 1 ? " and " : ", ");
                builder.Append(Ordinal(i + 1));
                shown++;
                if (OmittedInput(inputs[i].Origin!) is { } written)
                {
                    related[relatedCount++] = ("parameter", written, null);
                }
            }

            var ordinals = builder.ToString();
            builder.Clear();
            shown = 0;
            for (var i = 0; i < inputs.Count && i < 64; i++)
            {
                if ((fact.Parameters & (1UL << i)) != 0)
                {
                    builder.Append(shown == 0 ? string.Empty : shown == count - 1 ? " and " : ", ");
                    builder.Append(Ordinal(i + 1));
                    builder.Append(" parameter ");
                    builder.Append(DiagnosticText.Bound(DiagnosticTypeName(inputs[i]), 48).Text);
                    shown++;
                }
            }

            var shownParameters = builder.ToString();
            var note = $"The fixed expected call signature {DiagnosticText.Bound(DiagnosticTypeName(fact.Signature)).Text} binds the Origin{(count == 1 ? string.Empty : "s")} of its " +
                $"{shownParameters} at each call, and a per-call Origin never becomes part of a bound Type argument (SPEC 10.5, 15.3.6)";
            var evidence = count == 1 ? $"only the {ordinals} parameter's per-call Origin would satisfy Type parameter '{slot}'"
                : $"only the per-call Origins of the {ordinals} parameters would satisfy Type parameter '{slot}'";
            use.Report(requirement, code, note: note, evidence: [evidence], related: related.AsSpan(0, relatedCount).ToArray());
        }
        finally
        {
            builder.Dispose();
        }

        static string Ordinal(int n) => n + (n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }

    // SPEC 15.4.3, 15.6.1: a result fitting or Origin failure whose omitted result Origin defaulted to static because the only
    // matching borrow sits inside an Option input explains that boundary in the Note. Run only while publishing failures.
    private string? OmittedResultNote(Koto node)
    {
        if (node.BindingFailure is not (BindingFailure.TypeMismatch or BindingFailure.OriginRelation))
        {
            return null;
        }

        var returned = node as ReturnKoto ?? node.Parent as ReturnKoto;
        var function = returned is not null ? KotoHelper.ResolveTransferTarget(returned) as FunctionKoto : node.Parent as FunctionKoto;
        var syntax = function?.ReturnType;
        while (syntax is ParenthesizedTypeKoto group)
        {
            syntax = group.Type;
        }

        if (syntax is not TypeSemanticsKoto { HasOrigin: false } || function?.BoundSymbol?.Type is not
            { Origin.Kind: OriginKind.Static, Components.Count: 1 } expected)
        {
            return null;
        }

        var excluded = this.IsExcludedInputOrigin(returned?.Expression?.BoundType ?? node.BoundType, expected, function);
        if (!excluded && this.resultContexts.TryGetValue(node, out var context))
        {
            foreach (var source in context.Sources)
            {
                if (this.IsExcludedInputOrigin(source, expected, function))
                {
                    excluded = true;
                    break;
                }
            }
        }

        return excluded ? "The omitted result Origin is static: an Origin inside an Option input is not an elision candidate (SPEC 15.4.3)" : null;
    }

    private bool IsExcludedInputOrigin(BoundType? actual, BoundType expected, FunctionKoto function)
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

    // SPEC 15.3.2: a storage name that no declaration of any role matches is reported once, at the name. The Reason says that own
    // slots are declared only in the header; the header,
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
            use.Report(requirement, code, note: SelfNote, evidence: ["self is not a storage Origin"]);
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
        var note = $"{name} is declared neither in the header of {type.Name} nor as a visible enclosing Origin; a Type declares its own Origin slots only in its header (SPEC 15.3.2)";
        var related = slots is OriginNameList { HeaderSpan.Length: > 0 } written
            ? ("header", (Koto)type, written.HeaderSpan, "the Origin header")
            : ("type", type, TypeNameSpan(type), "the Type, which writes no Origin header");
        use.Report(requirement, code, note: note, evidence: [$"{name} is not a declared Origin slot"], relatedSpans: [related]);

        return true;
    }

    private void RecordAbsentSlot(Koto projection, BoundType type)
        => (this.absentSlotProjections ??= new(ReferenceEqualityComparer.Instance))[projection] = type;

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
        (string Role, Koto In, SourceSpan Span, string? Label)[]? related = symbol.Declaration is DeclarationContainerKoto declaration
            ? [declaration.OriginNames is OriginNameList { HeaderSpan.Length: > 0 } written ? ("header", declaration, written.HeaderSpan, "the Origin header") : ("type", declaration, TypeNameSpan(declaration), "the Type, which writes no Origin header")]
            : null;
        node.Report(requirement, code, note: note, at: slot, evidence: [$"{symbol.Name} has no Origin slot {slot.IdentifierName}"], relatedSpans: related);
        return true;
    }
}
