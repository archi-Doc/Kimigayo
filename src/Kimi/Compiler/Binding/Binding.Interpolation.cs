// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1202 // The compact internal plan accompanies its public binder.

// The source tree remains intact: calls share expressions without reparenting them.
// Every write is selected by the ordinary call binder and instantiated by the
// ordinary generic storage planner, including user-defined formatters.
internal sealed class BoundFormatting(Koto root)
{
    private readonly List<FormattingKoto> nodes = new();
    private readonly List<InvocationKoto> calls = new();
    private int nextNode;
    private int nextCall;

    internal bool Active { get; set; }

    internal Koto Root { get; } = root;

    internal InvocationKoto? Acquisition { get; set; }

    internal InvocationKoto Outcome { get; set; } = null!;

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

    internal static BoundFormatting Begin(Koto root)
    {
        var plan = root.FormattingStorage ??= new(root);
        plan.Active = true;
        plan.Acquisition = null;
        plan.Writes.Clear();
        plan.nextNode = 0;
        plan.nextCall = 0;
        return plan;
    }

    internal FormattingKoto Node(FormattingOperation operation, BoundType type)
    {
        var index = this.nextNode++;
        if (index == this.nodes.Count)
        {
            this.nodes.Add(new(this.Root, operation) { Parent = this.Root, Plan = this });
        }

        var node = this.nodes[index];
        if (node.Operation != operation || !node.Span.Equals(this.Root.Span))
        {
            this.nodes[index] = node = new(this.Root, operation) { Parent = this.Root, Plan = this };
        }

        node.Resolve(type);
        return node;
    }

    internal InvocationKoto Call(BindingSymbol target, ReadOnlySpan<Koto> arguments, BoundType? declaringType, bool never)
    {
        var index = this.nextCall++;
        if (index == this.calls.Count)
        {
            this.calls.Add(Create(arguments.Length));
        }

        var call = this.calls[index];
        if (call.ArgumentNodes.Count != arguments.Length || (call.Method is GenericsKoto) != never || !call.Span.Equals(this.Root.Span))
        {
            this.calls[index] = call = Create(arguments.Length);
        }

        var callee = (FormattingKoto)(call.Method is GenericsKoto generic ? generic.Identifier! : call.Method);
        callee.BoundSymbol = target;
        callee.BoundMeaning = null;
        callee.BindingFailure = BindingFailure.None;
        callee.DeclaringType = declaringType;
        callee.BindingState = BindingState.Resolved;
        arguments.CopyTo((Koto[])call.ArgumentNodes);
        call.BindingState = BindingState.Unvisited;
        call.BindingFailure = BindingFailure.None;
        call.BoundMeaning = null;
        call.BoundSymbol = null;
        call.ErasedFunctionType = null;
        return call;

        InvocationKoto Create(int count)
        {
            var callee = new FormattingKoto(this.Root, FormattingOperation.Callee) { Parent = this.Root };
            Koto method = callee;
            if (never)
            {
                var unit = new FormattingKoto(this.Root, FormattingOperation.Callee) { Parent = this.Root };
                unit.Resolve(BoundType.Unit);
                method = new GenericsKoto(this.Root, callee, [unit]);
            }

            return new(this.Root, method, new Koto[count]);
        }
    }
}

public sealed partial class Binding
{
    private BoundType? BindTryWrite(UnaryKoto syntax, BindingScope scope)
    {
        if (syntax.Operand is not InvocationKoto { ArgumentNodes.Count: 2 } source ||
            source.GetArgumentLabel(0) is not null || source.GetArgumentLabel(1) is not null ||
            source.ArgumentNodes[1] is not (StringLiteralKoto or InterpolatedStringKoto))
        {
            return Fail(syntax, BindingFailure.Unsupported);
        }

        var actual = this.BindNode(source.ArgumentNodes[0], scope);
        var writerType = actual is { Semantics: not SemanticsKind.Owner, Components.Count: 1 } ? actual.Components[0] : actual;
        if (ReferenceEquals(actual, BoundType.Never))
        {
            writerType = this.Library.GetSymbol(KimiDeclarationId.Utf8Writer)!.Type;
        }

        if (writerType?.Symbol?.LibraryDeclaration != KimiDeclarationId.Utf8Writer)
        {
            return Fail(syntax, BindingFailure.TypeMismatch);
        }

        var plan = BoundFormatting.Begin(syntax);
        var dummy = plan.Node(FormattingOperation.Storage, BoundType.Unit);
        plan.Acquisition = this.FormattingCall(syntax, KimiDeclarationId.WriterWrite, [source.ArgumentNodes[0], dummy], scope, writerType);
        if (plan.Acquisition.BoundCall is not { } acquired)
        {
            plan.Active = false;
            return Complete(syntax, null);
        }

        plan.Writer = plan.Node(FormattingOperation.Storage, this.PreparedBorrowType(source.ArgumentNodes[0], acquired.ArgumentOperations[0].ParameterType!));
        plan.Check = plan.Node(FormattingOperation.Status, BoundType.Boolean);
        var valid = true;
        if (source.ArgumentNodes[1] is InterpolatedStringKoto text)
        {
            for (var i = 0; i < text.Segments.Length; i++)
            {
                this.BindNode(text.Segments[i], scope);
                if (text.Segments[i].Literal.Length != 0)
                {
                    valid &= this.FormattingWrite(plan, text.Segments[i], scope, writerType);
                }

                if (i < text.Expressions.Length)
                {
                    valid &= this.FormattingWrite(plan, text.Expressions[i], scope, writerType);
                }
            }

            Complete(text, BoundType.String);
        }
        else
        {
            valid &= this.FormattingWrite(plan, source.ArgumentNodes[1], scope, writerType);
        }

        plan.Outcome = this.FormattingCall(syntax, KimiDeclarationId.WriterStatus, [plan.Writer], scope, writerType);
        Complete(source.Method, BoundType.Unit);
        Complete(source, plan.Outcome.BoundType);
        return Complete(syntax, valid ? plan.Outcome.BoundType : null);
    }

    private BoundType? BindInterpolation(InterpolatedStringKoto syntax, BindingScope scope)
    {
        var plan = BoundFormatting.Begin(syntax);
        plan.Capacity = plan.Node(FormattingOperation.Capacity, BoundType.ISize);
        plan.Heap = this.FormattingCall(syntax, KimiDeclarationId.TextHeap, [plan.Capacity], scope);
        if (plan.Heap.BoundType is not { } bufferType)
        {
            return Complete(syntax, null);
        }

        plan.BufferOwner = plan.Node(FormattingOperation.Storage, bufferType);
        var bufferBorrow = this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [bufferType], origin: this.OriginAtom(plan.Heap, OriginKind.Projection, 0));
        plan.Buffer = plan.Node(FormattingOperation.Storage, bufferBorrow);
        plan.Adapter = this.FormattingCall(syntax, KimiDeclarationId.TextWriter, [plan.Buffer], scope);
        if (plan.Adapter.BoundType is not { } writerType)
        {
            return Complete(syntax, null);
        }

        var writerBorrow = this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [writerType], origin: this.OriginAtom(plan.Adapter, OriginKind.Projection, 0));
        plan.Writer = plan.Node(FormattingOperation.Storage, writerBorrow);
        plan.WriterOwner = plan.Node(FormattingOperation.Storage, writerType);
        plan.Check = plan.Node(FormattingOperation.Check, BoundType.Unit);
        plan.Hint = plan.Node(FormattingOperation.Hint, BoundType.Unit);
        plan.Finish = plan.Node(FormattingOperation.Finish, BoundType.String);
        var valid = true;
        for (var i = 0; i < syntax.Segments.Length; i++)
        {
            this.BindNode(syntax.Segments[i], scope);
            if (syntax.Segments[i].Literal.Length != 0)
            {
                valid &= this.FormattingWrite(plan, syntax.Segments[i], scope, writerType);
            }

            if (i < syntax.Expressions.Length)
            {
                valid &= this.FormattingWrite(plan, syntax.Expressions[i], scope, writerType);
            }
        }

        return Complete(syntax, valid ? BoundType.String : null);
    }

    private bool FormattingWrite(BoundFormatting plan, Koto value, BindingScope scope, BoundType writerType)
    {
        var call = this.FormattingCall(plan.Root, KimiDeclarationId.WriterWrite, [plan.Writer, value], scope, writerType);
        plan.Writes.Add(call);
        return call.BoundCall is not null;
    }

    private InvocationKoto FormattingCall(Koto root, KimiDeclarationId identity, ReadOnlySpan<Koto> arguments, BindingScope scope, BoundType? declaringType = null)
    {
        var target = this.Library.GetSymbol(identity)!;
        // Never needs only an acquisition check; no formatter can run for it.
        var never = identity == KimiDeclarationId.WriterWrite && arguments.Length == 2 && !IsUnfittedLiteral(arguments[1]) &&
            ReferenceEquals(this.BindNode(arguments[1], scope), BoundType.Never);
        var call = root.Formatting!.Call(target, arguments, declaringType, never);
        this.nodes.Add(call);
        this.BindCall(call, scope, null);
        return call;
    }
}
