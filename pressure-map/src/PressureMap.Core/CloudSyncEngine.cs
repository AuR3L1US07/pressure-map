using System.Text.Json;
using System.Text.Json.Nodes;

namespace PressureMap.Core;

public enum SyncOutcome { Unchanged, Uploaded, Downloaded, Conflict }
public sealed record SyncConflict(string RemoteSnapshot, string Revision, string LocalHash);
public sealed record SyncBaseline(string? Revision, string Hash);

public sealed class CloudSyncEngine(CloudClient client, Store store, Func<bool>? canApply = null)
{
    private readonly SemaphoreSlim gate = new(1,1);
    private string StatePath => Path.Combine(store.DirectoryPath,"sync-state.json");
    public SyncConflict? Conflict { get; private set; }
    public bool IsBusy => gate.CurrentCount == 0;
    private SyncBaseline? Load()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<SyncBaseline>(File.ReadAllText(StatePath),DataJson.Options) : null; }
        catch (JsonException) { return null; }
    }
    private void Save(string? revision, string hash)
    {
        var temp = StatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp,JsonSerializer.Serialize(new SyncBaseline(revision,hash),DataJson.Options)); File.Move(temp,StatePath,true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static (string? Revision,string Json) Remote(JsonObject remote)
    {
        if (remote["snapshot"] is not JsonObject snapshot) throw new InvalidDataException("云端响应缺少数据快照。");
        var revision = remote["revision"]?.GetValue<string>();
        if (revision is { Length: < 1 or > 128 }) throw new InvalidDataException("云端版本号无效。");
        var json = snapshot.ToJsonString(DataJson.Compact); SnapshotData.Parse(json); return (revision,json);
    }
    private void SetConflict(string remote, string revision, string hash, string local)
    {
        store.WriteBackup(local,"local-conflict"); store.WriteBackup(remote,"cloud-conflict");
        Conflict = new(remote,revision,hash);
    }
    public async Task<SyncOutcome> SyncAsync(CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            if (Conflict is not null) return SyncOutcome.Conflict;
            var local = store.Snapshot(); var hash = SnapshotData.Hash(local); var baseline = Load();
            var remote = Remote(await client.DownloadAsync(cancellation)); var remoteHash = SnapshotData.Hash(remote.Json);
            if (hash == remoteHash) { Save(remote.Revision,hash); return SyncOutcome.Unchanged; }
            var localEmpty = SnapshotData.Parse(local).IsEmpty;
            var remoteEmpty = remote.Revision is null && SnapshotData.Parse(remote.Json).IsEmpty;
            var localChanged = baseline is null ? !localEmpty : hash != baseline.Hash;
            var remoteChanged = baseline is null ? !remoteEmpty : remote.Revision != baseline.Revision;
            if (localChanged && remoteChanged)
            {
                if (remote.Revision is null) throw new InvalidDataException("云端已重置，需要重新登录后确认数据。");
                SetConflict(remote.Json,remote.Revision,hash,local); return SyncOutcome.Conflict;
            }
            if (remoteChanged)
            {
                if (canApply is not null && !canApply()) throw new InvalidOperationException("正在编辑，稍后自动同步云端更新。");
                store.ApplySnapshot(remote.Json,hash); Save(remote.Revision,SnapshotData.Hash(store.Snapshot())); return SyncOutcome.Downloaded;
            }
            if (localChanged)
            {
                try
                {
                    var response = await client.UploadAsync(JsonNode.Parse(local)!.AsObject(),remote.Revision,cancellation);
                    var revision = response["revision"]?.GetValue<string>() ?? throw new InvalidDataException("上传响应缺少版本号。");
                    Save(revision,hash); return SyncOutcome.Uploaded;
                }
                catch (CloudConflictException)
                {
                    var latest = Remote(await client.DownloadAsync(cancellation));
                    SetConflict(latest.Json, latest.Revision ?? throw new InvalidDataException("云端版本号无效。"),hash,local); return SyncOutcome.Conflict;
                }
            }
            return SyncOutcome.Unchanged;
        }
        finally { gate.Release(); }
    }
    public async Task<SyncOutcome> ResolveAsync(bool useLocal, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var conflict = Conflict ?? throw new InvalidOperationException("没有待处理的同步冲突。");
            if (SnapshotData.Hash(store.Snapshot()) != conflict.LocalHash) { Conflict = null; throw new InvalidOperationException("本地数据已有新修改，请重新同步后再选择。"); }
            if (useLocal)
            {
                store.WriteBackup(conflict.RemoteSnapshot,"cloud-conflict"); store.Backup();
                try
                {
                    var uploaded = await client.UploadAsync(JsonNode.Parse(store.Snapshot())!.AsObject(),conflict.Revision,cancellation);
                    Save(uploaded["revision"]?.GetValue<string>() ?? throw new InvalidDataException("上传响应缺少版本号。"),conflict.LocalHash);
                }
                catch (CloudConflictException) { Conflict = null; throw; }
            }
            else
            {
                // Re-read before replacing so a decision cannot silently roll back a newer remote revision.
                var latest = Remote(await client.DownloadAsync(cancellation));
                if (latest.Revision != conflict.Revision) { Conflict = null; throw new InvalidOperationException("云端再次更新，请重新同步后再选择。"); }
                if (canApply is not null && !canApply()) throw new InvalidOperationException("正在编辑，请保存后处理冲突。");
                store.ApplySnapshot(conflict.RemoteSnapshot,conflict.LocalHash); Save(conflict.Revision,SnapshotData.Hash(store.Snapshot()));
            }
            Conflict = null; return useLocal ? SyncOutcome.Uploaded : SyncOutcome.Downloaded;
        }
        finally { gate.Release(); }
    }
}
