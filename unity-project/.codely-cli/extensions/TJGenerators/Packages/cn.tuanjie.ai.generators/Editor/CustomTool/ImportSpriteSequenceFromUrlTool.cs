using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Codely.Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEditor;

#if UNITY_EDITOR
using TJGenerators;
using TJGenerators.PostProcessing;
using TJGenerators.Utils;
using Unity.EditorCoroutines.Editor;
#endif

namespace UnityTcp.Editor.Tools
{
    /// <summary>
    /// Sprite-sequence import task tracker (persisted; domain-reload safe — CDN URL permanent, sheet path deterministic).
    /// </summary>
    public static class ImportSpriteSequenceTaskTracker
    {
#if UNITY_EDITOR
        [Serializable]
        private class PersistedTask
        {
            public string taskId;
            public string sessionId;
            public string sheetUrl;
            public string sheetPath;
            public string outputFolder;
            public string animationClipPath;
            public string prompt;
            public string status;
            public int progress;
            public int cols;
            public int rows;
            public float fps;
            public bool loop;
            public float chromaTolerance;
            public float chromaFeather;
            public bool preserveExistingAlpha;
            public int frameCount;
            public string historyTaskId;
            public string errorMessage;
            public long startTimeTicks;
            public long endTimeTicks;
        }

        public class ImportSpriteSequenceTaskInfo : IGenerationTaskInfo
        {
            public string TaskId { get; set; }
            public string SessionId { get; set; }
            public string SheetUrl { get; set; }
            public string SheetPath { get; set; }
            public string OutputFolder { get; set; }
            public string AnimationClipPath { get; set; }
            public string Prompt { get; set; }
            public string Status { get; set; }
            public int Progress { get; set; }
            public int Cols { get; set; } = 4;
            public int Rows { get; set; } = 4;
            public float Fps { get; set; } = 12f;
            public bool Loop { get; set; } = true;
            public float ChromaTolerance { get; set; } = SpriteSequencePostProcess.DefaultChromaTolerance;
            public float ChromaFeather { get; set; } = SpriteSequencePostProcess.DefaultChromaFeather;
            public bool PreserveExistingAlpha { get; set; } = true;
            public int FrameCount { get; set; }
            public string HistoryTaskId { get; set; }
            public string ErrorMessage { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public string BackendTaskId { get; set; }
            public string PreviewUrl { get; set; }
        }

        private static readonly GenerationTaskTrackerStore<ImportSpriteSequenceTaskInfo, PersistedTask> Store =
            new GenerationTaskTrackerStore<ImportSpriteSequenceTaskInfo, PersistedTask>(
                "TJGen_ImportSpriteSequence",
                BuildPersisted,
                FromPersisted);

        private static PersistedTask BuildPersisted(ImportSpriteSequenceTaskInfo info) => new PersistedTask
        {
            taskId = info.TaskId ?? "",
            sessionId = info.SessionId ?? "",
            sheetUrl = info.SheetUrl ?? "",
            sheetPath = info.SheetPath ?? "",
            outputFolder = info.OutputFolder ?? "",
            animationClipPath = info.AnimationClipPath ?? "",
            prompt = info.Prompt ?? "",
            status = info.Status ?? "",
            progress = info.Progress,
            cols = info.Cols,
            rows = info.Rows,
            fps = info.Fps,
            loop = info.Loop,
            chromaTolerance = info.ChromaTolerance,
            chromaFeather = info.ChromaFeather,
            preserveExistingAlpha = info.PreserveExistingAlpha,
            frameCount = info.FrameCount,
            historyTaskId = info.HistoryTaskId ?? "",
            errorMessage = info.ErrorMessage ?? "",
            startTimeTicks = info.StartTime.Ticks,
            endTimeTicks = info.EndTime?.Ticks ?? 0
        };

        private static ImportSpriteSequenceTaskInfo FromPersisted(PersistedTask p) => new ImportSpriteSequenceTaskInfo
        {
            TaskId = p.taskId,
            SessionId = p.sessionId,
            SheetUrl = p.sheetUrl,
            SheetPath = p.sheetPath,
            OutputFolder = p.outputFolder,
            AnimationClipPath = p.animationClipPath,
            Prompt = p.prompt,
            Status = p.status,
            Progress = p.progress,
            Cols = p.cols > 0 ? p.cols : 4,
            Rows = p.rows > 0 ? p.rows : 4,
            Fps = p.fps > 0 ? p.fps : 12f,
            Loop = p.loop,
            ChromaTolerance = p.chromaTolerance > 0 ? p.chromaTolerance : SpriteSequencePostProcess.DefaultChromaTolerance,
            ChromaFeather = p.chromaFeather > 0 ? p.chromaFeather : SpriteSequencePostProcess.DefaultChromaFeather,
            PreserveExistingAlpha = p.preserveExistingAlpha,
            FrameCount = p.frameCount,
            HistoryTaskId = p.historyTaskId,
            ErrorMessage = p.errorMessage,
            StartTime = new DateTime(p.startTimeTicks),
            EndTime = p.endTimeTicks > 0 ? (DateTime?)new DateTime(p.endTimeTicks) : null,
            BackendTaskId = "",
            PreviewUrl = p.sheetUrl
        };

        internal static void ApplyTaskUpdate(ImportSpriteSequenceTaskInfo task, Action<ImportSpriteSequenceTaskInfo> mutate) =>
            Store.ApplyTaskUpdate(task, mutate);

        internal static void RegisterTask(ImportSpriteSequenceTaskInfo task) =>
            Store.RegisterTask(task.TaskId, task);

        internal static string AllocateTaskId() => Store.AllocateTaskId("import_sprite_seq");

        public static ImportSpriteSequenceTaskInfo GetTask(string taskId) => Store.GetTask(taskId);
        public static List<ImportSpriteSequenceTaskInfo> GetAllTasks() => Store.GetAllTasks();
#endif
    }

    /// <summary>
    /// Replaces Unity-side sprite-sequence generation CustomTools for the agent path:
    /// MCP generate_sprite_animation + check_task yields a permanent 4×4 transparent PNG sheet URL;
    /// this tool downloads the sheet, runs CutoutAndSlice (preserveExistingAlpha), and produces
    /// frame Sprites + AnimationClip under Assets/TJGenerators/History/.
    /// Editor windows / GenerationService keep their own pipelines and are unaffected.
    /// </summary>
    public static class ImportSpriteSequenceFromUrlTool
    {
        private const string ToolName = "import_sprite_sequence_from_url";

        [ExecuteCustomTool.CustomTool(ToolName,
            "Import an already-generated 4×4 (or custom grid) sprite-sheet PNG from a permanent CDN URL into Unity: " +
            "download → green-screen cutout / preserve native alpha → fixed-grid slice → AnimationClip. " +
            "URLs come from MCP generate_sprite_animation → check_task (native transparent PNG sheet), NOT from a local path. " +
            "Key parameters: sheet_url or image_url (required), fps (default 12), loop (default true), " +
            "cols/rows (default 4/4 for MCP flare sheets), preserve_existing_alpha (default true — keep PNG alpha from Flare), " +
            "chroma_tolerance/chroma_feather (used only when preserve_existing_alpha is false or the sheet has no alpha), " +
            "prompt (optional History text), session_id, output_path (optional sheet file path; default History/ImportSpriteSequence/<url-hash>.png). " +
            "Supported extensions: .png / .jpg / .jpeg. No placeholder: wait for animation_clip_path before scene placement. " +
            "RELIABLE COMPLETION FLOW: after submit, run a foreground shell wait of at most 30 seconds in this subagent, then " +
            "call query_local_task ONCE with task_id. If still 'importing'/'postprocessing', wait again and query once more. " +
            "Domain reload safe: in-flight imports are re-launched (URL permanent, sheet path deterministic).")]
        public static object ImportSpriteSequenceFromUrl(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                TJLog.Log($"[ImportSpriteSequenceFromUrlTool] Import with parameters: {parameters}");

                if (TJGeneratorsPlayModeGuard.IsActive)
                    return Fail(TJGeneratorsPlayModeGuard.Message);

                string sheetUrl = parameters["sheet_url"]?.ToString();
                if (string.IsNullOrWhiteSpace(sheetUrl))
                    sheetUrl = parameters["image_url"]?.ToString();
                if (string.IsNullOrWhiteSpace(sheetUrl))
                    return Fail("'sheet_url' or 'image_url' is required. Get it from MCP: " +
                                "generate_sprite_animation → check_task → image URL (.png).");

                sheetUrl = sheetUrl.Trim();
                string ext;
                try
                {
                    ext = Path.GetExtension(new Uri(sheetUrl).AbsolutePath)?.ToLowerInvariant();
                }
                catch (Exception)
                {
                    return Fail($"Invalid sheet_url: {sheetUrl}");
                }
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg")
                    return Fail($"Unsupported sheet URL extension '{ext}'. Supported: .png / .jpg / .jpeg. " +
                                $"MCP generate_sprite_animation always returns PNG. URL: {sheetUrl}");

                float fps = 12f;
                if (parameters["fps"] != null && (!float.TryParse(parameters["fps"].ToString(), out fps) || fps < 1f || fps > 60f))
                    return Fail("'fps' must be a number between 1 and 60.");

                bool loop = true;
                if (parameters["loop"] != null && !bool.TryParse(parameters["loop"].ToString(), out loop))
                    return Fail("'loop' must be a boolean.");

                int cols = 4;
                int rows = 4;
                if (parameters["cols"] != null && (!int.TryParse(parameters["cols"].ToString(), out cols) || cols < 1 || cols > 32))
                    return Fail("'cols' must be an integer between 1 and 32.");
                if (parameters["rows"] != null && (!int.TryParse(parameters["rows"].ToString(), out rows) || rows < 1 || rows > 32))
                    return Fail("'rows' must be an integer between 1 and 32.");

                float chromaTolerance = SpriteSequencePostProcess.DefaultChromaTolerance;
                float chromaFeather = SpriteSequencePostProcess.DefaultChromaFeather;
                if (parameters["chroma_tolerance"] != null &&
                    (!float.TryParse(parameters["chroma_tolerance"].ToString(), out chromaTolerance) || chromaTolerance < 0f))
                    return Fail("'chroma_tolerance' must be a non-negative number.");
                if (parameters["chroma_feather"] != null &&
                    (!float.TryParse(parameters["chroma_feather"].ToString(), out chromaFeather) || chromaFeather < 0f))
                    return Fail("'chroma_feather' must be a non-negative number.");

                bool preserveExistingAlpha = true;
                if (parameters["preserve_existing_alpha"] != null &&
                    !bool.TryParse(parameters["preserve_existing_alpha"].ToString(), out preserveExistingAlpha))
                    return Fail("'preserve_existing_alpha' must be a boolean.");

                string prompt = parameters["prompt"]?.ToString() ?? "";
                string sessionId = parameters["session_id"]?.ToString() ?? "";
                string outputPath = parameters["output_path"]?.ToString();

                string sheetPath = string.IsNullOrEmpty(outputPath)
                    ? "Assets/TJGenerators/History/ImportSpriteSequence/" +
                      Import3DModelFromUrlTool.BuildUrlHashFileName(sheetUrl, ext)
                    : outputPath;
                if (!PathUtils.TryNormalizeOutputAssetPath(sheetPath, out sheetPath, out string pathError))
                    return Fail(pathError);
                if (string.IsNullOrEmpty(Path.GetExtension(sheetPath)))
                    sheetPath += ext;
                if (!string.Equals(Path.GetExtension(sheetPath), ext, StringComparison.OrdinalIgnoreCase))
                    return Fail("Sheet output_path must have the source image extension.");

                EnsureAssetFolder(Path.GetDirectoryName(sheetPath));
                if (!File.Exists(PathUtils.ToAbsoluteAssetPath(sheetPath)) &&
                    !WritePlaceholderSheet(sheetPath))
                    return Fail($"Failed to create placeholder sheet at: {sheetPath}");
                AssetDatabase.ImportAsset(sheetPath, ImportAssetOptions.ForceUpdate);
                TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(sheetPath));
                TJGeneratorsGenerationLabel.EnableSessionLabel(TJGeneratorsAssetReference.FromPath(sheetPath), sessionId);

                string historyTaskId = TJGeneratorsHistoryManager.AddGeneratingPlaceholder(
                    prompt ?? "", "", ToolName, true,
                    CustomToolHistoryBindings.HistoryGuidFromPlaceholderAssetPath(sheetPath),
                    prompt, sessionId);

                var task = new ImportSpriteSequenceTaskTracker.ImportSpriteSequenceTaskInfo
                {
                    TaskId = ImportSpriteSequenceTaskTracker.AllocateTaskId(),
                    SessionId = sessionId,
                    SheetUrl = sheetUrl,
                    SheetPath = sheetPath,
                    Prompt = prompt,
                    Status = "importing",
                    Progress = 0,
                    Cols = cols,
                    Rows = rows,
                    Fps = fps,
                    Loop = loop,
                    ChromaTolerance = chromaTolerance,
                    ChromaFeather = chromaFeather,
                    PreserveExistingAlpha = preserveExistingAlpha,
                    HistoryTaskId = historyTaskId,
                    PreviewUrl = sheetUrl,
                    StartTime = DateTime.Now
                };
                ImportSpriteSequenceTaskTracker.RegisterTask(task);
                LaunchImport(task);

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "submission_success", true },
                    { "task_id", task.TaskId },
                    { "status", "importing" },
                    { "sheet_path", sheetPath },
                    { "cols", cols },
                    { "rows", rows },
                    { "fps", fps },
                    { "loop", loop },
                    { "preserve_existing_alpha", preserveExistingAlpha },
                    { "estimated_wait_seconds", 30 },
                    { "notification_mode", "query_status" },
                    { "message",
                        "Import started (no scene placeholder — wait for animation_clip_path before placing). " +
                        "STEP 1: Wait in this subagent with run_shell_command(run_in_background=false), using Start-Sleep / sleep for at most 30 seconds. " +
                        "STEP 2: Do not call complete_task or return final text while the import is pending. " +
                        "STEP 3: After the foreground wait returns, call query_local_task ONCE with this task_id; " +
                        "if it still reports 'importing' or 'postprocessing', run another bounded foreground wait before querying again. " +
                        "*** Never wait indefinitely for a Unity-side notification; never loop query_local_task within one wake-up. ***" }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[ImportSpriteSequenceFromUrlTool] Error: {e}");
                return Fail($"Error: {e.Message}");
            }
#else
            return Fail("This tool only works in Unity Editor.");
#endif
        }

        public static object QueryImportStatus(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                string taskId = parameters["task_id"]?.ToString();
                if (string.IsNullOrEmpty(taskId))
                    return Fail("'task_id' parameter is required");

                var task = ImportSpriteSequenceTaskTracker.GetTask(taskId);
                if (task == null)
                    return Fail($"Task '{taskId}' not found. It may have been cleaned up or Unity was fully restarted.");

                var result = new Dictionary<string, object>
                {
                    { "success", true },
                    { "task_id", task.TaskId },
                    { "status", task.Status },
                    { "progress", task.Progress },
                    { "cols", task.Cols },
                    { "rows", task.Rows },
                    { "fps", task.Fps },
                    { "loop", task.Loop },
                    { "start_time", task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                if (!string.IsNullOrEmpty(task.SheetPath))
                    result["sheet_path"] = task.SheetPath;
                if (!string.IsNullOrEmpty(task.SheetUrl))
                    result["preview_url"] = task.SheetUrl;
                if (!string.IsNullOrEmpty(task.OutputFolder))
                    result["folder_path"] = task.OutputFolder;
                if (!string.IsNullOrEmpty(task.AnimationClipPath))
                {
                    result["animation_clip_path"] = task.AnimationClipPath;
                    result["animation_path"] = task.AnimationClipPath; // auto-compat alias
                }
                if (task.FrameCount > 0)
                    result["frame_count"] = task.FrameCount;
                if (!string.IsNullOrEmpty(task.ErrorMessage))
                    result["error"] = task.ErrorMessage;
                if (task.EndTime.HasValue)
                {
                    result["end_time"] = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    result["duration_seconds"] = (int)(task.EndTime.Value - task.StartTime).TotalSeconds;
                }

                if (task.Status == "recovering")
                    result["hint"] = "The import was interrupted by a domain reload and is being re-launched automatically. Wait in this subagent for up to 30 seconds, then check this same task again.";

                if (task.Status == "completed" && !string.IsNullOrEmpty(task.AnimationClipPath))
                    result["result_summary"] =
                        $"Import completed. AnimationClip: {task.AnimationClipPath}; frames folder: {task.OutputFolder}; frame_count={task.FrameCount}.";

                return result;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[ImportSpriteSequenceFromUrlTool] Query error: {e}");
                return Fail($"Error: {e.Message}");
            }
#else
            return Fail("This tool only works in Unity Editor.");
#endif
        }

#if UNITY_EDITOR
        internal static void LaunchImport(ImportSpriteSequenceTaskTracker.ImportSpriteSequenceTaskInfo task)
        {
            if (task == null || string.IsNullOrEmpty(task.SheetUrl)) return;
            EditorCoroutineUtility.StartCoroutineOwnerless(DownloadSliceAndBuild(task));
        }

        private static IEnumerator DownloadSliceAndBuild(ImportSpriteSequenceTaskTracker.ImportSpriteSequenceTaskInfo task)
        {
            using (var request = UnityWebRequest.Get(task.SheetUrl))
            {
                request.timeout = 300;
                yield return request.SendWebRequest();

                if (UnityWebRequestCompat.IsNotSuccess(request))
                {
                    FailTask(task, $"Download failed ({request.error}): {task.SheetUrl}");
                    yield break;
                }

                try
                {
                    string absPath = PathUtils.ToAbsoluteAssetPath(task.SheetPath);
                    string dir = Path.GetDirectoryName(absPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllBytes(absPath, request.downloadHandler.data);
                }
                catch (Exception e)
                {
                    FailTask(task, $"Save failed: {e.Message}");
                    yield break;
                }
            }

            AssetDatabase.ImportAsset(task.SheetPath, ImportAssetOptions.ForceUpdate);
            ImportSpriteSequenceTaskTracker.ApplyTaskUpdate(task, t =>
            {
                t.Status = "postprocessing";
                t.Progress = 60;
            });

            SpriteSequencePostProcess.SliceResult sliceResult;
            try
            {
                sliceResult = SpriteSequencePostProcess.CutoutAndSlice(
                    task.SheetPath,
                    task.ChromaTolerance,
                    task.ChromaFeather,
                    task.Cols,
                    task.Rows,
                    task.Fps,
                    task.Loop,
                    writeCutoutBackToAsset: false,
                    preserveExistingAlpha: task.PreserveExistingAlpha);
            }
            catch (Exception e)
            {
                FailTask(task, $"Post-process failed: {e.Message}");
                yield break;
            }

            if (string.IsNullOrEmpty(sliceResult.AnimationClipPath))
            {
                FailTask(task, "Post-process produced no AnimationClip.");
                yield break;
            }

            if (!string.IsNullOrEmpty(sliceResult.AnimationClipPath))
            {
                TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(sliceResult.AnimationClipPath));
                TJGeneratorsGenerationLabel.EnableSessionLabel(
                    TJGeneratorsAssetReference.FromPath(sliceResult.AnimationClipPath), task.SessionId ?? "");
            }

            if (!string.IsNullOrEmpty(sliceResult.OutputDirectory))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { sliceResult.OutputDirectory }))
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(assetPath));
                    TJGeneratorsGenerationLabel.EnableSessionLabel(
                        TJGeneratorsAssetReference.FromPath(assetPath), task.SessionId ?? "");
                }
            }

            ImportSpriteSequenceTaskTracker.ApplyTaskUpdate(task, t =>
            {
                t.Status = "completed";
                t.Progress = 100;
                t.OutputFolder = sliceResult.OutputDirectory ?? "";
                t.AnimationClipPath = sliceResult.AnimationClipPath ?? "";
                t.FrameCount = sliceResult.ExportedCount;
                t.EndTime = DateTime.Now;
                t.ErrorMessage = "";
            });

            if (!string.IsNullOrEmpty(task.HistoryTaskId))
                TJGeneratorsHistoryManager.CompletePlaceholder(
                    task.HistoryTaskId, sliceResult.AnimationClipPath, task.SheetUrl, null);

            var payload = new JObject
            {
                ["session_id"] = task.SessionId ?? "",
                ["prompt"] = task.Prompt ?? "",
                ["sheet_path"] = task.SheetPath ?? "",
                ["folder_path"] = task.OutputFolder ?? "",
                ["animation_clip_path"] = task.AnimationClipPath ?? "",
                ["animation_path"] = task.AnimationClipPath ?? "",
                ["frame_count"] = task.FrameCount,
                ["preview_url"] = task.SheetUrl ?? "",
                ["cols"] = task.Cols,
                ["rows"] = task.Rows,
                ["fps"] = task.Fps,
                ["loop"] = task.Loop,
                ["progress"] = 100,
                ["start_time"] = task.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                ["end_time"] = task.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                ["duration_seconds"] = task.EndTime.HasValue
                    ? (int)(task.EndTime.Value - task.StartTime).TotalSeconds
                    : 0
            };
            GenerationNotifier.NotifyCompleted(ToolName, task.TaskId, "", payload);
            TJLog.Log($"[ImportSpriteSequenceFromUrlTool] Completed: clip={task.AnimationClipPath}, frames={task.FrameCount}");
        }

        private static void FailTask(ImportSpriteSequenceTaskTracker.ImportSpriteSequenceTaskInfo task, string error)
        {
            ImportSpriteSequenceTaskTracker.ApplyTaskUpdate(task, t =>
            {
                t.Status = "failed";
                t.ErrorMessage = error;
                t.EndTime = DateTime.Now;
            });
            GenerationNotifier.NotifyFailed(ToolName, task.TaskId, "", error,
                new JObject { ["session_id"] = task.SessionId ?? "", ["prompt"] = task.Prompt ?? "" });
            TJLog.LogError($"[ImportSpriteSequenceFromUrlTool] {error}");
        }

        private static void EnsureAssetFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            string abs = PathUtils.ToAbsoluteAssetPath(folder);
            if (!string.IsNullOrEmpty(abs) && !Directory.Exists(abs))
                Directory.CreateDirectory(abs);
        }

        private static bool WritePlaceholderSheet(string assetPath)
        {
            try
            {
                string absPath = PathUtils.ToAbsoluteAssetPath(assetPath);
                if (string.IsNullOrEmpty(absPath)) return false;
                string dir = Path.GetDirectoryName(absPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                var pixels = new Color32[64];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(160, 160, 160, 255);
                tex.SetPixels32(pixels);
                tex.Apply();
                byte[] bytes = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);
                File.WriteAllBytes(absPath, bytes);
                return File.Exists(absPath);
            }
            catch (Exception e)
            {
                TJLog.LogError($"[ImportSpriteSequenceFromUrlTool] WritePlaceholderSheet failed: {e.Message}");
                return false;
            }
        }

        internal static Dictionary<string, object> Fail(string message)
        {
            return new Dictionary<string, object>
            {
                { "success", false },
                { "message", message }
            };
        }
#endif
    }

#if UNITY_EDITOR
    [InitializeOnLoad]
    public static class ImportSpriteSequenceDomainReloadRecovery
    {
        static ImportSpriteSequenceDomainReloadRecovery()
        {
            CustomToolDomainReloadRecovery.Schedule(ResumeInterruptedImports);
        }

        private static void ResumeInterruptedImports()
        {
            foreach (var task in ImportSpriteSequenceTaskTracker.GetAllTasks())
            {
                if (task == null) continue;
                if (task.Status != "importing" && task.Status != "recovering" && task.Status != "postprocessing")
                    continue;
                if (string.IsNullOrEmpty(task.SheetUrl)) continue;

                TJLog.Log($"[ImportSpriteSequenceDomainReloadRecovery] Resuming sprite-sequence import: {task.TaskId}");
                ImportSpriteSequenceTaskTracker.ApplyTaskUpdate(task, t => t.Status = "recovering");
                ImportSpriteSequenceFromUrlTool.LaunchImport(task);
            }
        }
    }
#endif
}
