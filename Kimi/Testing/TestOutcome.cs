// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text.Json;

namespace Kimi.Testing;

internal sealed class TestOutcome(TestCase test, TestCatalog catalog)
{
    internal TestCase Test { get; } = test;

    internal TestCatalog Catalog { get; } = catalog;

    internal bool Selected { get; set; }

    internal bool Started { get; set; }

    internal bool Completed { get; set; }

    internal bool HandshakeCompleted { get; set; }

    internal uint? NativeExitCode { get; set; }

    internal int? TerminationSite { get; set; }

    internal string? TerminationPhase { get; set; }

    internal ulong FailureCount { get; set; }

    internal string Termination { get; set; } = "notStarted";

    internal string? NotStartedReason { get; set; }

    internal List<string> Errors { get; } = new();

    internal List<TestFailure> Failures { get; } = new();

    internal long Microseconds { get; set; }

    internal string? Stdout { get; set; }

    internal string? Stderr { get; set; }

    internal long OmittedLogBytes { get; set; }

    internal int ExitCode => this.Termination == "cancelled" ? 130 : this.Errors.Count != 0 ? 3 : this.Started && (!this.Completed || this.FailureCount != 0 || this.Termination != "normal") ? 1 : 0;

    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("projectId", this.Catalog.ProjectId);
        writer.WriteString("testId", this.Test.TestId);
        writer.WriteString("caseId", this.Test.CaseId);
        writer.WriteString("name", this.Test.Name);
        writer.WriteString("file", this.Test.File);
        writer.WriteNumber("line", this.Test.Line);
        writer.WriteNumber("column", this.Test.Column);
        writer.WriteBoolean("selected", this.Selected);
        writer.WriteString("artifactId", this.Catalog.ArtifactId);
        writer.WriteBoolean("started", this.Started);
        writer.WriteBoolean("completed", this.Completed);
        writer.WriteString("failureCount", this.FailureCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("termination", this.Termination);
        writer.WriteString("notStartedReason", this.NotStartedReason);
        if (this.NativeExitCode is { } nativeExit)
        {
            writer.WriteNumber("nativeExitCode", nativeExit);
        }

        if (this.TerminationSite is { } terminationSite)
        {
            writer.WriteNumber("terminationSiteId", terminationSite);
        }

        writer.WriteString("terminationPhase", this.TerminationPhase);
        writer.WriteBoolean("failureCountIsLowerBound", this.Started && !this.Completed && this.Termination is not ("abort" or "requireAbort"));
        writer.WriteString("durationMicroseconds", this.Microseconds.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("stdout", this.Stdout);
        writer.WriteString("stderr", this.Stderr);
        writer.WriteString("omittedLogBytes", this.OmittedLogBytes.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("omittedFailureCount", (this.FailureCount - (ulong)this.Failures.Count).ToString(CultureInfo.InvariantCulture));
        writer.WriteStartArray("managementErrors");
        foreach (var error in this.Errors)
        {
            writer.WriteStringValue(error);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("failures");
        foreach (var failure in this.Failures)
        {
            writer.WriteStartObject();
            writer.WriteString("issueId", failure.Issue.ToString(CultureInfo.InvariantCulture));
            writer.WriteNumber("siteId", failure.Site.Id);
            writer.WriteString("phase", failure.Phase);
            writer.WriteString("file", failure.Site.File);
            writer.WriteNumber("line", failure.Site.Line);
            writer.WriteNumber("column", failure.Site.Column);
            writer.WriteString("expression", failure.Site.Expression);
            writer.WriteString("message", failure.Message);
            writer.WriteBoolean("messageOmittedOrTruncated", failure.MessageTruncated);
            writer.WriteBoolean("valuesOmitted", failure.Values.Count == 0);
            writer.WriteBoolean("condition", false);
            writer.WriteStartArray("values");
            foreach (var value in failure.Values)
            {
                writer.WriteStartObject();
                writer.WriteString("role", value.Side == 0 ? "left" : "right");
                writer.WriteString("type", value.Kind switch { 1 => "bool", 2 => "signed", 3 => "unsigned", _ => "float" });
                writer.WriteNumber("width", value.Width);
                writer.WriteString("bits", value.Bits);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

internal sealed class TestFailure(ulong issue, TestSite site)
{
    internal ulong Issue { get; } = issue;

    internal TestSite Site { get; } = site;

    internal string? Message { get; set; }

    internal string Phase { get; init; } = "body";

    internal bool MessageTruncated { get; set; }

    internal List<(int Side, int Kind, int Width, string Bits)> Values { get; } = new(2);
}
