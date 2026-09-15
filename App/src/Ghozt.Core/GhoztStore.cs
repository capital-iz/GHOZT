using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Ghozt.Core;

/// <summary>
/// The evidence plane: a real SQLite database holding targets, sweeps, the lattice, findings,
/// evidence and the event ledger.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a real store from the first commit rather than an in-memory stub. A tester whose own
/// findings vanish on restart cannot be used to certify anything, and "we will add persistence
/// later" is how a demo becomes a product with no record of truth.
/// </para>
/// <para>
/// Identifier allocation goes through the database, in a transaction, so two concurrent writers
/// cannot issue the same identifier. Sequences are per-kind, which is what makes an identifier read
/// as an ordinal within its own family.
/// </para>
/// </remarks>
public sealed class GhoztStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public GhoztStore(string databasePath)
    {
        DatabasePath = databasePath;

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString());

        _connection.Open();

        // WAL so a long read (a report export) does not block the sweep that is still writing.
        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA foreign_keys=ON;");
        Execute("PRAGMA busy_timeout=4000;");

        CreateSchema();
    }

    public string DatabasePath { get; }

    /// <summary>The default location: per-user application data, beside the rest of the suite.</summary>
    public static string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Capital-Iz",
        "Ghozt",
        "ghozt.db");

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private void CreateSchema()
    {
        Execute(
            """
            CREATE TABLE IF NOT EXISTS Sequences (
                Kind      TEXT PRIMARY KEY,
                NextValue INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Settings (
                Key   TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Targets (
                Id             TEXT PRIMARY KEY,
                Name           TEXT NOT NULL,
                Kind           TEXT NOT NULL,
                Environment    TEXT NOT NULL,
                Access         TEXT NOT NULL,
                Owner          TEXT NOT NULL DEFAULT '',
                RepositoryPath TEXT NOT NULL DEFAULT '',
                LaunchCommand  TEXT NOT NULL DEFAULT '',
                BaseAddress    TEXT NOT NULL DEFAULT '',
                BuildLabel     TEXT NOT NULL DEFAULT '',
                AuthorityNote  TEXT NOT NULL DEFAULT '',
                RegisteredAt   TEXT NOT NULL,
                LastSweepAt    TEXT NULL,
                Reachable      INTEGER NOT NULL DEFAULT 0,
                Notes          TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS Sweeps (
                Id               TEXT PRIMARY KEY,
                TargetId         TEXT NOT NULL,
                Name             TEXT NOT NULL,
                Mode             TEXT NOT NULL,
                State            TEXT NOT NULL,
                CreatedAt        TEXT NOT NULL,
                StartedAt        TEXT NULL,
                FinishedAt       TEXT NULL,
                MaxStates        INTEGER NOT NULL,
                MaxSeconds       INTEGER NOT NULL,
                MaxDepth         INTEGER NOT NULL,
                AllowDestructive INTEGER NOT NULL DEFAULT 0,
                StatesVisited    INTEGER NOT NULL DEFAULT 0,
                DeviationsFound  INTEGER NOT NULL DEFAULT 0,
                EvidenceCaptured INTEGER NOT NULL DEFAULT 0,
                Outcome          TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS LatticeNodes (
                Id            TEXT NOT NULL,
                TargetId      TEXT NOT NULL,
                Label         TEXT NOT NULL,
                Kind          TEXT NOT NULL,
                State         TEXT NOT NULL,
                Provenance    TEXT NOT NULL,
                ParentId      TEXT NOT NULL DEFAULT '',
                X             REAL NOT NULL DEFAULT 0,
                Y             REAL NOT NULL DEFAULT 0,
                VisitCount    INTEGER NOT NULL DEFAULT 0,
                Detail        TEXT NOT NULL DEFAULT '',
                EvidenceCount INTEGER NOT NULL DEFAULT 0,
                LastSeenAt    TEXT NULL,
                PRIMARY KEY (TargetId, Id)
            );

            CREATE TABLE IF NOT EXISTS LatticeEdges (
                TargetId       TEXT NOT NULL,
                FromId         TEXT NOT NULL,
                ToId           TEXT NOT NULL,
                Label          TEXT NOT NULL DEFAULT '',
                Relationship   TEXT NOT NULL DEFAULT 'observed',
                TraversalCount INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (TargetId, FromId, ToId, Relationship)
            );

            CREATE TABLE IF NOT EXISTS Findings (
                Id             TEXT PRIMARY KEY,
                TargetId       TEXT NOT NULL,
                SweepId        TEXT NULL,
                Title          TEXT NOT NULL,
                Severity       TEXT NOT NULL,
                Maturity       TEXT NOT NULL,
                Actual         TEXT NOT NULL DEFAULT '',
                Expected       TEXT NOT NULL DEFAULT '',
                Fingerprint    TEXT NOT NULL DEFAULT '',
                ReproAttempts  INTEGER NOT NULL DEFAULT 0,
                ReproSuccesses INTEGER NOT NULL DEFAULT 0,
                NodeId         TEXT NOT NULL DEFAULT '',
                Owner          TEXT NOT NULL DEFAULT '',
                FirstSeenAt    TEXT NOT NULL,
                LastSeenAt     TEXT NOT NULL,
                EvidenceCount  INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS Evidence (
                Id            TEXT PRIMARY KEY,
                TargetId      TEXT NOT NULL,
                Kind          TEXT NOT NULL,
                Summary       TEXT NOT NULL,
                Sha256        TEXT NOT NULL,
                SizeBytes     INTEGER NOT NULL DEFAULT 0,
                Payload       TEXT NOT NULL DEFAULT '',
                SourceAdapter TEXT NOT NULL DEFAULT '',
                FindingId     TEXT NULL,
                SweepId       TEXT NULL,
                CapturedAt    TEXT NOT NULL,
                Sealed        INTEGER NOT NULL DEFAULT 0,
                SealedBy      TEXT NOT NULL DEFAULT '',
                Redacted      INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS Events (
                Id        TEXT PRIMARY KEY,
                At        TEXT NOT NULL,
                Code      TEXT NOT NULL,
                Message   TEXT NOT NULL,
                Severity  TEXT NOT NULL,
                TargetId  TEXT NULL,
                SweepId   TEXT NULL,
                RelatedId TEXT NOT NULL DEFAULT '',
                Actor     TEXT NOT NULL DEFAULT '',
                Detail    TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS AuthorityGrants (
                GrantId               TEXT PRIMARY KEY,
                TargetId              TEXT NOT NULL,
                Actor                 TEXT NOT NULL,
                MaxLevel              INTEGER NOT NULL,
                AllowedActionsJson    TEXT NOT NULL,
                AllowedSensitivity    TEXT NOT NULL,
                IssuedAt              TEXT NOT NULL,
                ExpiresAt             TEXT NOT NULL,
                Approver              TEXT NOT NULL DEFAULT '',
                ScopeJustification    TEXT NOT NULL DEFAULT '',
                AuditSignature        TEXT NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS IX_Events_At       ON Events (At DESC);
            CREATE INDEX IF NOT EXISTS IX_Events_Sweep    ON Events (SweepId);
            CREATE INDEX IF NOT EXISTS IX_Findings_Target ON Findings (TargetId);
            CREATE INDEX IF NOT EXISTS IX_Evidence_Finding ON Evidence (FindingId);
            CREATE INDEX IF NOT EXISTS IX_Nodes_Target    ON LatticeNodes (TargetId);
            CREATE INDEX IF NOT EXISTS IX_Grants_Target   ON AuthorityGrants (TargetId);
            """);
    }

    // ---------------------------------------------------------------- identifiers

    /// <summary>
    /// Issues the next identifier of a kind.
    /// </summary>
    /// <remarks>
    /// In a transaction, and under a process-wide lock, because an identifier that two writers could
    /// both claim would silently overwrite one entity with another - and the overwritten one would be
    /// the evidence.
    /// </remarks>
    public GhoztId NextId(IdKind kind)
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO Sequences (Kind, NextValue) VALUES ($k, 2)
                ON CONFLICT(Kind) DO UPDATE SET NextValue = NextValue + 1
                RETURNING NextValue;
                """;
            command.Parameters.AddWithValue("$k", kind.ToString());

            var next = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            transaction.Commit();

            // The RETURNING value is the value AFTER the update, so the first insert (which writes 2)
            // hands out 1. Stated here because an off-by-one in identifier allocation is invisible
            // until two entities disagree about which is which.
            return GhoztId.Create(kind, next - 1);
        }
    }

    // ---------------------------------------------------------------- settings

    public string? GetSetting(string key)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = $k;";
        command.Parameters.AddWithValue("$k", key);
        return command.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Settings (Key, Value) VALUES ($k, $v)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$k", key);
        command.Parameters.AddWithValue("$v", value);
        command.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------- helpers

    private static string Iso(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseIso(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static DateTimeOffset? ParseIsoOrNull(object value) =>
        value is string s && !string.IsNullOrEmpty(s) ? ParseIso(s) : null;

    private static T Enum<T>(string value) where T : struct => System.Enum.Parse<T>(value, ignoreCase: true);

    // ---------------------------------------------------------------- targets

    public void Save(Target target)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Targets
              (Id, Name, Kind, Environment, Access, Owner, RepositoryPath, LaunchCommand, BaseAddress,
               BuildLabel, AuthorityNote, RegisteredAt, LastSweepAt, Reachable, Notes)
            VALUES
              ($id, $name, $kind, $env, $access, $owner, $repo, $launch, $base,
               $build, $auth, $reg, $last, $reach, $notes)
            ON CONFLICT(Id) DO UPDATE SET
              Name = excluded.Name, Kind = excluded.Kind, Environment = excluded.Environment,
              Access = excluded.Access, Owner = excluded.Owner, RepositoryPath = excluded.RepositoryPath,
              LaunchCommand = excluded.LaunchCommand, BaseAddress = excluded.BaseAddress,
              BuildLabel = excluded.BuildLabel, AuthorityNote = excluded.AuthorityNote,
              LastSweepAt = excluded.LastSweepAt, Reachable = excluded.Reachable, Notes = excluded.Notes;
            """;
        command.Parameters.AddWithValue("$id", target.Id.ToString());
        command.Parameters.AddWithValue("$name", target.Name);
        command.Parameters.AddWithValue("$kind", target.Kind);
        command.Parameters.AddWithValue("$env", target.Environment.ToString());
        command.Parameters.AddWithValue("$access", target.Access.ToString());
        command.Parameters.AddWithValue("$owner", target.Owner);
        command.Parameters.AddWithValue("$repo", target.RepositoryPath);
        command.Parameters.AddWithValue("$launch", target.LaunchCommand);
        command.Parameters.AddWithValue("$base", target.BaseAddress);
        command.Parameters.AddWithValue("$build", target.BuildLabel);
        command.Parameters.AddWithValue("$auth", target.AuthorityNote);
        command.Parameters.AddWithValue("$reg", Iso(target.RegisteredAt));
        command.Parameters.AddWithValue("$last", (object?)(target.LastSweepAt is null ? null : Iso(target.LastSweepAt.Value)) ?? DBNull.Value);
        command.Parameters.AddWithValue("$reach", target.Reachable ? 1 : 0);
        command.Parameters.AddWithValue("$notes", target.Notes);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<Target> Targets()
    {
        var list = new List<Target>();
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT * FROM Targets ORDER BY RegisteredAt DESC;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Target
            {
                Id = GhoztId.Parse(reader.GetString(reader.GetOrdinal("Id"))),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Kind = reader.GetString(reader.GetOrdinal("Kind")),
                Environment = Enum<TargetEnvironment>(reader.GetString(reader.GetOrdinal("Environment"))),
                Access = Enum<AccessClass>(reader.GetString(reader.GetOrdinal("Access"))),
                Owner = reader.GetString(reader.GetOrdinal("Owner")),
                RepositoryPath = reader.GetString(reader.GetOrdinal("RepositoryPath")),
                LaunchCommand = reader.GetString(reader.GetOrdinal("LaunchCommand")),
                BaseAddress = reader.GetString(reader.GetOrdinal("BaseAddress")),
                BuildLabel = reader.GetString(reader.GetOrdinal("BuildLabel")),
                AuthorityNote = reader.GetString(reader.GetOrdinal("AuthorityNote")),
                RegisteredAt = ParseIso(reader.GetString(reader.GetOrdinal("RegisteredAt"))),
                LastSweepAt = ParseIsoOrNull(reader.GetValue(reader.GetOrdinal("LastSweepAt"))),
                Reachable = reader.GetInt32(reader.GetOrdinal("Reachable")) != 0,
                Notes = reader.GetString(reader.GetOrdinal("Notes")),
            });
        }

        return list;
    }

    public void DeleteTarget(GhoztId id)
    {
        foreach (var sql in new[]
                 {
                     "DELETE FROM LatticeEdges WHERE TargetId = $id;",
                     "DELETE FROM LatticeNodes WHERE TargetId = $id;",
                     "DELETE FROM Evidence     WHERE TargetId = $id;",
                     "DELETE FROM Findings     WHERE TargetId = $id;",
                     "DELETE FROM Sweeps       WHERE TargetId = $id;",
                     "DELETE FROM Events       WHERE TargetId = $id;",
                     "DELETE FROM Targets      WHERE Id = $id;",
                 })
        {
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.ExecuteNonQuery();
        }
    }

    // ---------------------------------------------------------------- sweeps

    public void Save(Sweep sweep)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Sweeps
              (Id, TargetId, Name, Mode, State, CreatedAt, StartedAt, FinishedAt, MaxStates, MaxSeconds,
               MaxDepth, AllowDestructive, StatesVisited, DeviationsFound, EvidenceCaptured, Outcome)
            VALUES
              ($id, $target, $name, $mode, $state, $created, $started, $finished, $maxStates, $maxSeconds,
               $maxDepth, $destructive, $visited, $deviations, $evidence, $outcome)
            ON CONFLICT(Id) DO UPDATE SET
              State = excluded.State, StartedAt = excluded.StartedAt, FinishedAt = excluded.FinishedAt,
              StatesVisited = excluded.StatesVisited, DeviationsFound = excluded.DeviationsFound,
              EvidenceCaptured = excluded.EvidenceCaptured, Outcome = excluded.Outcome;
            """;
        command.Parameters.AddWithValue("$id", sweep.Id.ToString());
        command.Parameters.AddWithValue("$target", sweep.TargetId.ToString());
        command.Parameters.AddWithValue("$name", sweep.Name);
        command.Parameters.AddWithValue("$mode", sweep.Mode);
        command.Parameters.AddWithValue("$state", sweep.State.ToString());
        command.Parameters.AddWithValue("$created", Iso(sweep.CreatedAt));
        command.Parameters.AddWithValue("$started", (object?)(sweep.StartedAt is null ? null : Iso(sweep.StartedAt.Value)) ?? DBNull.Value);
        command.Parameters.AddWithValue("$finished", (object?)(sweep.FinishedAt is null ? null : Iso(sweep.FinishedAt.Value)) ?? DBNull.Value);
        command.Parameters.AddWithValue("$maxStates", sweep.MaxStates);
        command.Parameters.AddWithValue("$maxSeconds", sweep.MaxSeconds);
        command.Parameters.AddWithValue("$maxDepth", sweep.MaxDepth);
        command.Parameters.AddWithValue("$destructive", sweep.AllowDestructive ? 1 : 0);
        command.Parameters.AddWithValue("$visited", sweep.StatesVisited);
        command.Parameters.AddWithValue("$deviations", sweep.DeviationsFound);
        command.Parameters.AddWithValue("$evidence", sweep.EvidenceCaptured);
        command.Parameters.AddWithValue("$outcome", sweep.Outcome);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<Sweep> Sweeps(GhoztId? targetId = null)
    {
        var list = new List<Sweep>();
        using var command = _connection.CreateCommand();
        command.CommandText = targetId is null
            ? "SELECT * FROM Sweeps ORDER BY CreatedAt DESC;"
            : "SELECT * FROM Sweeps WHERE TargetId = $t ORDER BY CreatedAt DESC;";
        if (targetId is not null)
        {
            command.Parameters.AddWithValue("$t", targetId.Value.ToString());
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Sweep
            {
                Id = GhoztId.Parse(reader.GetString(reader.GetOrdinal("Id"))),
                TargetId = GhoztId.Parse(reader.GetString(reader.GetOrdinal("TargetId"))),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Mode = reader.GetString(reader.GetOrdinal("Mode")),
                State = Enum<RunState>(reader.GetString(reader.GetOrdinal("State"))),
                CreatedAt = ParseIso(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                StartedAt = ParseIsoOrNull(reader.GetValue(reader.GetOrdinal("StartedAt"))),
                FinishedAt = ParseIsoOrNull(reader.GetValue(reader.GetOrdinal("FinishedAt"))),
                MaxStates = reader.GetInt32(reader.GetOrdinal("MaxStates")),
                MaxSeconds = reader.GetInt32(reader.GetOrdinal("MaxSeconds")),
                MaxDepth = reader.GetInt32(reader.GetOrdinal("MaxDepth")),
                AllowDestructive = reader.GetInt32(reader.GetOrdinal("AllowDestructive")) != 0,
                StatesVisited = reader.GetInt32(reader.GetOrdinal("StatesVisited")),
                DeviationsFound = reader.GetInt32(reader.GetOrdinal("DeviationsFound")),
                EvidenceCaptured = reader.GetInt32(reader.GetOrdinal("EvidenceCaptured")),
                Outcome = reader.GetString(reader.GetOrdinal("Outcome")),
            });
        }

        return list;
    }

    // ---------------------------------------------------------------- lattice

    public void Save(LatticeNode node)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO LatticeNodes
              (Id, TargetId, Label, Kind, State, Provenance, ParentId, X, Y, VisitCount, Detail,
               EvidenceCount, LastSeenAt)
            VALUES
              ($id, $target, $label, $kind, $state, $prov, $parent, $x, $y, $visits, $detail,
               $evidence, $seen)
            ON CONFLICT(TargetId, Id) DO UPDATE SET
              Label = excluded.Label, State = excluded.State, Provenance = excluded.Provenance,
              X = excluded.X, Y = excluded.Y, VisitCount = excluded.VisitCount,
              Detail = excluded.Detail, EvidenceCount = excluded.EvidenceCount,
              LastSeenAt = excluded.LastSeenAt;
            """;
        command.Parameters.AddWithValue("$id", node.Id);
        command.Parameters.AddWithValue("$target", node.TargetId.ToString());
        command.Parameters.AddWithValue("$label", node.Label);
        command.Parameters.AddWithValue("$kind", node.Kind.ToString());
        command.Parameters.AddWithValue("$state", node.State);
        command.Parameters.AddWithValue("$prov", node.Provenance);
        command.Parameters.AddWithValue("$parent", node.ParentId);
        command.Parameters.AddWithValue("$x", node.X);
        command.Parameters.AddWithValue("$y", node.Y);
        command.Parameters.AddWithValue("$visits", node.VisitCount);
        command.Parameters.AddWithValue("$detail", node.Detail);
        command.Parameters.AddWithValue("$evidence", node.EvidenceCount);
        command.Parameters.AddWithValue("$seen", (object?)(node.LastSeenAt is null ? null : Iso(node.LastSeenAt.Value)) ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void Save(LatticeEdge edge)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO LatticeEdges (TargetId, FromId, ToId, Label, Relationship, TraversalCount)
            VALUES ($target, $from, $to, $label, $rel, $count)
            ON CONFLICT(TargetId, FromId, ToId, Relationship) DO UPDATE SET
              Label = excluded.Label, TraversalCount = excluded.TraversalCount;
            """;
        command.Parameters.AddWithValue("$target", edge.TargetId.ToString());
        command.Parameters.AddWithValue("$from", edge.FromId);
        command.Parameters.AddWithValue("$to", edge.ToId);
        command.Parameters.AddWithValue("$label", edge.Label);
        command.Parameters.AddWithValue("$rel", edge.Relationship);
        command.Parameters.AddWithValue("$count", edge.TraversalCount);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<LatticeNode> Nodes(GhoztId targetId)
    {
        var list = new List<LatticeNode>();
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT * FROM LatticeNodes WHERE TargetId = $t;";
        command.Parameters.AddWithValue("$t", targetId.ToString());
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new LatticeNode
            {
                Id = reader.GetString(reader.GetOrdinal("Id")),
                TargetId = targetId,
                Label = reader.GetString(reader.GetOrdinal("Label")),
                Kind = Enum<NodeKind>(reader.GetString(reader.GetOrdinal("Kind"))),
                State = reader.GetString(reader.GetOrdinal("State")),
                Provenance = reader.GetString(reader.GetOrdinal("Provenance")),
                ParentId = reader.GetString(reader.GetOrdinal("ParentId")),
                X = reader.GetDouble(reader.GetOrdinal("X")),
                Y = reader.GetDouble(reader.GetOrdinal("Y")),
                VisitCount = reader.GetInt32(reader.GetOrdinal("VisitCount")),
                Detail = reader.GetString(reader.GetOrdinal("Detail")),
                EvidenceCount = reader.GetInt32(reader.GetOrdinal("EvidenceCount")),
                LastSeenAt = ParseIsoOrNull(reader.GetValue(reader.GetOrdinal("LastSeenAt"))),
            });
        }

        return list;
    }

    public IReadOnlyList<LatticeEdge> Edges(GhoztId targetId)
    {
        var list = new List<LatticeEdge>();
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT * FROM LatticeEdges WHERE TargetId = $t;";
        command.Parameters.AddWithValue("$t", targetId.ToString());
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new LatticeEdge
            {
                TargetId = targetId,
                FromId = reader.GetString(reader.GetOrdinal("FromId")),
                ToId = reader.GetString(reader.GetOrdinal("ToId")),
                Label = reader.GetString(reader.GetOrdinal("Label")),
                Relationship = reader.GetString(reader.GetOrdinal("Relationship")),
                TraversalCount = reader.GetInt32(reader.GetOrdinal("TraversalCount")),
            });
        }

        return list;
    }

    // ---------------------------------------------------------------- findings

    public void Save(Finding finding)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Findings
              (Id, TargetId, SweepId, Title, Severity, Maturity, Actual, Expected, Fingerprint,
               ReproAttempts, ReproSuccesses, NodeId, Owner, FirstSeenAt, LastSeenAt, EvidenceCount)
            VALUES
              ($id, $target, $sweep, $title, $sev, $mat, $actual, $expected, $fp,
               $attempts, $successes, $node, $owner, $first, $last, $evidence)
            ON CONFLICT(Id) DO UPDATE SET
              Title = excluded.Title, Severity = excluded.Severity, Maturity = excluded.Maturity,
              Actual = excluded.Actual, Expected = excluded.Expected,
              ReproAttempts = excluded.ReproAttempts, ReproSuccesses = excluded.ReproSuccesses,
              Owner = excluded.Owner, LastSeenAt = excluded.LastSeenAt,
              EvidenceCount = excluded.EvidenceCount;
            """;
        command.Parameters.AddWithValue("$id", finding.Id.ToString());
        command.Parameters.AddWithValue("$target", finding.TargetId.ToString());
        command.Parameters.AddWithValue("$sweep", (object?)finding.SweepId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$title", finding.Title);
        command.Parameters.AddWithValue("$sev", finding.Severity.ToString());
        command.Parameters.AddWithValue("$mat", finding.Maturity.ToString());
        command.Parameters.AddWithValue("$actual", finding.Actual);
        command.Parameters.AddWithValue("$expected", finding.Expected);
        command.Parameters.AddWithValue("$fp", finding.Fingerprint);
        command.Parameters.AddWithValue("$attempts", finding.ReproAttempts);
        command.Parameters.AddWithValue("$successes", finding.ReproSuccesses);
        command.Parameters.AddWithValue("$node", finding.NodeId);
        command.Parameters.AddWithValue("$owner", finding.Owner);
        command.Parameters.AddWithValue("$first", Iso(finding.FirstSeenAt));
        command.Parameters.AddWithValue("$last", Iso(finding.LastSeenAt));
        command.Parameters.AddWithValue("$evidence", finding.EvidenceCount);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<Finding> Findings(GhoztId? targetId = null)
    {
        var list = new List<Finding>();
        using var command = _connection.CreateCommand();
        command.CommandText = targetId is null
            ? "SELECT * FROM Findings ORDER BY LastSeenAt DESC;"
            : "SELECT * FROM Findings WHERE TargetId = $t ORDER BY LastSeenAt DESC;";
        if (targetId is not null)
        {
            command.Parameters.AddWithValue("$t", targetId.Value.ToString());
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var sweepValue = reader.GetValue(reader.GetOrdinal("SweepId"));
            list.Add(new Finding
            {
                Id = GhoztId.Parse(reader.GetString(reader.GetOrdinal("Id"))),
                TargetId = GhoztId.Parse(reader.GetString(reader.GetOrdinal("TargetId"))),
                SweepId = sweepValue is string s && GhoztId.TryParse(s, out var sw) ? sw : null,
                Title = reader.GetString(reader.GetOrdinal("Title")),
                Severity = Enum<Severity>(reader.GetString(reader.GetOrdinal("Severity"))),
                Maturity = Enum<FindingMaturity>(reader.GetString(reader.GetOrdinal("Maturity"))),
                Actual = reader.GetString(reader.GetOrdinal("Actual")),
                Expected = reader.GetString(reader.GetOrdinal("Expected")),
                Fingerprint = reader.GetString(reader.GetOrdinal("Fingerprint")),
                ReproAttempts = reader.GetInt32(reader.GetOrdinal("ReproAttempts")),
                ReproSuccesses = reader.GetInt32(reader.GetOrdinal("ReproSuccesses")),
                NodeId = reader.GetString(reader.GetOrdinal("NodeId")),
                Owner = reader.GetString(reader.GetOrdinal("Owner")),
                FirstSeenAt = ParseIso(reader.GetString(reader.GetOrdinal("FirstSeenAt"))),
                LastSeenAt = ParseIso(reader.GetString(reader.GetOrdinal("LastSeenAt"))),
                EvidenceCount = reader.GetInt32(reader.GetOrdinal("EvidenceCount")),
            });
        }

        return list;
    }

    /// <summary>Finds an existing finding with the same fingerprint, so a fault clusters rather than duplicates.</summary>
    public Finding? FindByFingerprint(GhoztId targetId, string fingerprint) =>
        Findings(targetId).FirstOrDefault(f => f.Fingerprint == fingerprint);

    // ---------------------------------------------------------------- evidence

    public void Save(Evidence evidence)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Evidence
              (Id, TargetId, Kind, Summary, Sha256, SizeBytes, Payload, SourceAdapter, FindingId,
               SweepId, CapturedAt, Sealed, SealedBy, Redacted)
            VALUES
              ($id, $target, $kind, $summary, $hash, $size, $payload, $adapter, $finding,
               $sweep, $at, $sealed, $by, $redacted)
            ON CONFLICT(Id) DO UPDATE SET
              Sealed = excluded.Sealed, SealedBy = excluded.SealedBy, Redacted = excluded.Redacted,
              Summary = excluded.Summary;
            """;
        command.Parameters.AddWithValue("$id", evidence.Id.ToString());
        command.Parameters.AddWithValue("$target", evidence.TargetId.ToString());
        command.Parameters.AddWithValue("$kind", evidence.Kind);
        command.Parameters.AddWithValue("$summary", evidence.Summary);
        command.Parameters.AddWithValue("$hash", evidence.Sha256);
        command.Parameters.AddWithValue("$size", evidence.SizeBytes);
        command.Parameters.AddWithValue("$payload", evidence.Payload);
        command.Parameters.AddWithValue("$adapter", evidence.SourceAdapter);
        command.Parameters.AddWithValue("$finding", (object?)evidence.FindingId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$sweep", (object?)evidence.SweepId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$at", Iso(evidence.CapturedAt));
        command.Parameters.AddWithValue("$sealed", evidence.Sealed ? 1 : 0);
        command.Parameters.AddWithValue("$by", evidence.SealedBy);
        command.Parameters.AddWithValue("$redacted", evidence.Redacted ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<Evidence> EvidenceItems(GhoztId? targetId = null, GhoztId? findingId = null)
    {
        var list = new List<Evidence>();
        using var command = _connection.CreateCommand();
        command.CommandText = findingId is not null
            ? "SELECT * FROM Evidence WHERE FindingId = $f ORDER BY CapturedAt DESC;"
            : targetId is not null
                ? "SELECT * FROM Evidence WHERE TargetId = $t ORDER BY CapturedAt DESC;"
                : "SELECT * FROM Evidence ORDER BY CapturedAt DESC;";
        if (findingId is not null)
        {
            command.Parameters.AddWithValue("$f", findingId.Value.ToString());
        }
        else if (targetId is not null)
        {
            command.Parameters.AddWithValue("$t", targetId.Value.ToString());
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var findingValue = reader.GetValue(reader.GetOrdinal("FindingId"));
            var sweepValue = reader.GetValue(reader.GetOrdinal("SweepId"));
            list.Add(new Evidence
            {
                Id = GhoztId.Parse(reader.GetString(reader.GetOrdinal("Id"))),
                TargetId = GhoztId.Parse(reader.GetString(reader.GetOrdinal("TargetId"))),
                Kind = reader.GetString(reader.GetOrdinal("Kind")),
                Summary = reader.GetString(reader.GetOrdinal("Summary")),
                Sha256 = reader.GetString(reader.GetOrdinal("Sha256")),
                SizeBytes = reader.GetInt64(reader.GetOrdinal("SizeBytes")),
                Payload = reader.GetString(reader.GetOrdinal("Payload")),
                SourceAdapter = reader.GetString(reader.GetOrdinal("SourceAdapter")),
                FindingId = findingValue is string fs && GhoztId.TryParse(fs, out var fid) ? fid : null,
                SweepId = sweepValue is string ss && GhoztId.TryParse(ss, out var sid) ? sid : null,
                CapturedAt = ParseIso(reader.GetString(reader.GetOrdinal("CapturedAt"))),
                Sealed = reader.GetInt32(reader.GetOrdinal("Sealed")) != 0,
                SealedBy = reader.GetString(reader.GetOrdinal("SealedBy")),
                Redacted = reader.GetInt32(reader.GetOrdinal("Redacted")) != 0,
            });
        }

        return list;
    }

    // ---------------------------------------------------------------- events

    public void Append(LedgerEvent entry)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Events (Id, At, Code, Message, Severity, TargetId, SweepId, RelatedId, Actor, Detail)
            VALUES ($id, $at, $code, $msg, $sev, $target, $sweep, $related, $actor, $detail);
            """;
        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$at", Iso(entry.At));
        command.Parameters.AddWithValue("$code", entry.Code);
        command.Parameters.AddWithValue("$msg", entry.Message);
        command.Parameters.AddWithValue("$sev", entry.Severity.ToString());
        command.Parameters.AddWithValue("$target", (object?)entry.TargetId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$sweep", (object?)entry.SweepId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$related", entry.RelatedId);
        command.Parameters.AddWithValue("$actor", entry.Actor);
        command.Parameters.AddWithValue("$detail", entry.Detail);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// The most recent events, newest first.
    /// </summary>
    /// <remarks>
    /// Bounded by <paramref name="limit"/> rather than returning everything: the ledger is designed
    /// to grow without limit, and a stream surface that materialises the whole table to show the last
    /// hundred lines is how a forensics tool becomes unusable on the machine it was sized for.
    /// </remarks>
    public IReadOnlyList<LedgerEvent> RecentEvents(int limit = 500, GhoztId? targetId = null, string? codePrefix = null)
    {
        var list = new List<LedgerEvent>();
        using var command = _connection.CreateCommand();
        var where = new List<string>();
        if (targetId is not null)
        {
            where.Add("TargetId = $t");
            command.Parameters.AddWithValue("$t", targetId.Value.ToString());
        }

        if (!string.IsNullOrWhiteSpace(codePrefix))
        {
            where.Add("Code LIKE $c");
            command.Parameters.AddWithValue("$c", codePrefix + "%");
        }

        command.CommandText =
            "SELECT * FROM Events "
            + (where.Count > 0 ? "WHERE " + string.Join(" AND ", where) + " " : "")
            + "ORDER BY At DESC, Id DESC LIMIT $n;";
        command.Parameters.AddWithValue("$n", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var targetValue = reader.GetValue(reader.GetOrdinal("TargetId"));
            var sweepValue = reader.GetValue(reader.GetOrdinal("SweepId"));
            list.Add(new LedgerEvent
            {
                Id = GhoztId.Parse(reader.GetString(reader.GetOrdinal("Id"))),
                At = ParseIso(reader.GetString(reader.GetOrdinal("At"))),
                Code = reader.GetString(reader.GetOrdinal("Code")),
                Message = reader.GetString(reader.GetOrdinal("Message")),
                Severity = Enum<Severity>(reader.GetString(reader.GetOrdinal("Severity"))),
                TargetId = targetValue is string ts && GhoztId.TryParse(ts, out var tid) ? tid : null,
                SweepId = sweepValue is string ss && GhoztId.TryParse(ss, out var sid) ? sid : null,
                RelatedId = reader.GetString(reader.GetOrdinal("RelatedId")),
                Actor = reader.GetString(reader.GetOrdinal("Actor")),
                Detail = reader.GetString(reader.GetOrdinal("Detail")),
            });
        }

        return list;
    }

    public int CountEvents()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Events;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------- database maintenance & telemetry

    public DatabaseStatistics GetDatabaseStatistics()
    {
        lock (_gate)
        {
            var mainSize = File.Exists(DatabasePath) ? new FileInfo(DatabasePath).Length : 0;
            var walPath = DatabasePath + "-wal";
            var walSize = File.Exists(walPath) ? new FileInfo(walPath).Length : 0;

            int Count(string table)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }

            return new DatabaseStatistics
            {
                DatabasePath = DatabasePath,
                MainSizeBytes = mainSize,
                WalSizeBytes = walSize,
                TargetCount = Count("Targets"),
                SweepCount = Count("Sweeps"),
                NodeCount = Count("LatticeNodes"),
                EdgeCount = Count("LatticeEdges"),
                FindingCount = Count("Findings"),
                EvidenceCount = Count("Evidence"),
                EventCount = Count("Events"),
            };
        }
    }

    public string VerifyIntegrity()
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            using var reader = cmd.ExecuteReader();
            var results = new List<string>();
            while (reader.Read())
            {
                results.Add(reader.GetString(0));
            }
            return string.Join("; ", results);
        }
    }

    public void VacuumAndOptimize()
    {
        lock (_gate)
        {
            Execute("PRAGMA wal_checkpoint(TRUNCATE);");
            Execute("VACUUM;");
        }
    }

    public string BackupDatabase(string? destinationPath = null)
    {
        lock (_gate)
        {
            var backupDir = Path.Combine(Path.GetDirectoryName(DatabasePath) ?? "", "Backups");
            Directory.CreateDirectory(backupDir);
            var targetFile = destinationPath ?? Path.Combine(backupDir, $"ghozt_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.bak");

            if (File.Exists(targetFile))
            {
                File.Delete(targetFile);
            }

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "VACUUM INTO $dest;";
            cmd.Parameters.AddWithValue("$dest", targetFile);
            cmd.ExecuteNonQuery();

            return targetFile;
        }
    }

    // ---------------------------------------------------------------- authority grants (Patent Table 10, Table 70)

    public void Save(AuthorityGrant grant)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO AuthorityGrants
                  (GrantId, TargetId, Actor, MaxLevel, AllowedActionsJson, AllowedSensitivity, IssuedAt, ExpiresAt, Approver, ScopeJustification, AuditSignature)
                VALUES
                  ($id, $target, $actor, $level, $actions, $sens, $issued, $expires, $approver, $scope, $sig)
                ON CONFLICT(GrantId) DO UPDATE SET
                  Actor = excluded.Actor, MaxLevel = excluded.MaxLevel, AllowedActionsJson = excluded.AllowedActionsJson,
                  AllowedSensitivity = excluded.AllowedSensitivity, ExpiresAt = excluded.ExpiresAt,
                  Approver = excluded.Approver, ScopeJustification = excluded.ScopeJustification, AuditSignature = excluded.AuditSignature;
                """;
            command.Parameters.AddWithValue("$id", grant.GrantId.ToString());
            command.Parameters.AddWithValue("$target", grant.TargetId.ToString());
            command.Parameters.AddWithValue("$actor", grant.Actor);
            command.Parameters.AddWithValue("$level", (int)grant.MaxLevel);
            command.Parameters.AddWithValue("$actions", System.Text.Json.JsonSerializer.Serialize(grant.AllowedActionClasses));
            command.Parameters.AddWithValue("$sens", grant.AllowedDataSensitivity.ToString());
            command.Parameters.AddWithValue("$issued", Iso(grant.IssuedAt));
            command.Parameters.AddWithValue("$expires", Iso(grant.ExpiresAt));
            command.Parameters.AddWithValue("$approver", grant.Approver);
            command.Parameters.AddWithValue("$scope", grant.ScopeJustification);
            command.Parameters.AddWithValue("$sig", grant.AuditSignature);
            command.ExecuteNonQuery();
        }
    }

    public AuthorityGrant? ActiveGrantFor(GhoztId targetId)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                """
                SELECT * FROM AuthorityGrants
                WHERE TargetId = $target
                ORDER BY ExpiresAt DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$target", targetId.ToString());
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            return ReadAuthorityGrant(reader);
        }
    }

    public IReadOnlyList<AuthorityGrant> AuthorityGrantsFor(GhoztId targetId)
    {
        lock (_gate)
        {
            var list = new List<AuthorityGrant>();
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT * FROM AuthorityGrants WHERE TargetId = $target ORDER BY ExpiresAt DESC;";
            command.Parameters.AddWithValue("$target", targetId.ToString());
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(ReadAuthorityGrant(reader));
            }
            return list;
        }
    }

    private static AuthorityGrant ReadAuthorityGrant(SqliteDataReader reader)
    {
        var actionsJson = reader.GetString(reader.GetOrdinal("AllowedActionsJson"));
        var actions = System.Text.Json.JsonSerializer.Deserialize<List<string>>(actionsJson) ?? new List<string>();

        return new AuthorityGrant
        {
            GrantId = GhoztId.Parse(reader.GetString(reader.GetOrdinal("GrantId"))),
            TargetId = GhoztId.Parse(reader.GetString(reader.GetOrdinal("TargetId"))),
            Actor = reader.GetString(reader.GetOrdinal("Actor")),
            MaxLevel = (ExecutionIsolationLevel)reader.GetInt32(reader.GetOrdinal("MaxLevel")),
            AllowedActionClasses = actions,
            AllowedDataSensitivity = Enum<DataSensitivityTier>(reader.GetString(reader.GetOrdinal("AllowedSensitivity"))),
            IssuedAt = ParseIso(reader.GetString(reader.GetOrdinal("IssuedAt"))),
            ExpiresAt = ParseIso(reader.GetString(reader.GetOrdinal("ExpiresAt"))),
            Approver = reader.GetString(reader.GetOrdinal("Approver")),
            ScopeJustification = reader.GetString(reader.GetOrdinal("ScopeJustification")),
            AuditSignature = reader.GetString(reader.GetOrdinal("AuditSignature")),
        };
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
    }
}

/// <summary>
/// Telemetry statistics describing the SQLite database on disk and row counts across primary planes.
/// </summary>
public sealed record DatabaseStatistics
{
    public required string DatabasePath { get; init; }
    public required long MainSizeBytes { get; init; }
    public required long WalSizeBytes { get; init; }
    public required int TargetCount { get; init; }
    public required int SweepCount { get; init; }
    public required int NodeCount { get; init; }
    public required int EdgeCount { get; init; }
    public required int FindingCount { get; init; }
    public required int EvidenceCount { get; init; }
    public required int EventCount { get; init; }
}

