// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly BaseCallFailureRows baseCallFailures;

    // SPEC 6.4.4's use rules, in check order; the adapter explains each (DECISIONS BU-111).
    internal enum BaseCallCause : byte
    {
        NotCall,
        NoReceiver,
        NoBase,
        OutsideBody,
        NoSelfCapture,
        NotInstanceFunction,
    }

    private BoundType? BindBaseReference(BaseReferenceKoto reference, BindingScope scope)
    {
        reference.BasePath = null;
        if (reference.Parent is not MemberAccessKoto { Right: IdentifierNameKoto } member || !ReferenceEquals(member.Left, reference) || !IsCallee(member))
        {
            return this.FailExplained(BaseCallFailureRows.Family, reference, BindingFailure.BaseCall, (true, BaseCallCause.NotCall));
        }

        // SPEC 6.4.4: lexical self is the receiver of the function whose body holds the reference, an instance accessor's included (DECISIONS
        // BU-53), and the lexical declaring struct is that function's.
        if (this.types.EnclosingDeclaration(scope) is not
            { ReceiverIndex: >= 0, Declaration: FunctionKoto lexical, Scope.Owner: StructKoto { BoundSymbol: { } type } structure })
        {
            return this.FailExplained(BaseCallFailureRows.Family, reference, BindingFailure.BaseCall, (true, BaseCallCause.NoReceiver));
        }

        if (structure.BaseClause is not { } clause)
        {
            return this.FailExplained(BaseCallFailureRows.Family, reference, BindingFailure.BaseCall, (true, BaseCallCause.NoBase));
        }

        if (!(lexical.Body is { } body && IsWithin(reference, body)) && !(lexical.ExpressionBody is { } expression && IsWithin(reference, expression)))
        {
            return this.FailExplained(BaseCallFailureRows.Family, reference, BindingFailure.BaseCall, (true, BaseCallCause.OutsideBody));
        }

        var receiver = this.BaseReceiver(scope.Function, lexical);
        if (receiver is null)
        {
            return this.FailExplained(BaseCallFailureRows.Family, reference, BindingFailure.BaseCall, (true, BaseCallCause.NoSelfCapture));
        }

        if (structure.BindingState == BindingState.Invalid || AdtDef.Base(this.types, this.SelfType(type)) is not { } baseType)
        {
            return this.CompleteDependent(reference, structure);
        }

        reference.BasePath = this.MemberPath(null, clause, baseType);
        return this.BindReference(reference, receiver, scope);
    }

    // Lexical self as `function`, the innermost activation (a scope's Function), reads it: through the explicit self capture of each
    // anonymous function between them. `lexical` is TyCtxt.EnclosingDeclaration of that scope, for both callers (base calls, ObjectViews);
    // the first activation that is not anonymous is lexical's own, or null in an accessor's body, whose scopes record no Function.
    private BindingSymbol? BaseReceiver(FunctionKoto? function, FunctionKoto lexical)
    {
        if (function is not { IsAnonymous: true })
        {
            return this.ParameterSymbol(lexical, lexical.BoundSymbol!.ReceiverIndex);
        }

        if (function.Captures is null || this.BaseReceiver(this.scopes[function].Parent?.Function, lexical) is not { } source ||
            function.ClosureStorage is not { } closure)
        {
            return null;
        }

        for (var i = 0; i < closure.Captures.Count; i++)
        {
            if (ReferenceEquals(closure.Captures[i].Source, source))
            {
                return closure.Captures[i].Environment;
            }
        }

        return null;
    }

    private BindingSymbol? BaseMember(MemberAccessKoto member, BaseReferenceKoto reference, BindingScope scope)
    {
        if (this.BindNode(reference, scope) is not { } actual || !this.IsCurrent(reference) || reference.BasePath is not { } path ||
            member.Right is not IdentifierNameKoto name)
        {
            return null;
        }

        var selection = this.LookupMember(path.Type, name.IdentifierName, scope, LookupRole.Value, actual, path);
        var selected = selection.Member;
        if (selection.Pending)
        {
            this.Fail(reference, BindingFailure.Unsupported, true);
            return null;
        }

        if (selection.Ambiguous)
        {
            this.Fail(member, BindingFailure.Ambiguous, true);
            return null;
        }

        if (selected is null && selection.Hidden is { } hidden)
        {
            this.PublishHiddenMember(member, hidden);
            return null;
        }

        if (selected is not null)
        {
            if (selected.Kind != BindingSymbolKind.Function || selected.ReceiverIndex < 0)
            {
                this.FailExplained(BaseCallFailureRows.Family, reference, BindingFailure.BaseCall, (true, BaseCallCause.NotInstanceFunction));
                return null;
            }

            return this.PublishMemberAccess(member, selected, null, LookupRole.Value, selection);
        }

        return selected;
    }

    /// <summary>The `baseCallFailures` family's one accessor (DECISIONS BU-220): a pass family, the cause a direct base call fails for
    /// (SPEC 6.4.4), stored with its failure by the recorder (RecordedFact) and written by FailExplained (its first cause is 0, so the row
    /// carries its presence). Its members keep the names its readers use.</summary>
    private readonly struct BaseCallFailureRows(Binding binding)
    {
        // The family's column in a NodeColumns, the store the recorder decides (RecordedFact, SetRecordedFact).
        internal static ref StampedColumn<(bool Failed, BaseCallCause Cause), PassCounter> Family(ref NodeColumns columns) => ref columns.BaseCallFailures;

        internal BaseCallCause this[Koto reference]
            => binding.RecordedFact(reference, Family) is { Failed: true } row ? row.Cause : throw new KeyNotFoundException($"`{reference}` has no base call failure in this pass.");
    }
}
