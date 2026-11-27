using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Codely.Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;

#if UNITY_EDITOR
using TJGenerators;
using TJGenerators.Generators;
using TJGenerators.Config;
using TJGenerators.Pipeline;
using TJGenerators.PostProcessing;
using TJGenerators.Utils;
using Unity.EditorCoroutines.Editor;
#endif

namespace UnityTcp.Editor.Tools
{
    // New UI kits generate and split in MCP. Keep local slicing and legacy task queries.
    public static class GenerateGameUiKitTool
    {
        // IDs used to list kits that were created by older versions.
        private const string GeneratorId = "frontier-game-design";
        private const string SeedreamProGeneratorId = "huoshan_seedream_pro_image";

        [ExecuteCustomTool.CustomTool("slice_image",
            "Slice a sprite sheet / cutout sheet into individual sprite PNGs using CV connected-component detection. " +
            "Automatically detects background (transparent or solid color like magenta), finds connected regions via 8-connected BFS, " +
            "applies feather + color decontamination to remove background fringe, and exports each element as a separate PNG. " +
            "Also used as the fallback for merged layer-decomposition layers (a layer PNG still containing multiple elements): use background_mode 'transparent'. " +
            "Parameters: image_path (required, local asset path), " +
            "background_mode (optional 'auto'|'transparent'|'solid_color', default 'auto' — for magenta cutout sheets use 'solid_color'), " +
            "color_tolerance (optional 0-100, default 15, higher = more pixels treated as background), " +
            "alpha_threshold (optional 0-1, default 0.1, used when background_mode is 'transparent'), " +
            "min_region_pixels (optional, default 100, regions smaller than this are ignored), " +
            "padding (optional, default 2, extra pixels around each sliced element), " +
            "set_as_sprite (optional, default true, auto-set TextureImporterType.Sprite on output). " +
            "Returns: sliced_count, output_directory, sliced_asset_paths array. " +
            "This is a synchronous operation — no task_id or polling needed.")]
        public static object SliceImage(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                TJLog.Log($"[GenerateGameUiKitTool] SliceImage parameters: {parameters}");

                string imagePath = parameters["image_path"]?.ToString();
                if (string.IsNullOrEmpty(imagePath))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "'image_path' parameter is required" }
                    };
                }

                // Load readable texture
                var readableTex = SpriteSequencePostProcess.LoadReadableTextureFromAssetPath(imagePath);
                if (readableTex == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"Failed to load readable texture from: {imagePath}. Ensure the file exists and is a valid image." }
                    };
                }

                try
                {
                    // Parse parameters
                    string bgModeStr = parameters["background_mode"]?.ToString() ?? "auto";
                    ImageSlicePostProcess.BackgroundMode bgMode;
                    switch (bgModeStr.ToLowerInvariant())
                    {
                        case "transparent":
                            bgMode = ImageSlicePostProcess.BackgroundMode.Transparent;
                            break;
                        case "solid_color":
                        case "solidcolor":
                            bgMode = ImageSlicePostProcess.BackgroundMode.SolidColor;
                            break;
                        default:
                            bgMode = ImageSlicePostProcess.BackgroundMode.Auto;
                            break;
                    }

                    float alphaThreshold = parameters["alpha_threshold"] != null
                        ? (float)parameters["alpha_threshold"].Value<double>()
                        : 0.1f;
                    float colorTolerance = parameters["color_tolerance"] != null
                        ? (float)parameters["color_tolerance"].Value<double>()
                        : 15f;
                    int minRegionPixels = parameters["min_region_pixels"]?.Value<int>() ?? 100;
                    int padding = parameters["padding"]?.Value<int>() ?? 2;
                    bool setAsSprite = parameters["set_as_sprite"]?.Value<bool>() ?? true;

                    TJLog.Log($"[GenerateGameUiKitTool] SliceImage: bgMode={bgMode}, alphaThreshold={alphaThreshold}, " +
                        $"colorTolerance={colorTolerance}, minRegionPixels={minRegionPixels}, padding={padding}, setAsSprite={setAsSprite}");

                    var result = ImageSlicePostProcess.Export(
                        readableTex,
                        imagePath,
                        bgMode,
                        alphaThreshold,
                        colorTolerance,
                        minRegionPixels,
                        padding,
                        setAsSprite);

                    if (result.ExportedCount == 0)
                    {
                        return new Dictionary<string, object>
                        {
                            { "success", false },
                            { "message", "No regions detected. Try adjusting background_mode, color_tolerance, or min_region_pixels." }
                        };
                    }

                    TJLog.Log($"[GenerateGameUiKitTool] SliceImage completed: {result.ExportedCount} sprites exported to {result.OutputDirectory}");

                    return new Dictionary<string, object>
                    {
                        { "success", true },
                        { "sliced_count", result.ExportedCount },
                        { "output_directory", result.OutputDirectory },
                        { "sliced_asset_paths", result.AssetPaths },
                        { "message", $"Successfully sliced {result.ExportedCount} sprite(s) into {result.OutputDirectory}" }
                    };
                }
                finally
                {
                    // Destroy the runtime texture if it's not an asset
                    if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(readableTex)))
                        UnityEngine.Object.DestroyImmediate(readableTex);
                }
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateGameUiKitTool] SliceImage error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error slicing image: {e.Message}" }
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

        public static object QueryGameUiKitStatus(JObject parameters)
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

                var task = ImageTaskTracker.GetTask(taskId);
                if (task == null)
                {
                    // seedream Step 2 任务在 layers tracker 里
                    var layersTask = ImageLayersTaskTracker.GetTask(taskId);
                    if (layersTask == null)
                    {
                        return new Dictionary<string, object>
                        {
                            { "success", false },
                            { "message", $"Task '{taskId}' not found. It may have been completed and cleaned up." }
                        };
                    }
                    return BuildLayersTaskResult(layersTask);
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

                if (!string.IsNullOrEmpty(task.ResultPath))
                    result["image_path"] = task.ResultPath;

                result["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);

                if (!string.IsNullOrEmpty(task.ErrorMessage))
                    result["error"] = task.ErrorMessage;

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
                TJLog.LogError($"[GenerateGameUiKitTool] Query error: {e}");
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

        public static object ListGameUiKitTasks(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                // 单图步（Step 1 两 provider / frontier Step 2）在 ImageTaskTracker；
                // seedream Step 2（图层拆分）在 ImageLayersTaskTracker。两个 store 都要查。
                // 注意：两个 tracker 分别与 generate_image / generate_image_layers 共享，
                // 无法按工具名区分，只能按 generator id 过滤。
                ImageTaskTracker.CleanupCompletedTasks();
                ImageLayersTaskTracker.CleanupCompletedTasks();
                var taskList = new List<Dictionary<string, object>>();

                foreach (var task in ImageTaskTracker.GetAllTasks())
                {
                    if (task.GeneratorId != GeneratorId && task.GeneratorId != SeedreamProGeneratorId)
                        continue;
                    taskList.Add(BuildSingleImageTaskData(task));
                }

                foreach (var task in ImageLayersTaskTracker.GetAllTasks())
                {
                    if (task.GeneratorId != GenerateImageLayersTool.SeedreamGeneratorId)
                        continue;
                    taskList.Add(BuildLayersTaskData(task));
                }

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "count", taskList.Count },
                    { "tasks", taskList },
                    { "note", "game_ui_kit tasks share trackers with generate_image (frontier/seedream_pro screenshot steps) " +
                              "and generate_image_layers (seedream layer step). Tasks are not separately tagged by tool name." }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateGameUiKitTool] List error: {e}");
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
        /// <summary>layers tracker 任务的查询结果（seedream Step 2）</summary>
        internal static Dictionary<string, object> BuildLayersTaskResult(ImageLayersTaskTracker.ImageLayersTaskInfo task)
        {
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

            if (!string.IsNullOrEmpty(task.Layer0Path))
            {
                // 兼容字段：底图路径（注意 layer 0 是底图，不是元素精灵）
                result["image_path"] = task.Layer0Path;
                result["layer_0_path"] = task.Layer0Path;
            }
            if (!string.IsNullOrEmpty(task.LayersFolder))
                result["layers_folder"] = task.LayersFolder;
            result["layer_count"] = task.LayerCount;

            result["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);

            if (!string.IsNullOrEmpty(task.ErrorMessage))
                result["error"] = task.ErrorMessage;

            if (task.EndTime.HasValue)
            {
                result["end_time"] = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                result["duration_seconds"] = (int)(task.EndTime.Value - task.StartTime).TotalSeconds;
            }

            if (task.Status == "generating" || task.Status == "recovering")
            {
                if (!string.IsNullOrEmpty(task.PlaceholderPath))
                    result["placeholder_path"] = task.PlaceholderPath;
            }

            if (task.Status == "completed" && !string.IsNullOrEmpty(task.Layer0Path))
            {
                var layerPaths = GenerateImageLayersTool.CollectLayerPaths(task.Layer0Path, task.LayerCount);
                result["layer_paths"] = layerPaths;
                result["layers_found"] = layerPaths.Count;
            }

            return result;
        }

        private static Dictionary<string, object> BuildSingleImageTaskData(ImageTaskTracker.ImageTaskInfo task)
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

            if (!string.IsNullOrEmpty(task.ResultPath))
                taskData["image_path"] = task.ResultPath;

            taskData["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);

            if (!string.IsNullOrEmpty(task.ErrorMessage))
                taskData["error"] = task.ErrorMessage;

            if (task.EndTime.HasValue)
                taskData["end_time"] = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");

            return taskData;
        }

        private static Dictionary<string, object> BuildLayersTaskData(ImageLayersTaskTracker.ImageLayersTaskInfo task)
        {
            var taskData = new Dictionary<string, object>
            {
                { "task_id", task.TaskId },
                { "generator_id", task.GeneratorId },
                { "status", task.Status },
                { "progress", task.Progress },
                { "prompt", task.Prompt },
                { "layer_count", task.LayerCount },
                { "start_time", task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
            };

            if (!string.IsNullOrEmpty(task.Layer0Path))
                taskData["layer_0_path"] = task.Layer0Path;
            if (!string.IsNullOrEmpty(task.LayersFolder))
                taskData["layers_folder"] = task.LayersFolder;

            taskData["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);

            if (!string.IsNullOrEmpty(task.ErrorMessage))
                taskData["error"] = task.ErrorMessage;

            if (task.EndTime.HasValue)
                taskData["end_time"] = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");

            return taskData;
        }

#endif
    }
}
