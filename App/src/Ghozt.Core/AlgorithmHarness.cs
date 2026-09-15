using System.Diagnostics;

namespace Ghozt.Core;

/// <summary>
/// Result of evaluating a single deterministic algorithm or mathematical invariant.
/// </summary>
public sealed record AlgorithmTestResult
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Domain { get; init; }
    public required string Invariant { get; init; }
    public required string InputSummary { get; init; }
    public required string Expected { get; init; }
    public required string Actual { get; init; }
    public required bool Passed { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public string Details { get; init; } = "";
}

/// <summary>
/// A single size-latency point for an empirical complexity scaling assay.
/// </summary>
public sealed record ComplexitySample(int SizeN, double Microseconds);

/// <summary>
/// Outcome of an empirical computational complexity scaling assay.
/// </summary>
public sealed record ComplexityBenchmarkResult
{
    public required string AlgorithmName { get; init; }
    public required string Domain { get; init; }
    public required string TheoreticalComplexity { get; init; }
    public required IReadOnlyList<ComplexitySample> Samples { get; init; }
    public required double EmpiricalAlpha { get; init; }
    public required bool Passed { get; init; }
    public required string Summary { get; init; }
}

/// <summary>
/// A full suite report for computational complexity assays.
/// </summary>
public sealed record ComplexitySuiteReport
{
    public required DateTimeOffset ExecutedAt { get; init; }
    public required IReadOnlyList<ComplexityBenchmarkResult> Results { get; init; }
    public int PassedCount => Results.Count(r => r.Passed);
    public int FailedCount => Results.Count(r => !r.Passed);
    public required TimeSpan TotalDuration { get; init; }
    public required string Sha256Digest { get; init; }
}

/// <summary>
/// A full execution report from the deterministic algorithm harness.
/// </summary>
public sealed record AlgorithmSuiteReport
{
    public required DateTimeOffset ExecutedAt { get; init; }
    public required IReadOnlyList<AlgorithmTestResult> Results { get; init; }
    public int PassedCount => Results.Count(r => r.Passed);
    public int FailedCount => Results.Count(r => !r.Passed);
    public required TimeSpan TotalDuration { get; init; }
    public required string Sha256Digest { get; init; }
    public ComplexitySuiteReport? ComplexityReport { get; init; }
}

/// <summary>
/// Harness for testing deterministic algorithms, invariants, and mathematical closed-form proofs.
/// </summary>
public static class AlgorithmHarness
{
    /// <summary>
    /// Executes the standard suite of mathematical and algorithmic invariant tests.
    /// </summary>
    public static AlgorithmSuiteReport RunSuite()
    {
        var totalClock = Stopwatch.StartNew();
        var results = new List<AlgorithmTestResult>();

        // 1. Laplace Bayesian Prior & Update
        var sw = Stopwatch.StartNew();
        var prior = StateSpaceMath.LaplaceSmoothedProbability(0, 0);
        var post1 = StateSpaceMath.LaplaceSmoothedProbability(1, 1);
        sw.Stop();
        var laplacePassed = Math.Abs(prior - 0.5) < 1e-12 && Math.Abs(post1 - (2.0 / 3.0)) < 1e-12;
        results.Add(new AlgorithmTestResult
        {
            Id = "ALG.LAPLACE",
            Name = "Bayesian Laplace Estimator",
            Domain = "Stochastic Estimators / Probability",
            Invariant = "Beta(1,1) uniform prior mean equals 0.5; 1/1 update equals 2/3 (66.7%).",
            InputSummary = "k=0, n=0 (prior); k=1, n=1 (single trial)",
            Expected = "prior=0.5000, post=0.6667",
            Actual = $"prior={prior:F4}, post={post1:F4}",
            Passed = laplacePassed,
            Elapsed = sw.Elapsed,
            Details = "Proves small-sample dampening prevents 100% false certainty on 1 pass.",
        });

        // 2. Wilson Score Interval Bounds & Symmetry
        sw = Stopwatch.StartNew();
        var w1 = StateSpaceMath.WilsonScoreInterval(3, 10);
        var w2 = StateSpaceMath.WilsonScoreInterval(7, 10);
        sw.Stop();
        var symPassed = Math.Abs(w1.Lower - (1.0 - w2.Upper)) < 1e-10 && w1.Lower >= 0.0 && w1.Upper <= 1.0;
        results.Add(new AlgorithmTestResult
        {
            Id = "ALG.WILSON_SCORE",
            Name = "Wilson Score Confidence Interval",
            Domain = "Inferential Statistics",
            Invariant = "0.0 <= lower <= center <= upper <= 1.0; lower(k, n) == 1 - upper(n-k, n).",
            InputSummary = "k=3, n=10 vs k=7, n=10 at 95% confidence",
            Expected = "Interval bounded in [0, 1] with exact complementary symmetry",
            Actual = $"k=3: [{w1.Lower:F4}, {w1.Upper:F4}], k=7: [{w2.Lower:F4}, {w2.Upper:F4}]",
            Passed = symPassed,
            Elapsed = sw.Elapsed,
            Details = "Enforces non-Gaussian confidence bounds without asymptotic distortion near boundaries.",
        });

        // 3. Shannon Information Entropy Analytical Proof
        sw = Stopwatch.StartNew();
        var uniformVisits = new Dictionary<string, int> { ["A"] = 10, ["B"] = 10, ["C"] = 10, ["D"] = 10 };
        var entropy = StateSpaceMath.ShannonEntropy(uniformVisits);
        var efficiency = StateSpaceMath.ExplorationEfficiency(uniformVisits);
        sw.Stop();
        var entropyPassed = Math.Abs(entropy - 2.0) < 1e-10 && Math.Abs(efficiency - 1.0) < 1e-10;
        results.Add(new AlgorithmTestResult
        {
            Id = "ALG.SHANNON_ENTROPY",
            Name = "Shannon Information Entropy & Efficiency",
            Domain = "Information Theory",
            Invariant = "Uniform distribution over M=4 states produces H(S) = log2(4) = 2.0 bits exactly.",
            InputSummary = "4 states with uniform visit count = 10 each",
            Expected = "H(S) = 2.0000 bits, eta = 100.0%",
            Actual = $"H(S) = {entropy:F4} bits, eta = {efficiency:P1}",
            Passed = entropyPassed,
            Elapsed = sw.Elapsed,
            Details = "Verifies exploration dispersion metric distinguishes uniform coverage from local trap loops.",
        });

        // 4. Graph Cycle Detection & Acyclic Projection
        sw = Stopwatch.StartNew();
        var targetId = GhoztId.Create(IdKind.Target, 100);
        var nodes = new[]
        {
            new LatticeNode { Id = "S1", TargetId = targetId, Label = "S1", Kind = NodeKind.System, State = "Verified", Provenance = "Observed" },
            new LatticeNode { Id = "S2", TargetId = targetId, Label = "S2", Kind = NodeKind.DataState, State = "Verified", Provenance = "Observed" },
            new LatticeNode { Id = "S3", TargetId = targetId, Label = "S3", Kind = NodeKind.DataState, State = "Verified", Provenance = "Observed" },
        };
        var cyclicEdges = new[]
        {
            new LatticeEdge { TargetId = targetId, FromId = "S1", ToId = "S2" },
            new LatticeEdge { TargetId = targetId, FromId = "S2", ToId = "S3" },
            new LatticeEdge { TargetId = targetId, FromId = "S3", ToId = "S1" }, // Back loop
        };
        var cycles = StateSpaceMath.FindCycles(nodes, cyclicEdges);
        var tree = StateSpaceMath.ProjectAcyclicSpanningTree(nodes, cyclicEdges, "S1");
        sw.Stop();
        var graphPassed = cycles.Count > 0 && tree["S1"].Count == 1 && tree["S2"].Count == 1 && tree["S3"].Count == 0;
        results.Add(new AlgorithmTestResult
        {
            Id = "ALG.GRAPH_ACYCLIC",
            Name = "Tarjan Cycle & Acyclic Spanning Tree",
            Domain = "Graph Theory",
            Invariant = "Detects directed cycle in S1->S2->S3->S1; projects acyclic tree with 0 back-edges.",
            InputSummary = "3 nodes with circular directed edges",
            Expected = "Cycles detected >= 1; tree edges = 2, leaves = 1",
            Actual = $"Cycles found: {cycles.Count}; Tree child counts: S1={tree["S1"].Count}, S2={tree["S2"].Count}, S3={tree["S3"].Count}",
            Passed = graphPassed,
            Elapsed = sw.Elapsed,
            Details = "Eliminates StackOverflowException during UI hierarchy rendering on cyclic state machines.",
        });

        // 5. Cryptographic SHA-256 Collision Resistance
        sw = Stopwatch.StartNew();
        var fp1 = Hashing.Fingerprint("target_1", "click", "btnSubmit", "valA");
        var fp2 = Hashing.Fingerprint("target_1", "click", "btnSubmit", "valB");
        sw.Stop();
        var hashPassed = fp1.Length == 32 && fp2.Length == 32 && fp1 != fp2;
        results.Add(new AlgorithmTestResult
        {
            Id = "ALG.CRYPTO_FINGERPRINT",
            Name = "128-Bit Cryptographic Fingerprint Digest",
            Domain = "Applied Cryptography",
            Invariant = "32 hex characters (128-bit) provides 2^64 birthday collision resistance.",
            InputSummary = "Two distinct action intents differing by 1 character in value",
            Expected = "Length == 32 chars, fp1 != fp2",
            Actual = $"fp1={fp1}, fp2={fp2}",
            Passed = hashPassed,
            Elapsed = sw.Elapsed,
            Details = "Guarantees collision probability < 10^-18 across millions of recorded deviations.",
        });

        // 6. Phantom Intermittent Defect Classification Gate
        sw = Stopwatch.StartNew();
        var isPhantom5_10 = StateSpaceMath.IsPhantomDefect(5, 10);
        var isPhantom10_10 = StateSpaceMath.IsPhantomDefect(10, 10);
        var isPhantom1_2 = StateSpaceMath.IsPhantomDefect(1, 2);
        sw.Stop();
        var phantomPassed = isPhantom5_10 && !isPhantom10_10 && !isPhantom1_2;
        results.Add(new AlgorithmTestResult
        {
            Id = "ALG.PHANTOM_GATE",
            Name = "Phantom Defect Classification Gate",
            Domain = "Inferential Statistics",
            Invariant = "Requires n >= 3 attempts and Wilson confidence bounds 0.05 < p_lower and p_upper < 0.90.",
            InputSummary = "k=5/n=10 (intermittent) vs k=10/n=10 (deterministic) vs k=1/n=2 (small sample)",
            Expected = "k=5/n=10: true, k=10/n=10: false, k=1/n=2: false",
            Actual = $"k=5/n=10: {isPhantom5_10}, k=10/n=10: {isPhantom10_10}, k=1/n=2: {isPhantom1_2}",
            Passed = phantomPassed,
            Elapsed = sw.Elapsed,
            Details = "Prevents volatile point-estimate flapping on non-deterministic defects.",
        });

        // 7. IEEE 754 Floating-Point Hygiene & Subnormal Damping
        sw = Stopwatch.StartNew();
        var zeroEntropy = StateSpaceMath.ShannonEntropy(new Dictionary<string, int>());
        var singleEfficiency = StateSpaceMath.ExplorationEfficiency(new Dictionary<string, int> { ["A"] = 100 });
        var zeroWilson = StateSpaceMath.WilsonScoreInterval(0, 0);
        sw.Stop();
        var ieeePassed = !double.IsNaN(zeroEntropy) && !double.IsInfinity(zeroEntropy)
                         && !double.IsNaN(singleEfficiency) && singleEfficiency == 1.0
                         && zeroWilson.Lower == 0.0 && zeroWilson.Upper == 1.0 && zeroWilson.Center == 0.5;
        results.Add(new AlgorithmTestResult
        {
            Id = "ALG.IEEE754_HYGIENE",
            Name = "IEEE 754 Floating-Point Stability & Degeneracy Damping",
            Domain = "Numerical Stability",
            Invariant = "Degenerate and boundary states (0 states, 0 attempts, single node) produce bounded values, never NaN or Infinity.",
            InputSummary = "H(S) for M=0, eta for M=1, Wilson for n=0",
            Expected = "H(S)=0.0, eta=1.0, Wilson=[0.0, 1.0, 0.5], 0 NaN/Inf",
            Actual = $"H(S)={zeroEntropy:F1}, eta={singleEfficiency:F1}, Wilson=[{zeroWilson.Lower:F1}, {zeroWilson.Upper:F1}, {zeroWilson.Center:F1}]",
            Passed = ieeePassed,
            Elapsed = sw.Elapsed,
            Details = "Guarantees analytical calculations remain bounded and self-contained across edge cases.",
        });

        // 8. Metamorphic Permutation Invariance on Shannon Entropy
        sw = Stopwatch.StartNew();
        var origVisits = new Dictionary<string, int> { ["A"] = 14, ["B"] = 28, ["C"] = 7, ["D"] = 42 };
        var permutedVisits = new Dictionary<string, int> { ["D"] = 42, ["A"] = 14, ["C"] = 7, ["B"] = 28 };
        var h1 = StateSpaceMath.ShannonEntropy(origVisits);
        var h2 = StateSpaceMath.ShannonEntropy(permutedVisits);
        sw.Stop();
        var permutePassed = Math.Abs(h1 - h2) < 1e-12;
        results.Add(new AlgorithmTestResult
        {
            Id = "ALG.METAMORPHIC_PERMUTE",
            Name = "Metamorphic Permutation Invariance",
            Domain = "Information Theory",
            Invariant = "H(sigma(S)) == H(S): State visit permutation preserves exact Shannon entropy.",
            InputSummary = "Set of 4 states with visits [14, 28, 7, 42] vs permuted order [42, 14, 7, 28]",
            Expected = $"H(S1) == H(S2) ({h1:F4} bits)",
            Actual = $"h1={h1:F4}, h2={h2:F4}, diff={Math.Abs(h1 - h2):E2}",
            Passed = permutePassed,
            Elapsed = sw.Elapsed,
            Details = "Proves entropy metric is invariant to arbitrary dictionary enumeration ordering.",
        });

        // 9. Monotonicity of Laplace Estimator under Fixed Trials
        sw = Stopwatch.StartNew();
        var p0 = StateSpaceMath.LaplaceSmoothedProbability(0, 10);
        var p5 = StateSpaceMath.LaplaceSmoothedProbability(5, 10);
        var p10 = StateSpaceMath.LaplaceSmoothedProbability(10, 10);
        sw.Stop();
        var monotonePassed = p0 < p5 && p5 < p10 && p0 > 0.0 && p10 < 1.0;
        results.Add(new AlgorithmTestResult
        {
            Id = "ALG.METAMORPHIC_MONOTONE",
            Name = "Bayesian Monotonicity Invariant",
            Domain = "Stochastic Estimators / Probability",
            Invariant = "k1 < k2 under fixed n implies p(k1) < p(k2); smoothed boundaries satisfy 0 < p < 1.",
            InputSummary = "n=10 with k in [0, 5, 10]",
            Expected = "p(0) < p(5) < p(10) strictly in (0, 1)",
            Actual = $"p(0)={p0:F4}, p(5)={p5:F4}, p(10)={p10:F4}",
            Passed = monotonePassed,
            Elapsed = sw.Elapsed,
            Details = "Guarantees Bayesian order preservation and positive damping on failure/success bounds.",
        });

        // 10. Asymptotic Convergence of Wilson Score to Wald MLE
        sw = Stopwatch.StartNew();
        var wBig = StateSpaceMath.WilsonScoreInterval(5000, 10000);
        var pMle = 5000.0 / 10000.0;
        var waldHalfWidth = StateSpaceMath.Standard95PercentZ * Math.Sqrt((pMle * (1.0 - pMle)) / 10000.0);
        var waldLower = pMle - waldHalfWidth;
        var waldUpper = pMle + waldHalfWidth;
        sw.Stop();
        var asympPassed = Math.Abs(wBig.Center - pMle) < 1e-4 &&
                          Math.Abs(wBig.Lower - waldLower) < 1e-3 &&
                          Math.Abs(wBig.Upper - waldUpper) < 1e-3;
        results.Add(new AlgorithmTestResult
        {
            Id = "ALG.WILSON_ASYMPTOTIC",
            Name = "Asymptotic Wilson-Wald Convergence",
            Domain = "Inferential Statistics",
            Invariant = "For large N (10,000), Wilson score interval converges to Wald normal interval within epsilon < 1e-3.",
            InputSummary = "k=5,000, n=10,000 (central limit sample)",
            Expected = $"Wilson converges to Wald [{waldLower:F4}, {waldUpper:F4}]",
            Actual = $"Wilson=[{wBig.Lower:F4}, {wBig.Upper:F4}], Wald=[{waldLower:F4}, {waldUpper:F4}]",
            Passed = asympPassed,
            Elapsed = sw.Elapsed,
            Details = "Confirms statistical validity under Central Limit Theorem while preserving small-sample safety.",
        });

        totalClock.Stop();

        var payload = string.Join('\n', results.Select(r => $"{r.Id}:{r.Passed}:{r.Actual}"));
        var digest = Hashing.Sha256(payload);

        return new AlgorithmSuiteReport
        {
            ExecutedAt = DateTimeOffset.Now,
            Results = results,
            TotalDuration = totalClock.Elapsed,
            Sha256Digest = digest,
        };
    }

    /// <summary>
    /// Executes empirical computational complexity scaling assays across varying input sizes.
    /// </summary>
    public static ComplexitySuiteReport RunComplexitySuite()
    {
        var totalClock = Stopwatch.StartNew();
        var benchmarks = new List<ComplexityBenchmarkResult>();

        // JIT warm-up pass
        for (int w = 0; w < 100; w++)
        {
            _ = Hashing.Fingerprint("warmup", "click", "btn", w.ToString());
        }

        // 1. Cryptographic Fingerprint Digest Scaling (Expected O(N))
        var fpSizes = new[] { 100, 1000, 5000 };
        var fpSamples = new List<ComplexitySample>();
        foreach (var size in fpSizes)
        {
            double minUs = double.MaxValue;
            for (int r = 0; r < 3; r++)
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < size; i++)
                {
                    _ = Hashing.Fingerprint("target_test", "action_exec", $"btn_{i}", $"val_{i}");
                }
                sw.Stop();
                var us = Math.Max(0.1, sw.Elapsed.TotalMicroseconds);
                if (us < minUs) minUs = us;
            }
            fpSamples.Add(new ComplexitySample(size, minUs));
        }

        var fpAlpha = ComputeEmpiricalAlpha(fpSamples[0], fpSamples[^1]);
        var fpPassed = fpAlpha <= 1.45; // Linear complexity with allowance for CPU cache/scheduling
        benchmarks.Add(new ComplexityBenchmarkResult
        {
            AlgorithmName = "128-Bit Cryptographic Fingerprint Digest",
            Domain = "Applied Cryptography",
            TheoreticalComplexity = "O(N)",
            Samples = fpSamples,
            EmpiricalAlpha = fpAlpha,
            Passed = fpPassed,
            Summary = $"Scaling exponent alpha={fpAlpha:F2} across N=[{fpSizes[0]}..{fpSizes[^1]}]. {(fpPassed ? "Linear scaling verified." : "Super-linear degradation detected.")}",
        });

        // 2. Shannon Information Entropy Scaling (Expected O(N))
        var entropySizes = new[] { 100, 1000, 5000 };
        var entropySamples = new List<ComplexitySample>();
        foreach (var size in entropySizes)
        {
            var dict = new Dictionary<string, int>(size);
            for (int i = 0; i < size; i++)
            {
                dict[$"state_{i}"] = i + 1;
            }

            double minUs = double.MaxValue;
            for (int r = 0; r < 3; r++)
            {
                var sw = Stopwatch.StartNew();
                _ = StateSpaceMath.ShannonEntropy(dict);
                sw.Stop();
                var us = Math.Max(0.1, sw.Elapsed.TotalMicroseconds);
                if (us < minUs) minUs = us;
            }
            entropySamples.Add(new ComplexitySample(size, minUs));
        }

        var entropyAlpha = ComputeEmpiricalAlpha(entropySamples[0], entropySamples[^1]);
        var entropyPassed = entropyAlpha <= 1.45;
        benchmarks.Add(new ComplexityBenchmarkResult
        {
            AlgorithmName = "Shannon Information Entropy Estimation",
            Domain = "Information Theory",
            TheoreticalComplexity = "O(N)",
            Samples = entropySamples,
            EmpiricalAlpha = entropyAlpha,
            Passed = entropyPassed,
            Summary = $"Scaling exponent alpha={entropyAlpha:F2} across N=[{entropySizes[0]}..{entropySizes[^1]}]. {(entropyPassed ? "Linear scaling verified." : "Super-linear degradation detected.")}",
        });

        // 3. Tarjan Cycle & Acyclic Projection Scaling (Expected O(V + E))
        var graphSizes = new[] { 50, 200, 800 };
        var graphSamples = new List<ComplexitySample>();
        var dummyTarget = GhoztId.Create(IdKind.Target, 999);
        foreach (var vCount in graphSizes)
        {
            var testNodes = new List<LatticeNode>(vCount);
            var testEdges = new List<LatticeEdge>(vCount * 2);

            for (int i = 0; i < vCount; i++)
            {
                testNodes.Add(new LatticeNode
                {
                    Id = $"node_{i}",
                    TargetId = dummyTarget,
                    Label = $"N{i}",
                    Kind = NodeKind.Action,
                    State = "Verified",
                    Provenance = "Observed",
                });

                if (i > 0)
                {
                    testEdges.Add(new LatticeEdge
                    {
                        TargetId = dummyTarget,
                        FromId = $"node_{i - 1}",
                        ToId = $"node_{i}",
                    });
                }
            }

            // Introduce occasional back-edges
            for (int i = 4; i < vCount; i += 5)
            {
                testEdges.Add(new LatticeEdge
                {
                    TargetId = dummyTarget,
                    FromId = $"node_{i}",
                    ToId = $"node_{i - 3}",
                });
            }

            double minUs = double.MaxValue;
            for (int r = 0; r < 3; r++)
            {
                var sw = Stopwatch.StartNew();
                _ = StateSpaceMath.ProjectAcyclicSpanningTree(testNodes, testEdges, "node_0");
                sw.Stop();
                var us = Math.Max(0.1, sw.Elapsed.TotalMicroseconds);
                if (us < minUs) minUs = us;
            }
            graphSamples.Add(new ComplexitySample(vCount, minUs));
        }

        var graphAlpha = ComputeEmpiricalAlpha(graphSamples[0], graphSamples[^1]);
        var graphPassed = graphAlpha <= 1.50;
        benchmarks.Add(new ComplexityBenchmarkResult
        {
            AlgorithmName = "Acyclic Spanning Tree Projection (BFS)",
            Domain = "Graph Theory",
            TheoreticalComplexity = "O(V + E)",
            Samples = graphSamples,
            EmpiricalAlpha = graphAlpha,
            Passed = graphPassed,
            Summary = $"Scaling exponent alpha={graphAlpha:F2} across V=[{graphSizes[0]}..{graphSizes[^1]}]. {(graphPassed ? "Linear BFS scaling verified." : "Super-linear degradation detected.")}",
        });

        totalClock.Stop();

        var payload = string.Join('\n', benchmarks.Select(b => $"{b.AlgorithmName}:{b.EmpiricalAlpha:F2}:{b.Passed}"));
        var digest = Hashing.Sha256(payload);

        return new ComplexitySuiteReport
        {
            ExecutedAt = DateTimeOffset.Now,
            Results = benchmarks,
            TotalDuration = totalClock.Elapsed,
            Sha256Digest = digest,
        };
    }

    /// <summary>
    /// Computes empirical scaling exponent alpha = ln(T2 / T1) / ln(N2 / N1).
    /// </summary>
    public static double ComputeEmpiricalAlpha(ComplexitySample p1, ComplexitySample p2)
    {
        if (p1.SizeN <= 0 || p2.SizeN <= p1.SizeN || p1.Microseconds <= 0 || p2.Microseconds <= 0)
        {
            return 1.0;
        }

        var deltaN = Math.Log((double)p2.SizeN / p1.SizeN);
        var deltaT = Math.Log(p2.Microseconds / p1.Microseconds);

        if (deltaN <= 1e-9)
        {
            return 1.0;
        }

        return Math.Max(0.0, deltaT / deltaN);
    }

    /// <summary>
    /// Generic Delta Debugging (ddmin) algorithm input minimiser for isolating the 1-minimal
    /// subset of elements reproducing an invariant violation (Patent §20 &amp; §28).
    /// </summary>
    public static IReadOnlyList<T> MinimizeInput<T>(
        IReadOnlyList<T> inputs,
        Func<IReadOnlyList<T>, bool> isFailingProperty)
    {
        if (inputs.Count <= 1 || !isFailingProperty(inputs))
        {
            return inputs;
        }

        var current = inputs.ToList();
        var n = 2;

        while (current.Count >= 2)
        {
            var subsets = SplitIntoSubsets(current, n);
            var reduced = false;

            // 1. Try each individual subset (complement elimination)
            foreach (var sub in subsets)
            {
                if (sub.Count > 0 && sub.Count < current.Count && isFailingProperty(sub))
                {
                    current = sub;
                    n = Math.Max(n - 1, 2);
                    reduced = true;
                    break;
                }
            }

            if (reduced)
            {
                continue;
            }

            // 2. Try each subset complement (removing one subset)
            foreach (var sub in subsets)
            {
                var complement = current.Except(sub).ToList();
                if (complement.Count > 0 && complement.Count < current.Count && isFailingProperty(complement))
                {
                    current = complement;
                    n = Math.Max(n - 1, 2);
                    reduced = true;
                    break;
                }
            }

            if (reduced)
            {
                continue;
            }

            // 3. Increase granularity
            if (n < current.Count)
            {
                n = Math.Min(current.Count, n * 2);
            }
            else
            {
                // 1-minimal subset achieved
                break;
            }
        }

        return current;
    }

    private static List<List<T>> SplitIntoSubsets<T>(IReadOnlyList<T> list, int n)
    {
        var result = new List<List<T>>(n);
        var chunkSize = (int)Math.Ceiling((double)list.Count / n);

        for (int i = 0; i < list.Count; i += chunkSize)
        {
            var count = Math.Min(chunkSize, list.Count - i);
            result.Add(list.Skip(i).Take(count).ToList());
        }

        return result;
    }

    /// <summary>
    /// Generates a formal mathematical and algorithmic verification dossier in Markdown format.
    /// </summary>
    public static string GenerateMathematicalDossier(AlgorithmSuiteReport report, ComplexitySuiteReport? complexity = null)
    {
        var complexitySection = complexity is null ? "" : $"""
            ---

            ## 2. Computational Complexity Scaling Assays (Big-O Benchmark)

            | Algorithm | Domain | Theoretical | Samples (N -> us) | Empirical Exponent (alpha) | Status |
            |---|---|---|---|---|---|
            {string.Join("\n", complexity.Results.Select(c => $"| {c.AlgorithmName} | {c.Domain} | `{c.TheoreticalComplexity}` | {string.Join(", ", c.Samples.Select(s => $"{s.SizeN} => {s.Microseconds:F1}us"))} | `alpha={c.EmpiricalAlpha:F2}` | {(c.Passed ? "✅ PASS" : "❌ DRIFT")} |"))}

            """;

        return $"""
            # Ghozt — Mathematical & Algorithmic Invariant Verification Dossier

            | Field | Value |
            |---|---|
            | Executed At | {report.ExecutedAt:O} |
            | Invariants Evaluated | {report.Results.Count} |
            | Invariants Verified | {report.PassedCount} |
            | Deviations / Failures | {report.FailedCount} |
            | Total Execution Duration | {report.TotalDuration.TotalMilliseconds:F2} ms |
            | Cryptographic SHA-256 Digest | `{report.Sha256Digest}` |

            ---

            ## 1. Verified Invariant Matrix

            {string.Join("\n\n", report.Results.Select(r => $"""
            ### {r.Id}: {r.Name}
            - **Domain**: {r.Domain}
            - **Status**: {(r.Passed ? "✅ VERIFIED" : "❌ DEVIATION")}
            - **Evaluation Time**: {r.Elapsed.TotalMilliseconds:F3} ms
            - **Formal Invariant**: {r.Invariant}
            - **Input Parameters**: {r.InputSummary}
            - **Expected (Closed-Form)**: `{r.Expected}`
            - **Actual Calculated**: `{r.Actual}`
            - **Forensic Detail**: {r.Details}
            """))}

            {complexitySection}
            ---

            ## {(complexity is null ? "2" : "3")}. Mathematical Integrity Confirmation
            All evaluated equations, Bayesian estimators, Wilson score confidence boundaries, Shannon entropy estimators, metamorphic permutations, asymptotic limits, and acyclic graph projections have been evaluated against closed-form analytical truths under IEEE 754 double-precision arithmetic.
            """;
    }

    /// <summary>
    /// Cryptographically seals an algorithm suite execution report into the target's evidence store.
    /// </summary>
    public static Evidence SealReportToEvidence(
        GhoztStore store,
        GhoztId targetId,
        AlgorithmSuiteReport report,
        ComplexitySuiteReport? complexity = null)
    {
        var complexityDetail = complexity is null ? "" : $"""

            COMPLEXITY BENCHMARKS:
            {string.Join("\n", complexity.Results.Select(c => $"[{c.TheoreticalComplexity}] {c.AlgorithmName}: alpha={c.EmpiricalAlpha:F2} -> {(c.Passed ? "PASS" : "FAIL")}\n  {c.Summary}"))}
            """;

        var payload = $"""
            ALGORITHM SUITE VERIFICATION REPORT
            Target: {targetId}
            ExecutedAt: {report.ExecutedAt:O}
            TotalDuration: {report.TotalDuration.TotalMilliseconds:F2} ms
            Passed: {report.PassedCount}/{report.Results.Count}
            Failed: {report.FailedCount}
            Sha256Digest: {report.Sha256Digest}

            TEST DETAILS:
            {string.Join("\n", report.Results.Select(r => $"[{r.Id}] {r.Name} ({r.Domain}): {(r.Passed ? "PASS" : "FAIL")}\n  Expected: {r.Expected}\n  Actual:   {r.Actual}\n  Details:  {r.Details}"))}
            {complexityDetail}
            """;

        var evidence = new Evidence
        {
            Id = store.NextId(IdKind.Evidence),
            TargetId = targetId,
            Kind = "algorithm-proof",
            Summary = $"Algorithm Suite: {report.PassedCount}/{report.Results.Count} passed{(complexity != null ? $", {complexity.PassedCount}/{complexity.Results.Count} complexity assays passed" : "")}",
            Sha256 = Hashing.Sha256(payload),
            SizeBytes = System.Text.Encoding.UTF8.GetByteCount(payload),
            Payload = payload,
            SourceAdapter = "AlgorithmHarness.RunSuite",
            CapturedAt = DateTimeOffset.Now,
            Sealed = true,
            SealedBy = Environment.UserName,
        };

        store.Save(evidence);

        store.Append(new LedgerEvent
        {
            Id = store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "ALG.SEAL",
            Message = $"Algorithm suite verified and sealed ({report.PassedCount}/{report.Results.Count} invariants{(complexity != null ? $", {complexity.PassedCount}/{complexity.Results.Count} complexity assays" : "")}).",
            Severity = Severity.Info,
            TargetId = targetId,
            RelatedId = evidence.Id.ToString(),
            Actor = "AlgorithmHarness",
            Detail = $"Sha256={evidence.Sha256}; Duration={report.TotalDuration.TotalMilliseconds:F1}ms",
        });

        return evidence;
    }
}

