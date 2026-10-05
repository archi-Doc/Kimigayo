// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private readonly Dictionary<Koto, bool> definiteDefaults = new(ReferenceEqualityComparer.Instance);
    private DefaultDeclarationVisitor? defaultDeclarations;
    private DefaultDeclarationVisitor? definiteDefaultProbe;
    private OwnershipBody? defaultBody;

    // SPEC 7.2.3: whether a default definitely moves a preceding prepared argument. That declaration error is the default's
    // problem, so an unsupported execution of the same default adds no record.
    private bool DefiniteDefaultMove(FunctionKoto function, int index, Koto expression)
    {
        if (!this.definiteDefaults.TryGetValue(expression, out var definite))
        {
            definite = (this.definiteDefaultProbe ??= new(this, probe: true)).Probe(function, index, expression);
            this.definiteDefaults[expression] = definite;
        }

        return definite;
    }

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
    private sealed class DefaultDeclarationVisitor(OwnershipAnalysis owner, bool probe = false) : KotoVisitor
    {
        private FunctionKoto? function;
        private int parameter;
        private PlaceUseKind use;
        private bool transfer;
        private bool found;

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
                case FunctionKoto closure:
                    // Nested bodies have their own declaration/ownership pass; a capture entry that moves a preceding prepared
                    // argument is this default's own acquisition (SPEC 7.2.3, 7.6.2).
                    this.CheckCaptures(closure);
                    return;
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
                    // SPEC 13.5.3: a transfer (x@move) and an Identity acquisition (x@copy, x@i32) consume their operand; a transfer
                    // moves even a Copy Place, so it is a Move of that Place whatever its Type.
                    var transferred = conversion.ConversionBinding == ConversionBinding.Transfer;
                    var outer = this.transfer;
                    this.transfer = transferred && KotoHelper.UnwrapParentheses(conversion.Left) is IdentifierNameKoto or BinaryKoto or MemberAccessKoto;
                    this.Visit(conversion.Left, transferred || conversion.ConversionBinding == ConversionBinding.Identity ? PlaceUseKind.Consume : PlaceUseKind.Read);
                    this.transfer = outer;
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
                this.transfer = false;
            }
        }

        internal bool Probe(FunctionKoto declaration, int index, Koto expression)
        {
            this.found = false;
            this.Check(declaration, index, expression);
            return this.found;
        }

        private void Report(OwnershipIssue issue)
        {
            if (probe)
            {
                this.found = true;
            }
            else
            {
                owner.issues.Add(issue);
            }
        }

        private void CheckCaptures(FunctionKoto closure)
        {
            if (closure.ClosureStorage is not { } plan || closure.Captures is not { } entries)
            {
                return;
            }

            for (var i = 0; i < plan.Storage.Count; i++)
            {
                var capture = plan.Storage[i];
                if (capture.Environment.CaptureAcquisition == CaptureAcquisition.Move && this.PrecedingParameter(capture.Source))
                {
                    for (var entry = 0; entry < entries.Length; entry++)
                    {
                        if (entries[entry].Name == capture.Source.Name)
                        {
                            this.Report(new(closure, OwnershipFailure.DefaultArgumentMove, Capture: entry));
                            break;
                        }
                    }
                }
            }
        }

        private bool PrecedingParameter(BindingSymbol symbol)
            => symbol.Kind == BindingSymbolKind.Parameter && ReferenceEquals(symbol.Scope.Owner, this.function) && symbol.Slot < this.parameter;

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

            if (root is IdentifierNameKoto { BoundSymbol: { } symbol } && this.PrecedingParameter(symbol) &&
                (this.transfer || owner.compilation.Binding.ProveCopy(type, source) == ConstraintProof.Refuted))
            {
                this.Report(new(source, OwnershipFailure.DefaultArgumentMove));
            }
        }
    }
}
