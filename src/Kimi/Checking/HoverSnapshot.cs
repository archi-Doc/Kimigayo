// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Checking;

/// <summary>Optional immutable editor facts detached from one completed check; never a diagnostic or a compiler graph.</summary>
/// <param name="effects">Established Callable contracts and their complete effect evidence.</param>
internal sealed class HoverSnapshot(EffectHover[] effects)
{
    /// <summary>Gets the effect descriptions owned by this snapshot.</summary>
    internal EffectHover[] Effects { get; } = effects;
}
