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

    private MessageTransfers? messageTransfers;

    private void IndexInvalidTestAttribute(AttributeKoto attribute)
    {
        this.ClearFailure(attribute);
        this.Fail(attribute, BindingFailure.InvalidTestDefinition);
        this.nodes.Add(attribute);
    }

    private BoundType? BindVerification(TestVerificationKoto node, BindingScope scope)
    {
        var valid = TestDefinition.IsTestOnly(node) && scope.Function is { IsGenerated: false };
        this.RequireType(node.Condition, scope, BoundType.Boolean);
        if (node.Message is { } message)
        {
            this.RequireType(message, scope, BoundType.String);
            var transfers = this.messageTransfers ??= new();
            transfers.Root = message;
            transfers.Valid = true;
            transfers.Visit(message);
            transfers.Root = null;
            valid &= transfers.Valid;
        }

        return valid ? Complete(node, BoundType.Unit) : this.Fail(node, BindingFailure.InvalidTestDefinition);
    }

    // A message's transfers stay inside the message; a nested function's are its own. Root is the message during the walk only, so
    // the cached visitor keeps no edited-away tree alive.
    private sealed class MessageTransfers : KotoVisitor
    {
        internal Koto? Root { get; set; }

        internal bool Valid { get; set; }

        public override void Visit(Koto node)
        {
            if (!this.Valid || node is FunctionKoto)
            {
                return;
            }

            if (node is JumpKoto jump)
            {
                var target = KotoHelper.ResolveTransferTarget(jump);
                while (target is not null && !ReferenceEquals(target, this.Root))
                {
                    target = target.Parent;
                }

                if (target is null)
                {
                    this.Valid = false;
                    return;
                }
            }

            node.VisitChildren(this);
        }
    }
}
