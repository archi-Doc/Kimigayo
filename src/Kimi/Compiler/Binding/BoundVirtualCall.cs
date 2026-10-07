// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Only virtual calls allocate this retained record. Rebinding and generic destination reuse update it in place.
internal sealed class BoundVirtualCall
{
    internal Binding.VirtualSlot Slot { get; set; }

    internal BoundType? BaseLookupType { get; set; }

    internal FunctionKoto? Implementation { get; set; }

    internal BoundType? ImplementingType { get; set; }

    internal bool IsDirect { get; set; }
}
