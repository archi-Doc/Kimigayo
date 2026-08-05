// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;

namespace Kimi.Diagnostics;

public record class Diagnostics
{
    private readonly KimiControl kimiControl;
    private readonly Diagnostic.GoshujinClass diagnostics = new();

    public string Name { get; init; } = string.Empty;

    public bool IsGlobal => this.Name == string.Empty || this.Name == KimiControl.GlobalName;

    internal Diagnostics(KimiControl kimiControl, string name)
    {
        this.kimiControl = kimiControl;
        this.Name = name;
    }

    public void AddToken(Token token, ulong diagnosticHash, object? obj = null, object? obj2 = null)
    {
        using (this.diagnostics.LockObject.EnterScope())
        {
            if (this.diagnostics.StartPositionChain.ContainsKey(token.Range.Start))
            {
                return;
            }

            DiagnosticCode.GetSeverity(diagnosticHash, out var code, out var severity);

            string message;
            if (obj is null)
            {
                message = HashedString.Get(diagnosticHash);
            }
            else if (obj2 is null)
            {
                message = HashedString.Get(diagnosticHash, obj);
            }
            else
            {
                message = HashedString.Get(diagnosticHash, obj, obj2);
            }

            var diagnostic = new Diagnostic(token.Range, severity, message);
            diagnostic.Goshujin = this.diagnostics;

            this.kimiControl.ReportDiagnostic(this.Name, diagnostic);
        }
    }

    public void Add(SourceRange range, ulong diagnosticHash, object? obj = null)
    {
        using (this.diagnostics.LockObject.EnterScope())
        {
            if (this.diagnostics.StartPositionChain.ContainsKey(range.Start))
            {
                return;
            }

            DiagnosticCode.GetSeverity(diagnosticHash, out var code, out var severity);

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

            this.kimiControl.ReportDiagnostic(this.Name, diagnostic);
        }
    }

    public bool Remove(SourcePosition startPosition)
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
