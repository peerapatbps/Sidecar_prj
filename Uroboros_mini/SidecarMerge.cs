#nullable enable
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Uroboros.MiniUploader;

/// <summary>
/// Merges TruckSidecar's chemical-unload store (<c>TruckSidecar\data\sidecar.db</c>, tables
/// SIDECAR_UNLOAD + SIDECAR_FILL with the exact chem.db schema) into the chem SQLite snapshot
/// before it is uploaded to Google Drive.
///
/// The sidecar may be writing while we read, so we never open or copy its file directly:
/// <see cref="Snapshot"/> takes a consistent copy with the SQLite online-backup API, and the
/// copy is ATTACHed to the chem connection and copied with INSERT…SELECT.
///
/// Link to the scale data: SIDECAR_UNLOAD.TICKET1 == WDATA.TICKET1 (soft FK —
/// WDATA only carries completed STAT='2' trips, so in-progress records have no
/// parent row yet). SIDECAR_FILL.UNLOAD_ID -> SIDECAR_UNLOAD.ID is a real,
/// enforced FK (PRAGMA foreign_keys=ON in the create script).
///
/// Best-effort: any failure here is logged and swallowed so a sidecar problem
/// can never block the core MDB -> chem.db -> Drive upload.
/// </summary>
internal static class SidecarMerge
{
    private const string Alias = "sc";

    private const string UnloadCols =
        "ID,TICKET1,TICKET2,PLATE,CHEMICAL,PRODUCT,SOURCE,WEIGH_IN_AT,W1,WEIGH_OUT_AT,W2,NET_KG," +
        "SG,TEMP_C,BILL_BOOK,BILL_NO,IS_COMPLETE,VERSION,CREATED_AT,CREATED_BY,UPDATED_AT,UPDATED_BY,SYNCED";

    private const string FillCols = "UNLOAD_ID,SEQ,TANK,LEVEL_BEFORE,LEVEL_AFTER,AFTER_AT";

    /// <summary>
    /// Relative SidecarDb (e.g. "..\TruckSidecar\data\sidecar.db") is resolved against the
    /// exe folder, not the current directory — the CWD depends on how the process was launched.
    /// </summary>
    public static string? ResolvePath(string? path, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDir, path));
    }

    /// <summary>
    /// Change marker for the sidecar store: row count + SUM(VERSION) + SUM(SYNCED). Every sidecar
    /// save bumps VERSION, so a record saved while data.mdb is unchanged still triggers a
    /// re-convert + upload. (File mtime is not used — it can lag behind SQLite commits.)
    /// Empty when there is no store / no rows (keeps the old MDB-only stamp).
    /// </summary>
    public static string Stamp(string? dbPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath)) return "";
            using var c = OpenReadOnly(dbPath);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*), IFNULL(SUM(VERSION),0), IFNULL(SUM(SYNCED),0) FROM SIDECAR_UNLOAD;";
            using var r = cmd.ExecuteReader();
            if (!r.Read() || r.GetInt64(0) == 0) return "";
            return string.Create(CultureInfo.InvariantCulture, $"_sc{r.GetInt64(0)}v{r.GetInt64(1)}s{r.GetInt64(2)}");
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Consistent copy of the sidecar store into <paramref name="workDir"/> via the SQLite backup
    /// API (safe while the sidecar is writing). Returns null when there is nothing to merge.
    /// The caller deletes the returned file.
    /// </summary>
    public static string? Snapshot(string? dbPath, string workDir, ILogger? log)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                log?.Info("[SIDECAR] No SidecarDb configured; skipping sidecar merge.");
                return null;
            }
            if (!File.Exists(dbPath))
            {
                log?.Warn($"[SIDECAR] Sidecar db not found ({dbPath}); skipping sidecar merge.");
                return null;
            }

            Directory.CreateDirectory(workDir);
            var copy = Path.Combine(workDir, $"sidecar.snapshot.{Guid.NewGuid():N}.db");
            using (var src = OpenReadOnly(dbPath))
            using (var dst = new SqliteConnection($"Data Source={copy};Mode=ReadWriteCreate;Pooling=False;"))
            {
                dst.Open();
                src.BackupDatabase(dst);
            }
            return copy;
        }
        catch (Exception ex)
        {
            log?.Warn($"[SIDECAR] Snapshot of {dbPath} failed; skipping sidecar merge: {ex.Message}");
            return null;
        }
    }

    /// <summary>ATTACH the snapshot to <paramref name="conn"/>. Must run outside a transaction.</summary>
    public static bool Attach(SqliteConnection conn, string? snapshotPath, ILogger? log)
    {
        if (snapshotPath is null) return false;
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ATTACH DATABASE $p AS {Alias};";
            cmd.Parameters.AddWithValue("$p", snapshotPath);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            log?.Warn($"[SIDECAR] Attach failed; skipping sidecar merge: {ex.Message}");
            return false;
        }
    }

    public static void Detach(SqliteConnection conn)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DETACH DATABASE {Alias};";
            cmd.ExecuteNonQuery();
        }
        catch { /* not attached */ }
    }

    /// <summary>Copy the attached sidecar tables into chem.db (inside the convert transaction).</summary>
    public static void MergeAttached(SqliteConnection conn, SqliteTransaction tx, string source, ILogger? log)
    {
        try
        {
            int unloads = Exec(conn, tx, $"INSERT OR REPLACE INTO main.SIDECAR_UNLOAD ({UnloadCols}) SELECT {UnloadCols} FROM {Alias}.SIDECAR_UNLOAD;");
            int fills = Exec(conn, tx, $"INSERT INTO main.SIDECAR_FILL ({FillCols}) SELECT {FillCols} FROM {Alias}.SIDECAR_FILL ORDER BY UNLOAD_ID, SEQ;");
            log?.Info($"[SIDECAR] Merged {unloads} unload record(s), {fills} fill row(s) from {source}.");
        }
        catch (Exception ex)
        {
            // Never let a sidecar problem fail the whole convert/upload.
            log?.Warn($"[SIDECAR] Merge skipped due to error: {ex.Message}");
        }
    }

    /// <summary>
    /// Offline self-test (no MDB / dongle needed): builds a fresh chem schema in
    /// <paramref name="outDb"/>, merges <paramref name="sidecarDb"/>, then prints row counts
    /// and a preview. Invoked via <c>Uroboros.MiniUploader.exe --sidecar-test &lt;sidecar.db&gt; [outDb]</c>.
    /// </summary>
    public static int SelfTest(string sidecarDb, string outDb, ILogger? log)
    {
        string? snap = null;
        try
        {
            if (File.Exists(outDb)) File.Delete(outDb);

            using var conn = new SqliteConnection($"Data Source={outDb};Mode=ReadWriteCreate;Pooling=False;");
            conn.Open();
            MdbToSqliteConverter.EnsureChemSchema(conn);

            snap = Snapshot(sidecarDb, Path.GetDirectoryName(Path.GetFullPath(outDb))!, log);
            var attached = Attach(conn, snap, log);
            using (var tx = conn.BeginTransaction())
            {
                if (attached) MergeAttached(conn, tx, sidecarDb, log);
                tx.Commit();
            }
            if (attached) Detach(conn);
            Console.WriteLine($"Stamp               : '{Stamp(sidecarDb)}'");

            int Count(string table)
            {
                using var c = conn.CreateCommand();
                c.CommandText = $"SELECT COUNT(*) FROM {table};";
                return Convert.ToInt32(c.ExecuteScalar(), CultureInfo.InvariantCulture);
            }

            Console.WriteLine($"SIDECAR_UNLOAD rows : {Count("SIDECAR_UNLOAD")}");
            Console.WriteLine($"SIDECAR_FILL   rows : {Count("SIDECAR_FILL")}");
            Console.WriteLine();
            Console.WriteLine("ID                                TICKET1     TICKET2     CHEMICAL  NET_KG    COMPLETE");
            Console.WriteLine("--------------------------------  ----------  ----------  --------  --------  --------");

            using (var q = conn.CreateCommand())
            {
                q.CommandText =
                    "SELECT ID,TICKET1,TICKET2,CHEMICAL,NET_KG,IS_COMPLETE FROM SIDECAR_UNLOAD ORDER BY WEIGH_IN_AT;";
                using var r = q.ExecuteReader();
                while (r.Read())
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-32}  {1,-10}  {2,-10}  {3,-8}  {4,8}  {5}",
                        r.IsDBNull(0) ? "" : r.GetString(0),
                        r.IsDBNull(1) ? "" : r.GetString(1),
                        r.IsDBNull(2) ? "" : r.GetString(2),
                        r.IsDBNull(3) ? "" : r.GetString(3),
                        r.IsDBNull(4) ? "" : r.GetDouble(4).ToString("0", CultureInfo.InvariantCulture),
                        r.IsDBNull(5) ? "" : r.GetInt32(5).ToString(CultureInfo.InvariantCulture)));
                }
            }

            Console.WriteLine();
            Console.WriteLine($"OK — chem test db written to {outDb}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Self-test FAILED: {ex}");
            return 1;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (snap is not null) try { File.Delete(snap); } catch { }
        }
    }

    private static SqliteConnection OpenReadOnly(string dbPath)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 30,
        }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout = 30000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    private static int Exec(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd.ExecuteNonQuery();
    }
}
