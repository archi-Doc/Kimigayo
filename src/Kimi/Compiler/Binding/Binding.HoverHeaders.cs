// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    /// <summary>Projects source contracts through the ordinary syntax writers; never includes executable bodies.</summary>
    /// <param name="declaration">The established declaration.</param>
    /// <returns>The normalized header.</returns>
    internal static string HoverHeader(Koto declaration)
    {
        new HoverHeaderGuard().Visit(declaration);
        var builder = new IndentedStringBuilder();
        try
        {
            switch (declaration)
            {
                case FunctionKoto function:
                    function.WriteHeaderTo(ref builder);
                    break;
                case PropertyKoto property:
                    property.WriteHeaderTo(ref builder);
                    break;
                case DeclarationContainerKoto container:
                    container.WriteTo(ref builder);
                    builder.AppendLine();
                    builder.IncrementIndent();
                    OriginClauses.Write(container, ref builder, false);
                    foreach (var constraint in container.ConstraintNodes)
                    {
                        constraint.WriteTo(ref builder);
                        builder.AppendLine();
                    }

                    WriteContractMembers(container.Members, ref builder);
                    builder.DecrementIndent();
                    break;
                default:
                    declaration.WriteTo(ref builder);
                    break;
            }

            var result = builder.ToString().TrimEnd();
            return result.Length <= HoverLimits.Output ? result : throw new HoverLimitException("Hover declaration output limit exceeded");
        }
        finally
        {
            builder.Dispose();
        }
    }

    private static void WriteContractMembers(IReadOnlyList<Koto> members, ref IndentedStringBuilder builder)
    {
        foreach (var member in members)
        {
            if (member is EffectBoundKoto or IsKoto { IsAssociatedConstraint: true } || member.Akind == KotoKind.AssociatedType)
            {
                member.WriteTo(ref builder);
                builder.AppendLine();
            }
            else if (member is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } conditional && conditional.Operands.Length >= 2)
            {
                conditional.Operands[0].WriteTo(ref builder);
                builder.Append(" when ");
                conditional.Operands[1].WriteTo(ref builder);
                builder.AppendLine();
                if (conditional.Operands.Length == 3 && conditional.Operands[2] is CodeBlockKoto block)
                {
                    builder.IncrementIndent();
                    WriteContractMembers(block.Items, ref builder);
                    builder.DecrementIndent();
                }
            }
        }
    }

    // Check the syntax that the header writer consumes before invoking recursive syntax writers.
    // Function bodies, initializers and unrelated members are deliberately not traversed.
    private sealed class HoverHeaderGuard : KotoVisitor
    {
        private readonly HoverBudget budget = new();

        public override void Visit(Koto node)
        {
            using var guard = this.budget.Enter();
            if (node is not (FunctionKoto or DeclarationContainerKoto or PropertyKoto or PropertyAccessorKoto) && node.Span.Length > HoverLimits.Input)
            {
                throw new HoverLimitException("Hover declaration input limit exceeded");
            }

            switch (node)
            {
                case FunctionKoto function:
                    Attachments(function);
                    this.VisitMany(function.GenericArguments);
                    foreach (var parameter in function.Parameters)
                    {
                        this.Visit(parameter.Type);
                        if (parameter.AttributeChain is { } attribute)
                        {
                            this.Visit(attribute);
                        }
                    }

                    if (function.ReturnType is { } result)
                    {
                        this.Visit(result);
                    }

                    this.VisitMany(function.TypeConstraints);
                    this.VisitMany(function.EffectBounds);
                    break;
                case DeclarationContainerKoto container:
                    Attachments(container);
                    this.VisitMany(container.GenericParameterNodes);
                    this.VisitMany(container.Bases);
                    this.VisitMany(container.ConstraintNodes);
                    Members(container.Members);
                    break;
                case PropertyKoto property:
                    Attachments(property);
                    if (property.TypeKoto is { } type)
                    {
                        this.Visit(type);
                    }

                    this.VisitMany(property.Accessors);
                    break;
                case PropertyAccessorKoto accessor:
                    Attachments(accessor);
                    if (accessor.ReceiverType is { } receiver)
                    {
                        this.Visit(receiver);
                    }

                    if (accessor.ValueType is { } input)
                    {
                        this.Visit(input);
                    }

                    if (accessor.ReturnType is { } output)
                    {
                        this.Visit(output);
                    }

                    break;
                case SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, Operands.Length: >= 2 } conditional:
                    this.Visit(conditional.Operands[0]);
                    this.Visit(conditional.Operands[1]);
                    if (conditional.Operands.Length == 3 && conditional.Operands[2] is CodeBlockKoto block)
                    {
                        Members(block.Items);
                    }

                    break;
                default:
                    base.Visit(node);
                    break;
            }

            void Attachments(Koto declaration)
            {
                if (declaration.AttributeChain is { } attribute)
                {
                    this.Visit(attribute);
                }

                this.VisitMany(OriginClauses.Get(declaration));
            }

            void Members(IReadOnlyList<Koto> members)
            {
                foreach (var member in members)
                {
                    if (member is EffectBoundKoto or IsKoto { IsAssociatedConstraint: true } || member.Akind is KotoKind.AssociatedType or KotoKind.ConditionalConformance)
                    {
                        this.Visit(member);
                    }
                }
            }
        }
    }
}
