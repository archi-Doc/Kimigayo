// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi.Checking;

namespace Kimi.Lsp;

/// <summary>The session settings of SPEC 23.4.8, validated once from <c>initializationOptions</c> and fixed for the session.</summary>
internal sealed class LspSettings
{
    /// <summary>The default quiet period in milliseconds.</summary>
    public const int DefaultQuietPeriod = 1000;

    /// <summary>Gets the quiet period in milliseconds.</summary>
    public int QuietPeriod { get; private init; } = DefaultQuietPeriod;

    /// <summary>Gets the selected project files.</summary>
    public SourceIdentity[] SelectedProjects { get; private init; } = [];

    /// <summary>Gets the target of product units, or null.</summary>
    public string? Target { get; private init; }

    /// <summary>Gets a value indicating whether every configured target is checked.</summary>
    public bool AllTargets { get; private init; }

    /// <summary>Gets a value indicating whether every unit uses the <c>Debug</c> setting.</summary>
    public bool Debug { get; private init; }

    /// <summary>Validates the options. Unknown members and invalid values are logged; an invalid value keeps its default.</summary>
    /// <param name="options">The <c>initializationOptions</c> value.</param>
    /// <param name="log">Receives each problem.</param>
    /// <returns>The settings.</returns>
    public static LspSettings Parse(JsonElement? options, Action<string> log)
    {
        if (options is not { ValueKind: JsonValueKind.Object } value)
        {
            if (options is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) })
            {
                log("initializationOptions must be an object; the defaults apply.");
            }

            return new();
        }

        var quietPeriod = DefaultQuietPeriod;
        var selected = new List<SourceIdentity>();
        string? target = null;
        bool allTargets = false, debug = false;
        foreach (var member in value.EnumerateObject())
        {
            switch (member.Name)
            {
                case "checkQuietPeriodMs":
                    if (member.Value.ValueKind == JsonValueKind.Number && member.Value.TryGetInt32(out var period) && period is >= 0 and <= 10000)
                    {
                        quietPeriod = period;
                    }
                    else
                    {
                        log("checkQuietPeriodMs must be an integer from 0 to 10000; the default applies.");
                    }

                    break;

                case "selectedProjects":
                    if (member.Value.ValueKind != JsonValueKind.Array)
                    {
                        log("selectedProjects must be an array; it is ignored.");
                        break;
                    }

                    foreach (var item in member.Value.EnumerateArray())
                    {
                        if (TryProject(item, out var project))
                        {
                            selected.Add(project);
                        }
                        else
                        {
                            log("selectedProjects entries must be absolute .kimiproj paths or file: URIs; " + item.GetRawText() + " is ignored.");
                        }
                    }

                    break;

                case "target":
                    if (member.Value.ValueKind == JsonValueKind.String && member.Value.GetString() is { Length: > 0 } text)
                    {
                        target = text;
                    }
                    else
                    {
                        log("target must be a nonempty string; it is ignored.");
                    }

                    break;

                case "allTargets":
                    allTargets = ReadBool(member, log);
                    break;

                case "debug":
                    debug = ReadBool(member, log);
                    break;

                default:
                    log("Unknown initialization option: " + member.Name);
                    break;
            }
        }

        return new() { QuietPeriod = quietPeriod, SelectedProjects = selected.ToArray(), Target = target, AllTargets = allTargets, Debug = debug };
    }

    private static bool ReadBool(JsonProperty member, Action<string> log)
    {
        if (member.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return member.Value.GetBoolean();
        }

        log(member.Name + " must be a Boolean; the default applies.");
        return false;
    }

    private static bool TryProject(JsonElement item, out SourceIdentity project)
    {
        project = default;
        if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } text)
        {
            return false;
        }

        if (SourceIdentity.TryFromUri(text, out project))
        {
            return project.Value.EndsWith(".kimiproj", StringComparison.OrdinalIgnoreCase);
        }

        if (!Path.IsPathFullyQualified(text) || !text.EndsWith(".kimiproj", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            project = SourceIdentity.FromPath(text);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
