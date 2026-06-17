using System;
using VpsWatcher.Core.Configuration;
using VpsWatcher.Core.Ssh;

namespace VpsWatcher.Core.Tests;

/// <summary>
/// Unit tests for host-key verification (design §5.4.1 / §9.1). These run without a
/// network: real SSH connection behaviour is exercised manually via VpsWatcher.ConsoleTest.
///
/// The fingerprint test vector below is a genuine `ssh-keygen` output, so these tests
/// assert parity with `ssh-keygen -lf ssh_host_ed25519_key.pub` (the value an operator
/// reads off the VPS serial console to fill in servers.json `knownHostKey`).
/// </summary>
public class HostKeyVerificationTests
{
    // ssh-keygen -t ed25519 -> public key blob (base64 of the SSH wire-format key bytes).
    private const string KeyBlobBase64 =
        "AAAAC3NzaC1lZDI1NTE5AAAAIOFgYyxI5e3WOpGrP7hRUQErcl/02hkn+w5NcvgzezaU";

    // ssh-keygen -lf <pub>  =>  256 SHA256:D5cKV/20wphMD2QliSjc7EQgc1fkk5FRHFgq0XUD5GQ ...
    private const string ExpectedFingerprint =
        "SHA256:D5cKV/20wphMD2QliSjc7EQgc1fkk5FRHFgq0XUD5GQ";

    private static byte[] KeyBytes() => Convert.FromBase64String(KeyBlobBase64);

    [Fact]
    public void Sha256_fingerprint_matches_ssh_keygen_output()
    {
        var actual = HostKeyFingerprint.Sha256(KeyBytes());

        // Must equal `ssh-keygen -lf` exactly (SHA256:base64-no-padding), so the operator
        // can copy the displayed value straight into knownHostKey.
        Assert.Equal(ExpectedFingerprint, actual);
    }

    [Fact]
    public void Verify_trusts_matching_host_key()
    {
        var verifier = new HostKeyVerifier(ExpectedFingerprint);

        var result = verifier.Verify(KeyBytes());

        Assert.True(result.Trusted);
        Assert.Equal(ExpectedFingerprint, result.ActualFingerprint);
    }

    [Fact]
    public void Verify_rejects_mismatching_host_key()
    {
        // An attacker's server presents a different host key -> fingerprint differs -> reject.
        var pinnedToSomethingElse =
            new HostKeyVerifier("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

        var result = pinnedToSomethingElse.Verify(KeyBytes());

        Assert.False(result.Trusted);
        // The actual (computed) fingerprint is still reported for the warning/log.
        Assert.Equal(ExpectedFingerprint, result.ActualFingerprint);
    }

    [Fact]
    public void Verifier_fails_closed_when_pinned_key_is_empty()
    {
        // §9.1: a server with no knownHostKey must never be trusted — the verifier
        // refuses to even be constructed, so no connection can proceed.
        Assert.Throws<ArgumentException>(() => new HostKeyVerifier(""));
        Assert.Throws<ArgumentException>(() => new HostKeyVerifier("   "));
        Assert.Throws<ArgumentException>(() => new HostKeyVerifier(null!));
    }

    [Fact]
    public void ServerConfig_validate_rejects_missing_known_host_key()
    {
        // Fail-closed at config validation (before any connection attempt, §5.4.1/§9.1).
        var config = new ServerConfig
        {
            Id = "vps-example-1",
            Host = "203.0.113.10",
            Port = 49222,
            User = "metrics",
            KeyPath = @"C:\keys\watcher_ed25519",
            KnownHostKey = "", // missing
        };

        var ex = Assert.Throws<ArgumentException>(() => config.Validate());
        Assert.Contains("knownHostKey", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServerConfig_validate_passes_for_fully_specified_server()
    {
        var config = new ServerConfig
        {
            Id = "vps-example-1",
            Host = "203.0.113.10",
            Port = 49222,
            User = "metrics",
            KeyPath = @"C:\keys\watcher_ed25519",
            KnownHostKey = ExpectedFingerprint,
        };

        // Should not throw.
        config.Validate();
    }
}
