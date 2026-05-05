using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;

var contentRoot = AppContext.BaseDirectory;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    ContentRootPath = contentRoot,
    WebRootPath = Path.Combine(contentRoot, "wwwroot")
});

builder.Services.AddSignalR();
builder.Services.AddSingleton<FileStore>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var fileStore = app.Services.GetRequiredService<FileStore>();
var sharedSecret = app.Configuration["SharedSecret"];

if (string.IsNullOrWhiteSpace(sharedSecret))
{
    Console.Error.WriteLine("WARNING: SharedSecret not configured. Sync endpoint will reject all requests.");
    Console.Error.WriteLine("Set it in appsettings.json or via --SharedSecret argument.");
}

var secretKey = !string.IsNullOrWhiteSpace(sharedSecret)
    ? Convert.FromBase64String(sharedSecret)
    : Array.Empty<byte>();

app.MapPost("/api/sync", async (HttpContext context, IHubContext<SyncHub> hubContext) =>
{
    if (secretKey.Length == 0)
        return Results.Problem("Server not configured with SharedSecret", statusCode: 500);

    SyncRequest? request;
    try
    {
        request = await context.Request.ReadFromJsonAsync<SyncRequest>();
    }
    catch
    {
        return Results.BadRequest("Invalid JSON body");
    }

    if (request == null || string.IsNullOrWhiteSpace(request.Nonce) || string.IsNullOrWhiteSpace(request.Ciphertext))
        return Results.BadRequest("Missing nonce or ciphertext fields");

    string decrypted;
    try
    {
        decrypted = Decrypt(request.Nonce, request.Ciphertext, secretKey);
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"Decryption failed: {ex.Message}");
    }

    Dictionary<string, string>? files;
    try
    {
        var payload = JsonSerializer.Deserialize<SyncPayload>(decrypted,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        files = payload?.Files;
    }
    catch
    {
        return Results.BadRequest("Invalid decrypted payload");
    }

    if (files == null || files.Count == 0)
        return Results.BadRequest("No files in payload");

    fileStore.Update(files);
    await hubContext.Clients.All.SendAsync("FileUpdated", files);

    return Results.Ok(new { synced = files.Count });
});

app.MapGet("/api/state", (FileStore store) =>
    Results.Json(store.GetAll(), JsonSerializerOptions.Web));

app.MapHub<SyncHub>("/hub/sync");

Console.WriteLine("TrpgVoiceDigest.Server is starting...");
app.Run();

return;

static string Decrypt(string nonceB64, string ciphertextB64, byte[] key)
{
    var nonce = Convert.FromBase64String(nonceB64);
    var combined = Convert.FromBase64String(ciphertextB64);

    if (combined.Length < 16)
        throw new InvalidDataException("Ciphertext too short");

    var ciphertext = new byte[combined.Length - 16];
    var tag = new byte[16];
    Buffer.BlockCopy(combined, 0, ciphertext, 0, ciphertext.Length);
    Buffer.BlockCopy(combined, ciphertext.Length, tag, 0, 16);

    var plaintext = new byte[ciphertext.Length];
    using var aes = new AesGcm(key, tag.Length);
    aes.Decrypt(nonce, ciphertext, tag, plaintext);
    return Encoding.UTF8.GetString(plaintext);
}

internal sealed record SyncRequest(string Nonce, string Ciphertext);

internal sealed record SyncPayload(Dictionary<string, string> Files);

public sealed class FileStore
{
    private readonly ConcurrentDictionary<string, string> _files = new();

    public void Update(Dictionary<string, string> files)
    {
        foreach (var (key, value) in files)
            _files[key] = value;
    }

    public Dictionary<string, string> GetAll()
    {
        return new Dictionary<string, string>(_files);
    }
}

public sealed class SyncHub : Hub
{
    private readonly FileStore _store;

    public SyncHub(FileStore store)
    {
        _store = store;
    }

    public override async Task OnConnectedAsync()
    {
        var state = _store.GetAll();
        if (state.Count > 0)
            await Clients.Caller.SendAsync("FileUpdated", state);

        await base.OnConnectedAsync();
    }
}