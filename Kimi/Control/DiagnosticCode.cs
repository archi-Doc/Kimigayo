// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Reflection;

namespace Kimigayo.Diagnostics;

public static class DiagnosticCode
{
    private readonly struct CodeAndSeverity
    {
        public readonly string Code;
        public readonly DiagnosticSeverity Severity;

        public CodeAndSeverity(string code, DiagnosticSeverity severity)
        {
            this.Code = code;
            this.Severity = severity;
        }
    }

    private static readonly Dictionary<ulong, string> HashToCode = new();
    private static readonly ConcurrentDictionary<ulong, CodeAndSeverity> HashToCodeAndSeverity = new();

    static DiagnosticCode()
    {
        AddCode(typeof(Hashed), default);

        Add(Hashed.Project.NotFound, DiagnosticSeverity.Error);
    }

    public static void Add(ulong diagnosticHash, DiagnosticSeverity severity)
    {
        if (!HashToCode.TryGetValue(diagnosticHash, out var code))
        {
            code = "Code";
        }

        HashToCodeAndSeverity[diagnosticHash] = new(code, severity);
    }

    public static bool TryGet(ulong diagnosticHash, out string code, out DiagnosticSeverity severity)
    {
        if (HashToCodeAndSeverity.TryGetValue(diagnosticHash, out var v))
        {
            code = v.Code;
            severity = v.Severity;
            return true;
        }
        else
        {
            code = string.Empty;
            severity = default;
            return false;
        }
    }

    private static void AddCode(Type type, string? prefix)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var property in type.GetProperties(Flags))
        {
            if (property.PropertyType != typeof(ulong))
            {
                continue;
            }

            if (property.GetMethod is null)
            {
                continue;
            }

            var value = (ulong)property.GetValue(null)!;
            var name = Combine(prefix, property.Name);

            HashToCode.Add(value, name);
        }

        // public static readonly ulong Field;
        // public const ulong Field;
        foreach (var field in type.GetFields(Flags))
        {
            if (field.FieldType != typeof(ulong))
            {
                continue;
            }

            var value = (ulong)field.GetValue(null)!;
            var name = Combine(prefix, field.Name);

            HashToCode.Add(value, name);
        }

        // nested public classes
        foreach (var nestedType in type.GetNestedTypes(BindingFlags.Public))
        {
            var nestedPrefix = Combine(prefix, nestedType.Name);
            AddCode(nestedType, nestedPrefix);
        }
    }

    private static string Combine(string? prefix, string name)
    {
        return prefix is null ? name : $"{prefix}.{name}";
    }
}
