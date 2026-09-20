// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Retained closure conversion and capture vocabulary.

public readonly record struct BoundCapture(BindingSymbol Source, BindingSymbol Environment);

/// <summary>A retained capture environment, signature and minimum call receiver.</summary>
public sealed class BoundClosure
{
    public IReadOnlyList<BoundCapture> Captures => this.Storage;

    public BoundType Signature { get; internal set; } = null!;

    public BoundType? EnvironmentType { get; internal set; }

    public SemanticsKind Receiver { get; internal set; } = SemanticsKind.Ref;

    internal List<BoundCapture> Storage { get; } = new();

    internal List<BindingSymbol> SymbolPool { get; } = new();
}

public sealed partial class Binding
{
    private bool ClosureSignatureFits(FunctionKoto function, BoundType expected)
    {
        if (expected.Kind != BoundTypeKind.Function || expected.CarriesOrigin)
        {
            return false;
        }

        var inputs = expected.Components[0];
        var count = ReferenceEquals(inputs, BoundType.Unit) ? 0 : inputs.Components.Count;
        if (function.Parameters.Count != count)
        {
            return false;
        }

        var scope = this.scopes[function];
        for (var i = 0; i < count; i++)
        {
            var type = this.BindType(function.Parameters[i].Type, scope);
            if (type is null || !ReferenceEquals(type, inputs.Components[i]))
            {
                return false;
            }
        }

        return function.ReturnType is null || ReferenceEquals(this.BindType(function.ReturnType, scope), expected.Components[1]);
    }

    private BoundType? BindClosure(FunctionKoto function, BindingScope scope, BoundType? expected)
    {
        if (expected is null)
        {
            return this.BindConcreteClosure(function, scope);
        }

        if (!this.ClosureSignatureFits(function, expected))
        {
            return Fail(function, expected is null ? BindingFailure.Unsupported : BindingFailure.TypeMismatch);
        }

        var plan = function.ClosureStorage ??= new();
        plan.Storage.Clear();
        plan.EnvironmentType = null;
        plan.Receiver = SemanticsKind.Ref;
        plan.Signature = expected;
        var symbol = this.symbols[function];
        symbol.Type = expected.Components[1];
        symbol.HeaderBound = true;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            this.symbols[function.Parameters[i]].Type = expected.Components[0].Components[i];
        }

        if (function.Captures is { } captures)
        {
            for (var i = 0; i < captures.Length; i++)
            {
                var capture = captures[i];
                if (capture.IsMutable || capture.Operation is not null)
                {
                    return Fail(function, BindingFailure.Unsupported);
                }

                if (scope.Values.ContainsKey(capture.Name))
                {
                    return Fail(function, BindingFailure.Duplicate);
                }

                var source = this.Lookup(capture.Name, scope.Parent!, function, false);
                if (source?.Type is { } captureType && !(ScalarTypes.Supports(captureType) || ReferenceEquals(captureType, BoundType.Unit)))
                {
                    return Fail(function, BindingFailure.Unsupported);
                }

                if (source is null || this.Capture(function, source, scope) is null)
                {
                    return Fail(function, BindingFailure.Capture);
                }
            }
        }

        if (function.Body is { } block)
        {
            this.BindNode(block, scope);
        }
        else if (function.ExpressionBody is { } expression)
        {
            this.RequireType(expression, scope, symbol.Type);
        }

        return Complete(function, expected);
    }

    private BindingSymbol? Capture(FunctionKoto function, BindingSymbol source, BindingScope scope)
    {
        if (source.Type is null && source.Declaration is VariableKoto variable)
        {
            this.BindVariable(variable, source.Scope);
        }

        // Concrete environments preserve complete captured Types and dependencies.
        // Common-function erasure retains its independent Owned requirement.
        if (source.Kind is not (BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture) ||
            source.Type is not { } type || !(ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.Unit) ||
                (function.ClosureStorage?.EnvironmentType is not null && (ReferenceEquals(type, BoundType.String) || type.Kind == BoundTypeKind.Closure ||
                    ReferenceTypes.IsStorage(type) || ObjectTypes.IsOwner(type)))))
        {
            return null;
        }

        var plan = function.ClosureStorage!;
        for (var i = 0; i < plan.Storage.Count; i++)
        {
            if (ReferenceEquals(plan.Storage[i].Source, source))
            {
                return plan.Storage[i].Environment;
            }
        }

        var index = plan.Storage.Count;
        if (index == plan.SymbolPool.Count)
        {
            plan.SymbolPool.Add(new(source.Name, BindingSymbolKind.Capture, function, scope));
        }

        var environment = plan.SymbolPool[index];
        if (environment.Name != source.Name)
        {
            environment = new(source.Name, BindingSymbolKind.Capture, function, scope);
            plan.SymbolPool[index] = environment;
        }

        environment.Type = type;
        environment.Scope = scope;
        environment.Slot = index;
        environment.MutableCapture = false;
        plan.Storage.Add(new(source, environment));
        scope.Values[source.Name] = environment;
        return environment;
    }

    private BoundType? BindConcreteClosure(FunctionKoto function, BindingScope scope)
    {
        var plan = function.ClosureStorage ??= new();
        plan.Storage.Clear();
        plan.Receiver = SemanticsKind.Ref;
        var symbol = this.symbols[function];
        // The declaration identity distinguishes environments with identical storage.
        plan.EnvironmentType = this.InternType(BoundTypeKind.Closure, symbol, SemanticsKind.Owner, []);
        symbol.Type = function.ReturnType is { } annotation ? this.BindType(annotation, scope) : null;
        symbol.HeaderBound = true;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            this.symbols[function.Parameters[i]].Type = this.BindType(function.Parameters[i].Type, scope);
        }

        if (function.Captures is { } captures)
        {
            foreach (var capture in captures)
            {
                if (capture.Operation is not null)
                {
                    return Fail(function, BindingFailure.Unsupported);
                }

                if (scope.Values.ContainsKey(capture.Name))
                {
                    return Fail(function, BindingFailure.Duplicate);
                }

                var source = this.Lookup(capture.Name, scope.Parent!, function, false);
                if (source is null || this.Capture(function, source, scope) is not { } environment)
                {
                    return Fail(function, BindingFailure.Capture);
                }

                environment.MutableCapture = capture.IsMutable;
            }
        }

        if (function.Body is { } block)
        {
            // Unannotated block results still need the general result-inference pass.
            if (symbol.Type is null)
            {
                return Fail(function, BindingFailure.Unsupported);
            }

            this.BindNode(block, scope);
        }
        else if (function.ExpressionBody is { } expression)
        {
            var result = this.RequireType(expression, scope, symbol.Type);
            symbol.Type ??= result;
        }

        if (symbol.Type is null)
        {
            return Complete(function, null);
        }

        var buffer = this.typeScratch.Rent(Math.Max(function.Parameters.Count, plan.Storage.Count));
        try
        {
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (function.Parameters[i].Type.BoundType is not { } input)
                {
                    return Complete(function, null);
                }

                buffer[i] = input;
            }

            var inputs = function.Parameters.Count == 0 ? BoundType.Unit : this.InternType(BoundTypeKind.Tuple, null, SemanticsKind.Owner, ((BoundType[])(object)buffer).AsSpan(0, function.Parameters.Count));
            plan.Signature = this.InternType(BoundTypeKind.Function, null, SemanticsKind.Owner, [inputs, symbol.Type]);
            for (var i = 0; i < plan.Storage.Count; i++)
            {
                buffer[i] = plan.Storage[i].Environment.Type!;
            }

            plan.EnvironmentType = this.InternType(BoundTypeKind.Closure, symbol, SemanticsKind.Owner, ((BoundType[])(object)buffer).AsSpan(0, plan.Storage.Count));
        }
        finally
        {
            this.typeScratch.Return(buffer, clearArray: true);
        }

        var effects = new ClosureEffects(this, function, plan);
        (function.Body as Koto ?? function.ExpressionBody)?.VisitChildren(effects);
        if (function.ExpressionBody is { } bodyExpression)
        {
            effects.Visit(bodyExpression);
        }

        return Complete(function, plan.EnvironmentType);
    }

    private sealed class ClosureEffects(Binding binding, FunctionKoto function, BoundClosure plan) : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            if (node is FunctionKoto nested)
            {
                if (nested.BoundClosure is { } child)
                {
                    foreach (var capture in child.Captures)
                    {
                        if (ReferenceEquals(capture.Source.Declaration, function) && binding.ProveCopy(capture.Source.Type!, function) == ConstraintProof.Refuted)
                        {
                            plan.Receiver = SemanticsKind.Owner;
                        }
                    }
                }

                return;
            }

            if (node.BoundSymbol is { Kind: BindingSymbolKind.Capture } symbol && ReferenceEquals(symbol.Declaration, function))
            {
                var use = node;
                while (use.Parent is ParenthesizedKoto parentheses)
                {
                    use = parentheses;
                }

                var valueCall = use.Parent as InvocationKoto;
                var called = valueCall?.BoundValueCall;
                var receiver = called is not null && ReferenceEquals(called.Receiver, use);
                var memberCall = use.Parent is MemberAccessKoto { Parent: InvocationKoto { BoundCall: { } selected } } member &&
                    ReferenceEquals(member.Left, use) && ReferenceEquals(selected.Receiver, use) ? selected : null;
                var memberBorrow = memberCall?.ReceiverOperation.Kind is ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow or ArgumentOperationKind.PayloadProjection;
                if ((use.Parent is BinaryKoto assignment && assignment.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals && ReferenceEquals(assignment.Left, use)) ||
                    use.Parent is UnaryKoto { Akind: KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus or KotoKind.PostfixIncrement or KotoKind.PostfixDecrement } ||
                    (receiver && called!.ReceiverKind == SemanticsKind.Uniq) || (memberBorrow && memberCall!.ReceiverOperation.ParameterType?.Semantics == SemanticsKind.Uniq))
                {
                    if (plan.Receiver != SemanticsKind.Owner)
                    {
                        plan.Receiver = SemanticsKind.Uniq;
                    }
                }
                else if (binding.ProveCopy(symbol.Type!, function) == ConstraintProof.Refuted && use.Parent is not ConversionKoto &&
                    !(receiver && called!.ReceiverKind == SemanticsKind.Ref) && !memberBorrow && !InspectedString(use))
                {
                    plan.Receiver = SemanticsKind.Owner;
                }
            }

            node.VisitChildren(this);
        }

        private static bool InspectedString(Koto use)
        {
            if (!ReferenceEquals(use.BoundType, BoundType.String))
            {
                return false;
            }

            if (use.Parent is BinaryKoto { Akind: KotoKind.EqualsEquals or KotoKind.ExclamationEquals or KotoKind.LessThan or KotoKind.LessThanEquals or KotoKind.GreaterThan or KotoKind.GreaterThanEquals })
            {
                return true;
            }

            if (use.Parent is InvocationKoto { BoundCall: { } call })
            {
                foreach (var argument in call.ArgumentOperations)
                {
                    if (ReferenceEquals(argument.Source, use) && argument.Kind is ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
