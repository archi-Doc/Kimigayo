// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimplePrompt;

namespace Kimigayo;

public class Solution
{
    public IConsoleService ConsoleService { get; set; }

    public Solution()
    {
        this.ConsoleService = SimpleConsole.Instance;
    }

    public bool TryReadFile(string file)
    {
        byte[] utf8;
        try
        {
            utf8 = File.ReadAllBytes(file);
        }
        catch
        {
            this.ConsoleService.WriteLine(Hashed.Solution.NotFound, file);
            return false;
        }

        return true;
    }

    public async Task<bool> Build()
    {
        return true;
    }
}
