// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    /// <summary>Projects source contracts through the ordinary syntax writers; never includes executable bodies.</summary>
    /// <param name="declaration">The established declaration.</param>
    /// <returns>The normalized header.</returns>
    internal static string HoverHeader(Koto declaration)
    {
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

            return builder.ToString().TrimEnd();
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
}
