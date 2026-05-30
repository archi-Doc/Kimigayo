// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;

namespace Kimigayo;

[TinyhandGenerateHash("strings-en.tinyhand")]
public static partial class Hashed
{
    public static void SetDiagnosticSeverity(Action<ulong, DiagnosticSeverity> setSeverity)
    {
        // setSeverity(Hashed.Project.NotFound, DiagnosticSeverity.Error);
    }
}
