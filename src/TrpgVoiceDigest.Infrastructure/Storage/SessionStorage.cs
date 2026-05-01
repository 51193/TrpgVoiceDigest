using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Infrastructure.Storage;

public sealed partial class SessionStorage
{
    private readonly SessionPaths _paths;

    public SessionStorage(SessionPaths paths)
    {
        _paths = paths;
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(_paths.CampaignDirectory);
        Directory.CreateDirectory(_paths.SessionDirectory);
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

    public RefinementState LoadRefinementState()
    {
        if (!File.Exists(_paths.RefinementStatePath)) return new RefinementState();

        var json = File.ReadAllText(_paths.RefinementStatePath);
        using var document = JsonDocument.Parse(json);
        var state = new RefinementState();

        if (!document.RootElement.TryGetProperty("sentences", out var sentencesElement) ||
            sentencesElement.ValueKind != JsonValueKind.Array)
            return state;

        foreach (var item in sentencesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var number = item.TryGetProperty("number", out var numElement) && numElement.TryGetInt32(out var n) ? n : 0;
            var text = item.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? "" : "";
            if (number > 0 && !string.IsNullOrWhiteSpace(text))
                state.Sentences.Add(new RefinedSentence { Number = number, Text = text });
        }

        if (state.Sentences.Count > 0)
            state.Sentences.Sort((a, b) => a.Number.CompareTo(b.Number));

        return state;
    }

    internal void SaveRefinementState(RefinementState state)
    {
        var payload = new
        {
            sentences = state.Sentences.Select(s => new { s.Number, s.Text })
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
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

    internal void ExportRefinementMarkdown(RefinementState state)
    {
        var md = state.BuildMarkdown();
        File.WriteAllText(_paths.RefinementMarkdownPath, md);
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