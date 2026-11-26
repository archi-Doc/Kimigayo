// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns a bound Type syntax node's Type as written (AsWritten; PLAN G1001; SPEC 8.5.5, 8.6.3): the Type the binder formed for the node
// (BoundType) with each associated projection written in it kept as the projection BindAssociatedProjection formed (the node's `Right`, or
// an Origin application's target), its receiver and its Contract reference as written, as an associated specification's candidate keeps
// them while Constraint Types are bound, and an associated Type named bare as its projection identity. Inputs: a bound Type syntax
// node and its scope. Output: a Type, stored only for a signature's part, a requirement's or an implementation's (writtenParts and
// programParts, WrittenSignaturePart: per signature and position, filled on its first read, kept across unchanged passes and revoked by every edit,
// Binding.Invalidate), so a warm read walks and allocates nothing. Invariants: a node with no projection written below it is its
// BoundType; a composed Type keeps its BoundType's kind, symbol and Origins and takes the
// written parts in the binder's component order for the form: the Type list a form interns, a container reference's own arguments and
// then its environment (ContainerPath and QualifierEnvironment, the binder's members: the lookup at the qualifier's declared Self, the
// written qualifier's arguments substituted, StoredType), a Wrapping over a written projection the Constructed Wrapping TyCtxt interns.
// Prohibitions: no component position counted from syntax; no lookup but QualifierEnvironment's; no proof, record or obligation.
public sealed partial class Binding
{
    // WrittenSignaturePart's walks, per signature and position. Each key names one syntax node and one scope, so an entry's value is a
    // function of its key: a function's symbol, its parameter's or return Type node in the function's scope; an accessor's own
    // (SignatureSymbol), a Type node its declaration writes, in the accessor's scope; else its Property's symbol, the Property's Type
    // node in the Property's declaration scope, one part, so a getter and a setter that both read it (the Field) share its entry. The
    // frozen layer's rule (DECISIONS BU-220, OriginAtomRows' form; CC-60): writtenParts holds the rows a module's own bind forms, the
    // library's included; from the freeze (PLAN G949 S1-1) a row a later step forms for a signature of the frozen library table (its
    // written node's SyntaxOwner) is a program fact, a row of programParts, found after writtenParts, so the frozen layer holds no row a
    // later step made. Owner Binding.AsWritten; both emptied by Binding.Invalidate with the other facts kept across unchanged passes.
    private readonly Dictionary<(BindingSymbol Signature, int Position), BoundType> writtenParts = [];
    private readonly Dictionary<(BindingSymbol Signature, int Position), BoundType> programParts = [];

    // `syntax`'s Type as written, `bound` where it binds no Type.
    private BoundType AsWritten(Koto? syntax, BindingScope scope, BoundType bound)
        => syntax is { BoundType: not null } ? this.Written(syntax, scope) : bound;

    // SPEC 8.6.3, 8.5.5 (R7-C2e2 e2-5b, design R3.2, R3.3.6): a requirement's or an implementation's signature part as written, so a
    // comparison reads both sides in one image: a function's parameter at `position` or its result (position InputCount); an accessor's
    // value input (1) or result (InputCount) as its declaration writes it, else as its Property's Type writes it where the part is that
    // Type (a standard accessor's, a getter's unwritten result), and its stored Field at InputCount + 1; an accessor's receiver is formed;
    // the formed Type where nothing is written, none where the header has none. Its readers form it in the comparison's scope
    // (ContractType), as the header's formation forms it in the declaration's (N3).
    private BoundType? WrittenSignaturePart(FnSig signature, int position)
    {
        var accessor = signature.Accessor;
        if ((accessor is not null && position > signature.InputCount ? accessor.Property.Type : position == signature.InputCount ? signature.Result : signature.Input(position)) is not { } formed)
        {
            return null;
        }

        Koto? own;
        Koto? syntax;
        if (signature.Function is { } function)
        {
            syntax = own = position == signature.InputCount ? function.ReturnType : function.Parameters[position].Type;
        }
        else
        {
            // N1: the part its declaration writes; else the Property's Type where the part is it (Properties.cs:206-207 and :230: a
            // standard accessor's value, a getter's unwritten result, the Field); the receiver and an implicit setter input are formed.
            var result = position == signature.InputCount;
            own = position == 0 || position > signature.InputCount || accessor!.IsStandard ? null : result ? accessor.Declaration?.ReturnType : accessor.Declaration?.ValueType;
            syntax = own ?? (position != 0 && (position > signature.InputCount || (result ? accessor!.Kind == PropertyAccessorKind.Get : accessor!.IsStandard)) ? accessor!.Property.Declaration.TypeKoto : null);
        }

        if (syntax is not { BoundType: not null })
        {
            return formed;
        }

        var key = (signature.Function is { } owner ? owner.BoundSymbol! : own is null ? accessor!.Property.Symbol : accessor!.SignatureSymbol!, position);
        if (!this.writtenParts.TryGetValue(key, out var written) && !this.programParts.TryGetValue(key, out written))
        {
            var scope = signature.Function is { } scoped ? this.scopes[scoped] : own is null ? this.DeclarationScope(accessor!.Property.Symbol) : this.scopes[accessor!.Declaration!];
            (syntax.SyntaxOwner is { Frozen: true } ? this.programParts : this.writtenParts)[key] = written = this.Written(syntax, scope);
        }

        return written;
    }

    private BoundType Written(Koto syntax, BindingScope scope)
    {
        var bound = syntax.BoundType!;
        switch (syntax)
        {
            case MemberAccessKoto { Right.BoundType: { Kind: BoundTypeKind.AssociatedProjection } projection } member:
                return this.WrittenProjection(member, projection, scope);
            case OriginApplicationKoto application when UnwrapTransparentType(application.Type) is { BoundType: { Kind: BoundTypeKind.AssociatedProjection } applied } target:
                return target is MemberAccessKoto qualified ? this.WrittenProjection(qualified, applied, scope) : applied; // a bare target is bound as its identity
            case IdentifierNameKoto or TypeSemanticsKoto { Type: null } when syntax.BoundSymbol is { Kind: BindingSymbolKind.AssociatedType } associated &&
                this.EnclosingContractSelf(scope) is { Symbol.SelfOf: { } contract } self:
                return this.ProjectionIdentity(self, associated, this.DeclaringReference(associated, this.SelfType(contract)), null); // SPEC 8.6.1 (B3)
            case SyntaxFormKoto { Akind: KotoKind.RootName, Operands: [{ BoundType: not null } operand] }:
                return this.Through(bound, operand, scope);
            case FunctionTypeKoto function when bound is { Kind: BoundTypeKind.Function, Components: [var inputs, _] }:
                var input = function.Parameters is TupleTypeKoto list ? this.Composed(inputs, list.ElementNodes, scope, null)
                    : inputs.Components.Count == 1 ? this.Composed(inputs, [function.Parameters], scope, null) : inputs;
                return this.Composed(bound, [function.ReturnType], scope, null, input);
            case TupleTypeKoto tuple:
                return this.Composed(bound, tuple.ElementNodes, scope, null);
            case OptionalTypeKoto optional:
                return this.Composed(bound, [optional.Type], scope, null);
            case FixedArrayTypeKoto array:
                return this.Composed(bound, [array.ElementType], scope, null);
            case GenericsKoto generic when bound.IsWrappingInteger:
                var argument = this.Written(generic.TypeArguments[0], scope);
                return ReferenceEquals(argument, generic.TypeArguments[0].BoundType) ? bound : this.types.Intern(BoundTypeKind.Constructed, generic.BoundSymbol, SemanticsKind.Owner, [argument]);
            case GenericsKoto generic:
                return this.Composed(bound, generic.TypeArguments, scope, this.WrittenEnvironment(generic, bound, scope));
            case ParenthesizedTypeKoto or TypeSemanticsKoto when TransparentInner(syntax) is { } inner:
                return this.Through(bound, inner, scope);
            case TypeSemanticsKoto { Type: { } layer } when bound.Components.Count == 1:
                return this.Composed(bound, [layer], scope, null);
            case MemberAccessKoto or SyntaxFormKoto { Akind: KotoKind.RootName } when bound.Symbol?.Declaration is DeclarationContainerKoto:
                return this.Composed(bound, [], scope, this.WrittenEnvironment(syntax, bound, scope));
        }

        return bound;
    }

    // A projection as written (BindAssociatedProjection's composition `[receiver, declaring Contract reference]`): its receiver's Type as
    // written and, where a qualifier `.(C<..>)` writes a Contract reference, the associated Type's declaring reference as that written
    // reference sees it (DeclaringReference: SPEC 8.3.3, a projection's Contract arguments are parts of the Type; 8.5.3, a refining
    // selector's declaring reference). A qualifier naming a Contract without arguments (CompleteQualifier publishes Unit for it) and an
    // unqualified projection write no Contract argument: their declaring reference is the bound one.
    private BoundType WrittenProjection(MemberAccessKoto member, BoundType projection, BindingScope scope)
    {
        // A receiver written as a name (TypeSpelling) that its lookup selected may carry no Type of its own: a name writes no projection,
        // so its Type is the projection's own receiver, the binder's. Any other receiver node is bound with the projection (BindType).
        var written = member.Left is MemberAccessKoto { Right: ParenthesizedTypeKoto } qualified ? qualified.Left : member.Left;
        var receiver = written.BoundType is not null ? this.Written(written, scope)
            : TypeSpelling(written) is not null ? projection.Components[0]
            : throw new InvalidOperationException($"The receiver of `{projection}` has no Type (InternalInvariant; SPEC 8.5.5).");
        var contract = member.Left is MemberAccessKoto { Right: ParenthesizedTypeKoto selector } && WrittenReference(selector) is { BoundType: { } type } reference &&
            !ReferenceEquals(type, BoundType.Unit) ? this.ProjectionContract(projection.Symbol!, this.DeclaringReference(projection.Symbol!, this.Written(reference, scope))) : projection.Components[1];
        return ReferenceEquals(receiver, projection.Components[0]) && ReferenceEquals(contract, projection.Components[1]) ? projection
            : this.types.Rebuild(projection, [receiver, contract], projection.Origin, (BoundOrigin[])projection.OriginArguments);
    }

    // SPEC 8.4.3, 8.5.3, 8.6.1: the declaring Contract reference of `associated` as the Contract reference `reference` sees it: the
    // identity's declaring reference in the bound reference's associated identities (BoundContractReference, their one former: the
    // reference itself where its own declaration declares `associated`, else the declared identity's reference with the arguments
    // substituted), as SearchAssociated reads it; the projection's Contract component is ProjectionContract's of it, as
    // BindAssociatedProjection makes it.
    private BindingSymbol DeclaringReference(BindingSymbol associated, BoundType reference)
    {
        var identities = this.BoundContractReference(reference).Contract!.AssociatedTypes;
        for (var i = 0; i < identities.Count; i++)
        {
            if (ReferenceEquals(identities[i].Symbol, associated))
            {
                return identities[i].Contract;
            }
        }

        throw new InvalidOperationException($"`{reference}` declares no `{associated.Name}` (InternalInvariant; SPEC 8.5.3).");
    }

    // A wrapper's Type as written: its inner node's, with the wrapper's Origins.
    private BoundType Through(BoundType bound, Koto inner, BindingScope scope)
    {
        var written = this.Written(inner, scope);
        return ReferenceEquals(written, inner.BoundType) ? bound
            : written.Kind == bound.Kind && written.Components.Count == bound.Components.Count ? this.types.Rebuild(written, (BoundType[])written.Components, bound.Origin, (BoundOrigin[])bound.OriginArguments) : written;
    }

    // `bound` with its first components the written Types of `parts` (after `first`, a Function Type's inputs, when given) and the rest
    // the written environment's components when one is given; itself where nothing written differs.
    private BoundType Composed(BoundType bound, IReadOnlyList<Koto> parts, BindingScope scope, BoundType? environment, BoundType? first = null)
    {
        var count = bound.Components.Count;
        var offset = first is null ? 0 : 1;
        var components = this.RentTypes(count);
        try
        {
            var changed = first is not null && !ReferenceEquals(first, bound.Components[0]);
            if (first is not null)
            {
                components[0] = first;
            }

            for (var i = 0; i < parts.Count; i++)
            {
                components[offset + i] = this.Written(parts[i], scope);
                changed |= !ReferenceEquals(components[offset + i], bound.Components[offset + i]);
            }

            for (var i = offset + parts.Count; i < count; i++)
            {
                components[i] = environment is null ? bound.Components[i] : environment.Components[i - offset - parts.Count];
                changed |= !ReferenceEquals(components[i], bound.Components[i]);
            }

            return changed ? this.types.Rebuild(bound, components.AsSpan(0, count), bound.Origin, (BoundOrigin[])bound.OriginArguments) : bound;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // A container reference's environment as written (ContainerPath, BindContainerReference's path): from a qualifier whose Type as written
    // differs, the lookup at the qualifier's declared Self (QualifierEnvironment) with the written qualifier's arguments substituted
    // (StoredType), the lookup's own substitution normalizing them; none (the bound environment stands) for an environment from the
    // enclosing declaration or an import, which write no projection.
    private BoundType? WrittenEnvironment(Koto syntax, BoundType bound, BindingScope scope)
    {
        if (ContainerPath(syntax) is not MemberAccessKoto member || member.Left.BoundType is not { } qualifierType ||
            this.Written(member.Left, scope) is not { } qualifier || ReferenceEquals(qualifier, qualifierType) ||
            bound.Symbol!.Declaration is not DeclarationContainerKoto declaration ||
            (member.Left.BoundSymbol ?? qualifierType.Symbol)?.Declaration is not DeclarationContainerKoto container)
        {
            return null;
        }

        var parent = declaration.Parent as DeclarationContainerKoto;
        return ReferenceEquals(container, parent) ? qualifier
            : this.QualifierEnvironment(this.SelfType(container.BoundSymbol!), bound.Symbol, container, parent, scope, syntax) is { } declared
                ? this.StoredType(declared, qualifier) : throw new InvalidOperationException($"`{qualifier}` selects no `{bound.Symbol.Name}` (InternalInvariant; SPEC 9.6).");
    }
}
