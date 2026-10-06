// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Checking;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 8.4.10.7: descriptions are captured before the worker releases the compilation; no mutable compiler state
    // crosses into the language server. Unchecked calls and invalid premises never publish guarantees.
    internal EffectHover[] CreateCallableEffectHovers()
    {
        List<EffectHover>? results = null;
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is not InvocationKoto { BoundValueCall: { } call, BindingState: BindingState.Resolved } syntax ||
                syntax.CodeContext.SourceDocument is not { } document ||
                document.Path.StartsWith(SourceIdentity.BuiltInPrefix, StringComparison.Ordinal) ||
                CallableCore(call.ReceiverType) is not { } type || !AbstractTypes.IsAbstract(type))
            {
                continue;
            }

            var available = this.AvailableCallableEffects(type, call.DeclaredSignature, call.ReceiverKind, syntax);
            var text = new StringBuilder();
            text.Append("Call: ").AppendLine(syntax.ToString());
            text.Append("Callable: ").Append(type).Append(" is Callable<");
            if (call.ReceiverKind != SemanticsKind.Ref)
            {
                text.Append(call.ReceiverKind == SemanticsKind.Uniq ? "uniq, " : "owner, ");
            }

            text.Append(call.DeclaredSignature).AppendLine(">");
            if (available.Confined)
            {
                text.AppendLine("Available bound: confined");
            }

            if (available.Preserves)
            {
                text.AppendLine("Available bound: preserves results");
            }

            if (!available.Confined && !available.Preserves)
            {
                text.AppendLine("Available bound: none");
            }

            var scope = this.ConstraintScope(syntax);
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Constraints is not { Invalid: false } environment)
                {
                    continue;
                }

                for (var c = 0; c < this.callableEffectClauses.Count; c++)
                {
                    var clause = this.callableEffectClauses[c];
                    if (!ReferenceEquals(clause.Parent, current.Owner) || clause.BoundConstraint is not { Kind: ConstraintKind.Callable } fact ||
                        !this.AvailableConstraintFact(environment, fact) || !ReceiverCovers(fact.Mask, call.ReceiverKind) ||
                        !ReferenceEquals(this.ContractType(fact.Subject!, scope), this.ContractType(type, scope)) ||
                        !ReferenceEquals(this.ContractType(fact.RequiredType!, scope), this.ContractType(call.DeclaredSignature, scope)))
                    {
                        continue;
                    }

                    for (var e = 0; e < clause.EffectBounds.Count; e++)
                    {
                        var bound = clause.EffectBounds[e];
                        if (this.effectBoundRejections?.ContainsKey(bound) == true || IsRecovery(bound, out _))
                        {
                            continue;
                        }

                        text.Append("Declared by: ").AppendLine(current.Owner.BoundSymbol?.Name ?? "the enclosing declaration");
                        text.Append("Premise: ").Append(clause.Left).Append(" is ").Append(clause.Right)
                            .Append(" effect ").AppendLine(EffectBoundKoto.Spelling(bound.Bound));
                    }
                }
            }

            (results ??= []).Add(new(SourceIdentity.FromPath(document.Path), document.GetSourceRange(syntax.Span), text.ToString().TrimEnd()));
        }

        return results?.ToArray() ?? [];
    }
}
