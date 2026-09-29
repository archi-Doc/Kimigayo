// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json.Serialization;
using Kimi.Checking;

namespace Kimi.Diagnostics;

/// <summary>
/// The JSON form of finalized diagnostics (SPEC 23.3.6.8): every field and a self-contained message, prepared for the CSP.
/// No command emits it and it carries no public schema; the diagnostic snapshot keeps it as evidence.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UseStringEnumConverter = true)]
[JsonSerializable(typeof(DiagnosticResult))]
[JsonSerializable(typeof(CheckOutput))]
internal sealed partial class DiagnosticJsonContext : JsonSerializerContext
{
}
