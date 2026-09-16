// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private LocalLoopProof? localLoopProof;

    // Loop-local scalar lifetimes cannot change any enclosing ownership fact.
    // Calls, owned values, outer writes and cleanup effects need a different proof.
    private sealed class LocalLoopProof(OwnershipAnalysis owner) : KotoVisitor
    {
        private Koto? root;
        private bool supported;

        public override void Visit(Koto node)
        {
            if (!this.supported)
            {
                return;
            }

            if (node.AttributeChain is not null)
            {
                this.supported = false;
                return;
            }

            switch (node)
            {
                case FieldKoto field:
                    this.supported = ScalarDefaults.SupportsValue(field.BoundType);
                    if (field.InitializerKoto is { } initializer)
                    {
                        this.Visit(initializer);
                    }

                    return;
                case IdentifierNameKoto or NumberLiteralKoto or BoolLiteralKoto or CharLiteralKoto:
                    this.supported = ScalarDefaults.SupportsValue(node.BoundType);
                    return;
                case UnitLiteralKoto or TupleLiteralKoto { Elements.Count: 0 } or TupleTypeKoto { ElementNodes.Count: 0 }:
                    return;
                case CodeBlockKoto block:
                    this.VisitMany(block.Items);
                    return;
                case ParenthesizedKoto parentheses:
                    this.Visit(parentheses.Operand);
                    return;
                case LabeledKoto labeled:
                    this.Visit(labeled.Target);
                    return;
                case DoKoto scoped:
                    this.Visit(scoped.Body);
                    return;
                case LoopKoto loop:
                    this.Visit(loop.Body);
                    return;
                case WhileKoto loop:
                    this.Visit(loop.Condition);
                    this.Visit(loop.Body);
                    return;
                case IfKoto conditional:
                    for (var i = 0; i < conditional.Branches.Count; i++)
                    {
                        var branch = conditional.Branches[i];
                        this.Visit(branch.Condition);
                        this.Visit(branch.Body);
                    }

                    if (conditional.ElseBody is { } otherwise)
                    {
                        this.Visit(otherwise);
                    }

                    return;
                case JumpKoto jump when jump is ExitKoto or ContinueKoto or YieldKoto:
                    this.supported = this.IsInside(owner.flow!.Targets.GetValueOrDefault(jump));
                    if (jump.Expression is { } value)
                    {
                        this.Visit(value);
                    }

                    return;
                case ConversionKoto conversion when conversion.ConversionBinding is ConversionBinding.Identity or ConversionBinding.Literal or
                    ConversionBinding.Integer or ConversionBinding.Floating or ConversionBinding.Numeric:
                    this.supported = ScalarDefaults.SupportsValue(conversion.BoundType);
                    this.Visit(conversion.Left);
                    return;
                case UnaryKoto unary when unary.Akind is KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement:
                    this.supported = this.IsLocalWrite(unary.Operand);
                    return;
                case UnaryKoto unary when unary.Akind is KotoKind.PrefixPlus or KotoKind.PrefixMinus or KotoKind.Not:
                    this.Visit(unary.Operand);
                    return;
                case BinaryKoto binary when binary.Akind == KotoKind.Equals || ElementAccess.UpdateOperator(binary.Akind) != KotoKind.Invalid:
                    this.supported = this.IsLocalWrite(binary.Left);
                    this.Visit(binary.Right);
                    return;
                case BinaryKoto binary when binary.Akind is KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash or KotoKind.Percent or
                    KotoKind.Ampersand or KotoKind.Bar or KotoKind.Caret or KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan or
                    KotoKind.EqualsEquals or KotoKind.ExclamationEquals or KotoKind.LessThan or KotoKind.LessThanEquals or
                    KotoKind.GreaterThan or KotoKind.GreaterThanEquals or KotoKind.And or KotoKind.Or:
                    this.Visit(binary.Left);
                    this.Visit(binary.Right);
                    return;
                default:
                    this.supported = false;
                    return;
            }
        }

        internal bool Check(LoopKoto loop)
            => this.Check(loop, loop.Body);

        internal bool Check(Koto loop, CodeBlockKoto body)
        {
            this.root = loop;
            this.supported = true;
            this.Visit(body);
            this.root = null;
            return this.supported;
        }

        private bool IsLocalWrite(Koto node)
            => KotoHelper.UnwrapParentheses(node) is IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local, Declaration: FieldKoto field } } &&
                ScalarDefaults.SupportsValue(field.BoundType) && this.IsInside(field);

        private bool IsInside(Koto? node)
        {
            for (var current = node; current is not null; current = current.Parent)
            {
                if (ReferenceEquals(current, this.root))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
