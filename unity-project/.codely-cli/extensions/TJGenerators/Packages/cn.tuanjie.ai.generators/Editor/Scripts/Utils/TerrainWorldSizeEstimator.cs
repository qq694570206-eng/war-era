#if UNITY_EDITOR
using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Codely.Newtonsoft.Json.Linq;
using TJGenerators.Config;
using UnityEditor;
using UnityEngine;

namespace TJGenerators.Utils
{
    /// <summary>
    /// 用户未指定世界尺寸时，用后端豆包 Lite（task/doubao-multimodal）根据需求估较长边米数。
    /// LLM 失败则弹窗请用户输入；无关键词写死回退。
    /// </summary>
    public static class TerrainWorldSizeEstimator
    {
        public const float MinMeters = 32f;
        public const float MaxMeters = 4096f;
        public const float DialogDefaultMeters = 256f;

        public readonly struct EstimateResult
        {
            public float Meters { get; }
            /// <summary>user | user_text | llm | user_dialog | failed</summary>
            public string Source { get; }
            public string Reason { get; }

            /// <summary>LLM / 文案解析成功（含 meters=0 表示大地形走 1px=1m）。</summary>
            public bool Succeeded =>
                Source == "user" || Source == "user_text" || Source == "llm" || Source == "user_dialog";

            public EstimateResult(float meters, string source, string reason)
            {
                Meters = meters;
                Source = source ?? "failed";
                Reason = reason ?? "";
            }
        }

        private static readonly HttpClient Http = new HttpClient();

        /// <summary>
        /// 仅：显式米数解析 → 豆包 Lite。失败返回 Succeeded=false，由调用方弹窗询问。
        /// token/orgId 须在主线程取好；本方法可阻塞数秒（轮询）。
        /// </summary>
        public static EstimateResult EstimateSync(string userOrPromptText, string token, string orgId = null)
        {
            return PrepareEstimate(userOrPromptText, token, orgId)();
        }

        /// <summary>Capture Unity-backed configuration on the caller thread; run all network waits off-thread.</summary>
        public static Task<EstimateResult> EstimateAsync(string userOrPromptText, string token, string orgId = null)
        {
            var estimate = PrepareEstimate(userOrPromptText, token, orgId);
            return Task.Run(estimate);
        }

        private static Func<EstimateResult> PrepareEstimate(string userOrPromptText, string token, string orgId)
        {
            string text = (userOrPromptText ?? "").Trim();

            if (!string.IsNullOrEmpty(text) &&
                TryParseExplicitMeters(text, out float explicitMeters, out string explicitReason))
            {
                return () => new EstimateResult(ClampMeters(explicitMeters), "user_text", explicitReason);
            }

            if (string.IsNullOrEmpty(text))
                return () => new EstimateResult(0f, "failed", "empty prompt");

            if (string.IsNullOrEmpty(token))
                return () => new EstimateResult(0f, "failed", "no auth token");

            string baseUrl = ConfigManager.GetApiBaseUrl();
            string pollPattern = ConfigManager.GetPollStatusUrl("{taskId}");
            string source = ConfigManager.GetRequestSource();
            return () => EstimateFromBackend(text, token, orgId, baseUrl, pollPattern, source);
        }

        private static EstimateResult EstimateFromBackend(
            string text, string token, string orgId, string baseUrl, string pollPattern, string source)
        {
            try
            {
                if (TryEstimateWithDoubaoLite(text, token, orgId, baseUrl, pollPattern, source,
                        out float llmMeters, out string llmReason))
                    return new EstimateResult(ClampMeters(llmMeters), "llm", llmReason);
            }
            catch (Exception e)
            {
                TJLog.LogWarning($"[TerrainWorldSizeEstimator] doubao-multimodal failed: {e.Message}");
                return new EstimateResult(0f, "failed", e.Message);
            }

            return new EstimateResult(0f, "failed", "llm returned no usable size");
        }

        /// <summary>
        /// 主线程弹窗：请用户输入较长边米数。取消返回 false。
        /// </summary>
        public static bool TryAskUserMeters(string contextHint, out float meters, out string errorMessage)
        {
            if (Application.isBatchMode)
            {
                meters = 0f;
                errorMessage = "Provide world_size_meters when running without an interactive editor.";
                return false;
            }
            meters = 0f;
            errorMessage = null;

            if (!TerrainWorldSizeInputDialog.Show(
                    TJGeneratorsL10n.L("地形世界尺寸"),
                    BuildAskMessage(contextHint),
                    DialogDefaultMeters,
                    out float entered))
            {
                errorMessage = TJGeneratorsL10n.L("已取消：需要世界尺寸才能创建地形。请重新 apply 并填写 world_size_meters，或在弹窗中确认。");
                return false;
            }

            meters = ClampMeters(entered);
            if (meters <= 1f)
            {
                // 用户可填 0 表示「按像素 1:1」
                meters = 0f;
            }
            return true;
        }

        private static string BuildAskMessage(string contextHint)
        {
            string baseMsg = TJGeneratorsL10n.L(
                "无法自动判断地形世界尺寸（较长边，单位：米）。\n请输入大约边长后继续；填 0 表示按高度图像素 1 像素 = 1 米。");
            if (string.IsNullOrWhiteSpace(contextHint))
                return baseMsg;
            string clipped = contextHint.Trim();
            if (clipped.Length > 120)
                clipped = clipped.Substring(0, 120) + "…";
            return baseMsg + "\n\n" + TJGeneratorsL10n.L("需求摘要：") + clipped;
        }

        public static bool TryParseExplicitMeters(string text, out float meters, out string reason)
        {
            meters = 0f;
            reason = "";
            if (string.IsNullOrEmpty(text))
                return false;

            var km = Regex.Match(text, @"(?<![A-Za-z0-9_.+-])(?<value>\d+(?:\.\d+)?)\s*(?:(?:kilomet(?:er|re)s?|km)(?![A-Za-z])|公里|千米)",
                RegexOptions.IgnoreCase);
            if (km.Success &&
                float.TryParse(km.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float kmVal) &&
                kmVal > 0f)
            {
                meters = kmVal * 1000f;
                reason = $"explicit {kmVal} km";
                return true;
            }

            var m = Regex.Match(text,
                @"(?<![A-Za-z0-9_.+-])(?<value>\d+(?:\.\d+)?)\s*(?:(?:met(?:er|re)s?|m)(?![A-Za-z])|米)",
                RegexOptions.IgnoreCase);
            if (m.Success &&
                float.TryParse(m.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float mVal) &&
                mVal >= 0f && mVal <= MaxMeters * 2f)
            {
                meters = mVal;
                reason = $"explicit {mVal} m";
                return true;
            }

            return false;
        }

        public static float ClampMeters(float meters)
        {
            if (meters <= 1f)
                return 0f;
            return Mathf.Clamp(meters, MinMeters, MaxMeters);
        }

        public static bool TryParseLlmMeters(string llmText, out float meters, out string reason)
        {
            meters = 0f;
            reason = "";
            if (string.IsNullOrWhiteSpace(llmText))
                return false;

            string trimmed = llmText.Trim();
            var fence = Regex.Match(trimmed, @"```(?:json)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase);
            if (fence.Success)
                trimmed = fence.Groups[1].Value.Trim();

            try
            {
                if (trimmed.StartsWith("{"))
                {
                    var obj = JObject.Parse(trimmed);
                    if (TryReadMetersToken(obj, out meters, out reason))
                        return true;
                }

                int brace = trimmed.IndexOf('{');
                int braceEnd = trimmed.LastIndexOf('}');
                if (brace >= 0 && braceEnd > brace)
                {
                    var obj = JObject.Parse(trimmed.Substring(brace, braceEnd - brace + 1));
                    if (TryReadMetersToken(obj, out meters, out reason))
                        return true;
                }
            }
            catch
            {
                // fall through
            }

            var num = Regex.Match(trimmed, @"world_size_meters[\s:='""]+(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (num.Success &&
                float.TryParse(num.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out meters))
            {
                reason = "regex world_size_meters";
                return true;
            }

            return false;
        }

        private static bool TryReadMetersToken(JObject obj, out float meters, out string reason)
        {
            meters = 0f;
            reason = obj["reason"]?.ToString() ?? "";
            JToken token = obj["world_size_meters"] ?? obj["worldSizeMeters"] ?? obj["meters"];
            if (token == null || token.Type == JTokenType.Null)
                return false;
            meters = token.ToObject<float>();
            return true;
        }

        private static bool TryEstimateWithDoubaoLite(
            string text, string token, string orgId, string baseUrl, string pollPattern, string source,
            out float meters, out string reason)
        {
            meters = 0f;
            reason = "";
            if (string.IsNullOrEmpty(baseUrl))
                return false;

            string createUrl = baseUrl.TrimEnd('/') + "/task/doubao-multimodal";
            var body = new JObject { ["prompt"] = BuildAnalysisPrompt(text) };

            string createJson = PostJson(createUrl, body.ToString(), token, orgId, source, 30);
            var createObj = JObject.Parse(createJson);
            string backendTaskId = createObj["taskId"]?.ToString();
            if (string.IsNullOrEmpty(backendTaskId))
            {
                TJLog.LogWarning($"[TerrainWorldSizeEstimator] no taskId in response: {createJson}");
                return false;
            }

            string pollUrl = pollPattern.Replace("{taskId}", backendTaskId);
            const int maxTries = 40;
            for (int i = 0; i < maxTries; i++)
            {
                Thread.Sleep(i == 0 ? 800 : 1200);
                string statusJson = GetJson(pollUrl, token, orgId, source, 20);
                var statusObj = JObject.Parse(statusJson);
                string status = statusObj["status"]?.ToString() ?? "";
                if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
                {
                    TJLog.LogWarning($"[TerrainWorldSizeEstimator] task failed: {statusObj["error"]}");
                    return false;
                }

                if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
                    continue;

                string llmText =
                    statusObj["output"]?["data"]?["text"]?.ToString()
                    ?? statusObj["output"]?["text"]?.ToString()
                    ?? "";
                if (!TryParseLlmMeters(llmText, out meters, out reason))
                {
                    TJLog.LogWarning($"[TerrainWorldSizeEstimator] cannot parse LLM text: {llmText}");
                    return false;
                }

                reason = string.IsNullOrEmpty(reason) ? llmText.Trim() : reason;
                TJLog.Log($"[TerrainWorldSizeEstimator] llm meters={meters}, reason={reason}");
                return true;
            }

            TJLog.LogWarning("[TerrainWorldSizeEstimator] poll timeout");
            return false;
        }

        private static string BuildAnalysisPrompt(string userText)
        {
            return
                "You estimate Unity Terrain world size for a game scene.\n" +
                "Input is the user's terrain request or heightmap prompt (Chinese or English).\n" +
                "Return ONLY one JSON object, no markdown:\n" +
                "{\"world_size_meters\": <number>, \"reason\": \"<short>\"}\n" +
                "Rules:\n" +
                "- world_size_meters = longer side length in meters (1 Unity unit = 1 meter).\n" +
                "- Heightmap resolution is independent; do NOT shrink meters just because image is 2K.\n" +
                "- Small park / farm / golf / courtyard: typically 64–256.\n" +
                "- Flat tropical island / small islet: typically 120–300.\n" +
                "- Village / rolling hills / canyon: typically 400–900.\n" +
                "- Large mountain range / wilderness: use 0 (meaning keep 1 pixel = 1 meter at generation resolution).\n" +
                "- If the user already states meters or kilometers, convert and use that value.\n" +
                "- Prefer playable near-scene scale over geographic realism unless the user asks for kilometers.\n" +
                "User request:\n" + userText;
        }

        private static string PostJson(string url, string jsonBody, string token, string orgId, string source, int timeoutSeconds)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Post, url))
            {
                req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                ApplyAuth(req, token, orgId, source);
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                using (var response = Http.SendAsync(req, cts.Token).Result)
                {
                    string body = response.Content.ReadAsStringAsync().Result;
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {body}");
                    return body;
                }
            }
        }

        private static string GetJson(string url, string token, string orgId, string source, int timeoutSeconds)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                ApplyAuth(req, token, orgId, source);
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                using (var response = Http.SendAsync(req, cts.Token).Result)
                {
                    string body = response.Content.ReadAsStringAsync().Result;
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {body}");
                    return body;
                }
            }
        }

        private static void ApplyAuth(HttpRequestMessage req, string token, string orgId, string source)
        {
            if (!string.IsNullOrEmpty(token))
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            if (!string.IsNullOrEmpty(orgId))
                req.Headers.TryAddWithoutValidation("orgId", orgId);
            req.Headers.TryAddWithoutValidation("source", source);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
        }
    }

    /// <summary>模态输入较长边米数。</summary>
    internal sealed class TerrainWorldSizeInputDialog : EditorWindow
    {
        private string _message;
        private string _fieldText;
        private bool _ok;
        private bool _closed;
        private float _result;

        public static bool Show(string title, string message, float defaultMeters, out float meters)
        {
            meters = 0f;
            var win = CreateInstance<TerrainWorldSizeInputDialog>();
            win.titleContent = new GUIContent(title ?? "Terrain size");
            win._message = message ?? "";
            win._fieldText = defaultMeters.ToString("0", CultureInfo.InvariantCulture);
            win.minSize = new Vector2(420, 180);
            win.maxSize = new Vector2(520, 280);
            win.ShowModalUtility();
            meters = win._result;
            return win._ok;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(_message ?? "", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(TJGeneratorsL10n.L("较长边（米）"), GUILayout.Width(100));
            _fieldText = EditorGUILayout.TextField(_fieldText);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(TJGeneratorsL10n.L("取消"), GUILayout.Width(80), GUILayout.Height(24)))
            {
                _ok = false;
                _closed = true;
                Close();
            }
            if (GUILayout.Button(TJGeneratorsL10n.L("确定"), GUILayout.Width(80), GUILayout.Height(24)))
            {
                if (!float.TryParse(_fieldText?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) &&
                    !float.TryParse(_fieldText?.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out v))
                {
                    EditorUtility.DisplayDialog(
                        TJGeneratorsL10n.L("地形世界尺寸"),
                        TJGeneratorsL10n.L("请输入有效数字（米）。填 0 表示按像素 1:1。"),
                        TJGeneratorsL10n.L("确定"));
                    return;
                }
                _result = v;
                _ok = true;
                _closed = true;
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void OnDestroy()
        {
            if (!_closed)
                _ok = false;
        }
    }
}
#endif
