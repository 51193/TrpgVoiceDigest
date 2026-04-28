using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Infrastructure.Storage;

public sealed class SessionStorage
{
    public void EnsureDirectories(SessionPaths paths)
    {
        Directory.CreateDirectory(paths.CampaignDirectory);
        Directory.CreateDirectory(paths.SessionDirectory);
        Directory.CreateDirectory(paths.AudioSegmentDirectory);
        Directory.CreateDirectory(paths.TranscriptDirectory);
    }

    public DigestState LoadDigestState(SessionPaths paths)
    {
        if (!File.Exists(paths.DigestStatePath))
        {
            return new DigestState();
        }

        var json = File.ReadAllText(paths.DigestStatePath);
        var state = new DigestState();
        using var document = JsonDocument.Parse(json);
        if (TryLoadLegacyDigestEntries(document.RootElement, state))
        {
            return state;
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return state;
        }

        LoadDigestEntries(document.RootElement, "digestEntries", state.Entries);
        LoadStringEntries(document.RootElement, "activeTasks", state.ActiveTasks);
        LoadStringEntries(document.RootElement, "completedTasks", state.CompletedTasks);
        LoadStringEntries(document.RootElement, "storyEntries", state.StoryEntries);
        return state;
    }

    public void SaveDigestState(SessionPaths paths, DigestState state)
    {
        var payload = new
        {
            digestEntries = state.Entries,
            activeTasks = state.ActiveTasks,
            completedTasks = state.CompletedTasks,
            storyEntries = state.StoryEntries
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(paths.DigestStatePath, json);
    }

    public string AppendTranscriptBatch(SessionPaths paths, IReadOnlyList<TranscriptSegment> segments, DateTimeOffset now)
    {
        var title = now.ToString("yyyyMMdd_HHmmss_fff");
        var filePath = Path.Combine(paths.TranscriptDirectory, $"{title}.md");
        var sb = new StringBuilder();
        sb.AppendLine($"# {now:yyyy-MM-dd HH:mm:ss zzz}");
        foreach (var seg in segments)
        {
            sb.AppendLine($"[{seg.Start:hh\\:mm\\:ss}-{seg.End:hh\\:mm\\:ss}] {seg.Text}");
        }

        File.WriteAllText(filePath, sb.ToString());
        return filePath;
    }

    public string ReadAllTranscriptText(SessionPaths paths)
    {
        if (!Directory.Exists(paths.TranscriptDirectory))
        {
            return string.Empty;
        }

        var files = Directory.GetFiles(paths.TranscriptDirectory, "*.md").OrderBy(x => x).ToArray();
        var sb = new StringBuilder();
        foreach (var file in files)
        {
            sb.AppendLine(File.ReadAllText(file));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public string ComputeTranscriptHash(string transcriptText)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(transcriptText));
        return Convert.ToHexString(bytes);
    }

    public string? LoadSubmitHash(SessionPaths paths)
    {
        if (!File.Exists(paths.SubmitCursorPath))
        {
            return null;
        }

        return File.ReadAllText(paths.SubmitCursorPath).Trim();
    }

    public void SaveSubmitHash(SessionPaths paths, string hash)
    {
        File.WriteAllText(paths.SubmitCursorPath, hash);
    }

    public void AppendLlmEditLog(
        SessionPaths paths,
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
        File.AppendAllText(paths.LlmEditLogPath, line + Environment.NewLine);
    }

    public void ExportCampaignDigest(SessionPaths paths, DigestState state)
    {
        var groups = state.GetTagGroupsExcludingTag(DigestState.ConsistencyTag);

        var sb = new StringBuilder();
        sb.AppendLine("# Campaign Digest");
        sb.AppendLine();
        foreach (var group in groups)
        {
            sb.AppendLine($"## {group.Tag}");
            foreach (var (key, content) in group.Items)
            {
                sb.AppendLine($"- **{key}**: {content}");
            }

            sb.AppendLine();
        }

        File.WriteAllText(paths.CampaignDigestMarkdownPath, sb.ToString());
    }

    public void ExportCampaignConsistency(SessionPaths paths, DigestState state)
    {
        var groups = state.GetTagGroupsByTag(DigestState.ConsistencyTag);
        var sb = new StringBuilder();
        sb.AppendLine("# Campaign Consistency");
        sb.AppendLine();
        if (groups.Count == 0)
        {
            sb.AppendLine("- (none)");
        }
        else
        {
            foreach (var group in groups)
            {
                sb.AppendLine($"## {group.Tag}");
                foreach (var (key, content) in group.Items)
                {
                    sb.AppendLine($"- **{key}**: {content}");
                }

                sb.AppendLine();
            }
        }

        File.WriteAllText(paths.CampaignConsistencyMarkdownPath, sb.ToString());
    }

    public void ExportCampaignTasks(SessionPaths paths, DigestState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Campaign Tasks");
        sb.AppendLine();
        sb.AppendLine("## Active Tasks");
        if (state.ActiveTasks.Count == 0)
        {
            sb.AppendLine("- (none)");
        }
        else
        {
            foreach (var pair in state.ActiveTasks.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- **{pair.Key}**: {pair.Value}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Completed Tasks");
        if (state.CompletedTasks.Count == 0)
        {
            sb.AppendLine("- (none)");
        }
        else
        {
            foreach (var pair in state.CompletedTasks.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- **{pair.Key}**: {pair.Value}");
            }
        }

        File.WriteAllText(paths.CampaignTasksMarkdownPath, sb.ToString());
    }

    public void ExportCampaignStory(SessionPaths paths, DigestState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Campaign Story");
        sb.AppendLine();
        if (state.StoryEntries.Count == 0)
        {
            sb.AppendLine("- (none)");
        }
        else
        {
            foreach (var pair in state.StoryEntries.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- **{pair.Key}**: {pair.Value}");
            }
        }

        File.WriteAllText(paths.CampaignStoryMarkdownPath, sb.ToString());
    }

    private static bool TryLoadLegacyDigestEntries(JsonElement root, DigestState state)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (root.TryGetProperty("digestEntries", out _))
        {
            return false;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object ||
                !property.Value.TryGetProperty("content", out var contentElement))
            {
                return false;
            }

            var content = contentElement.GetString() ?? string.Empty;
            var tags = new List<string>();
            if (property.Value.TryGetProperty("tags", out var tagsElement) &&
                tagsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tagsElement.EnumerateArray())
                {
                    tags.Add(item.GetString() ?? string.Empty);
                }
            }

            state.Entries[property.Name] = new DigestEntry(content, tags);
        }

        return true;
    }

    private static void LoadDigestEntries(JsonElement root, string propertyName, Dictionary<string, DigestEntry> target)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var content = property.Value.TryGetProperty("content", out var contentElement)
                ? contentElement.GetString() ?? string.Empty
                : string.Empty;
            var tags = new List<string>();
            if (property.Value.TryGetProperty("tags", out var tagsElement) &&
                tagsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tagsElement.EnumerateArray())
                {
                    tags.Add(item.GetString() ?? string.Empty);
                }
            }

            target[property.Name] = new DigestEntry(content, tags);
        }
    }

    private static void LoadStringEntries(JsonElement root, string propertyName, Dictionary<string, string> target)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            target[property.Name] = property.Value.GetString() ?? string.Empty;
        }
    }

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
