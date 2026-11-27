#if UNITY_EDITOR
using System;
using Codely.Newtonsoft.Json.Linq;
using TJGenerators.Config;
using Unity.UniAsset.Manager.Editor.InternalBridge;

namespace TJGenerators.AssetSearch
{
    /// <summary>
    /// Codely access_token 换票与缓存入口。UI / CustomTool 只接触 <see cref="GetToken"/> 与 <see cref="Invalidate"/>。
    /// 鉴权失败时抛 <see cref="InvalidOperationException"/>，消息以 <c>AUTH_REQUIRED:</c> 前缀开头，调用方原样呈现给用户。
    /// </summary>
    public static class CodelyTokenProvider
    {
        private const string TokenExchangeEndpoint = "/auth/exchange-with-unity-token";

        private static string _codelyAccessToken;
        private static DateTime _codelyTokenExpiry = DateTime.MinValue;

        private static string _exchangeSourceToken;

        internal static string GetCachedExchange(string editorToken)
        {
            return !string.IsNullOrEmpty(editorToken) && editorToken == _exchangeSourceToken &&
                DateTime.UtcNow < _codelyTokenExpiry ? _codelyAccessToken : null;
        }

        public static string GetToken()
        {
            string codely = UnityConnectSession.ReadCodelyToken();
            if (!string.IsNullOrEmpty(codely)) return codely;

            string editor = UnityConnectSession.instance.GetRawAccessToken();
            if (string.IsNullOrEmpty(editor))
                throw new InvalidOperationException("AUTH_REQUIRED: Please sign in to Codely or the Unity Editor.");
            return GetCachedExchange(editor) ?? DoTokenExchange(editor, UnityConnectSession.instance.GetUserId());
        }

        public static void Invalidate()
        {
            _exchangeSourceToken = null;
            _codelyAccessToken = null;
            _codelyTokenExpiry = DateTime.MinValue;
        }

        private static string DoTokenExchange(string unityToken, string unityUserId)
        {
            var body = new JObject
            {
                ["unity_access_token"] = unityToken,
                ["unity_user_id"] = unityUserId ?? ""
            };
            string url = ConfigManager.GetCodelyBaseUrl().TrimEnd('/') + TokenExchangeEndpoint;

            string response;
            try
            {
                response = CodelyHttpClient.PostJsonSync(url, body.ToString(), token: "", timeoutSeconds: 10);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"AUTH_REQUIRED: Codely token exchange failed (network error: {ex.Message}). Please sign in to Unity Editor again.");
            }

            JObject data;
            try { data = JObject.Parse(response); }
            catch
            {
                throw new InvalidOperationException(
                    "AUTH_REQUIRED: Codely token exchange returned invalid response. Unity token may be expired, please sign in again.");
            }

            string accessToken = data["access_token"]?.ToString();
            if (string.IsNullOrEmpty(accessToken))
                throw new InvalidOperationException(
                    "AUTH_REQUIRED: Exchange response missing access_token field");

            int expiresIn = data["expires_in"]?.ToObject<int>() ?? 3600;
            _exchangeSourceToken = unityToken;
            _codelyAccessToken = accessToken;
            _codelyTokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);
            return _codelyAccessToken;
        }
    }
}
#endif
