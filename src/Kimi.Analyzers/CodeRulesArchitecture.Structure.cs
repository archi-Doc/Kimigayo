// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Kimi.Analyzers;

internal sealed partial class CodeRulesArchitecture
{
    private static readonly CodeRule SideTable = CodeRule.Find("KR1004")!;
    private static readonly CodeRule Gate = CodeRule.Find("KR1005")!;
    private static readonly CodeRule Dispatch = CodeRule.Find("KR1006")!;
    private static readonly CodeRule Catch = CodeRule.Find("KR1008")!;
    private readonly HashSet<INamedTypeSymbol> collections = new(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> syntaxTypes = new(SymbolEqualityComparer.Default);
    private readonly INamedTypeSymbol? syntaxKind;
    private readonly INamedTypeSymbol? exceptionType;
    private Layer? syntaxLayer;

    internal void CheckDispatch(CodeRulesSource source, SyntaxNode node, CancellationToken cancellation)
    {
        var test = false;
        if (node is BinaryExpressionSyntax binary)
        {
            if (binary.IsKind(SyntaxKind.IsExpression) || binary.IsKind(SyntaxKind.AsExpression))
            {
                var type = source.Semantics.GetTypeInfo(binary.Right, cancellation).Type;
                test = this.IsSyntaxType(type) || (binary.IsKind(SyntaxKind.IsExpression) && this.IsSyntaxKind(type));
            }
            else if (binary.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression or
                SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression or
                SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression)
            {
                test = this.IsSyntaxKind(source.Semantics.GetTypeInfo(binary.Left, cancellation).Type) ||
                    this.IsSyntaxKind(source.Semantics.GetTypeInfo(binary.Right, cancellation).Type);
            }
        }
        else if (node is PatternSyntax and not (BinaryPatternSyntax or UnaryPatternSyntax or ParenthesizedPatternSyntax or DiscardPatternSyntax or VarPatternSyntax) &&
            source.Semantics.GetOperation(node, cancellation) is IPatternOperation pattern)
        {
            var matchedType = pattern switch
            {
                ITypePatternOperation typed => typed.MatchedType,
                IDeclarationPatternOperation declaration => declaration.MatchedType,
                IRecursivePatternOperation recursive => recursive.MatchedType,
                _ => null,
            };
            var comparedType = pattern switch
            {
                IConstantPatternOperation constant => constant.Value.Type,
                IRelationalPatternOperation relational => relational.Value.Type,
                _ => matchedType,
            };
            test = this.IsSyntaxType(matchedType) || this.IsSyntaxKind(comparedType) || this.IsSyntaxKind(pattern.InputType);
            if (node.Parent is IsPatternExpressionSyntax expression)
            {
                node = expression;
            }
        }
        else if (node is CaseSwitchLabelSyntax label)
        {
            test = this.IsSyntaxKind(source.Semantics.GetTypeInfo(label.Value, cancellation).ConvertedType);
        }

        if (test)
        {
            source.Record(Dispatch, node, 1, 0, source.NodeAnchor(node.DescendantTokens()));
        }
    }

    private static TypeSyntax? CollectionCreation(SimpleNameSyntax name)
    {
        TypeSyntax type = name;
        while ((type.Parent is QualifiedNameSyntax qualified && qualified.Right == type) ||
            (type.Parent is AliasQualifiedNameSyntax alias && alias.Name == type) ||
            type.Parent is NullableTypeSyntax or RefTypeSyntax)
        {
            type = (TypeSyntax)type.Parent;
        }

        return type.Parent is ObjectCreationExpressionSyntax creation && creation.Type == type ? type : null;
    }

    private static SyntaxNode? DeclarationAnchor(SyntaxNode node)
        => node switch
        {
            VariableDeclarationSyntax declaration => declaration.Type,
            ParameterSyntax parameter => (SyntaxNode?)parameter.Type ?? parameter,
            MethodDeclarationSyntax method => method.ReturnType,
            LocalFunctionStatementSyntax local => local.ReturnType,
            PropertyDeclarationSyntax property => property.Type,
            IndexerDeclarationSyntax indexer => indexer.Type,
            DelegateDeclarationSyntax declaration => declaration.ReturnType,
            OperatorDeclarationSyntax operation => operation.ReturnType,
            ConversionOperatorDeclarationSyntax conversion => conversion.Type,
            ForEachStatementSyntax loop => loop.Type,
            CatchDeclarationSyntax declaration => declaration.Type,
            SingleVariableDesignationSyntax designation => DesignationAnchor(designation),
            _ => null,
        };

    private static SyntaxNode DesignationAnchor(SingleVariableDesignationSyntax designation)
    {
        var owner = designation.Parent;
        while (owner is ParenthesizedVariableDesignationSyntax)
        {
            owner = owner.Parent;
        }

        return owner switch
        {
            DeclarationExpressionSyntax declaration => declaration.Type,
            DeclarationPatternSyntax pattern => pattern.Type,
            RecursivePatternSyntax pattern when pattern.Type is not null => pattern.Type,
            _ => designation,
        };
    }

    private static List<ITypeSymbol> ConstraintClosure(ITypeParameterSymbol root)
    {
        var closure = new List<ITypeSymbol> { root };
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default) { root };
        for (var index = 0; index < closure.Count; index++)
        {
            if (closure[index] is ITypeParameterSymbol parameter)
            {
                foreach (var constraint in parameter.ConstraintTypes)
                {
                    if (seen.Add(constraint))
                    {
                        closure.Add(constraint);
                    }
                }
            }
        }

        return closure;
    }

    private void CheckStructure(CodeRulesSource source, SyntaxNode node, Layer? layer, CancellationToken cancellation)
    {
        if (node is ImplicitObjectCreationExpressionSyntax or CollectionExpressionSyntax && (layer is null || layer != this.syntaxLayer))
        {
            var type = source.Semantics.GetTypeInfo(node, cancellation);
            this.CheckCollection(source, node, type.Type ?? type.ConvertedType);
        }
        else if (node is CatchClauseSyntax clause)
        {
            this.CheckCatch(source, clause, cancellation);
        }
        else
        {
            this.CheckDeclaration(source, node, layer, cancellation);
        }
    }

    private void CheckDeclaration(CodeRulesSource source, SyntaxNode node, Layer? layer, CancellationToken cancellation)
    {
        var anchor = DeclarationAnchor(node);
        if (anchor is null)
        {
            return;
        }

        var declaration = node is VariableDeclarationSyntax variables ? variables.Variables.FirstOrDefault() : node;
        if (declaration is null)
        {
            return;
        }

        var symbol = source.Semantics.GetDeclaredSymbol(declaration, cancellation);
        var type = symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            IEventSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            IMethodSymbol method => method.ReturnType,
            INamedTypeSymbol { DelegateInvokeMethod: { } method } => method.ReturnType,
            _ => null,
        };
        if (type?.SpecialType == SpecialType.System_Boolean &&
            symbol is IMethodSymbol or IPropertySymbol && symbol.Name.StartsWith("Supports", StringComparison.Ordinal))
        {
            source.RecordMember(Gate, node, symbol, 1, 0);
        }

        if (layer is null || layer != this.syntaxLayer)
        {
            this.CheckCollection(source, anchor, type);
        }
    }

    private void CheckCollection(CodeRulesSource source, SyntaxNode node, ITypeSymbol? type)
    {
        var found = type is INamedTypeSymbol named && this.IsSyntaxCollection(named);
        if (!found && type is ITypeParameterSymbol { ConstraintTypes.IsEmpty: false } parameter)
        {
            foreach (var constraint in ConstraintClosure(parameter))
            {
                if (constraint is INamedTypeSymbol candidate && this.IsSyntaxCollection(candidate))
                {
                    found = true;
                    break;
                }
            }
        }

        if (found)
        {
            source.Record(SideTable, node, 1, 0, source.NodeAnchor(node.DescendantTokens()));
        }
    }

    private bool IsSyntaxCollection(INamedTypeSymbol type)
    {
        if (this.IsSyntaxCollectionContract(type))
        {
            return true;
        }

        foreach (var contract in type.AllInterfaces)
        {
            if (this.IsSyntaxCollectionContract(contract))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsSyntaxCollectionContract(INamedTypeSymbol type)
        => this.collections.Contains(type.OriginalDefinition) && this.IsSyntaxType(type.TypeArguments[0]);

    private bool IsSyntaxType(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol named)
        {
            return InFamily(named, this.syntaxTypes);
        }

        if (type is ITypeParameterSymbol { ConstraintTypes.IsEmpty: false } parameter)
        {
            foreach (var constraint in ConstraintClosure(parameter))
            {
                if (constraint is INamedTypeSymbol candidate && InFamily(candidate, this.syntaxTypes))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsSyntaxKind(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            type = nullable.TypeArguments[0];
        }

        return this.syntaxKind is not null && SymbolEqualityComparer.Default.Equals(type, this.syntaxKind);
    }

    private void CheckCatch(CodeRulesSource source, CatchClauseSyntax clause, CancellationToken cancellation)
    {
        if (clause.Filter is not null)
        {
            return;
        }

        if (this.config.Lists.TryGetValue("catch_boundaries", out var boundaries))
        {
            foreach (var boundary in boundaries)
            {
                if (source.Path.StartsWith(boundary, StringComparison.Ordinal))
                {
                    return;
                }
            }
        }

        if (clause.Declaration is null ||
            SymbolEqualityComparer.Default.Equals(source.Semantics.GetTypeInfo(clause.Declaration.Type, cancellation).Type, this.exceptionType))
        {
            source.Record(Catch, clause, 1, 0, source.NodeAnchor(clause.DescendantTokens(x => x is not BlockSyntax)));
        }
    }
}
