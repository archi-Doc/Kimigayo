// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

public static class IConsoleServiceExtension
{
    public static void WriteLine(this IConsoleService service, ulong hash)
        => service.WriteLine(HashedString.Get(hash));

    public static void WriteLine(this IConsoleService service, ulong hash, object obj1)
        => service.WriteLine(HashedString.Get(hash, obj1));

    public static void WriteLine(this IConsoleService service, ulong hash, object obj1, object obj2)
        => service.WriteLine(HashedString.Get(hash, obj1, obj2));

    public static void WriteLine(this IConsoleService service, ulong hash, ConsoleColor color)
        => service.WriteLine(HashedString.Get(hash), color);

    public static void WriteLine(this IConsoleService service, ulong hash, object obj1, ConsoleColor color)
        => service.WriteLine(HashedString.Get(hash, obj1), color);

    public static void WriteLine(this IConsoleService service, ulong hash, object obj1, object obj2, ConsoleColor color)
        => service.WriteLine(HashedString.Get(hash, obj1, obj2), color);
}
