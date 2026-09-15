using System.Globalization;
using System.Text.RegularExpressions;

namespace Ghozt.Core;

/// <summary>The entity kinds Ghozt issues identifiers for, with their canonical prefixes.</summary>
public enum IdKind
{
    Target,
    Build,
    Authority,
    Manifest,
    Sweep,
    Action,
    Event,
    Evidence,
    Deviation,
    Fault,
    Reproducer,
    Counterfactual,
    FixBrief,
    RegressionGuard,
    Clearance,
    Report,
}

/// <summary>
/// A canonical Ghozt identifier: a three-letter kind prefix and a six-digit sequence.
/// </summary>
/// <remarks>
/// <para>
/// Identifiers are immutable. Human-readable names may change, but references, evidence and audit
/// records continue to resolve to the original entity. That is the whole reason this is a type
/// rather than a string: a renamed target must not orphan the evidence that proves something about
/// it, and a string is too easy to rebuild from a display name by accident.
/// </para>
/// <para>
/// The format is fixed width so that identifiers sort lexicographically in the same order they were
/// issued, which is what lets a log grid sort by ID without a second numeric column.
/// </para>
/// </remarks>
public readonly record struct GhoztId
{
    private static readonly Regex Pattern = new("^([A-Z]{3})-([0-9]{6})$", RegexOptions.Compiled);

    private GhoztId(IdKind kind, int sequence)
    {
        Kind = kind;
        Sequence = sequence;
    }

    public IdKind Kind { get; }

    public int Sequence { get; }

    public string Prefix => PrefixFor(Kind);

    public static string PrefixFor(IdKind kind) => kind switch
    {
        IdKind.Target => "TGT",
        IdKind.Build => "BLD",
        IdKind.Authority => "AUT",
        IdKind.Manifest => "MAN",
        IdKind.Sweep => "SWP",
        IdKind.Action => "ACT",
        IdKind.Event => "EVT",
        IdKind.Evidence => "EVD",
        IdKind.Deviation => "DEV",
        IdKind.Fault => "FLT",
        IdKind.Reproducer => "RPR",
        IdKind.Counterfactual => "CFX",
        IdKind.FixBrief => "FXB",
        IdKind.RegressionGuard => "RGR",
        IdKind.Clearance => "CLR",
        IdKind.Report => "RPT",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No prefix is registered for this kind."),
    };

    public static IdKind KindFor(string prefix) => prefix switch
    {
        "TGT" => IdKind.Target,
        "BLD" => IdKind.Build,
        "AUT" => IdKind.Authority,
        "MAN" => IdKind.Manifest,
        "SWP" => IdKind.Sweep,
        "ACT" => IdKind.Action,
        "EVT" => IdKind.Event,
        "EVD" => IdKind.Evidence,
        "DEV" => IdKind.Deviation,
        "FLT" or "FND" => IdKind.Fault,
        "RPR" => IdKind.Reproducer,
        "CFX" => IdKind.Counterfactual,
        "FXB" => IdKind.FixBrief,
        "RGR" => IdKind.RegressionGuard,
        "CLR" => IdKind.Clearance,
        "RPT" => IdKind.Report,
        _ => throw new FormatException($"'{prefix}' is not a Ghozt identifier prefix."),
    };

    public static GhoztId Create(IdKind kind, int sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sequence, 999_999);
        return new GhoztId(kind, sequence);
    }

    public static GhoztId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var match = Pattern.Match(value.Trim().ToUpperInvariant());
        if (!match.Success)
        {
            throw new FormatException($"'{value}' is not a Ghozt identifier. Expected e.g. TGT-000001.");
        }

        return new GhoztId(
            KindFor(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    public static bool TryParse(string? value, out GhoztId id)
    {
        try
        {
            if (value is not null)
            {
                id = Parse(value);
                return true;
            }
        }
        catch (FormatException)
        {
            // A malformed identifier in stored data is a data problem, not a crash: the caller gets
            // false and decides. Callers that cannot proceed without one use Parse.
        }

        id = default;
        return false;
    }

    public override string ToString() => $"{Prefix}-{Sequence:D6}";
}
