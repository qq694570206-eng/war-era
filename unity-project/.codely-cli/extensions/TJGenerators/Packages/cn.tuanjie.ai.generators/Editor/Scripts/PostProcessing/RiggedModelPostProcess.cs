#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using TJGenerators.Utils;

namespace TJGenerators.PostProcessing
{
    public static class RiggedModelPostProcess
    {
        public static void SetupRiggedCharacterImport(string assetPath)
        {
            ModelImporter modelImporter = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (modelImporter != null)
            {
                modelImporter.animationType = ModelImporterAnimationType.Human;
                modelImporter.isReadable = true;

                var embeddedClips = GetValidDefaultClipAnimations(modelImporter);
                if (embeddedClips.Length > 0)
                {
                    modelImporter.importAnimation = true;
                    foreach (var clip in embeddedClips)
                    {
                        clip.loopTime = InferDefaultLoop(clip.name);
                        ConfigureHumanoidRootMotionGrounding(clip);
                    }
                    modelImporter.clipAnimations = embeddedClips;
                    modelImporter.SaveAndReimport();
                    TJLog.Log(
                        $"[RiggedModelPostProcess] 绑骨模型 Humanoid 导入（保留 {embeddedClips.Length} 个内嵌动画剪辑）: {assetPath}"
                    );
                }
                else
                {
                    modelImporter.importAnimation = false;
                    modelImporter.SaveAndReimport();
                    TJLog.Log($"[RiggedModelPostProcess] 绑骨模型 Humanoid 导入（不内嵌动画剪辑）: {assetPath}");
                }
            }
        }

        private static void ConfigureHumanoidRootMotionGrounding(ModelImporterClipAnimation clip)
        {
            if (clip == null) return;
            clip.lockRootHeightY = true;
            clip.keepOriginalPositionY = false;
            clip.heightFromFeet = true;
            clip.lockRootRotation = true;
            clip.keepOriginalOrientation = true;
        }

        private static ModelImporterClipAnimation[] GetValidDefaultClipAnimations(ModelImporter importer)
        {
            if (importer == null)
                return Array.Empty<ModelImporterClipAnimation>();
            var defaults = importer.defaultClipAnimations;
            if (defaults == null || defaults.Length == 0)
                return Array.Empty<ModelImporterClipAnimation>();

            var valid = new List<ModelImporterClipAnimation>();
            foreach (var c in defaults)
            {
                if (c == null) continue;
                if (!string.IsNullOrEmpty(c.name) && c.name.StartsWith("__", StringComparison.Ordinal))
                    continue;
                if (c.lastFrame <= c.firstFrame)
                    continue;
                valid.Add(c);
            }
            return valid.ToArray();
        }

        internal static bool InferDefaultLoop(string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
                return true;
            string lower = clipName.ToLowerInvariant();
            string[] oneShotKeywords =
            {
                "attack", "slash", "strike", "punch", "kick", "cast", "spell",
                "jump", "die", "death", "dead", "hit", "hurt", "damage", "dodge", "roll"
            };
            foreach (string kw in oneShotKeywords)
            {
                if (lower.Contains(kw))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 绑骨 FBX 导入收尾：Humanoid 配置 → Refresh → 骨映射修复 → 缺贴图时从源模型/渲染图恢复 → 若含内嵌剪辑则建多状态控制器。
        /// </summary>
        public static void FinalizeRiggedImport(
            string assetPath,
            string sourceModelPath = null,
            string renderedImagePath = null)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            SetupRiggedCharacterImport(assetPath);
            PathUtils.ImportAssetAfterDiskWrite(assetPath);
            TryFixHumanoidBoneMapping(assetPath);

            RepairMaterialsFromExtractedFbm(assetPath);

            if (!HasValidMainTexture(assetPath) &&
                (!string.IsNullOrEmpty(sourceModelPath) || !string.IsNullOrEmpty(renderedImagePath)))
            {
                ApplyTexturesFromSourceToRiggedModel(sourceModelPath, assetPath, renderedImagePath);
            }

            var embeddedClips = GetAnimationClipsFromFbx(assetPath);
            if (embeddedClips.Count > 0)
            {
                string modelDir = Path.GetDirectoryName(assetPath)?.Replace("\\", "/") ?? "";
                string baseName = Path.GetFileNameWithoutExtension(assetPath);
                BuildAnimatorControllerFromEmbeddedClips(modelDir, baseName, assetPath);
            }
        }

        /// <summary>
        /// 在 SetupRiggedCharacterImport + Refresh() 之后调用。
        /// 若 Avatar.isHuman 仍为 False，扫描 FBX 内的标准命名骨骼并补充缺失的 Humanoid 映射，
        /// 再触发一次重新导入。适用于 UniRig 输出中 Chest/UpperChest/Neck/Head 未被自动识别的情况。
        /// </summary>
        /// <returns>修复后 avatar.isHuman 是否为 True。</returns>
        public static bool TryFixHumanoidBoneMapping(string assetPath)
        {
            // 已经是 Humanoid 则不处理
            var existingAvatar = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                                              .OfType<Avatar>()
                                              .FirstOrDefault(a => a != null && a.isValid);
            if (existingAvatar != null && existingAvatar.isHuman)
                return true;

            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null) return false;

            // 收集 FBX 中所有骨骼名
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (go == null) return false;
            var allBones = new HashSet<string>(
                go.GetComponentsInChildren<Transform>(true).Select(t => t.name));

            // 已映射的 humanName 集合
            var desc           = importer.humanDescription;
            var alreadyMapped  = new HashSet<string>(
                (desc.human ?? new HumanBone[0]).Select(h => h.humanName));
            var humanBones     = new List<HumanBone>(desc.human ?? new HumanBone[0]);
            bool changed       = false;

            // 候选：(humanName, boneName in FBX)
            // 先列直接同名映射，Head 条目按优先级排列（先尝试 "Head"，再用 "Neck_end" 兜底）
            var candidates = new (string humanName, string boneName)[]
            {
                ("Chest",          "Chest"),
                ("UpperChest",     "UpperChest"),
                ("Neck",           "Neck"),
                ("Head",           "Head"),
                ("Head",           "Neck_end"),    // UniRig 常见兜底
                ("LeftShoulder",   "LeftShoulder"),
                ("RightShoulder",  "RightShoulder"),
                ("LeftToes",       "LeftToes"),
                ("RightToes",      "RightToes"),
            };

            foreach (var (humanName, boneName) in candidates)
            {
                if (alreadyMapped.Contains(humanName)) continue;
                if (!allBones.Contains(boneName)) continue;

                var hb = new HumanBone();
                hb.humanName           = humanName;
                hb.boneName            = boneName;
                hb.limit.useDefaultValues = true;
                humanBones.Add(hb);
                alreadyMapped.Add(humanName);   // 防止同 humanName 的兜底条目重复添加
                changed = true;
                TJLog.Log($"[RiggedModelPostProcess] 补充 Humanoid 骨映射: {humanName} → {boneName}");
            }

            if (!changed)
            {
                TJLog.LogWarning($"[RiggedModelPostProcess] Avatar 不是 Humanoid 且无法自动补全缺失映射: {assetPath}");
                return false;
            }

            desc.human            = humanBones.ToArray();
            importer.humanDescription = desc;
            importer.SaveAndReimport();

            var newAvatar = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                                         .OfType<Avatar>()
                                         .FirstOrDefault(a => a != null && a.isValid);
            bool success = newAvatar?.isHuman == true;
            if (success)
                TJLog.Log($"[RiggedModelPostProcess] Humanoid 骨映射修复成功: {assetPath}");
            else
                TJLog.LogWarning($"[RiggedModelPostProcess] 修复后 Avatar 仍非 Humanoid，骨骼结构可能不兼容: {assetPath}");
            return success;
        }

        public static void SetupAnimationImport(string assetPath, bool loopTime = true)
        {
            SetupAnimationImport(assetPath, loopTime, null);
        }

        public static void SetupAnimationImport(string assetPath, bool loopTime, string clipName)
        {
            ModelImporter modelImporter = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (modelImporter != null)
            {
                modelImporter.animationType = ModelImporterAnimationType.Human;
                modelImporter.importAnimation = true;
                modelImporter.isReadable = true;
                var clips = modelImporter.defaultClipAnimations;
                if (clips != null && clips.Length > 0)
                {
                    string sanitizedClipName = SanitizeStateOrClipName(clipName);
                    foreach (var clip in clips)
                    {
                        clip.loopTime = loopTime;
                        ConfigureHumanoidRootMotionGrounding(clip);
                    }
                    if (clips.Length == 1 && !string.IsNullOrEmpty(sanitizedClipName))
                        clips[0].name = sanitizedClipName;
                    modelImporter.clipAnimations = clips;
                }
                modelImporter.SaveAndReimport();
                TryFixHumanoidBoneMapping(assetPath);
                TJLog.Log($"[RiggedModelPostProcess] 动画导入配置完成: {assetPath}, loopTime={loopTime}, clipName={clipName ?? "(default)"}");
            }
        }

        private static string SanitizeStateOrClipName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            return raw.Trim();
        }

        /// <summary>
        /// 检查模型上是否至少有一个材质槽绑定了有效主贴图（mainTexture / _BaseMap / _MainTex）。
        /// </summary>
        private static bool HasValidMainTexture(string assetPath)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (root == null) return false;
            foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
            {
                if (rend.sharedMaterials == null) continue;
                foreach (var mat in rend.sharedMaterials)
                {
                    if (mat == null) continue;
                    if (mat.mainTexture != null) return true;
                    if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null) return true;
                    if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 将进 UniRig 前模型上的材质赋给绑骨后的模型。
        /// 优先按 Renderer 数量一致时按下标一一对应；否则按层级相对路径匹配；再否则按较短列表长度按下标对齐。
        /// </summary>
        /// <returns>成功写入材质的 Renderer 数量。</returns>
        public static int ApplyMaterialsFromSourceModelToRiggedModel(string sourceAssetPath, string riggedAssetPath)
        {
            if (string.IsNullOrEmpty(sourceAssetPath) || string.IsNullOrEmpty(riggedAssetPath))
                return 0;

            var sourceRoot = AssetDatabase.LoadAssetAtPath<GameObject>(sourceAssetPath);
            var riggedRoot = AssetDatabase.LoadAssetAtPath<GameObject>(riggedAssetPath);
            if (sourceRoot == null || riggedRoot == null)
            {
                TJLog.LogWarning(
                    $"[RiggedModelPostProcess] 绑骨后材质复用：无法加载模型 source={sourceAssetPath} rigged={riggedAssetPath}"
                );
                return 0;
            }

            Renderer[] src = sourceRoot.GetComponentsInChildren<Renderer>(true);
            Renderer[] dst = riggedRoot.GetComponentsInChildren<Renderer>(true);
            if (src.Length == 0 || dst.Length == 0)
                return 0;

            int applied = 0;

            if (src.Length == dst.Length)
            {
                for (int i = 0; i < src.Length; i++)
                {
                    if (src[i].sharedMaterials == null)
                        continue;
                    dst[i].sharedMaterials = src[i].sharedMaterials;
                    applied++;
                }
            }
            else
            {
                var byPath = new Dictionary<string, Material[]>(StringComparer.Ordinal);
                foreach (var r in src)
                {
                    string p = RendererHierarchyPath(sourceRoot.transform, r);
                    if (p == null)
                        continue;
                    if (!byPath.ContainsKey(p) && r.sharedMaterials != null)
                        byPath[p] = r.sharedMaterials;
                }

                var matchedDst = new bool[dst.Length];
                for (int i = 0; i < dst.Length; i++)
                {
                    string p = RendererHierarchyPath(riggedRoot.transform, dst[i]);
                    if (p != null && byPath.TryGetValue(p, out Material[] mats) && mats != null)
                    {
                        dst[i].sharedMaterials = mats;
                        matchedDst[i] = true;
                        applied++;
                    }
                }

                if (applied == 0)
                {
                    int n = Math.Min(src.Length, dst.Length);
                    for (int i = 0; i < n; i++)
                    {
                        if (src[i].sharedMaterials == null)
                            continue;
                        dst[i].sharedMaterials = src[i].sharedMaterials;
                        applied++;
                    }
                }
            }

            if (applied > 0)
                AssetDatabase.SaveAssets();

            TJLog.Log(
                $"[RiggedModelPostProcess] 绑骨后从源模型复用材质: {applied}/{dst.Length} 个 Renderer，源={sourceAssetPath}"
            );
            return applied;
        }

        /// <summary>
        /// 使用外部动作 FBX 中的剪辑，为绑骨后主模型创建单状态循环 AnimatorController。
        /// </summary>
        /// <returns>创建的 AnimatorController 资产路径；若失败返回 null。</returns>
        public static string CreateSingleClipLoopAnimatorControllerFromMotionClip(
            string modelDir,
            string targetRigBaseName,
            string motionFbxUnityPath)
        {
            return CreateSingleClipLoopAnimatorControllerFromMotionClip(modelDir, targetRigBaseName,
                motionFbxUnityPath, true);
        }

        public static string CreateSingleClipLoopAnimatorControllerFromMotionClip(
            string modelDir,
            string targetRigBaseName,
            string motionFbxUnityPath,
            bool loop)
        {
            try
            {
                modelDir = PathUtils.NormalizeModelDirectory(modelDir);

                string controllerPath =
                    Path.Combine(modelDir, targetRigBaseName + "_Controller.controller").Replace("\\", "/");
                string controllerDir = Path.GetDirectoryName(controllerPath)?.Replace("\\", "/") ?? "";
                string absoluteControllerDir = PathUtils.ToAbsoluteAssetPath(controllerDir);
                if (!string.IsNullOrEmpty(absoluteControllerDir) && !Directory.Exists(absoluteControllerDir))
                    Directory.CreateDirectory(absoluteControllerDir);

                AnimationClip clip = GetAnimationClipFromFbx(motionFbxUnityPath);
                if (clip == null)
                {
                    TJLog.LogWarning(
                        $"[RiggedModelPostProcess] 无法从动作 FBX 提取剪辑，跳过控制器创建: {motionFbxUnityPath}"
                    );
                    return null;
                }

                // 若 Controller 已存在（重新生成同一任务时），删除后重建，确保新动作剪辑生效
                if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath) != null)
                {
                    AssetDatabase.DeleteAsset(controllerPath);
                    TJLog.Log($"[RiggedModelPostProcess] 已删除旧 Animator Controller，重新创建: {controllerPath}");
                }

                var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                if (controller == null)
                {
                    TJLog.LogWarning($"[RiggedModelPostProcess] 无法创建 Animator Controller: {controllerPath}");
                    return null;
                }

                var sm = controller.layers[0].stateMachine;
                AnimatorState previousDefault = sm.defaultState;
                string stateName = string.IsNullOrEmpty(clip.name) ? "Motion" : clip.name;
                var motionState = sm.AddState(stateName);
                motionState.motion = clip;
                motionState.writeDefaultValues = true;
                sm.defaultState = motionState;
                if (previousDefault != null && previousDefault != motionState)
                    sm.RemoveState(previousDefault);

                if (loop)
                {
                    var selfLoop = motionState.AddTransition(motionState);
                    selfLoop.hasExitTime = true;
                    selfLoop.exitTime = 1f;
                    selfLoop.duration = 0f;
                    selfLoop.offset = 0f;
                    selfLoop.hasFixedDuration = true;
                }

                AssetDatabase.SaveAssets();
                PathUtils.SafeRefresh();

                TJLog.Log(
                    $"[RiggedModelPostProcess] 单剪辑 Animator Controller 已创建: {controllerPath} (clip={clip.name}, loop={loop})"
                );
                return controllerPath;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[RiggedModelPostProcess] 创建 Animator Controller 失败: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 向绑骨模型对应的 AnimatorController 增量添加或更新一个具名动画状态，保留已有状态（如 idle / walk / attack）。
        /// 首次创建时或新增状态为 idle 且当前默认状态不是 idle 时，自动将该状态设为 defaultState。
        /// </summary>
        /// <returns>创建或更新后的 AnimatorController 资产路径；若失败返回 null。</returns>
        public static string AddOrUpdateMotionClipInAnimatorController(
            string modelDir,
            string targetRigBaseName,
            string motionFbxUnityPath,
            string stateName,
            bool loop = true,
            bool replaceController = false)
        {
            try
            {
                modelDir = PathUtils.NormalizeModelDirectory(modelDir);

                string controllerPath =
                    Path.Combine(modelDir, targetRigBaseName + "_Controller.controller").Replace("\\", "/");
                string controllerDir = Path.GetDirectoryName(controllerPath)?.Replace("\\", "/") ?? "";
                string absoluteControllerDir = PathUtils.ToAbsoluteAssetPath(controllerDir);
                if (!string.IsNullOrEmpty(absoluteControllerDir) && !Directory.Exists(absoluteControllerDir))
                    Directory.CreateDirectory(absoluteControllerDir);

                AnimationClip clip = GetAnimationClipFromFbx(motionFbxUnityPath);
                if (clip == null)
                {
                    TJLog.LogWarning(
                        $"[RiggedModelPostProcess] 无法从动作 FBX 提取剪辑，跳过控制器更新: {motionFbxUnityPath}"
                    );
                    return null;
                }

                AnimatorController controller = null;
                if (replaceController && AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath) != null)
                {
                    AssetDatabase.DeleteAsset(controllerPath);
                }
                else
                {
                    controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                }

                if (controller == null)
                {
                    controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                    if (controller == null)
                    {
                        TJLog.LogWarning($"[RiggedModelPostProcess] 无法创建 Animator Controller: {controllerPath}");
                        return null;
                    }
                }

                var sm = controller.layers[0].stateMachine;
                string effectiveStateName = !string.IsNullOrWhiteSpace(stateName)
                    ? stateName.Trim()
                    : (string.IsNullOrEmpty(clip.name) ? "Motion" : clip.name);

                AnimatorState targetState = null;
                foreach (var childState in sm.states)
                {
                    if (childState.state != null &&
                        string.Equals(childState.state.name, effectiveStateName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetState = childState.state;
                        break;
                    }
                }

                if (targetState == null)
                {
                    int stateCount = sm.states != null ? sm.states.Length : 0;
                    targetState = sm.AddState(effectiveStateName, new Vector3(300f, stateCount * 70f, 0f));
                }

                targetState.motion = clip;
                targetState.writeDefaultValues = true;

                // 清理该状态遗留的旧自循环转换，避免非循环动作误循环（循环行为由 clip.loopTime 驱动）
                if (targetState.transitions != null && targetState.transitions.Length > 0)
                {
                    foreach (var t in targetState.transitions.ToArray())
                    {
                        if (t != null && t.destinationState == targetState)
                            targetState.RemoveTransition(t);
                    }
                }

                if (sm.defaultState == null ||
                    (IsIdleStateName(effectiveStateName) && !IsIdleStateName(sm.defaultState.name)))
                {
                    sm.defaultState = targetState;
                }

                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                PathUtils.SafeRefresh();

                TJLog.Log(
                    $"[RiggedModelPostProcess] Animator Controller 状态已更新: {controllerPath} (state={effectiveStateName}, clip={clip.name}, loop={loop}, totalStates={sm.states.Length})"
                );
                return controllerPath;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[RiggedModelPostProcess] 更新 Animator Controller 失败: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 当绑骨 FBX 本身已内嵌多条动画剪辑时（例如 Rodin animation_presets），自动构建多状态 AnimatorController。
        /// </summary>
        public static string BuildAnimatorControllerFromEmbeddedClips(
            string modelDir,
            string targetRigBaseName,
            string riggedFbxUnityPath)
        {
            var clips = GetAnimationClipsFromFbx(riggedFbxUnityPath);
            if (clips.Count == 0)
                return null;

            try
            {
                modelDir = PathUtils.NormalizeModelDirectory(modelDir);
                string controllerPath =
                    Path.Combine(modelDir, targetRigBaseName + "_Controller.controller").Replace("\\", "/");
                string controllerDir = Path.GetDirectoryName(controllerPath)?.Replace("\\", "/") ?? "";
                string absoluteControllerDir = PathUtils.ToAbsoluteAssetPath(controllerDir);
                if (!string.IsNullOrEmpty(absoluteControllerDir) && !Directory.Exists(absoluteControllerDir))
                    Directory.CreateDirectory(absoluteControllerDir);

                if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath) != null)
                    AssetDatabase.DeleteAsset(controllerPath);

                var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                if (controller == null)
                    return null;

                var sm = controller.layers[0].stateMachine;
                AnimatorState preferredDefault = null;

                for (int i = 0; i < clips.Count; i++)
                {
                    var clip = clips[i];
                    string stateName = string.IsNullOrWhiteSpace(clip.name) ? $"Clip_{i}" : clip.name.Trim();
                    var state = sm.AddState(stateName, new Vector3(300f, i * 70f, 0f));
                    state.motion = clip;
                    state.writeDefaultValues = true;

                    if (preferredDefault == null)
                        preferredDefault = state;
                    else if (IsIdleStateName(stateName) && !IsIdleStateName(preferredDefault.name))
                        preferredDefault = state;
                }

                if (preferredDefault != null)
                    sm.defaultState = preferredDefault;

                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                PathUtils.SafeRefresh();

                TJLog.Log(
                    $"[RiggedModelPostProcess] 从内嵌剪辑创建 Animator Controller: {controllerPath} ({clips.Count} 个状态, default={sm.defaultState?.name})"
                );
                return controllerPath;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[RiggedModelPostProcess] 从内嵌剪辑创建 Animator Controller 失败: {e.Message}");
                return null;
            }
        }

        public static string[] GetControllerStateNames(string controllerPath)
        {
            if (string.IsNullOrEmpty(controllerPath))
                return Array.Empty<string>();
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null || controller.layers == null || controller.layers.Length == 0)
                return Array.Empty<string>();
            var states = controller.layers[0].stateMachine?.states;
            if (states == null || states.Length == 0)
                return Array.Empty<string>();
            return states
                .Where(s => s.state != null)
                .Select(s => s.state.name)
                .ToArray();
        }

        private static bool IsIdleStateName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            return name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static List<AnimationClip> GetAnimationClipsFromFbx(string fbxPath)
        {
            if (string.IsNullOrEmpty(fbxPath) || !File.Exists(PathUtils.ToAbsoluteAssetPath(fbxPath)))
                return new List<AnimationClip>();

            return AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                .OfType<AnimationClip>()
                .Where(c => c != null && !c.name.StartsWith("__", StringComparison.Ordinal))
                .ToList();
        }

        public static AnimationClip GetAnimationClipFromFbx(string fbxPath)
        {
            var clips = GetAnimationClipsFromFbx(fbxPath);
            if (clips.Count > 0)
            {
                TJLog.Log($"[RiggedModelPostProcess] 从 {fbxPath} 找到 {clips.Count} 个动画剪辑");
                return clips[0];
            }

            TJLog.LogWarning($"[RiggedModelPostProcess] 未在 {fbxPath} 中找到动画剪辑");
            return null;
        }

        /// <summary>
        /// 将绑骨 FBX 中嵌入的材质提取为外部 .mat 文件，应用贴图后通过 ModelImporter.AddRemap 建立持久映射。
        /// 解决直接修改嵌入材质后在历史记录切换、域重载等场景中贴图丢失的问题。
        /// 提取目录为 &lt;modelDir&gt;/&lt;baseName&gt;.fbm/，与主模型后处理保持一致。
        /// </summary>
        /// <param name="riggedAssetPath">绑骨模型的 Unity 相对路径（Assets/...）</param>
        /// <param name="textureAssetPath">要写入的贴图 Unity 相对路径；为 null/empty 时仅提取材质不赋贴图</param>
        /// <returns>成功处理的材质数量</returns>
        public static int ExtractMaterialsAndApplyTextureToRiggedModel(string riggedAssetPath, string textureAssetPath)
        {
            if (string.IsNullOrEmpty(riggedAssetPath))
                return 0;

            Texture2D texture = null;
            if (!string.IsNullOrEmpty(textureAssetPath))
            {
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(textureAssetPath);
                if (texture == null)
                {
                    TJLog.LogWarning($"[RiggedModelPostProcess] 无法加载贴图，跳过材质写入: {textureAssetPath}");
                    return 0;
                }
            }

            return ExtractMaterialsAndApplyTextureToRiggedModel(riggedAssetPath, texture,
                texture != null ? $"→ {textureAssetPath}" : "，未赋贴图");
        }

        /// <summary>
        /// 将绑骨 FBX 中嵌入的材质提取为外部 .mat，并写入 Texture2D。
        /// </summary>
        public static int ExtractMaterialsAndApplyTextureToRiggedModel(string riggedAssetPath, Texture2D texture)
        {
            if (string.IsNullOrEmpty(riggedAssetPath))
                return 0;

            string label = texture != null ? $"→ {texture.name}" : "，未赋贴图";
            return ExtractMaterialsAndApplyTextureToRiggedModel(riggedAssetPath, texture, label);
        }

        private static int ExtractMaterialsAndApplyTextureToRiggedModel(
            string riggedAssetPath, Texture2D texture, string textureLabel)
        {
            var importer = AssetImporter.GetAtPath(riggedAssetPath) as ModelImporter;
            if (importer == null) return 0;

            string modelDir = Path.GetDirectoryName(riggedAssetPath)?.Replace("\\", "/") ?? "";
            string baseName = Path.GetFileNameWithoutExtension(riggedAssetPath);
            string matDir = string.IsNullOrEmpty(modelDir)
                ? $"{baseName}.fbm"
                : $"{modelDir}/{baseName}.fbm";

            string absMatDir = PathUtils.ToAbsoluteAssetPath(matDir);
            if (!string.IsNullOrEmpty(absMatDir) && !Directory.Exists(absMatDir))
                Directory.CreateDirectory(absMatDir);

            var embeddedMaterials = AssetDatabase.LoadAllAssetsAtPath(riggedAssetPath)
                .OfType<Material>()
                .ToList();

            if (embeddedMaterials.Count == 0)
            {
                TJLog.LogWarning($"[RiggedModelPostProcess] 绑骨模型中无嵌入材质，跳过材质提取: {riggedAssetPath}");
                return 0;
            }

            int processed = 0;
            foreach (var embMat in embeddedMaterials)
            {
                string matPath = $"{matDir}/{embMat.name}.mat";

                Material extMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (extMat == null)
                {
                    extMat = new Material(embMat);
                    AssetDatabase.CreateAsset(extMat, matPath);
                }

                if (texture != null)
                {
                    extMat.mainTexture = texture;
                    if (extMat.HasProperty("_BaseMap"))
                        extMat.SetTexture("_BaseMap", texture);
                    if (extMat.HasProperty("_MainTex"))
                        extMat.SetTexture("_MainTex", texture);
                    EditorUtility.SetDirty(extMat);
                }

                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(typeof(Material), embMat.name),
                    extMat
                );
                processed++;
            }

            if (processed > 0)
            {
                importer.SaveAndReimport();
                AssetDatabase.SaveAssets();
            }

            TJLog.Log(
                $"[RiggedModelPostProcess] 绑骨模型材质外部化完成: {processed} 个材质{textureLabel}，模型={riggedAssetPath}"
            );
            return processed;
        }

        /// <summary>
        /// 绑骨完成后从源模型恢复贴图：① 复制材质 → ② rendered_image → ③ .fbm。
        /// </summary>
        public static int ApplyTexturesFromSourceToRiggedModel(
            string sourceAssetPath,
            string riggedAssetPath,
            string renderedTexturePath = null)
        {
            if (string.IsNullOrEmpty(riggedAssetPath))
                return 0;

            if (!string.IsNullOrEmpty(sourceAssetPath))
            {
                int materialsApplied = ApplyMaterialsFromSourceModelToRiggedModel(sourceAssetPath, riggedAssetPath);
                if (materialsApplied > 0 && HasValidMainTexture(riggedAssetPath))
                    return materialsApplied;
                // 贴图槽为空：继续走后续 rendered_image / .fbm 路径
                if (materialsApplied > 0)
                    TJLog.LogWarning($"[RiggedModelPostProcess] 源模型材质已复用但主贴图为空，继续尝试 rendered_image: {riggedAssetPath}");
            }

            if (!string.IsNullOrEmpty(renderedTexturePath))
            {
                int extracted = ExtractMaterialsAndApplyTextureToRiggedModel(riggedAssetPath, renderedTexturePath);
                if (extracted > 0)
                    return extracted;
                TJLog.LogWarning($"[RiggedModelPostProcess] 材质外部化失败，rendered_image 贴图未写入: {riggedAssetPath}");
                return 0;
            }

            if (string.IsNullOrEmpty(sourceAssetPath))
                return 0;

            string texturePath = FindMainTextureFromSourceModel(sourceAssetPath);
            if (!string.IsNullOrEmpty(texturePath))
            {
                int extracted = ExtractMaterialsAndApplyTextureToRiggedModel(riggedAssetPath, texturePath);
                if (extracted > 0)
                    return extracted;
            }

            return 0;
        }

        /// <summary>
        /// 从源模型目录查找主贴图（优先 .fbm 子文件夹）。
        /// </summary>
        public static string FindMainTextureFromSourceModel(string sourceAssetPath)
        {
            if (string.IsNullOrEmpty(sourceAssetPath)) return null;

            string modelDir = Path.GetDirectoryName(sourceAssetPath)?.Replace("\\", "/") ?? "";
            string baseName = Path.GetFileNameWithoutExtension(sourceAssetPath);
            string fbmDir = string.IsNullOrEmpty(modelDir)
                ? $"{baseName}.fbm"
                : $"{modelDir}/{baseName}.fbm";
            string absFbmDir = PathUtils.ToAbsoluteAssetPath(fbmDir);

            if (!string.IsNullOrEmpty(absFbmDir) && Directory.Exists(absFbmDir))
            {
                string[] texFiles = Directory.GetFiles(absFbmDir, "*.png", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(absFbmDir, "*.jpg",  SearchOption.TopDirectoryOnly))
                    .Concat(Directory.GetFiles(absFbmDir, "*.jpeg", SearchOption.TopDirectoryOnly))
                    .Concat(Directory.GetFiles(absFbmDir, "*.webp", SearchOption.TopDirectoryOnly))
                    .ToArray();

                bool hasWebp = texFiles.Any(f => f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase));
                if (hasWebp)
                    TJLog.LogWarning($"[RiggedModelPostProcess] 贴图目录含 .webp 文件，Unity 2021.2 以下版本可能无法导入: {absFbmDir}");

                string primary = texFiles.FirstOrDefault(f =>
                {
                    string lower = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    return lower.Contains("albedo") || lower.Contains("diffuse") ||
                           lower.Contains("basecolor") || lower.Contains("color");
                });
                if (primary != null)
                    return PathUtils.AbsolutePathToAssetsRelative(primary.Replace("\\", "/"));

                string nonPbr = texFiles.FirstOrDefault(f =>
                {
                    string lower = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    return !lower.Contains("normal") && !lower.Contains("roughness") &&
                           !lower.Contains("metallic") && !lower.Contains("occlusion") &&
                           !lower.Contains("emission") && !lower.Contains("height");
                });
                if (nonPbr != null)
                    return PathUtils.AbsolutePathToAssetsRelative(nonPbr.Replace("\\", "/"));

                if (texFiles.Length > 0)
                    return PathUtils.AbsolutePathToAssetsRelative(texFiles[0].Replace("\\", "/"));
            }

            if (!string.IsNullOrEmpty(modelDir))
            {
                string absModelDir = PathUtils.ToAbsoluteAssetPath(modelDir);
                if (!string.IsNullOrEmpty(absModelDir) && Directory.Exists(absModelDir))
                {
                    foreach (string ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
                    {
                        string absTexPath = Path.Combine(absModelDir, baseName + ext).Replace("\\", "/");
                        if (File.Exists(absTexPath))
                        {
                            if (ext == ".webp")
                                TJLog.LogWarning($"[RiggedModelPostProcess] 找到 .webp 贴图，Unity 2021.2 以下版本可能无法导入: {absTexPath}");
                            return PathUtils.AbsolutePathToAssetsRelative(absTexPath);
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 扫描 FBX 同目录 &lt;baseName&gt;.fbm/ 中提取出的贴图，自动修复 Unity 无法映射的嵌入材质
        /// （如 Blender/Rodin 导出路径与下载文件名不一致导致白模、或分离的 metallic/roughness 未合成为 URP MetallicGlossMap）。
        /// 幂等：若所有材质已绑定主贴图且无待合成的 PBR 通道，直接返回 0。
        /// </summary>
        public static int RepairMaterialsFromExtractedFbm(string fbxAssetPath)
        {
            if (string.IsNullOrEmpty(fbxAssetPath))
                return 0;

            var importer = AssetImporter.GetAtPath(fbxAssetPath) as ModelImporter;
            if (importer == null)
                return 0;

            string modelDir = Path.GetDirectoryName(fbxAssetPath)?.Replace("\\", "/") ?? "";
            string baseName = Path.GetFileNameWithoutExtension(fbxAssetPath);
            string safeBase = PathUtils.SanitizeAssetFolderName(baseName);
            string fbmDir = string.IsNullOrEmpty(modelDir) ? $"{safeBase}.fbm" : $"{modelDir}/{safeBase}.fbm";
            string absFbmDir = PathUtils.ToAbsoluteAssetPath(fbmDir);
            if (string.IsNullOrEmpty(absFbmDir) || !Directory.Exists(absFbmDir))
            {
                fbmDir = string.IsNullOrEmpty(modelDir) ? $"{baseName}.fbm" : $"{modelDir}/{baseName}.fbm";
                absFbmDir = PathUtils.ToAbsoluteAssetPath(fbmDir);
            }
            if (string.IsNullOrEmpty(absFbmDir) || !Directory.Exists(absFbmDir))
                return 0;

            string[] imageFiles = Directory.GetFiles(absFbmDir)
                .Where(f =>
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga" || ext == ".webp";
                })
                .ToArray();
            if (imageFiles.Length == 0)
                return 0;

            string diffusePath = FindFbmTextureByRole(imageFiles, "diffuse", "albedo", "basecolor", "base_color", "color");
            string normalPath = FindFbmTextureByRole(imageFiles, "normal", "nrm", "bump");
            string metallicPath = FindFbmTextureByRole(imageFiles, "metallic", "metalness", "metal");
            string roughnessPath = FindFbmTextureByRole(imageFiles, "roughness", "rough");
            string occlusionPath = FindFbmTextureByRole(imageFiles, "occlusion", "ambient", "_ao", "ao");
            string emissionPath = FindFbmTextureByRole(imageFiles, "emission", "emissive");
            string packedPath = imageFiles.FirstOrDefault(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f), "metallic_smoothness", StringComparison.OrdinalIgnoreCase));

            if (diffusePath == null)
            {
                diffusePath = imageFiles.FirstOrDefault(f =>
                {
                    string lower = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    return !lower.Contains("normal") && !lower.Contains("nrm") && !lower.Contains("bump")
                        && !lower.Contains("rough") && !lower.Contains("metal")
                        && !lower.Contains("occlusion") && !lower.Contains("ambient")
                        && !lower.Contains("emission") && !lower.Contains("emissive")
                        && !lower.Contains("height") && lower != "ao" && !lower.EndsWith("_ao");
                });
            }

            if (string.IsNullOrEmpty(diffusePath))
                return 0;

            bool hasMissingMainTex = !HasValidMainTexture(fbxAssetPath);
            bool hasSeparatePbrMaps = !string.IsNullOrEmpty(metallicPath) || !string.IsNullOrEmpty(roughnessPath);
            bool needsPbrPacking = hasSeparatePbrMaps && !HasValidMetallicGlossMap(fbxAssetPath);

            if (!hasMissingMainTex && !needsPbrPacking)
                return 0;

            var embeddedMaterials = AssetDatabase.LoadAllAssetsAtPath(fbxAssetPath)
                .OfType<Material>()
                .ToList();
            if (embeddedMaterials.Count == 0)
                return 0;

            string diffuseAsset = PathUtils.AbsolutePathToAssetsRelative(diffusePath.Replace("\\", "/"));
            Texture2D diffuseTex = LoadImportedTexture(diffuseAsset, markAsNormal: false, forceLinear: false);
            if (diffuseTex == null)
                return 0;

            Texture2D normalTex = !string.IsNullOrEmpty(normalPath)
                ? LoadImportedTexture(PathUtils.AbsolutePathToAssetsRelative(normalPath.Replace("\\", "/")), markAsNormal: true, forceLinear: true)
                : null;
            Texture2D occlusionTex = !string.IsNullOrEmpty(occlusionPath)
                ? LoadImportedTexture(PathUtils.AbsolutePathToAssetsRelative(occlusionPath.Replace("\\", "/")), markAsNormal: false, forceLinear: true)
                : null;
            Texture2D emissionTex = !string.IsNullOrEmpty(emissionPath)
                ? LoadImportedTexture(PathUtils.AbsolutePathToAssetsRelative(emissionPath.Replace("\\", "/")), markAsNormal: false, forceLinear: false)
                : null;

            Texture2D metallicSmoothnessTex = null;
            if (hasSeparatePbrMaps)
            {
                string targetPackedAsset = fbmDir.TrimEnd('/') + "/metallic_smoothness.png";
                if (!string.IsNullOrEmpty(packedPath) && File.Exists(packedPath))
                {
                    metallicSmoothnessTex = LoadImportedTexture(targetPackedAsset, markAsNormal: false, forceLinear: true);
                }
                else
                {
                    Texture2D metalTex = !string.IsNullOrEmpty(metallicPath)
                        ? LoadImportedTexture(PathUtils.AbsolutePathToAssetsRelative(metallicPath.Replace("\\", "/")), markAsNormal: false, forceLinear: true)
                        : null;
                    Texture2D roughTex = !string.IsNullOrEmpty(roughnessPath)
                        ? LoadImportedTexture(PathUtils.AbsolutePathToAssetsRelative(roughnessPath.Replace("\\", "/")), markAsNormal: false, forceLinear: true)
                        : null;
                    metallicSmoothnessTex = BuildPackedMetallicSmoothnessMap(metalTex, roughTex, targetPackedAsset);
                }
            }

            Shader litShader = TJMaterialShaderUtility.ResolveSurfaceLitShader();
            int processed = 0;
            bool multiMat = embeddedMaterials.Count > 1;

            foreach (var embMat in embeddedMaterials)
            {
                if (embMat == null) continue;
                if (multiMat && MaterialHasMainTexture(embMat))
                    continue;

                string safeMatName = PathUtils.SanitizeAssetFolderName(string.IsNullOrEmpty(embMat.name) ? "Material" : embMat.name);
                string matPath = $"{fbmDir.TrimEnd('/')}/{safeMatName}.mat";

                Material extMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (extMat == null)
                {
                    extMat = litShader != null ? new Material(litShader) : new Material(embMat);
                    extMat.name = embMat.name;
                    AssetDatabase.CreateAsset(extMat, matPath);
                }
                else if (litShader != null)
                {
                    TJMaterialShaderUtility.EnsureCompatibleSurfaceShader(extMat);
                }

                if (extMat.HasProperty("_BaseColor")) extMat.SetColor("_BaseColor", Color.white);
                if (extMat.HasProperty("_Color")) extMat.SetColor("_Color", Color.white);
                TJMaterialShaderUtility.AssignBaseColorTexture(extMat, diffuseTex);

                if (normalTex != null && extMat.HasProperty("_BumpMap"))
                {
                    extMat.SetTexture("_BumpMap", normalTex);
                    if (extMat.HasProperty("_BumpScale")) extMat.SetFloat("_BumpScale", 1f);
                    extMat.EnableKeyword("_NORMALMAP");
                }

                ApplyPackedMetallicSmoothness(extMat, metallicSmoothnessTex);

                if (occlusionTex != null && extMat.HasProperty("_OcclusionMap"))
                {
                    extMat.SetTexture("_OcclusionMap", occlusionTex);
                    if (extMat.HasProperty("_OcclusionStrength")) extMat.SetFloat("_OcclusionStrength", 1f);
                    extMat.EnableKeyword("_OCCLUSIONMAP");
                }

                if (emissionTex != null && extMat.HasProperty("_EmissionMap"))
                {
                    extMat.SetTexture("_EmissionMap", emissionTex);
                    if (extMat.HasProperty("_EmissionColor")) extMat.SetColor("_EmissionColor", Color.white);
                    extMat.EnableKeyword("_EMISSION");
                    extMat.globalIlluminationFlags &= ~MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                }

                EditorUtility.SetDirty(extMat);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embMat.name), extMat);
                processed++;
            }

            if (processed > 0)
            {
                AssetDatabase.SaveAssets();
                importer.SaveAndReimport();
                TJLog.Log(
                    $"[RiggedModelPostProcess] 已从 .fbm 自动构建并映射 PBR 材质: {processed} 个材质 (normal={normalTex != null}, pbrPacked={metallicSmoothnessTex != null}) -> {fbxAssetPath}"
                );
            }
            return processed;
        }

        private static bool MaterialHasMainTexture(Material mat)
        {
            if (mat == null) return false;
            if (mat.mainTexture != null) return true;
            if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null) return true;
            if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null) return true;
            return false;
        }

        private static bool HasValidMetallicGlossMap(string assetPath)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (root == null) return false;
            foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
            {
                if (rend.sharedMaterials == null) continue;
                foreach (var mat in rend.sharedMaterials)
                {
                    if (mat == null) continue;
                    if (mat.HasProperty("_MetallicGlossMap") && mat.GetTexture("_MetallicGlossMap") != null)
                        return true;
                }
            }
            return false;
        }

        private static string FindFbmTextureByRole(string[] files, params string[] keywords)
        {
            foreach (string kw in keywords)
            {
                string match = files.FirstOrDefault(f =>
                {
                    string stem = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    if (stem == "metallic_smoothness") return false;
                    if (kw == "ao")
                        return stem == "ao" || stem.EndsWith("_ao") || stem.StartsWith("ao_");
                    return stem.Contains(kw);
                });
                if (match != null)
                    return match;
            }
            return null;
        }

        private static Texture2D LoadImportedTexture(string unityAssetPath, bool markAsNormal, bool forceLinear)
        {
            if (string.IsNullOrEmpty(unityAssetPath))
                return null;
            AssetDatabase.ImportAsset(unityAssetPath, ImportAssetOptions.Default);
            var ti = AssetImporter.GetAtPath(unityAssetPath) as TextureImporter;
            if (ti != null)
            {
                bool dirty = false;
                if (markAsNormal && ti.textureType != TextureImporterType.NormalMap)
                {
                    ti.textureType = TextureImporterType.NormalMap;
                    dirty = true;
                }
                if (forceLinear && !markAsNormal && ti.sRGBTexture)
                {
                    ti.sRGBTexture = false;
                    dirty = true;
                }
                if (dirty)
                    ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(unityAssetPath);
        }

        private static Texture2D BuildPackedMetallicSmoothnessMap(
            Texture2D metallicTex,
            Texture2D roughnessTex,
            string targetUnityAssetPath)
        {
            if (metallicTex == null && roughnessTex == null)
                return null;

            int width = roughnessTex != null ? roughnessTex.width : metallicTex.width;
            int height = roughnessTex != null ? roughnessTex.height : metallicTex.height;
            if (width <= 0 || height <= 0)
                return null;

            Color32[] metalPixels = metallicTex != null ? ReadLinearPixels(metallicTex, width, height) : null;
            Color32[] roughPixels = roughnessTex != null ? ReadLinearPixels(roughnessTex, width, height) : null;

            var packed = new Color32[width * height];
            for (int i = 0; i < packed.Length; i++)
            {
                byte m = metalPixels != null ? metalPixels[i].r : (byte)0;
                byte s = roughPixels != null ? (byte)(255 - roughPixels[i].r) : (byte)90;
                packed[i] = new Color32(m, 0, 0, s);
            }

            var outTex = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            outTex.SetPixels32(packed);
            outTex.Apply(false, false);
            byte[] pngBytes = outTex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(outTex);

            string absTarget = PathUtils.ToAbsoluteAssetPath(targetUnityAssetPath);
            if (string.IsNullOrEmpty(absTarget))
                return null;
            File.WriteAllBytes(absTarget, pngBytes);
            AssetDatabase.ImportAsset(targetUnityAssetPath, ImportAssetOptions.ForceUpdate);

            var ti = AssetImporter.GetAtPath(targetUnityAssetPath) as TextureImporter;
            if (ti != null)
            {
                ti.sRGBTexture = false;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(targetUnityAssetPath);
        }

        /// <summary>
        /// 按当前 Shader 类型（内置 Standard 用 _METALLICGLOSSMAP，URP/LWRP Lit 用 _METALLICSPECGLOSSMAP）
        /// 绑定线性 Metallic+Smoothness 打包贴图；无贴图时设置低光滑度哑光默认值。
        /// </summary>
        internal static void ApplyPackedMetallicSmoothness(Material mat, Texture2D metallicSmoothnessTex)
        {
            if (mat == null)
                return;

            string shaderName = mat.shader != null ? mat.shader.name : "";
            bool isBuiltInStandard = shaderName.Equals("Standard", StringComparison.Ordinal)
                || shaderName.StartsWith("Standard ", StringComparison.Ordinal);

            if (metallicSmoothnessTex != null && mat.HasProperty("_MetallicGlossMap"))
            {
                if (mat.HasProperty("_WorkflowMode")) mat.SetFloat("_WorkflowMode", 1f);
                if (mat.HasProperty("_SmoothnessTextureChannel")) mat.SetFloat("_SmoothnessTextureChannel", 0f);
                mat.SetTexture("_MetallicGlossMap", metallicSmoothnessTex);
                TJMaterialShaderUtility.SetMetallicAndSmoothness(mat, 1f, 1f);
                if (mat.HasProperty("_GlossMapScale")) mat.SetFloat("_GlossMapScale", 1f);
                if (isBuiltInStandard)
                {
                    mat.EnableKeyword("_METALLICGLOSSMAP");
                    mat.DisableKeyword("_METALLICSPECGLOSSMAP");
                }
                else
                {
                    mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                    mat.DisableKeyword("_METALLICGLOSSMAP");
                }
            }
            else
            {
                if (mat.HasProperty("_WorkflowMode")) mat.SetFloat("_WorkflowMode", 1f);
                TJMaterialShaderUtility.SetMetallicAndSmoothness(mat, 0f, 0.25f);
            }
        }

        private static Color32[] ReadLinearPixels(Texture2D source, int width, int height)
        {
            if (source.isReadable && source.width == width && source.height == height)
                return source.GetPixels32();

            var prev = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var readable = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readable.Apply(false, false);
                Color32[] pixels = readable.GetPixels32();
                UnityEngine.Object.DestroyImmediate(readable);
                return pixels;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        public static string RendererHierarchyPath(Transform modelRoot, Renderer renderer)
        {
            if (modelRoot == null || renderer == null)
                return null;

            Transform t = renderer.transform;
            var parts = new List<string>();
            while (t != null && t != modelRoot)
            {
                parts.Add(t.name);
                t = t.parent;
            }

            if (t != modelRoot)
                return null;

            parts.Reverse();
            return parts.Count == 0 ? string.Empty : string.Join("/", parts);
        }
    }
}
#endif
