// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Kimi.Diagnostics;

namespace Kimi;

public enum KimiDiagnostic
{
    Template_Kd,
    ConditionMustBeBool_Kd,
    ERROR1,

    Count, // Last sentinel
}

public static class DiagnosticEntries
{
    private static DiagnosticEntry[] table = [];

    static DiagnosticEntries()
    {
        LoadAssembly(Assembly.GetExecutingAssembly(), "Misc.Language.diagnostic-entries.tinyhand");
    }

    public static bool TryGet(KimiDiagnostic kimiDiagnostic, [MaybeNullWhen(false)] out DiagnosticEntry entry)
    {
        if (kimiDiagnostic >= KimiDiagnostic.Count)
        {
            entry = default;
            return false;
        }
        else
        {
            entry = table[(int)kimiDiagnostic];
            return entry is not null;
        }
    }

    internal static void LoadAssembly(Assembly assembly, string name)
    {
        try
        {
            using Stream? stream = assembly.GetManifestResourceStream(assembly.GetName().Name + "." + name);
            if (stream == null)
            {
                throw new FileNotFoundException();
            }

            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);

            var entries = TinyhandSerializer.DeserializeFromUtf8<DiagnosticEntry[]>(bytes);
            if (entries is not null)
            {
                table = new DiagnosticEntry[(int)KimiDiagnostic.Count + 1];
                foreach (var e in entries)
                {
                    if (Enum.TryParse<KimiDiagnostic>(e.Name, out var kimiDiagnostic))
                    {
                        table[(int)kimiDiagnostic] = e;
                    }
                }
            }
        }
        catch
        {
        }
    }
}
