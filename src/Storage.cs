using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

internal static class AppPaths
{
    internal static string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceFlow");
    internal static string History { get { return Path.Combine(Data, "History"); } }
    internal static string Logs { get { return Path.Combine(Data, "Logs"); } }
    internal static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VoiceFlow.local.v1");
    internal static byte[] Protect(byte[] value) { return ProtectedData.Protect(value, Entropy, DataProtectionScope.CurrentUser); }
    internal static byte[] Unprotect(byte[] value) { return ProtectedData.Unprotect(value, Entropy, DataProtectionScope.CurrentUser); }
    internal static void Atomic(string destination, Action<BinaryWriter> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        string temp = destination + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            { using (var writer = new BinaryWriter(file, Encoding.UTF8, true)) write(writer); file.Flush(true); }
            if (File.Exists(destination)) File.Replace(temp, destination, null); else File.Move(temp, destination);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

internal sealed class HistoryEntry
{
    internal string Id, Text, Status;
    internal DateTime Created;
    internal int Bytes;
    internal double Seconds { get { return Bytes / 32000.0; } }
    internal bool Confirmed { get { return Status == "complete" || Status == "clipboard"; } }
}

internal sealed class HistoryStore
{
    internal const int Limit = 20, MaxAudio = 32000 * 305;
    readonly string root;
    readonly object gate = new object();
    Task tail = Task.FromResult(0);
    internal HistoryStore(string directory) { root = directory; Directory.CreateDirectory(root); }
    string FileFor(string id) { Guid parsed; if (!Guid.TryParseExact(id, "N", out parsed)) throw new InvalidDataException(); return Path.Combine(root, id + ".vfh"); }
    Task Enqueue(Action action)
    {
        lock (gate) return tail = tail.ContinueWith(delegate { action(); }, TaskScheduler.Default);
    }
    internal Task Flush() { lock (gate) return tail; }
    static byte[] ReadBlob(BinaryReader reader, int limit)
    {
        int count = reader.ReadInt32(); if (count < 0 || count > limit) throw new InvalidDataException();
        byte[] bytes = reader.ReadBytes(count); if (bytes.Length != count) throw new EndOfStreamException(); return bytes;
    }
    static void Blob(BinaryWriter writer, byte[] bytes) { writer.Write(bytes.Length); writer.Write(bytes); }
    HistoryEntry Read(string file, bool withAudio, out byte[] audio)
    {
        audio = null;
        using (var reader = new BinaryReader(File.OpenRead(file)))
        {
            if (reader.ReadInt32() != 0x56464832) throw new InvalidDataException();
            using (var meta = new BinaryReader(new MemoryStream(AppPaths.Unprotect(ReadBlob(reader, 2 * 1024 * 1024))), Encoding.UTF8))
            {
                var item = new HistoryEntry { Id = Path.GetFileNameWithoutExtension(file), Created = new DateTime(meta.ReadInt64(), DateTimeKind.Utc), Status = meta.ReadString(), Text = meta.ReadString(), Bytes = meta.ReadInt32() };
                FileFor(item.Id);
                if (item.Bytes < 0 || item.Bytes > MaxAudio || item.Bytes % 2 != 0 || item.Text.Length > 200000 || !new[] { "complete", "clipboard", "empty", "interrupted" }.Contains(item.Status)) throw new InvalidDataException();
                if (withAudio) { audio = AppPaths.Unprotect(ReadBlob(reader, MaxAudio + 65536)); if (audio.Length != item.Bytes) throw new InvalidDataException(); }
                return item;
            }
        }
    }
    internal HistoryEntry[] List()
    {
        lock (gate)
        {
            var entries = new List<HistoryEntry>();
            foreach (string file in Directory.GetFiles(root, "*.vfh"))
            {
                try { byte[] ignored; entries.Add(Read(file, false, out ignored)); }
                catch { Diagnostics.Event("history-read-failed"); }
            }
            return entries.OrderByDescending(x => x.Created).ThenByDescending(x => x.Id).Take(Limit).ToArray();
        }
    }
    internal byte[] Audio(string id) { lock (gate) { byte[] audio; Read(FileFor(id), true, out audio); return audio; } }
    internal Task Save(string id, DateTime created, string text, string status, byte[] pcm)
    {
        if (pcm.Length > MaxAudio || pcm.Length % 2 != 0 || (text??"").Length>200000 || !new[]{"complete","clipboard","empty","interrupted"}.Contains(status)) throw new InvalidDataException("Invalid recording");
        return Enqueue(delegate { lock (gate) SaveNow(id, created, text, status, pcm); });
    }
    void SaveNow(string id, DateTime created, string text, string status, byte[] pcm)
    {
        AppPaths.Atomic(FileFor(id), delegate(BinaryWriter writer)
        {
            writer.Write(0x56464832);
            using (var memory = new MemoryStream())
            {
                using (var meta = new BinaryWriter(memory, Encoding.UTF8, true)) { meta.Write(created.ToUniversalTime().Ticks); meta.Write(status); meta.Write(text ?? ""); meta.Write(pcm.Length); }
                Blob(writer, AppPaths.Protect(memory.ToArray()));
            }
            Blob(writer, AppPaths.Protect(pcm));
        });
        string journal = Path.Combine(root, id + ".journal"); if (File.Exists(journal)) File.Delete(journal);
        Trim();
    }
    void Trim()
    {
        var files = Directory.GetFiles(root, "*.vfh").Select(file => new FileInfo(file)).OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
        // Keep the original recording date when a retry replaces its result.
        var valid = new List<HistoryEntry>();
        foreach (var file in files) { try { byte[] ignored; valid.Add(Read(file.FullName, false, out ignored)); } catch { File.Delete(file.FullName); } }
        foreach (var old in valid.OrderByDescending(x => x.Created).ThenByDescending(x => x.Id).Skip(Limit)) File.Delete(FileFor(old.Id));
    }
    internal Task Checkpoint(string id, DateTime created, byte[] delta)
    {
        if (delta.Length == 0) return Task.FromResult(0);
        return Enqueue(delegate
        {
            lock (gate)
            {
                string file = Path.Combine(root, id + ".journal"); FileFor(id);
                bool exists = File.Exists(file);
                using (var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                    { if (!exists) { writer.Write(0x56464A32); writer.Write(created.ToUniversalTime().Ticks); } Blob(writer, AppPaths.Protect(delta)); }
                    stream.Flush(true);
                }
            }
        });
    }
    internal void Recover()
    {
        lock (gate)
        {
            foreach (string temp in Directory.GetFiles(root, "*.tmp")) File.Delete(temp);
            foreach (string journal in Directory.GetFiles(root, "*.journal").OrderBy(File.GetLastWriteTimeUtc))
            {
                string id = Path.GetFileNameWithoutExtension(journal);
                try
                {
                    if (File.Exists(FileFor(id))) { File.Delete(journal); continue; }
                    using (var reader = new BinaryReader(File.OpenRead(journal)))
                    using (var pcm = new MemoryStream())
                    {
                        if (reader.ReadInt32() != 0x56464A32) throw new InvalidDataException();
                        var created = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
                        while (reader.BaseStream.Position < reader.BaseStream.Length)
                        {
                            try { byte[] part = AppPaths.Unprotect(ReadBlob(reader, MaxAudio + 65536)); if (part.Length % 2 != 0 || pcm.Length + part.Length > MaxAudio) break; pcm.Write(part, 0, part.Length); }
                            catch { break; } // A torn final checkpoint must not destroy earlier complete chunks.
                        }
                        if (pcm.Length > 0) { byte[] recovered = pcm.ToArray(); reader.Close(); SaveNow(id, created, "", "interrupted", recovered); }
                    }
                    if (File.Exists(journal)) File.Delete(journal);
                }
                catch { Diagnostics.Event("history-recovery-failed"); File.Delete(journal); }
            }
            Trim();
        }
    }
    internal Task Delete(string id)
    {
        return Enqueue(delegate { lock (gate) { File.Delete(FileFor(id)); File.Delete(Path.Combine(root, id + ".journal")); } });
    }
    internal Task Clear()
    {
        return Enqueue(delegate { lock (gate) foreach (string file in Directory.GetFiles(root)) if (new[] { ".vfh", ".journal", ".tmp" }.Contains(Path.GetExtension(file))) File.Delete(file); });
    }
}

internal static class Edition
{
    // The public build (csc /define:PUBLIC) has no Google account engine; it keeps the offline model and the user's own paid keys.
#if PUBLIC
    internal const bool Public = true;
#else
    internal const bool Public = false;
#endif
}

internal static class EngineChoice
{
    internal static readonly string[] Known = Edition.Public ? new[] { "local", "sarvam", "chariot" } : new[] { "google", "local", "sarvam", "chariot" }, Paid = { "sarvam", "chariot" };
    internal static string Label(string engine)
    {
        return engine == "google" ? "Google (সাবস্ক্রিপশন)" : engine == "local" ? "লোকাল মডেল (ইন্টারনেট ছাড়া)" : engine == "sarvam" ? "Sarvam (নিজের চাবি)" : "Chariot (নিজের চাবি)";
    }
    internal static string KeyPage(string engine) { return engine == "sarvam" ? "https://indus.sarvam.ai/key-management" : "https://platform.chariot.in"; }
    internal static string Company(string engine) { return engine == "sarvam" ? "Sarvam" : "Chariot"; }
    // Google needs its own sign-in (checked by the app); the others need their files or key.
    internal static bool Ready(string engine) { return engine == "google" || engine == "local" && LocalReady || Paid.Contains(engine) && KeyStore.Has(engine); }
    internal static string LocalModel { get { return Path.Combine(AppPaths.Data, "Models", "bangla-zipformer-int8"); } }
    internal static bool LocalReady { get { return new[] { "encoder.onnx", "decoder.onnx", "joiner.onnx", "tokens.txt" }.All(file => File.Exists(Path.Combine(LocalModel, file))); } }
    static string FileName { get { return Path.Combine(AppPaths.Data, "engine.txt"); } }
    // Anything missing or unknown falls back to the default engine, so a bad file can never stop dictation.
    internal static string Default { get { return Edition.Public ? "local" : "google"; } }
    internal static string Current
    {
        get { try { string value = File.ReadAllText(FileName).Trim(); return Known.Contains(value) ? value : Default; } catch { return Default; } }
        set { if (!Known.Contains(value)) throw new ArgumentException("Unknown voice engine"); AppPaths.Atomic(FileName, delegate(BinaryWriter writer) { writer.Write(Encoding.ASCII.GetBytes(value)); }); }
    }
}

internal static class SecretStore
{
    static string FileName { get { return Path.Combine(AppPaths.Data, "signin.vault"); } }
    internal static void Save(string value) { AppPaths.Atomic(FileName, delegate(BinaryWriter writer) { writer.Write(AppPaths.Protect(Encoding.UTF8.GetBytes(value))); }); }
    internal static string Load() { try { return Encoding.UTF8.GetString(AppPaths.Unprotect(File.ReadAllBytes(FileName))); } catch { return null; } }
    internal static void Clear() { if (File.Exists(FileName)) File.Delete(FileName); }
}
