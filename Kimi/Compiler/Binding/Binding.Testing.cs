// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    /// <summary>Certifies dedicated test startup without selecting or executing product startup.</summary>
    /// <returns>The dedicated test startup certificate.</returns>
    public StartupResult CheckTestStartup()
    {
        if (!this.compilation.IsTestBuild || this.Result.Mode != BindingMode.Final)
        {
            throw new InvalidOperationException("Test startup requires final test Binding.");
        }

        this.ResetStartup();
        return this.Startup = new(OutputKind.Application, StartupKind.Test, null, null, this.Result.IsComplete);
    }

    private static bool ValidMessageTransfers(Koto node, Koto root)
    {
        if (node is FunctionKoto)
        {
            return true;
        }

        if (node is JumpKoto jump)
        {
            var target = KotoHelper.ResolveTransferTarget(jump);
            while (target is not null && !ReferenceEquals(target, root))
            {
                target = target.Parent;
            }

            if (target is null)
            {
                return false;
            }
        }

        foreach (var child in node.ChildNodes)
        {
            if (!ValidMessageTransfers(child, root))
            {
                return false;
            }
        }

        return true;
    }

    private BoundType? BindVerification(TestVerificationKoto node, BindingScope scope)
    {
        var valid = TestDefinition.IsTestOnly(node) && scope.Function is { IsGenerated: false };
        this.RequireType(node.Condition, scope, BoundType.Boolean);
        if (node.Message is { } message)
        {
            this.RequireType(message, scope, BoundType.String);
            valid &= ValidMessageTransfers(message, message);
        }

        return valid ? Complete(node, BoundType.Unit) : Fail(node, BindingFailure.InvalidTestDefinition);
    }
}
