// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;

namespace Kimi;

// Source-only approval of an exceptional statement or nesting bound; the analyzer validates every use.
[Conditional("KIMI_CODE_RULES_ATTRIBUTES")]
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event,
    AllowMultiple = true)]
internal sealed class CodeRuleExceptionAttribute(string rule) : Attribute
{
    public string Rule { get; } = rule;

    public int Limit { get; set; }

    public string Reason { get; set; } = string.Empty;
}
