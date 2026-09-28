// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private DefaultDeclarationVisitor? defaultDeclarations;
    private OwnershipBody? defaultBody;

    private void CheckDefaultDeclarations(FunctionKoto function)
    {
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (function.Parameters[i].DefaultValue is { } expression)
            {
                (this.defaultDeclarations ??= new(this)).Check(function, i, expression);
                if (ScalarDefaults.Supports(function, i))
                {
                    this.Build(function, i);
                }
            }
        }
    }

    // This checks definite forbidden acquisitions, not a certificate that arbitrary
    // defaults are executable. Unsupported effects and representations retain their guards.
    private sealed class DefaultDeclarationVisitor(OwnershipAnalysis owner) : KotoVisitor
    {
        private FunctionKoto? function;
        private int parameter;
        private PlaceUseKind use;

        public override void Visit(Koto node)
        {
            if (owner.compilation.Binding.TryGetEnumConstruction(node, out var construction))
            {
                foreach (var operation in construction!.PayloadOperations)
                {
                    this.Visit(operation.Source!, operation.Kind == ArgumentOperationKind.Value ? PlaceUseKind.Consume : PlaceUseKind.Read);
                }

                return;
            }

            switch (node)
            {
                case FunctionKoto:
                    return; // Nested bodies have their own declaration/ownership pass.
                case IdentifierNameKoto:
                    this.CheckMove(node);
                    return;
                case BinaryKoto element when ElementAccess.IsSyntax(element):
                    this.CheckMove(element);
                    this.Visit(element.Left, PlaceUseKind.Read);
                    this.Visit(element.Right, PlaceUseKind.Read);
                    return;
                case TupleLiteralKoto tuple:
                    this.VisitAcquired(tuple.Elements);
                    return;
                case ArrayLiteralKoto array when array.BoundType?.Kind == BoundTypeKind.FixedArray:
                    this.VisitAcquired(array.Elements);
                    return;
                case BinaryKoto { Akind: KotoKind.Equals } assignment:
                    this.Visit(assignment.Left, PlaceUseKind.Read);
                    this.Visit(assignment.Right, PlaceUseKind.Consume);
                    return;
                case ParenthesizedKoto parentheses:
                    this.Visit(parentheses.Operand, this.use);
                    return;
                case LabeledKoto labeled:
                    this.Visit(labeled.Target, this.use);
                    return;
                case CodeBlockKoto block:
                    for (var i = 0; i < block.Items.Count; i++)
                    {
                        this.Visit(block.Items[i], PlaceUseKind.Consume);
                    }

                    return;
                case FieldKoto field:
                    if (field.InitializerKoto is { } initializer)
                    {
                        this.Visit(initializer, PlaceUseKind.Consume);
                    }

                    return;
                case JumpKoto jump:
                    if (jump.Expression is { } value)
                    {
                        this.Visit(value, PlaceUseKind.Consume);
                    }

                    return;
                case InvocationKoto { BoundCall: { } plan } call:
                    if (plan.Receiver is { } receiver)
                    {
                        this.Visit(receiver, plan.ReceiverOperation.Kind == ArgumentOperationKind.Value ? PlaceUseKind.Consume : PlaceUseKind.Read);
                    }

                    for (var i = 0; i < call.ArgumentNodes.Count; i++)
                    {
                        this.Visit(call.ArgumentNodes[i], plan.ArgumentOperations[i].Kind == ArgumentOperationKind.Value ? PlaceUseKind.Consume : PlaceUseKind.Read);
                    }

                    return;
                case ConversionKoto conversion:
                    // SPEC 13.5.3: a transfer (x@move) and an Identity acquisition (x@copy, x@i32) consume their operand.
                    this.Visit(conversion.Left, conversion.ConversionBinding is ConversionBinding.Transfer or ConversionBinding.Identity ? PlaceUseKind.Consume : PlaceUseKind.Read);
                    return;
            }

            // Conditions, ordinary operators and reference inspections do not acquire
            // their operands. Blocks and committed calls above introduce acquisition.
            var previous = this.use;
            this.use = PlaceUseKind.Read;
            node.VisitChildren(this);
            this.use = previous;
        }

        internal void Check(FunctionKoto declaration, int index, Koto expression)
        {
            this.function = declaration;
            this.parameter = index;
            try
            {
                this.Visit(expression, PlaceUseKind.Consume);
            }
            finally
            {
                this.function = null;
            }
        }

        private void Visit(Koto node, PlaceUseKind use)
        {
            var previous = this.use;
            this.use = use;
            this.Visit(node);
            this.use = previous;
        }

        private void VisitAcquired(IReadOnlyList<Koto> values)
        {
            for (var i = 0; i < values.Count; i++)
            {
                this.Visit(values[i], PlaceUseKind.Consume);
            }
        }

        private void CheckMove(Koto source)
        {
            if (this.use != PlaceUseKind.Consume || source.BoundType is not { } type)
            {
                return;
            }

            var root = source;
            while (root is BinaryKoto element && ElementAccess.IsSyntax(element))
            {
                // Only actual owned storage paths; never cross a borrowed referent
                // or interpret an arbitrary property getter as an owned field.
                if (!ElementAccess.TryType(element, out _, out _))
                {
                    return;
                }

                root = KotoHelper.UnwrapParentheses(element.Left);
            }

            if (root is IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Parameter } symbol } &&
                ReferenceEquals(symbol.Scope.Owner, this.function) && symbol.Slot < this.parameter &&
                owner.compilation.Binding.ProveCopy(type, source) == ConstraintProof.Refuted)
            {
                owner.issues.Add(new(source, OwnershipFailure.DefaultArgumentMove));
            }
        }
    }
}
