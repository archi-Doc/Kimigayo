// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Helper;

/// <summary>
/// Provides methods for validating Kimigayo identifiers.
/// </summary>
public static class IdentifierHelper
{
    /// <summary>
    /// Determines whether the specified text is a valid NFC identifier without format controls.
    /// </summary>
    /// <param name="identifier">The text to validate as an identifier.</param>
    /// <returns><see langword="true"/> if the text is valid; otherwise, <see langword="false"/>.</returns>
    public static bool IsValidIdentifier(ReadOnlySpan<char> identifier)
    {
        if (identifier.IsEmpty)
        {
            return false;
        }

        for (var i = 0; i < identifier.Length; i++)
        {
            var c = identifier[i];
            if (c > 0x7F)
            {
                return UnicodeIdentifierHelper.IsValid(identifier);
            }

            var lower = (uint)(c | 0x20);
            if (!(lower - 'a' <= 'z' - 'a' || c == '_' || (i > 0 && (uint)(c - '0') <= 9)))
            {
                return false;
            }
        }

        return true;
    }
}
