using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Codely.Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;

#if UNITY_EDITOR
using TJGenerators;
using TJGenerators.Generators;
using TJGenerators.Config;
using TJGenerators.Pipeline;
using TJGenerators.Utils;
using Unity.EditorCoroutines.Editor;
#endif

namespace UnityTcp.Editor.Tools
{
#if UNITY_EDITOR
    [InitializeOnLoad]
    internal static class TerrainApplySceneIdentity
    {
        private const string UnsavedPrefix = "TJGen_TerrainScene_Unsaved_";
        private const string SavedPrefix = "TJGen_TerrainScene_Saved_";

        static TerrainApplySceneIdentity()
        {
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += OnSceneSaved;
            UnityEditor.SceneManagement.EditorSceneManager.sceneClosed += OnSceneClosed;
        }

        internal static string GetIdentity(UnityEngine.SceneManagement.Scene scene)
        {
            string guid = AssetDatabase.AssetPathToGUID(scene.path);
            if (!string.IsNullOrEmpty(guid))
                return SessionState.GetString(SavedPrefix + guid, "asset:" + guid);

            string identity = "unsaved:" + scene.handle;
            SessionState.SetString(UnsavedPrefix + scene.handle, identity);
            return identity;
        }

        private static void OnSceneSaved(UnityEngine.SceneManagement.Scene scene)
        {
            string key = UnsavedPrefix + scene.handle;
            string identity = SessionState.GetString(key, "");
            string guid = AssetDatabase.AssetPathToGUID(scene.path);
            // Save As Copy leaves the original scene unsaved. Only its first real
            // save establishes an alias; later Save As operations keep distinct GUIDs.
            if (string.IsNullOrEmpty(identity) || string.IsNullOrEmpty(guid)) return;
            SessionState.SetString(SavedPrefix + guid, identity);
            SessionState.EraseString(key);
        }

        private static void OnSceneClosed(UnityEngine.SceneManagement.Scene scene)
        {
            // Keep the GUID alias for reopening the scene in this Editor session.
            SessionState.EraseString(UnsavedPrefix + scene.handle);
        }
    }
#endif

    public static class TerrainTaskTracker
    {
#if UNITY_EDITOR
        [Serializable]
        internal class AppliedTerrainResult
        {
            public string terrainDataPath;
            public string terrainGoName;
        }

        [Serializable]
        private class PersistedTask
        {
            public string taskId;
            public string prompt;
            public string imagePath;
            public string status;
            public int    progress;
            public string heightmapPath;
            public string errorMessage;
            public long   startTimeTicks;
            public long   endTimeTicks;
            public string previewUrl;
            public string placeholderPath;
            public string terrainDataPath;
            public string terrainGoName;
            public string backendTaskId;
            public List<AppliedTerrainResult> previousApplications;
        }

        public class TerrainTaskInfo : IGenerationTaskInfo
        {
            public string TaskId { get; set; }
            public string Prompt { get; set; }
            public string ImagePath { get; set; }
            public string Status { get; set; }
            public int Progress { get; set; }
            public string HeightmapPath { get; set; }
            public string ErrorMessage { get; set; }
            public string PreviewUrl { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public string PlaceholderPath { get; set; }
            public string TerrainDataPath { get; set; }
            public string TerrainGoName { get; set; }
            public string BackendTaskId { get; set; }
            internal List<AppliedTerrainResult> PreviousApplications { get; set; } = new List<AppliedTerrainResult>();
        }

        private static readonly GenerationTaskTrackerStore<TerrainTaskInfo, PersistedTask> Store =
            new GenerationTaskTrackerStore<TerrainTaskInfo, PersistedTask>(
                "TJGen_Terrain", BuildPersisted, FromPersisted);

        private static PersistedTask BuildPersisted(TerrainTaskInfo info) => new PersistedTask
        {
            taskId          = info.TaskId,
            prompt          = info.Prompt ?? "",
            imagePath       = info.ImagePath ?? "",
            status          = info.Status,
            progress        = info.Progress,
            heightmapPath   = info.HeightmapPath ?? "",
            errorMessage    = info.ErrorMessage ?? "",
            startTimeTicks  = info.StartTime.Ticks,
            endTimeTicks    = info.EndTime?.Ticks ?? 0,
            previewUrl      = info.PreviewUrl ?? "",
            placeholderPath = info.PlaceholderPath ?? "",
            terrainDataPath = info.TerrainDataPath ?? "",
            terrainGoName   = info.TerrainGoName ?? "",
            backendTaskId   = info.BackendTaskId ?? "",
            previousApplications = info.PreviousApplications
        };

        private static TerrainTaskInfo FromPersisted(PersistedTask p) => new TerrainTaskInfo
        {
            TaskId          = p.taskId,
            Prompt          = p.prompt,
            ImagePath       = p.imagePath,
            Status          = p.status,
            Progress        = p.progress,
            HeightmapPath   = p.heightmapPath,
            ErrorMessage    = p.errorMessage,
            PreviewUrl      = p.previewUrl,
            StartTime       = new DateTime(p.startTimeTicks),
            EndTime         = p.endTimeTicks > 0 ? (DateTime?)new DateTime(p.endTimeTicks) : null,
            PlaceholderPath = p.placeholderPath,
            TerrainDataPath = p.terrainDataPath,
            TerrainGoName   = p.terrainGoName,
            BackendTaskId   = p.backendTaskId,
            PreviousApplications = p.previousApplications ?? new List<AppliedTerrainResult>()
        };

        internal static void ApplyTaskUpdate(TerrainTaskInfo task, Action<TerrainTaskInfo> mutate) =>
            Store.ApplyTaskUpdate(task, mutate);

        public static string CreateTask(string prompt, string imagePath = null, string placeholderPath = null, string backendTaskId = null)
        {
            string taskId = Store.AllocateTaskId("terrain");
            var task = new TerrainTaskInfo
            {
                TaskId          = taskId,
                Prompt          = prompt ?? "",
                ImagePath       = imagePath ?? "",
                Status          = "generating",
                StartTime       = DateTime.Now,
                PlaceholderPath = placeholderPath,
                BackendTaskId   = backendTaskId
            };
            Store.RegisterTask(taskId, task);
            return taskId;
        }

        public static void MarkTaskCompleted(string taskId, string heightmapPath, string previewUrl = null)
        {
            var task = Store.GetTask(taskId);
            if (task == null) return;
            Store.ApplyTaskUpdate(task, t =>
            {
                t.Status        = "completed";
                t.Progress      = 100;
                t.HeightmapPath = heightmapPath;
                t.PreviewUrl    = previewUrl;
                t.EndTime       = DateTime.Now;
            });
        }

        public static void MarkTaskFailed(string taskId, string errorMessage)
        {
            var task = Store.GetTask(taskId);
            if (task == null) return;
            Store.ApplyTaskUpdate(task, t =>
            {
                t.Status       = "failed";
                t.ErrorMessage = errorMessage;
                t.EndTime      = DateTime.Now;
            });
        }

        public static void MarkTaskApplied(string taskId, string terrainDataPath, string terrainGoName)
        {
            var task = Store.GetTask(taskId);
            if (task == null) return;
            Store.ApplyTaskUpdate(task, t =>
            {
                // The previous scene may already be closed, and old tasks may have no
                // apply record yet. Retain its result before changing the current one.
                if (t.Status == "applied" && !string.IsNullOrEmpty(t.TerrainDataPath) &&
                    t.TerrainDataPath != terrainDataPath &&
                    !t.PreviousApplications.Exists(r => r.terrainDataPath == t.TerrainDataPath))
                    t.PreviousApplications.Add(new AppliedTerrainResult
                    {
                        terrainDataPath = t.TerrainDataPath,
                        terrainGoName = t.TerrainGoName
                    });
                t.Status          = "applied";
                t.TerrainDataPath = terrainDataPath;
                t.TerrainGoName   = terrainGoName;
                t.EndTime         = t.EndTime ?? DateTime.Now;
            });
        }

        public static TerrainTaskInfo GetTask(string taskId) => Store.GetTask(taskId);

        public static List<TerrainTaskInfo> GetAllTasks() => Store.GetAllTasks();

        public static TerrainTaskInfo GetTaskByBackendId(string backendTaskId) =>
            Store.GetTaskByBackendId(backendTaskId);

        public static TerrainTaskInfo CreateRecoveredTask(
            string backendTaskId, string prompt, string placeholderPath, long timestampMs, string imagePath = null)
        {
            return Store.CreateRecoveredTask(backendTaskId, () => new TerrainTaskInfo
            {
                TaskId          = $"recovered_{backendTaskId}",
                BackendTaskId   = backendTaskId,
                Prompt          = prompt ?? "",
                ImagePath       = imagePath ?? "",
                PlaceholderPath = placeholderPath ?? "",
                Status          = "recovering",
                Progress        = 0,
                StartTime       = timestampMs > 0
                                    ? DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).LocalDateTime
                                    : DateTime.Now
            });
        }

        public static void RemoveTask(string taskId) => Store.RemoveTask(taskId);
#endif
    }

    public static class TerrainApplyTaskTracker
    {
#if UNITY_EDITOR
        public class ApplyTaskInfo
        {
            public string ApplyTaskId    { get; set; }
            public string Status         { get; set; }   // "processing" | "completed" | "failed"
            public string HeightmapPath  { get; set; }
            public string ApplicationKey { get; set; }
            public string TerrainDataPath { get; set; }
            public string TerrainGoName  { get; set; }
            public string ErrorMessage   { get; set; }
            public DateTime StartTime    { get; set; }
            public DateTime? EndTime     { get; set; }
        }

        private static readonly Dictionary<string, ApplyTaskInfo> _tasks = new Dictionary<string, ApplyTaskInfo>();
        private static int _counter;
        private const string SessionKey = "TJGen_TerrainApply_Tasks";

        static TerrainApplyTaskTracker() => RestoreTasks();

        internal static void RestoreTasks()
        {
            _tasks.Clear();
            foreach (var item in SessionJsonStore.LoadJsonArray(SessionKey))
            {
                var task = item.ToObject<ApplyTaskInfo>();
                if (task == null || string.IsNullOrEmpty(task.ApplyTaskId)) continue;
                if (task.Status == "processing")
                {
                    task.Status = "failed";
                    task.ErrorMessage = "Terrain application interrupted by domain reload; retry the same request.";
                    task.EndTime = DateTime.Now;
                }
                _tasks[task.ApplyTaskId] = task;
            }
            SaveTasks();
        }

        private static void SaveTasks() => SessionJsonStore.SaveJsonArray(SessionKey, JArray.FromObject(_tasks.Values));

        internal static ApplyTaskInfo FindReusableTask(string applicationKey)
        {
            if (string.IsNullOrEmpty(applicationKey)) return null;
            foreach (var task in _tasks.Values)
                if (task.ApplicationKey == applicationKey && (task.Status == "processing" || task.Status == "completed"))
                    return task;
            return null;
        }

        public static string CreateTask(string heightmapPath, string applicationKey = null)
        {
            string id = $"terrain_apply_{++_counter}_{DateTime.Now.Ticks}";
            _tasks[id] = new ApplyTaskInfo
            {
                ApplyTaskId = id,
                Status      = "processing",
                HeightmapPath = heightmapPath,
                ApplicationKey = applicationKey,
                StartTime   = DateTime.Now
            };
            SaveTasks();
            return id;
        }

        public static void MarkCompleted(string id, string terrainDataPath, string terrainGoName)
        {
            if (_tasks.TryGetValue(id, out var t))
            {
                t.Status          = "completed";
                t.TerrainDataPath = terrainDataPath;
                t.TerrainGoName   = terrainGoName;
                t.EndTime         = DateTime.Now;
                SaveTasks();
            }
        }

        public static void MarkFailed(string id, string error)
        {
            if (_tasks.TryGetValue(id, out var t))
            {
                t.Status       = "failed";
                t.ErrorMessage = error;
                t.EndTime      = DateTime.Now;
                SaveTasks();
            }
        }

        public static ApplyTaskInfo GetTask(string id)
        {
            _tasks.TryGetValue(id, out var t);
            return t;
        }

        public static List<ApplyTaskInfo> GetAllTasks() => new List<ApplyTaskInfo>(_tasks.Values);

        internal static void RemoveTask(string id)
        {
            _tasks.Remove(id);
            SaveTasks();
        }
#endif
    }

    /// <summary>
    /// CustomTools for generating Unity Terrain heightmaps via the Frontier AI model.
    /// Workflow: generate_terrain (async) → query_local_task → apply_terrain_heightmap (async) → query_local_task.
    /// </summary>
    public static class GenerateTerrainTool
    {
        [ExecuteCustomTool.CustomTool("generate_terrain",
            "Generate a Unity Terrain heightmap using the Frontier AI model (unity_terrain_heightmap template). " +
            "Frontier is the only supported model — there is no generator_id parameter. " +
            "Returns immediately with task_id and placeholder_path. " +
            "After generation completes (~30-90s), call apply_terrain_heightmap with the heightmap_path " +
            "to create a Unity Terrain in the scene. " +
            "Parameters: prompt (terrain description, e.g. 'rolling hills with river valley'), " +
            "image_path (optional reference image), " +
            "resolution ('1K'/'2K'/'4K', default '2K'; terrain heightmaps are always square), " +
            "output_path (optional save path). " +
            "IMPORTANT: Generation takes 30-90 seconds. Wait at least 5 seconds before the first " +
            "query_local_task call, then poll every 10-15 seconds.")]
        public static object GenerateTerrain(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                TJLog.Log($"[GenerateTerrainTool] Generating terrain with parameters: {parameters}");

                string prompt     = parameters["prompt"]?.ToString();
                string imagePath  = parameters["image_path"]?.ToString();
                string resolution = parameters["resolution"]?.ToString() ?? "2K";
                string outputPath = parameters["output_path"]?.ToString();
                string sessionId  = parameters["session_id"]?.ToString() ?? "";
                GenerationRequestOrigin.SetWorkspaceName(parameters["workspace_name"]?.ToString());
                // aspect_ratio is always "1:1" for terrain heightmaps (square)
                const string aspectRatio = "1:1";

                if (string.IsNullOrEmpty(prompt) && string.IsNullOrEmpty(imagePath))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "Either 'prompt' or 'image_path' must be provided" }
                    };
                }

                int maxLen = TJGeneratorsPromptLimits.GetMaxLength("frontier-effect");
                if (!string.IsNullOrEmpty(prompt) && prompt.Length > maxLen)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "error_code", "INVALID_PARAMS" },
                        { "message", $"Prompt length ({prompt.Length}) exceeds the {maxLen} character limit." }
                    };
                }

                // 加载 frontier-effect 配置（地形高度图固定使用此生成器）
                var config = ConfigManager.GetGeneratorConfig(ConfigType.Image, "frontier-effect");
                if (config == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "Cannot find image generator config for 'frontier-effect'. " +
                                     "Please run AI/开发/清除配置缓存并重新加载 (requires TJGENERATORS_DEBUG)." }
                    };
                }

                var generator = new DynamicGenerator(config);

                if (!string.IsNullOrEmpty(prompt))
                    generator.SetTextPrompt(prompt);

                if (!string.IsNullOrEmpty(imagePath))
                    generator.SetImagePath(imagePath);

                // 注入地形高度图提示词模板
                var templateConfig = config.promptTemplateSelector?.options
                    ?.Find(t => string.Equals(t.id, "unity_terrain_heightmap", StringComparison.OrdinalIgnoreCase));
                if (templateConfig != null)
                    generator.SetPromptTemplateSelection(templateConfig);

                // 设置 Frontier 参数（强制 PNG，避免后端 jpeg 导致地形链路异常）
                generator.SetParameter("resolution", resolution);
                generator.SetParameter("aspectRatio", aspectRatio);
                generator.SetParameter("outputFormat", "png");

                // 同步提交任务
                var submitResult = TJGeneratorsGenerationService.SubmitTaskSync(generator, sessionId);
                if (!submitResult.Success)
                {
                    TJLog.LogError($"[GenerateTerrainTool] 任务提交失败 [{submitResult.ErrorCode}]: {submitResult.Message}");
                    return new Dictionary<string, object>
                    {
                        { "success",    false },
                        { "error_code", submitResult.ErrorCode },
                        { "message",    submitResult.Message }
                    };
                }

                TJLog.Log($"[GenerateTerrainTool] 任务提交成功，backend_task_id={submitResult.BackendTaskId}");

                // 创建占位符 PNG
                string placeholderPath = CreatePlaceholderTexture(outputPath);

                // 注册任务
                string capturedBackendTaskId = submitResult.BackendTaskId;
                string taskId = TerrainTaskTracker.CreateTask(prompt, imagePath, placeholderPath, capturedBackendTaskId);

                // 启动异步轮询
                var host = new TerrainPipelineHost(
                    placeholderPath,
                    sessionId,
                    (heightmapPath, previewUrl) =>
                    {
                        TerrainTaskTracker.MarkTaskCompleted(taskId, heightmapPath, previewUrl);
                        var t = TerrainTaskTracker.GetTask(taskId);
                        GenerationNotifier.NotifyCompleted("generate_terrain", taskId, capturedBackendTaskId,
                            new JObject
                            {
                                ["session_id"]       = sessionId,
                                ["prompt"]           = prompt ?? "",
                                ["heightmap_path"]   = heightmapPath ?? "",
                                ["preview_url"]      = previewUrl ?? "",
                                ["progress"]         = 100,
                                ["start_time"]       = t?.StartTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                                ["end_time"]         = t?.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                                ["duration_seconds"] = (t != null && t.EndTime.HasValue) ? (int)(t.EndTime.Value - t.StartTime).TotalSeconds : 0
                            });
                    },
                    errorMsg =>
                    {
                        TerrainTaskTracker.MarkTaskFailed(taskId, errorMsg);
                        GenerationNotifier.NotifyFailed("generate_terrain", taskId, capturedBackendTaskId, errorMsg,
                            new JObject { ["session_id"] = sessionId, ["prompt"] = prompt ?? "" });
                    }
                );
                var pipeline = new GenerationPipeline(host, ConfigType.Image, GenerationRequestOrigin.Agent, sessionId, "generate_terrain");
                string historyAssetGuid = CustomToolHistoryBindings.HistoryGuidFromPlaceholderAssetPath(placeholderPath);
                EditorCoroutineUtility.StartCoroutineOwnerless(
                    pipeline.StartFromSubmittedTask(generator, historyAssetGuid, submitResult.BackendTaskId));

                TJLog.Log($"[GenerateTerrainTool] 轮询已启动，task_id={taskId}, backend_task_id={submitResult.BackendTaskId}, placeholder={placeholderPath}");

                string mode = string.IsNullOrEmpty(imagePath) ? "text-to-heightmap" : "image-to-heightmap";

                return new Dictionary<string, object>
                {
                    { "success",            true },
                    { "submission_success", true },
                    { "message",
                        "Terrain heightmap generation started. " +
                        "STEP 1 (do now): Note the placeholder_path. " +
                        "STEP 2 (critical): Keep this worker active until the task is terminal and its assets are verified. " +
                        "STEP 3: A <bg_task_done> notification may arrive (~60s) " +
                        "containing ALL results (heightmap_path, preview_url, timing, etc.). " +
                        "When completed, call apply_terrain_heightmap with the heightmap_path. " +
                        "Wait up to 30 seconds in the current worker, then call query_local_task; repeat only while pending. Verify the real heightmap before applying it; do not resubmit or finish this worker on submission." },
                    { "task_id",            taskId },
                    { "backend_task_id",    submitResult.BackendTaskId },
                    { "status",             "submitted" },
                    { "mode",               mode },
                    { "prompt",             prompt ?? "" },
                    { "placeholder_path",   placeholderPath },
                    { "preview_url",        PreviewUrlHelper.BuildFixedPreviewUrl(submitResult.BackendTaskId) },
                    { "estimated_wait_seconds", 60 },
                    { "notification_mode",  "bg_task_done" }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateTerrainTool] Error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error generating terrain: {e.Message}" }
                };
            }
#else
            return new Dictionary<string, object>
            {
                { "success", false },
                { "message", "This tool only works in Unity Editor." }
            };
#endif
        }

        [ExecuteCustomTool.CustomTool("apply_terrain_heightmap",
            "Start async post-processing of a heightmap PNG and place a Unity Terrain in the scene. " +
            "ASYNC: returns apply_task_id before network size estimation and filtering complete. " +
            "Post-processing (luminance height + multi-scale spike/pit limit) runs in background (~10-60s). " +
            "Poll query_local_task with apply_task_id to check completion. " +
            "Parameters: heightmap_path (required), " +
            "task_id (optional but strongly recommended — prevents duplicate terrain creation; pass the task_id from generate_terrain), " +
            "session_id (optional, string — associates generated assets with a session for tracking), " +
            "application_id (optional string — reuse on retry; change only to intentionally create another copy). " +
            "Identical source content, scene and settings reuse the pending/completed apply task, including MCP imports. " +
            "use_default_options (bool, default true — spike limit on + detail_blend 0.25 + median + gaussian + thermal; bilateral off), " +
            "terrain_go_name (string, default 'TJGenerators Terrain'), " +
            "world_size_meters (float, optional — longer-side world length in meters. " +
            "If omitted: tool calls backend doubao-multimodal Lite to estimate from generate_terrain prompt; " +
            "if that fails, Unity shows an input dialog for the user before creating Terrain. " +
            "0 means 1 pixel = 1 meter. Height ~14% of side. Resolution unchanged). " +
            "prompt (string, optional on apply — used for size estimate when task_id has no stored prompt). " +
            "Advanced post-processing (only when use_default_options=false): " +
            "sketch_reconstruction (bool, default true — multi-scale spike/pit limit, keep valley corridors), " +
            "sketch_detail_blend (float 0-0.25, default 0.25), " +
            "sketch_structure_scale (float, 0=auto), sketch_illumination_scale (float, 0=unused), " +
            "median3x3 (bool), gaussian_blur (bool), gaussian_sigma (float), " +
            "bilateral_filter (bool), bilateral_sigma_space (float), bilateral_sigma_color (float), " +
            "thermal_erosion (bool), thermal_erosion_iterations (int), thermal_erosion_talus (float), " +
            "percentile_normalize (bool), percentile_low (float), percentile_high (float), " +
            "height_gamma (float, <1 more mountains, >1 more plains), " +
            "remap_output_min (float), remap_output_max (float).")]
        public static object ApplyTerrainHeightmap(JObject parameters)
        {
#if UNITY_EDITOR
            return ApplyTerrainHeightmapCore(parameters, TerrainWorldSizeEstimator.EstimateAsync);
#else
            return new Dictionary<string, object> { { "success", false }, { "message", "This tool only works in Unity Editor." } };
#endif
        }

#if UNITY_EDITOR
        internal static object ApplyTerrainHeightmapCore(JObject parameters,
            Func<string, string, string, Task<TerrainWorldSizeEstimator.EstimateResult>> estimateSize)
        {
            string startedApplyTaskId = null;
            try
            {
                string heightmapPath = parameters["heightmap_path"]?.ToString();
                if (string.IsNullOrEmpty(heightmapPath))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "'heightmap_path' parameter is required." }
                    };
                }

                string originalPath = heightmapPath.Replace('\\', '/');
                string absOriginal  = PathUtils.ToAbsoluteAssetPath(originalPath);
                if (!File.Exists(absOriginal))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"Heightmap file not found: {heightmapPath}. Make sure query_local_task returns status='completed' before calling apply." }
                    };
                }

                string taskId        = parameters["task_id"]?.ToString() ?? "";
                string sessionId     = parameters["session_id"]?.ToString() ?? "";
                string applicationId = parameters["application_id"]?.ToString() ?? "";
                GenerationRequestOrigin.SetWorkspaceName(parameters["workspace_name"]?.ToString());
                string terrainGoName = parameters["terrain_go_name"]?.ToString() ?? "TJGenerators Terrain";
                float? requestedWorldSize = ReadRequestedWorldSize(parameters);
                float worldSizeMeters = requestedWorldSize ?? 0f;
                string sizeAnalysisPrompt = parameters["prompt"]?.ToString() ?? "";
                string worldSizeSource = requestedWorldSize.HasValue ? "user" : "pending";

                var existingTask = string.IsNullOrEmpty(taskId) ? null : TerrainTaskTracker.GetTask(taskId);
                // 未显式传尺寸时，用 Stage 1 存的 prompt 做尺度分析。
                if (!requestedWorldSize.HasValue && string.IsNullOrWhiteSpace(sizeAnalysisPrompt) &&
                    existingTask != null && !string.IsNullOrWhiteSpace(existingTask.Prompt))
                {
                    sizeAnalysisPrompt = existingTask.Prompt;
                }

                // 解析后处理选项
                bool useDefaults = parameters["use_default_options"] == null ||
                                   parameters["use_default_options"].ToObject<bool>();
                var opts = useDefaults ? TerrainHeightmapPostProcessOptions.Default :
                    new TerrainHeightmapPostProcessOptions
                    {
                        sketchReconstruction     = GetBool(parameters, "sketch_reconstruction", true),
                        sketchDetailBlend        = GetFloat(parameters, "sketch_detail_blend", 0.25f),
                        sketchStructureScale     = GetFloat(parameters, "sketch_structure_scale", 0f),
                        sketchIlluminationScale  = GetFloat(parameters, "sketch_illumination_scale", 0f),
                        median3x3                = GetBool(parameters, "median3x3", true),
                        gaussianBlur             = GetBool(parameters, "gaussian_blur", true),
                        gaussianSigma            = GetFloat(parameters, "gaussian_sigma", 1.2f),
                        bilateralFilter          = GetBool(parameters, "bilateral_filter", false),
                        bilateralSigmaSpace      = GetFloat(parameters, "bilateral_sigma_space", 2.5f),
                        bilateralSigmaColor      = GetFloat(parameters, "bilateral_sigma_color", 0.12f),
                        thermalErosion           = GetBool(parameters, "thermal_erosion", true),
                        thermalErosionIterations = GetInt(parameters, "thermal_erosion_iterations", 8),
                        thermalErosionTalus      = GetFloat(parameters, "thermal_erosion_talus", 0.022f),
                        percentileNormalization  = GetBool(parameters, "percentile_normalize", true),
                        percentileLow            = GetFloat(parameters, "percentile_low", 0.01f),
                        percentileHigh           = GetFloat(parameters, "percentile_high", 0.99f),
                        heightGamma              = GetFloat(parameters, "height_gamma", 1.0f),
                        remapOutputMin           = GetFloat(parameters, "remap_output_min", 0f),
                        remapOutputMax           = GetFloat(parameters, "remap_output_max", 1f),
                    };

                var targetScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                // First-save aliases retain an unsaved scene's identity even if it is
                // closed or the domain reloads before the next application request.
                string applicationKey = BuildApplicationKey(absOriginal, File.ReadAllBytes(absOriginal), new JObject
                {
                    ["scene"] = TerrainApplySceneIdentity.GetIdentity(targetScene),
                    ["session"] = sessionId,
                    ["name"] = terrainGoName,
                    ["application_id"] = applicationId,
                    ["world_size_meters"] = requestedWorldSize.HasValue ? (JToken)requestedWorldSize.Value : JValue.CreateNull(),
                    ["prompt"] = requestedWorldSize.HasValue ? "" : sizeAnalysisPrompt,
                    ["options"] = JObject.FromObject(opts)
                });
                var reusable = TerrainApplyTaskTracker.FindReusableTask(applicationKey);
                if (reusable != null && reusable.Status == "completed" && !HasTerrainResult(reusable.TerrainDataPath, targetScene))
                {
                    TerrainApplyTaskTracker.MarkFailed(reusable.ApplyTaskId, "Previous terrain result was removed; application may be retried.");
                    reusable = null;
                }
                // Backfill a scene-specific record before the legacy task's single result
                // can be replaced by an application in another scene.
                if (reusable == null && string.IsNullOrEmpty(applicationId) && existingTask != null && existingTask.Status == "applied")
                {
                    string legacyDataPath = existingTask.TerrainDataPath;
                    string legacyGoName = existingTask.TerrainGoName;
                    if (!HasTerrainResult(legacyDataPath, targetScene))
                    {
                        var previous = existingTask.PreviousApplications.FindLast(r => HasTerrainResult(r.terrainDataPath, targetScene));
                        legacyDataPath = previous?.terrainDataPath;
                        legacyGoName = previous?.terrainGoName;
                    }
                    if (!string.IsNullOrEmpty(legacyDataPath))
                    {
                        string recoveredId = TerrainApplyTaskTracker.CreateTask(heightmapPath, applicationKey);
                        TerrainApplyTaskTracker.MarkCompleted(recoveredId, legacyDataPath, legacyGoName);
                        reusable = TerrainApplyTaskTracker.GetTask(recoveredId);
                    }
                }
                if (reusable != null)
                {
                    // Bridge recognizes Dictionary responses with success as already
                    // formatted; JObject would add a data wrapper only on retries.
                    var result = (Dictionary<string, object>)QueryTerrainApplyStatus(new JObject { ["apply_task_id"] = reusable.ApplyTaskId });
                    result["reused"] = true;
                    result["already_applied"] = reusable.Status == "completed";
                    return result;
                }

                string processedAssetPath = TerrainCreationUtils.GenerateProcessedHeightmapAssetPath(originalPath);
                if (string.IsNullOrEmpty(processedAssetPath))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "Cannot generate post-processing file path" }
                    };
                }
                string absProcessed = PathUtils.ToAbsoluteAssetPath(processedAssetPath);
                string processedDir = Path.GetDirectoryName(absProcessed);
                if (!string.IsNullOrEmpty(processedDir) && !Directory.Exists(processedDir))
                    Directory.CreateDirectory(processedDir);
                try { File.Copy(absOriginal, absProcessed, overwrite: true); }
                catch (Exception e)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "Failed to copy heightmap: " + e.Message }
                    };
                }

                if (!TerrainHeightmapPostProcessor.TryExtractPixelData(
                        absProcessed, out float[] luminance, out int imgW, out int imgH, out string extractErr))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "Failed to decode heightmap: " + extractErr }
                    };
                }

                // Read credentials/configuration on the main thread. Network waits run on a worker.
                string applyTaskId = TerrainApplyTaskTracker.CreateTask(heightmapPath, applicationKey);
                startedApplyTaskId = applyTaskId;
                Task<TerrainWorldSizeEstimator.EstimateResult> sizeEstimateTask;
                if (!requestedWorldSize.HasValue)
                {
                    string authToken = "";
                    string orgId = "";
                    try
                    {
                        authToken = Unity.UniAsset.Manager.Editor.InternalBridge.UnityConnectSession.instance.GetAccessToken() ?? "";
                        orgId = Unity.UniAsset.Manager.Editor.InternalBridge.UnityConnectSession.instance.GetOrgId(authToken) ?? "";
                    }
                    catch { /* LLM 将失败并走弹窗 */ }

                    sizeEstimateTask = estimateSize(sizeAnalysisPrompt, authToken, orgId);
                }
                else
                    sizeEstimateTask = Task.FromResult(new TerrainWorldSizeEstimator.EstimateResult(worldSizeMeters, "user", "explicit parameter"));
                TJLog.Log($"[GenerateTerrainTool] apply_terrain_heightmap started async: applyTaskId={applyTaskId}, processed={processedAssetPath}");

                string capturedApplyTaskId     = applyTaskId;
                string capturedProcessedAsset  = processedAssetPath;
                string capturedAbsProcessed    = absProcessed;
                string capturedTerrainGoName   = terrainGoName;
                float capturedWorldSizeMeters  = worldSizeMeters;
                float capturedWorldSizeRequested = requestedWorldSize ?? 0f;
                string capturedWorldSizeSource = worldSizeSource;
                string capturedSessionId       = sessionId;
                string capturedTaskId          = taskId;
                string capturedHeightmapPath   = heightmapPath;

                const int timeoutSeconds = 300;

                Task.Run(async () =>
                {
                    try
                    {
                        var estimate = await sizeEstimateTask.ConfigureAwait(false);
                        if (!estimate.Succeeded)
                        {
                            var input = new TaskCompletionSource<TerrainWorldSizeEstimator.EstimateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                            TerrainApplyMainThreadDispatcher.Dispatch(() =>
                            {
                                try
                                {
                                    input.SetResult(TerrainWorldSizeEstimator.TryAskUserMeters(sizeAnalysisPrompt, out float meters, out string error)
                                        ? new TerrainWorldSizeEstimator.EstimateResult(meters, "user_dialog", "")
                                        : new TerrainWorldSizeEstimator.EstimateResult(0f, "failed", error ?? "world_size_meters required"));
                                }
                                catch (Exception ex) { input.SetException(ex); }
                            });
                            estimate = await input.Task.ConfigureAwait(false);
                            if (!estimate.Succeeded)
                                throw new InvalidOperationException("world_size_required: " + estimate.Reason);
                        }
                        capturedWorldSizeMeters = estimate.Meters;
                        capturedWorldSizeSource = estimate.Source;
                        var filterStart = DateTime.Now;
                        TerrainHeightmapPostProcessor.ApplyFilters(luminance, imgW, imgH, opts);
                        double filterSeconds = (DateTime.Now - filterStart).TotalSeconds;
                        TJLog.Log($"[GenerateTerrainTool] ApplyFilters done in {filterSeconds:F1}s");

                        TerrainApplyMainThreadDispatcher.Dispatch(() =>
                        {
                            try
                            {
                                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene() != targetScene)
                                    throw new InvalidOperationException("Active scene changed during terrain application; return to the original scene and retry.");
                                if (!TerrainHeightmapPostProcessor.TryEncodeAndSave(
                                        capturedAbsProcessed, luminance, imgW, imgH, out string saveErr))
                                {
                                    TerrainApplyTaskTracker.MarkFailed(capturedApplyTaskId, "PNG encoding failed: " + saveErr);
                                    GenerationNotifier.NotifyFailed("apply_terrain_heightmap", capturedApplyTaskId, "", "PNG encoding failed: " + saveErr,
                                        new JObject { ["session_id"] = capturedSessionId, ["heightmap_path"] = capturedHeightmapPath });
                                    TJLog.LogError($"[GenerateTerrainTool] encode failed: {saveErr}");
                                    return;
                                }

                                AssetDatabase.ImportAsset(capturedProcessedAsset, ImportAssetOptions.ForceUpdate);

                                string terrainDataPath; string goName; Vector3 terrainSize;
                                try
                                {
                                    (terrainDataPath, goName, terrainSize) = TerrainCreationUtils.CreateTerrainFromHeightmap(
                                        capturedProcessedAsset, capturedTerrainGoName, capturedWorldSizeMeters);
                                }
                                catch (Exception ex)
                                {
                                    TerrainApplyTaskTracker.MarkFailed(capturedApplyTaskId, "Terrain creation failed: " + ex.Message);
                                    GenerationNotifier.NotifyFailed("apply_terrain_heightmap", capturedApplyTaskId, "", "Terrain creation failed: " + ex.Message,
                                        new JObject { ["session_id"] = capturedSessionId, ["heightmap_path"] = capturedHeightmapPath });
                                    TJLog.LogError($"[GenerateTerrainTool] CreateTerrain failed: {ex}");
                                    return;
                                }

                                var terrainRef   = TJGeneratorsAssetReference.FromPath(terrainDataPath);
                                var processedRef = TJGeneratorsAssetReference.FromPath(capturedProcessedAsset);
                                TJGeneratorsGenerationLabel.EnableLabel(terrainRef);
                                TJGeneratorsGenerationLabel.EnableSessionLabel(terrainRef, capturedSessionId);
                                TJGeneratorsGenerationLabel.EnableLabel(processedRef);
                                TJGeneratorsGenerationLabel.EnableSessionLabel(processedRef, capturedSessionId);

                                TerrainApplyTaskTracker.MarkCompleted(capturedApplyTaskId, terrainDataPath, goName);
                                GenerationNotifier.NotifyCompleted("apply_terrain_heightmap", capturedApplyTaskId, "",
                                    new JObject
                                    {
                                        ["session_id"]             = capturedSessionId,
                                        ["heightmap_path"]         = capturedHeightmapPath,
                                        ["terrain_data_path"]      = terrainDataPath ?? "",
                                        ["terrain_go_name"]        = goName ?? "",
                                        ["world_size_meters"]      = capturedWorldSizeMeters > 1f
                                            ? capturedWorldSizeMeters
                                            : (float)Math.Max(terrainSize.x, terrainSize.z),
                                        ["world_size_requested"]   = capturedWorldSizeRequested,
                                        ["world_size_source"]      = capturedWorldSizeSource ?? "",
                                        ["terrain_width_meters"]   = terrainSize.x,
                                        ["terrain_length_meters"]  = terrainSize.z,
                                        ["terrain_height_meters"]  = terrainSize.y,
                                        ["progress"]               = 100
                                    });
                                // Explicit copies must not replace an already-applied legacy
                                // task's default result (which may predate apply-task tracking).
                                if (!string.IsNullOrEmpty(capturedTaskId) &&
                                    (string.IsNullOrEmpty(applicationId) || TerrainTaskTracker.GetTask(capturedTaskId)?.Status != "applied"))
                                    TerrainTaskTracker.MarkTaskApplied(capturedTaskId, terrainDataPath, goName);

                                TJLog.Log($"[GenerateTerrainTool] Terrain created async: {goName}, TerrainData={terrainDataPath}, " +
                                          $"size={terrainSize.x:F1}×{terrainSize.z:F1}×{terrainSize.y:F1}m, " +
                                          $"world_size_source={capturedWorldSizeSource}, requested={capturedWorldSizeRequested}");
                            }
                            catch (Exception ex)
                            {
                                TerrainApplyTaskTracker.MarkFailed(capturedApplyTaskId, ex.Message);
                                GenerationNotifier.NotifyFailed("apply_terrain_heightmap", capturedApplyTaskId, "", ex.Message,
                                    new JObject { ["session_id"] = capturedSessionId, ["heightmap_path"] = capturedHeightmapPath });
                                TJLog.LogError($"[GenerateTerrainTool] main-thread finalization failed: {ex}");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        string bgErrMsg = "Background filtering failed: " + ex.Message;
                        TerrainApplyMainThreadDispatcher.Dispatch(() =>
                        {
                            TerrainApplyTaskTracker.MarkFailed(capturedApplyTaskId, bgErrMsg);
                            GenerationNotifier.NotifyFailed("apply_terrain_heightmap", capturedApplyTaskId, "", bgErrMsg,
                                new JObject { ["session_id"] = capturedSessionId, ["heightmap_path"] = capturedHeightmapPath });
                        });
                        TJLog.LogError($"[GenerateTerrainTool] background filter failed: {ex}");
                    }
                });

                return new Dictionary<string, object>
                {
                    { "success",          true },
                    { "apply_task_id",    applyTaskId },
                    { "status",           "processing" },
                    { "message",
                        $"Post-processing started in background. " +
                        "A <bg_task_done> notification will arrive automatically when terrain is ready (~60-180s). " +
                        "Do NOT retry apply_terrain_heightmap. " +
                        "Wait up to 30 seconds in the current worker, then call query_local_task with apply_task_id; repeat only while pending. Verify Terrain and TerrainData before reporting success; do not reapply while pending." },
                    { "estimated_seconds",        90 },
                    { "notification_mode",        "bg_task_done" },
                    { "max_wait_before_retry_seconds", timeoutSeconds }
                };
            }
            catch (Exception e)
            {
                if (startedApplyTaskId != null)
                    TerrainApplyTaskTracker.MarkFailed(startedApplyTaskId, e.Message);
                TJLog.LogError($"[GenerateTerrainTool] apply error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error starting terrain apply: {e.Message}" }
                };
            }
        }
#endif

        public static object QueryTerrainApplyStatus(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                string applyTaskId = parameters["apply_task_id"]?.ToString();
                if (string.IsNullOrEmpty(applyTaskId))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "'apply_task_id' is required. It is returned by apply_terrain_heightmap." }
                    };
                }

                var task = TerrainApplyTaskTracker.GetTask(applyTaskId);
                if (task == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"apply task '{applyTaskId}' not found. It may be from a previous Editor session." }
                    };
                }

                var result = new Dictionary<string, object>
                {
                    { "success",        true },
                    { "apply_task_id",  task.ApplyTaskId },
                    { "status",         task.Status },
                    { "heightmap_path", task.HeightmapPath ?? "" },
                    { "start_time",     task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                if (task.EndTime.HasValue)
                {
                    result["end_time"]         = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    result["duration_seconds"] = (int)(task.EndTime.Value - task.StartTime).TotalSeconds;
                }

                int elapsedSeconds = (int)(DateTime.Now - task.StartTime).TotalSeconds;
                result["elapsed_seconds"] = elapsedSeconds;

                switch (task.Status)
                {
                    case "processing":
                        result["message"] = $"Background post-processing is running ({elapsedSeconds}s elapsed). 2K images take 60-180s — this is normal. Do NOT retry apply_terrain_heightmap.";
                        break;
                    case "completed":
                        result["terrain_data_path"] = task.TerrainDataPath ?? "";
                        result["terrain_go_name"]   = task.TerrainGoName ?? "";
                        result["message"]           = "Terrain is in the scene. WORKFLOW COMPLETE. Do NOT call apply_terrain_heightmap again.";
                        result["next_step"]         = "DONE. Terrain GameObject is selected in scene.";
                        break;
                    case "failed":
                        result["error"]   = task.ErrorMessage ?? "";
                        result["message"] = "Post-processing failed. You may retry with apply_terrain_heightmap.";
                        break;
                }

                return result;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateTerrainTool] query apply status error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error querying apply status: {e.Message}" }
                };
            }
#else
            return new Dictionary<string, object>
            {
                { "success", false },
                { "message", "This tool only works in Unity Editor." }
            };
#endif
        }

        public static object QueryTerrainStatus(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                string taskId = parameters["task_id"]?.ToString();

                if (string.IsNullOrEmpty(taskId))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "'task_id' parameter is required" }
                    };
                }

                var task = TerrainTaskTracker.GetTask(taskId);

                if (task == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"Task '{taskId}' not found. It may have been completed and cleaned up." }
                    };
                }

                var result = new Dictionary<string, object>
                {
                    { "success",    true },
                    { "task_id",    task.TaskId },
                    { "status",     task.Status },
                    { "progress",   task.Progress },
                    { "prompt",     task.Prompt },
                    { "start_time", task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                if (!string.IsNullOrEmpty(task.ImagePath))     result["input_image_path"] = task.ImagePath;
                if (!string.IsNullOrEmpty(task.HeightmapPath)) result["heightmap_path"]   = task.HeightmapPath;
                result["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);
                if (!string.IsNullOrEmpty(task.ErrorMessage))  result["error"]            = task.ErrorMessage;

                if (task.EndTime.HasValue)
                {
                    result["end_time"]        = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    result["duration_seconds"] = (int)(task.EndTime.Value - task.StartTime).TotalSeconds;
                }

                if (task.Status == "generating" || task.Status == "recovering")
                {
                    if (!string.IsNullOrEmpty(task.PlaceholderPath))
                        result["placeholder_path"] = task.PlaceholderPath;
                }
                else if (task.Status == "completed")
                {
                    result["next_step"] = "Call apply_terrain_heightmap ONCE with this task_id and heightmap_path. Do NOT call it again after that.";
                }
                else if (task.Status == "applied")
                {
                    result["terrain_data_path"] = task.TerrainDataPath ?? "";
                    result["terrain_go_name"]   = task.TerrainGoName ?? "";
                    result["next_step"]         = "DONE. Terrain is already in the scene. Do NOT call apply_terrain_heightmap again. Workflow complete.";
                }

                return result;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateTerrainTool] Query error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error querying task status: {e.Message}" }
                };
            }
#else
            return new Dictionary<string, object>
            {
                { "success", false },
                { "message", "This tool only works in Unity Editor." }
            };
#endif
        }

        public static object ListTerrainTasks(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                var tasks    = TerrainTaskTracker.GetAllTasks();
                var taskList = new List<Dictionary<string, object>>();

                foreach (var task in tasks)
                {
                    var taskData = new Dictionary<string, object>
                    {
                        { "task_id",    task.TaskId },
                        { "status",     task.Status },
                        { "progress",   task.Progress },
                        { "prompt",     task.Prompt },
                        { "start_time", task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                    };

                    if (!string.IsNullOrEmpty(task.ImagePath))     taskData["input_image_path"] = task.ImagePath;
                    if (!string.IsNullOrEmpty(task.HeightmapPath)) taskData["heightmap_path"]   = task.HeightmapPath;
                    taskData["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);
                    if (!string.IsNullOrEmpty(task.ErrorMessage))  taskData["error"]            = task.ErrorMessage;
                    if (task.EndTime.HasValue) taskData["end_time"] = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");

                    taskList.Add(taskData);
                }

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "count",   taskList.Count },
                    { "tasks",   taskList }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateTerrainTool] List error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error listing tasks: {e.Message}" }
                };
            }
#else
            return new Dictionary<string, object>
            {
                { "success", false },
                { "message", "This tool only works in Unity Editor." }
            };
#endif
        }

#if UNITY_EDITOR
        private static void EnsureAssetDatabaseFolder(string folderPath)
        {
            folderPath = folderPath.Replace('\\', '/').TrimEnd('/');
            string[] parts = folderPath.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string CreatePlaceholderTexture(string outputPath)
        {
            string placeholderPath;
            if (!string.IsNullOrEmpty(outputPath))
            {
                string dir = Path.GetDirectoryName(outputPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir))
                    EnsureAssetDatabaseFolder(dir);
                placeholderPath = AssetDatabase.GenerateUniqueAssetPath(
                    Path.ChangeExtension(outputPath, ".png"));
            }
            else
            {
                if (!AssetDatabase.IsValidFolder("Assets/TJGenerators"))
                    AssetDatabase.CreateFolder("Assets", "TJGenerators");
                if (!AssetDatabase.IsValidFolder("Assets/TJGenerators/History"))
                    AssetDatabase.CreateFolder("Assets/TJGenerators", "History");
                string uniqueName = "Terrain_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                placeholderPath = AssetDatabase.GenerateUniqueAssetPath("Assets/TJGenerators/History/" + uniqueName);
            }

            // 创建 1x1 灰色占位 PNG
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, new Color(0.5f, 0.5f, 0.5f, 1f));
            tex.Apply();
            byte[] pngBytes = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            string absolutePath = PathUtils.ToAbsoluteAssetPath(placeholderPath);
            File.WriteAllBytes(absolutePath, pngBytes);
            PathUtils.ImportAssetAfterDiskWrite(placeholderPath);

            return placeholderPath;
        }

        internal static float? ReadRequestedWorldSize(JObject parameters)
        {
            var value = parameters["world_size_meters"];
            if (value == null || value.Type == JTokenType.Null) return null;
            float meters = value.ToObject<float>();
            if (float.IsNaN(meters) || float.IsInfinity(meters) || meters < 0f || (meters > 0f && meters <= 1f))
                throw new ArgumentException("world_size_meters must be 0 (1 pixel = 1 meter) or a finite number greater than 1.");
            return meters;
        }

        private static bool HasTerrainResult(string terrainDataPath, UnityEngine.SceneManagement.Scene scene)
        {
            if (string.IsNullOrEmpty(terrainDataPath)) return false;
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(terrainDataPath);
            if (data == null) return false;
            foreach (var terrain in Resources.FindObjectsOfTypeAll<Terrain>())
                if (terrain.gameObject.scene == scene && terrain.terrainData == data)
                    return true;
            return false;
        }

        internal static string BuildApplicationKey(string heightmapPath, byte[] sourceBytes, JObject settings)
        {
            // Content and effective settings matter; transient MCP/import IDs do not.
            using (var hash = SHA256.Create())
            {
                string contentHash = BitConverter.ToString(hash.ComputeHash(sourceBytes));
                string path = Path.GetFullPath(heightmapPath).Replace('\\', '/');
                if (Path.DirectorySeparatorChar == '\\') path = path.ToUpperInvariant();
                string request = path + "\n" + contentHash + "\n" + settings.ToString();
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(request))).Replace("-", "");
            }
        }

        private static bool GetBool(JObject p, string key, bool defaultValue)
            => p[key] != null ? p[key].ToObject<bool>() : defaultValue;

        private static float GetFloat(JObject p, string key, float defaultValue)
            => p[key] != null ? p[key].ToObject<float>() : defaultValue;

        private static int GetInt(JObject p, string key, int defaultValue)
            => p[key] != null ? p[key].ToObject<int>() : defaultValue;

        internal static void ApplyTerrainRecoveryGeneratorSettings(
            DynamicGenerator generator,
            GeneratorConfig config,
            InterruptedTaskData interrupted = null,
            TerrainTaskTracker.TerrainTaskInfo trackerTask = null)
        {
            if (generator == null) return;

            if (interrupted != null || trackerTask != null)
            {
                string prompt = !string.IsNullOrEmpty(trackerTask?.Prompt) ? trackerTask.Prompt : interrupted?.prompt;
                string imagePath = !string.IsNullOrEmpty(trackerTask?.ImagePath) ? trackerTask.ImagePath : interrupted?.imagePath;

                if (!string.IsNullOrEmpty(prompt))
                    generator.SetTextPrompt(prompt);
                if (!string.IsNullOrEmpty(imagePath))
                    generator.SetImagePath(imagePath);
            }

            var templateConfig = config?.promptTemplateSelector?.options
                ?.Find(t => string.Equals(t.id, "unity_terrain_heightmap", StringComparison.OrdinalIgnoreCase));
            if (templateConfig != null)
                generator.SetPromptTemplateSelection(templateConfig);

            generator.SetParameter("resolution", "2K");
            generator.SetParameter("aspectRatio", "1:1");
            generator.SetParameter("outputFormat", "png");
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// Automatically resumes interrupted generate_terrain tasks after domain reload.
    /// </summary>
    [InitializeOnLoad]
    public static class TerrainDomainReloadRecovery
    {
        static TerrainDomainReloadRecovery()
        {
            CustomToolDomainReloadRecovery.Schedule(ResumeInterruptedTasks);
        }

        private static void ResumeInterruptedTasks()
        {
            CustomToolDomainReloadRecovery.Resume(
                "GenerateTerrainTool",
                ConfigType.Image,
                t => t.toolName == "generate_terrain",
                () => TerrainTaskTracker.GetAllTasks(),
                (interrupted, config, generator) =>
                {
                    var trackerTask = TerrainTaskTracker.GetTaskByBackendId(interrupted.backendTaskId);
                    if (trackerTask != null)
                    {
                        CustomToolDomainReloadRecovery.MarkTrackerRecoveringIfNeeded(trackerTask.Status, () =>
                        {
                            TerrainTaskTracker.ApplyTaskUpdate(trackerTask, t => t.Status = "recovering");
                        });
                    }
                    else
                    {
                        string placeholderPath = CustomToolDomainReloadRecovery.ResolveAssetPath(interrupted.targetAssetGuid);
                        trackerTask = TerrainTaskTracker.CreateRecoveredTask(
                            interrupted.backendTaskId, interrupted.prompt, placeholderPath, interrupted.timestamp,
                            interrupted.imagePath);
                    }

                    string placeholderPathForHost = trackerTask.PlaceholderPath ?? "";
                    if (string.IsNullOrEmpty(placeholderPathForHost))
                        placeholderPathForHost = CustomToolDomainReloadRecovery.ResolveAssetPath(interrupted.targetAssetGuid);

                    GenerateTerrainTool.ApplyTerrainRecoveryGeneratorSettings(generator, config, interrupted, trackerTask);

                    string sessionId = interrupted.sessionId ?? "";
                    string capturedBackendTaskId = interrupted.backendTaskId;
                    string taskId = trackerTask.TaskId;

                    var host = new TerrainPipelineHost(
                        placeholderPathForHost,
                        sessionId,
                        (heightmapPath, previewUrl) =>
                        {
                            TerrainTaskTracker.MarkTaskCompleted(taskId, heightmapPath, previewUrl);
                            var t = TerrainTaskTracker.GetTask(taskId);
                            GenerationNotifier.NotifyCompleted("generate_terrain", taskId, capturedBackendTaskId,
                                new JObject
                                {
                                    ["session_id"]       = sessionId,
                                    ["prompt"]           = t?.Prompt ?? interrupted.prompt ?? "",
                                    ["heightmap_path"]   = heightmapPath ?? "",
                                    ["preview_url"]      = previewUrl ?? "",
                                    ["progress"]         = 100,
                                    ["start_time"]       = t?.StartTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                                    ["end_time"]         = t?.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                                    ["duration_seconds"] = (t != null && t.EndTime.HasValue) ? (int)(t.EndTime.Value - t.StartTime).TotalSeconds : 0
                                });
                        },
                        errorMsg =>
                        {
                            TerrainTaskTracker.MarkTaskFailed(taskId, errorMsg);
                            GenerationNotifier.NotifyFailed("generate_terrain", taskId, capturedBackendTaskId, errorMsg,
                                new JObject
                                {
                                    ["session_id"] = sessionId,
                                    ["prompt"]     = trackerTask.Prompt ?? interrupted.prompt ?? ""
                                });
                        });

                    CustomToolDomainReloadRecovery.StartPolling(
                        "GenerateTerrainTool", host, ConfigType.Image,
                        sessionId, "generate_terrain", generator, interrupted.backendTaskId);
                });
        }
    }

    /// <summary>
    /// IGenerationPipelineHost implementation for headless terrain heightmap generation via custom tools.
    /// Keeps TextureImporterType.Default (not Sprite) — heightmaps are raw grayscale data.
    /// </summary>
    internal class TerrainPipelineHost : HeadlessPipelineHostBase, IMediaAssetPipelineHost
    {
        private readonly string _placeholderPath;
        private readonly TJGeneratorsAssetReference _placeholderRef;
        private readonly string _sessionId;
        private readonly Action<string, string> _onCompleted;  // (heightmapPath, previewUrl)
        private readonly Action<string> _onFailed;

        public TerrainPipelineHost(string placeholderPath, string sessionId, Action<string, string> onCompleted, Action<string> onFailed)
        {
            _placeholderPath = placeholderPath;
            _placeholderRef  = TJGeneratorsAssetReference.FromPath(placeholderPath);
            _sessionId       = sessionId ?? "";
            _onCompleted     = onCompleted;
            _onFailed        = onFailed;
        }

        protected override string DialogLogTag => "GenerateTerrainTool";
        protected override Action<string> DialogFailedCallback => errorMessage => _onFailed?.Invoke(errorMessage);

        public override TJGeneratorsAssetReference GetTargetAsset() => _placeholderRef;

        public void StartEditorCoroutine(IEnumerator coroutine)
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(coroutine);
        }

        public string GetAssetSavePath(PipelineMediaType _type, ModelGeneratorBase generator) =>
            // 返回 placeholder 路径，pipeline 直接覆盖文件内容，保持 GUID 不变
            _type == PipelineMediaType.Texture ? _placeholderPath : null;

        public void OnAssetSaved(PipelineMediaType _type, string savePath, ModelGeneratorBase generator)
        {
            if (_type != PipelineMediaType.Texture) return;

            TJLog.Log($"[GenerateTerrainTool] Heightmap saved: {savePath}");

            // 保持 TextureImporterType.Default（高度图不是 Sprite）
            TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(savePath));
            TJGeneratorsGenerationLabel.EnableSessionLabel(TJGeneratorsAssetReference.FromPath(savePath), _sessionId);
            _onCompleted?.Invoke(savePath, generator.CurrentPreviewUrl);
        }
    }

    [InitializeOnLoad]
    internal static class TerrainApplyMainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        static TerrainApplyMainThreadDispatcher() => EditorApplication.update += Pump;

        public static void Dispatch(Action action)
        {
            if (action == null) return;
            _queue.Enqueue(action);
        }

        private static void Pump()
        {
            while (_queue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { TJLog.LogError($"[TerrainApplyMainThreadDispatcher] {e}"); }
            }
        }
    }
#endif
}
