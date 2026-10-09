// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Where a node lies, for the written forms an Advice may offer there (SPEC 15.3.3): a body, whose statements may declare locals
    // (the top-level runtime body and an anonymous function's body included, also inside a Field initializer); a stored Field's
    // initializer, whose declaration takes origin clauses; an expression of a function header, a parameter default or base
    // initializer, which holds no local declaration and whose sets no supported clause relates; or a declaration header's Types.
    private enum AdviceSite : byte
    {
        Body,
        StoredInitializer,
        HeaderExpression,
        Header,
    }

    // A container keeps its own slots first, followed by its parent's effective slots.
    // Slot symbols retain their original binder and ordinal; spelling is not identity.
    internal static int ContainerSlot(Koto binder, BindingSymbol parameter)
    {
        if (ReferenceEquals(parameter.Scope.Owner, binder))
        {
            return parameter.Slot;
        }

        if (binder is DeclarationContainerKoto && binder.BoundSymbol?.Schema is { } schema)
        {
            for (var i = 0; i < schema.GenericSlots.Count; i++)
            {
                if (ReferenceEquals(schema.GenericSlots[i].Symbol, parameter) || ReferenceEquals(schema.GenericSlots[i].Semantics, parameter))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    // SPEC 12.4.2: the member is the called function of its invocation, possibly with explicit Type arguments and inside
    // parentheses, as in (Holder.twice)(n@ref).
    private static bool IsCallee(MemberAccessKoto member)
    {
        Koto callee = member;
        while ((callee.Parent is ParenthesizedKoto grouped && ReferenceEquals(grouped.Operand, callee)) ||
            (callee.Parent is GenericsKoto generic && ReferenceEquals(generic.Identifier, callee)))
        {
            callee = callee.Parent;
        }

        return callee.Parent is InvocationKoto call && ReferenceEquals(call.Method, callee);
    }

    // The written part of an expression qualifier that failed although its Type completed, such as a binding set that reuses a
    // visible name (SPEC 15.3.1).
    private static Koto? FailedQualifierPart(Koto qualifier)
    {
        for (Koto? node = qualifier; node is not null; node = node switch { ParenthesizedTypeKoto grouped => grouped.Type, TypeSemanticsKoto { Type: { } inner } => inner, _ => null })
        {
            if (node.BindingState == BindingState.Invalid)
            {
                return node;
            }
        }

        return null;
    }

    // SPEC 15.3.3: a local declaration with an initializer holds the expression, so origin clauses can be written under it.
    private static bool InLocalInitializer(Koto node)
    {
        for (var current = node; current.Parent is { } parent; current = parent)
        {
            if (parent is VariableKoto variable)
            {
                return variable is not PropertyKoto && variable.InitializerKoto is not null && ReferenceEquals(variable.InitializerKoto, current);
            }

            if (parent is FunctionKoto or PropertyAccessorKoto or DeclarationContainerKoto)
            {
                return false;
            }
        }

        return false;
    }

    private static AdviceSite SiteOf(Koto node)
    {
        for (var current = node; current.Parent is { } parent; current = parent)
        {
            switch (parent)
            {
                case PropertyAccessorKoto accessor:
                    return ReferenceEquals(accessor.Body, current) ? AdviceSite.Body : AdviceSite.Header;
                case FunctionKoto function:
                    if (ReferenceEquals(function.Body, current) || ReferenceEquals(function.ExpressionBody, current))
                    {
                        // An anonymous function written in a parameter default is not supported (Unsupported_Kd).
                        return function.IsAnonymous && SiteOf(function) == AdviceSite.HeaderExpression ? AdviceSite.HeaderExpression : AdviceSite.Body;
                    }

                    if (ReferenceEquals(function.BaseInitializer, current))
                    {
                        return AdviceSite.HeaderExpression;
                    }

                    for (var i = 0; i < function.Parameters.Count; i++)
                    {
                        if (ReferenceEquals(function.Parameters[i].DefaultValue, current))
                        {
                            return AdviceSite.HeaderExpression;
                        }
                    }

                    return AdviceSite.Header;
                case PropertyKoto property:
                    return ReferenceEquals(property.InitializerKoto, current) ? AdviceSite.StoredInitializer : AdviceSite.Header;
                case DeclarationContainerKoto:
                    return AdviceSite.Header;
            }
        }

        return AdviceSite.Header;
    }

    // SPEC 15.3.3, 15.3.4: the declaration whose origin clauses relate a binding set written in its own Type: a local declaration's
    // annotation, a stored Field's Type, a named function's parameter or result Type (a Contract requirement's too) and an enum
    // Case's payload. A set inside a Function Type is quantified per call, and an anonymous function's or an accessor's signature, a
    // computed Property's Type and an expression's Type argument or adaptation hold no clause that relates the set, so none is found
    // there. Whole: the set's Type is the whole Type of its position, so a during written in its place needs no grouping.
    private static bool RelatesOnDeclaration(TypeSemanticsKoto annotation, out bool whole)
    {
        Koto current = annotation;
        for (var parent = annotation.Parent; parent is not null; current = parent, parent = parent.Parent)
        {
            whole = ReferenceEquals(current, annotation);
            switch (parent)
            {
                case FunctionTypeKoto or PropertyAccessorKoto or DeclarationContainerKoto or AliasKoto:
                    return false;
                case FieldKoto local:
                    return ReferenceEquals(local.TypeKoto, current);
                case PropertyKoto property:
                    return property.DeclarationKind is PropertyDeclarationKind.Let or PropertyDeclarationKind.Var && ReferenceEquals(property.TypeKoto, current);
                case FunctionKoto function:
                    if (function.IsAnonymous)
                    {
                        return false;
                    }

                    for (var i = 0; i < function.Parameters.Count; i++)
                    {
                        if (ReferenceEquals(function.Parameters[i].Type, current))
                        {
                            return true;
                        }
                    }

                    return ReferenceEquals(function.ReturnType, current);
            }

            if (parent.Akind == KotoKind.EnumCase)
            {
                return true;
            }
        }

        whole = false;
        return false;
    }

    // A Slice keeps its one slot as the Type's own Origin (Binding.TypeOrigins); every other container keeps its slots as Origin
    // arguments.
    private static BoundOrigin? QualifierSlot(BoundType qualified, int slot)
        => qualified.Kind == BoundTypeKind.Slice ? slot == 0 ? qualified.Origin : null : slot < qualified.OriginArguments.Count ? qualified.OriginArguments[slot] : null;

    private static bool NamesOrigin(BoundType type, BoundOrigin origin)
    {
        if (!type.CarriesOrigin)
        {
            return false;
        }

        if (type.Origin is { } own && ContainsOrigin(own, origin))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (ContainsOrigin(type.OriginArguments[i], origin))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (NamesOrigin(type.Components[i], origin))
            {
                return true;
            }
        }

        return false;

        static bool ContainsOrigin(BoundOrigin expression, BoundOrigin atom)
        {
            if (ReferenceEquals(expression, atom))
            {
                return true;
            }

            for (var i = 0; i < expression.Operands.Count; i++)
            {
                if (ContainsOrigin(expression.Operands[i], atom))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static BoundOrigin? InferenceAtom(BoundOrigin origin)
    {
        if (origin.Kind == OriginKind.Inference && !IsLocalRegion(origin))
        {
            return origin;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (InferenceAtom(origin.Operands[i]) is { } atom)
            {
                return atom;
            }
        }

        return null;
    }

    private static BoundOrigin? InferenceAtom(BoundType type)
    {
        if (!type.CarriesOrigin)
        {
            return null;
        }

        if (type.Origin is { } own && InferenceAtom(own) is { } atom)
        {
            return atom;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (InferenceAtom(type.OriginArguments[i]) is { } argument)
            {
                return argument;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (InferenceAtom(type.Components[i]) is { } component)
            {
                return component;
            }
        }

        return null;
    }

    // SPEC 15.4.4, 9.6.1.1: an Origin slot that a called member's expression qualifier omits, or names by a binding set that no clause
    // of its declaration relates, is inferred from the call. Binding has no inference variables in bodies yet, so such a call is one
    // Unsupported at the qualifier, never a failed selection or a missing Origin. A written part of the qualifier that failed
    // explains the member instead (SPEC 23.3.6.4).
    private bool RejectedOriginQualifier(MemberAccessKoto member, BoundType qualified, BindingSymbol? called)
    {
        if (FailedQualifierPart(member.Left) is { } failed)
        {
            this.Consulted(failed);
            return true;
        }

        if (called is null || qualified.Symbol?.Schema is not { } schema)
        {
            return false;
        }

        for (var i = 0; i < schema.Origins.Count; i++)
        {
            if (QualifierSlot(qualified, i) is { } origin && InferenceAtom(origin) is not null)
            {
                this.Fail(member.Left, BindingFailure.Unsupported);
                return true;
            }
        }

        return false;
    }

    // SPEC 15.3.3: a stored Field's initializer has no value whose Origin is a slot of a call's expression qualifier (no receiver, and
    // static storage is not lent there), so a set related on the Field repairs the call only when no parameter of any candidate of
    // the called member names a slot of that Type. A parameter Type that did not bind names it, conservatively.
    private bool ParametersNameSlots(BindingSymbol? type, string member)
    {
        if (type?.Schema is not { } schema || !this.scopes.TryGetValue(type.Declaration, out var members) || !members.Values.TryGetValue(member, out var called))
        {
            return true;
        }

        for (var candidate = called; candidate is not null; candidate = candidate.Next)
        {
            if (candidate.Declaration is not FunctionKoto function)
            {
                return true;
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (function.Parameters[i].Type.BoundType is not { } parameter)
                {
                    return true;
                }

                for (var s = 0; s < schema.Origins.Count; s++)
                {
                    if (NamesOrigin(parameter, schema.Origins[s].Origin))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // SPEC 15.3.1: a binding set introduces a new set name and never applies an existing Origin or set. The later set is the location
    // and the first declaration of the name is related. The Advice writes a new set related where origin clauses can be written: on
    // the declaration that holds the set (a local, a stored Field, a named function or an enum Case, RelatesOnDeclaration), or, for
    // an expression qualifier elsewhere in a body, on a new local declaration (SPEC 15.3.3, 15.4.4); a parameter default or base
    // initializer gets none (AdviceSite), nor does any other position. A one-slot Type in a Type position may instead apply the
    // existing Origin with during (SPEC 15.3.1).
    private void ReportDuplicateBindingSet(TypeSemanticsKoto annotation, string name, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var earlier = this.EarlierBindingName(annotation, name);
        var type = annotation.Type?.BoundType?.Symbol ?? annotation.BoundType?.Symbol;
        var slots = type?.Schema?.Origins;
        var slot = slots is { Count: > 0 } ? slots[0].Name : null;
        var scope = this.NameScope(annotation);
        var fresh = this.FreshName(name, annotation, scope);
        var typeText = annotation.Type?.ToString() ?? annotation.Identifier;
        var written = $"{typeText}{{{fresh}}}";
        var qualifier = annotation.Parent is ParenthesizedTypeKoto grouped && grouped.Parent is MemberAccessKoto { Right: IdentifierNameKoto called } member && ReferenceEquals(member.Left, grouped)
            ? (Member: called.IdentifierName, Call: member.Parent is InvocationKoto { ArgumentNodes.Count: > 0 } ? "(...)" : "()")
            : default;
        var relatedTo = earlier?.Set is { } first && ReferenceEquals(first.Type?.BoundType?.Symbol ?? first.BoundType?.Symbol, type) ? $"{name}.{slot}"
            : earlier?.Scalar == true ? name : null;
        var relation = slot is not null && relatedTo is not null ? $" followed by origin {fresh}.{slot} == {relatedTo}" : null;
        var site = SiteOf(annotation);
        string? advice = null;
        if (qualifier.Member is not null && site == AdviceSite.Body && !InLocalInitializer(annotation))
        {
            var local = this.FreshName("v", annotation, scope);
            advice = relation is not null
                ? $"Write a new set name in a local declaration and relate it there, as in let {local} = ({written}).{qualifier.Member}{qualifier.Call}{relation}"
                : $"Write a new set name in a local declaration, as in let {local} = ({written}).{qualifier.Member}{qualifier.Call}, and relate each of its slots in an origin clause there";
        }
        else if (qualifier.Member is not null
            ? site == AdviceSite.Body || (site == AdviceSite.StoredInitializer && !this.ParametersNameSlots(type, qualifier.Member))
            : site != AdviceSite.HeaderExpression && RelatesOnDeclaration(annotation, out _))
        {
            // A parameter default or base initializer holds no local declaration, and a function clause over a set written there is
            // not supported; a Field's initializer has no value at a slot that the called member's parameters name; and the other
            // positions hold no clause that relates the set (RelatesOnDeclaration). No written form relates a new set there.
            written = qualifier.Member is null ? written : $"({written}).{qualifier.Member}";
            advice = relation is not null
                ? $"Write a new set name and relate it on the declaration, as in {written}{relation}"
                : $"Write a new set name, as in {written}, and relate each of its slots in an origin clause on the declaration";
            if (qualifier.Member is null && earlier?.Scalar == true && slots is { Count: 1 })
            {
                // Inside another Type, such as ref/Holder{a}, the one-slot form is grouped so that during binds the slot, not the borrow.
                var form = RelatesOnDeclaration(annotation, out var whole) && whole ? $"{typeText} during {name}" : $"({typeText} during {name})";
                advice = $"Apply the existing Origin with the one-slot form {form}, or {char.ToLowerInvariant(advice[0])}{advice[1..]}";
            }
        }

        var note = $"A binding set introduces a new set name and never applies an existing Origin or set (SPEC 15.3.1); {name} is already declared in this scope";
        if (earlier is not { } declaration)
        {
            annotation.Report(requirement, code, note: note, evidence: [name], advice: advice);
        }
        else if (declaration.Span is { } span)
        {
            annotation.Report(requirement, code, note: note, evidence: [name], advice: advice, relatedSpans: [("declaration", declaration.At, span, "Origin slot")]);
        }
        else
        {
            annotation.Report(requirement, code, note: note, evidence: [name], advice: advice, related: [("declaration", declaration.At, "first declaration")]);
        }
    }

    // The scope whose names a binding set's declaration sees: a local's own scope, or the scope of its declaration or, for one that
    // has none, such as an enum Case, of the nearest enclosing declaration.
    private BindingScope? NameScope(Koto node)
    {
        var owner = OriginOwner(node);
        if (owner is VariableKoto { BoundSymbol: { } local })
        {
            return local.Scope;
        }

        for (var current = owner; current is not null; current = current.Parent)
        {
            if (this.scopes.TryGetValue(current, out var scope))
            {
                return scope;
            }
        }

        return null;
    }

    // A name for an Advice example that no visible value, Origin or binding set declares: the stem, then the stem with 2, 3 and so on.
    private string FreshName(string stem, Koto at, BindingScope? scope)
    {
        var owner = OriginOwner(at);
        for (var n = 1; ; n++)
        {
            var name = n == 1 ? stem : stem + n.ToString(CultureInfo.InvariantCulture);
            var taken = owner is not null && this.originDeclarations.GetValueOrDefault(owner)?.Sets.ContainsKey(name) == true;
            for (var current = scope; current is not null && !taken; current = current.Parent)
            {
                taken = current.Values.ContainsKey(name) || current.Origins?.ContainsKey(name) == true || current.OriginSets?.ContainsKey(name) == true ||
                    this.originDeclarations.GetValueOrDefault(current.Owner)?.Sets.ContainsKey(name) == true;
            }

            if (!taken)
            {
                return name;
            }
        }
    }

    // The first declaration, in source order, of a name that a binding set reuses: a set of the same declaration or of an enclosing
    // scope, a visible value, or an Origin a declaration declares, such as a container's slot (Span, within At).
    private (Koto At, SourceSpan? Span, TypeSemanticsKoto? Set, bool Scalar)? EarlierBindingName(TypeSemanticsKoto annotation, string name)
    {
        (Koto At, SourceSpan? Span, TypeSemanticsKoto? Set, bool Scalar)? earlier = null;
        var owner = OriginOwner(annotation);
        if (owner is null)
        {
            return null;
        }

        ConsiderSet(this.originDeclarations.GetValueOrDefault(owner)?.Sets.GetValueOrDefault(name));
        for (var current = this.NameScope(annotation); current is not null; current = current.Parent)
        {
            ConsiderSet(this.originDeclarations.GetValueOrDefault(current.Owner)?.Sets.GetValueOrDefault(name));
            ConsiderSet(current.OriginSets?.GetValueOrDefault(name));
            if (current.Values.TryGetValue(name, out var value) && value.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture)
            {
                Consider(value.Declaration, null, null, false);
            }

            if (current.Origins?.TryGetValue(name, out var origin) == true && current.Owner.BoundSymbol?.Schema is { } schema)
            {
                for (var i = 0; i < schema.Origins.Count; i++)
                {
                    if (ReferenceEquals(schema.Origins[i].Origin, origin))
                    {
                        Consider(current.Owner, schema.Origins[i].Span, null, true);
                        break;
                    }
                }
            }
        }

        return earlier;

        void ConsiderSet(TypeSemanticsKoto? set)
        {
            if (set is not null)
            {
                Consider(set, null, set, false);
            }
        }

        void Consider(Koto candidate, SourceSpan? span, TypeSemanticsKoto? set, bool scalar)
        {
            var start = span?.Start ?? candidate.Span.Start;
            if (!ReferenceEquals(candidate, annotation) && ReferenceEquals(candidate.CodeContext, annotation.CodeContext) &&
                start < annotation.Span.Start && (earlier is not { } found || start < (found.Span?.Start ?? found.At.Span.Start)))
            {
                earlier = (candidate, span, set, scalar);
            }
        }
    }

    private BoundType? BindContainerReference(Koto syntax, BindingSymbol symbol, BindingScope scope, TypeBindingContext context, ReadOnlySpan<BoundType> own)
    {
        var declaration = (DeclarationContainerKoto)symbol.Declaration;
        var parent = declaration.Parent as DeclarationContainerKoto;
        var inherited = parent?.BoundSymbol?.Schema;
        if (own.Length != declaration.GenericParameterNodes.Count)
        {
            return this.FailExplained(ref this.arityFailures, syntax, BindingFailure.InvalidTypeFormation, (symbol, declaration.GenericParameterNodes.Count, own.Length, false));
        }

        BoundType? environment = this.ImportedEnvironment(syntax);
        if (ReferenceEquals(environment?.Symbol, symbol) && own.IsEmpty)
        {
            return environment;
        }

        var path = UnwrapTypeSyntax(syntax);
        if (path is SyntaxFormKoto { Akind: KotoKind.RootName, Operands.Length: 1 } root)
        {
            path = UnwrapTypeSyntax(root.Operands[0]);
        }

        if (path is GenericsKoto generic)
        {
            path = UnwrapTypeSyntax(generic.Identifier!);
        }

        if (path is MemberAccessKoto member)
        {
            var qualifier = this.TypeName(member.Left, scope, false);
            if (qualifier?.Declaration is DeclarationContainerKoto container && !container.IsRoot)
            {
                environment = this.BindContainerQualifier(member.Left, qualifier, scope, context);
                if (environment is null)
                {
                    return null;
                }

                if (!ReferenceEquals(container, parent))
                {
                    var selected = this.LookupTypeMember(environment, symbol.Name, scope, typeRole: true);
                    environment = ReferenceEquals(selected.Member, symbol) ? selected.DeclaringType : null;
                }
            }
        }

        if (inherited is not { GenericSlots.Count: > 0 } and not { Origins.Count: > 0 })
        {
            return this.InternType(own.IsEmpty ? BoundTypeKind.Nominal : BoundTypeKind.Constructed, symbol, SemanticsKind.Owner, own);
        }

        if (environment is null)
        {
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (ReferenceEquals(current.Owner, parent))
                {
                    environment = this.SelfType(parent!.BoundSymbol!);
                    break;
                }
            }
        }

        if (environment is null || environment.Components.Count != inherited.GenericSlots.Count)
        {
            return this.FailExplained(ref this.arityFailures, syntax, BindingFailure.InvalidTypeFormation, (parent!.BoundSymbol!, inherited.GenericSlots.Count, environment?.Components.Count ?? 0, true));
        }

        var count = own.Length + environment.Components.Count;
        var types = this.RentTypes(count);
        var origins = this.originScratch.Rent(declaration.OriginNames.Count + inherited.Origins.Count);
        try
        {
            own.CopyTo(types);
            for (var i = 0; i < environment.Components.Count; i++)
            {
                types[own.Length + i] = environment.Components[i];
            }

            var originCount = environment.OriginArguments.Count == 0 ? 0 : declaration.OriginNames.Count + inherited.Origins.Count;
            Array.Clear(origins, 0, originCount);
            for (var i = 0; i < environment.OriginArguments.Count; i++)
            {
                origins[declaration.OriginNames.Count + i] = environment.OriginArguments[i];
            }

            return this.InternType(count == 0 ? BoundTypeKind.Nominal : BoundTypeKind.Constructed, symbol, SemanticsKind.Owner, types.AsSpan(0, count), originArguments: origins.AsSpan(0, originCount));
        }
        finally
        {
            this.typeScratch.Return(types, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    private BoundType? BindContainerQualifier(Koto syntax, BindingSymbol symbol, BindingScope scope, TypeBindingContext context)
    {
        if (syntax.BoundType is { } known)
        {
            return known;
        }

        if (syntax is ParenthesizedTypeKoto grouped)
        {
            return Complete(syntax, this.BindContainerQualifier(grouped.Type, symbol, scope, context));
        }

        if (symbol.Declaration is GroupKoto)
        {
            var result = this.BindContainerReference(syntax, symbol, scope, context, []);
            return result is null ? null : Complete(syntax, this.CompleteOrigins(result, syntax as TypeSemanticsKoto, syntax, scope, context with { SuppressOuter = true }));
        }

        return this.BindType(syntax, scope, context with { SuppressOuter = true });
    }

    private BoundType? ImportedEnvironment(Koto syntax)
    {
        for (; ;)
        {
            if (this.importedContainerEnvironments.TryGetValue(syntax, out var environment))
            {
                return environment;
            }

            var next = syntax is GenericsKoto generic ? generic.Identifier! : UnwrapTypeSyntax(syntax);
            if (ReferenceEquals(next, syntax))
            {
                return null;
            }

            syntax = next;
        }
    }
}
