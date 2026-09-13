using Ghozt.Core;
using Xunit;

namespace Ghozt.Tests;

public sealed class MathematicalInvariantsTests
{
    [Fact]
    public void LaplacePriorAndPosteriorConvergenceProof()
    {
        // Analytical Proof 1: Uninformative Prior at n=0, k=0
        var prior = StateSpaceMath.LaplaceSmoothedProbability(0, 0);
        Assert.Equal(0.5, prior);

        // Analytical Proof 2: Cautious update on single trial
        // 1 pass out of 1 trial should evaluate to 2/3 (66.7%), NOT 100%
        var singlePass = StateSpaceMath.LaplaceSmoothedProbability(1, 1);
        Assert.Equal(2.0 / 3.0, singlePass, precision: 10);

        // 0 pass out of 1 trial should evaluate to 1/3 (33.3%), NOT 0%
        var singleFail = StateSpaceMath.LaplaceSmoothedProbability(0, 1);
        Assert.Equal(1.0 / 3.0, singleFail, precision: 10);

        // Analytical Proof 3: Large-sample asymptotic convergence to MLE
        var largeSample = StateSpaceMath.LaplaceSmoothedProbability(5000, 10000);
        Assert.Equal(5001.0 / 10002.0, largeSample, precision: 10);
        Assert.True(Math.Abs(largeSample - 0.5) < 0.0001);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 10)]
    [InlineData(5, 10)]
    [InlineData(9, 10)]
    [InlineData(10, 10)]
    [InlineData(50, 100)]
    [InlineData(1, 1000)]
    public void WilsonScoreIntervalSymmetryAndBounds(int k, int n)
    {
        var interval = StateSpaceMath.WilsonScoreInterval(k, n);

        // Invariant 1: Bounds Ordering
        Assert.True(interval.Lower >= 0.0, $"Lower bound {interval.Lower} < 0.0");
        Assert.True(interval.Upper <= 1.0, $"Upper bound {interval.Upper} > 1.0");
        Assert.True(interval.Lower <= interval.Center, "Lower > Center");
        Assert.True(interval.Center <= interval.Upper, "Center > Upper");

        // Invariant 2: Complementary Symmetry
        // Wilson interval for k successes in n trials has: lower(k, n) == 1 - upper(n - k, n)
        var complement = StateSpaceMath.WilsonScoreInterval(n - k, n);
        Assert.Equal(interval.Lower, 1.0 - complement.Upper, precision: 10);
        Assert.Equal(interval.Upper, 1.0 - complement.Lower, precision: 10);
    }

    [Fact]
    public void IsPhantomDefectRequiresStatisticalSignificance()
    {
        // 1 out of 1 trial is not statistically significant (sample size n < 3)
        Assert.False(StateSpaceMath.IsPhantomDefect(1, 1));
        Assert.False(StateSpaceMath.IsPhantomDefect(0, 1));

        // 1 out of 2 trials is still n < 3
        Assert.False(StateSpaceMath.IsPhantomDefect(1, 2));

        // 5 out of 10 trials is genuine intermittency (Phantom)
        Assert.True(StateSpaceMath.IsPhantomDefect(5, 10));

        // 10 out of 10 trials is a deterministic defect (NOT Phantom)
        Assert.False(StateSpaceMath.IsPhantomDefect(10, 10));

        // 0 out of 10 trials never reproduced (NOT Phantom)
        Assert.False(StateSpaceMath.IsPhantomDefect(0, 10));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(64)]
    public void ShannonEntropyAnalyticalProofOnUniformDistribution(int stateCount)
    {
        // Invariant: For a uniform probability distribution over M states, H(S) = log2(M) exactly
        var visits = new Dictionary<string, int>();
        for (var i = 0; i < stateCount; i++)
        {
            visits[$"state_{i}"] = 10; // Uniform frequency
        }

        var h = StateSpaceMath.ShannonEntropy(visits);
        var expected = stateCount == 1 ? 0.0 : Math.Log2(stateCount);

        Assert.Equal(expected, h, precision: 10);

        var efficiency = StateSpaceMath.ExplorationEfficiency(visits);
        Assert.Equal(1.0, efficiency, precision: 10);
    }

    [Fact]
    public void ShannonEntropyAsymptoticZeroLimitAndDegenerateSafeguard()
    {
        // Empty dictionary returns 0.0 without throwing
        Assert.Equal(0.0, StateSpaceMath.ShannonEntropy(new Dictionary<string, int>()));

        // Single state returns 0.0 without throwing
        Assert.Equal(0.0, StateSpaceMath.ShannonEntropy(new Dictionary<string, int> { ["s1"] = 500 }));

        // Extreme degenerate distribution: 1 million visits to state 0, 1 visit to state 1
        var skewed = new Dictionary<string, int>
        {
            ["s0"] = 1_000_000,
            ["s1"] = 1
        };

        var h = StateSpaceMath.ShannonEntropy(skewed);
        Assert.False(double.IsNaN(h));
        Assert.False(double.IsInfinity(h));
        Assert.True(h > 0.0 && h < 0.01, $"Expected near-zero entropy for skewed distribution, got {h}");

        var efficiency = StateSpaceMath.ExplorationEfficiency(skewed);
        Assert.True(efficiency < 0.01, $"Expected near-zero efficiency for skewed distribution, got {efficiency}");
    }

    [Fact]
    public void GraphCycleDetectionIdentifiesCyclicLoops()
    {
        var targetId = GhoztId.Create(IdKind.Target, 1);
        var nodes = new[]
        {
            new LatticeNode { Id = "A", TargetId = targetId, Label = "A", Kind = NodeKind.System, State = "Verified", Provenance = "Observed" },
            new LatticeNode { Id = "B", TargetId = targetId, Label = "B", Kind = NodeKind.Action, State = "Verified", Provenance = "Observed" },
            new LatticeNode { Id = "C", TargetId = targetId, Label = "C", Kind = NodeKind.DataState, State = "Verified", Provenance = "Observed" },
        };

        // Cyclic graph: A -> B -> C -> A
        var cyclicEdges = new[]
        {
            new LatticeEdge { TargetId = targetId, FromId = "A", ToId = "B" },
            new LatticeEdge { TargetId = targetId, FromId = "B", ToId = "C" },
            new LatticeEdge { TargetId = targetId, FromId = "C", ToId = "A" },
        };

        var cycles = StateSpaceMath.FindCycles(nodes, cyclicEdges);
        Assert.NotEmpty(cycles);
        Assert.Contains(cycles, c => c.Contains("A") && c.Contains("B") && c.Contains("C"));

        // Acyclic graph: A -> B -> C
        var acyclicEdges = new[]
        {
            new LatticeEdge { TargetId = targetId, FromId = "A", ToId = "B" },
            new LatticeEdge { TargetId = targetId, FromId = "B", ToId = "C" },
        };

        var noCycles = StateSpaceMath.FindCycles(nodes, acyclicEdges);
        Assert.Empty(noCycles);
    }

    [Fact]
    public void AcyclicSpanningTreeProjectionGuaranteesZeroRecursionStackOverflow()
    {
        var targetId = GhoztId.Create(IdKind.Target, 2);
        var nodeCount = 500;
        var nodes = new List<LatticeNode>();
        var edges = new List<LatticeEdge>();

        for (var i = 0; i < nodeCount; i++)
        {
            nodes.Add(new LatticeNode
            {
                Id = $"node_{i}",
                TargetId = targetId,
                Label = $"Node {i}",
                Kind = i == 0 ? NodeKind.System : NodeKind.DataState,
                State = "Verified",
                Provenance = "Observed"
            });
        }

        // Create heavy cyclic multigraph with forward, backward, and self edges
        for (var i = 0; i < nodeCount - 1; i++)
        {
            edges.Add(new LatticeEdge { TargetId = targetId, FromId = $"node_{i}", ToId = $"node_{i + 1}" });
            // Back edge creating cycles
            if (i > 0 && i % 5 == 0)
            {
                edges.Add(new LatticeEdge { TargetId = targetId, FromId = $"node_{i}", ToId = $"node_{i - 3}" });
            }
        }
        // Giant loop from end to start
        edges.Add(new LatticeEdge { TargetId = targetId, FromId = $"node_{nodeCount - 1}", ToId = "node_0" });

        // Project onto acyclic spanning tree
        var tree = StateSpaceMath.ProjectAcyclicSpanningTree(nodes, edges, "node_0");

        // Verify that recursive DFS traversal terminates without StackOverflowException
        var visitedCount = 0;
        var visitedSet = new HashSet<string>();

        void Recurse(string u)
        {
            visitedCount++;
            Assert.True(visitedSet.Add(u), $"Node {u} visited more than once in projected spanning tree!");

            if (tree.TryGetValue(u, out var children))
            {
                foreach (var child in children)
                {
                    Recurse(child.Id);
                }
            }
        }

        Recurse("node_0");

        // All 500 connected nodes visited exactly once
        Assert.Equal(nodeCount, visitedCount);
        Assert.Equal(nodeCount, visitedSet.Count);
    }

    [Fact]
    public void FingerprintCollisionResistanceFuzzingProof()
    {
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        var count = 10_000;

        for (var i = 0; i < count; i++)
        {
            var fp = Hashing.Fingerprint($"target_{i % 50}", $"action_kind_{i % 10}", $"element_id_{i}", $"value_{i * 3}");

            // Invariant: 32 hex characters = 128-bit collision resistance
            Assert.Equal(32, fp.Length);
            Assert.True(fingerprints.Add(fp), $"Hash collision detected at permutation {i}: {fp}");
        }

        Assert.Equal(count, fingerprints.Count);
    }

    [Fact]
    public void FindingPropertiesConsumeMathematicalEngine()
    {
        var finding = new Finding
        {
            Id = GhoztId.Create(IdKind.Fault, 1),
            TargetId = GhoztId.Create(IdKind.Target, 1),
            Title = "Intermittent NullRef",
            Severity = Severity.Critical,
            ReproAttempts = 10,
            ReproSuccesses = 5,
        };

        // Standard MLE
        Assert.Equal(0.5, finding.Reproducibility);

        // Bayesian Laplace estimate: (5 + 1) / (10 + 2) = 6/12 = 0.5
        Assert.Equal(0.5, finding.BayesianReproducibility);

        // Wilson bounds
        var wilson = finding.WilsonConfidence;
        Assert.True(wilson.Lower > 0.23 && wilson.Lower < 0.25);
        Assert.True(wilson.Upper > 0.75 && wilson.Upper < 0.77);

        // Validated Phantom status
        Assert.True(finding.IsPhantom);
    }
}
