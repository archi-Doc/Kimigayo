// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;

namespace Kimigayo;

[TinyhandGenerateHash("strings-en.tinyhand")]
public static partial class Hashed
{
    public static void SetDiagnosticSeverity(Action<ulong, DiagnosticSeverity> setSeverity)
    {
        setSeverity(Hashed.Solution.NoProject, DiagnosticSeverity.Warning);
        setSeverity(Hashed.Kimi.IndentationLevelMismatch, DiagnosticSeverity.Warning);
    }

    public static void Write(this LogWriter writer, ulong hash)
        => writer.Write(HashedString.Get(hash));

    public static void Write(this LogWriter writer, ulong hash, object obj1)
        => writer.Write(HashedString.Get(hash, obj1));

    public static void Write(this LogWriter writer, ulong hash, object obj1, object obj2)
        => writer.Write(HashedString.Get(hash, obj1, obj2));
}
