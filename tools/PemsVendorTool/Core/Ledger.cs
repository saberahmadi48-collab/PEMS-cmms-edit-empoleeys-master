using System.Globalization;
using Microsoft.Data.Sqlite;
using PEMS.Licensing.Seal;

namespace PemsVendorTool.Core;

public sealed record CustomerRow(string CustomerRef, string Name, string? Note);

public sealed record LedgerRow(
    long IssueSeq, string LicenceId, string CustomerRef, string InstallId, string FpHash, string SigningKeyId, string Edition,
    string Modules, string Type, DateTimeOffset NotBefore, DateTimeOffset? NotAfter, DateTimeOffset? UpdatesUntil, string Status,
    DateTimeOffset IssuedUtc, string RequestNonce, string RequestType, int TransferCount, long MaxCounter, string AuditHeadSeen,
    string? Note, bool Imported);

public sealed record AuditRow(long Seq, DateTimeOffset Utc, string Operator, string Action, string Subject, string DataHash, string PrevHash, string EntryHash);

public sealed record Witness(long MaxCounter, string AuditHead, string CustomerRef);

/// <summary>
/// The issued-licence ledger, nonce register and hash-chained audit log in one SQLite file (design 9.4, plan #106/#341).
/// Every mutating method takes the transaction of the caller, so an issue, its ledger row, the nonce and its audit entry commit or fail together.
/// </summary>
public sealed class Ledger
{
    private readonly string _connectionString;

    public Ledger(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        using var c = Open();
        Exec(c, null, """
            CREATE TABLE IF NOT EXISTS customers(customer_ref TEXT PRIMARY KEY, name TEXT NOT NULL, note TEXT, created_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS licences(
                issue_seq INTEGER PRIMARY KEY, licence_id TEXT NOT NULL UNIQUE, customer_ref TEXT NOT NULL, install_id TEXT NOT NULL,
                fp_hash TEXT NOT NULL, signing_key_id TEXT NOT NULL, edition TEXT NOT NULL, modules TEXT NOT NULL, type TEXT NOT NULL,
                not_before TEXT NOT NULL, not_after TEXT, updates_until TEXT, status TEXT NOT NULL, issued_utc TEXT NOT NULL,
                request_nonce TEXT NOT NULL, request_type TEXT NOT NULL, transfer_count INTEGER NOT NULL, max_counter INTEGER NOT NULL,
                audit_head_seen TEXT NOT NULL, note TEXT, licence_text TEXT NOT NULL, imported INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS nonces(nonce TEXT PRIMARY KEY, install_id TEXT NOT NULL, request_type TEXT NOT NULL, seen_utc TEXT NOT NULL,
                counter INTEGER NOT NULL, audit_head TEXT NOT NULL, customer_ref TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS audit(seq INTEGER PRIMARY KEY, utc TEXT NOT NULL, operator TEXT NOT NULL, action TEXT NOT NULL,
                subject TEXT NOT NULL, data_hash TEXT NOT NULL, prev_hash TEXT NOT NULL, entry_hash TEXT NOT NULL);
            """);
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    // ---- customers
    public void AddCustomer(SqliteConnection c, SqliteTransaction? t, CustomerRow row, DateTimeOffset now)
        => Exec(c, t, "INSERT INTO customers(customer_ref,name,note,created_utc) VALUES($r,$n,$o,$u)",
            ("$r", row.CustomerRef), ("$n", row.Name), ("$o", row.Note), ("$u", Iso(now)));

    public CustomerRow? GetCustomer(string customerRef)
    {
        using var c = Open();
        using var cmd = Cmd(c, null, "SELECT customer_ref,name,note FROM customers WHERE customer_ref=$r", ("$r", customerRef));
        using var r = cmd.ExecuteReader();
        return r.Read() ? new CustomerRow(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)) : null;
    }

    public IReadOnlyList<CustomerRow> Customers()
    {
        using var c = Open();
        using var cmd = Cmd(c, null, "SELECT customer_ref,name,note FROM customers ORDER BY customer_ref");
        using var r = cmd.ExecuteReader();
        var list = new List<CustomerRow>();
        while (r.Read()) list.Add(new CustomerRow(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    // ---- nonces and witness
    public bool NonceSeen(string nonce)
    {
        using var c = Open();
        using var cmd = Cmd(c, null, "SELECT 1 FROM nonces WHERE nonce=$n", ("$n", nonce));
        return cmd.ExecuteScalar() is not null;
    }

    public void RegisterNonce(SqliteConnection c, SqliteTransaction t, string nonce, string installId, string requestType, DateTimeOffset now, long counter, string head, string customerRef)
        => Exec(c, t, "INSERT INTO nonces(nonce,install_id,request_type,seen_utc,counter,audit_head,customer_ref) VALUES($n,$i,$q,$u,$c,$h,$r)",
            ("$n", nonce), ("$i", installId), ("$q", requestType), ("$u", Iso(now)), ("$c", counter), ("$h", head), ("$r", customerRef));

    /// <summary>Highest counter the vendor has seen for an Install ID (the vendor-side restore witness, design 4.4).</summary>
    public Witness? WitnessFor(string installId)
    {
        using var c = Open();
        using var cmd = Cmd(c, null, "SELECT counter,audit_head,customer_ref FROM nonces WHERE install_id=$i ORDER BY counter DESC LIMIT 1", ("$i", installId));
        using var r = cmd.ExecuteReader();
        return r.Read() ? new Witness(r.GetInt64(0), r.GetString(1), r.GetString(2)) : null;
    }

    // ---- licences
    public long NextIssueSeq(SqliteConnection c, SqliteTransaction t)
    {
        using var cmd = Cmd(c, t, "SELECT COALESCE(MAX(issue_seq),0)+1 FROM licences");
        return (long)cmd.ExecuteScalar()!;
    }

    public void InsertLicence(SqliteConnection c, SqliteTransaction t, LedgerRow row, string licenceText)
        => Exec(c, t, """
            INSERT INTO licences(issue_seq,licence_id,customer_ref,install_id,fp_hash,signing_key_id,edition,modules,type,not_before,not_after,updates_until,status,
                issued_utc,request_nonce,request_type,transfer_count,max_counter,audit_head_seen,note,licence_text,imported)
            VALUES($s,$id,$c,$i,$f,$k,$e,$m,$ty,$nb,$na,$uu,$st,$iu,$rn,$rt,$tc,$mc,$ah,$no,$lt,$im)
            """,
            ("$s", row.IssueSeq), ("$id", row.LicenceId), ("$c", row.CustomerRef), ("$i", row.InstallId), ("$f", row.FpHash), ("$k", row.SigningKeyId),
            ("$e", row.Edition), ("$m", row.Modules), ("$ty", row.Type), ("$nb", Iso(row.NotBefore)), ("$na", row.NotAfter is null ? null : Iso(row.NotAfter.Value)),
            ("$uu", row.UpdatesUntil is null ? null : Iso(row.UpdatesUntil.Value)), ("$st", row.Status), ("$iu", Iso(row.IssuedUtc)),
            ("$rn", row.RequestNonce), ("$rt", row.RequestType), ("$tc", row.TransferCount), ("$mc", row.MaxCounter), ("$ah", row.AuditHeadSeen),
            ("$no", row.Note), ("$lt", licenceText), ("$im", row.Imported ? 1 : 0));

    public void SetStatus(SqliteConnection c, SqliteTransaction t, string licenceId, string status)
        => Exec(c, t, "UPDATE licences SET status=$s WHERE licence_id=$id", ("$s", status), ("$id", licenceId));

    public LedgerRow? GetLicence(string licenceId) => Query("WHERE licence_id=$id", ("$id", licenceId)).FirstOrDefault();

    public string? GetLicenceText(string licenceId)
    {
        using var c = Open();
        using var cmd = Cmd(c, null, "SELECT licence_text FROM licences WHERE licence_id=$id", ("$id", licenceId));
        return cmd.ExecuteScalar() as string;
    }

    public IReadOnlyList<LedgerRow> Licences(string? customerRef = null)
        => customerRef is null ? Query("") : Query("WHERE customer_ref=$c", ("$c", customerRef));

    public IReadOnlyList<LedgerRow> ForInstall(string installId) => Query("WHERE install_id=$i", ("$i", installId));

    /// <summary>Licences with the same fingerprint hash under another Install ID, or the same Install ID under another customer (clone flags, design 9.2).</summary>
    public IReadOnlyList<LedgerRow> CloneCandidates(string installId, string fpHash, string customerRef)
        => Query("WHERE (fp_hash=$f AND install_id<>$i) OR (install_id=$i AND customer_ref<>$c)", ("$f", fpHash), ("$i", installId), ("$c", customerRef));

    private List<LedgerRow> Query(string where, params (string, object?)[] args)
    {
        using var c = Open();
        using var cmd = Cmd(c, null, "SELECT issue_seq,licence_id,customer_ref,install_id,fp_hash,signing_key_id,edition,modules,type,not_before,not_after,updates_until,status,issued_utc,request_nonce,request_type,transfer_count,max_counter,audit_head_seen,note,imported FROM licences "
            + where + " ORDER BY issue_seq", args);
        using var r = cmd.ExecuteReader();
        var list = new List<LedgerRow>();
        while (r.Read())
            list.Add(new LedgerRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8),
                Parse(r.GetString(9)), r.IsDBNull(10) ? null : Parse(r.GetString(10)), r.IsDBNull(11) ? null : Parse(r.GetString(11)), r.GetString(12),
                Parse(r.GetString(13)), r.GetString(14), r.GetString(15), r.GetInt32(16), r.GetInt64(17), r.GetString(18), r.IsDBNull(19) ? null : r.GetString(19), r.GetInt32(20) == 1));
        return list;
    }

    // ---- audit chain
    public AuditRow Audit(SqliteConnection c, SqliteTransaction t, string op, string action, string subject, string data, DateTimeOffset now)
    {
        using var last = Cmd(c, t, "SELECT seq,utc,operator,action,subject,data_hash,prev_hash,entry_hash FROM audit ORDER BY seq DESC LIMIT 1");
        AuditRow? prev = null;
        using (var r = last.ExecuteReader())
            if (r.Read()) prev = ReadAudit(r);
        var e = EventChain.Next(prev is null ? null : new LicenseEventEntry(prev.Seq, prev.Utc, prev.Action, prev.DataHash, prev.PrevHash, prev.EntryHash), now, action, data);
        Exec(c, t, "INSERT INTO audit(seq,utc,operator,action,subject,data_hash,prev_hash,entry_hash) VALUES($s,$u,$o,$a,$j,$d,$p,$h)",
            ("$s", e.Seq), ("$u", Iso(now)), ("$o", op), ("$a", action), ("$j", subject), ("$d", e.DataHash), ("$p", e.PrevHash), ("$h", e.EntryHash));
        return new AuditRow(e.Seq, now, op, action, subject, e.DataHash, e.PrevHash, e.EntryHash);
    }

    public IReadOnlyList<AuditRow> AuditLog()
    {
        using var c = Open();
        using var cmd = Cmd(c, null, "SELECT seq,utc,operator,action,subject,data_hash,prev_hash,entry_hash FROM audit ORDER BY seq");
        using var r = cmd.ExecuteReader();
        var list = new List<AuditRow>();
        while (r.Read()) list.Add(ReadAudit(r));
        return list;
    }

    /// <summary>True when every entry chains to the previous one and no entry was edited or removed from the middle.</summary>
    public bool VerifyAuditChain()
        => EventChain.IsValid(AuditLog().Select(a => new LicenseEventEntry(a.Seq, a.Utc, a.Action, a.DataHash, a.PrevHash, a.EntryHash)));

    public string? AuditHead() => AuditLog().LastOrDefault()?.EntryHash;

    private static AuditRow ReadAudit(SqliteDataReader r)
        => new(r.GetInt64(0), Parse(r.GetString(1)), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7));

    // ---- helpers
    public static string Iso(DateTimeOffset d) => d.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string s) => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static SqliteCommand Cmd(SqliteConnection c, SqliteTransaction? t, string sql, params (string, object?)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd;
    }

    private static void Exec(SqliteConnection c, SqliteTransaction? t, string sql, params (string, object?)[] args)
    {
        using var cmd = Cmd(c, t, sql, args);
        cmd.ExecuteNonQuery();
    }
}
