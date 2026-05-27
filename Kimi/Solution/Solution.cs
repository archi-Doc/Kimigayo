// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

public class Solution
{
    // public IConsoleService ConsoleService { get; set; }
    private readonly IConsoleService consoleService;

    public Solution(IConsoleService consoleService)
    {
        this.consoleService = consoleService;
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
            this.consoleService.WriteLine(Hashed.Solution.NotFound, file);
            return false;
        }

        return true;
    }

    public async Task<bool> Build()
    {
        return true;
    }
}
