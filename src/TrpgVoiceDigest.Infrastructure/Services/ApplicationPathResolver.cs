using System;
using System.Diagnostics;
using System.IO;
using TrpgVoiceDigest.Core.Services;

// ReSharper disable InconsistentNaming

namespace TrpgVoiceDigest.Infrastructure.Services;

/// <summary>
/// 统一管理应用所有文件路径，外部无需关心 CWD、程序集位置等细节。
/// 所有路径访问均记录日志（Info/Debug），便于排查路径相关问题。
/// </summary>
public static class ApplicationPathResolver
{
    private static readonly string[] RepoMarkers = ["TrpgVoiceDigest.slnx", "AGENTS.md", ".git"];

    private static string? _appRoot;
    private static ILogService? _logger;

    /// <summary>应用根目录（绝对路径）。未初始化时抛出异常。</summary>
    public static string AppRoot
    {
        get
        {
            if (_appRoot is null)
                throw new InvalidOperationException(
                    "ApplicationPathResolver 未初始化，请先调用 Initialize() 设置应用根目录。");
            return _appRoot;
        }
    }

    public static bool IsInitialized => _appRoot is not null;

    private static readonly string[] DeploymentMarkers = ["config", "prompts", "python"];

    /// <summary>
    /// 初始化路径解析器。在程序启动时调用一次。
    /// 优先级：
    /// 1. 显式指定 appRoot
    /// 2. 向上搜索仓库标记文件 (TrpgVoiceDigest.slnx / AGENTS.md / .git) → 开发场景
    /// 3. AppContext.BaseDirectory 中包含 config/prompts/python 子目录 → 发布部署场景
    /// 4. 回退到 AppContext.BaseDirectory
    /// </summary>
    public static void Initialize(string? appRoot = null, ILogService? logger = null)
    {
        _logger = logger;

        if (appRoot is not null)
        {
            _appRoot = Path.GetFullPath(appRoot);
        }
        else
        {
            var baseDir = Path.GetFullPath(AppContext.BaseDirectory);

            var discovered = DiscoverAppRoot();
            if (discovered is not null)
            {
                _appRoot = discovered;
                Log(LogLevel.Debug, $"开发场景: 通过标记文件发现应用根目录: {_appRoot}");
            }
            else if (ContainsDeploymentFiles(baseDir))
            {
                _appRoot = baseDir;
                Log(LogLevel.Info, "发布部署场景: 在程序集目录检测到项目文件，直接使用作为根目录。");
            }
            else
            {
                _appRoot = baseDir;
                Log(LogLevel.Debug, "未找到项目标记文件，回退到程序集目录作为应用根目录。");
            }
        }

        Log(LogLevel.Info, $"ApplicationPathResolver 已初始化: AppRoot='{_appRoot}'");
    }

    private static bool ContainsDeploymentFiles(string dir)
    {
        foreach (var marker in DeploymentMarkers)
        {
            if (!Directory.Exists(Path.Combine(dir, marker)))
                return false;
        }

        return true;
    }

    /// <summary>设置日志服务。可在启动后调用，将路径日志路由到日志文件。</summary>
    public static void SetLogger(ILogService? logger)
    {
        _logger = logger;
    }

    /// <summary>尝试发现应用根目录，返回绝对路径或 null。</summary>
    public static string? DiscoverAppRoot(string? startDir = null)
    {
        startDir ??= AppContext.BaseDirectory;
        var root = FindRootUpwards(startDir);
        if (root is not null)
            Log(LogLevel.Debug, $"通过标记文件发现应用根目录: {root}");
        return root;
    }

    private static string? FindRootUpwards(string startDir)
    {
        var dir = startDir;
        while (dir is not null)
        {
            foreach (var marker in RepoMarkers)
            {
                var path = Path.Combine(dir, marker);
                if (File.Exists(path) || Directory.Exists(path))
                {
                    Log(LogLevel.Debug, $"命中标记 '{marker}' 在 {dir}");
                    return dir;
                }
            }

            var parent = Path.GetDirectoryName(dir);
            if (parent == dir || parent is null)
                break;
            dir = parent;
        }

        return null;
    }

    // ───────────────────────────── 通用路径解析 ─────────────────────────────

    /// <summary>
    /// 将相对路径解析为绝对路径（基于 AppRoot）；绝对路径保持不变（规范化后返回）。
    /// 记录 Debug 级别日志。
    /// </summary>
    public static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var resolved = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(AppRoot, path));

        Log(LogLevel.Debug, $"路径解析: '{path}' → '{resolved}'");
        return resolved;
    }

    // ─────────────────────── 具名路径方法（带语义化日志）───────────────────────

    public static string ResolveConfigFilePath(string path)
    {
        var resolved = ResolvePath(path);
        Log(LogLevel.Info, $"配置文件: '{path}' → '{resolved}'");
        return resolved;
    }

    public static string ResolvePromptPath(string path, string promptName)
    {
        var resolved = ResolvePath(path);
        Log(LogLevel.Info, $"提示词 [{promptName}]: '{path}' → '{resolved}'");
        return resolved;
    }

    public static string ResolvePythonExecutable(string path)
    {
        var resolved = ResolvePath(path);
        if (File.Exists(resolved))
        {
            Log(LogLevel.Info, $"Python 可执行文件: '{path}' → '{resolved}'");
            return resolved;
        }

        if (OperatingSystem.IsWindows())
        {
            var windowsPath = ResolvePath("python/venv/Scripts/python.exe");
            if (File.Exists(windowsPath))
            {
                Log(LogLevel.Info, $"Python 可执行文件 (Windows 回退): '{path}' 不存在，使用 '{windowsPath}'");
                return windowsPath;
            }
        }

        Log(LogLevel.Warning, $"Python 可执行文件不存在: {resolved}");
        Log(LogLevel.Info, $"Python 可执行文件: '{path}' → '{resolved}'");
        return resolved;
    }

    public static string ResolvePythonScript(string path)
    {
        var resolved = ResolvePath(path);
        if (!File.Exists(resolved))
            Log(LogLevel.Warning, $"Python 脚本文件不存在: {resolved}");
        Log(LogLevel.Info, $"Python 脚本: '{path}' → '{resolved}'");
        return resolved;
    }

    public static string ResolveCampaignRoot(string path)
    {
        var resolved = ResolvePath(path);
        Log(LogLevel.Info, $"Campaign 根目录: '{path}' → '{resolved}'");
        return resolved;
    }

    // ──────────────────────────── 路径构建 ──────────────────────────────

    public static CampaignPaths BuildCampaignPaths(string campaignRoot, string campaignName)
    {
        var resolvedRoot = ResolveCampaignRoot(campaignRoot);
        var paths = CampaignPathBuilder.Build(resolvedRoot, campaignName);
        Log(LogLevel.Info,
            $"路径已构建: campaign='{campaignName}', dir='{paths.CampaignDirectory}'");
        return paths;
    }

    // ─────────────────────────── 文件系统辅助 ───────────────────────────────

    public static bool FileExists(string path)
    {
        var resolved = ResolvePath(path);
        var exists = File.Exists(resolved);
        Log(LogLevel.Debug, $"File.Exists('{resolved}') → {exists}");
        return exists;
    }

    public static bool DirectoryExists(string path)
    {
        var resolved = ResolvePath(path);
        var exists = Directory.Exists(resolved);
        Log(LogLevel.Debug, $"Directory.Exists('{resolved}') → {exists}");
        return exists;
    }

    public static string[]? GetDirectories(string path)
    {
        var resolved = ResolvePath(path);
        if (!Directory.Exists(resolved))
        {
            Log(LogLevel.Debug, $"GetDirectories: 目录不存在 '{resolved}'");
            return null;
        }

        var dirs = Directory.GetDirectories(resolved);
        Log(LogLevel.Debug, $"GetDirectories('{resolved}') → {dirs.Length} 项");
        return dirs;
    }

    // ───────────────────────────── 日志内部方法 ──────────────────────────────

    private static void Log(LogLevel level, string message)
    {
        if (_logger is not null)
            _logger.Log(level, $"[Path] {message}");
        else
            Debug.WriteLine($"[PathResolver] [{level.ToString().ToUpperInvariant()}] {message}");
    }
}
