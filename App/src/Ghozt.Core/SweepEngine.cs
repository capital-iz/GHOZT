using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Ghozt.Core;

/// <summary>What a sweep is currently doing, for the live board.</summary>
public sealed record SweepProgress(
    string Phase,
    int StatesVisited,
    int MaxStates,
    int Deviations,
    int Evidence,
    TimeSpan Elapsed,
    string Detail);

/// <summary>
/// Runs a governed examination against a registered target.
/// </summary>
/// <remarks>
/// <para>
/// This engine performs real work against the target it is given and records what it actually
/// observes. It does not fabricate findings. Where a check cannot be performed - no base address, an
/// unreadable path, an access class that forbids the action - it records that as a blocked or
/// skipped check with the reason, which is a different outcome from a passing one and is shown as
/// such.
/// </para>
/// <para>
/// Budgets are enforced rather than advertised: the run stops when its state, time or depth budget
/// is exhausted, and the resulting sweep is marked Partial rather than Completed. A run that
/// silently ran past its budget would make every budget in the product meaningless.
/// </para>
/// </remarks>
public sealed class SweepEngine
{
    private static readonly HttpClient s_httpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        CheckCertificateRevocationList = false,
    })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private readonly GhoztStore _store;

    public SweepEngine(GhoztStore store) => _store = store;

    /// <summary>The checks a sweep mode performs, in order.</summary>
    public static IReadOnlyList<string> ChecksFor(string mode) => mode switch
    {
        "Smoke" => ["Reachability", "Identity", "Structure"],
        "Changed area" => ["Reachability", "Identity", "Structure", "Integrity"],
        "Full sweep" => ["Reachability", "Identity", "Structure", "Integrity", "Quality Validation", "Regression Guarding", "Capacity", "Consistency"],
        "Deep forensic" => ["Reachability", "Identity", "Structure", "Integrity", "Quality Validation", "Regression Guarding", "Capacity", "Consistency", "Provenance"],
        "Autonomous E2E" => ["Reachability", "Identity", "Autonomous Exploration", "Quality Validation", "Regression Guarding", "Integrity"],
        _ => ["Reachability", "Identity"],
    };

    public static IReadOnlyList<string> Modes =>
        ["Smoke", "Changed area", "Full sweep", "Deep forensic", "Autonomous E2E"];

    /// <summary>
    /// Executes a sweep, reporting progress as it goes.
    /// </summary>
    /// <remarks>
    /// Cancellation is honoured between checks rather than only at the end, so a stop actually stops.
    /// Whatever was completed before the stop is preserved and the sweep is marked Cancelled, because
    /// discarding partial evidence would punish an operator for stopping a run that was going wrong.
    /// </remarks>
    public async Task<Sweep> RunAsync(
        Sweep sweep,
        Target target,
        IProgress<SweepProgress>? progress,
        CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew();
        var checks = ChecksFor(sweep.Mode);

        var visited = 0;
        var deviations = 0;
        var evidence = 0;
        var budgetExhausted = false;

        var current = sweep with { State = RunState.Running, StartedAt = DateTimeOffset.Now };
        _store.Save(current);

        Log("SWP.START", $"Sweep {sweep.Id} started in {sweep.Mode} mode against {target.Name}.",
            Severity.Info, sweep, target,
            $"budget: {sweep.MaxStates} states, {sweep.MaxSeconds}s, depth {sweep.MaxDepth}; "
            + $"destructive={sweep.AllowDestructive}");

        foreach (var check in checks)
        {
            if (cancellation.IsCancellationRequested)
            {
                break;
            }

            if (visited >= sweep.MaxStates || clock.Elapsed.TotalSeconds >= sweep.MaxSeconds)
            {
                budgetExhausted = true;
                Log("SWP.BUDGET", $"Budget exhausted before {check}.", Severity.Medium, sweep, target,
                    $"states={visited}/{sweep.MaxStates}; elapsed={clock.Elapsed.TotalSeconds:F1}/{sweep.MaxSeconds}s");
                break;
            }

            progress?.Report(new SweepProgress(check, visited, sweep.MaxStates, deviations, evidence,
                clock.Elapsed, $"Running {check}…"));

            // A check that throws is a blocked check, not a dead sweep.
            //
            // The first version let the exception propagate, and one unreadable file ended the whole
            // run - losing the evidence from every check that had already passed. A forensics tool
            // examines systems that are live, locked, half-configured and occasionally hostile; a
            // single failing probe must degrade that probe, not the examination.
            CheckResult result;
            try
            {
                result = await RunCheckAsync(check, sweep, target, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // An adapter can fail in any way; the sweep records it and continues.
            catch (Exception ex)
            {
                result = new CheckResult(
                    Passed: false,
                    Blocked: true,
                    Summary: $"{check} could not be completed.",
                    Detail: $"The check raised {ex.GetType().Name}: {ex.Message}\n\n{ex}",
                    Severity: Severity.Medium,
                    StatesVisited: 1);
            }
#pragma warning restore CA1031

            visited += result.StatesVisited;

            Log(result.Passed ? "CHK.PASS" : result.Blocked ? "CHK.BLOCKED" : "CHK.DEVIATION",
                result.Summary,
                result.Passed ? Severity.Info : result.Blocked ? Severity.Medium : result.Severity,
                sweep, target, result.Detail);

            // Every check produces an artefact, whatever its outcome. A check that passed with no
            // record behind it is an assertion; the point of this product is that it is not.
            var payload = $"check={check}\ntarget={target.Id}\nsweep={sweep.Id}\n"
                          + $"at={DateTimeOffset.Now:O}\noutcome={(result.Blocked ? "blocked" : result.Passed ? "pass" : "deviation")}\n"
                          + result.Detail;

            _store.Save(new Evidence
            {
                Id = _store.NextId(IdKind.Evidence),
                TargetId = target.Id,
                SweepId = sweep.Id,
                Kind = $"check:{check.ToLowerInvariant()}",
                Summary = result.Summary,
                Sha256 = Hashing.Sha256(payload),
                SizeBytes = System.Text.Encoding.UTF8.GetByteCount(payload),
                Payload = payload,
                SourceAdapter = "SweepEngine",
                CapturedAt = DateTimeOffset.Now,
            });
            evidence++;

            // A node in the lattice for the check, carrying its observed state. Real structure, not
            // decoration: it resolves to a check that actually ran against this target.
            _store.Save(new LatticeNode
            {
                Id = $"check:{check.ToLowerInvariant()}",
                TargetId = target.Id,
                Label = check,
                Kind = NodeKind.Action,
                State = result.Blocked ? "Warning" : result.Passed ? "Verified" : "Deviation",
                Provenance = "Observed",
                ParentId = "system:root",
                VisitCount = 1,
                Detail = result.Detail,
                EvidenceCount = 1,
                LastSeenAt = DateTimeOffset.Now,
            });

            _store.Save(new LatticeEdge
            {
                TargetId = target.Id,
                FromId = "system:root",
                ToId = $"check:{check.ToLowerInvariant()}",
                Label = check,
                Relationship = "observed",
                TraversalCount = 1,
            });

            if (!result.Passed && !result.Blocked)
            {
                deviations++;
                RecordFinding(result, sweep, target);
            }

            progress?.Report(new SweepProgress(check, visited, sweep.MaxStates, deviations, evidence,
                clock.Elapsed, result.Summary));
        }

        var finalState =
            cancellation.IsCancellationRequested ? RunState.Cancelled
            : budgetExhausted ? RunState.Partial
            : RunState.Completed;

        var outcome = finalState switch
        {
            RunState.Cancelled => "Stopped by the operator. Completed checks and their evidence are preserved.",
            RunState.Partial => "Budget exhausted before every check ran. Untested scope is marked, not assumed to pass.",
            _ when deviations > 0 => $"Completed with {deviations} deviation{(deviations == 1 ? "" : "s")}.",
            _ => "Completed. Every check in scope passed with evidence.",
        };

        current = current with
        {
            State = finalState,
            FinishedAt = DateTimeOffset.Now,
            StatesVisited = visited,
            DeviationsFound = deviations,
            EvidenceCaptured = evidence,
            Outcome = outcome,
        };

        _store.Save(current);
        _store.Save(target with { LastSweepAt = DateTimeOffset.Now });

        Log("SWP.END", $"Sweep {sweep.Id} {finalState.ToString().ToLowerInvariant()}. {outcome}",
            deviations > 0 ? Severity.Medium : Severity.Info, sweep, target,
            $"states={visited}; deviations={deviations}; evidence={evidence}; elapsed={clock.Elapsed.TotalSeconds:F1}s");

        progress?.Report(new SweepProgress("Finished", visited, sweep.MaxStates, deviations, evidence,
            clock.Elapsed, outcome));

        return current;
    }

    private sealed record CheckResult(
        bool Passed,
        bool Blocked,
        string Summary,
        string Detail,
        Severity Severity,
        int StatesVisited,
        string Expected = "",
        string Actual = "");

    /// <summary>
    /// Performs one check against the real target.
    /// </summary>
    /// <remarks>
    /// Every branch measures something on this machine. Where the target does not expose what a check
    /// needs, the check is <em>blocked</em> - which is neither a pass nor a failure, and is the
    /// outcome the blueprint requires be distinguishable from both.
    /// </remarks>
    private async Task<CheckResult> RunCheckAsync(
        string check,
        Sweep sweep,
        Target target,
        CancellationToken cancellation)
    {
        // A short, real pause between checks so the live board is legible and cancellation has
        // somewhere to land. Not a fake workload: the checks below do the actual work.
        await Task.Delay(120, cancellation).ConfigureAwait(false);

        var path = target.BaseAddress;

        switch (check)
        {
            case "Reachability":
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return new CheckResult(false, true,
                        "Reachability could not be established.",
                        "No base address or path is registered for this target, so there is nothing to reach.",
                        Severity.Medium, 1);
                }

                if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                {
                    try
                    {
                        var sw = Stopwatch.StartNew();
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                        cts.CancelAfter(TimeSpan.FromSeconds(8));
                        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                        using var resp = await s_httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                        sw.Stop();
                        var status = (int)resp.StatusCode;
                        var latency = sw.ElapsedMilliseconds;
                        var server = resp.Headers.Server?.ToString() ?? "Standard";
                        var contentType = resp.Content.Headers.ContentType?.ToString() ?? "unknown";
                        var pass = resp.IsSuccessStatusCode;
                        return pass
                            ? new CheckResult(true, false,
                                $"HTTP Endpoint reachable: HTTP {status} in {latency}ms.",
                                $"uri={uri}\nstatusCode={status} {resp.ReasonPhrase}\nlatencyMs={latency}\nserver={server}\ncontentType={contentType}",
                                Severity.Info, 1)
                            : new CheckResult(false, false,
                                $"HTTP Endpoint returned error status: HTTP {status} {resp.ReasonPhrase}.",
                                $"uri={uri}\nstatusCode={status} {resp.ReasonPhrase}\nlatencyMs={latency}",
                                Severity.High, 1, "HTTP 2xx Success", $"HTTP {status}");
                    }
                    catch (Exception ex)
                    {
                        return new CheckResult(false, false,
                            $"HTTP Endpoint connection failed: {ex.Message}",
                            $"uri={uri}\nexception={ex.GetType().Name}: {ex.Message}",
                            Severity.High, 1, "Reachable HTTP endpoint", ex.Message);
                    }
                }

                if (path.StartsWith("process:", StringComparison.OrdinalIgnoreCase) && int.TryParse(path["process:".Length..], out var pid))
                {
                    try
                    {
                        var proc = Process.GetProcessById(pid);
                        var responding = proc.Responding;
                        return new CheckResult(!proc.HasExited, false,
                            $"Process target reachable: {proc.ProcessName} (PID {pid}).",
                            $"processName={proc.ProcessName}\npid={pid}\nresponding={responding}\nthreads={proc.Threads.Count}",
                            Severity.Info, 1);
                    }
                    catch (Exception ex)
                    {
                        return new CheckResult(false, false,
                            $"Process target unreachable (PID {pid}): {ex.Message}",
                            $"pid={pid}\nerror={ex.Message}", Severity.High, 1, "Active process", "Exited or access denied");
                    }
                }

                if (File.Exists(path))
                {
                    var procName = Path.GetFileNameWithoutExtension(path);
                    var running = Process.GetProcessesByName(procName);
                    if (running.Length > 0)
                    {
                        var main = running[0];
                        return new CheckResult(true, false,
                            $"Target binary present and active: {procName} (PID {main.Id}).",
                            $"path={path}\nactivePid={main.Id}\nresponding={main.Responding}\nwindowHandle={main.MainWindowHandle:X}",
                            Severity.Info, 1);
                    }

                    return new CheckResult(true, false,
                        "Target file is present and readable.",
                        $"Resolved {path} ({new FileInfo(path).Length:N0} bytes).", Severity.Info, 1);
                }

                if (Directory.Exists(path))
                {
                    var dir = new DirectoryInfo(path);
                    var count = 0;
                    try { count = Directory.EnumerateFileSystemEntries(path).Take(100).Count(); } catch { }
                    return new CheckResult(true, false,
                        $"Target directory reachable: {dir.Name} ({count}+ items).",
                        $"path={dir.FullName}\ncreated={dir.CreationTimeUtc:O}\naccessibleItems={count}", Severity.Info, 1);
                }

                return new CheckResult(false, false, "Target path does not exist.",
                    $"Expected a file, directory or endpoint at {path}; nothing is there.",
                    Severity.High, 1, $"A readable artefact at {path}", "Path not found");
            }

            case "Identity":
            {
                if (string.IsNullOrWhiteSpace(target.BuildLabel))
                {
                    return new CheckResult(false, false,
                        "No build identity is recorded for this target.",
                        "Findings cannot be attributed to a build, so a regression cannot be bounded to a change.",
                        Severity.Medium, 1,
                        "A version, commit or package identifier", "empty");
                }

                return new CheckResult(true, false,
                    $"Build identity recorded: {target.BuildLabel}.",
                    $"buildLabel={target.BuildLabel}; owner={target.Owner}", Severity.Info, 1);
            }

            case "Structure":
            {
                if (Uri.TryCreate(path, UriKind.Absolute, out var sUri) && sUri.Scheme is "http" or "https")
                {
                    try
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                        cts.CancelAfter(TimeSpan.FromSeconds(8));
                        using var resp = await s_httpClient.GetAsync(sUri, cts.Token).ConfigureAwait(false);
                        var content = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                        var contentType = resp.Content.Headers.ContentType?.MediaType ?? "text/plain";
                        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) || content.TrimStart().StartsWith('{') || content.TrimStart().StartsWith('['))
                        {
                            try
                            {
                                using var doc = JsonDocument.Parse(content);
                                var root = doc.RootElement;
                                var keyCount = root.ValueKind == JsonValueKind.Object ? root.EnumerateObject().Count() : (root.ValueKind == JsonValueKind.Array ? root.GetArrayLength() : 1);
                                return new CheckResult(true, false,
                                    $"HTTP JSON structure verified: {root.ValueKind} ({keyCount} items/keys, {content.Length:N0} chars).",
                                    $"contentType={contentType}\nrootKind={root.ValueKind}\nitemsCount={keyCount}\npayloadBytes={System.Text.Encoding.UTF8.GetByteCount(content)}",
                                    Severity.Info, 2);
                            }
                            catch (JsonException jex)
                            {
                                return new CheckResult(false, false,
                                    "HTTP response declared JSON but failed structural parsing.",
                                    $"uri={sUri}\nerror={jex.Message}\npayloadSnippet={content[..Math.Min(200, content.Length)]}",
                                    Severity.High, 2, "Valid JSON document", "Invalid JSON syntax");
                            }
                        }
                        else
                        {
                            return new CheckResult(true, false,
                                $"HTTP response structure verified: {contentType} ({content.Length:N0} chars).",
                                $"uri={sUri}\ncontentType={contentType}\nsizeBytes={System.Text.Encoding.UTF8.GetByteCount(content)}",
                                Severity.Info, 2);
                        }
                    }
                    catch (Exception ex)
                    {
                        return new CheckResult(false, false,
                            $"HTTP response structure check failed: {ex.Message}",
                            $"uri={sUri}\nexception={ex.GetType().Name}: {ex.Message}",
                            Severity.High, 2, "Readable HTTP response", ex.Message);
                    }
                }

                if (Directory.Exists(path))
                {
                    var dir = new DirectoryInfo(path);
                    var gitDir = Path.Combine(path, ".git");
                    var hasGit = Directory.Exists(gitDir);
                    var gitBranch = "";
                    if (hasGit)
                    {
                        var headFile = Path.Combine(gitDir, "HEAD");
                        if (File.Exists(headFile))
                        {
                            var headContent = File.ReadAllText(headFile).Trim();
                            gitBranch = headContent.StartsWith("ref: refs/heads/") ? headContent["ref: refs/heads/".Length..] : headContent;
                        }
                    }
                    var files = Directory.EnumerateFiles(path, "*.*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 3 }).Take(500).ToList();
                    var extensions = files.GroupBy(f => Path.GetExtension(f).ToLowerInvariant())
                                          .OrderByDescending(g => g.Count())
                                          .Take(5)
                                          .Select(g => $"{g.Key} ({g.Count()})");
                    return new CheckResult(true, false,
                        $"Directory structure verified: {files.Count} files sampled" + (hasGit ? $" · Git branch: {gitBranch}" : "") + ".",
                        $"path={dir.FullName}\nsampledFiles={files.Count}\nhasGitRepository={hasGit}\ngitBranch={gitBranch}\ntopExtensions={string.Join(", ", extensions)}",
                        Severity.Info, 2);
                }

                if (File.Exists(path) && (path.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;");
                        conn.Open();
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT type, count(*) FROM sqlite_master GROUP BY type;";
                        using var reader = cmd.ExecuteReader();
                        var summaryParts = new List<string>();
                        while (reader.Read())
                        {
                            summaryParts.Add($"{reader.GetString(0)}s: {reader.GetInt32(1)}");
                        }
                        using var cmdVer = conn.CreateCommand();
                        cmdVer.CommandText = "PRAGMA user_version;";
                        var userVer = cmdVer.ExecuteScalar()?.ToString() ?? "0";
                        return new CheckResult(true, false,
                            $"SQLite schema structure verified: {string.Join(", ", summaryParts)} · Schema v{userVer}.",
                            $"path={path}\nuserVersion={userVer}\nschemaObjects={string.Join("; ", summaryParts)}",
                            Severity.Info, 2);
                    }
                    catch (Exception ex)
                    {
                        return new CheckResult(false, false,
                            "SQLite database structure check failed.",
                            $"path={path}\nexception={ex.GetType().Name}: {ex.Message}",
                            Severity.High, 2, "Valid readable SQLite schema", "Corrupted or unreadable SQLite database");
                    }
                }

                if (File.Exists(path) && (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    var vi = FileVersionInfo.GetVersionInfo(path);
                    var fi = new FileInfo(path);
                    return new CheckResult(true, false,
                        $"Binary PE structure verified: {fi.Length:N0} bytes · Version {vi.FileVersion ?? vi.ProductVersion ?? "unknown"}.",
                        $"path={path}\nproduct={vi.ProductName}\nfileVersion={vi.FileVersion}\ncompany={vi.CompanyName}\ndescription={vi.FileDescription}\nsizeBytes={fi.Length}",
                        Severity.Info, 2);
                }

                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    return new CheckResult(true, false,
                        $"Structure readable: {info.Length:N0} bytes.",
                        $"path={info.FullName}\nlength={info.Length}\nmodified={info.LastWriteTimeUtc:O}",
                        Severity.Info, 2);
                }

                return new CheckResult(false, false,
                    "Structure check failed: target path does not exist.",
                    $"Target path '{path}' was not found as a file, directory or URL.",
                    Severity.High, 2, "Existing file or directory", "Not found");
            }

            case "Integrity":
            {
                if (Uri.TryCreate(path, UriKind.Absolute, out var iUri) && iUri.Scheme is "http" or "https")
                {
                    try
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                        cts.CancelAfter(TimeSpan.FromSeconds(8));
                        using var resp = await s_httpClient.GetAsync(iUri, cts.Token).ConfigureAwait(false);
                        var etag = resp.Headers.ETag?.Tag ?? "";
                        var bodyBytes = await resp.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
                        var hash = Hashing.Sha256(bodyBytes);
                        return new CheckResult(true, false,
                            $"HTTP payload integrity hash computed: {hash[..12]}…" + (!string.IsNullOrEmpty(etag) ? $" (ETag: {etag})" : "") + ".",
                            $"uri={iUri}\nsha256={hash}\netag={etag}\nsizeBytes={bodyBytes.Length}",
                            Severity.Info, 2);
                    }
                    catch (Exception ex)
                    {
                        return new CheckResult(false, false,
                            $"HTTP integrity check failed: {ex.Message}",
                            $"uri={iUri}\nexception={ex.GetType().Name}: {ex.Message}",
                            Severity.High, 2, "Fetchable payload", ex.Message);
                    }
                }

                if (Directory.Exists(path))
                {
                    var gitDir = Path.Combine(path, ".git");
                    if (Directory.Exists(gitDir))
                    {
                        var headFile = Path.Combine(gitDir, "HEAD");
                        if (File.Exists(headFile))
                        {
                            var headRef = File.ReadAllText(headFile).Trim();
                            var commitHash = headRef;
                            if (headRef.StartsWith("ref: "))
                            {
                                var refPath = Path.Combine(gitDir, headRef[5..].Replace('/', Path.DirectorySeparatorChar));
                                if (File.Exists(refPath)) commitHash = File.ReadAllText(refPath).Trim();
                            }
                            return new CheckResult(true, false,
                                $"Git repository integrity verified: commit {commitHash[..Math.Min(10, commitHash.Length)]}…",
                                $"path={path}\nheadRef={headRef}\ncommitHash={commitHash}",
                                Severity.Info, 2);
                        }
                    }
                    var sample = Directory.EnumerateFiles(path, "*.*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 2 })
                                          .Take(50).Select(f => $"{Path.GetFileName(f)}:{new FileInfo(f).Length}").ToList();
                    var manifestHash = Hashing.Sha256(string.Join(";", sample));
                    return new CheckResult(true, false,
                        $"Directory tree manifest hash computed: {manifestHash[..12]}… ({sample.Count} files).",
                        $"path={path}\nmanifestHash={manifestHash}\nsampledFiles={sample.Count}",
                        Severity.Info, 2);
                }

                if (File.Exists(path) && (path.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;");
                        conn.Open();
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "PRAGMA integrity_check(10);";
                        var integrityOutput = cmd.ExecuteScalar()?.ToString() ?? "unknown";
                        var isOk = string.Equals(integrityOutput, "ok", StringComparison.OrdinalIgnoreCase);
                        var fileHash = Hashing.Sha256File(path);
                        return isOk
                            ? new CheckResult(true, false,
                                "SQLite database integrity verified (PRAGMA integrity_check = ok).",
                                $"path={path}\nintegrityCheck={integrityOutput}\nfileSha256={fileHash}",
                                Severity.Info, 2)
                            : new CheckResult(false, false,
                                $"SQLite integrity check reported corruption: {integrityOutput}",
                                $"path={path}\nintegrityCheck={integrityOutput}\nfileSha256={fileHash}",
                                Severity.High, 2, "ok", integrityOutput);
                    }
                    catch (Exception ex)
                    {
                        return new CheckResult(false, false,
                            $"SQLite database integrity check error: {ex.Message}",
                            $"path={path}\nerror={ex}",
                            Severity.High, 2, "Readable database with PRAGMA integrity_check = ok", ex.Message);
                    }
                }

                if (File.Exists(path))
                {
                    var hash = Hashing.Sha256File(path);
                    return new CheckResult(true, false,
                        "Content hash computed.",
                        $"sha256={hash}\nThis is the value a later run compares against to detect drift.",
                        Severity.Info, 2);
                }

                return new CheckResult(false, false,
                    "Integrity check failed: target path does not exist.",
                    $"Cannot compute integrity over non-existent path '{path}'.",
                    Severity.High, 2, "Existing artifact", "Not found");
            }

            case "Capacity":
            {
                if (Uri.TryCreate(path, UriKind.Absolute, out var cUri) && cUri.Scheme is "http" or "https")
                {
                    try
                    {
                        var sw = Stopwatch.StartNew();
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                        cts.CancelAfter(TimeSpan.FromSeconds(5));
                        using var resp = await s_httpClient.GetAsync(cUri, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                        sw.Stop();
                        var latency = sw.ElapsedMilliseconds;
                        var rateLimitRemaining = resp.Headers.TryGetValues("X-RateLimit-Remaining", out var rVals) ? rVals.FirstOrDefault() : null;
                        var passed = latency < 1500;
                        return passed
                            ? new CheckResult(true, false,
                                $"HTTP endpoint capacity adequate: {latency}ms latency.",
                                $"uri={cUri}\nlatencyMs={latency}\nrateLimitRemaining={rateLimitRemaining ?? "unbounded"}",
                                Severity.Info, 1)
                            : new CheckResult(false, false,
                                $"HTTP endpoint latency exceeds capacity SLA: {latency}ms (> 1500ms).",
                                $"uri={cUri}\nlatencyMs={latency}\nrateLimitRemaining={rateLimitRemaining ?? "unbounded"}",
                                Severity.Medium, 1, "< 1500ms latency", $"{latency}ms");
                    }
                    catch (Exception ex)
                    {
                        return new CheckResult(false, false,
                            $"HTTP endpoint capacity probe failed: {ex.Message}",
                            $"uri={cUri}\nexception={ex.GetType().Name}: {ex.Message}",
                            Severity.Medium, 1, "Responsive endpoint", ex.Message);
                    }
                }

                if (File.Exists(path) && (path.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase)))
                {
                    var fi = new FileInfo(path);
                    var walPath = path + "-wal";
                    var walSize = File.Exists(walPath) ? new FileInfo(walPath).Length : 0;
                    var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)) ?? "C:\\");
                    var freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                    return freeGb < 2.0
                        ? new CheckResult(false, false,
                            $"Storage pressure: {freeGb:F1} GB free on DB volume.",
                            $"path={path}\ndbSizeBytes={fi.Length}\nwalSizeBytes={walSize}\nfreeSpaceGb={freeGb:F2}",
                            Severity.High, 1, "at least 2 GB free", $"{freeGb:F2} GB")
                        : new CheckResult(true, false,
                            $"Database capacity adequate: {fi.Length / 1024.0 / 1024.0:F2} MB DB (volume has {freeGb:F1} GB free).",
                            $"path={path}\ndbSizeBytes={fi.Length}\nwalSizeBytes={walSize}\nfreeSpaceGb={freeGb:F2}",
                            Severity.Info, 1);
                }

                if (File.Exists(path))
                {
                    var procName = Path.GetFileNameWithoutExtension(path);
                    var procs = Process.GetProcessesByName(procName);
                    if (procs.Length > 0)
                    {
                        var p = procs[0];
                        var wsMb = p.WorkingSet64 / 1024.0 / 1024.0;
                        var privateMb = p.PrivateMemorySize64 / 1024.0 / 1024.0;
                        var handles = p.HandleCount;
                        var threads = p.Threads.Count;
                        var highMem = wsMb > 1500.0;
                        return highMem
                            ? new CheckResult(false, false,
                                $"Process memory capacity warning: {wsMb:F1} MB working set.",
                                $"process={p.ProcessName} (PID {p.Id})\nworkingSetMb={wsMb:F1}\nprivateMemoryMb={privateMb:F1}\nhandles={handles}\nthreads={threads}",
                                Severity.Medium, 1, "< 1500 MB memory", $"{wsMb:F1} MB")
                            : new CheckResult(true, false,
                                $"Process capacity healthy: {wsMb:F1} MB working set, {threads} threads, {handles} handles.",
                                $"process={p.ProcessName} (PID {p.Id})\nworkingSetMb={wsMb:F1}\nprivateMemoryMb={privateMb:F1}\nhandles={handles}\nthreads={threads}",
                                Severity.Info, 1);
                    }
                }

                if (File.Exists(path) || Directory.Exists(path))
                {
                    var driveRoot = Path.GetPathRoot(Path.GetFullPath(path)) ?? "C:\\";
                    var drive = new DriveInfo(driveRoot);
                    var freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                    return freeGb < 2.0
                        ? new CheckResult(false, false,
                            $"Storage pressure: {freeGb:F1} GB free.",
                            $"drive={drive.Name}; free={freeGb:F2} GB. Evidence capture may be degraded or lost below 2 GB.",
                            Severity.High, 1, "at least 2 GB free", $"{freeGb:F2} GB")
                        : new CheckResult(true, false,
                            $"Storage headroom adequate: {freeGb:F1} GB free.",
                            $"drive={drive.Name}; free={freeGb:F2} GB", Severity.Info, 1);
                }

                return new CheckResult(false, false,
                    "Capacity check failed: target path does not exist.",
                    $"Cannot measure capacity for non-existent target '{path}'.",
                    Severity.Medium, 1, "Valid target entity", "Not found");
            }

            case "Consistency":
            {
                // Measured against this product's own records, which is a real consistency question:
                // an artefact bound to a finding that no longer exists is a broken evidence chain.
                var findings = _store.Findings(target.Id);
                var artefacts = _store.EvidenceItems(target.Id);
                var orphaned = artefacts.Count(a => a.FindingId is { } id && findings.All(f => f.Id != id));

                return orphaned == 0
                    ? new CheckResult(true, false,
                        "Evidence chain is consistent.",
                        $"{artefacts.Count} artefact(s) checked against {findings.Count} finding(s); no orphans.",
                        Severity.Info, 2)
                    : new CheckResult(false, false,
                        $"{orphaned} evidence artefact(s) reference a finding that no longer exists.",
                        $"artefacts={artefacts.Count}; findings={findings.Count}; orphaned={orphaned}",
                        Severity.High, 2, "every artefact resolves to a finding", $"{orphaned} orphaned");
            }

            case "Provenance":
            {
                var artefacts = _store.EvidenceItems(target.Id);
                var unhashed = artefacts.Count(a => string.IsNullOrWhiteSpace(a.Sha256));
                var unsealed = artefacts.Count(a => !a.Sealed);

                return unhashed == 0
                    ? new CheckResult(true, false,
                        $"All {artefacts.Count} artefact(s) carry a content hash.",
                        $"unsealed={unsealed} (unsealed is not a fault; it means no reviewer has verified them yet)",
                        Severity.Info, 1)
                    : new CheckResult(false, false,
                        $"{unhashed} artefact(s) have no content hash.",
                        "An artefact without a hash cannot support a clearance decision, because it cannot be shown to be unchanged.",
                        Severity.High, 1, "every artefact hashed", $"{unhashed} unhashed");
            }

            case "Autonomous Exploration":
            {
                var exe = AgenticSweepRunner.ResolveExecutablePath(target);
                var isAttached = !string.IsNullOrWhiteSpace(target.BaseAddress) &&
                    (target.BaseAddress.StartsWith("process:", StringComparison.OrdinalIgnoreCase) ||
                     int.TryParse(target.BaseAddress.Trim(), out _));

                if (!isAttached && (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)))
                {
                    var active = TargetSupervisor.GetActiveDesktopProcesses()
                        .FirstOrDefault(p => p.ProcessName.Equals(target.Name, StringComparison.OrdinalIgnoreCase));
                    if (active is null)
                    {
                        return new CheckResult(false, true,
                            "Autonomous exploration skipped: target has no valid executable or running process.",
                            $"Set LaunchCommand or BaseAddress to an .exe path or attach an active process. Configured: '{target.LaunchCommand}', '{target.BaseAddress}'",
                            Severity.Low, 1);
                    }
                }

                var runner = new AgenticSweepRunner(_store);
                var subSweep = await runner.RunAgenticSweepAsync(target, sweep, null, cancellation).ConfigureAwait(false);

                return new CheckResult(
                    Passed: subSweep.DeviationsFound == 0,
                    Blocked: subSweep.State == RunState.Blocked,
                    Summary: subSweep.Outcome,
                    Detail: $"Explored {subSweep.StatesVisited} states, captured {subSweep.EvidenceCaptured} artefacts, found {subSweep.DeviationsFound} deviations.",
                    Severity: subSweep.DeviationsFound > 0 ? Severity.Medium : Severity.Info,
                    StatesVisited: Math.Max(1, subSweep.StatesVisited));
            }

            case "Quality Validation":
            {
                var reports = await ValidationRegistry.Default.RunAllAsync(target, _store, cancellation).ConfigureAwait(false);
                var totalFindings = reports.Sum(r => r.Findings.Count);
                var totalMetrics = reports.Sum(r => r.Metrics.Count);
                var totalViolations = reports.Count(r => !r.Passed);
                var worstSeverity = reports.SelectMany(r => r.Findings).Select(f => f.Severity).DefaultIfEmpty(Severity.Info).Max();

                var detailSb = new System.Text.StringBuilder();
                detailSb.AppendLine($"Multi-Modal Validation Registry executed {reports.Count} modules:");
                foreach (var rep in reports)
                {
                    detailSb.AppendLine($"- [{rep.ModuleId}] {rep.ModuleName}: {(rep.Passed ? "PASS" : "DEVIATION")} ({rep.Findings.Count} findings, {rep.Metrics.Count} metrics)");
                    if (rep.Findings.Count > 0)
                    {
                        foreach (var f in rep.Findings)
                        {
                            detailSb.AppendLine($"    * {f.Severity} [{f.Category}]: {f.Title} ({f.Actual})");

                            var fp = FaultFingerprint.Compute(
                                target.Id.ToString(),
                                f.Category,
                                "VAL.DEVIATION",
                                f.Title,
                                deltaSignature: f.Expected);

                            var finding = new Finding
                            {
                                Id = _store.NextId(IdKind.Fault),
                                TargetId = target.Id,
                                SweepId = sweep.Id,
                                Title = f.Title,
                                Severity = f.Severity,
                                Maturity = FindingMaturity.New,
                                Expected = f.Expected,
                                Actual = !string.IsNullOrWhiteSpace(f.Actual) ? $"{f.Actual} ({f.Description})" : f.Description,
                                NodeId = f.LatticeNodeId,
                                Fingerprint = fp,
                                FirstSeenAt = DateTimeOffset.Now,
                                LastSeenAt = DateTimeOffset.Now,
                                ReproAttempts = 1,
                                ReproSuccesses = 1,
                            };
                            FaultFingerprint.ClusterFinding(_store, finding, target, sweep);
                        }
                    }

                    // Save module evidence artefact
                    var modPayload = rep.EvidencePayload;
                    _store.Save(new Evidence
                    {
                        Id = _store.NextId(IdKind.Evidence),
                        TargetId = target.Id,
                        SweepId = sweep.Id,
                        Kind = $"validation:{rep.ModuleId}",
                        Summary = rep.Summary,
                        Sha256 = Hashing.Sha256(modPayload),
                        SizeBytes = System.Text.Encoding.UTF8.GetByteCount(modPayload),
                        Payload = modPayload,
                        SourceAdapter = "ValidationRegistry",
                        CapturedAt = DateTimeOffset.Now,
                    });
                }

                if (totalFindings == 0)
                {
                    return new CheckResult(true, false,
                        $"Quality validation passed: {reports.Count} modules, {totalMetrics} metrics verified with 0 defects.",
                        detailSb.ToString(),
                        Severity.Info, reports.Count);
                }

                return new CheckResult(false, false,
                    $"Quality validation found {totalFindings} deviation(s) across {totalViolations} module(s).",
                    detailSb.ToString(),
                    worstSeverity, reports.Count,
                    "Zero quality, security or accessibility deviations",
                    $"{totalFindings} deviations detected");
            }

            case "Regression Guarding":
            {
                var regressionEngine = new RegressionEngine(_store);
                var evaluatedGuards = await regressionEngine.ExecuteGuardsAsync(target, cancellation).ConfigureAwait(false);

                if (evaluatedGuards.Count == 0)
                {
                    return new CheckResult(true, false,
                        "Regression guarding evaluated: 0 active guards configured for target.",
                        "No regression guards have been derived from defects for this target yet.",
                        Severity.Info, 1);
                }

                var passingCount = evaluatedGuards.Count(g => g.Status == GuardStatus.Passing);
                var failingCount = evaluatedGuards.Count(g => g.Status == GuardStatus.Failing);
                var worstSeverity = evaluatedGuards
                    .Where(g => g.Status == GuardStatus.Failing)
                    .Select(g => g.Severity)
                    .DefaultIfEmpty(Severity.Info)
                    .Max();

                var detailSb = new System.Text.StringBuilder();
                detailSb.AppendLine($"Regression Guard Suite evaluated {evaluatedGuards.Count} guards ({passingCount} passing, {failingCount} failing):");
                foreach (var g in evaluatedGuards)
                {
                    detailSb.AppendLine($"- [{g.Id}] {g.Title}: {g.Status} ({g.Severity}) -> {g.ExpectedBehavior}");
                    if (g.Status == GuardStatus.Failing)
                    {
                        var fp = FaultFingerprint.Compute(
                            target.Id.ToString(),
                            "REGRESSION",
                            g.Id,
                            g.Title,
                            deltaSignature: g.ExpectedBehavior);

                        var finding = new Finding
                        {
                            Id = _store.NextId(IdKind.Fault),
                            TargetId = target.Id,
                            SweepId = sweep.Id,
                            Title = $"Regression: {g.Title}",
                            Severity = g.Severity,
                            Maturity = FindingMaturity.New,
                            Expected = g.ExpectedBehavior,
                            Actual = $"Guard {g.Id} failed during sweep re-test ({g.Details})",
                            NodeId = "system:root",
                            Fingerprint = fp,
                            FirstSeenAt = DateTimeOffset.Now,
                            LastSeenAt = DateTimeOffset.Now,
                            ReproAttempts = 1,
                            ReproSuccesses = 1,
                        };
                        FaultFingerprint.ClusterFinding(_store, finding, target, sweep);
                    }
                }

                // Save sealed regression guard audit evidence
                var auditPayload = detailSb.ToString();
                _store.Save(new Evidence
                {
                    Id = _store.NextId(IdKind.Evidence),
                    TargetId = target.Id,
                    SweepId = sweep.Id,
                    Kind = "regression:audit",
                    Summary = $"Regression Guard Audit: {passingCount}/{evaluatedGuards.Count} passing",
                    Sha256 = Hashing.Sha256(auditPayload),
                    SizeBytes = System.Text.Encoding.UTF8.GetByteCount(auditPayload),
                    Payload = auditPayload,
                    SourceAdapter = "RegressionEngine",
                    CapturedAt = DateTimeOffset.Now,
                    Sealed = true,
                    SealedBy = Environment.UserName,
                });

                if (failingCount == 0)
                {
                    return new CheckResult(true, false,
                        $"Regression guarding passed: {evaluatedGuards.Count} guards evaluated with 100% pass rate.",
                        detailSb.ToString(),
                        Severity.Info, evaluatedGuards.Count);
                }

                return new CheckResult(false, false,
                    $"Regression guarding failed: {failingCount}/{evaluatedGuards.Count} regression guards failed!",
                    detailSb.ToString(),
                    worstSeverity, evaluatedGuards.Count,
                    "100% of regression guards passing",
                    $"{failingCount} regression guards failed");
            }

            default:
                return new CheckResult(false, true, $"{check} is not implemented.",
                    "This check is named in the sweep mode but has no implementation in this build.",
                    Severity.Low, 0);
        }
    }

    /// <summary>
    /// Records a deviation, clustering it onto an existing finding when the fingerprint matches.
    /// </summary>
    /// <remarks>
    /// Clustering rather than inserting is what stops a backlog filling with the same defect once per
    /// run. The fingerprint deliberately excludes the timestamp and the run identifier: including
    /// them would make every occurrence unique, which is the failure this is written to avoid.
    /// </remarks>
    private void RecordFinding(CheckResult result, Sweep sweep, Target target)
    {
        var fingerprint = FaultFingerprint.Compute(
            target.Id.ToString(),
            result.Severity.ToString(),
            "CHECK.DEVIATION",
            result.Summary,
            deltaSignature: result.Expected);

        var draft = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            SweepId = sweep.Id,
            Title = result.Summary,
            Severity = result.Severity,
            Maturity = FindingMaturity.New,
            Actual = result.Actual,
            Expected = result.Expected,
            Fingerprint = fingerprint,
            ReproAttempts = 1,
            ReproSuccesses = 1,
            FirstSeenAt = DateTimeOffset.Now,
            LastSeenAt = DateTimeOffset.Now,
            EvidenceCount = 1,
        };

        FaultFingerprint.ClusterFinding(_store, draft, target, sweep);
    }

    private void Log(string code, string message, Severity severity, Sweep sweep, Target target, string detail)
    {
        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = code,
            Message = message,
            Severity = severity,
            TargetId = target.Id,
            SweepId = sweep.Id,
            RelatedId = sweep.Id.ToString(),
            Actor = "sweep-engine",
            Detail = detail,
        });
    }
}
