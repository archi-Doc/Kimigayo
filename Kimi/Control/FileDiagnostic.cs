// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Diagnostics;

public record class FileDiagnostic
{
    private readonly KimiControl kimiControl;
    private readonly Diagnostic.GoshujinClass diagnostics = new();

    public string Url { get; init; } = string.Empty;

    public bool IsGlobal => this.Url == string.Empty || this.Url == KimiControl.GlobalName;

    internal FileDiagnostic(KimiControl kimiControl, string url)
    {
        this.kimiControl = kimiControl;
        this.Url = url;
    }

    public void Add(Range range, ulong diagnosticHash, object? obj = null)
    {
        using (this.diagnostics.LockObject.EnterScope())
        {
            if (this.diagnostics.StartPositionChain.ContainsKey(range.Start))
            {
                return;
            }

            if (DiagnosticCode.TryGet(diagnosticHash, out var code, out var severity))
            {
                string message;
                if (obj is null)
                {
                    message = HashedString.Get(diagnosticHash);
                }
                else
                {
                    message = HashedString.Get(diagnosticHash, obj);
                }

                var diagnostic = new Diagnostic(range, severity, message);
                diagnostic.Goshujin = this.diagnostics;

                this.kimiControl.ReportDiagnostic(this.Url, diagnostic);
            }
        }
    }

    public bool Remove(Position startPosition)
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
}
