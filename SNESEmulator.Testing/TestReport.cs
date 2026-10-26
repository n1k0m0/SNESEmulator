// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy at https://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE and NOTICE for permissions and attribution.
//
// Writes versioned laboratory JSON/JUnit reports with exact source identity and explicit non-pass states.
//

using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace SNESEmulator.Testing;

public sealed record TestResult(string Name, string Status, string? Detail, double Seconds, object? Evidence);
public sealed class TestReport(string suite)
{
    public string Suite { get; } = suite;
    public string Schema { get; } = "snes-lab-results-v1";
    public string Commit { get; } = ReadGit("rev-parse HEAD");
    public string WorkingTreeStatus { get; } = ReadGit("status --porcelain");
    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
    public Dictionary<string, object?> Metadata { get; } = new();
    public List<TestResult> Tests { get; } = [];
    public void Add(string name, string status, string? detail = null, double seconds = 0, object? evidence = null)
    {
        if (status is not ("pass" or "fail" or "skipped" or "timeout" or "crash")) throw new ArgumentException("Unknown status");
        Tests.Add(new(name, status, detail, seconds, evidence));
    }
    public int ExitCode => Tests.Any(t => t.Status is "fail" or "timeout" or "crash") ? 1 : Tests.Any(t => t.Status == "skipped") ? 2 : 0;
    public void Save(string prefix)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(prefix))!);
        File.WriteAllText(prefix + ".json", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        var root = new XElement("testsuite", new XAttribute("name", Suite), new XAttribute("tests", Tests.Count),
            new XAttribute("failures", Tests.Count(t=>t.Status=="fail")), new XAttribute("errors", Tests.Count(t=>t.Status is "timeout" or "crash")),
            new XAttribute("skipped", Tests.Count(t=>t.Status=="skipped")), new XAttribute("timestamp", StartedUtc.ToString("O")));
        foreach(var t in Tests)
        {
            var c = new XElement("testcase",new XAttribute("name",t.Name),new XAttribute("classname",Suite),new XAttribute("time",t.Seconds));
            c.Add(new XElement("properties",new XElement("property",new XAttribute("name","status"),new XAttribute("value",t.Status))));
            if(t.Status!="pass") c.Add(new XElement(t.Status=="fail"?"failure":t.Status=="skipped"?"skipped":"error",new XAttribute("type",t.Status),t.Detail??""));
            root.Add(c);
        }
        new XDocument(root).Save(prefix+".junit.xml");
        Console.WriteLine($"{Suite}: "+string.Join(", ", Tests.GroupBy(t=>t.Status).Select(g=>$"{g.Count()} {g.Key}"))+ $"; report {prefix}.json");
    }
    static string ReadGit(string arguments)
    {
        try { using var p=Process.Start(new ProcessStartInfo("git",arguments) { RedirectStandardOutput=true,UseShellExecute=false,CreateNoWindow=true }); return p!.StandardOutput.ReadToEnd().Trim(); }
        catch {return "unavailable";}
    }
}
