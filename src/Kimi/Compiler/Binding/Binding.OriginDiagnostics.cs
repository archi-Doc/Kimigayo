// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Origin failure explanations (SPEC 5.7, 10.6, 15.2.3, 15.3.2, 15.4.3, 23.1): the parts of an Origin failure's record beyond the
// typed facts its check records.
// - Responsibility: the records of a per-call slot (ReportPerCallSlot), an undeclared storage Origin name
//   (ReportUndeclaredStorageOrigin, StorageOriginType, TypeNameSpan), an absent slot (RecordAbsentSlot, ReportAbsentSlot) and an
//   invalid slot (ReportInvalidOriginSlot); the omitted-result Note (OmittedResultNote); an Origin relation record's end displays
//   (OriginDisplay, SlotProjection, OriginIncludes) and the Borrow that supplies an end (BorrowSource, LocalBorrow), which the
//   records of Binding.Diagnostics.cs show and relate.
// - Inputs: the failing node and the facts its check recorded (absentSlotProjections, invalidStaticOriginSlots, phantomRawOrigins,
//   PerCallSlotFact), the result contexts, the bound syntax.
// - Outputs: the records with their Notes, evidence and related locations; the Borrow node.
// - Invariants: a fact is recorded where its check fails (RecordAbsentSlot); everything else runs only while a failure's record is
//   formed.
// - Prohibitions: no inference, proof or judgment re-run to explain a failure; no failure decided here.

using System.Text;
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

    // The first Borrow in a value whose Origin supplies `supplier`, which the record names and relates (BorrowSource).
    private static Koto? LocalBorrow(Koto value, BoundOrigin supplier)
    {
        var finder = new LocalBorrowFinder(supplier);
        finder.Visit(value);
        return finder.Found;
    }

    // Whether a Borrow whose Origin is `borrowed` supplies a record's end that the failing check recorded as `supplier`, before the
    // receiver resolution (OriginRelationFact.Supplier): one holds the other, by identity; BorrowSource's one comparison (R4-M133).
    private static bool Supplies(BoundOrigin borrowed, BoundOrigin supplier) => OriginIncludes(borrowed, supplier) || OriginIncludes(supplier, borrowed);

    // The Borrow that supplies a record's end, by the atom `supplier` the failing check recorded for it: the value itself, the Borrow
    // initializing the local it names, or the first Borrow inside the value or its root local's initializer, explicit or a call's
    // implicit Borrow of an argument. For a meet, it is the Borrow of its first body-local operand, which the meet's display shows (SPEC
    // 23.1). A local region's source is found through the bound that brought it in, by that bound's parent as written.
    private static Koto? BorrowSource(Koto value, BoundOrigin supplier)
    {
        var unwrapped = KotoHelper.UnwrapParentheses(value);
        if (unwrapped.BoundType?.Origin is { } region && IsLocalRegion(region) &&
            value.CodeContext.Compilation.Binding.LocalRegionSourceUse(region, supplier) is { } bound && !ReferenceEquals(bound.Value, value))
        {
            return BorrowSource(bound.Value, bound.Parent);
        }

        if (unwrapped is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } borrowValue)
        {
            // SPEC 23.1: at an inner position of a Borrow, such as the stored `local@ref` of `y@uniq` after `var y = local@ref`, the
            // failing Origin is not the Borrow's own: it is found through the borrowed Place.
            if (borrowValue.BoundType?.Origin is not { } own || Supplies(own, supplier))
            {
                return unwrapped;
            }
        }

        if (supplier.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < supplier.Operands.Count; i++)
            {
                if (supplier.Operands[i].Kind is OriginKind.Projection or OriginKind.Anchor && BorrowSource(value, supplier.Operands[i]) is { } operand)
                {
                    return operand;
                }
            }

            return null;
        }

        if (supplier.Kind == OriginKind.Projection &&
            unwrapped is IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local, Declaration: VariableKoto { InitializerKoto: { } assigned } } } &&
            KotoHelper.UnwrapParentheses(assigned) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow, BoundType.Origin: { } held } source &&
            Supplies(held, supplier))
        {
            return source;
        }

        // A value that contains the Borrow, such as a call or Tuple over it, names that Borrow; so does the initializer of the local
        // whose Place the value reads or moves, such as `h0@move` or `h0.item` after `let h0 = H.init(a@ref)`.
        if (supplier.Kind is not (OriginKind.Projection or OriginKind.Anchor))
        {
            return null;
        }

        return LocalBorrow(value, supplier) ?? (RootLocal(unwrapped) is { InitializerKoto: { } rootValue } ? LocalBorrow(rootValue, supplier) : null);

        static VariableKoto? RootLocal(Koto value)
        {
            for (var depth = 0; depth < 64; depth++)
            {
                switch (value)
                {
                    case ConversionKoto { ConversionBinding: ConversionBinding.Transfer or ConversionBinding.Borrow } transfer:
                        value = KotoHelper.UnwrapParentheses(transfer.Left);
                        continue;
                    case MemberAccessKoto access:
                        value = KotoHelper.UnwrapParentheses(access.Left);
                        continue;
                    case IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local, Declaration: VariableKoto local } }:
                        return local;
                    default:
                        return null;
                }
            }

            return null;
        }
    }

    // SPEC 15.2.1: the omitted slot `slot` of a function parameter's Type, written directly or under one borrow layer, is the projection
    // `p.slot`, whether a named function's input or an anonymous function's body inference variable (SPEC 15.3.4); any other omitted
    // slot has no name.
    private static string? SlotProjection(Koto occurrence, int slot)
    {
        var function = occurrence.Parent;
        while (function is not null and not FunctionKoto)
        {
            function = function.Parent;
        }

        if (function is not FunctionKoto owner || occurrence.BoundType?.Symbol?.Schema?.Origins is not { } slots || (uint)slot >= (uint)slots.Count)
        {
            return null;
        }

        foreach (var parameter in owner.Parameters)
        {
            if (ReferenceEquals(parameter.Type, occurrence) || (parameter.Type is TypeSemanticsKoto { Type: { } target } && ReferenceEquals(target, occurrence)))
            {
                return parameter.InternalName + "." + slots[slot].Name;
            }
        }

        return null;
    }

    // SPEC 23.1: an Origin by its display kind and string: an Origin expression for static, a parameter or receiver, a slot (a
    // parameter's projection, SlotProjection) or a meet; the source text of a Borrow, or of its Place, for a body-local finite Origin;
    // the Type occurrence of an omitted slot.
    private static DiagnosticOrigin OriginDisplay(BoundOrigin origin, Koto? value, bool typeLevel = false)
    {
        switch (origin.Kind)
        {
            case OriginKind.Static:
                return new("expression", "static");
            case OriginKind.Intersection:
                // A body-local operand shows the Borrow that supplies it, never the text of its binder.
                var parts = new string[origin.Operands.Count];
                for (var i = 0; i < parts.Length; i++)
                {
                    var supplied = value is not null && KotoHelper.UnwrapParentheses(value) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow, BoundType.Origin: { } borrowed } &&
                        OriginIncludes(borrowed, origin.Operands[i]) ? value : null;
                    parts[i] = OriginDisplay(origin.Operands[i], supplied, typeLevel).Text;
                }

                return new("expression", "(" + string.Join(" and ", parts) + ")");
            case OriginKind.Input or OriginKind.Inference when OmittedAt(origin) is { } occurrence:
                return SlotProjection(occurrence, origin.Kind == OriginKind.Input ? origin.TargetSlot : origin.Slot) is { } projection
                    ? new("expression", projection) : new("omitted", occurrence.ToString());
            case OriginKind.Input when origin.Binder is { } binder && origin.InputIndex >= 0 && origin.InputIndex < InputCount(binder):
                return new("expression", InputName(binder, origin.InputIndex));
            case OriginKind.Parameter when origin.Occurrence is GenericParameterKoto declaration:
                return new("omitted", declaration.ToString()); // SPEC 8.1.1, 23.1: the outer Origin `o` of a pair binder.
            case OriginKind.Parameter:
                return new("expression", origin.Binder is DeclarationContainerKoto && !typeLevel ? "self." + origin.Name : origin.Name);
            case OriginKind.Inference when origin.Binder is VariableKoto local:
                return new("omitted", local.NameKoto.IdentifierName); // A local's region (LocalRegionSlot), as a Closure body names it.
            case OriginKind.Holder:
                return new("expression", "self"); // SPEC 5.7: `during self`.
            case OriginKind.Projection when origin.Binder is FunctionKoto { IsAnonymous: true } &&
                (origin.Slot == CallResultSlot || (origin.Slot == ClosureValueSlot && value is null)): // ρ, unless a Borrow supplies it.
                return new("closure", origin.Slot == CallResultSlot ? "call result" : "call receiver");
            case OriginKind.Projection or OriginKind.Anchor:
                // The Borrow that supplies it by its text, explicit or an implicit Borrow's argument; without one, the Place: a local or a
                // parameter by name, a constructor's or destructor's receiver (and its helper borrow) as self, and a temporary by its text.
                var text = value is not null ? KotoHelper.UnwrapParentheses(value).ToString()
                    : origin.Slot is HelperSlot or ReceiverSlot ? "self"
                    : origin.Binder is VariableKoto variable ? variable.NameKoto.IdentifierName
                    : origin.Binder is FunctionKoto function && origin.Slot >= 0 && origin.Slot < function.Parameters.Count ? function.Parameters[origin.Slot].InternalName
                    : origin.Binder!.ToString(); // OriginAtom gives every Projection and Anchor its binder.
                return new("borrow", text);
            default:
                return new("omitted", origin.Occurrence?.ToString() ?? origin.Name);
        }
    }

    // Whether `atom` is `whole` or one of the operands of its meet, at any depth.
    private static bool OriginIncludes(BoundOrigin whole, BoundOrigin atom)
    {
        if (ReferenceEquals(whole, atom))
        {
            return true;
        }

        for (var i = 0; i < whole.Operands.Count; i++)
        {
            if (OriginIncludes(whole.Operands[i], atom))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 23.1.2 (DECISIONS BU-44): the Semantics cases a slot's check recorded (Binding.FailingCase) where its requirement is `failing`,
    // as ` in the Semantics case s = owner`: per binder, its Semantics joined by `or`; binders joined by `, ` where every binder must give
    // the class (`none`, the weakest: a product) and listed by `; ` where one suffices (`uniq`, the strongest: a union); empty for none, a
    // failure in every case.
    private static string CaseText(LoanRequirement failing, PairCase[] cases)
    {
        var union = failing == LoanRequirement.Uniq;
        var text = new StringBuilder();
        var several = false;
        for (var i = 0; i < cases.Length; i++)
        {
            if (i > 0 && ReferenceEquals(cases[i].Target, cases[i - 1].Target))
            {
                text.Append(" or ");
                several = true;
            }
            else
            {
                var separator = union ? "; " : ", ";
                text.Append(i == 0 ? string.Empty : separator).Append(cases[i].Target.Pair?.Name ?? cases[i].Target.Name).Append(" = ");
                several |= union && i > 0;
            }

            text.Append(cases[i].Semantics.ToText());
        }

        return cases.Length == 0 ? string.Empty : (several ? " in the Semantics cases " : " in the Semantics case ") + text;
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
            if (parameter.Type.BoundType is { } input && input.Symbol == this.Library.Option && this.MentionsOrigin(input, actual.Origin))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 15.3.2: a storage name that no declaration of any role matches is reported once, at the name. The Reason says that own
    // slots are declared only in the header; the header,
    // or the Type name without one, is the related location. `during self` is explained by the storage rule of SPEC 11.6 instead.
    private bool ReportUndeclaredStorageOrigin(Koto use, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        if (use is not IdentifierNameKoto { IdentifierName: var name } || StorageOriginType(use) is not { } type)
        {
            return false;
        }

        if (name == "self")
        {
            const string SelfNote = "A stored value cannot borrow from the value that stores it: self creates no self-borrowing storage contract (SPEC 11.6)";
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

    // SPEC 15.2.3: a static Origin bound to a slot that requires uniq (ValidateOriginRequirements); SPEC 5.7: a Phantom slot a raw layer
    // states (InvalidRawOrigin); each with that slot related and, for a conditional slot, the Semantics cases its check recorded
    // (CaseText; SPEC 23.1.2, DECISIONS BU-44).
    private bool ReportInvalidOriginSlot(Koto node, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        if (this.invalidStaticOriginSlots?.TryGetValue(node, out var invalid) == true)
        {
            var slot = invalid.Slot;
            var found = CaseText(invalid.Failing, invalid.Cases);
            node.Report(
                requirement,
                code,
                evidence: [$"static cannot supply uniq for Origin slot {slot.Name}{found}"],
                note: $"Origin slot {slot.Name} requires uniq{found} and cannot bind to static; a static Origin admits no exclusive loan (SPEC 15.2.3)",
                relatedSpans: [("Origin slot", slot.Origin.Binder!, slot.Span, "requires uniq")]);
            return true;
        }

        if (this.phantomRawOrigins?.TryGetValue(node, out var phantom) == true)
        {
            var slot = phantom.Slot;
            var found = CaseText(phantom.Failing, phantom.Cases);
            var note = $"a raw layer states a slot that no stored safe reference or Loan<T> Field keeps{found}; add a Loan Field over {slot.Name}";
            node.Report(
                requirement,
                code,
                evidence: [$"Origin slot {slot.Name} is Phantom{found}"],
                note: note + " (SPEC 5.7, 15.3.5)",
                relatedSpans: [("Origin slot", slot.Origin.Binder!, slot.Span, "Phantom")]);
            return true;
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
        (string Role, Koto In, SourceSpan Span, string? Label)[]? related = symbol.Declaration is DeclarationContainerKoto declaration
            ? [declaration.OriginNames is OriginNameList { HeaderSpan.Length: > 0 } written ? ("header", declaration, written.HeaderSpan, "the Origin header") : ("type", declaration, TypeNameSpan(declaration), "the Type, which writes no Origin header")]
            : null;
        node.Report(requirement, code, note: note, at: slot, evidence: [$"{symbol.Name} has no Origin slot {slot.IdentifierName}"], relatedSpans: related);
        return true;
    }

    // The first Borrow in a value whose Origin supplies an atom (LocalBorrow), in source order: an explicit Borrow, or a call's implicit
    // Borrow of an argument (SPEC 15.6.1: `take(local)`), found as that argument. A call's plan is read as this pass recorded it, also at
    // a value its own record failed afterwards (a pending relation's record is at its value, R4-M133), where BoundCall reads none.
    private sealed class LocalBorrowFinder(BoundOrigin supplier) : KotoVisitor
    {
        internal Koto? Found { get; private set; }

        public override void Visit(Koto node)
        {
            if (this.Found is not null)
            {
                return;
            }

            if (node is ConversionKoto { ConversionBinding: ConversionBinding.Borrow, BoundType.Origin: { } borrowed } && Supplies(borrowed, supplier))
            {
                this.Found = node;
                return;
            }

            if (node is InvocationKoto { CallStorage: { } call })
            {
                foreach (var operation in call.ArgumentOperations)
                {
                    if (operation is { Kind: ArgumentOperationKind.Borrow, Source: { } argument, AdaptedType.Origin: { } implicitOrigin } &&
                        Supplies(implicitOrigin, supplier))
                    {
                        this.Found = argument;
                        return;
                    }
                }
            }

            node.VisitChildren(this);
        }
    }
}
