using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Codely.Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;

#if UNITY_EDITOR
using TJGenerators;
using TJGenerators.Generators;
using TJGenerators.Config;
using TJGenerators.Pipeline;
using TJGenerators.Utils;
#endif

namespace UnityTcp.Editor.Tools
{
    /// <summary>
    /// Tracks active sprite generation tasks
    /// </summary>
    public static class SpriteTaskTracker
    {
#if UNITY_EDITOR
        [Serializable]
        private class PersistedTask
        {
            public string taskId;
            public string generatorId;
            public string prompt;
            public string imagePath;
            public string typeId;
            public string styleId;
            public string status;
            public int    progress;
            public string spritePath;
            public string errorMessage;
            public long   startTimeTicks;
            public long   endTimeTicks;
            public string previewUrl;
            public string placeholderPath;
            public string backendTaskId;
        }

        public class SpriteTaskInfo : IGenerationTaskInfo
        {
            public string TaskId { get; set; }
            public string GeneratorId { get; set; }
            public string Prompt { get; set; }
            public string ImagePath { get; set; }
            public string TypeId { get; set; }
            public string StyleId { get; set; }
            public string Status { get; set; }
            public int Progress { get; set; }
            public string SpritePath { get; set; }
            public string ErrorMessage { get; set; }
            public string PreviewUrl { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public string PlaceholderPath { get; set; }
            public string BackendTaskId { get; set; }
        }

        private static readonly GenerationTaskTrackerStore<SpriteTaskInfo, PersistedTask> Store =
            new GenerationTaskTrackerStore<SpriteTaskInfo, PersistedTask>(
                "TJGen_Sprite", BuildPersisted, FromPersisted);

        private static PersistedTask BuildPersisted(SpriteTaskInfo info) => new PersistedTask
        {
            taskId          = info.TaskId,
            generatorId     = info.GeneratorId,
            prompt          = info.Prompt ?? "",
            imagePath       = info.ImagePath ?? "",
            typeId          = info.TypeId ?? "",
            styleId         = info.StyleId ?? "",
            status          = info.Status,
            progress        = info.Progress,
            spritePath      = info.SpritePath ?? "",
            errorMessage    = info.ErrorMessage ?? "",
            startTimeTicks  = info.StartTime.Ticks,
            endTimeTicks    = info.EndTime?.Ticks ?? 0,
            previewUrl      = info.PreviewUrl ?? "",
            placeholderPath = info.PlaceholderPath ?? "",
            backendTaskId   = info.BackendTaskId ?? ""
        };

        private static SpriteTaskInfo FromPersisted(PersistedTask p) => new SpriteTaskInfo
        {
            TaskId          = p.taskId,
            GeneratorId     = p.generatorId,
            Prompt          = p.prompt,
            ImagePath       = p.imagePath,
            TypeId          = p.typeId,
            StyleId         = p.styleId,
            Status          = p.status,
            Progress        = p.progress,
            SpritePath      = p.spritePath,
            ErrorMessage    = p.errorMessage,
            PreviewUrl      = p.previewUrl,
            StartTime       = new DateTime(p.startTimeTicks),
            EndTime         = p.endTimeTicks > 0 ? (DateTime?)new DateTime(p.endTimeTicks) : null,
            PlaceholderPath = p.placeholderPath,
            BackendTaskId   = p.backendTaskId
        };

        internal static void ApplyTaskUpdate(SpriteTaskInfo task, Action<SpriteTaskInfo> mutate) =>
            Store.ApplyTaskUpdate(task, mutate);

        public static void MarkTaskCompleted(string taskId, string spritePath, string previewUrl = null)
        {
            var task = Store.GetTask(taskId);
            if (task == null) return;
            Store.ApplyTaskUpdate(task, t =>
            {
                t.Status     = "completed";
                t.Progress   = 100;
                t.SpritePath = spritePath;
                t.PreviewUrl = previewUrl;
                t.EndTime    = DateTime.Now;
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

        public static SpriteTaskInfo GetTask(string taskId) => Store.GetTask(taskId);

        public static List<SpriteTaskInfo> GetAllTasks() => Store.GetAllTasks();

        public static SpriteTaskInfo GetTaskByBackendId(string backendTaskId) =>
            Store.GetTaskByBackendId(backendTaskId);

        public static SpriteTaskInfo CreateRecoveredTask(
            string backendTaskId, string prompt, string placeholderPath, long timestampMs, string generatorId = null)
        {
            return Store.CreateRecoveredTask(backendTaskId, () => new SpriteTaskInfo
            {
                TaskId          = $"recovered_{backendTaskId}",
                BackendTaskId   = backendTaskId,
                GeneratorId     = generatorId ?? "",
                Prompt          = prompt ?? "",
                PlaceholderPath = placeholderPath ?? "",
                Status          = "recovering",
                Progress        = 0,
                StartTime       = timestampMs > 0
                                    ? DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).LocalDateTime
                                    : DateTime.Now
            });
        }

        public static void RemoveTask(string taskId) => Store.RemoveTask(taskId);

        public static void CleanupCompletedTasks() => Store.CleanupCompletedTasks();
#endif
    }

    // Previous versions persist sprite tasks across editor reloads.
    public static class GenerateSpriteTool
    {
        public static object QuerySpriteStatus(JObject parameters)
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

                var task = SpriteTaskTracker.GetTask(taskId);

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
                    { "success", true },
                    { "task_id", task.TaskId },
                    { "generator_id", task.GeneratorId },
                    { "status", task.Status },
                    { "progress", task.Progress },
                    { "prompt", task.Prompt },
                    { "start_time", task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                if (!string.IsNullOrEmpty(task.TypeId)) result["type_id"] = task.TypeId;
                if (!string.IsNullOrEmpty(task.StyleId)) result["style_id"] = task.StyleId;
                if (!string.IsNullOrEmpty(task.SpritePath)) result["sprite_path"] = task.SpritePath;
                result["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);
                if (!string.IsNullOrEmpty(task.ErrorMessage)) result["error"] = task.ErrorMessage;

                if (task.EndTime.HasValue)
                {
                    result["end_time"] = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    result["duration_seconds"] = (int)(task.EndTime.Value - task.StartTime).TotalSeconds;
                }

                if (task.Status == "generating")
                {
                    if (!string.IsNullOrEmpty(task.PlaceholderPath))
                        result["placeholder_path"] = task.PlaceholderPath;
                }

                return result;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateSpriteTool] Query error: {e}");
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

        public static object ListSpriteTasks(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                var tasks = SpriteTaskTracker.GetAllTasks();
                var taskList = new List<Dictionary<string, object>>();

                foreach (var task in tasks)
                {
                    var taskData = new Dictionary<string, object>
                    {
                        { "task_id", task.TaskId },
                        { "generator_id", task.GeneratorId },
                        { "status", task.Status },
                        { "progress", task.Progress },
                        { "prompt", task.Prompt },
                        { "start_time", task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                    };

                    if (!string.IsNullOrEmpty(task.TypeId)) taskData["type_id"] = task.TypeId;
                    if (!string.IsNullOrEmpty(task.StyleId)) taskData["style_id"] = task.StyleId;
                    if (!string.IsNullOrEmpty(task.SpritePath)) taskData["sprite_path"] = task.SpritePath;
                    taskData["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);
                    if (!string.IsNullOrEmpty(task.ErrorMessage)) taskData["error"] = task.ErrorMessage;
                    if (task.EndTime.HasValue) taskData["end_time"] = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");

                    taskList.Add(taskData);
                }

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "count", taskList.Count },
                    { "tasks", taskList }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateSpriteTool] List error: {e}");
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

    }

#if UNITY_EDITOR
    /// <summary>
    /// Automatically resumes interrupted generate_sprite tasks after domain reload.
    /// </summary>
    [InitializeOnLoad]
    public static class SpriteDomainReloadRecovery
    {
        static SpriteDomainReloadRecovery()
        {
            CustomToolDomainReloadRecovery.Schedule(ResumeInterruptedTasks);
        }

        private static void ResumeInterruptedTasks()
        {
            CustomToolDomainReloadRecovery.Resume(
                "GenerateSpriteTool",
                ConfigType.Sprite,
                t => t.toolName == "generate_sprite",
                () => SpriteTaskTracker.GetAllTasks(),
                (interrupted, _, generator) =>
                {
                    var trackerTask = SpriteTaskTracker.GetTaskByBackendId(interrupted.backendTaskId);
                    if (trackerTask != null)
                    {
                        CustomToolDomainReloadRecovery.MarkTrackerRecoveringIfNeeded(trackerTask.Status, () =>
                        {
                            SpriteTaskTracker.ApplyTaskUpdate(trackerTask, t => t.Status = "recovering");
                        });
                    }
                    else
                    {
                        string placeholderPath = CustomToolDomainReloadRecovery.ResolveAssetPath(interrupted.targetAssetGuid);
                        trackerTask = SpriteTaskTracker.CreateRecoveredTask(
                            interrupted.backendTaskId, interrupted.prompt, placeholderPath, interrupted.timestamp,
                            interrupted.modelVersion);
                    }

                    string placeholderPathForHost = trackerTask.PlaceholderPath ?? "";
                    if (string.IsNullOrEmpty(placeholderPathForHost))
                        placeholderPathForHost = CustomToolDomainReloadRecovery.ResolveAssetPath(interrupted.targetAssetGuid);

                    var host = new SpriteRecoveryHost(
                        placeholderPathForHost, interrupted.backendTaskId, interrupted.sessionId, generator);
                    CustomToolDomainReloadRecovery.StartPolling(
                        "GenerateSpriteTool", host, ConfigType.Sprite,
                        interrupted.sessionId, "generate_sprite", generator, interrupted.backendTaskId);
                });
        }
    }

    /// <summary>
    /// Headless pipeline host for resuming sprite tasks after domain reload.
    /// </summary>
    internal class SpriteRecoveryHost : HeadlessPipelineHostBase, IMediaAssetPipelineHost
    {
        private readonly string _placeholderPath;
        private readonly TJGeneratorsAssetReference _placeholderRef;
        private readonly string _backendTaskId;
        private readonly string _sessionId;
        private readonly ModelGeneratorBase _generator;

        public SpriteRecoveryHost(string placeholderPath, string backendTaskId, string sessionId, ModelGeneratorBase generator)
        {
            _placeholderPath = placeholderPath ?? "";
            _placeholderRef = string.IsNullOrEmpty(_placeholderPath)
                ? null
                : TJGeneratorsAssetReference.FromPath(_placeholderPath);
            _backendTaskId = backendTaskId;
            _sessionId = sessionId ?? "";
            _generator = generator;
        }

        protected override string DialogLogTag => "SpriteRecovery";

        public override TJGeneratorsAssetReference GetTargetAsset() => _placeholderRef;

        public override void Repaint()
        {
            if (_generator == null) return;
            var trackerTask = SpriteTaskTracker.GetTaskByBackendId(_backendTaskId);
            if (trackerTask == null || !TJGeneratorsTaskRecovery.IsRecoverableTrackerStatus(trackerTask.Status)) return;

            int progress = _generator.CurrentProgress;
            if (progress <= trackerTask.Progress) return;

            SpriteTaskTracker.ApplyTaskUpdate(trackerTask, t =>
            {
                t.Status = "generating";
                t.Progress = progress;
            });
        }

        public override void ShowDialog(string title, string message)
        {
            base.ShowDialog(title, message);

            if (ErrorDialogUtils.IsErrorDialog(title))
            {
                var trackerTask = SpriteTaskTracker.GetTaskByBackendId(_backendTaskId);
                if (trackerTask != null)
                {
                    var friendlyError = ErrorDialogUtils.ConvertToUserFriendlyError(title, message);
                    SpriteTaskTracker.MarkTaskFailed(trackerTask.TaskId, friendlyError.TechnicalMessage);
                    GenerationNotifier.NotifyFailed(
                        "generate_sprite",
                        trackerTask.TaskId,
                        _backendTaskId,
                        friendlyError.TechnicalMessage,
                        new JObject
                        {
                            ["session_id"] = _sessionId,
                            ["generator_id"] = trackerTask.GeneratorId ?? "",
                            ["prompt"] = trackerTask.Prompt ?? ""
                        });
                }
            }
        }

        public string GetAssetSavePath(PipelineMediaType _type, ModelGeneratorBase generator) =>
            _type == PipelineMediaType.Texture ? _placeholderPath : null;

        public void OnAssetSaved(PipelineMediaType _type, string savePath, ModelGeneratorBase generator)
        {
            if (_type != PipelineMediaType.Texture) return;

            TJLog.Log($"[GenerateSpriteTool] Recovered sprite saved: {savePath}");

            GeneratedTextureImportUtils.ConfigureImportedTexture(
                savePath, TextureImporterType.Sprite, alphaIsTransparency: true);

            TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(savePath));
            TJGeneratorsGenerationLabel.EnableSessionLabel(TJGeneratorsAssetReference.FromPath(savePath), _sessionId);

            var trackerTask = SpriteTaskTracker.GetTaskByBackendId(_backendTaskId);
            if (trackerTask == null) return;

            string previewUrl = generator?.CurrentPreviewUrl;
            SpriteTaskTracker.MarkTaskCompleted(trackerTask.TaskId, savePath, previewUrl);
            var t = SpriteTaskTracker.GetTask(trackerTask.TaskId);
            GenerationNotifier.NotifyCompleted(
                "generate_sprite",
                trackerTask.TaskId,
                _backendTaskId,
                new JObject
                {
                    ["session_id"] = _sessionId,
                    ["generator_id"] = t?.GeneratorId ?? "",
                    ["prompt"] = t?.Prompt ?? "",
                    ["image_path"] = savePath ?? "",
                    ["preview_url"] = previewUrl ?? "",
                    ["progress"] = 100,
                    ["start_time"] = t?.StartTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                    ["end_time"] = t?.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                    ["duration_seconds"] = (t != null && t.EndTime.HasValue) ? (int)(t.EndTime.Value - t.StartTime).TotalSeconds : 0
                });
        }
    }

#endif
}
