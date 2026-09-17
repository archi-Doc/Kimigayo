// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // The retained invocation plan accompanies its binder.

/// <summary>A positional common-function invocation; the receiver is inspected, never acquired by Move.</summary>
public sealed class BoundValueCall
{
    private BoundArgumentOperation[] arguments = [];

    public Koto Receiver { get; private set; } = null!;

    public BoundType Signature { get; private set; } = null!;

    public BoundType ReturnType => this.Signature.Components[1];

    public ReadOnlySpan<BoundArgumentOperation> Arguments => this.arguments;

    internal void Set(Koto receiver, BoundType signature, ReadOnlySpan<BoundArgumentOperation> arguments)
    {
        this.Receiver = receiver;
        this.Signature = signature;
        if (this.arguments.Length != arguments.Length)
        {
            this.arguments = new BoundArgumentOperation[arguments.Length];
        }

        arguments.CopyTo(this.arguments);
    }
}

public sealed partial class Binding
{
    private BoundType? BindValueCall(InvocationKoto call, BindingScope scope, BoundType signature)
    {
        var parameters = signature.Components[0];
        var count = ReferenceEquals(parameters, BoundType.Unit) ? 0 : parameters.Components.Count;
        if (call.Method is GenericsKoto || count != call.ArgumentNodes.Count)
        {
            return Fail(call, BindingFailure.NoApplicableCandidate);
        }

        // Public Origin substitution and borrowed callable receivers require their
        // own retained Loan contracts; do not silently discard those dependencies.
        if (HasDeclaredOrigins(signature))
        {
            return Fail(call, BindingFailure.Unsupported);
        }

        var operations = this.argumentOperationScratch.Rent(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                if (call.GetArgumentLabel(i) is not null)
                {
                    return Fail(call, BindingFailure.NoApplicableCandidate);
                }

                var source = call.ArgumentNodes[i];
                var parameter = parameters.Components[i];
                var literal = IsUnfittedLiteral(source);
                var actual = this.BindNode(source, scope, parameter);
                if (actual is null || source.BindingState != BindingState.Resolved)
                {
                    return Complete(call, null);
                }

                if (!this.AdaptInput(source, parameter, actual, scope, null, null, out var adapted, out var quality, out var kind) ||
                    !this.CheckTypeUse(adapted, parameter, source))
                {
                    return Fail(call, BindingFailure.NoApplicableCandidate);
                }

                operations[i] = new(source, actual, parameter, kind, literal ? ArgumentAdaptation.Literal : quality, ParameterIndex: i);
            }

            call.ValueCallStorage ??= new();
            call.ValueCallStorage.Set(call.Method, signature, operations.AsSpan(0, count));
            call.IsValueCall = true;
            return Complete(call, signature.Components[1]);
        }
        finally
        {
            this.argumentOperationScratch.Return(operations, clearArray: true);
        }
    }
}
