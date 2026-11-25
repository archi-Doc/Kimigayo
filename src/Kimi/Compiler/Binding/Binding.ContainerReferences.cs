// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly InferredConstructionTargetRows inferredConstructionTargets;

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

    // SPEC 12.5.2: the member is the called function of its invocation, possibly with explicit Type arguments and inside
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

    // A lookup publishes the qualifier it selected. Its Resolved, without a Type, stands only where no binder has decided the node: a
    // qualifier its binder failed, directly or by resting on a part, keeps that outcome (BindType replaces the lookup's Resolved), so
    // its record is reported and a consumer rests on it (SPEC 23.1.2; PLAN G481).
    private static void PublishQualifier(Koto qualifier, BindingSymbol symbol)
    {
        qualifier.BoundSymbol = symbol;
        if (qualifier.BindingState == BindingState.Unvisited)
        {
            qualifier.BindingState = BindingState.Resolved;
        }
    }

    // SPEC 9.7, F.5: the path a container reference is written by: below transparent wrappers, a root name's operand and a Constructed
    // Type's head, the node whose qualifier (a MemberAccess's Left) gives the environment.
    private static Koto ContainerPath(Koto syntax)
    {
        var path = UnwrapTypeSyntax(syntax);
        if (path is SyntaxFormKoto { Akind: KotoKind.RootName, Operands.Length: 1 } root)
        {
            path = UnwrapTypeSyntax(root.Operands[0]);
        }

        return path is GenericsKoto generic ? UnwrapTypeSyntax(generic.Identifier!) : path;
    }

    // SPEC 15.4.4, 9.7.2: an Origin slot that a called member's expression qualifier omits, or names by a binding set that no clause
    // of its declaration relates, is inferred from the call. Binding has no inference variables in bodies yet, so such a call is one
    // Unsupported at the qualifier, never a failed selection or a missing Origin. A written part of the qualifier that failed
    // explains the member instead (SPEC 23.1.2).
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
    private void ReportDuplicateBindingSet(TypeSemanticsKoto annotation, DiagnosticCollection? store, string name, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var earlier = this.EarlierBindingName(annotation, name);
        var note = $"A binding set introduces a new set name and never applies an existing Origin or set (SPEC 15.3.1); {name} is already declared in this scope";
        if (earlier is not { } declaration)
        {
            annotation.ReportIn(store, requirement, code, note: note, evidence: [name]);
        }
        else if (declaration.Span is { } span)
        {
            annotation.ReportIn(store, requirement, code, note: note, evidence: [name], relatedSpans: [("declaration", declaration.At, span, "Origin slot")]);
        }
        else
        {
            annotation.ReportIn(store, requirement, code, note: note, evidence: [name], related: [("declaration", declaration.At, "first declaration")]);
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

        ConsiderSet(this.originDeclarations.GetValueOrDefault(owner)?.Set(name));
        for (var current = this.NameScope(annotation); current is not null; current = current.Parent)
        {
            ConsiderSet(this.originDeclarations.GetValueOrDefault(current.Owner)?.Set(name));
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
            if (!ReferenceEquals(candidate, annotation) && candidate.CodeContext.HasSameSource(annotation.CodeContext) &&
                start < annotation.Span.Start && (earlier is not { } found || start < (found.Span?.Start ?? found.At.Span.Start)))
            {
                earlier = (candidate, span, set, scalar);
            }
        }
    }

    // SPEC 9.7, 9.7.1, 10.8.1: a construction qualifier whose own Type-argument list is omitted: its environment from the path, its own
    // arguments those of `supplied`, else its own parameters, open, and the qualifier an inferred construction target, which the
    // construction completes with its solved Type. With no own parameters the path forms the whole Type, and the qualifier is its
    // formation site (FormedTypeRows, PLAN G1278).
    private BoundType? ConstructionTargetType(Koto syntax, BindingSymbol symbol, BindingScope scope, BoundType? supplied)
    {
        var own = ((DeclarationContainerKoto)symbol.Declaration).GenericParameterNodes.Count;
        var arguments = own == 0 ? default : ((BoundType[])(supplied ?? this.SelfType(symbol)).Components).AsSpan(0, own);
        var type = this.BindContainerReference(syntax, symbol, scope, this.TypeContext(syntax, scope), arguments);
        if (type is not null && own == 0)
        {
            FormedTypeRows.Add(syntax);
        }
        else if (type is not null && supplied is null)
        {
            this.inferredConstructionTargets.Add(syntax);
        }

        return type;
    }

    private BoundType? BindContainerReference(Koto syntax, BindingSymbol symbol, BindingScope scope, TypeBindingContext context, ReadOnlySpan<BoundType> own)
    {
        var declaration = (DeclarationContainerKoto)symbol.Declaration;
        var parent = declaration.Parent as DeclarationContainerKoto;
        var inherited = parent?.BoundSymbol?.Schema;
        if (own.Length != declaration.GenericParameterNodes.Count)
        {
            return this.RecordArity(syntax, symbol, declaration.GenericParameterNodes.Count, own.Length);
        }

        BoundType? environment = this.ImportedEnvironment(syntax);
        if (ReferenceEquals(environment?.Symbol, symbol) && own.IsEmpty)
        {
            return environment;
        }

        if (ContainerPath(syntax) is MemberAccessKoto member)
        {
            var qualifier = this.TypeName(member.Left, scope);
            if (qualifier?.Declaration is DeclarationContainerKoto container && !container.IsRoot)
            {
                environment = this.BindContainerQualifier(member.Left, qualifier, scope, context);
                if (environment is null)
                {
                    return this.CompleteDependent(syntax, member.Left); // SPEC 23.1.2 (rule W): the qualifier's formation record.
                }

                if (IsCore(qualifier) && symbol.Kind == BindingSymbolKind.Type && this.AlsoAssociated(environment, symbol.Name, member, scope))
                {
                    return this.Record(member, BindingFailure.Ambiguous, [], unresolved: true); // SPEC 9.7.3: it resolves both ways (a flag).
                }

                environment = this.QualifierEnvironment(environment, symbol, container, parent, scope, syntax);
            }
            else if (qualifier is null && HirTables.LookupOf(UseNode(member.Left)) == PathEnd.Projection && UseNode(member.Left).BoundType is { Symbol: { } formed } projected)
            {
                // SPEC 9.6, 8.5.2 (PLAN G724 (b)): after a projection the qualifier is its formed normal form (BindAssociatedProjection).
                var selected = ReferenceEquals(formed.Declaration, parent) ? default : this.LookupMember(projected, symbol.Name, scope, LookupRole.Qualifier);
                environment = ReferenceEquals(formed.Declaration, parent) ? projected : ReferenceEquals(selected.Member, symbol) ? selected.DeclaringType : null;
            }
        }

        if (inherited is not { GenericSlots.Count: > 0 } and not { Origins.Count: > 0 })
        {
            return this.FormReference(symbol, own, default, default);
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
            return this.RecordArity(syntax, parent!.BoundSymbol!, inherited.GenericSlots.Count, environment?.Components.Count ?? 0);
        }

        return this.FormReference(symbol, own, (BoundType[])environment.Components, (BoundOrigin[])environment.OriginArguments);
    }

    // SPEC 9.7, 9.7.1: a container reference's Type from its own arguments and its environment's components and Origin arguments, the
    // one formation of it: the own arguments, then the environment's; the declaration's own Origin slots empty, then the environment's
    // Origins. Pure: it interns, and writes no node, record or table. With no environment it is the declaration's Type at `own`.
    private BoundType FormReference(BindingSymbol symbol, ReadOnlySpan<BoundType> own, ReadOnlySpan<BoundType> environment, ReadOnlySpan<BoundOrigin> environmentOrigins)
    {
        if (environment.IsEmpty && environmentOrigins.IsEmpty)
        {
            return this.types.Intern(own.IsEmpty ? BoundTypeKind.Nominal : BoundTypeKind.Constructed, symbol, SemanticsKind.Owner, own);
        }

        var declaration = (DeclarationContainerKoto)symbol.Declaration;
        var slots = declaration.OriginNames.Count + ((DeclarationContainerKoto)declaration.Parent!).BoundSymbol!.Schema!.Origins.Count;
        var count = own.Length + environment.Length;
        var types = this.RentTypes(count);
        var origins = this.originScratch.Rent(slots);
        try
        {
            own.CopyTo(types);
            environment.CopyTo(types.AsSpan(own.Length));
            var originCount = environmentOrigins.IsEmpty ? 0 : slots;
            Array.Clear(origins, 0, originCount);
            environmentOrigins.CopyTo(origins.AsSpan(declaration.OriginNames.Count));
            return this.types.Intern(count == 0 ? BoundTypeKind.Nominal : BoundTypeKind.Constructed, symbol, SemanticsKind.Owner, types.AsSpan(0, count), originArguments: origins.AsSpan(0, originCount));
        }
        finally
        {
            this.typeScratch.Return(types, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    // SPEC 6.1.3, 9.6: a qualified nested Type's environment from its qualifier's Type: the qualifier when it declares the nested Type, else
    // the declaring Type (a base layer's, substituted) of the member the qualifier's lookup selects in the row the use's path selected it in,
    // or none.
    private BoundType? QualifierEnvironment(BoundType qualifier, BindingSymbol symbol, DeclarationContainerKoto container, DeclarationContainerKoto? parent, BindingScope scope, Koto use)
    {
        if (ReferenceEquals(container, parent))
        {
            return qualifier;
        }

        var selected = this.LookupMember(qualifier, symbol.Name, scope, UseRow(UseNode(use)));
        return ReferenceEquals(selected.Member, symbol) ? selected.DeclaringType : null;
    }

    private BoundType? BindContainerQualifier(Koto syntax, BindingSymbol symbol, BindingScope scope, TypeBindingContext context)
    {
        if (syntax.BoundType is { } known)
        {
            return known;
        }

        if (syntax is ParenthesizedTypeKoto grouped)
        {
            return this.PublishType(syntax, this.BindContainerQualifier(grouped.Type, symbol, scope, context));
        }

        if (symbol.Declaration is GroupKoto)
        {
            // SPEC 6.1, 9.7: a group has no own parameters, so written arguments meet the one arity check of written arguments.
            var result = UnwrapTypeSyntax(syntax) is GenericsKoto generic ? this.BindConstructedType(generic, symbol, scope, context) : this.BindContainerReference(syntax, symbol, scope, context, []);

            // The qualifier names its group, as BindType names a struct qualifier, so no consumer takes the group's Type as a value.
            syntax.BoundSymbol = symbol;
            return result is null ? null : this.PublishType(syntax, this.CompleteOrigins(result, syntax as TypeSemanticsKoto, syntax, scope, context with { SuppressOuter = true }));
        }

        if (symbol.Declaration is ContractKoto)
        {
            // SPEC 8.4, 18.2.1: a Contract reference has no Type role either; the bound Contract reference is formed where every other is,
            // and the qualifier completes with it, as a group's does (a Contract without arguments forms no Type there).
            return this.BindContractReference(syntax, symbol, scope) is { } contract ? this.PublishType(syntax, contract.Type) : null;
        }

        return this.BindType(syntax, scope, context with { SuppressOuter = true });
    }

    private BoundType? ImportedEnvironment(Koto syntax)
    {
        for (; ;)
        {
            if (this.importedContainers.TryGetValue(syntax, out var import))
            {
                return import.Environment;
            }

            var next = syntax is GenericsKoto generic ? generic.Identifier! : UnwrapTypeSyntax(syntax);
            if (ReferenceEquals(next, syntax))
            {
                return null;
            }

            syntax = next;
        }
    }

    /// <summary>The `inferredConstructionTargets` family's one accessor (DECISIONS BU-220): a working-state family, the construction
    /// qualifiers that leave their own slots open (ConstructionTargetType), which the construction completes with its solved Type; a
    /// `bool` row in the qualifier's module's NodeColumns at its SyntaxId. A row of an earlier pass reads none, and the library's rows
    /// are released at its freeze (NodeColumns.Freeze). Its members keep the names its readers use.</summary>
    private readonly struct InferredConstructionTargetRows(Binding binding)
    {
        internal void Add(Koto syntax) => HirTables.Of(syntax).Families.InferredConstructionTargets.Ref(syntax.SyntaxId, binding) = true;

        internal bool Contains(Koto syntax) => syntax.SyntaxOwner is { } table && table.Hir.Families.InferredConstructionTargets.Get(syntax.SyntaxId, binding);
    }
}
