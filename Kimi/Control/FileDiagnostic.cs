// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Diagnostics;

public record class FileDiagnostic
{
    public string Url { get; init; } = string.Empty;

    private readonly Diagnostic.GoshujinClass diagnostics = new();

    public FileDiagnostic(string url)
    {
        this.Url = url;
    }

    public void AddDiagnostic(Diagnostic diagnostic)
    {
        using (this.diagnostics.LockObject.EnterScope())
        {
            if (this.diagnostics.StartPositionChain.ContainsKey(diagnostic.StartPosition))
            {
                return;
            }

            diagnostic.Goshujin = this.diagnostics;
        }
    }

    public bool RemoveDiagnostic(Position startPosition)
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

    public Diagnostic[] GetDiagnostics()
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
