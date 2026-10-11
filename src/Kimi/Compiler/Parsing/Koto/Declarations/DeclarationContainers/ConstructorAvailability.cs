// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

/// <summary>How the merged members of a structure determine its constructors (SPEC 6.2.3.6, 22.1).</summary>
internal enum ConstructorAvailability : byte
{
    /// <summary>The structure declares a constructor, so none is synthesized.</summary>
    Explicit,

    /// <summary>An own Field has no initializer, so no constructor exists.</summary>
    MissingInitializer,

    /// <summary>The declarations admit a synthesized constructor; with a base it also needs the omitted base selection.</summary>
    Eligible,

    /// <summary>A Kimi Type whose representation the compiler manages has only its declared constructors.</summary>
    CompilerManaged,
}
