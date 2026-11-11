// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The contract of Documentation (src/Kimi/Documentation, namespace Kimi.Documentation), outside the compiler (the R7-R9 plan's §5.3,
// unit M1), stated here, in its entry file, for all its types.
// Owns: the limited-profile Markdown parser and its immutable document (SPEC 2.3; DocumentationMarkdownParser, DocumentationMarkdownDocument),
// item classification against a declaration's parameters (this facade), HTML rendering and link resolution (DocumentationLinks).
// Input: a documentation comment of Core's association (DocumentationSource), which the Tokenizer and the Parser collect, or detached text.
// Reads of the compiler, exactly these: the association's DocumentationComment with its DocumentationText,
// DocumentationSource.LogicalName (a comment's Owner) and DocumentationDiagnostic; Core's SourceDocument, the comment's and the text's
// Source (its SourceText, to map an original CRLF; passed into DocumentationDiagnostic); a declaration's syntax (Koto, FunctionKoto,
// DeclarationContainerKoto, PropertyAccessorKoto: generic parameters, origins, parameters, modifiers with ModifierKind); its Binding facts
// only through HirTables.Symbol (bound or not, the receiver index; DECISIONS R5-M193); its Kotonoha, the default project identity;
// Kimi.Diagnostics' SourceSpan; Helper's IntegerTable.
// Output: documents, items, HTML and link targets; optional DocumentationDiagnostic records, never language diagnostics.
// Invariants: no file of Compiler/, Testing/ or Diagnostics/ reads a Documentation type; its readers are hover's projection in check and
// the LSP's rendering (ArchitectureRulesTest.ComponentIsReadOnlyByItsReaders).
// Must not: write compiler state, start Binding, or read a Koto semantic slot.

using System.Runtime.InteropServices;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;

namespace Kimi.Documentation;

#pragma warning disable SA1600, SA1204 // Source-backed facade; keep presentation helpers together.

/// <summary>A source-backed fragment using the independent limited Markdown parser.</summary>
public sealed class DocumentationMarkdown
{
    private DocumentationMarkdown(DocumentationComment comment, CancellationToken token, int maximumDepth)
    {
        this.Comment = comment;
        this.Document = DocumentationMarkdownDocument.Parse(comment, token, maximumDepth);
    }

    public DocumentationComment Comment { get; }

    public DocumentationMarkdownDocument Document { get; }

    public DocumentationMarkdownNode? Summary => this.Document.Summary;

    /// <summary>Gets items using current declaration facts; unbound candidates stay unclassified.</summary>
    public ReadOnlyMemory<DocumentationMarkdownItem> Items => this.GetItems();

    public static DocumentationMarkdown Parse(DocumentationComment comment, CancellationToken cancellationToken = default, int maximumDepth = 256)
    {
        ArgumentNullException.ThrowIfNull(comment);
        return new(comment, cancellationToken, maximumDepth);
    }

    public ReadOnlyMemory<DocumentationMarkdownItem> GetItems(CancellationToken cancellationToken = default)
    {
        if (this.Document.GetItemCandidates(cancellationToken).IsEmpty)
        {
            return ReadOnlyMemory<DocumentationMarkdownItem>.Empty;
        }

        var declaration = this.Comment.Declaration;
        if (declaration is null || HirTables.Symbol(declaration) is null)
        {
            return this.Document.GetItemCandidates(cancellationToken).ToArray();
        }

        var parameters = new List<DocumentationMarkdownParameter>();
        AddParameters(declaration, parameters, cancellationToken);
        return this.Document.ClassifyItems(CollectionsMarshal.AsSpan(parameters), cancellationToken);
    }

    /// <summary>Renders with root-relative identity output placement and the default URL policy.</summary>
    /// <param name="declarationHeadingLevel">Enclosing heading level (1–6).</param>
    /// <param name="rewriteLink">Deterministic final rewrite; null disables a target.</param>
    /// <returns>Escaped HTML.</returns>
    public string ToHtml(int declarationHeadingLevel = 1, Func<string, string?>? rewriteLink = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(declarationHeadingLevel, 1);
        return this.ToHtml(new DocumentationHtmlOptions { DeclarationHeadingLevel = declarationHeadingLevel, RewriteLink = rewriteLink });
    }

    public string ToHtml(DocumentationHtmlOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configured = options with
        {
            LogicalSourceName = this.Comment.Owner.LogicalName,
            ProjectIdentity = options.ProjectIdentity ?? this.Comment.Declaration?.Kotonoha,
        };
        return this.Document.ToHtml(configured, cancellationToken);
    }

    public static string? ResolveLink(string url, string? logicalSourceName)
        => DocumentationLinks.Resolve(url, new DocumentationHtmlOptions { LogicalSourceName = logicalSourceName });

    public IEnumerable<DocumentationDiagnostic> GetDiagnostics(CancellationToken cancellationToken = default)
    {
        if (!this.Comment.IsSelected || this.Comment.Declaration is not { } declaration || HirTables.Symbol(declaration) is null)
        {
            yield break;
        }

        var items = this.GetItems(cancellationToken);
        var hasSafety = false;
        for (var i = 0; i < items.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items.Span[i];
            hasSafety |= item.Kind == DocumentationMarkdownItemKind.Standard && item.Name == "safety";
            if (item.Kind is DocumentationMarkdownItemKind.Unknown or DocumentationMarkdownItemKind.Ambiguous)
            {
                yield return new(
                    this.Comment.Source,
                    item.Node.SourceSpan,
                    item.Kind == DocumentationMarkdownItemKind.Unknown ? "UnknownDocumentationParameter" : "AmbiguousDocumentationParameter");
            }
        }

        if (this.Comment.Declaration is FunctionKoto { Modifier: var modifier } && modifier.HasFlag(ModifierKind.Unsafe) && !hasSafety)
        {
            yield return new(this.Comment.Source, this.Comment.Span, "MissingSafetyDocumentation");
        }
    }

    internal static DocumentationMarkdownParameter[] Parameters(Koto declaration, CancellationToken cancellationToken = default)
    {
        var parameters = new List<DocumentationMarkdownParameter>();
        AddParameters(declaration, parameters, cancellationToken);
        return parameters.ToArray();
    }

    private static void AddParameters(Koto declaration, List<DocumentationMarkdownParameter> parameters, CancellationToken cancellationToken)
    {
        var generics = declaration switch
        {
            FunctionKoto function => function.GenericArguments,
            DeclarationContainerKoto container => container.GenericParameterNodes,
            _ => [],
        };
        foreach (var generic in generics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            parameters.Add(new(generic.Identifier));
            if (generic.SemanticsParameter is { } semantics)
            {
                parameters.Add(new(semantics));
            }
        }

        var origins = declaration switch
        {
            FunctionKoto function => function.Origins,
            DeclarationContainerKoto container => container.OriginNames,
            PropertyAccessorKoto accessor => accessor.Origins,
            _ => [],
        };
        foreach (var origin in origins)
        {
            parameters.Add(new(origin));
        }

        if (declaration is FunctionKoto f)
        {
            for (var index = 0; index < f.Parameters.Count; index++)
            {
                parameters.Add(new(f.Parameters[index].ExternalName, index == HirTables.Symbol(f)!.ReceiverIndex));
            }
        }
    }
}
