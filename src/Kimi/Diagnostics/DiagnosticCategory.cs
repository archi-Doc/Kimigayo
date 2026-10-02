// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Diagnostics;

/// <summary>The kind of problem a diagnostic code reports (SPEC 23.3.6.1). Each code has exactly one.</summary>
public enum DiagnosticCategory : byte
{
    /// <summary>The source violates a language rule.</summary>
    Language,

    /// <summary>A required fact could not be established, including a requirement left undecided by a failed prerequisite.</summary>
    Proof,

    /// <summary>The form is valid but outside the implemented subset.</summary>
    Unsupported,

    /// <summary>An input, the configuration, dependency content or a lock could not be established or is invalid.</summary>
    Input,

    /// <summary>A finite compiler resource limit was reached.</summary>
    Resource,

    /// <summary>A compiler defect.</summary>
    Internal,
}
