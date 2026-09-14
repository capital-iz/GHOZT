namespace Ghozt.Core;

/// <summary>
/// Result of a workflow sequence minimization run (Patent §28).
/// </summary>
public sealed record MinimizationResult<T>
{
    public required IReadOnlyList<T> OriginalSequence { get; init; }
    public required IReadOnlyList<T> MinimalSequence { get; init; }
    public required IReadOnlyList<T> PrunedSteps { get; init; }
    public int OriginalCount => OriginalSequence.Count;
    public int MinimalCount => MinimalSequence.Count;
    public int PrunedCount => PrunedSteps.Count;
    public double ReductionPercent => OriginalCount == 0 ? 0 : Math.Round((double)PrunedCount / OriginalCount * 100.0, 1);
}

/// <summary>
/// Reproduction sequence minimizer and delta debugger (Patent §28).
/// Reduces long failing workflow action sequences to the minimal causal sequence that reproduces the defect.
/// </summary>
public static class SequenceMinimizer
{
    /// <summary>
    /// Heuristic 1-minimal reduction: prunes redundant observational steps (hovers, passive scrolls, repeated reads)
    /// and collapses consecutive duplicate state transitions while strictly preserving essential state-changing
    /// actions and the terminal failure step.
    /// </summary>
    public static MinimizationResult<T> MinimizeSequence<T>(
        IReadOnlyList<T> steps,
        Func<T, bool> isStateChanging,
        Func<T, bool>? isTerminalFailure = null,
        Func<T, T, bool>? areEquivalentActions = null)
    {
        if (steps.Count <= 1)
        {
            return new MinimizationResult<T>
            {
                OriginalSequence = steps,
                MinimalSequence = steps,
                PrunedSteps = Array.Empty<T>(),
            };
        }

        var minimal = new List<T>();
        var pruned = new List<T>();

        for (var i = 0; i < steps.Count; i++)
        {
            var current = steps[i];
            var isLast = i == steps.Count - 1;
            var isTerminal = isTerminalFailure?.Invoke(current) ?? isLast;

            // 1. Terminal failure step is always preserved
            if (isTerminal || isLast)
            {
                minimal.Add(current);
                continue;
            }

            // 2. Filter out non-state-changing intermediate inspection steps
            if (!isStateChanging(current))
            {
                pruned.Add(current);
                continue;
            }

            // 3. Collapse duplicate consecutive actions
            if (minimal.Count > 0 && areEquivalentActions != null && areEquivalentActions(minimal[^1], current))
            {
                pruned.Add(current);
                continue;
            }

            minimal.Add(current);
        }

        return new MinimizationResult<T>
        {
            OriginalSequence = steps,
            MinimalSequence = minimal,
            PrunedSteps = pruned,
        };
    }

    /// <summary>
    /// Algorithmic Delta-Debugging (ddmin) minimization.
    /// Searches for a 1-minimal sub-sequence using hierarchical partition testing against an oracle function.
    /// </summary>
    public static async Task<MinimizationResult<T>> DeltaDebugAsync<T>(
        IReadOnlyList<T> steps,
        Func<IReadOnlyList<T>, Task<bool>> testFn,
        CancellationToken cancellationToken = default)
    {
        if (steps.Count <= 1)
        {
            return new MinimizationResult<T>
            {
                OriginalSequence = steps,
                MinimalSequence = steps,
                PrunedSteps = Array.Empty<T>(),
            };
        }

        // Verify that initial sequence reproduces the defect
        if (!await testFn(steps).ConfigureAwait(false))
        {
            return new MinimizationResult<T>
            {
                OriginalSequence = steps,
                MinimalSequence = steps,
                PrunedSteps = Array.Empty<T>(),
            };
        }

        var current = new List<T>(steps);
        var n = 2;

        while (current.Count >= 2)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var subsets = Split(current, n);
            var reduced = false;

            // 1. Test chunks
            foreach (var subset in subsets)
            {
                if (subset.Count < current.Count && await testFn(subset).ConfigureAwait(false))
                {
                    current = subset;
                    n = Math.Max(n - 1, 2);
                    reduced = true;
                    break;
                }
            }

            if (reduced) continue;

            // 2. Test complements
            foreach (var subset in subsets)
            {
                var complement = Difference(current, subset);
                if (complement.Count > 0 && complement.Count < current.Count && await testFn(complement).ConfigureAwait(false))
                {
                    current = complement;
                    n = Math.Max(n - 1, 2);
                    reduced = true;
                    break;
                }
            }

            if (reduced) continue;

            // 3. Increase granularity if cannot reduce at current granularity
            if (n < current.Count)
            {
                n = Math.Min(current.Count, n * 2);
            }
            else
            {
                // Reached 1-minimal granularity
                break;
            }
        }

        var pruned = Difference(steps, current);

        return new MinimizationResult<T>
        {
            OriginalSequence = steps,
            MinimalSequence = current,
            PrunedSteps = pruned,
        };
    }

    private static List<List<T>> Split<T>(List<T> items, int n)
    {
        var result = new List<List<T>>();
        var count = items.Count;
        var chunkSize = (int)Math.Ceiling((double)count / n);

        for (var i = 0; i < count; i += chunkSize)
        {
            var take = Math.Min(chunkSize, count - i);
            result.Add(items.GetRange(i, take));
        }

        return result;
    }

    private static List<T> Difference<T>(IReadOnlyList<T> all, IReadOnlyList<T> remove)
    {
        var removeSet = new HashSet<T>(remove);
        var list = new List<T>();
        foreach (var item in all)
        {
            if (!removeSet.Contains(item))
            {
                list.Add(item);
            }
        }
        return list;
    }
}
