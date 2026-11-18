using System.Text.Json;

namespace Tempo;

public sealed class SyncItem
{
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
    public DateTime StampUtc { get; set; }
    public JsonElement Data { get; set; }
}

public sealed class Tombstone
{
    public string Key { get; set; } = "";        // "Kind:Id"
    public DateTime StampUtc { get; set; }
}

/// <summary>What one device publishes into the shared folder.</summary>
public sealed class SyncFile
{
    public string DeviceId { get; set; } = "";
    public string Member { get; set; } = "";
    public DateTime WrittenUtc { get; set; }
    public int InvoiceCounter { get; set; }
    public int QuoteCounter { get; set; }
    public List<SyncItem> Items { get; set; } = new();
    public List<Tombstone> Deleted { get; set; } = new();
}

/// <summary>This device's memory of the last sync: a fingerprint and timestamp per record, plus deletions.</summary>
public sealed class SyncState
{
    public Dictionary<string, Seen> Seen { get; set; } = new();
    public Dictionary<string, DateTime> Deleted { get; set; } = new();
}

/// <summary>A record's fingerprint (to notice edits) and when it last changed.</summary>
public sealed record Seen(string Hash, DateTime Stamp);

public sealed record SyncResult(SyncFile Own, int Received, int Removed);

/// <summary>The disk side of sync: reads the other devices' files from the shared folder, merges, writes ours.</summary>
public static class SyncService
{
    const long MaxFileBytes = 64L * 1024 * 1024;
    const int MaxDevices = 50;
    public static DateTime? LastRunUtc { get; private set; }
    public static string? LastError { get; private set; }

    public static string FileFor(string folder, string deviceId) => System.IO.Path.Combine(folder, $"tempo-sync-{deviceId}.json");

    /// <summary>One sync round. Returns null if the folder can't be used. Other devices' broken files are skipped.</summary>
    public static SyncResult? RunWith(Workspace w, string folder, string statePath, string deviceId, string member, DateTime nowUtc)
    {
        try
        {
            if (!System.IO.Directory.Exists(folder)) { LastError = "folder"; return null; }
            SyncState state;
            try { state = System.IO.File.Exists(statePath) ? JsonSerializer.Deserialize<SyncState>(System.IO.File.ReadAllText(statePath)) ?? new() : new(); }
            catch { state = new(); }

            var own = System.IO.Path.GetFileName(FileFor(folder, deviceId));
            var remotes = new List<SyncFile>();
            foreach (var f in new System.IO.DirectoryInfo(folder).GetFiles("tempo-sync-*.json").Where(f => f.Name != own).OrderByDescending(f => f.LastWriteTimeUtc).Take(MaxDevices))
            {
                if (f.Length > MaxFileBytes) continue;
                try { if (JsonSerializer.Deserialize<SyncFile>(System.IO.File.ReadAllText(f.FullName)) is { } sf && sf.DeviceId.Length > 0) remotes.Add(sf); }
                catch { /* half-written by a sync client, or not ours: try again next round */ }
            }

            var res = Sync.Step(w, state, remotes, deviceId, member, nowUtc);

            // write atomically so other devices never read half a file
            var tmp = FileFor(folder, deviceId) + ".tmp";
            System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(res.Own));
            System.IO.File.Move(tmp, FileFor(folder, deviceId), true);
            System.IO.File.WriteAllText(statePath, JsonSerializer.Serialize(state));
            LastRunUtc = nowUtc; LastError = null;
            return res;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    /// <summary>Sync the live workspace using Settings. Refreshes the UI if anything arrived.</summary>
    public static SyncResult? Run()
    {
        var s = State.Data.Settings;
        if (string.IsNullOrWhiteSpace(s.SyncFolder)) return null;
        if (string.IsNullOrEmpty(s.DeviceId)) s.DeviceId = Guid.NewGuid().ToString("N")[..12];
        var res = RunWith(State.Data, s.SyncFolder, System.IO.Path.Combine(State.Dir, "sync-state.json"), s.DeviceId, s.MemberName, DateTime.UtcNow);
        if (res is { } r && (r.Received > 0 || r.Removed > 0)) State.Notify(); else State.Save();
        return res;
    }
}

/// <summary>Folder sync: merge other devices' files into this workspace (newest edit wins), then publish ours.</summary>
public static class Sync
{
    sealed record Kind(string Name, System.Collections.IList List, Type Type);

    static List<Kind> Kinds(Workspace w) => new()
    {
        new("Session", w.Sessions, typeof(Session)), new("Project", w.Projects, typeof(Project)), new("Client", w.Clients, typeof(Client)),
        new("Invoice", w.Invoices, typeof(Invoice)), new("Expense", w.Expenses, typeof(Expense)), new("Recurring", w.Recurring, typeof(RecurringInvoice)),
    };

    static string IdOf(object o) => o.GetType().GetProperty("Id")?.GetValue(o) as string ?? "";
    static string Hash(string json) => Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));

    public static SyncResult Step(Workspace local, SyncState state, IEnumerable<SyncFile> remotes, string deviceId, string member, DateTime nowUtc)
    {
        var kinds = Kinds(local);

        // 1. notice what changed here since the last sync (new or edited records get "now"; vanished ones become deletions)
        var present = new Dictionary<string, (Kind K, object Obj, string Json)>();
        foreach (var k in kinds)
            foreach (var o in k.List)
            {
                var json = JsonSerializer.Serialize(o, k.Type);
                present[$"{k.Name}:{IdOf(o!)}"] = (k, o!, json);
            }
        foreach (var (key, p) in present)
        {
            var h = Hash(p.Json);
            if (!state.Seen.TryGetValue(key, out var seen) || seen.Hash != h) state.Seen[key] = new Seen(h, nowUtc);
            state.Deleted.Remove(key);
        }
        foreach (var key in state.Seen.Keys.Where(k => !present.ContainsKey(k)).ToList())
        {
            state.Seen.Remove(key);
            state.Deleted[key] = nowUtc;
        }

        // 2. take anything newer from the other devices
        int received = 0, removed = 0;
        foreach (var r in remotes.Where(r => r.DeviceId != deviceId))
        {
            local.InvoiceCounter = Math.Max(local.InvoiceCounter, r.InvoiceCounter);
            local.QuoteCounter = Math.Max(local.QuoteCounter, r.QuoteCounter);
            foreach (var item in r.Items)
            {
                var key = $"{item.Kind}:{item.Id}";
                var kind = kinds.FirstOrDefault(k => k.Name == item.Kind);
                if (kind == null || string.IsNullOrEmpty(item.Id)) continue;
                var mine = state.Seen.TryGetValue(key, out var s) ? s.Stamp : DateTime.MinValue;
                var gone = state.Deleted.TryGetValue(key, out var dstamp) ? dstamp : DateTime.MinValue;
                if (item.StampUtc <= mine || item.StampUtc <= gone) continue;
                object? obj;
                try { obj = item.Data.Deserialize(kind.Type); } catch { continue; }
                if (obj == null || IdOf(obj) != item.Id) continue;
                if (present.TryGetValue(key, out var old)) kind.List[kind.List.IndexOf(old.Obj)] = obj; else kind.List.Add(obj);
                present[key] = (kind, obj, JsonSerializer.Serialize(obj, kind.Type));
                state.Seen[key] = new Seen(Hash(present[key].Json), item.StampUtc);
                state.Deleted.Remove(key);
                received++;
            }
            foreach (var t in r.Deleted)
            {
                if (state.Seen.TryGetValue(t.Key, out var s) && t.StampUtc > s.Stamp && present.TryGetValue(t.Key, out var p))
                {
                    p.K.List.Remove(p.Obj); present.Remove(t.Key); state.Seen.Remove(t.Key);
                    state.Deleted[t.Key] = t.StampUtc; removed++;
                }
                else if (!state.Seen.ContainsKey(t.Key))
                    state.Deleted[t.Key] = state.Deleted.TryGetValue(t.Key, out var d) && d > t.StampUtc ? d : t.StampUtc;
            }
        }

        // 3. publish our view
        var own = new SyncFile
        {
            DeviceId = deviceId, Member = member, WrittenUtc = nowUtc, InvoiceCounter = local.InvoiceCounter, QuoteCounter = local.QuoteCounter,
            Items = present.Select(p => new SyncItem { Kind = p.Value.K.Name, Id = IdOf(p.Value.Obj), StampUtc = state.Seen[p.Key].Stamp, Data = JsonDocument.Parse(p.Value.Json).RootElement.Clone() }).ToList(),
            Deleted = state.Deleted.Where(d => d.Value > nowUtc.AddDays(-180)).Select(d => new Tombstone { Key = d.Key, StampUtc = d.Value }).ToList(),
        };
        return new SyncResult(own, received, removed);
    }
}
