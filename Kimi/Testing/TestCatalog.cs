// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;

namespace Kimi.Testing;

#pragma warning disable SA1402, CS1591 // Internal immutable execution metadata.

internal sealed record TestCase(string TestId, string CaseId, string Name, string File, int Line, int Column, FunctionKoto Function);

internal sealed record TestSite(int Id, string File, int Line, int Column, string Expression, TestVerificationKoto Node);

internal sealed class TestCatalog
{
    internal string ProjectId { get; private set; } = string.Empty;

    internal string ArtifactId { get; private set; } = string.Empty;

    internal List<TestCase> Cases { get; } = new();

    internal List<TestSite> Sites { get; } = new();

    internal string[] ManifestFields { get; private set; } = [];

    internal static string Hash(string prefix, params string[] fields)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> size = stackalloc byte[4];
        foreach (var field in fields)
        {
            var bytes = Encoding.UTF8.GetBytes(field);
            BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)bytes.Length);
            hash.AppendData(size);
            hash.AppendData(bytes);
        }

        return prefix + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal void Discover(Compilation compilation)
    {
        this.Cases.Clear();
        this.Sites.Clear();
        if (!compilation.IsTestBuild || !compilation.Binding.Result.IsComplete || !compilation.Ownership.Result.IsVerified)
        {
            throw new InvalidOperationException("Discovery requires complete test semantic verification.");
        }

        var project = compilation.Project;
        this.ProjectId = (project.ProjectFile.TestProjectId ?? project.ProjectFile.PackageId ??
            (project.FilePath is { } file ? Path.GetFileName(file) : project.Name + ".kimi")).Normalize(NormalizationForm.FormC);
        if (string.IsNullOrWhiteSpace(this.ProjectId) || this.ProjectId.Contains('\0'))
        {
            throw new InvalidDataException("TestProjectId must be nonempty text without NUL.");
        }

        new Collector(this, project).Visit(compilation.Kotonoha.RootKoto);
        this.Cases.Sort(static (a, b) => Compare(a.Name, a.File, a.CaseId, b.Name, b.File, b.CaseId));
        this.Sites.Sort(static (a, b) =>
        {
            var order = string.CompareOrdinal(a.File, b.File);
            if (order == 0)
            {
                order = a.Line.CompareTo(b.Line);
            }

            return order == 0 ? a.Column.CompareTo(b.Column) : order;
        });
        for (var i = 0; i < this.Sites.Count; i++)
        {
            this.Sites[i].Node.SiteId = i;
            this.Sites[i] = this.Sites[i] with { Id = i };
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var test in this.Cases)
        {
            if (!ids.Add(test.CaseId))
            {
                throw new InvalidDataException("Duplicate test identity: " + test.Name);
            }
        }

        var inputs = new List<string>
        {
            "kimi-test-artifact-v1", Compilation.CompilerVersion, compilation.BuildMetadata!.TargetTriple,
            compilation.BuildMetadata.LanguageVersion, this.ProjectId, project.KimiOptions.Debug.ToString(), project.ProjectFile.Optimization,
        };
        var aliases = Compilation.EffectiveAliases(project.ProjectFile.Alias).ToArray();
        Section("aliases", aliases.Length);
        inputs.AddRange(aliases);
        Section("settings", project.ProjectFile.CompileTimeSettings.Count);
        foreach (var setting in project.ProjectFile.CompileTimeSettings.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            inputs.Add(setting.Key);
            inputs.Add(Convert.ToHexStringLower(SHA256.HashData(TinyhandSerializer.SerializeToUtf8(setting.Value))));
        }

        Section("modules", compilation.SourceModules.Count());
        foreach (var module in compilation.SourceModules)
        {
            inputs.Add(module.Name);
            Section("sources", module.SourceDocuments.Count);
            foreach (var source in module.SourceDocuments.OrderBy(x => LogicalPath(project, x.Path), StringComparer.Ordinal))
            {
                inputs.Add(LogicalPath(project, source.Path));
                inputs.Add(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source.SourceText))));
                inputs.Add(source.IsTestOnly.ToString());
            }
        }

        Section("cases", this.Cases.Count);
        foreach (var test in this.Cases)
        {
            inputs.Add(test.TestId);
            inputs.Add(test.CaseId);
            inputs.Add(test.Name);
            inputs.Add(test.File);
            inputs.Add(test.Line.ToString(System.Globalization.CultureInfo.InvariantCulture));
            inputs.Add(test.Column.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Section("sites", this.Sites.Count);
        foreach (var site in this.Sites)
        {
            inputs.Add(site.File);
            inputs.Add(site.Line.ToString(System.Globalization.CultureInfo.InvariantCulture));
            inputs.Add(site.Column.ToString(System.Globalization.CultureInfo.InvariantCulture));
            inputs.Add(site.Expression);
        }

        this.ManifestFields = inputs.ToArray();
        this.ArtifactId = Hash("a1-", this.ManifestFields);

        void Section(string name, int count)
        {
            inputs.Add(name);
            inputs.Add(count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    internal void WriteManifest(string path, string executable)
    {
        using var binary = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
        var digest = Convert.ToHexStringLower(SHA256.HashData(binary));
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new Utf8JsonWriter(output, new() { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("artifactId", this.ArtifactId);
        writer.WriteString("executableSha256", digest);
        writer.WriteStartArray("canonicalFields");
        foreach (var field in this.ManifestFields)
        {
            writer.WriteStringValue(field);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("cases");
        foreach (var test in this.Cases)
        {
            writer.WriteStartObject();
            writer.WriteString("testId", test.TestId);
            writer.WriteString("caseId", test.CaseId);
            writer.WriteString("name", test.Name);
            writer.WriteString("file", test.File);
            writer.WriteNumber("line", test.Line);
            writer.WriteNumber("column", test.Column);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("sites");
        foreach (var site in this.Sites)
        {
            writer.WriteStartObject();
            writer.WriteNumber("siteId", site.Id);
            writer.WriteString("file", site.File);
            writer.WriteNumber("line", site.Line);
            writer.WriteNumber("column", site.Column);
            writer.WriteString("expression", site.Expression);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static int Compare(string a, string b, string c, string x, string y, string z)
    {
        var order = string.CompareOrdinal(a, x);
        if (order == 0)
        {
            order = string.CompareOrdinal(b, y);
        }

        return order == 0 ? string.CompareOrdinal(c, z) : order;
    }

    private static string LogicalPath(Project project, string source)
        => Path.GetRelativePath(Path.GetFullPath(string.IsNullOrEmpty(project.Directory) ? "." : project.Directory), Path.GetFullPath(source)).Replace('\\', '/');

    private sealed class Collector(TestCatalog catalog, Project project) : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            var source = node.CodeContext.SourceDocument;
            if (node is FunctionKoto function && TestDefinition.Marker(function) is not null)
            {
                if (!TestDefinition.IsIncluded(function))
                {
                    return;
                }

                if (!TestDefinition.IsValidSyntax(function) || function.BoundSymbol is not { Type: var type, ReceiverIndex: < 0 } || !ReferenceEquals(type, BoundType.Unit))
                {
                    throw new InvalidDataException("Invalid test signature: " + function.Name);
                }

                var names = new List<string>();
                for (var parent = function.Parent; parent is not null; parent = parent.Parent)
                {
                    if (parent is DeclarationContainerKoto { IsRoot: false } container)
                    {
                        names.Add(container.Name);
                    }
                }

                names.Reverse();
                var containerName = string.Join('.', names);
                var name = containerName.Length == 0 ? function.Name : containerName + "." + function.Name;
                var file = LogicalPath(project, source!.Path);
                var position = source.GetPosition(function.Span.Start);
                var testId = Hash("t1-", "kimi-test-v1", catalog.ProjectId, containerName, function.Name + "()->()", file);
                catalog.Cases.Add(new(testId, Hash("c1-", "kimi-case-v1", testId, "default"), name, file, position.Line + 1, position.Character + 1, function));
            }

            if (node is TestVerificationKoto verification && source is not null)
            {
                var position = source.GetPosition(verification.Span.Start);
                catalog.Sites.Add(new(-1, LogicalPath(project, source.Path), position.Line + 1, position.Character + 1, source.SourceText.Substring(verification.Condition.Span.Start, verification.Condition.Span.Length), verification));
            }

            node.VisitChildren(this);
        }
    }
}
