using System.IO;
using VpsWatcher.Core;
using VpsWatcher.Core.Schema;

namespace VpsWatcher.App.Tests;

/// <summary>
/// Resolves the repo's canonical <c>testdata/</c> fixtures — the SINGLE SOURCE OF TRUTH
/// shared by the agent (Go) and client (C#) tests (CLAUDE.md / schema §6). We parse the real
/// repo fixtures (not a copy) so the ViewModel tests exercise the same bytes as the parser tests.
/// </summary>
internal static class TestData
{
    private static readonly string Dir = Locate();

    private static string Locate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "testdata", "sample.ndjson");
            if (File.Exists(candidate))
                return Path.Combine(dir.FullName, "testdata");
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repo 'testdata' directory (single source of truth) by walking up from " +
            AppContext.BaseDirectory);
    }

    /// <summary>Parses the first non-empty line of the named fixture into a <see cref="Sample"/>.</summary>
    public static Sample Sample(string fixtureFileName)
    {
        var path = Path.Combine(Dir, fixtureFileName);
        foreach (var line in File.ReadLines(path))
        {
            if (line.Trim().Length > 0)
                return NdjsonParser.Parse(line);
        }

        throw new InvalidDataException($"Fixture '{fixtureFileName}' contains no content line.");
    }
}
