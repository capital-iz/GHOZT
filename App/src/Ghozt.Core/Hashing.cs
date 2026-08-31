using System.Security.Cryptography;
using System.Text;

namespace Ghozt.Core;

/// <summary>
/// Content hashing for the evidence plane.
/// </summary>
/// <remarks>
/// An evidence seal is meaningless without a hash: it is what turns "this artefact was captured" into
/// "this artefact has not changed since it was captured". Kept in one place so that every artefact is
/// hashed the same way and a digest recorded in a report can always be recomputed.
/// </remarks>
public static class Hashing
{
    public static string Sha256(string content) => Sha256(Encoding.UTF8.GetBytes(content));

    public static string Sha256(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>
    /// Hashes a file that another process may be holding open.
    /// </summary>
    /// <remarks>
    /// <see cref="File.OpenRead"/> requests <c>FileShare.Read</c>, which fails against a file some
    /// other process has open for writing - and a system under examination almost always has its own
    /// files open. Ghozt hit this against its own SQLite store: the Integrity check threw, and the
    /// whole sweep died on it.
    /// <para>
    /// Sharing read, write and delete means the content can change underneath the read. That is
    /// acceptable and honest: the hash records what the bytes were at this moment, which is exactly
    /// what an evidence timestamp claims. Refusing to hash a live file would mean refusing to
    /// examine any running system.
    /// </para>
    /// </remarks>
    public static string Sha256File(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// A stable signature used to cluster equivalent failures rather than duplicate them.
    /// </summary>
    /// <remarks>
    /// Built from the features that stay the same across occurrences of one fault - where it happened,
    /// what was expected, what class of thing went wrong - and deliberately not from the timestamp,
    /// the run identifier or any value that changes every time. A fingerprint that included those
    /// would make every occurrence a new fault and the backlog would fill with the same defect.
    /// </remarks>
    public static string Fingerprint(params string?[] features)
    {
        var normalised = features
            .Select(f => (f ?? "").Trim().ToLowerInvariant())
            .Where(f => f.Length > 0);

        // A unit separator between features, so two different splits of the same characters
        // ("ab"+"c" and "a"+"bc") cannot collide into one fingerprint.
        return Sha256(string.Join('\u001F', normalised))[..16];
    }
}
