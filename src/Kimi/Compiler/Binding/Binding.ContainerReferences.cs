// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
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

    // SPEC 15.3.1: a binding set introduces a new set name and never applies an existing Origin or set. The later set is the location
    // and the first declaration of the name is related.
    private void ReportDuplicateBindingSet(TypeSemanticsKoto annotation, string name, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var earlier = this.EarlierBindingName(annotation, name);
        var note = $"A binding set introduces a new set name and never applies an existing Origin or set (SPEC 15.3.1); {name} is already declared in this scope";
        if (earlier is not { } declaration)
        {
            annotation.Report(requirement, code, note: note, evidence: [name]);
        }
        else if (declaration.Span is { } span)
        {
            annotation.Report(requirement, code, note: note, evidence: [name], relatedSpans: [("declaration", declaration.At, span, "Origin slot")]);
        }
        else
        {
            annotation.Report(requirement, code, note: note, evidence: [name], related: [("declaration", declaration.At, "first declaration")]);
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
