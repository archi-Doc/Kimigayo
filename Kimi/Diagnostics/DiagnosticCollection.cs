// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;

namespace Kimi.Diagnostics;

public record class DiagnosticCollection
{
    private readonly Kimigayo kimigayo;
    private readonly Diagnostic.GoshujinClass diagnostics = new();
    private static readonly DiagnosticEntry NotRegistered = new("NotRegistered_Kd", DiagnosticSeverity.Error, "Diagnostic not registered");

    public string Name { get; init; } = string.Empty;

    public SourceDocument? SourceDocument { get; private set; }

    public bool IsGlobal => this.Name == string.Empty || this.Name == Kimigayo.GlobalName;

    internal DiagnosticCollection(Kimigayo kimigayo, string name)
    {
        this.kimigayo = kimigayo;
        this.Name = name;
    }

    public void Add(TextSpan range, KimiDiagnostic kimiDiagnostic, object? obj = null, object? obj2 = null)
    {
        if (!DiagnosticEntries.TryGet(kimiDiagnostic, out var entry))
        {
            entry = NotRegistered;
        }

        using (this.diagnostics.LockObject.EnterScope())
        {
            if (this.diagnostics.StartPositionChain.ContainsKey(range.Start))
            {
                return;
            }

            var message = entry.Message;
            if (obj is not null)
            {
                if (obj2 is not null)
                {
                    message = string.Format(message, obj, obj2);
                }
                else
                {
                    message = string.Format(message, obj);
                }
            }

            var diagnostic = new Diagnostic(range, entry, this.SourceDocument) { Message = message };
            diagnostic.Goshujin = this.diagnostics;

            this.kimigayo.ReportDiagnostic(this.Name, diagnostic);
        }
    }

    public bool Remove(int startPosition)
    {
        using (this.diagnostics.LockObject.EnterScope())
        {
            if (this.diagnostics.StartPositionChain.TryGetValue(startPosition, out var diagnostic))
            {
                diagnostic.Goshujin = default;
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
        }
    }

    internal void SetSourceDocument(SourceDocument sourceDocument)
    {
        using (this.diagnostics.LockObject.EnterScope())
        {
            this.SourceDocument = sourceDocument;
        }
    }
}
