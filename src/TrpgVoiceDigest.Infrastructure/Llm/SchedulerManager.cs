using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Infrastructure.Llm;

public sealed class SchedulerManager
{
    private static readonly object _lock = new();
    private static SchedulerManager? _instance;

    public static bool IsInitialized => _instance is not null;

    public static SchedulerManager Instance
    {
        get
        {
            if (_instance is null)
                throw new InvalidOperationException(
                    "SchedulerManager 尚未初始化。请先调用 SchedulerManager.Initialize()。");
            return _instance;
        }
    }

    public static SchedulerManager Initialize(
        ILlmClient llmClient,
        IPromptTemplateResolver resolver,
        ILogService? logService = null)
    {
        lock (_lock)
        {
            if (_instance is not null)
                throw new InvalidOperationException("SchedulerManager 已初始化，不允许重复初始化。");
            _instance = new SchedulerManager(llmClient, resolver, logService);
            return _instance;
        }
    }

    private readonly Dictionary<string, StructuredLlmContainer> _schedulers = new();

    public const string Refinement = "refinement";
    public const string Consistency = "consistency";
    public const string StoryProgress = "story_progress";

    private SchedulerManager(
        ILlmClient llmClient,
        IPromptTemplateResolver resolver,
        ILogService? logService)
    {
        _schedulers[Refinement] = BuildRefinement(llmClient, resolver, logService);
        _schedulers[Consistency] = BuildConsistency(llmClient, resolver, logService);
        _schedulers[StoryProgress] = BuildStoryProgress(llmClient, resolver, logService);

        logService?.Info($"SchedulerManager 已初始化: {_schedulers.Count} 个调度器 "
            + $"[{string.Join(", ", _schedulers.Keys)}]");
    }

    public StructuredLlmContainer Get(string key)
    {
        if (!_schedulers.TryGetValue(key, out var scheduler))
            throw new ArgumentException(
                $"调度器 '{key}' 不存在。可用调度器: [{string.Join(", ", _schedulers.Keys)}]");
        return scheduler;
    }

    private static StructuredLlmContainer BuildRefinement(
        ILlmClient llmClient,
        IPromptTemplateResolver resolver,
        ILogService? logService)
    {
        var promptSections = new PromptSection[]
        {
            new("system", "{{include:prompts/system_refinement.md}}"),
            new("user", "{{include:prompts/refinement_user_static.md}}"),
            new("user", "{{include:prompts/refinement_user_dynamic.md}}")
        };

        var parsers = new IResponseParser[] { new RefinementResponseParser() };

        return new StructuredLlmContainer(llmClient, resolver, promptSections, parsers, logService);
    }

    private static StructuredLlmContainer BuildConsistency(
        ILlmClient llmClient,
        IPromptTemplateResolver resolver,
        ILogService? logService)
    {
        var promptSections = new PromptSection[]
        {
            new("system", "{{include:prompts/system_consistency.md}}"),
            new("user", "{{include:prompts/consistency_user_template.md}}")
        };

        var parsers = new IResponseParser[] { new ConsistencyResponseParser() };

        return new StructuredLlmContainer(llmClient, resolver, promptSections, parsers, logService);
    }

    private static StructuredLlmContainer BuildStoryProgress(
        ILlmClient llmClient,
        IPromptTemplateResolver resolver,
        ILogService? logService)
    {
        var promptSections = new PromptSection[]
        {
            new("system", "{{include:prompts/system_story_progress.md}}"),
            new("user", "{{include:prompts/story_progress_user_static.md}}"),
            new("user", "{{include:prompts/story_progress_user_dynamic.md}}")
        };

        var parsers = new IResponseParser[]
        {
            new StoryProgressResponseParser(),
            new TaskResponseParser()
        };

        return new StructuredLlmContainer(llmClient, resolver, promptSections, parsers, logService);
    }
}
