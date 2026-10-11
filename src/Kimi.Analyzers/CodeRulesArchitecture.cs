// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns resolved dependency, member, vocabulary and structural policy for one compilation.
// Input: validated configuration and source symbols. Output: observations through the common baseline pipeline.
// File layers, all partial declaration layers and configured type identities are resolved once, then read concurrently.
// Must not infer a layer from a namespace, stringify each reference or maintain a second diagnostic/baseline pipeline.

using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kimi.Analyzers;

internal sealed partial class CodeRulesArchitecture
{
    private static readonly CodeRule Dependency = CodeRule.Find("KR1001")!;
    private static readonly CodeRule Member = CodeRule.Find("KR1002")!;
    private static readonly CodeRule Vocabulary = CodeRule.Find("KR1003")!;
    private readonly Dictionary<SyntaxTree, Layer?> files = new();
    private readonly Dictionary<INamedTypeSymbol, Layer[]> declarations = new(SymbolEqualityComparer.Default);
    private readonly HashSet<string> globalMembers = new(StringComparer.Ordinal);
    private readonly CodeRulesConfiguration config;

    private CodeRulesArchitecture(Compilation compilation, CodeRulesConfiguration config)
    {
        this.config = config;
        if (compilation.GetTypeByMetadataName("Kimi.Compiler.Parsing.Koto") is { } syntax)
        {
            this.syntaxTypes.Add(syntax);
        }

        this.syntaxKind = compilation.GetTypeByMetadataName("Kimi.Compiler.Parsing.KotoKind");
        this.exceptionType = compilation.GetTypeByMetadataName("System.Exception");
        foreach (var name in new[] { "System.Collections.Generic.IDictionary`2", "System.Collections.Generic.IReadOnlyDictionary`2", "System.Collections.Generic.ISet`1", "System.Runtime.CompilerServices.ConditionalWeakTable`2" })
        {
            if (compilation.GetTypeByMetadataName(name) is { } type)
            {
                this.collections.Add(type);
            }
        }
    }

    internal static CodeRulesArchitecture Read(Compilation compilation, CodeRulesConfiguration config, List<Diagnostic> errors, List<Diagnostic> notices, List<List<CodeRuleObservation>> batches, CancellationToken cancellation)
    {
        var result = new CodeRulesArchitecture(compilation, config);
        var layers = new Dictionary<string, Layer>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in config.Lists)
        {
            if (!pair.Key.StartsWith("layer.", StringComparison.Ordinal))
            {
                continue;
            }

            var name = pair.Key.Substring(6);
            if (!SyntaxFacts.IsValidIdentifier(name) || name == "all")
            {
                Invalid(pair.Key, "Invalid layer name: " + name);
                continue;
            }

            layers.Add(name, new Layer(pair.Value));
            foreach (var path in pair.Value)
            {
                var star = path.IndexOf('*');
                if (!paths.Add(path) || (star >= 0 ? !path.EndsWith("/*.cs", StringComparison.Ordinal) || star != path.Length - 4 : !path.EndsWith("/", StringComparison.Ordinal)))
                {
                    Invalid(pair.Key, "Layer paths must be unique directory prefixes or direct /*.cs patterns");
                }
            }
        }

        foreach (var pair in config.Lists)
        {
            var dot = pair.Key.IndexOf('.');
            var kind = dot < 0 ? pair.Key : pair.Key.Substring(0, dot);
            if (kind is not ("deny_types" or "allow_types" or "deny_layers" or "deny_members"))
            {
                continue;
            }

            var name = pair.Key.Substring(dot + 1);
            if (kind == "deny_members" && name == "all")
            {
                foreach (var member in pair.Value)
                {
                    if (!SyntaxFacts.IsValidIdentifier(member) || !result.globalMembers.Add(member))
                    {
                        Invalid(pair.Key, "Global member prohibitions must be unique bare member names");
                    }
                }

                continue;
            }

            if (!layers.TryGetValue(name, out var layer))
            {
                Invalid(pair.Key, "Unknown consumer layer: " + name);
                continue;
            }

            foreach (var entry in pair.Value)
            {
                if (kind == "deny_layers")
                {
                    if (!layers.TryGetValue(entry, out var target) || !layer.DeniedLayers.Add(target))
                    {
                        Invalid(pair.Key, "Unknown or duplicate target layer: " + entry);
                    }

                    continue;
                }

                var memberDot = kind == "deny_members" ? entry.LastIndexOf('.') : -1;
                var typeName = memberDot < 0 ? entry : entry.Substring(0, memberDot);
                var type = compilation.GetTypeByMetadataName(typeName);
                if (type is null)
                {
                    Invalid(pair.Key, "Unknown or ambiguous configured type: " + typeName);
                    continue;
                }

                if (kind == "deny_members")
                {
                    var memberName = memberDot < 0 ? string.Empty : entry.Substring(memberDot + 1);
                    if (!SyntaxFacts.IsValidIdentifier(memberName))
                    {
                        Invalid(pair.Key, "Member selectors require a type and member name: " + entry);
                        continue;
                    }

                    if (!layer.Members.TryGetValue(memberName, out var owners))
                    {
                        layer.Members.Add(memberName, owners = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));
                    }

                    if (!owners.Add(type))
                    {
                        Invalid(pair.Key, "Duplicate member selector: " + entry);
                    }
                }
                else if (!(kind == "allow_types" ? layer.AllowedTypes : layer.DeniedTypes).Add(type))
                {
                    Invalid(pair.Key, "Duplicate type selector: " + entry);
                }
            }
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellation.ThrowIfCancellationRequested();
            var path = config.Relative(tree.FilePath);
            Layer? owner = null;
            var longest = -1;
            if (path is not null)
            {
                foreach (var layer in layers.Values)
                {
                    foreach (var prefix in layer.Paths)
                    {
                        if (prefix.Length > longest && Matches(path, prefix))
                        {
                            owner = layer;
                            longest = prefix.Length;
                        }
                    }
                }
            }

            result.files.Add(tree, owner);
        }

        layers.TryGetValue("syntax", out result.syntaxLayer);
        result.Index(compilation.Assembly.GlobalNamespace, cancellation);
        result.CheckVocabularies(compilation, errors, notices, batches, cancellation);
        return result;

        void Invalid(string option, string message) => errors.Add(Diagnostic.Create(CodeRule.Invalid, config.OptionLocation(option), message));
    }

    internal Layer? FileLayer(SyntaxTree tree) => this.files[tree];

    internal void CheckNode(CodeRulesSource source, SyntaxNode node, Layer? layer, CancellationToken cancellation)
    {
        if (node is SimpleNameSyntax name)
        {
            var collection = layer is null || layer != this.syntaxLayer ? CollectionCreation(name) : null;
            if (AttributeName(name) || (collection is null && (layer is null || !layer.HasReferences) && !this.globalMembers.Contains(name.Identifier.ValueText)))
            {
                return;
            }

            var symbol = source.Semantics.GetSymbolInfo(name, cancellation).Symbol;
            this.CheckReference(source, name, symbol, layer);
            if (collection is not null)
            {
                this.CheckCollection(source, collection, symbol is IAliasSymbol alias ? alias.Target as ITypeSymbol : symbol as ITypeSymbol);
            }
        }
        else if (node is MemberDeclarationSyntax or VariableDeclaratorSyntax && this.globalMembers.Count != 0)
        {
            foreach (var token in node.ChildTokens())
            {
                if (token.IsKind(SyntaxKind.IdentifierToken) && this.globalMembers.Contains(token.ValueText))
                {
                    var symbol = source.Semantics.GetDeclaredSymbol(node, cancellation);
                    if (MemberType(symbol) is not null && symbol is not INamedTypeSymbol)
                    {
                        source.Record(Member, node, 1, 0, source.NodeAnchor(node.DescendantTokens()));
                    }

                    break;
                }
            }
        }

        this.CheckStructure(source, node, layer, cancellation);
    }

    internal void CheckReference(CodeRulesSource source, SyntaxNode node, ISymbol? symbol, Layer? layer)
    {
        if (symbol is IAliasSymbol alias)
        {
            symbol = alias.Target;
        }

        if (symbol is IMethodSymbol { ReducedFrom: { } original })
        {
            symbol = original;
        }

        var type = MemberType(symbol);
        if (type is null)
        {
            return;
        }

        if (layer is not null && this.Forbidden(type.OriginalDefinition, layer))
        {
            source.Record(Dependency, node, 1, 0, source.NodeAnchor(node.DescendantTokens()));
        }

        if (symbol is not INamedTypeSymbol && (this.globalMembers.Contains(symbol!.Name) ||
            (layer is not null && layer.Members.TryGetValue(symbol.Name, out var owners) && InFamily(type, owners))))
        {
            source.Record(Member, node, 1, 0, source.NodeAnchor(node.DescendantTokens()));
        }
    }

    private static INamedTypeSymbol? MemberType(ISymbol? symbol) => symbol switch
    {
        INamedTypeSymbol type => type,
        IMethodSymbol { MethodKind: not (MethodKind.LocalFunction or MethodKind.AnonymousFunction) } or IPropertySymbol or IFieldSymbol or IEventSymbol => symbol.ContainingType,
        _ => null,
    };

    private static bool InFamily(INamedTypeSymbol type, HashSet<INamedTypeSymbol> owners)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (owners.Contains(current.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AttributeName(SimpleNameSyntax name)
    {
        SyntaxNode terminal = name;
        while ((terminal.Parent is QualifiedNameSyntax qualified && qualified.Right == terminal) ||
            (terminal.Parent is AliasQualifiedNameSyntax alias && alias.Name == terminal))
        {
            terminal = terminal.Parent;
        }

        return terminal.Parent is AttributeSyntax attribute && attribute.Name == terminal;
    }

    private static bool Matches(string path, string prefix)
    {
        if (!prefix.EndsWith("*.cs", StringComparison.Ordinal))
        {
            return path.StartsWith(prefix, StringComparison.Ordinal);
        }

        var length = prefix.Length - 4;
        return path.EndsWith(".cs", StringComparison.Ordinal) && path.Length > length &&
            string.CompareOrdinal(path, 0, prefix, 0, length) == 0 && path.IndexOf('/', length) < 0;
    }

    private bool Forbidden(INamedTypeSymbol type, Layer layer)
    {
        if (layer.AllowedTypes.Contains(type))
        {
            return false;
        }

        if (layer.DeniedTypes.Contains(type))
        {
            return true;
        }

        if (this.declarations.TryGetValue(type, out var declarations))
        {
            foreach (var declaration in declarations)
            {
                if (layer.DeniedLayers.Contains(declaration))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void Index(INamespaceOrTypeSymbol container, CancellationToken cancellation)
    {
        foreach (var member in container.GetMembers())
        {
            cancellation.ThrowIfCancellationRequested();
            if (member is INamespaceSymbol space)
            {
                this.Index(space, cancellation);
            }
            else if (member is INamedTypeSymbol type)
            {
                var layers = new List<Layer>();
                foreach (var declaration in type.DeclaringSyntaxReferences)
                {
                    if (this.files.TryGetValue(declaration.SyntaxTree, out var layer) && layer is not null && !layers.Contains(layer))
                    {
                        layers.Add(layer);
                    }
                }

                if (layers.Count != 0)
                {
                    this.declarations.Add(type, layers.ToArray());
                }

                this.Index(type, cancellation);
            }
        }
    }

    private void CheckVocabularies(Compilation compilation, List<Diagnostic> errors, List<Diagnostic> notices, List<List<CodeRuleObservation>> batches, CancellationToken cancellation)
    {
        if (!this.config.Lists.TryGetValue("vocabularies", out var entries))
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var location = this.config.OptionLocation("vocabularies");
        foreach (var entry in entries)
        {
            cancellation.ThrowIfCancellationRequested();
            var colon = entry.IndexOf(':');
            if (colon <= 0 || !int.TryParse(entry.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var limit) ||
                limit <= 0 || !names.Add(entry.Substring(0, colon)))
            {
                errors.Add(Diagnostic.Create(CodeRule.Invalid, location, "Vocabularies require unique metadata type names and positive limits"));
                continue;
            }

            var name = entry.Substring(0, colon);
            var type = compilation.GetTypeByMetadataName(name);
            if (type is null)
            {
                notices.Add(CodeRule.StaleEntry(this.config.Mode, location, "Closed vocabulary no longer exists: " + name));
            }
            else if (type.TypeKind != TypeKind.Enum)
            {
                errors.Add(Diagnostic.Create(CodeRule.Invalid, location, "Closed vocabulary must be an enum: " + name));
            }
            else
            {
                var count = type.GetMembers().Count(x => x is IFieldSymbol { IsImplicitlyDeclared: false, HasConstantValue: true });
                if (count > limit && type.DeclaringSyntaxReferences.FirstOrDefault() is { } declaration &&
                    this.config.Relative(declaration.SyntaxTree.FilePath) is { } path && this.config.Applies(Vocabulary, path))
                {
                    var source = new CodeRulesSource(compilation, declaration.SyntaxTree, path, cancellation);
                    var node = declaration.GetSyntax(cancellation);
                    source.Record(Vocabulary, node, count, limit, source.NodeAnchor(node.DescendantTokens()));
                    batches.Add(source.Observations!);
                }
            }
        }
    }

    internal sealed class Layer(string[] paths)
    {
        internal bool HasReferences => this.DeniedLayers.Count != 0 || this.DeniedTypes.Count != 0 || this.Members.Count != 0;

        internal string[] Paths { get; } = paths;

        internal HashSet<Layer> DeniedLayers { get; } = new();

        internal HashSet<INamedTypeSymbol> AllowedTypes { get; } = new(SymbolEqualityComparer.Default);

        internal HashSet<INamedTypeSymbol> DeniedTypes { get; } = new(SymbolEqualityComparer.Default);

        internal Dictionary<string, HashSet<INamedTypeSymbol>> Members { get; } = new(StringComparer.Ordinal);
    }
}
