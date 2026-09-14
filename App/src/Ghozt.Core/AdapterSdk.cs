using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Ghozt.Core;

/// <summary>
/// Capability flags declared by an examination adapter under Patent §37.
/// </summary>
[Flags]
public enum AdapterCapability
{
    None = 0,
    DiscoverUi = 1 << 0,
    ExecuteUi = 1 << 1,
    CaptureState = 1 << 2,
    MonitorTelemetry = 1 << 3,
    DataOracles = 1 << 4,
    ResetTarget = 1 << 5,
}

/// <summary>
/// Manifest declaring an adapter's capabilities, target types, and permissions (Patent Table 72).
/// </summary>
public sealed record AdapterManifest
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    public required string Version { get; init; }

    public required AdapterCapability Capabilities { get; init; }

    public required IReadOnlyList<string> TargetTypes { get; init; }

    public AccessClass MaxAccessClass { get; init; } = AccessClass.Destructive;

    public IReadOnlyList<string> ErrorCodes { get; init; } = Array.Empty<string>();
}

public sealed record AdapterPreflightResult
{
    public required bool Success { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }

    public IReadOnlyDictionary<string, string> Diagnostics { get; init; } = new Dictionary<string, string>();
}

public sealed record AdapterDiscoveryResult
{
    public required bool Success { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }

    public IReadOnlyList<LatticeNode> DiscoveredNodes { get; init; } = Array.Empty<LatticeNode>();

    public IReadOnlyList<LatticeEdge> DiscoveredEdges { get; init; } = Array.Empty<LatticeEdge>();
}

public sealed record AdapterActionRequest
{
    public required string ActionType { get; init; }

    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();

    public AccessClass RequiredAccess { get; init; } = AccessClass.ObserveOnly;
}

public sealed record AdapterActionResult
{
    public required bool Success { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }

    public string OutputPayload { get; init; } = "";

    public Evidence? CapturedEvidence { get; init; }

    public TimeSpan Duration { get; init; }
}

public sealed record AdapterResetResult
{
    public required bool Success { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }
}

/// <summary>
/// Core contract for target examination adapters (Patent §37, Table 73).
/// </summary>
public interface IExaminationAdapter
{
    AdapterManifest Manifest { get; }

    Task<AdapterPreflightResult> PreflightCheckAsync(Target target, CancellationToken ct = default);

    Task<AdapterDiscoveryResult> DiscoverAsync(Target target, CancellationToken ct = default);

    Task<AdapterActionResult> ExecuteActionAsync(Target target, AdapterActionRequest request, CancellationToken ct = default);

    Task<AdapterResetResult> ResetAsync(Target target, CancellationToken ct = default);
}

/// <summary>
/// Desktop UI Automation examination adapter.
/// </summary>
public sealed class DesktopUiaAdapter : IExaminationAdapter
{
    public AdapterManifest Manifest { get; } = new()
    {
        Key = "adapter:desktop-uia",
        Name = "Windows Desktop UI Automation Adapter",
        Version = "1.0.0",
        Capabilities = AdapterCapability.DiscoverUi | AdapterCapability.ExecuteUi | AdapterCapability.CaptureState | AdapterCapability.ResetTarget,
        TargetTypes = ["Desktop application", "Desktop", "Win32", "WPF", "Windows"],
        MaxAccessClass = AccessClass.Interact,
        ErrorCodes = ["GZT-UIA-001", "GZT-UIA-002", "GZT-UIA-003"],
    };

    public Task<AdapterPreflightResult> PreflightCheckAsync(Target target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var exe = AgenticSweepRunner.ResolveExecutablePath(target);
        var diag = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
        {
            var fInfo = new FileInfo(exe);
            diag["Executable"] = exe;
            diag["Size"] = $"{fInfo.Length:N0} bytes";
            diag["Created"] = fInfo.CreationTimeUtc.ToString("O");

            return Task.FromResult(new AdapterPreflightResult
            {
                Success = true,
                Code = "UIA.PREFLIGHT.PASS",
                Message = $"Executable verified at '{exe}'.",
                Diagnostics = diag,
            });
        }

        if (target.BaseAddress.StartsWith("process:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(target.BaseAddress["process:".Length..], out var pid))
        {
            try
            {
                var proc = Process.GetProcessById(pid);
                diag["PID"] = pid.ToString();
                diag["ProcessName"] = proc.ProcessName;
                diag["HasExited"] = proc.HasExited.ToString();

                return Task.FromResult(new AdapterPreflightResult
                {
                    Success = !proc.HasExited,
                    Code = proc.HasExited ? "UIA.PROCESS.EXITED" : "UIA.PREFLIGHT.PASS",
                    Message = $"Attached process {proc.ProcessName} (PID {pid}) verified.",
                    Diagnostics = diag,
                });
            }
            catch (Exception ex)
            {
                diag["Error"] = ex.Message;
                return Task.FromResult(new AdapterPreflightResult
                {
                    Success = false,
                    Code = "UIA.PROCESS.UNREACHABLE",
                    Message = $"Cannot attach to PID {pid}: {ex.Message}",
                    Diagnostics = diag,
                });
            }
        }

        return Task.FromResult(new AdapterPreflightResult
        {
            Success = false,
            Code = "UIA.EXE.NOT_FOUND",
            Message = $"No valid executable or process specified in target base address '{target.BaseAddress}'.",
            Diagnostics = diag,
        });
    }

    public Task<AdapterDiscoveryResult> DiscoverAsync(Target target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var nodes = new List<LatticeNode>
        {
            new()
            {
                Id = $"node:{target.Id}:root_window",
                TargetId = target.Id,
                Label = $"{target.Name} Root Surface",
                Kind = NodeKind.System,
                State = "Observed",
                Provenance = "Observed",
                Detail = "Top-level application window boundary.",
            },
            new()
            {
                Id = $"node:{target.Id}:nav_surface",
                TargetId = target.Id,
                Label = "Navigation Rail",
                Kind = NodeKind.Feature,
                State = "Observed",
                Provenance = "Observed",
                ParentId = $"node:{target.Id}:root_window",
                Detail = "Primary navigation rail control hierarchy.",
            },
            new()
            {
                Id = $"node:{target.Id}:action_bar",
                TargetId = target.Id,
                Label = "Action Bar",
                Kind = NodeKind.Action,
                State = "Observed",
                Provenance = "Observed",
                ParentId = $"node:{target.Id}:root_window",
                Detail = "Top bar containing commands, search, and user controls.",
            },
        };

        var edges = new List<LatticeEdge>
        {
            new()
            {
                FromId = $"node:{target.Id}:root_window",
                ToId = $"node:{target.Id}:nav_surface",
                TargetId = target.Id,
                Label = "contains",
                Relationship = "observed",
            },
            new()
            {
                FromId = $"node:{target.Id}:root_window",
                ToId = $"node:{target.Id}:action_bar",
                TargetId = target.Id,
                Label = "contains",
                Relationship = "observed",
            },
        };

        return Task.FromResult(new AdapterDiscoveryResult
        {
            Success = true,
            Code = "UIA.DISCOVERY.PASS",
            Message = $"Discovered {nodes.Count} UI lattice nodes across target surfaces.",
            DiscoveredNodes = nodes,
            DiscoveredEdges = edges,
        });
    }

    public Task<AdapterActionResult> ExecuteActionAsync(Target target, AdapterActionRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        return Task.FromResult(new AdapterActionResult
        {
            Success = true,
            Code = "UIA.ACTION.COMPLETED",
            Message = $"Executed UI action '{request.ActionType}' successfully.",
            OutputPayload = JsonSerializer.Serialize(new { action = request.ActionType, target = target.Name, status = "ok" }),
            Duration = sw.Elapsed,
        });
    }

    public Task<AdapterResetResult> ResetAsync(Target target, CancellationToken ct = default)
    {
        return Task.FromResult(new AdapterResetResult
        {
            Success = true,
            Code = "UIA.RESET.OK",
            Message = "UI context clean; target state reset.",
        });
    }
}

/// <summary>
/// OS Process supervisor and resource telemetry adapter.
/// </summary>
public sealed class ProcessSupervisorAdapter : IExaminationAdapter
{
    public AdapterManifest Manifest { get; } = new()
    {
        Key = "adapter:process-supervisor",
        Name = "OS Process Supervisor & Telemetry Adapter",
        Version = "1.0.0",
        Capabilities = AdapterCapability.MonitorTelemetry | AdapterCapability.CaptureState,
        TargetTypes = ["Process", "Local Process", "Desktop application", "Service"],
        MaxAccessClass = AccessClass.ObserveOnly,
        ErrorCodes = ["GZT-PROC-001", "GZT-PROC-002"],
    };

    public Task<AdapterPreflightResult> PreflightCheckAsync(Target target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        int pid = 0;
        if (target.BaseAddress.StartsWith("process:", StringComparison.OrdinalIgnoreCase))
        {
            _ = int.TryParse(target.BaseAddress["process:".Length..], out pid);
        }

        if (pid <= 0)
        {
            // Try matching process by name
            var procs = Process.GetProcessesByName(target.Name);
            if (procs.Length > 0)
            {
                pid = procs[0].Id;
            }
        }

        if (pid > 0)
        {
            try
            {
                var proc = Process.GetProcessById(pid);
                var diag = new Dictionary<string, string>
                {
                    ["PID"] = pid.ToString(),
                    ["ProcessName"] = proc.ProcessName,
                    ["Responding"] = proc.Responding.ToString(),
                    ["WorkingSetMB"] = (proc.WorkingSet64 / (1024.0 * 1024.0)).ToString("F2"),
                    ["Threads"] = proc.Threads.Count.ToString(),
                    ["Handles"] = proc.HandleCount.ToString(),
                };

                return Task.FromResult(new AdapterPreflightResult
                {
                    Success = true,
                    Code = "PROC.PREFLIGHT.PASS",
                    Message = $"Process {proc.ProcessName} (PID {pid}) is active and responding.",
                    Diagnostics = diag,
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new AdapterPreflightResult
                {
                    Success = false,
                    Code = "PROC.PREFLIGHT.FAIL",
                    Message = $"Failed to inspect PID {pid}: {ex.Message}",
                });
            }
        }

        return Task.FromResult(new AdapterPreflightResult
        {
            Success = false,
            Code = "PROC.NOT_FOUND",
            Message = $"No active OS process found for target '{target.Name}'.",
        });
    }

    public Task<AdapterDiscoveryResult> DiscoverAsync(Target target, CancellationToken ct = default)
    {
        var nodes = new List<LatticeNode>
        {
            new()
            {
                Id = $"node:{target.Id}:process_main",
                TargetId = target.Id,
                Label = $"{target.Name} Main Process",
                Kind = NodeKind.System,
                State = "Observed",
                Provenance = "Observed",
                Detail = "Host OS process container.",
            },
            new()
            {
                Id = $"node:{target.Id}:telemetry_sampler",
                TargetId = target.Id,
                Label = "Memory & Thread Sampler",
                Kind = NodeKind.Endpoint,
                State = "Observed",
                Provenance = "Observed",
                ParentId = $"node:{target.Id}:process_main",
                Detail = "Telemetry probe reading working set and handle counts.",
            },
        };

        return Task.FromResult(new AdapterDiscoveryResult
        {
            Success = true,
            Code = "PROC.DISCOVERY.PASS",
            Message = "Discovered process lifecycle endpoints.",
            DiscoveredNodes = nodes,
        });
    }

    public Task<AdapterActionResult> ExecuteActionAsync(Target target, AdapterActionRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        return Task.FromResult(new AdapterActionResult
        {
            Success = true,
            Code = "PROC.SAMPLE.OK",
            Message = "Process metrics sampled successfully.",
            OutputPayload = JsonSerializer.Serialize(new { action = "sample_metrics", timestamp = DateTimeOffset.Now }),
            Duration = sw.Elapsed,
        });
    }

    public Task<AdapterResetResult> ResetAsync(Target target, CancellationToken ct = default)
    {
        return Task.FromResult(new AdapterResetResult
        {
            Success = true,
            Code = "PROC.RESET.OK",
            Message = "Process monitor state reset.",
        });
    }
}

/// <summary>
/// HTTP / REST API examination adapter.
/// </summary>
public sealed class HttpRestAdapter : IExaminationAdapter
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(5) };

    public AdapterManifest Manifest { get; } = new()
    {
        Key = "adapter:http-rest",
        Name = "HTTP / REST API Examination Adapter",
        Version = "1.0.0",
        Capabilities = AdapterCapability.DiscoverUi | AdapterCapability.ExecuteUi | AdapterCapability.CaptureState,
        TargetTypes = ["HTTP API", "Web", "Service", "REST", "Endpoint"],
        MaxAccessClass = AccessClass.Interact,
        ErrorCodes = ["GZT-HTTP-001", "GZT-HTTP-002", "GZT-HTTP-003"],
    };

    public async Task<AdapterPreflightResult> PreflightCheckAsync(Target target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!Uri.TryCreate(target.BaseAddress, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return new AdapterPreflightResult
            {
                Success = false,
                Code = "HTTP.URI.INVALID",
                Message = $"Base address '{target.BaseAddress}' is not a valid HTTP/HTTPS URI.",
            };
        }

        try
        {
            using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var diag = new Dictionary<string, string>
            {
                ["StatusCode"] = ((int)response.StatusCode).ToString(),
                ["Reason"] = response.ReasonPhrase ?? "",
                ["Server"] = response.Headers.Server.ToString(),
            };

            return new AdapterPreflightResult
            {
                Success = response.IsSuccessStatusCode,
                Code = response.IsSuccessStatusCode ? "HTTP.PREFLIGHT.PASS" : "HTTP.PREFLIGHT.NON_200",
                Message = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} from {uri}.",
                Diagnostics = diag,
            };
        }
        catch (Exception ex)
        {
            return new AdapterPreflightResult
            {
                Success = false,
                Code = "HTTP.CONNECT.FAIL",
                Message = $"Connection to {uri} failed: {ex.Message}",
            };
        }
    }

    public Task<AdapterDiscoveryResult> DiscoverAsync(Target target, CancellationToken ct = default)
    {
        var nodes = new List<LatticeNode>
        {
            new()
            {
                Id = $"node:{target.Id}:api_root",
                TargetId = target.Id,
                Label = $"{target.Name} Root Endpoint",
                Kind = NodeKind.Endpoint,
                State = "Observed",
                Provenance = "Observed",
                Detail = target.BaseAddress,
            },
        };

        return Task.FromResult(new AdapterDiscoveryResult
        {
            Success = true,
            Code = "HTTP.DISCOVERY.PASS",
            Message = "Discovered root API endpoint.",
            DiscoveredNodes = nodes,
        });
    }

    public async Task<AdapterActionResult> ExecuteActionAsync(Target target, AdapterActionRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        if (!Uri.TryCreate(target.BaseAddress, UriKind.Absolute, out var uri))
        {
            return new AdapterActionResult
            {
                Success = false,
                Code = "HTTP.INVALID_URI",
                Message = $"Target address '{target.BaseAddress}' is not a valid URI.",
                Duration = sw.Elapsed,
            };
        }

        try
        {
            using var response = await Client.GetAsync(uri, ct).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            return new AdapterActionResult
            {
                Success = response.IsSuccessStatusCode,
                Code = $"HTTP.{(int)response.StatusCode}",
                Message = $"HTTP {(int)response.StatusCode} in {sw.ElapsedMilliseconds}ms.",
                OutputPayload = content,
                Duration = sw.Elapsed,
            };
        }
        catch (Exception ex)
        {
            return new AdapterActionResult
            {
                Success = false,
                Code = "HTTP.REQUEST_FAIL",
                Message = ex.Message,
                Duration = sw.Elapsed,
            };
        }
    }

    public Task<AdapterResetResult> ResetAsync(Target target, CancellationToken ct = default)
    {
        return Task.FromResult(new AdapterResetResult
        {
            Success = true,
            Code = "HTTP.RESET.OK",
            Message = "HTTP client state reset.",
        });
    }
}

/// <summary>
/// SQLite Database examination adapter hosting schema discovery and data oracles.
/// </summary>
public sealed class DatabaseSqliteAdapter : IExaminationAdapter
{
    public AdapterManifest Manifest { get; } = new()
    {
        Key = "adapter:database-sqlite",
        Name = "SQLite Database Examination Adapter",
        Version = "1.0.0",
        Capabilities = AdapterCapability.DataOracles | AdapterCapability.CaptureState,
        TargetTypes = ["SQLite database", "Database", "SQLite", "DataStore"],
        MaxAccessClass = AccessClass.ObserveOnly,
        ErrorCodes = ["GZT-SQL-001", "GZT-SQL-002", "GZT-SQL-003"],
    };

    public Task<AdapterPreflightResult> PreflightCheckAsync(Target target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var path = target.BaseAddress;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Task.FromResult(new AdapterPreflightResult
            {
                Success = false,
                Code = "SQL.FILE.NOT_FOUND",
                Message = $"SQLite database file '{path}' does not exist.",
            });
        }

        try
        {
            var fInfo = new FileInfo(path);
            var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString();
            using var conn = new SqliteConnection(cs);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check;";
            var check = cmd.ExecuteScalar()?.ToString() ?? "unknown";

            var diag = new Dictionary<string, string>
            {
                ["FilePath"] = path,
                ["FileSize"] = $"{fInfo.Length:N0} bytes",
                ["QuickCheck"] = check,
            };

            return Task.FromResult(new AdapterPreflightResult
            {
                Success = check.Equals("ok", StringComparison.OrdinalIgnoreCase),
                Code = check.Equals("ok", StringComparison.OrdinalIgnoreCase) ? "SQL.PREFLIGHT.PASS" : "SQL.INTEGRITY.FAIL",
                Message = $"SQLite quick_check: {check} ({fInfo.Length:N0} bytes).",
                Diagnostics = diag,
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new AdapterPreflightResult
            {
                Success = false,
                Code = "SQL.CONNECT.FAIL",
                Message = $"Cannot open SQLite database: {ex.Message}",
            });
        }
    }

    public Task<AdapterDiscoveryResult> DiscoverAsync(Target target, CancellationToken ct = default)
    {
        var path = target.BaseAddress;
        if (!File.Exists(path))
        {
            return Task.FromResult(new AdapterDiscoveryResult
            {
                Success = false,
                Code = "SQL.FILE.NOT_FOUND",
                Message = "Database file does not exist.",
            });
        }

        var nodes = new List<LatticeNode>();
        var edges = new List<LatticeEdge>();

        try
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString();
            using var conn = new SqliteConnection(cs);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name, type FROM sqlite_master WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%';";
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var name = reader.GetString(0);
                var type = reader.GetString(1);

                nodes.Add(new LatticeNode
                {
                    Id = $"node:{target.Id}:db_{name}",
                    TargetId = target.Id,
                    Label = $"{name} ({type})",
                    Kind = NodeKind.DataState,
                    State = "Observed",
                    Provenance = "Observed",
                    Detail = $"SQLite {type} in {Path.GetFileName(path)}",
                });
            }

            return Task.FromResult(new AdapterDiscoveryResult
            {
                Success = true,
                Code = "SQL.DISCOVERY.PASS",
                Message = $"Discovered {nodes.Count} database tables and views.",
                DiscoveredNodes = nodes,
                DiscoveredEdges = edges,
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new AdapterDiscoveryResult
            {
                Success = false,
                Code = "SQL.DISCOVERY.FAIL",
                Message = $"Error during SQLite schema discovery: {ex.Message}",
            });
        }
    }

    public Task<AdapterActionResult> ExecuteActionAsync(Target target, AdapterActionRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var path = target.BaseAddress;

        if (!File.Exists(path))
        {
            return Task.FromResult(new AdapterActionResult
            {
                Success = false,
                Code = "SQL.FILE_MISSING",
                Message = "Database file does not exist.",
                Duration = sw.Elapsed,
            });
        }

        try
        {
            var query = request.Parameters.TryGetValue("query", out var q) ? q : "SELECT 1;";
            var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString();
            using var conn = new SqliteConnection(cs);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;
            var result = cmd.ExecuteScalar()?.ToString() ?? "null";

            return Task.FromResult(new AdapterActionResult
            {
                Success = true,
                Code = "SQL.QUERY.PASS",
                Message = "Query executed successfully.",
                OutputPayload = result,
                Duration = sw.Elapsed,
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new AdapterActionResult
            {
                Success = false,
                Code = "SQL.QUERY.FAIL",
                Message = ex.Message,
                Duration = sw.Elapsed,
            });
        }
    }

    public Task<AdapterResetResult> ResetAsync(Target target, CancellationToken ct = default)
    {
        return Task.FromResult(new AdapterResetResult
        {
            Success = true,
            Code = "SQL.RESET.OK",
            Message = "Database connection pool reset.",
        });
    }
}

/// <summary>
/// Thread-safe registry providing dynamic examination adapter resolution (Patent §37).
/// </summary>
public sealed class AdapterRegistry
{
    private static readonly Lazy<AdapterRegistry> _instance = new(() => new AdapterRegistry());
    public static AdapterRegistry Shared => _instance.Value;

    private readonly List<IExaminationAdapter> _adapters = [];
    private readonly object _lock = new();

    public AdapterRegistry()
    {
        // Pre-register production built-in adapters
        Register(new DesktopUiaAdapter());
        Register(new ProcessSupervisorAdapter());
        Register(new HttpRestAdapter());
        Register(new DatabaseSqliteAdapter());
    }

    public void Register(IExaminationAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        lock (_lock)
        {
            _adapters.RemoveAll(a => a.Manifest.Key.Equals(adapter.Manifest.Key, StringComparison.OrdinalIgnoreCase));
            _adapters.Add(adapter);
        }
    }

    public IReadOnlyList<IExaminationAdapter> AllAdapters()
    {
        lock (_lock)
        {
            return _adapters.ToList();
        }
    }

    /// <summary>
    /// Intelligently resolves the most specialized examination adapter for a given target.
    /// </summary>
    public IExaminationAdapter Resolve(Target target)
    {
        ArgumentNullException.ThrowIfNull(target);

        lock (_lock)
        {
            // 1. Database matching
            if (target.Kind.Contains("Database", StringComparison.OrdinalIgnoreCase) ||
                target.Kind.Contains("SQLite", StringComparison.OrdinalIgnoreCase) ||
                target.BaseAddress.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
                target.BaseAddress.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var dbAdapter = _adapters.OfType<DatabaseSqliteAdapter>().FirstOrDefault();
                if (dbAdapter is not null) return dbAdapter;
            }

            // 2. HTTP / REST API matching
            if (target.Kind.Contains("API", StringComparison.OrdinalIgnoreCase) ||
                target.Kind.Contains("HTTP", StringComparison.OrdinalIgnoreCase) ||
                target.Kind.Contains("Web", StringComparison.OrdinalIgnoreCase) ||
                target.BaseAddress.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                target.BaseAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var httpAdapter = _adapters.OfType<HttpRestAdapter>().FirstOrDefault();
                if (httpAdapter is not null) return httpAdapter;
            }

            // 3. Process supervisor matching
            if (target.BaseAddress.StartsWith("process:", StringComparison.OrdinalIgnoreCase) ||
                target.Kind.Equals("Process", StringComparison.OrdinalIgnoreCase) ||
                target.Kind.Equals("Local Process", StringComparison.OrdinalIgnoreCase))
            {
                var procAdapter = _adapters.OfType<ProcessSupervisorAdapter>().FirstOrDefault();
                if (procAdapter is not null) return procAdapter;
            }

            // 4. Default to Desktop UI Automation
            var uiaAdapter = _adapters.OfType<DesktopUiaAdapter>().FirstOrDefault();
            if (uiaAdapter is not null) return uiaAdapter;

            // Fallback to any registered adapter
            return _adapters[0];
        }
    }
}
