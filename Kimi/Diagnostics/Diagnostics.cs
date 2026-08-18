// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;

namespace Kimi.Diagnostics;

public record class Diagnostics
{
    private readonly Kimigayo kimigayo;
    private readonly Diagnostic.GoshujinClass diagnostics = new();

    public string Name { get; init; } = string.Empty;

    public bool IsGlobal => this.Name == string.Empty || this.Name == Kimigayo.GlobalName;

    internal Diagnostics(Kimigayo kimigayo, string name)
    {
        this.kimigayo = kimigayo;
        this.Name = name;
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
