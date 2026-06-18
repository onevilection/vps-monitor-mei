using System.Security.Cryptography;

namespace VpsWatcher.Core.Ssh;

/// <summary>
/// Computes the SSH host-key fingerprint the way OpenSSH does, so the value matches what
/// an operator sees and pins (design §5.4.1).
///
/// The format is <c>SHA256:&lt;base64-no-padding&gt;</c> — identical to:
/// <list type="bullet">
///   <item><c>ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub</c></item>
///   <item>the fingerprint OpenSSH prints on first connection ("authenticity of host ...")</item>
/// </list>
/// The hash is taken over the raw SSH wire-format public-key blob (the bytes SSH.NET hands
/// us in <c>HostKeyEventArgs.HostKey</c>, i.e. the base64 payload of a <c>.pub</c> line).
/// </summary>
public static class HostKeyFingerprint
{
    /// <summary>Returns <c>SHA256:base64(sha256(hostKeyBytes))</c> with padding stripped.</summary>
    public static string Sha256(byte[] hostKeyBytes)
    {
        ArgumentNullException.ThrowIfNull(hostKeyBytes);

        byte[] hash = SHA256.HashData(hostKeyBytes);
        // OpenSSH strips the '=' base64 padding for SHA256 fingerprints.
        string base64 = Convert.ToBase64String(hash).TrimEnd('=');
        return "SHA256:" + base64;
    }
}
