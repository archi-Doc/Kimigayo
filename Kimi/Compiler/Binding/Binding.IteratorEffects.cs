// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private IteratorEffects? iteratorEffects;

    private void ValidateIteratorEffects()
    {
        for (var i = 0; i < this.activeConformancePaths.Count; i++)
        {
            var path = this.activeConformancePaths[i];
            if (!path.IsVerified || !IsRefinement(path.Contract, this.Library.Iterator))
            {
                continue;
            }

            for (var w = 0; w < path.WitnessStorage.Count; w++)
            {
                var witness = path.WitnessStorage[w];
                if (ReferenceEquals(witness.Requirement.Scope.Owner, this.Library.LendingIterator.Declaration) &&
                    witness.Implementation.Declaration is FunctionKoto function &&
                    !(this.iteratorEffects ??= new(this)).Check(function))
                {
                    path.Invalid = true;
                    path.IsVerified = false;
                    path.Identity.Invalid = true;
                    path.Identity.IsVerified = false;
                    Fail(path.Use, BindingFailure.IncompatibleImplementation);
                    break;
                }
            }
        }
    }

    // SPEC 22.1.2.4: a previous item's external Loans remain live throughout the next step.
    // The receiver's own Origin is distinct from the stored source Origins of the published result.
    private sealed class IteratorEffects(Binding binding) : KotoVisitor
    {
        private BoundType result = BoundType.Unit;
        private bool valid;

        public override void Visit(Koto node)
        {
            if (!this.valid || node is FunctionKoto or DeclarationContainerKoto)
            {
                return;
            }

            if (node is BinaryKoto assignment && assignment.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals)
            {
                this.Access(PlaceReference(assignment.Left), LoanRequirement.Uniq, node);
            }
            else if (node is UnaryKoto update && ElementAccess.UpdateOperator(update.Akind) != KotoKind.Invalid)
            {
                this.Access(PlaceReference(update.Operand), LoanRequirement.Uniq, node);
            }
            else if (node is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } borrow)
            {
                this.Access(borrow.BoundType, borrow.BoundType?.Semantics == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref, node);
            }
            else if (node is ConversionKoto { ConversionBinding: ConversionBinding.Follow } follow)
            {
                this.Access(follow.Left.BoundType, LoanRequirement.Ref, node);
            }
            else if (node is MemberAccessKoto or IndexKoto)
            {
                this.Access(PlaceReference(node), LoanRequirement.Ref, node);
            }

            node.VisitChildren(this);
        }

        internal bool Check(FunctionKoto function)
        {
            this.result = function.BoundSymbol!.Type!;
            this.valid = true;
            if (this.result.CarriesOrigin)
            {
                if (function.Body is { } body)
                {
                    this.Visit(body);
                }

                if (function.ExpressionBody is { } expression)
                {
                    this.Visit(expression);
                }
            }

            return this.valid;
        }

        private static BoundType? PlaceReference(Koto node)
        {
            node = KotoHelper.UnwrapParentheses(node);
            return node switch
            {
                ConversionKoto { ConversionBinding: ConversionBinding.Follow } follow => follow.Left.BoundType,
                BinaryKoto part when ElementAccess.IsSyntax(part) => ReferenceTypes.IsBorrow(part.Left.BoundType) || part.Left.BoundType?.Kind == BoundTypeKind.Slice
                    ? part.Left.BoundType : PlaceReference(part.Left),
                _ => null,
            };
        }

        private void Access(BoundType? type, LoanRequirement mode, Koto use)
        {
            if (type?.Origin is { } origin && this.Conflicts(this.result, origin, mode, use))
            {
                this.valid = false;
            }
        }

        private bool Conflicts(BoundType type, BoundOrigin origin, LoanRequirement mode, Koto use)
        {
            if (type.Origin is { } retained && (mode == LoanRequirement.Uniq || type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq) &&
                binding.ProvesOriginOutlives(origin, retained, use) && binding.ProvesOriginOutlives(retained, origin, use))
            {
                return true;
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                if ((mode == LoanRequirement.Uniq || type.Symbol?.Schema?.Origins[i].LoanRequirement == LoanRequirement.Uniq) &&
                    binding.ProvesOriginOutlives(origin, type.OriginArguments[i], use) && binding.ProvesOriginOutlives(type.OriginArguments[i], origin, use))
                {
                    return true;
                }
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (this.Conflicts(type.Components[i], origin, mode, use))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
