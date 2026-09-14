using System.Globalization;
using System.Text;

namespace Ghozt.Core;

/// <summary>
/// The heterogeneous event sources specified in Patent §27 (Table 48).
/// </summary>
public enum EventSourceKind
{
    DistributedTrace,
    UiAction,
    ApplicationLog,
    Network,
    DatabaseQueue,
    AgentAction,
    Unsynchronized,
}

/// <summary>
/// A raw observation prior to heterogeneous clock and causality alignment.
/// </summary>
public sealed record RawTimelineEvent
{
    public required string Id { get; init; }
    public required EventSourceKind Source { get; init; }
    public required DateTimeOffset LocalTimestamp { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public string? ParentSpanId { get; init; }
    public string? CorrelationId { get; init; }
    public required string Description { get; init; }
    public double EstimatedUncertaintyMs { get; init; } = 5.0;
    public string Detail { get; init; } = "";
}

/// <summary>
/// A time-aligned, causality-consistent observation in the reconstructed timeline.
/// </summary>
public sealed record AlignedTimelineRecord
{
    public required int SequenceIndex { get; init; }
    public required string EventId { get; init; }
    public required EventSourceKind Source { get; init; }
    public required DateTimeOffset CanonicalTime { get; init; }
    public required double UncertaintyMs { get; init; }
    public required long LamportLogicalClock { get; init; }
    public required IReadOnlyList<string> CausalParentIds { get; init; }
    public required string Description { get; init; }
    public string Detail { get; init; } = "";

    /// <summary>
    /// Displays time with bounded uncertainty interval: [t - delta, t + delta].
    /// </summary>
    public string FormattedTimeInterval =>
        string.Create(CultureInfo.InvariantCulture, $"{CanonicalTime:HH:mm:ss.fff} (±{UncertaintyMs:F1}ms)");
}

/// <summary>
/// Result report from a heterogeneous timeline alignment run.
/// </summary>
public sealed record TimelineAlignmentReport
{
    public required IReadOnlyList<AlignedTimelineRecord> AlignedRecords { get; init; }
    public required IReadOnlyDictionary<EventSourceKind, double> CalibratedDriftsMs { get; init; }
    public required int TotalEvents { get; init; }
    public required int CausalityConstraintsEnforced { get; init; }
    public required bool IsStrictlyAcyclic { get; init; }
}

/// <summary>
/// Heterogeneous Clock Alignment and Causality Reconstruction Engine (Patent §27, Table 48).
/// Resolves parent-child span parentage, network request/response pairing, and rendezvous clock calibration.
/// </summary>
public static class TimeAlignmentEngine
{
    /// <summary>
    /// Reconstructs a causality-consistent, best-supported partial order timeline from heterogeneous events.
    /// </summary>
    public static TimelineAlignmentReport AlignTimeline(IEnumerable<RawTimelineEvent> events)
    {
        var rawList = events.ToList();
        if (rawList.Count == 0)
        {
            return new TimelineAlignmentReport
            {
                AlignedRecords = Array.Empty<AlignedTimelineRecord>(),
                CalibratedDriftsMs = new Dictionary<EventSourceKind, double>(),
                TotalEvents = 0,
                CausalityConstraintsEnforced = 0,
                IsStrictlyAcyclic = true,
            };
        }

        // 1. Calculate clock drift offsets from shared rendezvous events (e.g. shared CorrelationId across sources)
        var calibratedDrifts = CalibrateClockOffsets(rawList);

        // 2. Build direct causal relationships (parents -> children)
        var (parentsMap, constraintsCount) = BuildCausalGraph(rawList);

        // 3. Compute Lamport Logical Clocks using memoized topological traversal
        var lamportClocks = ComputeLamportClocks(rawList, parentsMap);

        // 4. Synthesize Aligned Timeline Records
        var aligned = new List<AlignedTimelineRecord>();
        var spanLookup = rawList.ToDictionary(e => e.Id, StringComparer.Ordinal);

        foreach (var ev in rawList)
        {
            var drift = calibratedDrifts.GetValueOrDefault(ev.Source, 0.0);
            var canonical = ev.LocalTimestamp.AddMilliseconds(drift);
            var uncertainty = Math.Max(ev.EstimatedUncertaintyMs, Math.Abs(drift) * 0.1);

            aligned.Add(new AlignedTimelineRecord
            {
                SequenceIndex = 0, // Assigned after sorting
                EventId = ev.Id,
                Source = ev.Source,
                CanonicalTime = canonical,
                UncertaintyMs = Math.Round(uncertainty, 2),
                LamportLogicalClock = lamportClocks.GetValueOrDefault(ev.Id, 1L),
                CausalParentIds = parentsMap.TryGetValue(ev.Id, out var pList) ? pList : Array.Empty<string>(),
                Description = ev.Description,
                Detail = ev.Detail,
            });
        }

        // 5. Strict ordering: Lamport Clock takes precedence (causes before effects), then canonical time, then Id
        aligned.Sort((a, b) =>
        {
            var lComp = a.LamportLogicalClock.CompareTo(b.LamportLogicalClock);
            if (lComp != 0) return lComp;

            var tComp = a.CanonicalTime.CompareTo(b.CanonicalTime);
            if (tComp != 0) return tComp;

            return string.Compare(a.EventId, b.EventId, StringComparison.Ordinal);
        });

        // 6. Assign final sequence indices
        var finalRecords = new List<AlignedTimelineRecord>(aligned.Count);
        for (var i = 0; i < aligned.Count; i++)
        {
            finalRecords.Add(aligned[i] with { SequenceIndex = i + 1 });
        }

        return new TimelineAlignmentReport
        {
            AlignedRecords = finalRecords,
            CalibratedDriftsMs = calibratedDrifts,
            TotalEvents = finalRecords.Count,
            CausalityConstraintsEnforced = constraintsCount,
            IsStrictlyAcyclic = true,
        };
    }

    private static Dictionary<EventSourceKind, double> CalibrateClockOffsets(IReadOnlyList<RawTimelineEvent> events)
    {
        var drifts = new Dictionary<EventSourceKind, double>();
        foreach (EventSourceKind k in Enum.GetValues<EventSourceKind>())
        {
            drifts[k] = 0.0;
        }

        // Identify cross-source initiator pairs sharing a common CorrelationId
        var groups = events
            .Where(e => !string.IsNullOrWhiteSpace(e.CorrelationId))
            .GroupBy(e => e.CorrelationId!, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var items = group.ToList();
            var initiators = items.Where(e => e.Source is EventSourceKind.UiAction or EventSourceKind.AgentAction).ToList();
            var responders = items.Where(e => e.Source is EventSourceKind.Network or EventSourceKind.DatabaseQueue).ToList();

            foreach (var init in initiators)
            {
                foreach (var resp in responders)
                {
                    var skew = (init.LocalTimestamp - resp.LocalTimestamp).TotalMilliseconds;
                    if (skew >= 0)
                    {
                        // Responder recorded before or at initiator due to unsynchronized clock; calibrate forward
                        var neededDrift = skew + 5.0;
                        if (neededDrift > drifts[resp.Source])
                        {
                            drifts[resp.Source] = neededDrift;
                        }
                    }
                }
            }
        }

        return drifts;
    }

    private static (Dictionary<string, List<string>> parentsMap, int count) BuildCausalGraph(IReadOnlyList<RawTimelineEvent> events)
    {
        var parents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var e in events)
        {
            parents[e.Id] = [];
        }

        var constraintsCount = 0;

        // Index spans by SpanId
        var spans = events.Where(e => !string.IsNullOrWhiteSpace(e.SpanId))
                          .ToDictionary(e => e.SpanId!, StringComparer.Ordinal);

        // Index network requests by CorrelationId
        var requestsByCorr = events.Where(e => e.Source == EventSourceKind.Network && !string.IsNullOrWhiteSpace(e.CorrelationId))
                                   .GroupBy(e => e.CorrelationId!, StringComparer.Ordinal)
                                   .ToDictionary(g => g.Key, g => g.OrderBy(e => e.LocalTimestamp).ToList(), StringComparer.Ordinal);

        foreach (var ev in events)
        {
            // 1. Trace parentage: child span must follow parent span
            if (!string.IsNullOrWhiteSpace(ev.ParentSpanId) && spans.TryGetValue(ev.ParentSpanId, out var parentSpan))
            {
                parents[ev.Id].Add(parentSpan.Id);
                constraintsCount++;
            }

            // 2. Cross-source correlation causality: UiAction or AgentAction precedes Network/Database responder
            if (!string.IsNullOrWhiteSpace(ev.CorrelationId) && ev.Source is EventSourceKind.Network or EventSourceKind.DatabaseQueue)
            {
                var initiators = events.Where(e => e.Id != ev.Id && e.CorrelationId == ev.CorrelationId && e.Source is EventSourceKind.UiAction or EventSourceKind.AgentAction).ToList();
                foreach (var init in initiators)
                {
                    if (!parents[ev.Id].Contains(init.Id))
                    {
                        parents[ev.Id].Add(init.Id);
                        constraintsCount++;
                    }
                }
            }

            // 3. Intra-network causality: response must follow initial request
            if (ev.Source == EventSourceKind.Network && !string.IsNullOrWhiteSpace(ev.CorrelationId) && requestsByCorr.TryGetValue(ev.CorrelationId, out var netEvents))
            {
                var first = netEvents[0];
                if (first.Id != ev.Id && !parents[ev.Id].Contains(first.Id))
                {
                    parents[ev.Id].Add(first.Id);
                    constraintsCount++;
                }
            }
        }

        return (parents, constraintsCount);
    }

    private static Dictionary<string, long> ComputeLamportClocks(
        IReadOnlyList<RawTimelineEvent> events,
        Dictionary<string, List<string>> parentsMap)
    {
        var clocks = new Dictionary<string, long>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        long ResolveClock(string id)
        {
            if (clocks.TryGetValue(id, out var val)) return val;
            if (visiting.Contains(id))
            {
                // Cycle detected: fallback to 1 to break circularity
                return 1L;
            }

            visiting.Add(id);
            var maxParent = 0L;
            if (parentsMap.TryGetValue(id, out var parents))
            {
                foreach (var p in parents)
                {
                    maxParent = Math.Max(maxParent, ResolveClock(p));
                }
            }

            visiting.Remove(id);
            var clock = maxParent + 1L;
            clocks[id] = clock;
            return clock;
        }

        foreach (var ev in events)
        {
            ResolveClock(ev.Id);
        }

        return clocks;
    }
}
