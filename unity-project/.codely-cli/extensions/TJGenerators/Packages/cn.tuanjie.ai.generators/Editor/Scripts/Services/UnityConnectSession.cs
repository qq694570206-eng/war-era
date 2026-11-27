using UnityEditor;
using System.IO;
using System.Reflection;
using Codely.Newtonsoft.Json.Linq;
using TJGenerators.AssetSearch;

namespace Unity.UniAsset.Manager.Editor.InternalBridge
{
    public class UnityConnectSession
    {
        static UnityConnectSession _instance = new UnityConnectSession();
        private const string CodelyCliDir = ".codely-cli";
        private const string OAuthCredsFile = "oauth_creds.json";
        private const string OrgFile = "org.json";
        
        public static UnityConnectSession instance
        {
            get => _instance;
        }
        
        public string GetAccessToken()
        {
            string codely = ReadCodelyToken();
            if (!string.IsNullOrEmpty(codely))
                return codely;

            string editor = GetRawAccessToken();
            return CodelyTokenProvider.GetCachedExchange(editor) ?? editor;
        }

        public string GetOrgId() => GetOrgId(GetAccessToken());

        // The org selection belongs to the account in the same CLI credential file.
        public string GetOrgId(string accessToken)
        {
            if (string.IsNullOrEmpty(accessToken)) return "";
            try
            {
                string dir = GetCodelyDirectory();
                var creds = ReadJson(Path.Combine(dir, OAuthCredsFile));
                if ((string)creds?["access_token"] != accessToken) return "";
                string userId = (string)creds["user_id"];
                if (string.IsNullOrEmpty(userId)) return "";
                var orgs = ReadJson(Path.Combine(dir, OrgFile));
                return (string)orgs?["accounts"]?[userId]?["currentOrgId"] ?? "";
            }
            catch (System.Exception) { return ""; }
        }

        /// <summary>
        /// 获取原始的 Unity Connect access token，不含 fallback 逻辑。
        /// 仅供 CodelyTokenProvider 换票判断使用，其他场景请用 GetAccessToken()。
        /// </summary>
        public string GetRawAccessToken()
        {
            var unityConnectType = System.Type.GetType("UnityEditor.Connect.UnityConnect,UnityEditor");
            if (unityConnectType != null)
            {
                var instanceProperty = unityConnectType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty != null)
                {
                    var unityConnectInstance = instanceProperty.GetValue(null);
                    var getAccessTokenMethod = unityConnectType.GetMethod("GetAccessToken", BindingFlags.Public | BindingFlags.Instance);
                    if (getAccessTokenMethod != null)
                    {
                        string token = (string)getAccessTokenMethod.Invoke(unityConnectInstance, null);
                        if (!string.IsNullOrEmpty(token))
                            return token;
                    }
                }
            }
            return "";
        }

        public string GetUserId()
        {
            var unityConnectType = System.Type.GetType("UnityEditor.Connect.UnityConnect,UnityEditor");
            if (unityConnectType != null)
            {
                var instanceProperty = unityConnectType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty != null)
                {
                    var unityConnectInstance = instanceProperty.GetValue(null);
                    var getUserIdMethod = unityConnectType.GetMethod("GetUserId", BindingFlags.Public | BindingFlags.Instance);
                    if (getUserIdMethod != null)
                    {
                        return (string)getUserIdMethod.Invoke(unityConnectInstance, null);
                    }
                }
            }
            return "";
        }

        public string GetEnvironment()
        {
            var unityConnectType = System.Type.GetType("UnityEditor.Connect.UnityConnect,UnityEditor");
            if (unityConnectType != null)
            {
                var instanceProperty = unityConnectType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty != null)
                {
                    var unityConnectInstance = instanceProperty.GetValue(null);
                    var getEnvironmentMethod = unityConnectType.GetMethod("GetEnvironment", BindingFlags.Public | BindingFlags.Instance);
                    if (getEnvironmentMethod != null)
                    {
                        return (string)getEnvironmentMethod.Invoke(unityConnectInstance, null);
                    }
                }
            }
            return "";
        }

        public void ShowLogin()
        {
            var unityConnectType = System.Type.GetType("UnityEditor.Connect.UnityConnect,UnityEditor");
            if (unityConnectType != null)
            {
                var instanceProperty = unityConnectType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty != null)
                {
                    var unityConnectInstance = instanceProperty.GetValue(null);
                    var showLoginMethod = unityConnectType.GetMethod("ShowLogin", BindingFlags.Public | BindingFlags.Instance);
                    showLoginMethod?.Invoke(unityConnectInstance, null);
                }
            }
        }
        
        public static void OpenAuthorizedURLInWebBrowser(string url)
        {
            var unityConnectType = System.Type.GetType("UnityEditor.Connect.UnityConnect,UnityEditor");
            if (unityConnectType != null)
            {
                var instanceProperty = unityConnectType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty != null)
                {
                    var unityConnectInstance = instanceProperty.GetValue(null);
                    var openURLMethod = unityConnectType.GetMethod("OpenAuthorizedURLInWebBrowser", BindingFlags.Public | BindingFlags.Instance);
                    openURLMethod?.Invoke(unityConnectInstance, new object[] { url });
                }
            }
        }

        private static string GetCodelyDirectory()
        {
            string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home)) home = System.Environment.GetEnvironmentVariable("HOME");
            string configured = System.Environment.GetEnvironmentVariable("CODELY_CLI_HOME")?.Trim();
            if (string.IsNullOrEmpty(home)) home = Path.GetTempPath();
            if (string.IsNullOrEmpty(configured)) return Path.Combine(home, CodelyCliDir);
            if (configured == "~") return home;
            if (configured.StartsWith("~/") || configured.StartsWith(@"~\")) return Path.Combine(home, configured.Substring(2));
            return Path.GetFullPath(configured);
        }

        private static JObject ReadJson(string path) =>
            File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null;

        internal static string ReadCodelyToken()
        {
            string explicitToken = System.Environment.GetEnvironmentVariable("CODELY_TOKEN")?.Trim();
            if (!string.IsNullOrEmpty(explicitToken)) return explicitToken;
            try
            {
                var creds = ReadJson(Path.Combine(GetCodelyDirectory(), OAuthCredsFile));
                long? expires = (long?)creds?["expiry_date"];
                if (expires.HasValue && expires.Value <= System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                    return null;
                return ((string)creds?["access_token"])?.Trim();
            }
            catch (System.Exception) { return null; }
        }
    }
}
