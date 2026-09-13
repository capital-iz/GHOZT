namespace Ghozt.Core;

/// <summary>
/// A two-sided confidence interval [Lower, Upper] with a point estimate or center.
/// </summary>
public sealed record ConfidenceInterval(double Lower, double Upper, double Center);

/// <summary>
/// Mathematical and information-theoretic foundations for state-space exploration,
/// Bayesian defect reproducibility estimation, graph cycle prevention, and entropy calculations.
/// </summary>
public static class StateSpaceMath
{
    public const double Standard95PercentZ = 1.959963984540054; // Two-sided 95% confidence standard normal score

    /// <summary>
    /// Computes the Bayesian posterior mean for a binomial process under a flat Beta(1,1) uniform prior
    /// (Laplace / additive smoothing): (successes + 1) / (attempts + 2).
    /// </summary>
    /// <remarks>
    /// Eliminates small-sample extreme point estimate bias (e.g. 1/1 claiming 100% certainty, or 0/1 claiming 0%).
    /// As attempts grow, converges asymptotically to the maximum likelihood estimate (successes / attempts).
    /// </remarks>
    public static double LaplaceSmoothedProbability(int successes, int attempts)
    {
        var n = Math.Max(0, attempts);
        var k = Math.Clamp(successes, 0, n);
        return (double)(k + 1) / (n + 2);
    }

    /// <summary>
    /// Calculates the Wilson score confidence interval for a binomial proportion.
    /// </summary>
    /// <param name="successes">Number of successful occurrences (k).</param>
    /// <param name="attempts">Total number of trials (n).</param>
    /// <param name="z">Standard score for desired confidence level (default: 1.95996 for 95%).</param>
    /// <returns>A confidence interval [Lower, Upper] bounded within [0.0, 1.0].</returns>
    public static ConfidenceInterval WilsonScoreInterval(int successes, int attempts, double z = Standard95PercentZ)
    {
        if (attempts <= 0)
        {
            return new ConfidenceInterval(0.0, 1.0, 0.5);
        }

        var n = (double)Math.Max(1, attempts);
        var k = (double)Math.Clamp(successes, 0, attempts);
        var p = k / n;

        var z2 = z * z;
        var denominator = 1.0 + (z2 / n);
        var center = (p + (z2 / (2.0 * n))) / denominator;
        
        var rad = (p * (1.0 - p) / n) + (z2 / (4.0 * n * n));
        var halfWidth = (z * Math.Sqrt(Math.Max(0.0, rad))) / denominator;

        var lower = Math.Clamp(center - halfWidth, 0.0, 1.0);
        var upper = Math.Clamp(center + halfWidth, 0.0, 1.0);

        return new ConfidenceInterval(lower, upper, center);
    }

    /// <summary>
    /// Evaluates whether a defect's reproducibility pattern exhibits non-deterministic "Phantom" behavior
    /// based on Wilson confidence intervals rather than volatile point ratios.
    /// </summary>
    public static bool IsPhantomDefect(int successes, int attempts)
    {
        // Require at least 3 attempts to establish statistical significance
        if (attempts < 3)
        {
            return false;
        }

        var interval = WilsonScoreInterval(successes, attempts);
        // Intermittent defects have non-zero lower bound and non-deterministic upper bound
        return interval.Lower > 0.05 && interval.Upper < 0.90;
    }

    /// <summary>
    /// Computes the Shannon information entropy H(S) across a distribution of visited states:
    /// H(S) = -sum( P(s) * log2( P(s) ) )
    /// </summary>
    /// <remarks>
    /// Guarantees IEEE 754 stability: limits p -> 0 => p * log2(p) = 0, preventing NaN or -Infinity.
    /// </remarks>
    public static double ShannonEntropy(IReadOnlyDictionary<string, int> stateVisits)
    {
        if (stateVisits.Count <= 1)
        {
            return 0.0;
        }

        double totalVisits = 0;
        foreach (var count in stateVisits.Values)
        {
            if (count > 0)
            {
                totalVisits += count;
            }
        }

        if (totalVisits <= 0.0)
        {
            return 0.0;
        }

        var entropy = 0.0;
        foreach (var count in stateVisits.Values)
        {
            if (count <= 0)
            {
                continue;
            }

            var p = count / totalVisits;
            if (p > 0.0)
            {
                entropy -= p * Math.Log2(p);
            }
        }

        return Math.Max(0.0, entropy);
    }

    /// <summary>
    /// Computes the exploration efficiency ratio eta = H(S) / log2(M), where M is the number of discovered states.
    /// Returns 1.0 for perfect uniform exploration, down to near 0.0 for extreme local trap oscillation.
    /// </summary>
    public static double ExplorationEfficiency(IReadOnlyDictionary<string, int> stateVisits)
    {
        var m = stateVisits.Count;
        if (m <= 1)
        {
            return 1.0;
        }

        var h = ShannonEntropy(stateVisits);
        var hMax = Math.Log2(m);

        if (hMax <= 1e-12)
        {
            return 1.0;
        }

        return Math.Clamp(h / hMax, 0.0, 1.0);
    }

    /// <summary>
    /// Detects directed cycles in a graph using 3-color depth-first search.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> FindCycles(
        IEnumerable<LatticeNode> nodes,
        IEnumerable<LatticeEdge> edges)
    {
        var adj = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            adj[node.Id] = [];
        }

        foreach (var edge in edges)
        {
            if (!adj.TryGetValue(edge.FromId, out var neighbors))
            {
                neighbors = [];
                adj[edge.FromId] = neighbors;
            }
            neighbors.Add(edge.ToId);
        }

        // Color states: 0 = White (unvisited), 1 = Gray (in current call stack), 2 = Black (visited)
        var color = new Dictionary<string, int>(StringComparer.Ordinal);
        var path = new List<string>();
        var cycles = new List<IReadOnlyList<string>>();

        foreach (var nodeId in adj.Keys)
        {
            if (!color.ContainsKey(nodeId))
            {
                Dfs(nodeId);
            }
        }

        void Dfs(string u)
        {
            color[u] = 1; // Gray
            path.Add(u);

            if (adj.TryGetValue(u, out var neighbors))
            {
                foreach (var v in neighbors)
                {
                    if (!color.TryGetValue(v, out var vColor) || vColor == 0)
                    {
                        Dfs(v);
                    }
                    else if (vColor == 1)
                    {
                        // Gray neighbor found: cycle detected!
                        var cycleStart = path.IndexOf(v);
                        if (cycleStart >= 0)
                        {
                            var cycle = path.Skip(cycleStart).Append(v).ToList();
                            cycles.Add(cycle);
                        }
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            color[u] = 2; // Black
        }

        return cycles;
    }

    /// <summary>
    /// Projects an arbitrary directed (and potentially cyclic) multigraph into an acyclic spanning tree
    /// rooted at the given start node.
    /// </summary>
    /// <remarks>
    /// Essential for UI hierarchies (e.g. WPF TreeView) to guarantee complete cycle safety and
    /// eliminate the risk of StackOverflowException during recursive rendering.
    /// </remarks>
    public static Dictionary<string, List<LatticeNode>> ProjectAcyclicSpanningTree(
        IEnumerable<LatticeNode> nodes,
        IEnumerable<LatticeEdge> edges,
        string rootId)
    {
        var nodeMap = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var tree = new Dictionary<string, List<LatticeNode>>(StringComparer.Ordinal);

        foreach (var id in nodeMap.Keys)
        {
            tree[id] = [];
        }

        if (!nodeMap.ContainsKey(rootId))
        {
            return tree;
        }

        var outgoing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!outgoing.TryGetValue(edge.FromId, out var list))
            {
                list = [];
                outgoing[edge.FromId] = list;
            }
            list.Add(edge.ToId);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { rootId };
        var queue = new Queue<string>();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var u = queue.Dequeue();

            if (outgoing.TryGetValue(u, out var children))
            {
                foreach (var v in children)
                {
                    if (visited.Add(v))
                    {
                        if (nodeMap.TryGetValue(v, out var childNode))
                        {
                            tree[u].Add(childNode);
                        }
                        queue.Enqueue(v);
                    }
                }
            }
        }

        return tree;
    }
}
