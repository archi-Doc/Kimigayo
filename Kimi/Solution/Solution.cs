// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

public class Solution
{
    private readonly KimiControl kimiControl;

    public Solution(KimiControl kimiControl)
    {
        this.kimiControl = kimiControl;
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
            this.kimiControl.WriteLine(Hashed.Solution.NotFound, file);
            return false;
        }

        return true;
    }

    public async Task<bool> Build()
    {
        return true;
    }
}
