// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Checking;

/// <summary>An immutable effect description from the source snapshot whose check produced it.</summary>
/// <param name="Source">The source identity.</param>
/// <param name="Range">The checked call's range.</param>
/// <param name="Text">The typed description and its contributing premises.</param>
internal sealed record EffectHover(SourceIdentity Source, SourceRange Range, string Text);
