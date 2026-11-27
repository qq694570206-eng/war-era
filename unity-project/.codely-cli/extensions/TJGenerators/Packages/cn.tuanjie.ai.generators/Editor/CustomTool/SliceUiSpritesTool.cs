using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Codely.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using TJGenerators;
using TJGenerators.Utils;

namespace UnityTcp.Editor.Tools
{
    /// <summary>
    /// Local measured UI slicing via the verified game-ui-slicing Python scripts
    /// (inspect / slice / qa / preview_dynamic / stage_upload). No MCP / generation backend.
    /// </summary>
    public static class SliceUiSpritesTool
    {
        private const string ToolName = "slice_ui_sprites";

        [ExecuteCustomTool.CustomTool(ToolName,
            "Measured game-UI slicing (local Python, no MCP): inspect / slice / qa / preview_dynamic / stage_upload. " +
            "Use for production widgets (nine-slice, safe text areas, multi-state alignment, HP track/fill/frame). " +
            "Not a drop-in replacement for slice_image (CV connected-components) or MCP generate_image_layers (AI layer extraction). " +
            "Required: command. " +
            "inspect: image_path, out_dir. " +
            "slice: image_path, regions_path, out_dir. " +
            "qa: slices_dir, layout_path, out_dir. " +
            "preview_dynamic: config_path, out_dir. " +
            "stage_upload: image_path, out_dir (hash-named copy for file_upload). " +
            "Paths may be Assets/... or absolute. Requires Python 3.10+ and Pillow (extension/skills/game_ui_slicing/requirements.txt). " +
            "Synchronous — no task_id / polling.")]
        public static object SliceUiSprites(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                if (TJGeneratorsPlayModeGuard.IsActive)
                    return Fail(TJGeneratorsPlayModeGuard.Message);

                string command = parameters?["command"]?.ToString()?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(command))
                    return Fail("'command' is required: inspect | slice | qa | preview_dynamic | stage_upload");

                string scriptsDir = ResolveScriptsDir();
                if (string.IsNullOrEmpty(scriptsDir))
                    return Fail("Cannot locate extension/skills/game_ui_slicing/scripts under the TJGenerators package.");

                string python;
                string pythonPrefixArgs;
                if (!TryResolvePython(out python, out pythonPrefixArgs))
                    return Fail("Python 3.10+ not found. Install Python and Pillow (pip install -r extension/skills/game_ui_slicing/requirements.txt).");

                string script;
                var argv = new List<string>();

                switch (command)
                {
                    case "inspect":
                    {
                        script = Path.Combine(scriptsDir, "ui_sprites.py");
                        string imagePath = RequireExistingFile(parameters, "image_path");
                        if (imagePath == null) return Fail("'image_path' is required and must exist");
                        string outDir = RequireOutDir(parameters);
                        if (outDir == null) return Fail("'out_dir' is required");
                        argv.Add("inspect");
                        argv.Add(imagePath);
                        argv.Add("--out");
                        argv.Add(outDir);
                        if (parameters["grid"] != null)
                        {
                            argv.Add("--grid");
                            argv.Add(parameters["grid"].ToString());
                        }
                        break;
                    }
                    case "slice":
                    {
                        script = Path.Combine(scriptsDir, "ui_sprites.py");
                        string imagePath = RequireExistingFile(parameters, "image_path");
                        string regionsPath = RequireExistingFile(parameters, "regions_path");
                        if (imagePath == null) return Fail("'image_path' is required and must exist");
                        if (regionsPath == null) return Fail("'regions_path' is required and must exist");
                        string outDir = RequireOutDir(parameters);
                        if (outDir == null) return Fail("'out_dir' is required");
                        argv.Add("slice");
                        argv.Add(imagePath);
                        argv.Add(regionsPath);
                        argv.Add("--out");
                        argv.Add(outDir);
                        break;
                    }
                    case "qa":
                    {
                        script = Path.Combine(scriptsDir, "ui_sprites.py");
                        string slicesDir = RequireExistingDir(parameters, "slices_dir");
                        string layoutPath = RequireExistingFile(parameters, "layout_path");
                        if (slicesDir == null) return Fail("'slices_dir' is required and must exist");
                        if (layoutPath == null) return Fail("'layout_path' is required and must exist");
                        string outDir = RequireOutDir(parameters);
                        if (outDir == null) return Fail("'out_dir' is required");
                        argv.Add("qa");
                        argv.Add(slicesDir);
                        argv.Add(layoutPath);
                        argv.Add("--out");
                        argv.Add(outDir);
                        break;
                    }
                    case "stage_upload":
                    case "stage-upload":
                    {
                        script = Path.Combine(scriptsDir, "ui_sprites.py");
                        string imagePath = RequireExistingFile(parameters, "image_path");
                        if (imagePath == null) return Fail("'image_path' is required and must exist");
                        string outDir = RequireOutDir(parameters);
                        if (outDir == null) return Fail("'out_dir' is required");
                        argv.Add("stage-upload");
                        argv.Add(imagePath);
                        argv.Add("--out");
                        argv.Add(outDir);
                        break;
                    }
                    case "preview_dynamic":
                    case "preview-dynamic":
                    {
                        script = Path.Combine(scriptsDir, "preview_dynamic_ui.py");
                        string configPath = RequireExistingFile(parameters, "config_path");
                        if (configPath == null) return Fail("'config_path' is required and must exist");
                        string outDir = RequireOutDir(parameters);
                        if (outDir == null) return Fail("'out_dir' is required");
                        argv.Add(configPath);
                        argv.Add("--out");
                        argv.Add(outDir);
                        break;
                    }
                    default:
                        return Fail($"Unknown command '{command}'. Use inspect | slice | qa | preview_dynamic | stage_upload");
                }

                if (!File.Exists(script))
                    return Fail($"Script not found: {script}");

                var run = RunPython(python, pythonPrefixArgs, script, argv);
                if (run.ExitCode != 0)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "command", command },
                        { "exit_code", run.ExitCode },
                        { "message", string.IsNullOrEmpty(run.StdErr) ? run.StdOut : run.StdErr },
                        { "stdout", run.StdOut ?? "" },
                        { "stderr", run.StdErr ?? "" }
                    };
                }

                string primaryOut = parameters["out_dir"]?.ToString();
                string absOut = ToAbsolutePath(primaryOut);
                ImportUnderAssetsIfNeeded(absOut, setAsSprite: command == "slice");

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "command", command },
                    { "exit_code", 0 },
                    { "out_dir", ToAssetsRelativeOrAbsolute(absOut) },
                    { "stdout", run.StdOut ?? "" },
                    { "stderr", run.StdErr ?? "" },
                    { "message", $"slice_ui_sprites '{command}' completed." }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[SliceUiSpritesTool] {e}");
                return Fail(e.Message);
            }
#else
            return Fail("This tool only works in Unity Editor.");
#endif
        }

#if UNITY_EDITOR
        private static string ResolveScriptsDir()
        {
            string root = PathUtils.TryGetTjGeneratorsPackageRoot();
            if (!string.IsNullOrEmpty(root))
            {
                string bundled = Path.Combine(root, "extension", "skills", "game_ui_slicing", "scripts");
                if (Directory.Exists(bundled)) return bundled;
            }
            string project = Path.GetDirectoryName(Application.dataPath);
            string installed = Path.Combine(project, ".codely-cli", "extensions", "TJGenerators", "skills", "game_ui_slicing", "scripts");
            return Directory.Exists(installed) ? installed : null;
        }

        private static bool TryResolvePython(out string exe, out string prefixArgs)
        {
            exe = null;
            prefixArgs = "";
            // Prefer Windows py launcher
            if (TryRunVersion("py", "-3"))
            {
                exe = "py";
                prefixArgs = "-3";
                return true;
            }
            if (TryRunVersion("python", null)) { exe = "python"; return true; }
            if (TryRunVersion("python3", null)) { exe = "python3"; return true; }
            return false;
        }

        private static bool TryRunVersion(string exe, string prefix)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = string.IsNullOrEmpty(prefix) ? "--version" : prefix + " --version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    p.WaitForExit(5000);
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private struct ProcResult
        {
            public int ExitCode;
            public string StdOut;
            public string StdErr;
        }

        private static ProcResult RunPython(string exe, string prefixArgs, string script, List<string> argv)
        {
            var args = new StringBuilder();
            if (!string.IsNullOrEmpty(prefixArgs))
            {
                args.Append(prefixArgs);
                args.Append(' ');
            }
            args.Append('"').Append(script).Append('"');
            foreach (string a in argv)
            {
                args.Append(' ');
                args.Append('"').Append(a.Replace("\"", "\\\"")).Append('"');
            }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args.ToString(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            TJLog.Log($"[SliceUiSpritesTool] {exe} {psi.Arguments}");

            using (var p = Process.Start(psi))
            {
                if (p == null)
                    throw new InvalidOperationException("Failed to start Python process.");

                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                p.WaitForExit();
                return new ProcResult { ExitCode = p.ExitCode, StdOut = stdout.Result, StdErr = stderr.Result };
            }
        }

        private static string RequireOutDir(JObject parameters)
        {
            string outDir = parameters["out_dir"]?.ToString();
            if (string.IsNullOrWhiteSpace(outDir)) return null;
            string abs = ToAbsolutePath(outDir.Trim());
            string parent = Path.GetDirectoryName(abs);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                Directory.CreateDirectory(parent);
            return abs;
        }

        private static string RequireExistingFile(JObject parameters, string key)
        {
            string p = parameters[key]?.ToString();
            if (string.IsNullOrWhiteSpace(p)) return null;
            string abs = ToAbsolutePath(p.Trim());
            return File.Exists(abs) ? abs : null;
        }

        private static string RequireExistingDir(JObject parameters, string key)
        {
            string p = parameters[key]?.ToString();
            if (string.IsNullOrWhiteSpace(p)) return null;
            string abs = ToAbsolutePath(p.Trim());
            return Directory.Exists(abs) ? abs : null;
        }

        private static string ToAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            path = path.Replace('\\', '/');
            if (Path.IsPathRooted(path))
                return Path.GetFullPath(path);
            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return PathUtils.ToAbsoluteAssetPath(path);
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
        }

        private static string ToAssetsRelativeOrAbsolute(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return absolutePath;
            string rel = PathUtils.TryGetAssetsRelativePathFromAbsolute(absolutePath);
            return string.IsNullOrEmpty(rel) ? absolutePath.Replace('\\', '/') : rel;
        }

        private static void ImportUnderAssetsIfNeeded(string absoluteOutDir, bool setAsSprite)
        {
            if (string.IsNullOrEmpty(absoluteOutDir) || !Directory.Exists(absoluteOutDir))
                return;

            string assetsRel = PathUtils.TryGetAssetsRelativePathFromAbsolute(absoluteOutDir);
            // out_dir may not exist as a file; map folder under Assets
            if (string.IsNullOrEmpty(assetsRel))
            {
                string dataPath = Path.GetFullPath(Application.dataPath);
                string full = Path.GetFullPath(absoluteOutDir);
                if (!full.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                    return;
                assetsRel = "Assets" + full.Substring(dataPath.Length).Replace('\\', '/');
            }

            AssetDatabase.Refresh();
            if (!setAsSprite) return;

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { assetsRel });
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null) continue;
                bool dirty = false;
                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    dirty = true;
                }
                if (!importer.alphaIsTransparency)
                {
                    importer.alphaIsTransparency = true;
                    dirty = true;
                }
                if (dirty)
                    importer.SaveAndReimport();
                TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(assetPath));
            }
        }

        private static Dictionary<string, object> Fail(string message)
        {
            return new Dictionary<string, object>
            {
                { "success", false },
                { "message", message }
            };
        }
#endif
    }
}
