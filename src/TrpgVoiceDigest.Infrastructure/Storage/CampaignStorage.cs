using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Infrastructure.Storage;

public sealed partial class CampaignStorage
{
    private readonly CampaignPaths _paths;

    public CampaignStorage(CampaignPaths paths)
    {
        _paths = paths;
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(_paths.CampaignDirectory);
        Directory.CreateDirectory(_paths.SystemDirectory);
        Directory.CreateDirectory(_paths.AudioSegmentsDirectory);
        Directory.CreateDirectory(_paths.SpeakerEmbeddingsDirectory);
    }

    public string? GetOldestAudioSegmentPath()
    {
        if (!Directory.Exists(_paths.AudioSegmentsDirectory)) return null;

        var files = Directory.GetFiles(_paths.AudioSegmentsDirectory, "*.wav");
        if (files.Length == 0) return null;

        Array.Sort(files, StringComparer.Ordinal);
        return files[0];
    }

    internal void AppendToDialogueLog(DateTimeOffset capturedAt, string text, string? speaker = null)
    {
        var speakerPart = speaker is not null ? $"[{speaker}]: " : "";
        var line = $"[{capturedAt:HH:mm:ss}] {speakerPart}{text}";
        File.AppendAllText(_paths.DialogueLogPath, line + Environment.NewLine);
    }

    public Dictionary<string, string> LoadSpeakerNameMap()
    {
        if (!File.Exists(_paths.CampaignSpeakersPath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var json = File.ReadAllText(_paths.CampaignSpeakersPath);
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                  ?? new Dictionary<string, string>();
        return new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
    }

    internal void SaveSpeakerNameMap(Dictionary<string, string> map)
    {
        var json = JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_paths.CampaignSpeakersPath, json);
    }

    internal void EnsureSpeakerInMap(Dictionary<string, string> map, string speakerId)
    {
        if (string.IsNullOrWhiteSpace(speakerId)) return;
        if (map.ContainsKey(speakerId)) return;

        map[speakerId] = speakerId;
        SaveSpeakerNameMap(map);
    }

    public string GetSpeakerEmbeddingsDirectory()
    {
        return _paths.SpeakerEmbeddingsDirectory;
    }

    public int LoadProcessedSequence()
    {
        if (!File.Exists(_paths.ProcessedSequencePath)) return -1;

        var text = File.ReadAllText(_paths.ProcessedSequencePath).Trim();
        return int.TryParse(text, out var seq) ? seq : -1;
    }

    internal void SaveProcessedSequence(int sequence)
    {
        File.WriteAllText(_paths.ProcessedSequencePath, sequence.ToString());
    }

    internal string ReadDialogueLog()
    {
        return File.Exists(_paths.DialogueLogPath) ? File.ReadAllText(_paths.DialogueLogPath) : string.Empty;
    }

    internal string ComputeDialogueLogHash(string dialogueLogText)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(dialogueLogText));
        return Convert.ToHexString(bytes);
    }

    internal void SaveMergedDialogue(string content)
    {
        File.WriteAllText(_paths.MergedDialoguePath, content);
    }

    internal string ReadMergedDialogue()
    {
        if (!File.Exists(_paths.MergedDialoguePath)) return string.Empty;

        return File.ReadAllText(_paths.MergedDialoguePath);
    }

    public IncrementalDigestContainer LoadRefinementState(ILogService? log = null)
    {
        var container = new IncrementalDigestContainer("refinement", "跑团剧本精炼", log);

        if (!File.Exists(_paths.RefinementStatePath)) return container;

        var json = File.ReadAllText(_paths.RefinementStatePath);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("entries", out var entriesElement) &&
            entriesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in entriesElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var key = item.TryGetProperty("key", out var keyElement) && keyElement.TryGetInt32(out var k) ? k : 0;
                var text = item.TryGetProperty("content", out var textElement) ? textElement.GetString() ?? "" : "";
                if (key > 0 && !string.IsNullOrWhiteSpace(text))
                    container.AddEntry(text, afterKey: key - 1);
            }

            return container;
        }

        if (!root.TryGetProperty("sentences", out var sentencesElement) ||
            sentencesElement.ValueKind != JsonValueKind.Array)
            return container;

        foreach (var item in sentencesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var number = item.TryGetProperty("number", out var numElement) && numElement.TryGetInt32(out var n) ? n : 0;
            var text = item.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? "" : "";
            if (number > 0 && !string.IsNullOrWhiteSpace(text))
                container.AddEntry(text, afterKey: number - 1);
        }

        return container;
    }

    internal void SaveRefinementState(IncrementalDigestContainer container)
    {
        var json = container.ExportJson();
        File.WriteAllText(_paths.RefinementStatePath, json);
    }

    internal string? LoadRefinementCursor()
    {
        if (!File.Exists(_paths.RefinementCursorPath)) return null;

        return File.ReadAllText(_paths.RefinementCursorPath).Trim();
    }

    internal void SaveRefinementCursor(string hash)
    {
        File.WriteAllText(_paths.RefinementCursorPath, hash);
    }

    internal void AppendRefinementEditLog(
        DateTimeOffset timestamp,
        string transcriptHash,
        string llmResponse,
        IReadOnlyList<RefineOperation> operations)
    {
        var payload = new
        {
            timestamp = timestamp.ToString("O"),
            transcriptHash,
            operationCount = operations.Count,
            isEmpty = operations.All(x => x.Action == RefineAction.Empty),
            operations = operations.Select(o => new
            {
                action = o.Action.ToString(),
                number = o.Number,
                text = o.Text
            }),
            llmResponse
        };

        var line = JsonSerializer.Serialize(payload);
        File.AppendAllText(_paths.RefinementEditLogPath, line + Environment.NewLine);
    }

    internal void ExportRefinementMarkdown(IncrementalDigestContainer container)
    {
        var md = container.ExportMarkdown();
        File.WriteAllText(_paths.RefinementMarkdownPath, md);
    }

    internal ConsistencyState LoadConsistencyState()
    {
        if (!File.Exists(_paths.ConsistencyJsonPath)) return new ConsistencyState();

        var json = File.ReadAllText(_paths.ConsistencyJsonPath);
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            var state = new ConsistencyState();
            if (dict is not null)
                foreach (var (key, value) in dict)
                    state.Entries.Add(new ConsistencyEntry { Key = key, Value = value });
            return state;
        }
        catch
        {
            return new ConsistencyState();
        }
    }

    internal void SaveConsistencyState(ConsistencyState state)
    {
        var dict = new Dictionary<string, string>();
        foreach (var entry in state.Entries)
            dict[entry.Key] = entry.Value;
        var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_paths.ConsistencyJsonPath, json);
        File.WriteAllText(_paths.ConsistencyMarkdownPath, state.BuildMarkdown());
    }

    internal void AppendConsistencyEditLog(DateTimeOffset timestamp, string response,
        IReadOnlyList<ConsistencyOperation> operations)
    {
        var payload = new
        {
            timestamp = timestamp.ToString("O"),
            operationCount = operations.Count,
            operations = operations.Select(o => new
            {
                action = o.Action.ToString(),
                key = o.Key,
                value = o.Value
            }),
            response
        };
        var line = JsonSerializer.Serialize(payload);
        File.AppendAllText(_paths.ConsistencyJsonPath.Replace(".json", "_edit_log.jsonl"), line + Environment.NewLine);
    }

    internal static string MergeConsecutiveSpeakerLines(string dialogueLog)
    {
        if (string.IsNullOrWhiteSpace(dialogueLog)) return string.Empty;

        var lines = dialogueLog.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var entries = new List<(string Time, string Speaker, string Text)>();

        foreach (var line in lines)
        {
            var match = DialogueLogLineRegex().Match(line);
            if (!match.Success) continue;

            var time = match.Groups["time"].Value;
            var speaker = match.Groups["speaker"].Success ? match.Groups["speaker"].Value : "";
            var text = match.Groups["text"].Value.Trim();

            if (entries.Count > 0 &&
                string.Equals(entries[^1].Speaker, speaker, StringComparison.OrdinalIgnoreCase) &&
                speaker.Length > 0)
            {
                var prev = entries[^1];
                entries[^1] = (prev.Time, prev.Speaker, prev.Text + "。" + text);
            }
            else
            {
                entries.Add((time, speaker, text));
            }
        }

        if (entries.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i < entries.Count; i++)
        {
            var (time, speaker, text) = entries[i];
            var number = i + 1;
            if (speaker.Length > 0)
                sb.AppendLine($"[{number}] [{speaker}] [{time}]: {text}");
            else
                sb.AppendLine($"[{number}] [{time}]: {text}");
        }

        return sb.ToString().TrimEnd();
    }

    [GeneratedRegex(@"^\[(?<time>\d{2}:\d{2}:\d{2})\]\s*(?:\[(?<speaker>[^\]]+)\]:?\s*)?(?<text>.+)$")]
    private static partial Regex DialogueLogLineRegex();
}
