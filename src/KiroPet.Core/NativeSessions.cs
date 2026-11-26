using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace KiroPet {
    [JsonConverter(typeof(JsonStringEnumConverter<CliHost>))]
    public enum CliHost { Unknown, Standalone, Herdr }

    public sealed class NativeSession {
        public string Key { get; set; }
        public string SessionId { get; set; }
        public string Source { get; set; }
        public CliHost Host { get; set; }
        public int OwnerPid { get; set; }
        public long OwnerStartedUtcTicks { get; set; }
        public long LeaseAcquiredAt { get; set; }
        public string Title { get; set; }
        public string Workspace { get; set; }
        public string WorkspaceKey { get; set; }
        public string Status { get; set; } = "idle";
        public string CompletionId { get; set; }
        public long Timestamp { get; set; }
        public bool Closed { get; set; }
        public bool Disconnected { get; set; }
        public string Event { get; set; }
    }

    public sealed class NativeSnapshot {
        public NativeSession[] Sessions { get; set; } = Array.Empty<NativeSession>();
        public string[] Health { get; set; } = Array.Empty<string>();
        public long Version { get; set; }
        public bool Recovering { get; set; }
    }

    internal enum TurnStatus { Idle, Working, Waiting, Done, Error }

    internal sealed class NativeEvent {
        public string Type, Execution, ParentExecution, TurnId, Interaction, Outcome, Option, ChildId;
        public string Reason;
        public long Timestamp;
        public static NativeEvent Parse(JsonElement envelope) {
            if (!NativeReader.TryObject(envelope, "payload", out var payload)) return null;
            string type = NativeReader.Text(payload, "type");
            if (!new[] { "turn_start", "turn_end", "pending_interaction", "interaction_resolved", "sub_agent_start" }.Contains(type)) return null;
            return new NativeEvent {
                Type = type, Timestamp = NativeReader.Time(envelope, "timestamp"),
                Execution = NativeReader.Text(payload, "executionId"), ParentExecution = NativeReader.Text(payload, "parentExecutionId"),
                TurnId = NativeReader.Text(payload, "turnId"), Interaction = NativeReader.Text(payload, "toolCallId"),
                Outcome = NativeReader.Text(payload, "outcome"), Option = NativeReader.Text(payload, "selectedOption"),
                ChildId = NativeReader.Text(payload, "subSessionId"), Reason = NativeReader.Text(payload, "stopReason")
            };
        }
    }

    internal sealed class Turn {
        public string Execution;
        public string TurnId;
        public long Started;
        public long Updated;
        public string LastEvent;
        public TurnStatus Status;
        public bool Finished;
        public bool ParentEnded;
        public readonly HashSet<string> Pending = new HashSet<string>();

        public void Consume(NativeEvent record, string child, string rootExecution) {
            long time = record.Timestamp;
            if (time <= 0) return;
            string type = record.Type;
            string execution = child == null ? record.Execution : rootExecution;
            if (child == null && type == "turn_start") {
                if (string.IsNullOrEmpty(execution) || execution == Execution || time < Started) return;
                Execution = execution;
                TurnId = record.TurnId ?? execution;
                Started = time;
                Pending.Clear();
                Finished = false;
                ParentEnded = false;
                Status = TurnStatus.Working;
            } else {
                if (Execution == null || Finished || time < Started ||
                    (execution != Execution && record.ParentExecution != Execution)) return;
                string interaction = record.Interaction;
                string interactionKey = (child ?? "parent") + ":" + interaction;
                if (type == "pending_interaction" && !string.IsNullOrEmpty(interaction)) {
                    Pending.Add(interactionKey);
                    Status = TurnStatus.Waiting;
                } else if (type == "interaction_resolved" && !string.IsNullOrEmpty(interaction)) {
                    if (!Pending.Remove(interactionKey)) return;
                    string outcome = record.Outcome;
                    string option = record.Option;
                    bool rejected = new[] { "cancelled", "canceled", "dismissed", "rejected", "denied" }.Contains(outcome) ||
                        (option != null && Regex.IsMatch(option, "^(reject|deny)($|_)"));
                    if (rejected) {
                        Pending.Clear();
                        Status = TurnStatus.Idle;
                        Finished = true;
                    } else {
                        Status = Pending.Count > 0 ? TurnStatus.Waiting : ParentEnded ? TurnStatus.Done : TurnStatus.Working;
                        Finished = ParentEnded && Pending.Count == 0;
                    }
                } else if (child == null && type == "turn_end") {
                    string reason = record.Reason;
                    if (new[] { "error", "failed", "content_filtered", "max_tokens", "model_context_window_exceeded" }.Contains(reason)) {
                        Pending.Clear();
                        Status = TurnStatus.Error;
                        Finished = true;
                    } else if (new[] { "cancelled", "canceled", "aborted" }.Contains(reason)) {
                        Pending.Clear();
                        Status = TurnStatus.Idle;
                        Finished = true;
                    } else if (new[] { "end_turn", "completed", "complete", "stop" }.Contains(reason)) {
                        ParentEnded = true;
                        Status = Pending.Count > 0 ? TurnStatus.Waiting : TurnStatus.Done;
                        Finished = Pending.Count == 0;
                    } else return;
                } else return;
            }
            Updated = Math.Max(Updated, time);
            LastEvent = type;
        }
    }

    internal sealed class EventTail {
        public readonly string File;
        public readonly string BoundExecution;
        public readonly string RootExecution;
        public long Offset;
        private long creation;
        private Decoder decoder = new UTF8Encoding(false, true).GetDecoder();
        private StringBuilder pending = new StringBuilder();
        private bool skipping;
        private byte[] fingerprint;
        private int fingerprintSize;
        private long writeStamp;
        public bool Partial => pending.Length > 0 || skipping;
        public EventTail(string file, string boundExecution = null, string rootExecution = null) {
            File = file;
            BoundExecution = boundExecution;
            RootExecution = rootExecution;
        }
        public bool Behind {
            get { return System.IO.File.Exists(File) && new FileInfo(File).Length > Offset; }
        }
        public List<NativeEvent> Read(out bool rotated) {
            var records = new List<NativeEvent>();
            rotated = false;
            var info = new FileInfo(File);
            if (!info.Exists) return records;
            long stamp = info.CreationTimeUtc.Ticks;
            if (info.Length == Offset && creation == stamp && writeStamp == info.LastWriteTimeUtc.Ticks) return records;
            writeStamp = info.LastWriteTimeUtc.Ticks;
            if (info.Length < Offset || (creation != 0 && creation != stamp)) {
                Offset = 0;
                decoder.Reset();
                pending.Clear();
                skipping = false;
                fingerprint = null;
                rotated = true;
            }
            creation = stamp;
            using (var stream = new FileStream(File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                if (fingerprint != null && Offset > 0 && !rotated) {
                    var prefix = new byte[fingerprintSize];
                    int count = stream.Read(prefix, 0, prefix.Length);
                    if (count != prefix.Length || !SHA256.HashData(prefix).SequenceEqual(fingerprint)) {
                        Offset = 0;
                        decoder.Reset();
                        pending.Clear();
                        skipping = false;
                        fingerprint = null;
                        rotated = true;
                    }
                }
                stream.Seek(Offset, SeekOrigin.Begin);
                if (stream.Length == Offset) return records;
                var bytes = new byte[65536];
                var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
                int budget = 524288;
                while (budget > 0) {
                    int count = stream.Read(bytes, 0, Math.Min(bytes.Length, budget));
                    if (count == 0) break;
                    Offset += count;
                    budget -= count;
                    int decoded = decoder.GetChars(bytes, 0, count, chars, 0, false);
                    for (int i = 0; i < decoded; i++) {
                        if (chars[i] == '\n') {
                            if (!skipping && pending.Length > 0) {
                                try {
                                    using (var doc = JsonDocument.Parse(pending.ToString())) {
                                        var record = NativeEvent.Parse(doc.RootElement);
                                        if (record != null && (BoundExecution == null ||
                                            record.Execution == BoundExecution || record.ParentExecution == BoundExecution)) records.Add(record);
                                    }
                                } catch (JsonException) {}
                            }
                            pending.Clear();
                            skipping = false;
                        } else if (!skipping) {
                            pending.Append(chars[i]);
                            if (pending.Length > 4194304) { pending.Clear(); skipping = true; }
                        }
                    }
                }
                if (Offset > 0 && (fingerprint == null || rotated)) {
                    fingerprintSize = (int)Math.Min(512, Offset);
                    var prefix = new byte[fingerprintSize];
                    stream.Seek(0, SeekOrigin.Begin);
                    int count = stream.Read(prefix, 0, prefix.Length);
                    if (count == prefix.Length) fingerprint = SHA256.HashData(prefix);
                }
            }
            return records;
        }
    }

    internal sealed class Tracked {
        public NativeSession Metadata;
        public EventTail Main;
        public Turn Turn = new Turn();
        public readonly Dictionary<string, EventTail> Children = new Dictionary<string, EventTail>();
        public readonly List<Tuple<NativeEvent, EventTail>> Backlog = new List<Tuple<NativeEvent, EventTail>>();
        public bool Recovering = true;
        public bool Live;
        public bool WasLive;
        public long LeaseAcquired;
        public bool InitialIdle;
    }

    public sealed class NativeReader : IDisposable {
        private readonly string root;
        private readonly string cache;
        private readonly bool persist;
        private readonly Dictionary<string, Tracked> tracked = new Dictionary<string, Tracked>();
        private readonly Dictionary<string, NativeSession> remembered = new Dictionary<string, NativeSession>();
        private readonly Dictionary<string, string> metadataVersions = new Dictionary<string, string>();
        private readonly List<string> health = new List<string>();
        private DateTime nextDiscovery = DateTime.MinValue;
        private long version;
        private string lastJson;
        private Task<NativeSnapshot> task;
        private NativeSnapshot latest = new NativeSnapshot { Recovering = true, Health = new[] { "Discovering Kiro sessions..." } };
        private bool disposed;
        private readonly DateTime beganAt = DateTime.UtcNow;
        private readonly Func<int, CliHost> classifyHost;

        public NativeReader(string kiroHome, string cachePath, bool persistCache, Func<int, CliHost> hostClassifier = null) {
            root = Path.GetFullPath(kiroHome);
            cache = cachePath;
            persist = persistCache;
            classifyHost = hostClassifier ?? ProcessAncestry.Classify;
            if (System.IO.File.Exists(cache)) {
                try {
                    foreach (var session in JsonSerializer.Deserialize<NativeSession[]>(System.IO.File.ReadAllText(cache)) ?? Array.Empty<NativeSession>()) {
                        if (session.SessionId != null && session.Key == "kiro:" + session.SessionId &&
                            !PhrasePacks.IsJobWorkspace(session.Workspace) &&
                            (session.Host == CliHost.Herdr || (session.Status == "done" || session.Status == "error") &&
                            !string.IsNullOrEmpty(session.CompletionId))) {
                            session.Closed = true;
                            session.Disconnected = true;
                            remembered[session.SessionId] = session;
                        }
                    }
                } catch (JsonException) {} catch (IOException) {}
            }
        }

        public NativeSnapshot Poll() {
            if (disposed) return latest;
            if (task != null && task.IsCompleted) {
                var finished = task;
                task = null;
                try { latest = finished.GetAwaiter().GetResult(); }
                catch (IOException) { latest.Health = new[] { "Native session files are temporarily unavailable; retrying." }; }
                catch (UnauthorizedAccessException) { latest.Health = new[] { "Cannot read Kiro's session store; retrying." }; }
            }
            if (task == null) task = Task.Run(() => ReadNow());
            return latest;
        }

        public NativeSnapshot ReadNow() {
            if (DateTime.UtcNow >= nextDiscovery) { Discover(); nextDiscovery = DateTime.UtcNow.AddSeconds(1); }
            foreach (var item in tracked.Values) {
                if (item.Metadata.Host == CliHost.Herdr) continue;
                if (!item.Live && !item.Recovering && !item.Main.Behind) continue;
                try { ReadEvents(item); }
                catch (IOException) { health.Add("A session event file is temporarily unavailable; retrying."); }
                catch (DecoderFallbackException) { health.Add("A session contains invalid UTF-8; status is unavailable."); }
            }
            var sessions = new List<NativeSession>(remembered.Values.Where(s => !tracked.ContainsKey(s.SessionId)));
            foreach (var item in tracked.Values) {
                if (item.Metadata.Host == CliHost.Herdr) {
                    var marker = Copy(item.Metadata);
                    marker.Status = "idle";
                    marker.CompletionId = null;
                    marker.Closed = marker.Disconnected = !item.Live;
                    sessions.Add(marker);
                    remembered[marker.SessionId] = Copy(marker);
                    continue;
                }
                if (!item.Live && !item.WasLive && item.Turn.Updated < new DateTimeOffset(beganAt).ToUnixTimeMilliseconds()) continue;
                var session = Copy(item.Metadata);
                session.Closed = !item.Live;
                session.Disconnected = !item.Live;
                bool previousOwnerTurn = item.Live && item.Turn.Started > 0 && item.Turn.Started + 1000 < item.LeaseAcquired &&
                    (item.Turn.Status == TurnStatus.Working || item.Turn.Status == TurnStatus.Waiting);
                session.Status = item.Recovering ? "recovering" : previousOwnerTurn ? "idle" : State(item.Turn.Status);
                session.Timestamp = item.Turn.Updated;
                session.Event = item.Turn.LastEvent;
                session.CompletionId = session.Status == "done" || session.Status == "error" ? item.Turn.Execution + ":" + item.Turn.TurnId : null;
                sessions.Add(session);
                if (session.Status == "done" || session.Status == "error") remembered[session.SessionId] = Copy(session);
                else remembered.Remove(session.SessionId);
            }
            sessions = sessions.OrderBy(s => s.Key, StringComparer.Ordinal).ToList();
            string json = JsonSerializer.Serialize(sessions);
            if (json != lastJson) {
                lastJson = json;
                version++;
                if (persist && !sessions.Any(s => s.Host != CliHost.Herdr && s.Status == "recovering")) SaveCache(json);
            }
            var notes = health.Distinct().ToList();
            if (!Directory.Exists(Path.Combine(root, "sessions"))) notes.Add("No native Kiro session store found. Start Kiro IDE or CLI V3.");
            else if (!tracked.Values.Any(s => s.Live && s.Metadata.Host != CliHost.Herdr))
                notes.Add("No live Kiro sessions. The pet is resting; unread completions remain available.");
            else notes.Add("Automatically monitoring Kiro IDE and CLI V3. No workspace hooks are needed.");
            return new NativeSnapshot {
                Sessions = sessions.Where(s => s.Host != CliHost.Herdr).ToArray(), Health = notes.ToArray(), Version = version,
                Recovering = tracked.Values.Any(s => s.Live && s.Metadata.Host != CliHost.Herdr && s.Recovering)
            };
        }

        private void Discover() {
            health.Clear();
            foreach (var item in tracked.Values) { item.Live = false; }
            string leaseRoot = Path.Combine(root, "session-leases");
            string sessionsRoot = Path.Combine(root, "sessions");
            if (!Directory.Exists(leaseRoot) || !Directory.Exists(sessionsRoot)) return;
            var live = new Dictionary<string, string>();
            var acquiredTimes = new Dictionary<string, long>();
            var ownerPids = new Dictionary<string, int>();
            var hosts = new Dictionary<string, CliHost>();
            var hostsByPid = new Dictionary<int, CliHost>();
            var knownSources = new Dictionary<string, string>();
            var processes = new Dictionary<int, DateTime?>();
            foreach (string file in Directory.EnumerateFiles(leaseRoot, "sess_*.lease")) {
                try {
                    using (var doc = JsonDocument.Parse(System.IO.File.ReadAllText(file))) {
                        var lease = doc.RootElement;
                        string client = Text(lease, "clientName") ?? "";
                        string source = client == "kiro-ide" ? "ide" : client.IndexOf("kiro", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            client.IndexOf("cli", StringComparison.OrdinalIgnoreCase) >= 0 ? "cli" : null;
                        if (source == null) continue;
                        string id = Path.GetFileNameWithoutExtension(file);
                        knownSources[id] = source;
                        if (!lease.TryGetProperty("pid", out var pidElement) || pidElement.ValueKind != JsonValueKind.Number ||
                            !pidElement.TryGetInt32(out int pid) || pid <= 0) continue;
                        if (!processes.ContainsKey(pid)) {
                            try {
                                using (var process = Process.GetProcessById(pid)) {
                                    processes[pid] = process.HasExited ? (DateTime?)null : process.StartTime.ToUniversalTime();
                                }
                            } catch (ArgumentException) { processes[pid] = null; }
                            catch (System.ComponentModel.Win32Exception) { processes[pid] = null; }
                        }
                        long acquired = Time(lease, "acquiredAt");
                        if (!processes[pid].HasValue || acquired <= 0 ||
                            processes[pid].Value > DateTimeOffset.FromUnixTimeMilliseconds(acquired).UtcDateTime.AddSeconds(1)) continue;
                        if (lease.TryGetProperty("parentPid", out var parentElement) && parentElement.ValueKind == JsonValueKind.Number &&
                            parentElement.TryGetInt32(out int parentPid) && parentPid > 0) {
                            if (!processes.ContainsKey(parentPid)) {
                                try {
                                    using (var parent = Process.GetProcessById(parentPid)) {
                                        processes[parentPid] = parent.HasExited ? (DateTime?)null : parent.StartTime.ToUniversalTime();
                                    }
                                } catch (ArgumentException) { processes[parentPid] = null; }
                                catch (System.ComponentModel.Win32Exception) { processes[parentPid] = null; }
                            }
                            if (!processes[parentPid].HasValue || processes[parentPid].Value >
                                DateTimeOffset.FromUnixTimeMilliseconds(acquired).UtcDateTime.AddSeconds(1)) continue;
                        }
                        live[id] = source;
                        acquiredTimes[id] = acquired;
                        ownerPids[id] = pid;
                        if (source == "cli") {
                            if (!hostsByPid.TryGetValue(pid, out var host)) {
                                host = classifyHost(pid);
                                hostsByPid[pid] = host;
                            }
                            hosts[id] = host;
                        }
                    }
                } catch (IOException) {} catch (JsonException) {}
            }
            foreach (string group in Directory.EnumerateDirectories(sessionsRoot)) {
                foreach (string directory in Directory.EnumerateDirectories(group, "sess_*")) {
                    string id = Path.GetFileName(directory);
                    bool isLive = live.TryGetValue(id, out string source);
                    if (!isLive && remembered.TryGetValue(id, out var cached) && cached.Host == CliHost.Herdr) continue;
                    if (!isLive && (!knownSources.TryGetValue(id, out source) ||
                        System.IO.File.GetLastWriteTimeUtc(Path.Combine(directory, "messages.jsonl")) < beganAt)) continue;
                    string metadata = Path.Combine(directory, "session.json");
                    try {
                        var info = new FileInfo(metadata);
                        string stamp = info.Length + ":" + info.LastWriteTimeUtc.Ticks + ":" + source;
                        if (!tracked.TryGetValue(id, out var item) || !metadataVersions.TryGetValue(id, out var previous) || previous != stamp) {
                            using (var doc = JsonDocument.Parse(System.IO.File.ReadAllText(metadata))) {
                                var data = doc.RootElement;
                                if (Text(data, "schemaVersion") != "1.0.0" ||
                                    !data.TryGetProperty("dataModelVersion", out var model) || model.ValueKind != JsonValueKind.Number ||
                                    !model.TryGetInt32(out int modelVersion) || modelVersion != 1) {
                                    health.Add("Unsupported Kiro session schema in " + id + ".");
                                    continue;
                                }
                                if (Text(data, "id") != id || !data.TryGetProperty("workspacePaths", out var paths) ||
                                    paths.ValueKind != JsonValueKind.Array || paths.GetArrayLength() == 0 ||
                                    paths[0].ValueKind != JsonValueKind.String) continue;
                                string workspace = Path.GetFullPath(paths[0].GetString());
                                if (PhrasePacks.IsJobWorkspace(workspace)) {
                                    tracked.Remove(id);
                                    remembered.Remove(id);
                                    metadataVersions.Remove(id);
                                    continue;
                                }
                                CliHost previousHost = item?.Metadata.Host ??
                                    (remembered.TryGetValue(id, out var cachedSession) ? cachedSession.Host : CliHost.Unknown);
                                var session = new NativeSession {
                                    Key = "kiro:" + id, SessionId = id, Source = source, Title = Text(data, "title"),
                                    Workspace = workspace, WorkspaceKey = Hash(workspace), Status = "idle",
                                    Host = hosts.TryGetValue(id, out var detected) && detected != CliHost.Unknown ? detected : previousHost
                                };
                                if (item == null) {
                                    item = new Tracked {
                                        Main = new EventTail(Path.Combine(directory, "messages.jsonl")),
                                        WasLive = remembered.ContainsKey(id)
                                    };
                                    tracked[id] = item;
                                }
                                item.Metadata = session;
                                item.InitialIdle = Text(data, "status") == "idle";
                                metadataVersions[id] = stamp;
                            }
                        }
                        item.Live = isLive;
                        item.WasLive |= isLive;
                        if (isLive) {
                            if (hosts.TryGetValue(id, out var detected) && detected != CliHost.Unknown)
                                item.Metadata.Host = detected;
                            item.LeaseAcquired = acquiredTimes[id];
                            item.Metadata.OwnerPid = ownerPids[id];
                            item.Metadata.OwnerStartedUtcTicks = processes[ownerPids[id]].Value.Ticks;
                            item.Metadata.LeaseAcquiredAt = acquiredTimes[id];
                        }
                    } catch (IOException) { health.Add("Waiting for a Kiro session's metadata to become readable."); }
                    catch (JsonException) { health.Add("Waiting for complete Kiro session metadata."); }
                }
            }
        }

        private void ReadEvents(Tracked item) {
            if (!System.IO.File.Exists(item.Main.File)) {
                if (item.Turn.Execution == null && item.InitialIdle && !remembered.ContainsKey(item.Metadata.SessionId)) {
                    item.Recovering = false;
                } else health.Add("Waiting for a Kiro session's native event file.");
                return;
            }
            var main = item.Main.Read(out bool rotated);
            if (rotated) {
                item.Turn = new Turn();
                item.Children.Clear();
                item.Backlog.Clear();
                item.Recovering = true;
            }
            item.Backlog.AddRange(main.Select(record => Tuple.Create(record, (EventTail)null)));
            foreach (var record in main) DeclareChild(item, record, null);
            foreach (var child in item.Children.Values.ToArray()) {
                if (!System.IO.File.Exists(child.File)) continue;
                foreach (var record in child.Read(out _)) {
                    item.Backlog.Add(Tuple.Create(record, child));
                    DeclareChild(item, record, child);
                }
            }
            bool behind = item.Main.Behind || item.Main.Partial || item.Children.Values.Any(t => t.Behind || t.Partial);
            if (behind) return;
            foreach (var record in item.Backlog.OrderBy(r => r.Item1.Timestamp).ThenBy(r => EventOrder(r.Item1.Type))) {
                item.Turn.Consume(record.Item1, record.Item2?.File, record.Item2?.RootExecution);
            }
            item.Backlog.Clear();
            item.Recovering = false;
        }
        private static int EventOrder(string type) {
            return type == "turn_start" ? 0 : type == "pending_interaction" ? 1 : type == "interaction_resolved" ? 2 : type == "turn_end" ? 3 : 1;
        }

        private void DeclareChild(Tracked item, NativeEvent record, EventTail parent) {
            if (record.Type != "sub_agent_start") return;
            string id = record.ChildId;
            string execution = record.ParentExecution;
            if (id == null || execution == null || !Regex.IsMatch(id, "^[a-zA-Z0-9_-]{1,160}$") || item.Children.ContainsKey(id)) return;
            item.Children[id] = new EventTail(Path.Combine(Path.GetDirectoryName(item.Main.File), "sub-executions", id + ".jsonl"), execution, parent?.RootExecution ?? execution);
        }

        private void SaveCache(string json) {
            string temporary = cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(cache));
                System.IO.File.WriteAllText(temporary, json, new UTF8Encoding(false));
                for (int attempt = 0; attempt < 5; attempt++) {
                    try { System.IO.File.Move(temporary, cache, true); return; }
                    catch (IOException) { if (attempt == 4) throw; System.Threading.Thread.Sleep(40); }
                    catch (UnauthorizedAccessException) { if (attempt == 4) throw; System.Threading.Thread.Sleep(40); }
                }
            } catch (IOException) { health.Add("Could not save native session summaries."); }
            finally { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); }
        }

        private static NativeSession Copy(NativeSession session) {
            return new NativeSession {
                Key = session.Key, SessionId = session.SessionId, Source = session.Source, Title = session.Title, OwnerPid = session.OwnerPid,
                Host = session.Host,
                OwnerStartedUtcTicks = session.OwnerStartedUtcTicks, LeaseAcquiredAt = session.LeaseAcquiredAt,
                Workspace = session.Workspace, WorkspaceKey = session.WorkspaceKey, Status = session.Status,
                CompletionId = session.CompletionId, Timestamp = session.Timestamp, Closed = session.Closed,
                Disconnected = session.Disconnected, Event = session.Event
            };
        }
        private static string State(TurnStatus status) {
            return status == TurnStatus.Waiting ? "waiting" : status.ToString().ToLowerInvariant();
        }
        private static string Hash(string workspace) {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(workspace).TrimEnd('\\').ToLowerInvariant()))).Substring(0, 16).ToLowerInvariant();
        }
        internal static string Text(JsonElement data, string key) {
            return data.ValueKind == JsonValueKind.Object && data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        internal static bool TryObject(JsonElement data, string key, out JsonElement value) {
            value = default;
            return data.ValueKind == JsonValueKind.Object && data.TryGetProperty(key, out value) && value.ValueKind == JsonValueKind.Object;
        }
        internal static long Time(JsonElement data, string key) {
            string text = Text(data, key);
            if (text != null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)) return time.ToUnixTimeMilliseconds();
            return data.ValueKind == JsonValueKind.Object && data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt64(out long number) && number >= -62135596800000 && number <= 253402300799999 ? number : 0;
        }
        public void Dispose() {
            disposed = true;
            if (task != null) { latest = task.GetAwaiter().GetResult(); task = null; }
        }
    }
}
