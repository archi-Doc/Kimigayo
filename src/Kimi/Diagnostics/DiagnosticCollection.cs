// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;

namespace Kimi.Diagnostics;

public record class DiagnosticCollection
{
    private readonly Kimigayo kimigayo;
    private readonly Diagnostic.GoshujinClass diagnostics = new();

    public string Name { get; init; } = string.Empty;

    private SourceDocument? sourceDocument;
    private int errorCount;
    private long errorVersion;

    /// <summary>Gets a value indicating whether this collection contains errors without allocating a diagnostic snapshot.</summary>
    public bool HasErrors => Volatile.Read(ref this.errorCount) != 0;

    public SourceDocument? SourceDocument => Volatile.Read(ref this.sourceDocument);

    public bool IsGlobal => this.Name == string.Empty || this.Name == Kimigayo.GlobalName;

    // Counts attempted error reports, even when location deduplication hides the
    // message. Clearing displayed diagnostics does not erase source failure history.
    internal long ErrorVersion => Volatile.Read(ref this.errorVersion);

    internal DiagnosticCollection(Kimigayo kimigayo, string name)
    {
        this.kimigayo = kimigayo;
        this.Name = name;
    }

    /// <summary>Adds a diagnostic unless one is already recorded at the same start offset.</summary>
    /// <param name="range">The source span; ignored for placement when no source document applies.</param>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="obj">The first message argument.</param>
    /// <param name="obj2">The second message argument.</param>
    /// <param name="sourceDocument">The source document; defaults to the collection's document.</param>
    /// <param name="hint">An explanation appended to the message.</param>
    /// <param name="location">The path of an input the diagnostic concerns when no source document exists, such as an unreadable file.</param>
    public void Add(SourceSpan range, DiagnosticCode code, object? obj = null, object? obj2 = null, SourceDocument? sourceDocument = null, string? hint = null, string? location = null)
    {
        var entry = Validate(range, code, obj, obj2, sourceDocument ?? this.SourceDocument);

        using (this.diagnostics.LockObject.EnterScope())
        {
            if (entry.Severity == DiagnosticSeverity.Error)
            {
                this.errorVersion++;
            }

            if (this.diagnostics.StartPositionChain.ContainsKey(range.Start))
            {
                return;
            }

            var message = entry.FormatMessage(obj, obj2);
            if (hint is not null)
            {
                message = string.Concat(message, " ", hint);
            }

            var diagnostic = new Diagnostic(range, entry, sourceDocument ?? this.SourceDocument) { Message = message, Location = location };
            diagnostic.Goshujin = this.diagnostics;
            if (entry.Severity == DiagnosticSeverity.Error)
            {
                this.errorCount++;
            }

            if (this.kimigayo.RendersDiagnostics)
            {
                this.kimigayo.ReportDiagnostic(this.Name, diagnostic);
            }
        }
    }

    public bool Remove(int startPosition)
    {
        using (this.diagnostics.LockObject.EnterScope())
        {
            if (this.diagnostics.StartPositionChain.TryGetValue(startPosition, out var diagnostic))
            {
                diagnostic.Goshujin = default;
                if (diagnostic.Entry.Severity == DiagnosticSeverity.Error)
                {
                    this.errorCount--;
                }

                return true;
            }
            else
            {
                return false;
            }
        }
    }

    public bool Remove(SourcePosition startPosition)
    {
        var sourceDocument = this.SourceDocument;
        return sourceDocument is not null && this.Remove(sourceDocument.GetOffset(startPosition));
    }

    public Diagnostic[] GetArray()
    {
        using (this.diagnostics.LockObject.EnterScope())
        {
            return this.diagnostics.ToArray();
        }
    }

    public void ClearDiagnostic()
    {
        using (this.diagnostics.LockObject.EnterScope())
        {
            this.diagnostics.ClearAll();
            this.errorCount = 0;
        }
    }

    internal void SetSourceDocument(SourceDocument sourceDocument)
    {
        // A reference store is atomic; the parse hot path must not pay for the lock.
        Volatile.Write(ref this.sourceDocument, sourceDocument);
    }

    // SPEC 23.3.6.7: a report that breaks its code's definition is a compiler defect, never a diagnostic of the source.
    private static DiagnosticEntry Validate(SourceSpan range, DiagnosticCode code, object? first, object? second, SourceDocument? document)
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
