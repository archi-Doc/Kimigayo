// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;

namespace Kimi;

// Source-only removal obligation; the analyzer validates the stage and publishes it in the shared audit inventory.
[Conditional("KIMI_CODE_RULES_ATTRIBUTES")]
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Delegate |
    AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event)]
internal sealed class TransitionalAttribute(string stage) : Attribute
{
    public string Stage { get; } = stage;

    public string Reason { get; set; } = string.Empty;
}
