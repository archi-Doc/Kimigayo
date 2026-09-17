// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Retained closure conversion and capture vocabulary.

public readonly record struct BoundCapture(BindingSymbol Source, BindingSymbol Environment);

/// <summary>A direct conversion of closure syntax to an Owned, Shared common function value.</summary>
public sealed class BoundClosure
{
    public IReadOnlyList<BoundCapture> Captures => this.Storage;

    public BoundType Signature { get; internal set; } = null!;

    internal List<BoundCapture> Storage { get; } = new();

    internal List<BindingSymbol> SymbolPool { get; } = new();
}

public sealed partial class Binding
{
    private bool ClosureSignatureFits(FunctionKoto function, BoundType expected)
    {
        if (expected.Kind != BoundTypeKind.Function || HasDeclaredOrigins(expected))
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
        if (expected is null || !this.ClosureSignatureFits(function, expected))
        {
            return Fail(function, expected is null ? BindingFailure.Unsupported : BindingFailure.TypeMismatch);
        }

        var plan = function.ClosureStorage ??= new();
        plan.Storage.Clear();
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

        // This initial executable conversion admits independent scalar snapshots.
        // Richer environments retain their unsupported boundary, never lose Loans.
        if (source.Kind is not (BindingSymbolKind.Local or BindingSymbolKind.Parameter) ||
            source.Type is not { } type || !(ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.Unit)))
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
        plan.Storage.Add(new(source, environment));
        scope.Values[source.Name] = environment;
        return environment;
    }
}
