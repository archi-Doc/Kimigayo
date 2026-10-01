// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;

namespace Kimi.Diagnostics;

/// <summary>
/// A recording target of a <see cref="DiagnosticOwner"/>, bound to one module and optionally one document. Lexical and
/// syntax reports are problems whose subject is their span; an analysis reports keyed problems.
/// </summary>
public sealed class DiagnosticCollection
{
    private readonly int module;

    internal DiagnosticCollection(DiagnosticOwner owner, string name, int module, SourceDocument? document = null)
    {
        this.Owner = owner;
        this.Name = name;
        this.module = module;
        this.Document = document;
    }

    /// <summary>Gets the owner.</summary>
    public DiagnosticOwner Owner { get; }

    /// <summary>Gets the name, which also names sources parsed from text through this target.</summary>
    public string Name { get; }

    /// <summary>Gets the document a report without an explicit document belongs to, fixed when the target is created.</summary>
    public SourceDocument? Document { get; }

    /// <summary>Gets the key of the last lexical or syntax Error reported through this target; parser recovery names it as the cause of what it synthesizes.</summary>
    internal DiagnosticKey? LastError { get; private set; }

    /// <summary>Gets a target bound to one document, registering the document in the source table in consumption order.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The bound target.</returns>
    public DiagnosticCollection For(SourceDocument document)
    {
        if (ReferenceEquals(document, this.Document))
        {
            return this;
        }

        var module = this.CurrentModule();
        this.Owner.DocumentSource(document, module);
        return new(this.Owner, this.Name, module, document);
    }

    /// <summary>Registers a document in the source table in consumption order, without creating a target.</summary>
    /// <param name="document">The document.</param>
    public void Register(SourceDocument document)
        => this.SourceOf(document);

    /// <summary>Reports a lexical or syntax problem of this target's module; its subject is its span.</summary>
    /// <param name="range">The span; it must be the default value when no source applies.</param>
    /// <param name="code">The code.</param>
    /// <param name="obj">The first message argument.</param>
    /// <param name="obj2">The second message argument.</param>
    /// <param name="sourceDocument">The source the span belongs to; the target's document by default.</param>
    /// <param name="note">A Note formed from the facts.</param>
    /// <param name="advice">Conditional advice formed from the facts.</param>
    /// <returns><see langword="true"/> when the report is an Error.</returns>
    public bool Add(SourceSpan range, DiagnosticCode code, object? obj = null, object? obj2 = null, SourceDocument? sourceDocument = null, string? note = null, string? advice = null)
        => this.AddSyntax(range, code, obj, obj2, sourceDocument, note, advice);

    // SPEC 23.3.6.7: a report that breaks its code's definition is a compiler defect, never a diagnostic of the source.
    internal static DiagnosticEntry Validate(SourceSpan range, DiagnosticCode code, object? first, object? second, SourceDocument? document, DiagnosticKey[]? derivedFrom)
    {
        if (DiagnosticEntries.Anomalies.Count != 0)
        {
            throw new DiagnosticContractException(DiagnosticFault.Catalog, DiagnosticEntries.Anomalies[0]);
        }

        if (DiagnosticRequirements.Anomalies.Count != 0)
        {
            throw new DiagnosticContractException(DiagnosticFault.Catalog, DiagnosticRequirements.Anomalies[0]);
        }

        if (code == DiagnosticCode.Template_Kd || !DiagnosticEntries.TryGet(code, out var entry))
        {
            throw new DiagnosticContractException(DiagnosticFault.UnknownCode, code.ToString());
        }

        var count = second is not null ? 2 : first is not null ? 1 : 0;
        if (count != entry.Arity || (second is not null && first is null))
        {
            throw new DiagnosticContractException(DiagnosticFault.InvalidArgument, $"{entry.Name} takes {entry.Arity} arguments, not {count}.");
        }

        if (count > 0)
        {
            entry.ValidateValue(entry.ArgumentSchema[0], first);
        }

        if (count > 1)
        {
            entry.ValidateValue(entry.ArgumentSchema[1], second);
        }

        // Invariant: a problem is derived, reported as PrerequisiteUnavailable_Kd, exactly when it names prerequisites.
        if ((code == DiagnosticCode.PrerequisiteUnavailable_Kd) != (derivedFrom is { Length: > 0 }))
        {
            throw new DiagnosticContractException(DiagnosticFault.InvalidArgument, $"{entry.Name} is derived exactly when it names prerequisites.");
        }

        if (document is null ? range != default : range.Start < 0 || range.Length < 0 || range.Start > document.SourceText.Length - range.Length)
        {
            throw new DiagnosticContractException(DiagnosticFault.InvalidLocation, $"{entry.Name} at {range} in {document?.Path ?? "no source"}.");
        }

        return entry;
    }

    /// <summary>Records a syntax problem with optional related evidence, preserving the parser's recovery cause.</summary>
    /// <param name="range">The primary span.</param>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="obj">The first message argument.</param>
    /// <param name="obj2">The second message argument.</param>
    /// <param name="sourceDocument">The source; the target's document by default.</param>
    /// <param name="note">A Note formed from the facts.</param>
    /// <param name="advice">Conditional advice formed from the facts.</param>
    /// <param name="related">Locations obtained through <see cref="Relate"/>.</param>
    /// <returns>Whether the report is an Error.</returns>
    internal bool AddSyntax(SourceSpan range, DiagnosticCode code, object? obj = null, object? obj2 = null, SourceDocument? sourceDocument = null, string? note = null, string? advice = null, DiagnosticRelatedFact[]? related = null)
    {
        sourceDocument ??= this.Document;
        var source = this.SourceOf(sourceDocument);
        var length = sourceDocument is null ? -1 : range.Length;

        // A syntax problem is its token, code and expectation: two expectations at one token are two problems.
        var context = obj is null ? null : obj2 is null ? obj.ToString() : string.Concat(obj.ToString(), "\u001f", obj2.ToString());
        var key = new DiagnosticKey(null, source, range.Start, length, DiagnosticRequirement.Syntax, 0, context);
        var isError = this.Report(DiagnosticPartition.Syntax, key, range, code, obj, obj2, note, advice, null, sourceDocument, related: related);
        if (isError)
        {
            this.LastError = key;
        }

        return isError;
    }

    /// <summary>Records that a form of syntax is expected, missing or not permitted at a span (docs/dev/DIAGNOSTICS.md §4.4). The
    /// form is the requirement of the check; its phrase and advice come from the requirement table, so the recorder supplies no
    /// text beyond the token it found. The report becomes <see cref="LastError"/>, which the parser's recovery rests on.</summary>
    /// <param name="range">The found token, the insertion point of the missing form, or the misplaced syntax.</param>
    /// <param name="code"><c>ExpectedSyntax_Kd</c>, <c>MissingSyntax_Kd</c> or <c>MisplacedSyntax_Kd</c>.</param>
    /// <param name="form">The form.</param>
    /// <param name="found">The text of the token found where the form was expected; <see langword="null"/> for the other codes.</param>
    /// <param name="document">The source; the target's document by default.</param>
    /// <param name="related">Locations obtained through <see cref="Relate"/>, such as the opening delimiter of a missing closer.</param>
    /// <returns>The key of the check.</returns>
    internal DiagnosticKey ReportSyntax(SourceSpan range, DiagnosticCode code, SyntaxForm form, string? found, SourceDocument? document = null, DiagnosticRelatedFact[]? related = null)
    {
        document ??= this.Document;
        var requirement = DiagnosticRequirement.SyntaxOf(form);
        var key = new DiagnosticKey(null, this.SourceOf(document), range.Start, document is null ? -1 : range.Length, requirement);
        this.Report(DiagnosticPartition.Syntax, key, range, code, requirement, found, null, DiagnosticRequirements.AdviceOf(requirement), null, document, related: related);
        this.LastError = key;
        return key;
    }

    /// <summary>Records a syntax check whose decision depends on an earlier lexical or syntax failure.</summary>
    /// <param name="range">The check's source boundary.</param>
    /// <param name="condition">The syntax expected at that boundary.</param>
    /// <param name="cause">The failed prerequisite.</param>
    /// <param name="document">The source being recovered.</param>
    internal void AddDependentSyntax(SourceSpan range, string condition, DiagnosticKey cause, SourceDocument document)
    {
        var key = new DiagnosticKey(null, this.SourceOf(document), range.Start, range.Length, DiagnosticRequirement.Syntax, 0, condition);
        this.Report(DiagnosticPartition.Syntax, key, range, DiagnosticCode.PrerequisiteUnavailable_Kd, null, null, null, null, [cause], document);
        this.LastError = key;
    }

    /// <summary>Makes the lexical Error that rejected a token of this target's document the cause of the parser's recovery, without
    /// reporting the token again (SPEC 23.3.6.4).</summary>
    /// <param name="range">The rejected token's span, or the insertion point of a missing form.</param>
    /// <param name="requirement">The form whose Error is recalled; any syntax Error at the range by default.</param>
    /// <returns><see langword="true"/> when such an Error was recorded; it is then <see cref="LastError"/>.</returns>
    internal bool RecallError(SourceSpan range, DiagnosticRequirement? requirement = null)
    {
        if (this.Document is null || this.Owner.SyntaxErrorAt(this.CurrentModule(), this.SourceOf(this.Document), range, requirement) is not { } key)
        {
            return false;
        }

        this.LastError = key;
        return true;
    }

    /// <summary>Gets the key of a check whose subject is a syntax node.</summary>
    /// <param name="node">The subject.</param>
    /// <param name="span">The subject's span.</param>
    /// <param name="document">The node's document.</param>
    /// <param name="requirement">The requirement.</param>
    /// <param name="condition">The condition within the requirement.</param>
    /// <returns>The key.</returns>
    internal DiagnosticKey KeyOf(object node, SourceSpan span, SourceDocument? document, DiagnosticRequirement requirement, ushort condition = 0)
    {
        document ??= this.Document;
        return new(node, this.SourceOf(document), span.Start, document is null ? -1 : span.Length, requirement, condition);
    }

    /// <summary>Reports one problem.</summary>
    /// <param name="partition">The partition of the requirement's phase.</param>
    /// <param name="key">The check key.</param>
    /// <param name="range">The primary span.</param>
    /// <param name="code">The code; <c>PrerequisiteUnavailable_Kd</c> exactly when <paramref name="derivedFrom"/> is not empty.</param>
    /// <param name="first">The first message argument.</param>
    /// <param name="second">The second message argument.</param>
    /// <param name="note">A Note formed from the facts.</param>
    /// <param name="advice">Conditional advice formed from the facts.</param>
    /// <param name="derivedFrom">The unmet prerequisites.</param>
    /// <param name="document">The source the span belongs to; the target's document by default.</param>
    /// <param name="evidence">The code's evidence facts, all of them in catalog order, or <see langword="null"/> for none.</param>
    /// <param name="related">Locations related to the problem, located by <see cref="Relate"/>.</param>
    /// <returns><see langword="true"/> when the report is an Error.</returns>
    internal bool Report(DiagnosticPartition partition, in DiagnosticKey key, SourceSpan range, DiagnosticCode code, object? first, object? second, string? note, string? advice, DiagnosticKey[]? derivedFrom, SourceDocument? document, object?[]? evidence = null, DiagnosticRelatedFact[]? related = null)
    {
        document ??= this.Document;
        var entry = Validate(range, code, first, second, document, derivedFrom);
        if (evidence is not null && (evidence.Length != entry.EvidenceSchema.Length || Array.IndexOf(evidence, null) >= 0))
        {
            throw new DiagnosticContractException(DiagnosticFault.InvalidArgument, $"{entry.Name} takes {entry.EvidenceSchema.Length} evidence facts, all of them or none.");
        }

        if (evidence is not null)
        {
            for (var i = 0; i < evidence.Length; i++)
            {
                entry.ValidateValue(entry.EvidenceSchema[i], evidence[i]);
            }
        }

        var module = this.CurrentModule();
        var source = this.SourceOf(document);
        var isError = entry.Severity == DiagnosticSeverity.Error;
        var length = document is null ? -1 : range.Length;
        var causes = derivedFrom is { Length: > 0 } ? derivedFrom.Distinct().ToArray() : null;
        var capturedRelated = related is null ? null : DiagnosticOwner.OrderRelated(related.Distinct().ToArray());
        this.Owner.Record(partition, module, new(code, key, source, range.Start, length, DiagnosticOwner.Capture(first), DiagnosticOwner.Capture(second), note, advice, causes, DiagnosticOwner.Capture(evidence), capturedRelated), isError);
        return isError;
    }

    /// <summary>Locates a related location in the source table.</summary>
    /// <param name="role">The role, such as <c>candidate</c>.</param>
    /// <param name="span">The span.</param>
    /// <param name="document">The span's source, or <see langword="null"/> for a location outside every source.</param>
    /// <param name="label">A short description of the location.</param>
    /// <returns>The related location.</returns>
    internal DiagnosticRelatedFact Relate(string role, SourceSpan span, SourceDocument? document, string? label)
    {
        if (document is null ? span != default : span.Start < 0 || span.Length < 0 || span.Start > document.SourceText.Length - span.Length)
        {
            throw new DiagnosticContractException(DiagnosticFault.InvalidLocation, $"Related {role} at {span} in {document?.Path ?? "no source"}.");
        }

        if (string.IsNullOrWhiteSpace(role))
        {
            throw new DiagnosticContractException(DiagnosticFault.InvalidArgument, "A related location needs a role.");
        }

        return new(role, this.SourceOf(document), document is null ? 0 : span.Start, document is null ? -1 : span.Length, label);
    }

    private int CurrentModule()
        => this.module >= 0 ? this.module : this.Owner.UnattributedModule();

    private int SourceOf(SourceDocument? document)
        => document is null ? -1 : this.Owner.DocumentSource(document, this.CurrentModule());
}
