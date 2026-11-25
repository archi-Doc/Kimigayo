// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ViewRangeCallRows viewRangeCalls;

    internal InvocationKoto? ViewRangeCall(Koto node)
        => KotoHelper.UnwrapParentheses(node) is IndexKoto { BindingState: BindingState.Resolved } index &&
            this.viewRangeCalls.TryGetValue(index, out var call) && call.BindingState == BindingState.Resolved ? call : null;

    private bool TryBindViewSelection(IndexKoto source, BindingScope scope, BoundType? receiver, out BoundType? result)
    {
        result = null;
        var core = receiver;
        while (core?.SafeReferent is { } inner)
        {
            core = inner;
        }

        if (core?.Symbol?.LibraryDeclaration != KimiDeclarationId.UniqSlice || core.Components is not [var element])
        {
            return false;
        }

        var key = this.BindPositionKey(source.Right, scope);
        if (key is null)
        {
            result = this.CompleteDependent(source, source.Right);
            return true;
        }

        var range = key is { Kind: BoundTypeKind.Nominal, Symbol.LibraryDeclaration: KimiDeclarationId.ResolvedRange } || this.ProvesClosedContract(key, this.Library.PositionRange, scope, source);
        if (!range && !this.ProvesClosedContract(key, this.Library.Position, scope, source))
        {
            this.RecordMismatch(source.Right, source.Right, key, new(null, MismatchForm.PositionType), ComparisonFailure.Shape);
            result = this.CompleteDependent(source, source.Right);
            return true;
        }

        this.CheckLiteralKey(source, core);
        var shared = this.BindViewCall(source, scope, core, key, range, false);
        if (!range && shared is not null)
        {
            this.uniqIndexing[source] = new(true, null, null);
            if (this.ExclusivelyAcquirable(source.Left))
            {
                this.BindViewCall(source, scope, core, key, false, true);
            }
        }

        result = Complete(source, range ? shared?.BoundType : this.LentElement(shared));
        return true;
    }

    private InvocationKoto? BindViewCall(IndexKoto source, BindingScope scope, BoundType receiver, BoundType key, bool range, bool exclusive)
    {
        var found = range ? this.viewRangeCalls.TryGetValue(source, out var call) : this.indexerCalls.TryGetValue((source, exclusive), out call);
        if (!found || call is null || !ReferenceEquals(((SyntheticKoto)((GenericsKoto)call.Method).Identifier!).Receiver, source.Left) || !ReferenceEquals(call.ArgumentNodes[0], source.Right))
        {
            var callee = new SyntheticKoto(source) { Parent = source, Receiver = source.Left };
            call = new InvocationKoto(source, new GenericsKoto(source, callee, [new SyntheticKoto(source)]), [source.Right]);
            if (range)
            {
                this.viewRangeCalls[source] = call;
            }
            else
            {
                this.indexerCalls[(source, exclusive)] = call;
            }
        }

        var generic = (GenericsKoto)call.Method;
        var name = range ? "slice" : exclusive ? "indexPositionUniq" : "indexPosition";
        var member = receiver.Symbol?.Declaration is DeclarationContainerKoto group && this.scopes.TryGetValue(group, out var members) && members.Values.TryGetValue(name, out var selected) ? selected : null;
        ((SyntheticKoto)generic.Identifier!).Resolve(member, null, receiver);
        ((SyntheticKoto)generic.TypeArguments[0]).Resolve(null, key, null);
        this.ResetSynthetic(call);
        this.ResetSynthetic(generic);
        return this.BindSynthesizedCall(source, call, scope) is null ? null : call;
    }

    /// <summary>The `viewRangeCalls` family's one accessor (DECISIONS BU-220): a kept family, a view's range selection's synthesized
    /// `slice` call (SPEC 4.7), made once and reset in place by each pass (ResetSyntheticCalls), kept in its key node's module's
    /// NodeColumns at its SyntaxId. Its members keep the names its readers use.</summary>
    private readonly struct ViewRangeCallRows(Binding binding)
    {
        internal FamilyRows<InvocationKoto?> Values => new(binding, static (table, _) => table.Hir.Families.ViewRangeCalls.Enumerate());

        internal InvocationKoto this[IndexKoto node]
        {
            set => HirTables.Of(node).Families.ViewRangeCalls.Ref(node.SyntaxId) = value;
        }

        internal bool TryGetValue(IndexKoto node, [NotNullWhen(true)] out InvocationKoto? value)
            => (value = node.SyntaxOwner is { } table ? table.Hir.Families.ViewRangeCalls.Get(node.SyntaxId) : null) is not null;
    }
}
