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
        var payload = JsonSerializer.Deserialize<Dictionary<string, DigestEntry>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new Dictionary<string, DigestEntry>();

        var state = new DigestState();
        foreach (var pair in payload)
        {
            state.Entries[pair.Key] = pair.Value;
        }

        return state;
    }

    public void SaveDigestState(SessionPaths paths, DigestState state)
    {
        var json = JsonSerializer.Serialize(state.Entries, new JsonSerializerOptions { WriteIndented = true });
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

    public void ExportCampaignDigest(SessionPaths paths, DigestState state)
    {
        var grouped = state.Entries
            .SelectMany(x => x.Value.Tags.Select(tag => new { Tag = tag, Key = x.Key, Value = x.Value.Content }))
            .GroupBy(x => x.Tag)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Campaign Digest");
        sb.AppendLine();
        foreach (var group in grouped)
        {
            sb.AppendLine($"## {group.Key}");
            foreach (var item in group)
            {
                sb.AppendLine($"- **{item.Key}**: {item.Value}");
            }

            sb.AppendLine();
        }

        File.WriteAllText(paths.CampaignDigestMarkdownPath, sb.ToString());
    }
}
