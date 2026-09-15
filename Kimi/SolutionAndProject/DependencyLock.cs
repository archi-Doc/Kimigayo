// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;

namespace Kimi;

/// <summary>Writes deterministic, path-free resolution state and validates required partitions.</summary>
internal static class DependencyLock
{
    internal static string PathForProject(string projectPath) => Path.ChangeExtension(projectPath, "kimi.lock.json");

    internal static byte[] Serialize(DependencyResolution resolution)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new() { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            WritePartition(writer, "product", resolution.Product);
            WritePartition(writer, "test", resolution.Test);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    internal static bool Update(string path, DependencyResolution resolution, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prior = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var next = Serialize(resolution);
        using var guard = new FileStream(path + ".writing", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        var current = File.Exists(path) ? File.ReadAllBytes(path) : null;
        if ((prior is null) != (current is null) || (prior is not null && !prior.AsSpan().SequenceEqual(current)))
        {
            throw new IOException("The dependency lock changed during restore; retry from a new configuration snapshot.");
        }

        if (current is not null && current.AsSpan().SequenceEqual(next))
        {
            return false;
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(next);
                stream.Flush(true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
            return true;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    internal static string? Validate(string path, DependencyResolution resolution, bool includeTests)
    {
        if (!resolution.Product.IsResolved || (includeTests && !resolution.Test.IsResolved))
        {
            return resolution.Product.Diagnostic ?? resolution.Test.Diagnostic ?? "Required dependency resolution is incomplete.";
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || HasDuplicateProperties(root) || !root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version != 1)
            {
                return "Invalid dependency lock schema; run restore.";
            }

            return Matches(root, "product", resolution.Product) && (!includeTests || Matches(root, "test", resolution.Test)) ? null : "Dependency lock is stale or unresolved; run restore.";
        }
        catch (JsonException)
        {
            return "Dependency lock contains invalid JSON; run restore.";
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Empty(resolution.Product) && (!includeTests || Empty(resolution.Test)) ? null : "Dependency lock is missing; run restore.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Cannot read dependency lock: {ex.Message}";
        }
    }

    private static bool Empty(DependencyPartition partition)
        => partition.Nodes.Length == 1 && partition.Nodes[0].Key == "root" && partition.Nodes[0].Edges.Count == 0;

    private static void WritePartition(Utf8JsonWriter writer, string name, DependencyPartition partition)
    {
        writer.WriteStartObject(name);
        writer.WriteString("status", partition.IsResolved ? "resolved" : "unresolved");
        writer.WriteString("reasonCode", partition.ReasonCode);
        writer.WriteStartArray("nodes");
        var nodes = partition.Nodes.ToArray();
        Array.Sort(nodes, static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        foreach (var node in nodes)
        {
            writer.WriteStartObject();
            writer.WriteString("nodeId", node.Key);
            writer.WriteString("packageId", node.Input.Configuration.PackageId);
            writer.WriteString("packageVersion", node.Input.Configuration.PackageVersion);
            writer.WriteString("inputKind", "project");
            writer.WriteNull("sourceId");
            writer.WriteStartObject("dependencies");
            foreach (var (reference, target) in node.Edges)
            {
                writer.WriteString(reference, target);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static bool Matches(JsonElement root, string name, DependencyPartition partition)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object || HasDuplicateProperties(value) ||
            !value.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String || status.GetString() != "resolved" ||
            !value.TryGetProperty("reasonCode", out var reason) || reason.ValueKind != JsonValueKind.Null ||
            !value.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() != partition.Nodes.Length)
        {
            return false;
        }

        var expected = new Dictionary<string, DependencyNode>(partition.Nodes.Length, StringComparer.Ordinal);
        foreach (var node in partition.Nodes)
        {
            expected.Add(node.Key, node);
        }

        foreach (var item in nodes.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || HasDuplicateProperties(item) ||
                !item.TryGetProperty("nodeId", out var id) || id.ValueKind != JsonValueKind.String || !expected.Remove(id.GetString()!, out var node) ||
                !StringValue(item, "packageId", node.Input.Configuration.PackageId) || !StringValue(item, "packageVersion", node.Input.Configuration.PackageVersion) ||
                !StringValue(item, "inputKind", "project") || !StringValue(item, "sourceId", null) ||
                !item.TryGetProperty("dependencies", out var edges) || edges.ValueKind != JsonValueKind.Object || HasDuplicateProperties(edges))
            {
                return false;
            }

            var count = 0;
            foreach (var edge in edges.EnumerateObject())
            {
                count++;
                if (edge.Value.ValueKind != JsonValueKind.String || !node.Edges.TryGetValue(edge.Name, out var target) || edge.Value.GetString() != target)
                {
                    return false;
                }
            }

            if (count != node.Edges.Count)
            {
                return false;
            }
        }

        return expected.Count == 0;
    }

    private static bool StringValue(JsonElement item, string name, string? expected)
        => item.TryGetProperty(name, out var value) && (expected is null ? value.ValueKind == JsonValueKind.Null : value.ValueKind == JsonValueKind.String && value.GetString() == expected);

    private static bool HasDuplicateProperties(JsonElement item)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in item.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                return true;
            }
        }

        return false;
    }
}
