// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

public class Solution
{
    private readonly KimiControl kimiControl;

    public SolutionOptions Options { get; private set; } = new();

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
            // this.kimiControl.WriteLine(Hashed.Solution.NotFound, file);
            return false;
        }

        return true;
    }

    public async Task<bool> Build()
    {
        return true;
    }

    public void Load(ILogger logger, SolutionOptions options, string[] args)
    {
        var projectList = new List<string>();
        this.Options = options;

        if (args.Length == 0)
        {// If not specified, the current directory is used.
            args = [Directory.GetCurrentDirectory(),];
        }

        // Tries to load solution file
        foreach (var x in args)
        {
            if (x.EndsWith(Constants.KimiSolutionExtension, StringComparison.InvariantCultureIgnoreCase))
            {// *.kimisln
                if (this.TryReadFile(x))
                {
                    this.kimiControl.GlobalDiagnostic.Add(default, Hashed.Solution.Loaded, x);
                    goto SolutionLoaed;
                }
            }
        }

        // Tries to load solution file in directory
        foreach (var x in args)
        {
            if (Directory.Exists(x))
            {
                foreach (var y in Directory.EnumerateFiles(x, Constants.KimiSolutionExtension, SearchOption.TopDirectoryOnly))
                {
                    if (this.TryReadFile(y))
                    {
                        this.kimiControl.GlobalDiagnostic.Add(default, Hashed.Solution.Loaded, x);
                        goto SolutionLoaed;
                    }
                }

                // Load project file in directory
                foreach (var y in Directory.EnumerateFiles(x, Constants.KimiProjectExtension, SearchOption.TopDirectoryOnly))
                {
                    projectList.Add(y);
                }
            }
        }

        // Load project file
        foreach (var x in args)
        {
            if (x.EndsWith(Constants.KimiProjectExtension, StringComparison.InvariantCultureIgnoreCase))
            {// *.kimiproj
                projectList.Add(x);
            }
        }

SolutionLoaed:

        return;
    }
}
