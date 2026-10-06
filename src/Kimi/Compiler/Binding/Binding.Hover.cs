// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Checking;
using Kimi.Compiler.Documentation;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1204 // Keep the one-shot projection in execution order.

public sealed partial class Binding
{
    internal HoverSnapshot CreateHoverSnapshot()
        => this.compilation.HoverFailure is { } failure ? throw new InvalidOperationException(failure) : new HoverBuilder(this).Build();

    // This builder is local to optional post-check projection. Its dictionaries and all compiler references die together.
    private sealed partial class HoverBuilder(Binding binding)
    {
        private readonly Dictionary<Koto, HoverDeclaration> declarations = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Koto, HoverInfo?> descriptions = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Koto, List<DocumentationComment>> comments = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Koto, List<HoverOrigin>> origins = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Kotonoha, HoverPlacement> placements = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<(BoundType Type, BindingScope Scope), ConstraintProof> copies = new();
        private readonly Dictionary<BoundType, HoverDeclaration> builtinTypes = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<BoundType, string> typeNames = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, HoverKey> identities = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<(HoverDeclaration Declaration, BoundType Type, ConstraintProof Copy), HoverInfo> typeDescriptions = new();
        private readonly Dictionary<SourceDocument, Dictionary<SourceSpan, Candidate>> documents = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<string> syntaxErrorSources = new(SourceIdentity.PathComparer);
        private readonly HashSet<string> documentationErrorSources = new(SourceIdentity.PathComparer);
        private readonly List<EffectHover> legacyEffects = [];
        private readonly HoverBudget budget = new(maximumWork: 4 * HoverLimits.Work);

        internal HoverSnapshot Build()
        {
            var compilation = binding.compilation;
            foreach (var failedSource in compilation.DocumentationFailureSources)
            {
                this.documentationErrorSources.Add(failedSource.Path);
            }

            foreach (var source in compilation.Kotonoha.SourceDocuments)
            {
                if (!source.Path.StartsWith(SourceIdentity.BuiltInPrefix, StringComparison.Ordinal) &&
                    (!source.IsTestOnly || compilation.IsTestBuild))
                {
                    this.documents.TryAdd(source, []);
                }
            }

            foreach (var module in compilation.SourceModules.Append(compilation.Library.Kotonoha))
            {
                foreach (var source in module.SourceDocuments)
                {
                    if (compilation.Diagnostics.HasSyntaxErrors(source))
                    {
                        this.syntaxErrorSources.Add(source.Path);
                    }

                    if (compilation.HasDocumentationFailure(source))
                    {
                        this.documentationErrorSources.Add(source.Path);
                    }
                }

                foreach (var source in module.DocumentationSources)
                {
                    try
                    {
                        if (compilation.HasDocumentationFailure(source.Source))
                        {
                            this.documentationErrorSources.Add(source.Source.Path);
                            continue;
                        }

                        foreach (var comment in source.Comments)
                        {
                            if (comment.IsSelected && comment.Declaration is { } declaration)
                            {
                                declaration = Canonical(declaration);
                                if (!this.comments.TryGetValue(declaration, out var list))
                                {
                                    this.comments.Add(declaration, list = []);
                                }

                                list.Add(comment);
                            }
                        }
                    }
                    catch (Exception ex) when (Compilation.OptionalHoverFailure(ex))
                    {
                        compilation.RecordDocumentationFailure(source.Source, ex.Message);
                        this.documentationErrorSources.Add(source.Source.Path);
                    }
                }
            }

            foreach (var anchor in compilation.HoverAnchors)
            {
                if (IsDeclaration(anchor.Syntax))
                {
                    var declaration = Canonical(anchor.Syntax);
                    if (!this.origins.TryGetValue(declaration, out var list))
                    {
                        this.origins.Add(declaration, list = []);
                    }

                    list.Add(new(this.Project(declaration), anchor.Source.Path, anchor.Token));
                }
            }

            // Explicit parser positions own their tokens. References supply names; selected direct calls augment those names.
            foreach (var anchor in compilation.HoverAnchors)
            {
                this.Add(anchor.Source, anchor.Token, Effective(anchor.Syntax), 2);
            }

            foreach (var node in binding.nodes)
            {
                if (node.CodeContext.SourceDocument is not { } source || !this.documents.ContainsKey(source))
                {
                    continue;
                }

                if (node is IdentifierNameKoto identifier &&
                    source.SourceText.AsSpan(node.Span.Start, node.Span.Length).SequenceEqual(identifier.IdentifierName))
                {
                    this.Add(source, node.Span, Effective(node), 1);
                }
                else if (node is InvocationKoto call && Name(call.Method) is { } name)
                {
                    this.Add(source, name.Span, call, 3);
                }
            }

            var result = new Dictionary<SourceIdentity, HoverDocument>(this.documents.Count);
            foreach (var document in this.documents)
            {
                var entries = new List<HoverEntry>(document.Value.Count);
                foreach (var candidate in document.Value)
                {
                    if (candidate.Value.Info is { } info)
                    {
                        entries.Add(new(candidate.Key, info));
                    }
                }

                entries.Sort(static (a, b) => a.Span.Start.CompareTo(b.Span.Start));
                // Parser tokens cannot overlap. A conflicting projection is unavailable, never guessed by search order.
                for (var i = entries.Count - 1; i > 0; i--)
                {
                    if (entries[i - 1].Span.End > entries[i].Span.Start)
                    {
                        entries.RemoveAt(i);
                        entries.RemoveAt(i - 1);
                        i--;
                    }
                }

                result.Add(SourceIdentity.FromPath(document.Key.Path), new(document.Key, entries.ToArray()));
            }

            return new(this.legacyEffects.ToArray()) { Documents = result };
        }

        private static Koto Canonical(Koto declaration)
            => declaration is StructKoto && declaration.BoundSymbol is { } symbol ? symbol.Declaration : declaration;

        private static bool IsDeclaration(Koto node)
            => node is StructKoto or EnumKoto or ContractKoto or PropertyKoto or GenericParameterKoto or FunctionKoto { IsAnonymous: false };

        private static Koto? Name(Koto syntax) => syntax switch
        {
            IdentifierNameKoto or TypeSemanticsKoto { Type: null } or SyntaxFormKoto { Akind: KotoKind.ConstructorReference } => syntax,
            MemberAccessKoto member => Name(member.Right),
            GenericsKoto { Identifier: { } identifier } => Name(identifier),
            OriginApplicationKoto application => Name(application.Type),
            _ => null,
        };

        private static Koto Effective(Koto syntax)
        {
            while (syntax.Parent is { } parent && ((parent is GenericsKoto generic && ReferenceEquals(generic.Identifier, syntax)) ||
                (parent is MemberAccessKoto member && ReferenceEquals(member.Right, syntax)) ||
                (parent is TypeSemanticsKoto { IsTransparentWrapper: true } wrapper && ReferenceEquals(wrapper.Type, syntax)) ||
                (parent is OriginApplicationKoto application && ReferenceEquals(application.Type, syntax))))
            {
                syntax = parent;
            }

            return syntax;
        }

        private static BindingSymbol? ReferenceSymbol(Koto syntax) => syntax switch
        {
            TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner } => ReferenceSymbol(inner) ?? syntax.BoundSymbol,
            OriginApplicationKoto application => ReferenceSymbol(application.Type) ?? syntax.BoundSymbol,
            GenericsKoto { Identifier: { } identifier } => syntax.BoundSymbol ?? ReferenceSymbol(identifier),
            _ => syntax.BoundSymbol,
        };

        private void Add(SourceDocument source, SourceSpan span, Koto syntax, int priority)
        {
            this.budget.Charge();
            if (!this.documents.TryGetValue(source, out var entries) || span.Length == 0 || span.End > source.SourceText.Length)
            {
                return;
            }

            if (entries.TryGetValue(span, out var existing))
            {
                if (existing.Priority > priority || ReferenceEquals(existing.Syntax, syntax))
                {
                    return;
                }

                if (existing.Priority == priority)
                {
                    entries[span] = new(syntax, null, priority);
                    return;
                }
            }

            if (!this.descriptions.TryGetValue(syntax, out var info))
            {
                info = this.Describe(syntax);
                this.descriptions.Add(syntax, info);
            }

            entries[span] = new(syntax, info, priority);
        }

        private HoverInfo? Describe(Koto syntax)
        {
            if (IsRecovery(syntax, out _) || (syntax.BindingState != BindingState.Resolved && !IsDeclaration(syntax)))
            {
                return null;
            }

            var symbol = ReferenceSymbol(syntax);
            if (syntax is InvocationKoto invocation)
            {
                return this.Call(invocation);
            }

            var declaration = IsDeclaration(syntax) ? syntax : symbol?.Declaration;
            if (symbol?.Kind == BindingSymbolKind.AssociatedType && syntax.Parent is IsKoto { IsAssociatedConstraint: true } specification &&
                ReferenceEquals(specification.Left, syntax))
            {
                declaration = specification;
            }

            if (declaration is ContractKoto)
            {
                return symbol?.Contract is not null ? new([this.Declaration(declaration)]) : null;
            }

            if (symbol?.Kind is BindingSymbolKind.Function or BindingSymbolKind.Property)
            {
                if (syntax is not FunctionKoto && declaration is FunctionKoto function)
                {
                    declaration = binding.GetSpecializationOriginal(function) ?? function;
                }

                return symbol.Type is not null && declaration is not null
                    ? new([this.Declaration(declaration)], TypeIdentity: this.TypeIdentity(symbol.Type)) : null;
            }

            if (symbol is not null && symbol.Kind is not (BindingSymbolKind.Type or BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsTarget or BindingSymbolKind.AssociatedType))
            {
                return null;
            }

            var isType = syntax is TypeKoto or GenericsKoto or OriginApplicationKoto ||
                symbol?.Kind is BindingSymbolKind.Type or BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsTarget or BindingSymbolKind.AssociatedType;
            if (!isType || symbol?.Kind is BindingSymbolKind.SemanticsParameter or BindingSymbolKind.LengthParameter ||
                syntax.BoundType is not { } type)
            {
                return null;
            }

            if (symbol is null && syntax is TypeSemanticsKoto { Type: null, Identifier: not "Self" } simple &&
                (type.Kind != BoundTypeKind.Primitive || simple.Identifier != type.Name))
            {
                return null;
            }

            if (syntax is StructKoto or EnumKoto && symbol is not null)
            {
                type = binding.SelfType(symbol);
            }

            if (declaration is not (StructKoto or EnumKoto or GenericParameterKoto) && symbol?.Kind != BindingSymbolKind.AssociatedType)
            {
                declaration = type.Symbol?.Declaration;
            }

            var declared = declaration is null || (declaration is not (StructKoto or EnumKoto or GenericParameterKoto) && symbol?.Kind != BindingSymbolKind.AssociatedType)
                ? this.Builtin(type)
                : this.Declaration(declaration);
            var scope = binding.ConstraintScope(syntax);
            if (!this.copies.TryGetValue((type, scope), out var copy))
            {
                var invalid = false;
                for (var current = scope; current is not null; current = current.Parent)
                {
                    invalid |= current.Constraints is { Invalid: true };
                }

                copy = invalid ? ConstraintProof.Error : binding.ProveCopy(type, syntax);
                this.copies.Add((type, scope), copy);
            }

            if (!this.typeDescriptions.TryGetValue((declared, type, copy), out var info))
            {
                var spelling = this.TypeName(type);
                info = new([declared], "Type: " + spelling, spelling, copy, TypeIdentity: this.TypeIdentity(type));
                this.typeDescriptions.Add((declared, type, copy), info);
            }

            return info;
        }

        private HoverDeclaration Builtin(BoundType type)
        {
            if (!this.builtinTypes.TryGetValue(type, out var declaration))
            {
                declaration = new("Built-in type", string.Empty, this.TypeName(type), [], []);
                this.builtinTypes.Add(type, declaration);
            }

            return declaration;
        }

        private HoverDeclaration Declaration(Koto syntax)
        {
            syntax = Canonical(syntax);
            if (this.declarations.TryGetValue(syntax, out var result))
            {
                return result;
            }

            var kind = syntax switch
            {
                ContractKoto => "Contract",
                StructKoto or EnumKoto => "Type",
                PropertyKoto => "Property",
                FunctionKoto => "Function",
                GenericParameterKoto => "Type parameter",
                _ => "Associated type",
            };
            var owner = Owner(syntax);
            var source = syntax.CodeContext.SourceDocument;
            var generated = syntax is FunctionKoto { IsGenerated: true } ||
                (syntax.Parent is StructKoto container && ReferenceEquals(container.ImplicitConstructor, syntax));
            var origins = generated ? [] : this.origins.TryGetValue(syntax, out var fragments)
                ? fragments.ToArray()
                : source is null ? [] : new HoverOrigin[] { new(this.Project(syntax), source.Path, syntax.Span) };
            Array.Sort(origins, static (a, b) =>
            {
                var result = string.CompareOrdinal(a.Project, b.Project);
                if (result == 0)
                {
                    result = string.CompareOrdinal(a.Source, b.Source);
                }

                return result != 0 ? result : a.Name.Start.CompareTo(b.Name.Start);
            });
            var documentation = this.Documentation(syntax, out var projectionFailed);
            var deferred = source is not null && binding.compilation.Diagnostics.HasSyntaxErrors(source);
            var documentationFailed = projectionFailed || (source is not null && binding.compilation.HasDocumentationFailure(source));
            foreach (var origin in origins)
            {
                deferred |= this.syntaxErrorSources.Contains(origin.Source);
                documentationFailed |= this.documentationErrorSources.Contains(origin.Source);
            }

            var header = HoverHeader(syntax);
            if (syntax is GenericParameterKoto parameter && parameter.Parent is { } parent)
            {
                var parentDeclaration = this.Declaration(parent);
                result = new(kind, owner, header, origins, parentDeclaration.Documentation, parentDeclaration.DocumentationNotice, parameter.Identifier, this.DeclarationDetails(syntax));
            }
            else
            {
                var notice = documentationFailed ? (deferred ? "Documentation deferred: syntax errors\nDocumentation unavailable: collection failure" : "Documentation unavailable: collection failure") : deferred ? "Documentation deferred: syntax errors" : null;
                result = new(kind, owner, header, origins, documentation, notice, Details: this.DeclarationDetails(syntax), ImplementationNote: syntax is FunctionKoto { IsSpecialization: true });
            }

            result = result with { Identity = this.DeclarationIdentity(syntax) };
            this.declarations.Add(syntax, result);
            return result;
        }

        private HoverDocumentation[] Documentation(Koto syntax, out bool failed)
        {
            failed = false;
            try
            {
                if (!this.comments.TryGetValue(syntax, out var selected))
                {
                    return [];
                }

                var documentation = new List<HoverDocumentation>(selected.Count);
                var parameters = syntax.BoundSymbol is null ? [] : DocumentationMarkdown.Parameters(syntax);
                foreach (var comment in selected)
                {
                    if (binding.compilation.HasDocumentationFailure(comment.Source))
                    {
                        failed = true;
                        continue;
                    }

                    var location = comment.Owner;
                    documentation.Add(new(comment.Source, comment.Span, comment.Indent, this.Project(syntax), location.LogicalName, location.ModId, location.AdditionOrder, comment.DeclarationSpan, parameters, this.Placement(syntax.Kotonoha)));
                }

                documentation.Sort(static (a, b) =>
                {
                    var result = string.CompareOrdinal(a.Project, b.Project);
                    if (result == 0)
                    {
                        result = (a.ModId is not null).CompareTo(b.ModId is not null);
                    }

                    if (result == 0)
                    {
                        result = string.CompareOrdinal(a.ModId ?? a.LogicalName, b.ModId ?? b.LogicalName);
                    }

                    if (result == 0)
                    {
                        result = a.AdditionOrder.CompareTo(b.AdditionOrder);
                    }

                    return result != 0 ? result : a.DeclarationSpan.Start.CompareTo(b.DeclarationSpan.Start);
                });
                return documentation.ToArray();
            }
            catch (Exception ex) when (Compilation.OptionalHoverFailure(ex))
            {
                binding.compilation.RecordDocumentationFailure(syntax.CodeContext.SourceDocument, ex.Message, invalidateSource: false);
                failed = true;
                return [];
            }
        }

        private string Project(Koto syntax)
            => syntax.Kotonoha.Url.Length != 0 ? syntax.Kotonoha.Url : binding.compilation.Project.FilePath ?? binding.compilation.Project.Directory;

        private HoverPlacement Placement(Kotonoha module)
        {
            if (ReferenceEquals(module, binding.compilation.Library.Kotonoha))
            {
                return HoverPlacement.Unsupported;
            }

            if (!this.placements.TryGetValue(module, out var placement))
            {
                var files = new SortedDictionary<string, string?>(StringComparer.Ordinal);
                foreach (var source in module.DocumentationSources)
                {
                    if (source.LogicalName is { } logical && Path.IsPathFullyQualified(source.Source.Path))
                    {
                        if (files.TryGetValue(logical, out var existing) && !SourceIdentity.PathComparer.Equals(existing, source.Source.Path))
                        {
                            files[logical] = null; // Conflicting placement is unsupported, not an arrival-order choice.
                        }
                        else
                        {
                            files.TryAdd(logical, source.Source.Path);
                        }
                    }
                }

                var root = module.SourceDirectory;
                placement = new(Path.IsPathFullyQualified(root) ? root : null, files.ToArray());
                this.placements.Add(module, placement);
            }

            return placement;
        }

        private static string Owner(Koto syntax)
        {
            var names = new List<string>();
            for (var parent = syntax.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent.BoundSymbol is { Name.Length: > 0 } symbol)
                {
                    names.Add(symbol.Name);
                }
            }

            names.Reverse();
            return names.Count == 0 ? syntax.Kotonoha.Name : string.Join('.', names);
        }

        private string CallUse(BoundCall call)
        {
            var text = new StringBuilder("Result: ").Append(this.TypeName(call.ReturnType));
            if (call.TypeArguments.Length != 0 || call.LengthArguments.Length != 0)
            {
                text.AppendLine().Append("Static arguments: ");
                for (var i = 0; i < call.TypeArguments.Length; i++)
                {
                    if (i != 0)
                    {
                        text.Append(", ");
                    }

                    if (call.TypeArguments[i] is { } argument)
                    {
                        text.Append(this.TypeName(argument));
                    }
                    else if (i < call.LengthArguments.Length && call.LengthArguments[i] is { } length)
                    {
                        text.Append(DiagnosticLengthName(length, this.budget));
                    }
                }
            }

            return text.ToString();
        }

        private string TypeName(BoundType type)
        {
            if (!this.typeNames.TryGetValue(type, out var name))
            {
                name = HoverTypeName(type);
                this.budget.Charge(name.Length);
                this.typeNames.Add(type, name);
            }

            return name;
        }

        // Identity is a DAG, not an expanded serialization: nested/repeated Types and Origin expressions share children.
        // Each key's local strings are length-prefixed and tagged, and every binder uses stable source facts.
        private HoverKey TypeIdentity(BoundType? type)
        {
            using var guard = this.budget.Enter();
            if (type is null)
            {
                return HoverKey.Missing;
            }

            if (this.identities.TryGetValue(type, out var result))
            {
                return result;
            }

            var text = new StringBuilder("type;");
            text.Append((int)type.Kind).Append(',').Append((int)type.Semantics).Append(',').Append(type.Length).Append(';');
            AppendKey(text, type.Name);
            text.Append(type.Components.Count).Append(',').Append(type.OriginArguments.Count).Append(';');
            var parts = new HoverKey[4 + type.Components.Count + type.OriginArguments.Count];
            parts[0] = this.SymbolIdentity(type.Symbol);
            parts[1] = this.OriginIdentity(type.Origin);
            parts[2] = this.LengthIdentity(type.LengthExpression);
            // Closure storage is not a public Type component. Its declaration still identifies the hidden environment.
            parts[3] = this.BinderIdentity(type.ClosureContext?.Target.Declaration);
            var index = 4;
            foreach (var component in type.Components)
            {
                parts[index++] = this.TypeIdentity(component);
            }

            foreach (var argument in type.OriginArguments)
            {
                parts[index++] = this.OriginIdentity(argument);
            }

            result = new(text.ToString(), parts);
            this.identities.Add(type, result);
            return result;
        }

        private HoverKey CallIdentity(BoundCall call)
        {
            var text = FormattableString.Invariant($"call;{call.TypeArguments.Length},{call.LengthArguments.Length},{call.Origins.Length},{call.InputOrigins.Length}");
            var parts = new HoverKey[5 + call.TypeArguments.Length + call.LengthArguments.Length + call.Origins.Length + call.InputOrigins.Length];
            parts[0] = this.TypeIdentity(call.ReturnType);
            parts[1] = this.TypeIdentity(call.DeclaringType);
            parts[2] = this.TypeIdentity(call.ConformingType);
            parts[3] = this.SymbolIdentity(call.RequirementContract);
            parts[4] = this.TypeIdentity(call.RequirementContract?.Type);
            var index = 5;
            foreach (var type in call.TypeArguments)
            {
                parts[index++] = this.TypeIdentity(type);
            }

            foreach (var length in call.LengthArguments)
            {
                parts[index++] = this.LengthIdentity(length);
            }

            foreach (var origin in call.Origins)
            {
                parts[index++] = this.OriginIdentity(origin);
            }

            foreach (var origin in call.InputOrigins)
            {
                parts[index++] = this.OriginIdentity(origin);
            }

            return new(text, parts);
        }

        private HoverKey BinderIdentity(Koto? binder)
        {
            if (binder is null)
            {
                return HoverKey.Missing;
            }

            if (!this.identities.TryGetValue(binder, out var result))
            {
                var text = new StringBuilder("binder;");
                AppendKey(text, this.Project(binder));
                AppendKey(text, binder.CodeContext.SourceDocument?.Path ?? string.Empty);
                text.Append((int)binder.Akind).Append(',').Append(binder.Span.Start).Append(',').Append(binder.Span.Length);
                result = new(text.ToString(), []);
                this.identities.Add(binder, result);
            }

            return result;
        }

        private HoverKey SymbolIdentity(BindingSymbol? symbol)
        {
            if (symbol is null)
            {
                return HoverKey.Missing;
            }

            if (!this.identities.TryGetValue(symbol, out var result))
            {
                var text = new StringBuilder("symbol;");
                AppendKey(text, symbol.Name);
                text.Append((int)symbol.Kind).Append(',').Append(symbol.Slot);
                result = new(text.ToString(), [this.BinderIdentity(symbol.Declaration)]);
                this.identities.Add(symbol, result);
            }

            return result;
        }

        private HoverKey OriginIdentity(BoundOrigin? origin)
        {
            using var guard = this.budget.Enter();
            if (origin is null)
            {
                return HoverKey.Missing;
            }

            if (!this.identities.TryGetValue(origin, out var result))
            {
                var text = new StringBuilder("origin;");
                text.Append((int)origin.Kind).Append(',').Append(origin.Slot).Append(',').Append(origin.InputIndex).Append(';');
                AppendKey(text, origin.Name);
                var parts = new HoverKey[2 + origin.Operands.Count];
                parts[0] = this.BinderIdentity(origin.Binder);
                parts[1] = this.BinderIdentity(origin.Occurrence);
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    parts[i + 2] = this.OriginIdentity(origin.Operands[i]);
                }

                result = new(text.ToString(), parts);
                this.identities.Add(origin, result);
            }

            return result;
        }

        private HoverKey LengthIdentity(BoundLength? length)
        {
            using var guard = this.budget.Enter();
            if (length is null)
            {
                return HoverKey.Missing;
            }

            if (!this.identities.TryGetValue(length, out var result))
            {
                result = new(
                    FormattableString.Invariant($"length;{(int)length.Operation},{length.Value}"),
                    [this.SymbolIdentity(length.Parameter), this.LengthIdentity(length.Left), this.LengthIdentity(length.Right)]);
                this.identities.Add(length, result);
            }

            return result;
        }

        private static void AppendKey(StringBuilder text, string value) => text.Append(value.Length).Append(':').Append(value);

        private readonly record struct Candidate(Koto Syntax, HoverInfo? Info, int Priority);
    }
}
