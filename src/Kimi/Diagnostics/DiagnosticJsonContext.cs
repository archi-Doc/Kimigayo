// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json.Serialization;
using Kimi.Checking;

namespace Kimi.Diagnostics;

/// <summary>
/// The JSON form of finalized diagnostics (SPEC 23.3.6.8): every field and a self-contained message. <c>kimi check --Format json</c>
/// writes it as the <see cref="CheckDocument"/> of the public schema <c>docs/spec/schemas/check-output.schema.json</c>; the
/// diagnostic snapshot keeps the result form as evidence.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UseStringEnumConverter = true)]
[JsonSerializable(typeof(DiagnosticResult))]
[JsonSerializable(typeof(CheckOutput))]
[JsonSerializable(typeof(CheckDocument))]
internal sealed partial class DiagnosticJsonContext : JsonSerializerContext
{
}
