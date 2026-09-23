// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1202 // The compact internal plan accompanies its public binder.

// The source tree remains intact: calls share expressions without reparenting them.
// Every write is selected by the ordinary call binder and instantiated by the
// ordinary generic storage planner, including user-defined formatters.
internal sealed class BoundFormatting(Koto root)
{
    internal Koto Root { get; } = root;

    internal InvocationKoto Heap { get; set; } = null!;

    internal InvocationKoto Adapter { get; set; } = null!;

    internal FormattingKoto Buffer { get; set; } = null!;

    internal FormattingKoto BufferOwner { get; set; } = null!;

    internal FormattingKoto Writer { get; set; } = null!;

    internal FormattingKoto WriterOwner { get; set; } = null!;

    internal FormattingKoto Capacity { get; set; } = null!;

    internal FormattingKoto Check { get; set; } = null!;

    internal FormattingKoto Hint { get; set; } = null!;

    internal FormattingKoto Finish { get; set; } = null!;

    internal List<InvocationKoto> Writes { get; } = new();
}

public sealed partial class Binding
{
    private BoundType? BindInterpolation(InterpolatedStringKoto syntax, BindingScope scope)
    {
        var plan = new BoundFormatting(syntax);
        syntax.Formatting = plan;
        plan.Capacity = Node(FormattingOperation.Capacity, BoundType.ISize);
        plan.Heap = this.FormattingCall(syntax, KimiDeclarationId.TextHeap, [plan.Capacity], scope);
        if (plan.Heap.BoundType is not { } bufferType)
        {
            return Complete(syntax, null);
        }

        plan.BufferOwner = Node(FormattingOperation.Storage, bufferType);
        var bufferBorrow = this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [bufferType], origin: this.OriginAtom(syntax, OriginKind.Projection, 0));
        plan.Buffer = Node(FormattingOperation.Storage, bufferBorrow);
        plan.Adapter = this.FormattingCall(syntax, KimiDeclarationId.TextWriter, [plan.Buffer], scope);
        if (plan.Adapter.BoundType is not { } writerType)
        {
            return Complete(syntax, null);
        }

        var writerBorrow = this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [writerType], origin: this.OriginAtom(syntax, OriginKind.Projection, 1));
        plan.Writer = Node(FormattingOperation.Storage, writerBorrow);
        plan.WriterOwner = Node(FormattingOperation.Storage, writerType);
        plan.Check = Node(FormattingOperation.Check, BoundType.Unit);
        plan.Hint = Node(FormattingOperation.Hint, BoundType.Unit);
        plan.Finish = Node(FormattingOperation.Finish, BoundType.String);
        var valid = true;
        for (var i = 0; i < syntax.Segments.Length; i++)
        {
            this.BindNode(syntax.Segments[i], scope);
            if (syntax.Segments[i].Literal.Length != 0)
            {
                Add(syntax.Segments[i]);
            }

            if (i < syntax.Expressions.Length)
            {
                Add(syntax.Expressions[i]);
            }
        }

        return Complete(syntax, valid ? BoundType.String : null);

        FormattingKoto Node(FormattingOperation operation, BoundType type)
        {
            var node = new FormattingKoto(syntax, operation) { Parent = syntax, Plan = plan };
            node.Resolve(type);
            return node;
        }

        void Add(Koto value)
        {
            var call = this.FormattingCall(syntax, KimiDeclarationId.WriterWrite, [plan.Writer, value], scope, writerType);
            plan.Writes.Add(call);
            valid &= call.BoundCall is not null;
        }
    }

    private InvocationKoto FormattingCall(Koto root, KimiDeclarationId identity, Koto[] arguments, BindingScope scope, BoundType? declaringType = null)
    {
        var target = this.Library.GetSymbol(identity)!;
        var callee = new FormattingKoto(root, FormattingOperation.Callee)
        {
            Parent = root,
            BoundSymbol = target,
            BindingState = BindingState.Resolved,
            DeclaringType = declaringType,
        };
        Koto method = callee;
        if (identity == KimiDeclarationId.WriterWrite && arguments.Length == 2 && !IsUnfittedLiteral(arguments[1]) &&
            ReferenceEquals(this.BindNode(arguments[1], scope), BoundType.Never))
        {
            // No value reaches this write. A concrete dummy witness lets ordinary
            // call acquisition check the transfer without inventing a Never formatter.
            var unit = new FormattingKoto(root, FormattingOperation.Callee) { Parent = root };
            unit.Resolve(BoundType.Unit);
            method = new GenericsKoto(root, callee, [unit]);
        }

        var call = new InvocationKoto(root, method, arguments);
        this.nodes.Add(call);
        this.BindCall(call, scope, null);
        return call;
    }
}
