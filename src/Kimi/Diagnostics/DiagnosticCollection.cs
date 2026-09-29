// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;

namespace Kimi.Diagnostics;

/// <summary>
/// A recording target of a <see cref="DiagnosticOwner"/>: facts reported through it belong to its module's syntax
/// partition unless a phase names its own partition. Its name is the former collection name, which only the
/// transitional start-offset filter of D2a uses (docs/dev/DIAGNOSTICS.md §8).
/// </summary>
public sealed class DiagnosticCollection
{
    private readonly int module;

    internal DiagnosticCollection(DiagnosticOwner owner, string name, int unit, int module, SourceDocument? document = null)
    {
        this.Owner = owner;
        this.Name = name;
        this.Unit = unit;
        this.module = module;
        this.Document = document;
    }

    /// <summary>Gets the document a report without an explicit document belongs to, fixed when the target is created.</summary>
    public SourceDocument? Document { get; }

    /// <summary>Gets the owner.</summary>
    public DiagnosticOwner Owner { get; }

    /// <summary>Gets the name, which also names sources parsed from text through this target.</summary>
    public string Name { get; }

    internal int Unit { get; }

    /// <summary>Gets a target bound to one document, registering the document in the source table in consumption order.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The bound target.</returns>
    public DiagnosticCollection For(SourceDocument document)
    {
        if (ReferenceEquals(document, this.Document))
        {
            return this;
        }

        var module = this.module >= 0 ? this.module : this.Owner.UnattributedModule();
        this.Owner.DocumentSource(document, module);
        return new(this.Owner, this.Name, this.Unit, module, document);
    }

    /// <summary>Reports a lexical or syntax problem of this target's module.</summary>
    /// <param name="range">The span; it must be the default value when no source applies.</param>
    /// <param name="code">The code.</param>
    /// <param name="obj">The first message argument.</param>
    /// <param name="obj2">The second message argument.</param>
    /// <param name="sourceDocument">The source the span belongs to.</param>
    /// <param name="hint">Text appended to the message; removed in D2b.</param>
    /// <returns><see langword="true"/> when the report is an Error.</returns>
    public bool Add(SourceSpan range, DiagnosticCode code, object? obj = null, object? obj2 = null, SourceDocument? sourceDocument = null, string? hint = null)
        => this.Add(DiagnosticPartition.Syntax, range, code, obj, obj2, sourceDocument, hint);

    /// <summary>Reports a problem of one phase.</summary>
    /// <param name="partition">The phase's partition.</param>
    /// <param name="range">The span; it must be the default value when no source applies.</param>
    /// <param name="code">The code.</param>
    /// <param name="obj">The first message argument.</param>
    /// <param name="obj2">The second message argument.</param>
    /// <param name="sourceDocument">The source the span belongs to.</param>
    /// <param name="hint">Text appended to the message; removed in D2b.</param>
    /// <returns><see langword="true"/> when the report is an Error.</returns>
    public bool Add(DiagnosticPartition partition, SourceSpan range, DiagnosticCode code, object? obj = null, object? obj2 = null, SourceDocument? sourceDocument = null, string? hint = null)
    {
        sourceDocument ??= this.Document;
        var entry = Validate(range, code, obj, obj2, sourceDocument);
        var module = this.module >= 0 ? this.module : this.Owner.UnattributedModule();
        var source = sourceDocument is null ? -1 : this.Owner.DocumentSource(sourceDocument, module);
        var isError = entry.Severity == DiagnosticSeverity.Error;
        var length = sourceDocument is null ? -1 : range.Length;
        this.Owner.Record(partition, module, new(code, source, range.Start, length, DiagnosticOwner.Capture(obj), DiagnosticOwner.Capture(obj2), hint, this.Unit), isError);
        return isError;
    }

    // SPEC 23.3.6.7: a report that breaks its code's definition is a compiler defect, never a diagnostic of the source.
    internal static DiagnosticEntry Validate(SourceSpan range, DiagnosticCode code, object? first, object? second, SourceDocument? document)
    {
        if (DiagnosticEntries.Anomalies.Count != 0)
        {
            throw new DiagnosticContractException(DiagnosticFault.Catalog, DiagnosticEntries.Anomalies[0]);
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

        if (document is null ? range != default : range.Start < 0 || range.Length < 0 || range.Start > document.SourceText.Length - range.Length)
        {
            throw new DiagnosticContractException(DiagnosticFault.InvalidLocation, $"{entry.Name} at {range} in {document?.Path ?? "no source"}.");
        }

        return entry;
    }
}
