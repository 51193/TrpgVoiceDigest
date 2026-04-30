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
        Directory.CreateDirectory(_paths.CharacterCardsDirectory);
    }

    public DigestState LoadDigestState()
    {
        if (!File.Exists(_paths.DigestStatePath)) return new DigestState();

        var json = File.ReadAllText(_paths.DigestStatePath);
        var state = new DigestState();
        using var document = JsonDocument.Parse(json);
        if (TryLoadLegacyDigestEntries(document.RootElement, state)) return state;

        if (document.RootElement.ValueKind != JsonValueKind.Object) return state;

        LoadDigestEntries(document.RootElement, "digestEntries", state.Entries);
        LoadStringEntries(document.RootElement, "activeTasks", state.ActiveTasks);
        LoadStringEntries(document.RootElement, "completedTasks", state.CompletedTasks);
        LoadStringEntries(document.RootElement, "storyEntries", state.StoryEntries);
        return state;
    }

    internal void SaveDigestState(DigestState state)
    {
        var payload = new
        {
            digestEntries = state.Entries,
            activeTasks = state.ActiveTasks,
            completedTasks = state.CompletedTasks,
            storyEntries = state.StoryEntries
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_paths.DigestStatePath, json);
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

    internal string ReadDialogueLog()
    {
        if (!File.Exists(_paths.DialogueLogPath)) return string.Empty;

        return File.ReadAllText(_paths.DialogueLogPath);
    }

    internal string ComputeDialogueLogHash(string dialogueLogText)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(dialogueLogText));
        return Convert.ToHexString(bytes);
    }

    internal string? LoadSubmitHash()
    {
        if (!File.Exists(_paths.SubmitCursorPath)) return null;

        return File.ReadAllText(_paths.SubmitCursorPath).Trim();
    }

    internal void SaveSubmitHash(string hash)
    {
        File.WriteAllText(_paths.SubmitCursorPath, hash);
    }

    public string ReadCampaignConsistencyLexicon()
    {
        if (!File.Exists(_paths.CampaignConsistencyLexiconPath)) return string.Empty;

        return File.ReadAllText(_paths.CampaignConsistencyLexiconPath);
    }

    public string ReadCampaignCharacterCards()
    {
        if (!Directory.Exists(_paths.CharacterCardsDirectory)) return string.Empty;

        var files = Directory.GetFiles(_paths.CharacterCardsDirectory, "*.md")
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (var file in files)
        {
            sb.AppendLine($"### 人物卡：{Path.GetFileName(file)}");
            sb.AppendLine(File.ReadAllText(file));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public void AppendCampaignConsistencyLexiconEntry(string entry)
    {
        var normalized = entry.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return;

        Directory.CreateDirectory(_paths.CampaignDirectory);
        File.AppendAllText(_paths.CampaignConsistencyLexiconPath, normalized + Environment.NewLine);
    }

    internal void AppendLlmEditLog(
        DateTimeOffset timestamp,
        string transcriptHash,
        string llmResponse,
        IReadOnlyList<EditOperation> operations)
    {
        var payload = new
        {
            timestamp = timestamp.ToString("O"),
            transcriptHash,
            operationCount = operations.Count,
            isEmpty = operations.All(x => x.Action == EditAction.Empty),
            operations = operations.Select(BuildOperationLog),
            llmResponse
        };

        var line = JsonSerializer.Serialize(payload);
        File.AppendAllText(_paths.LlmEditLogPath, line + Environment.NewLine);
    }


    internal void ExportCampaignDigest(DigestState state)
    {
        var md = DigestState.BuildGroupedSection(
            "# Campaign Digest",
            state.GetTagGroupsExcludingTag(DigestState.ConsistencyTag));
        File.WriteAllText(_paths.CampaignDigestMarkdownPath, md);
    }

    internal void ExportCampaignConsistency(DigestState state)
    {
        var md = DigestState.BuildGroupedSection(
            "# Campaign Consistency",
            state.GetTagGroupsByTag(DigestState.ConsistencyTag));
        File.WriteAllText(_paths.CampaignConsistencyMarkdownPath, md);
    }

    internal void ExportCampaignTasks(DigestState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Campaign Tasks");
        sb.AppendLine();
        sb.Append(DigestState.BuildKvpSection("## Active Tasks", state.ActiveTasks));
        sb.AppendLine();
        sb.Append(DigestState.BuildKvpSection("## Completed Tasks", state.CompletedTasks));
        File.WriteAllText(_paths.CampaignTasksMarkdownPath, sb.ToString());
    }

    internal void ExportCampaignStory(DigestState state)
    {
        var md = DigestState.BuildKvpSection("# Campaign Story", state.StoryEntries);
        File.WriteAllText(_paths.CampaignStoryMarkdownPath, md);
    }

    private static bool TryLoadLegacyDigestEntries(JsonElement root, DigestState state)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;

        if (root.TryGetProperty("digestEntries", out _)) return false;

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object ||
                !property.Value.TryGetProperty("content", out var contentElement))
                return false;

            var content = contentElement.GetString() ?? string.Empty;
            var tags = new List<string>();
            if (property.Value.TryGetProperty("tags", out var tagsElement) &&
                tagsElement.ValueKind == JsonValueKind.Array)
                foreach (var item in tagsElement.EnumerateArray())
                    tags.Add(item.GetString() ?? string.Empty);

            state.Entries[property.Name] = new DigestEntry(content, tags);
        }

        return true;
    }

    private static void LoadDigestEntries(JsonElement root, string propertyName, Dictionary<string, DigestEntry> target)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object) return;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object) continue;

            var content = property.Value.TryGetProperty("content", out var contentElement)
                ? contentElement.GetString() ?? string.Empty
                : string.Empty;
            var tags = new List<string>();
            if (property.Value.TryGetProperty("tags", out var tagsElement) &&
                tagsElement.ValueKind == JsonValueKind.Array)
                foreach (var item in tagsElement.EnumerateArray())
                    tags.Add(item.GetString() ?? string.Empty);

            target[property.Name] = new DigestEntry(content, tags);
        }
    }

    private static void LoadStringEntries(JsonElement root, string propertyName, Dictionary<string, string> target)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object) return;

        foreach (var property in element.EnumerateObject())
            target[property.Name] = property.Value.GetString() ?? string.Empty;
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

    private static object BuildOperationLog(EditOperation operation)
    {
        return new
        {
            action = operation.Action.ToString(),
            area = operation.Area.ToString(),
            key = operation.Key,
            value = operation.Value is null
                ? null
                : operation.Area == EntryArea.Digest
                    ? (object)new
                    {
                        digestContent = operation.Value.Digest?.Content,
                        digestTags = operation.Value.Digest?.Tags
                    }
                    : new
                    {
                        textContent = operation.Value.Text
                    }
        };
    }
}