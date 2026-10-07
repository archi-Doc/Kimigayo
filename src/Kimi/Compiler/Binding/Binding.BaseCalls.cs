// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private Dictionary<Koto, string>? baseCallFailures;

    private BoundType? BindBaseReference(SyntaxFormKoto reference, BindingScope scope)
    {
        if (reference.Parent is not MemberAccessKoto { Right: IdentifierNameKoto } member || !ReferenceEquals(member.Left, reference) || !IsCallee(member))
        {
            return this.FailExplained(ref this.baseCallFailures, reference, BindingFailure.BaseCall, "base has no value; use base.name(arguments)");
        }

        var lexical = scope.Function;
        while (lexical is { IsAnonymous: true })
        {
            lexical = this.scopes[lexical].Parent?.Function;
        }

        if (lexical?.BoundSymbol is not { ReceiverIndex: >= 0, Scope.Owner: StructKoto { BoundSymbol: { } type } structure })
        {
            return this.FailExplained(ref this.baseCallFailures, reference, BindingFailure.BaseCall, "base requires a lexical derived instance function; a named local function has no implicit receiver");
        }

        if (structure.Bases.Count == 0)
        {
            return this.FailExplained(ref this.baseCallFailures, reference, BindingFailure.BaseCall, "the lexical declaring struct has no direct base");
        }

        var receiver = this.Lookup("self", scope, reference, false);
        if (receiver is null || !ReferenceEquals(receiver.Scope.Function, scope.Function))
        {
            return this.FailExplained(ref this.baseCallFailures, reference, BindingFailure.BaseCall, "base in an anonymous function requires an explicit self capture at each function boundary");
        }

        if (structure.BindingState == BindingState.Invalid || this.StoredBase(this.SelfType(type)) is null)
        {
            return this.CompleteDependent(reference, structure);
        }

        reference.BoundSymbol = receiver;
        // Keep source syntax and lexical identity without fabricating a first-class base value or
        // a static-call fallback. The selected implementation, receiver proof and generation must agree.
        return this.Fail(reference, BindingFailure.Unsupported, true);
    }

    private void ReportBaseCall(SyntaxFormKoto reference, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        if (code == DiagnosticCode.InvalidBaseCall_Kd)
        {
            reference.Report(requirement, code, evidence: [this.baseCallFailures![reference]]);
        }
        else
        {
            reference.Report(requirement, code, note: "Direct base-call syntax and lexical self are retained; implementation selection, receiver-completeness proofs and direct-entry generation are not yet implemented");
        }
    }
}
