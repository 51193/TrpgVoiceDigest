using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var configPath = args.Length > 0 ? args[0] : "appsettings.json";
if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"Config file not found: {configPath}");
    Console.Error.WriteLine("Copy appsettings.example.json to appsettings.json and fill in your settings.");
    return 1;
}

var config = JsonSerializer.Deserialize(File.ReadAllText(configPath), AppJsonContext.Default.UploadConfig);
if (config == null)
{
    Console.Error.WriteLine("Failed to parse config file.");
    return 1;
}

if (string.IsNullOrWhiteSpace(config.CampaignDirectory))
{
    Console.Error.WriteLine("CampaignDirectory is required.");
    return 1;
}

if (string.IsNullOrWhiteSpace(config.ServerUrl))
{
    Console.Error.WriteLine("ServerUrl is required.");
    return 1;
}

if (string.IsNullOrWhiteSpace(config.SharedSecret))
{
    Console.Error.WriteLine("SharedSecret is required. Run scripts/generate_key.sh to create one.");
    return 1;
}

var secretKey = Convert.FromBase64String(config.SharedSecret);
var scanInterval = Math.Max(1, config.ScanIntervalSeconds);
var files = config.Files ??
            ["refinement.md", "story_progress.md", "tasks.md", "campaign_speakers.json", "consistency.md"];
var fileHashes = new Dictionary<string, string>();

var trackerFile = Path.Combine(config.CampaignDirectory, "_system", ".upload_hashes.json");
if (File.Exists(trackerFile))
    try
    {
        fileHashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(trackerFile)) ?? [];
    }
    catch
    {
    }

Console.WriteLine($"Upload service started. Campaign: {config.CampaignDirectory}");
Console.WriteLine($"Target: {config.ServerUrl}  |  Interval: {scanInterval}s");
Console.WriteLine($"Watching: {string.Join(", ", files)}");
Console.WriteLine();

using var httpClient = new HttpClient { BaseAddress = new Uri(config.ServerUrl) };
httpClient.Timeout = TimeSpan.FromSeconds(30);

while (true)
{
    try
    {
        var changedFiles = new Dictionary<string, string>();
        var hasAnyChange = false;

        foreach (var file in files)
        {
            var filePath = Path.Combine(config.CampaignDirectory, file);
            if (!File.Exists(filePath)) continue;

            var content = File.ReadAllText(filePath);
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

            if (!fileHashes.TryGetValue(file, out var cachedHash) ||
                !string.Equals(cachedHash, hash, StringComparison.Ordinal))
            {
                changedFiles[file] = content;
                fileHashes[file] = hash;
                hasAnyChange = true;
            }
        }

        if (hasAnyChange)
        {
            var payload = JsonSerializer.Serialize(new { files = changedFiles });
            var encrypted = Encrypt(payload, secretKey);
            var requestBody = new
            {
                nonce = Convert.ToBase64String(encrypted.Nonce),
                ciphertext = Convert.ToBase64String(encrypted.Ciphertext)
            };

            var response = await httpClient.PostAsJsonAsync("/api/sync", requestBody);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss}] Synced {changedFiles.Count} file(s): {string.Join(", ", changedFiles.Keys)}");
                var trackerDir = Path.GetDirectoryName(trackerFile);
                if (trackerDir != null && !Directory.Exists(trackerDir))
                    Directory.CreateDirectory(trackerDir);
                File.WriteAllText(trackerFile, JsonSerializer.Serialize(fileHashes));
            }
            else
            {
                var err = await response.Content.ReadAsStringAsync();
                Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sync failed ({response.StatusCode}): {err}");
            }
        }
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] Connection error: {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error: {ex.Message}");
    }

    await Task.Delay(TimeSpan.FromSeconds(scanInterval));
}

static (byte[] Nonce, byte[] Ciphertext) Encrypt(string plaintext, byte[] key)
{
    var nonce = new byte[12];
    RandomNumberGenerator.Fill(nonce);
    var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
    var ciphertext = new byte[plaintextBytes.Length];
    var tag = new byte[16];

    using var aes = new AesGcm(key, tag.Length);
    aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

    var result = new byte[ciphertext.Length + tag.Length];
    Buffer.BlockCopy(ciphertext, 0, result, 0, ciphertext.Length);
    Buffer.BlockCopy(tag, 0, result, ciphertext.Length, tag.Length);
    return (nonce, result);
}

[JsonSerializable(typeof(UploadConfig))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class AppJsonContext : JsonSerializerContext
{
}

internal sealed record UploadConfig
{
    public string CampaignDirectory { get; init; } = "";
    public string ServerUrl { get; init; } = "";
    public string SharedSecret { get; init; } = "";
    public int ScanIntervalSeconds { get; init; } = 2;
    public string[]? Files { get; init; }
}