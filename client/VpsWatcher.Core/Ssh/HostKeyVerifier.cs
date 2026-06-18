namespace VpsWatcher.Core.Ssh;

/// <summary>Outcome of one host-key check. <see cref="ActualFingerprint"/> is safe to log (not a secret).</summary>
public sealed record HostKeyVerificationResult(bool Trusted, string ActualFingerprint, string ExpectedFingerprint);

/// <summary>
/// Verifies a server-presented host key against a pinned fingerprint (design §5.4.1).
///
/// Fail-closed: constructing a verifier with an empty pin throws, so a server with no
/// <c>knownHostKey</c> can never be connected to (§9.1). <see cref="Verify"/> is pure and
/// stateless — it caches nothing, so it can be (and is) re-run on every (re)connection
/// with no "trusted once, skip later" shortcut.
/// </summary>
public sealed class HostKeyVerifier
{
    private readonly string _expected;

    public HostKeyVerifier(string knownHostKey)
    {
        if (string.IsNullOrWhiteSpace(knownHostKey))
            throw new ArgumentException(
                "knownHostKey must be set (fail-closed): a server with no pinned host key " +
                "must never be trusted (design §5.4.1/§9.1).",
                nameof(knownHostKey));

        _expected = knownHostKey.Trim();
    }

    /// <summary>
    /// Computes the presented key's fingerprint and compares it (ordinal) to the pin.
    /// Returns the result; the caller decides trust (sets <c>CanTrust</c>). No state is kept.
    /// </summary>
    public HostKeyVerificationResult Verify(byte[] hostKeyBytes)
    {
        string actual = HostKeyFingerprint.Sha256(hostKeyBytes);
        bool trusted = string.Equals(actual, _expected, StringComparison.Ordinal);
        return new HostKeyVerificationResult(trusted, actual, _expected);
    }
}
