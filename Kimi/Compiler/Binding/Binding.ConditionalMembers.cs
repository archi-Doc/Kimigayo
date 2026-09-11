// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static bool TryConditionalBlock(SyntaxFormKoto syntax, out CodeBlockKoto block)
    {
        block = (syntax.Operands.Length == 3 ? syntax.Operands[2] as CodeBlockKoto : null)!;
        return block is not null;
    }

    private BindingScope DeclarationScope(BindingSymbol symbol)
        => symbol.ConditionalDeclaration is { } syntax ? this.scopes[syntax] : symbol.Scope;

    private BoundType? MemberType(BoundType type, BoundType? declaringType)
        => declaringType is null ? type : this.StoredType(type, declaringType);

    private BoundType? CallDeclaringType(Koto callee, BindingSymbol member)
        => callee is MemberAccessKoto access && this.memberSelections.TryGetValue(access, out var selection) ? selection.DeclaringType
            : member.Scope.Owner is StructKoto or EnumKoto ? this.SelfType(member.Scope.Owner.BoundSymbol!) : null;

    private ConstraintProof ProveMemberConditions(BindingSymbol member, BoundType? declaringType, BindingScope scope)
    {
        if (member.ConditionalDeclaration is not { } declaration)
        {
            return ConstraintProof.Proven;
        }

        if (declaration.BindingState == BindingState.Invalid || member.Declaration.BindingState == BindingState.Invalid || this.scopes[declaration].Constraints?.Invalid == true)
        {
            return ConstraintProof.Error;
        }

        declaringType ??= this.SelfType(member.Scope.Owner.BoundSymbol!);
        return this.ProveConditionalPremises((SyntaxFormKoto)declaration.Operands[1], member.Scope.Owner, declaringType, scope);
    }

    private void ValidateConditionalBlock(SyntaxFormKoto syntax, DeclarationContainerKoto owner)
    {
        if (!TryConditionalBlock(syntax, out var block))
        {
            return;
        }

        var valid = true;
        for (var i = 0; i < block.Items.Count; i++)
        {
            var member = block.Items[i];
            var allowed = member switch
            {
                FunctionKoto f => !f.IsConstructor && !f.IsDestructor && !f.IsSpecialization,
                PropertyKoto p => owner is StructKoto && p.DeclarationKind == PropertyDeclarationKind.Computed,
                IsKoto { IsAssociatedConstraint: true } => true,
                _ => false,
            };
            if (!allowed)
            {
                Fail(member, BindingFailure.InvalidConstraint);
                valid = false;
            }
        }

        if (!valid)
        {
            Fail(syntax, BindingFailure.InvalidConstraint);
            (this.scopes[syntax].Constraints ??= new()).Invalid = true;
        }
    }

    private void BindConditionalAssociatedSpecifications()
    {
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, Parent: DeclarationContainerKoto owner } syntax || !TryConditionalBlock(syntax, out var block))
            {
                continue;
            }

            var scope = this.scopes[syntax];
            if (syntax.Operands[0] is IsKoto { BoundConstraint.Contract: { } contract } target && this.conformancePaths.TryGetValue((owner.BoundSymbol!, contract, target, contract), out var path))
            {
                scope.ConformancePath = path;
            }

            for (var i = 0; i < block.Items.Count; i++)
            {
                if (block.Items[i] is IsKoto { IsAssociatedConstraint: true } clause)
                {
                    this.BindAssociatedSpecification(clause, scope, owner.BoundSymbol!);
                    if (scope.ConformancePath is not { } declaringPath || !declaringPath.RootContract.Contract!.AssociatedStorage.Contains(clause.BoundSymbol!))
                    {
                        Fail(clause, BindingFailure.InvalidAssociatedType);
                    }
                }
            }
        }
    }
}
