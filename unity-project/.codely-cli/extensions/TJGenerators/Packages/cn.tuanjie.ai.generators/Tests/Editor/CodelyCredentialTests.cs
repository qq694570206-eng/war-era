using System;
using System.IO;
using Codely.Newtonsoft.Json.Linq;
using NUnit.Framework;
using TJGenerators.AssetSearch;
using Unity.UniAsset.Manager.Editor.InternalBridge;

namespace TJGenerators.Editor.Tests
{
    public class CodelyCredentialTests
    {
        [Test]
        public void CurrentCodelyAccountPrecedesEditorAndOwnsOrgSelection()
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string previousHome = Environment.GetEnvironmentVariable("CODELY_CLI_HOME");
            string previousToken = Environment.GetEnvironmentVariable("CODELY_TOKEN");
            Directory.CreateDirectory(root);
            try
            {
                Environment.SetEnvironmentVariable("CODELY_CLI_HOME", root);
                Environment.SetEnvironmentVariable("CODELY_TOKEN", null);
                string path = Path.Combine(root, "oauth_creds.json");
                var creds = new JObject { ["access_token"] = "first-token", ["user_id"] = "first" };
                File.WriteAllText(path, creds.ToString());
                File.WriteAllText(Path.Combine(root, "org.json"),
                    new JObject { ["accounts"] = new JObject { ["first"] = new JObject { ["currentOrgId"] = "first-org" } } }.ToString());
                var session = UnityConnectSession.instance;
                Assert.AreEqual("first-token", session.GetAccessToken());
                Assert.AreEqual("first-token", CodelyTokenProvider.GetToken());
                Assert.AreEqual("first-org", session.GetOrgId("first-token"));

                Environment.SetEnvironmentVariable("CODELY_TOKEN", "explicit-token");
                Assert.AreEqual("explicit-token", session.GetAccessToken());
                Assert.AreEqual("explicit-token", CodelyTokenProvider.GetToken());
                Assert.IsEmpty(session.GetOrgId("explicit-token"));

                Environment.SetEnvironmentVariable("CODELY_TOKEN", null);
                creds["access_token"] = "second-token";
                creds["user_id"] = "second";
                File.WriteAllText(path, creds.ToString());
                Assert.AreEqual("second-token", CodelyTokenProvider.GetToken());
                Assert.IsEmpty(session.GetOrgId("first-token"));
                creds["expiry_date"] = 1;
                File.WriteAllText(path, creds.ToString());
                Assert.IsNull(UnityConnectSession.ReadCodelyToken());
                File.Delete(path);
                Assert.IsNull(UnityConnectSession.ReadCodelyToken());
            }
            finally
            {
                Environment.SetEnvironmentVariable("CODELY_TOKEN", previousToken);
                Environment.SetEnvironmentVariable("CODELY_CLI_HOME", previousHome);
                File.Delete(Path.Combine(root, "oauth_creds.json"));
                File.Delete(Path.Combine(root, "org.json"));
                Directory.Delete(root);
            }
        }
    }
}
