using System.IO;

namespace VpsWatcher.Core.Tests;

/// <summary>
/// Resolves the repository's canonical <c>testdata/</c> fixtures — the SINGLE SOURCE
/// OF TRUTH shared by the agent (Go) and client (C#) tests (CLAUDE.md / schema §6).
/// We read the real repo files (not a copy) by walking up from the test binary
/// until we find <c>testdata/sample.ndjson</c>, so there is no divergent duplicate.
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

    /// <summary>Returns the first non-empty line of the given fixture file.</summary>
    public static string ReadFirstLine(string fixtureFileName)
    {
        var path = Path.Combine(Dir, fixtureFileName);
        foreach (var line in File.ReadLines(path))
        {
            if (line.Trim().Length > 0)
                return line;
        }

        throw new InvalidDataException($"Fixture '{fixtureFileName}' contains no content line.");
    }
}
