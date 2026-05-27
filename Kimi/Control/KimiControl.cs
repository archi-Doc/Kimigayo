// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

public class KimiControl
{
    private readonly IConsoleService consoleService;

    public KimiControl(IConsoleService consoleService)
    {
        this.consoleService = consoleService;
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
