using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Ghozt.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Ghozt.Tests;

public sealed class RealExecutionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempDbPath;
    private readonly GhoztStore _store;

    public RealExecutionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ghozt_real_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(_tempDir, "ghozt_test.db");
        _store = new GhoztStore(_tempDbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public async Task SqliteTarget_RealStructureAndIntegrityChecks_Pass()
    {
        // 1. Create a real sample SQLite database
        var sampleDbPath = Path.Combine(_tempDir, "sample_data.db");
        using (var conn = new SqliteConnection($"Data Source={sampleDbPath};"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT); CREATE TABLE audit_log (id INTEGER, action TEXT); INSERT INTO users VALUES (1, 'alice');";
            cmd.ExecuteNonQuery();
        }

        // 2. Register Target pointing to the SQLite database
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Sample SQLite Target",
            Kind = "Database",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            Owner = "test-runner",
            BaseAddress = sampleDbPath,
            BuildLabel = "1.0.0-test",
            Reachable = true,
        };
        _store.Save(target);

        var sweep = new Sweep
        {
            Id = _store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = "Full sweep against SQLite DB",
            Mode = "Full sweep",
            MaxStates = 20,
            MaxSeconds = 30,
            MaxDepth = 3,
        };

        var engine = new SweepEngine(_store);
        var completed = await engine.RunAsync(sweep, target, null, CancellationToken.None);

        Assert.Equal(RunState.Completed, completed.State);
        Assert.Equal(0, completed.DeviationsFound);

        // Verify evidence items generated
        var evidence = _store.EvidenceItems(target.Id);
        Assert.NotEmpty(evidence);

        var structureArtefact = evidence.FirstOrDefault(e => e.Kind == "check:structure");
        Assert.NotNull(structureArtefact);
        Assert.Contains("SQLite schema structure verified", structureArtefact.Summary);
        Assert.Contains("tables: 2", structureArtefact.Summary, StringComparison.OrdinalIgnoreCase);

        var integrityArtefact = evidence.FirstOrDefault(e => e.Kind == "check:integrity");
        Assert.NotNull(integrityArtefact);
        Assert.Contains("PRAGMA integrity_check = ok", integrityArtefact.Summary);
    }

    [Fact]
    public async Task DirectoryAndGitTarget_RealStructureAndIntegrityChecks_Pass()
    {
        // 1. Create a simulated git repo directory structure
        var repoDir = Path.Combine(_tempDir, "mock_repo");
        Directory.CreateDirectory(repoDir);
        File.WriteAllText(Path.Combine(repoDir, "App.cs"), "class App {}");
        File.WriteAllText(Path.Combine(repoDir, "Main.xaml"), "<Window />");

        var gitDir = Path.Combine(repoDir, ".git");
        Directory.CreateDirectory(gitDir);
        var headRef = "ref: refs/heads/feature/real-exec";
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), headRef);
        var headsDir = Path.Combine(gitDir, "refs", "heads", "feature");
        Directory.CreateDirectory(headsDir);
        var commitSha = "abcdef0123456789abcdef0123456789abcdef01";
        File.WriteAllText(Path.Combine(headsDir, "real-exec"), commitSha);

        // 2. Register Target pointing to the directory
        var target = new Target
        {
            Id = _store.NextId(IdKind.Target),
            Name = "Mock Git Repository",
            Kind = "Service",
            Environment = TargetEnvironment.Local,
            Access = AccessClass.ObserveOnly,
            Owner = "test-runner",
            BaseAddress = repoDir,
            RepositoryPath = repoDir,
            BuildLabel = "git-commit-test",
            Reachable = true,
        };
        _store.Save(target);

        var sweep = new Sweep
        {
            Id = _store.NextId(IdKind.Sweep),
            TargetId = target.Id,
            Name = "Sweep against Git repo",
            Mode = "Changed area",
            MaxStates = 20,
            MaxSeconds = 30,
            MaxDepth = 3,
        };

        var engine = new SweepEngine(_store);
        var completed = await engine.RunAsync(sweep, target, null, CancellationToken.None);

        Assert.Equal(RunState.Completed, completed.State);

        var evidence = _store.EvidenceItems(target.Id);
        var structEvt = evidence.FirstOrDefault(e => e.Kind == "check:structure");
        Assert.NotNull(structEvt);
        Assert.Contains("Git branch: feature/real-exec", structEvt.Summary);

        var integEvt = evidence.FirstOrDefault(e => e.Kind == "check:integrity");
        Assert.NotNull(integEvt);
        Assert.Contains("commit abcdef0123", integEvt.Summary);
    }

    [Fact]
    public void ProcessSupervisor_CanAttachToCurrentProcess()
    {
        using var supervisor = new TargetSupervisor();
        var currentPid = Environment.ProcessId;
        var attached = supervisor.Attach(currentPid);

        Assert.True(attached);
        Assert.True(supervisor.IsRunning);
        Assert.Equal(currentPid, supervisor.ProcessId);

        var activeProcesses = TargetSupervisor.GetActiveDesktopProcesses();
        Assert.NotNull(activeProcesses);
    }

    [Fact]
    public void StateMapWorkspace_HasZeroOvalActionNodes_MustBeRoundedRectangle()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ghozt.App", "Workspaces", "StateMapWorkspace.xaml.cs"));
        Assert.True(File.Exists(sourcePath), $"Could not find StateMapWorkspace.xaml.cs at {sourcePath}");

        var code = File.ReadAllText(sourcePath);

        // Assert no CornerRadius(999) exists in StateMapWorkspace
        Assert.DoesNotContain("new CornerRadius(999)", code);
        Assert.Contains("NodeKind.Action => new CornerRadius(8)", code);
    }

    [Fact]
    public void TargetsWorkspace_EliminatedSeedDemonstrationTarget_HasAttachAndRegisterSystem()
    {
        var xamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ghozt.App", "Workspaces", "TargetsWorkspace.xaml"));
        Assert.True(File.Exists(xamlPath), $"Could not find TargetsWorkspace.xaml at {xamlPath}");

        var xaml = File.ReadAllText(xamlPath);

        // Must not contain "Seed demonstration target"
        Assert.DoesNotContain("Seed demonstration target", xaml, StringComparison.OrdinalIgnoreCase);

        // Must contain "Attach Process" and "Register Local System"
        Assert.Contains("Attach Process", xaml);
        Assert.Contains("Register Local System", xaml);
    }
}
