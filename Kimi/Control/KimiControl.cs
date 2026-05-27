// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Lsp;

namespace Kimigayo;

public class KimiControl
{
    private readonly IConsoleService consoleService;
    private readonly Diagnostic.GoshujinClass diagnostics = new();

    public KimiControl(IConsoleService consoleService)
    {
        this.consoleService = consoleService;
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

    public Task<InputResult> ReadLine(CancellationToken cancellationToken = default)
        => this.consoleService.ReadLine(cancellationToken);

    public void WriteLine(string? message = null, ConsoleColor color = (ConsoleColor)(-1))
        => this.consoleService.WriteLine(message, color);

    public void WriteLine(ReadOnlySpan<char> message, ConsoleColor color = (ConsoleColor)(-1))
        => this.consoleService.WriteLine(message, color);

    public void WriteLine(ulong hash)
        => this.WriteLine(HashedString.Get(hash));

    public void WriteLine(ulong hash, object obj1)
        => this.WriteLine(HashedString.Get(hash, obj1));

    public void WriteLine(ulong hash, object obj1, object obj2)
        => this.WriteLine(HashedString.Get(hash, obj1, obj2));

    public void WriteLine(ulong hash, ConsoleColor color)
        => this.WriteLine(HashedString.Get(hash), color);

    public void WriteLine(ulong hash, object obj1, ConsoleColor color)
        => this.WriteLine(HashedString.Get(hash, obj1), color);

    public void WriteLine(ulong hash, object obj1, object obj2, ConsoleColor color)
        => this.WriteLine(HashedString.Get(hash, obj1, obj2), color);
}
