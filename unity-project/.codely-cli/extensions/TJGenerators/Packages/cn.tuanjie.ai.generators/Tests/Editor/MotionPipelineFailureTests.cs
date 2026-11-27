using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TJGenerators.Config;
using TJGenerators.Generators;
using TJGenerators.Pipeline;
using TJGenerators.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace TJGenerators.Editor.Tests
{
    public class MotionPipelineFailureTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private string _folder;
        private string _source;
        private RemoteConfig _previousConfig;

        private sealed class Generator : DynamicGenerator
        {
            public Generator() : base(new GeneratorConfig { id = "motion-test", displayName = "test" }) { }
            public override bool GetAddMotionEnabled() => true;
            public override string GetMotionDescription() => "wave";
        }

        private sealed class Host : HeadlessPipelineHostBase
        {
            public string Error;
            public int Completed;
            public override TJGeneratorsAssetReference GetTargetAsset() => null;
            public override void ShowDialog(string title, string message) => Error = message;
            public override void OnGenerationCompleted(string assetPath) => Completed++;
        }

        private sealed class Transport : IGenerationBackendTransport
        {
            public string State;
            public int Submissions;
            public string ModelUrl;
            public IEnumerator CreateTask(string url, byte[] data, Action<TJTaskResponse> success, Action<string> error)
            { Assert.Fail("must stop before submitting motion"); yield break; }
            public IEnumerator CreateTaskMultipart(string url, MultipartRequestData data, Action<TJTaskResponse> success, Action<string> error)
            {
                Submissions++;
                if (State == "submit-failed") error("injected rig submission failure");
                else success(new TJTaskResponse { taskId = "original-rig-task", status = "queued" });
                yield break;
            }
            public IEnumerator PollStatus(string id, string url, Action<TJTaskStatusResponse> success, Action<string> error)
            {
                Assert.AreEqual("original-rig-task", id);
                success(State == "invalid-response" ? null : new TJTaskStatusResponse
                {
                    taskId = id, status = ModelUrl == null ? State : "completed", error = "injected rig failure",
                    output = ModelUrl == null ? null : new TJTaskOutput { data = new TJTaskOutputData { result = new TJTaskResult { model = ModelUrl } } }
                });
                yield break;
            }
            public IEnumerator DownloadBytes(string url, Action<byte[]> success, Action<string> error)
            { Assert.Fail("unexpected transport download"); yield break; }
        }

        [SetUp]
        public void SetUp()
        {
            var configField = typeof(ConfigManager).GetField("_config", BindingFlags.NonPublic | BindingFlags.Static);
            _previousConfig = (RemoteConfig)configField.GetValue(null);
            configField.SetValue(null, JsonUtility.FromJson<RemoteConfig>(
                "{\"apiBaseUrl\":\"https://example.invalid/api/editor/\",\"pollConfig\":{\"maxRetries\":1,\"intervalSeconds\":0.001},\"generators\":["
                + "{\"id\":\"unirig\",\"responseMapping\":{\"downloadUrlPath\":\"model\"},\"endpoints\":[{\"key\":\"default\",\"value\":\"test/rig\"}]},"
                + "{\"id\":\"hunyuan-motion\",\"endpoints\":[{\"key\":\"default\",\"value\":\"test/motion\"}]}]}"));
            string name = "MotionFailureTest_" + Guid.NewGuid().ToString("N");
            _folder = "Assets/" + name;
            AssetDatabase.CreateFolder("Assets", name);
            _source = Path.GetFullPath(_folder + "/source.obj");
            File.WriteAllText(_source, "o triangle\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
            AssetDatabase.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            typeof(ConfigManager).GetField("_config", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, _previousConfig);
            AssetDatabase.DeleteAsset(_folder);
        }

        private IEnumerator VerifyFailure(string state, string detail)
        {
            var host = new Host();
            var generator = new Generator { IsRunning = true };
            var transport = new Transport { State = state };
            if (state == "download-failed")
            {
                transport.ModelUrl = new Uri(Path.GetFullPath(_folder + "/missing.obj")).AbsoluteUri;
                File.WriteAllText(Path.GetFullPath(_folder + "/downloaded_rigged.obj"), File.ReadAllText(_source));
            }
            if (state == "invalid-avatar") transport.ModelUrl = new Uri(_source).AbsoluteUri;
            var pipeline = new GenerationPipeline(host, ConfigType.Generator);
            typeof(GenerationPipeline).GetField("_transport", PrivateInstance).SetValue(pipeline, transport);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[GenerationPipeline\\] 动作后处理"));
            yield return RunInEditMode((IEnumerator)typeof(GenerationPipeline).GetMethod("DownloadModel", PrivateInstance)
                .Invoke(pipeline, new object[] { generator, new Uri(_source).AbsoluteUri, _folder + "/downloaded.obj", false, null }));
            Assert.AreEqual(0, host.Completed, "failed add_motion must not report completion");
            Assert.IsFalse(generator.IsRunning);
            StringAssert.Contains(detail, host.Error);
            StringAssert.Contains("已保留模型:", host.Error);
            Assert.IsTrue(File.Exists(_source), "static source was discarded");
            Assert.AreEqual(1, transport.Submissions);
        }

        // EditMode tests yield null while waiting for the same async operations
        // used by EditorCoroutines in production.
        private static IEnumerator RunInEditMode(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0)
            {
                var current = stack.Peek();
                if (!current.MoveNext()) { stack.Pop(); continue; }
                if (current.Current is IEnumerator nested) stack.Push(nested);
                else if (current.Current is AsyncOperation operation)
                    while (!operation.isDone) yield return null;
                else yield return null;
            }
        }

        [UnityTest] public IEnumerator RigSubmissionFailure() => VerifyFailure("submit-failed", "UniRig 提交失败");
        [UnityTest] public IEnumerator RigPollingFailure() => VerifyFailure("failed", "original-rig-task");
        [UnityTest] public IEnumerator RigCancellation() => VerifyFailure("cancelled", "original-rig-task");
        [UnityTest] public IEnumerator RigTimeout() => VerifyFailure("running", TJGeneratorsL10n.L("轮询超时"));
        [UnityTest] public IEnumerator InvalidRigResponse() => VerifyFailure("invalid-response", TJGeneratorsL10n.L("无效响应"));
        [UnityTest] public IEnumerator CompletedRigWithoutUrl() => VerifyFailure("completed", "无模型 URL");
        [UnityTest] public IEnumerator RigDownloadFailureRejectsStaleFile() => VerifyFailure("download-failed", "绑骨模型下载失败");
        [UnityTest] public IEnumerator InvalidHumanoidAvatar() => VerifyFailure("invalid-avatar", "无有效 Humanoid Avatar");
    }
}
