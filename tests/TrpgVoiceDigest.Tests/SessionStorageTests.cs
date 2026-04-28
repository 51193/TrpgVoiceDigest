using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Infrastructure.Storage;

namespace TrpgVoiceDigest.Tests;

public class SessionStorageTests
{
    [Fact]
    public void ExportCampaignDigest_GroupsByTag()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trpg_test_{Guid.NewGuid():N}");
        var paths = SessionPathBuilder.Build(root, "DND_A", "Session_01");
        var storage = new SessionStorage();
        storage.EnsureDirectories(paths);

        var state = new DigestState();
        state.Apply([
            new EditOperation(EditAction.Add, "线索A", new DigestEntry("地下室有符号", ["线索", "地点"])),
            new EditOperation(EditAction.Add, "人物B", new DigestEntry("是神秘教徒", ["人物"]))
        ]);

        storage.ExportCampaignDigest(paths, state);
        var text = File.ReadAllText(paths.CampaignDigestMarkdownPath);

        Assert.Contains("## 线索", text);
        Assert.Contains("## 人物", text);
        Assert.Contains("**线索A**", text);

        Directory.Delete(root, true);
    }
}
