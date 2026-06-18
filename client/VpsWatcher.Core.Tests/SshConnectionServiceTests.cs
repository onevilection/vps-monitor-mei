using System;
using System.IO;
using VpsWatcher.Core.Configuration;
using VpsWatcher.Core.Ssh;

namespace VpsWatcher.Core.Tests;

/// <summary>
/// Network-free tests for <see cref="SshConnectionService"/> construction (design §5.4 / §9.1).
/// Real connection behaviour is exercised manually via VpsWatcher.ConsoleTest; here we only
/// assert the ctor's key-loading contract.
/// </summary>
public class SshConnectionServiceTests
{
    // A genuine ssh-keygen fingerprint (see HostKeyVerificationTests) so Validate()/the verifier
    // ctor pass and we reach the key-loading step.
    private const string ValidKnownHostKey =
        "SHA256:D5cKV/20wphMD2QliSjc7EQgc1fkk5FRHFgq0XUD5GQ";

    private static ServerConfig ConfigWithKeyPath(string keyPath) => new()
    {
        Id = "vps-example-1",
        Host = "203.0.113.10",
        Port = 49222,
        User = "metrics",
        KeyPath = keyPath,
        KnownHostKey = ValidKnownHostKey,
    };

    [Fact]
    public void KeyPath_environment_variables_are_expanded_before_loading_the_key()
    {
        // A keyPath written the documented way (design §9.1 / README use %USERPROFILE%\.ssh\...)
        // must be expanded before SSH.NET opens it; otherwise the literal "%VAR%\..." path never
        // resolves and a user who copied the example can never connect.
        //
        // We define our own env var (deterministic, cross-platform: .NET's ExpandEnvironmentVariables
        // honours %NAME% on every OS) pointing at a guaranteed-missing directory, so the ctor throws
        // while loading the key. The failure must reference the EXPANDED path (proving expansion
        // happened) and never the literal "%VAR%" token.
        var varName = "VPSWATCHER_TEST_KEYDIR_" + Guid.NewGuid().ToString("N");
        var missingDir = Path.Combine(
            Path.GetTempPath(), "vpswatcher_no_such_dir_" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(varName, missingDir);
        try
        {
            var token = $"%{varName}%/id_ed25519";
            var expanded = Environment.ExpandEnvironmentVariables(token);
            Assert.NotEqual(token, expanded); // sanity: the token really does expand

            var ex = Record.Exception(() => new SshConnectionService(ConfigWithKeyPath(token)));

            Assert.NotNull(ex);
            var detail = ex!.ToString();
            Assert.Contains(missingDir, detail);     // loaded the expanded path...
            Assert.DoesNotContain(varName, detail);  // ...not the literal %VAR% token
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }
}
