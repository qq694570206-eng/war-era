using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Codely.Newtonsoft.Json.Linq;
using NUnit.Framework;
using TJGenerators.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityTcp.Editor.Tools;

namespace TJGenerators.Editor.Tests
{
    public class TerrainApplyRegressionTests
    {
        private string _folder;
        private Scene _scene;
        private int _initialRootCount;
        private readonly List<string> _tasks = new List<string>();
        private readonly List<string> _legacyTasks = new List<string>();

        [SetUp]
        public void SetUp()
        {
            // Use the runner's scene, which may be an unsaved empty scene in batch mode.
            // Cleanup only the Terrain objects whose data belongs to this fixture.
            _scene = SceneManager.GetActiveScene();
            _initialRootCount = _scene.rootCount;
            string name = "TerrainApplyRegression_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            _folder = "Assets/" + name;
            var image = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color[256];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.Lerp(Color.black, Color.white, i / 255f);
                image.SetPixels(pixels);
                image.Apply();
                File.WriteAllBytes(PathUtils.ToAbsoluteAssetPath(_folder + "/height.png"), image.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
            AssetDatabase.ImportAsset(_folder + "/height.png");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string id in _tasks) TerrainApplyTaskTracker.RemoveTask(id);
            _tasks.Clear();
            foreach (string id in _legacyTasks) TerrainTaskTracker.RemoveTask(id);
            _legacyTasks.Clear();
            if (_scene.IsValid() && !string.IsNullOrEmpty(_folder))
                foreach (var terrain in Resources.FindObjectsOfTypeAll<Terrain>())
                    if (terrain.gameObject.scene == _scene && AssetDatabase.GetAssetPath(terrain.terrainData).StartsWith(_folder + "/", StringComparison.Ordinal))
                        UnityEngine.Object.DestroyImmediate(terrain.gameObject);
            if (!string.IsNullOrEmpty(_folder)) AssetDatabase.DeleteAsset(_folder);
        }

        private JObject Request() => new JObject
        {
            ["heightmap_path"] = _folder + "/height.png", ["prompt"] = "island",
            ["use_default_options"] = false, ["sketch_reconstruction"] = false,
            ["median3x3"] = false, ["gaussian_blur"] = false, ["thermal_erosion"] = false,
            ["percentile_normalize"] = false
        };

        private JObject Track(object response)
        {
            var result = JObject.FromObject(response);
            if (result["apply_task_id"] != null) _tasks.Add((string)result["apply_task_id"]);
            return result;
        }

        private JObject ApplyThroughBridge(JObject request)
        {
            var response = Track(ExecuteCustomTool.HandleCommand(new JObject
            {
                ["tool_name"] = "apply_terrain_heightmap", ["parameters"] = request
            }));
            // Keep the raw Bridge response for assertions. Track a mistakenly wrapped
            // task too, so a failing contract regression still cleans up its record.
            string nestedId = (string)response["data"]?["apply_task_id"];
            if (!string.IsNullOrEmpty(nestedId)) _tasks.Add(nestedId);
            return response;
        }

        private IEnumerator WaitForTask(string id, string expectedStatus = "completed")
        {
            double deadline = EditorApplication.timeSinceStartup + 20;
            while (TerrainApplyTaskTracker.GetTask(id)?.Status == "processing" && EditorApplication.timeSinceStartup < deadline)
                yield return null;
            var task = TerrainApplyTaskTracker.GetTask(id);
            Assert.NotNull(task);
            Assert.AreEqual(expectedStatus, task.Status, task.ErrorMessage);
        }

        private int CountFixtureTerrains(Scene scene)
        {
            int count = 0;
            foreach (var terrain in Resources.FindObjectsOfTypeAll<Terrain>())
                if (terrain.gameObject.scene == scene &&
                    AssetDatabase.GetAssetPath(terrain.terrainData).StartsWith(_folder + "/", StringComparison.Ordinal))
                    count++;
            return count;
        }

        private sealed class UntitledRunnerSceneScope : IDisposable
        {
            private readonly TerrainApplyRegressionTests _fixture;
            private readonly string _backupPath;
            private Scene _backupScene;
            public Scene Scene { get; private set; }

            public UntitledRunnerSceneScope(TerrainApplyRegressionTests fixture)
            {
                _fixture = fixture;
                Scene = fixture._scene;
                // EditModeLauncher supplies an untitled runner scene and restores
                // the user's original scene setup after the run. Preserve its
                // contents here without closing any other loaded user scenes.
                Assert.IsEmpty(Scene.path, "First-save regressions require the Test Runner's untitled scene.");
                _backupPath = fixture._folder + "/RunnerBackup.unity";
                Assert.IsTrue(EditorSceneManager.SaveScene(Scene, _backupPath, saveAsCopy: true));
            }

            private void OpenBackup()
            {
                if (!_backupScene.IsValid() || !_backupScene.isLoaded)
                    _backupScene = EditorSceneManager.OpenScene(_backupPath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(_backupScene);
            }

            public void CloseAndReopen(string path)
            {
                OpenBackup();
                Assert.IsTrue(EditorSceneManager.CloseScene(Scene, true));
                Scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                Assert.IsTrue(SceneManager.SetActiveScene(Scene));
            }

            public void Dispose()
            {
                OpenBackup();
                if (Scene.IsValid() && Scene.isLoaded) EditorSceneManager.CloseScene(Scene, true);
                var restored = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                Assert.IsTrue(restored.IsValid(), "Restore the runner to an untitled scene.");
                foreach (var root in _backupScene.GetRootGameObjects())
                    SceneManager.MoveGameObjectToScene(root, restored);
                SceneManager.SetActiveScene(restored);
                EditorSceneManager.CloseScene(_backupScene, true);
                _fixture._scene = restored;
            }
        }

        [UnityTest]
        public IEnumerator FirstSaveAfterSaveAsCopyReusesApplicationAfterReopenAndRestore()
        {
            using (var scope = new UntitledRunnerSceneScope(this))
            {
                var request = Request();
                request["world_size_meters"] = 0;
                var first = ApplyThroughBridge(request);
                Assert.IsTrue((bool)first["success"], first.ToString());
                string id = (string)first["apply_task_id"];
                yield return WaitForTask(id);
                string dataPath = TerrainApplyTaskTracker.GetTask(id).TerrainDataPath;
                var unsavedHandle = scope.Scene.handle;

                // A saved copy must not consume the original scene's first-save alias.
                Assert.IsTrue(EditorSceneManager.SaveScene(scope.Scene, _folder + "/Copy.unity", saveAsCopy: true));
                Assert.IsEmpty(scope.Scene.path);
                Assert.AreEqual(unsavedHandle, scope.Scene.handle);
                string savedPath = _folder + "/FirstSave.unity";
                Assert.IsTrue(EditorSceneManager.SaveScene(scope.Scene, savedPath));
                // Deliberately do not retry before closing: migration must happen
                // at the save event, not only on a later request to the same handle.
                scope.CloseAndReopen(savedPath);
                Assert.AreNotEqual(unsavedHandle, scope.Scene.handle);
                TerrainApplyTaskTracker.RestoreTasks();
                var retry = ApplyThroughBridge(request);
                yield return WaitForTask((string)(retry["apply_task_id"] ?? retry["data"]?["apply_task_id"]));

                Assert.AreEqual(id, (string)retry["apply_task_id"]);
                Assert.IsNull(retry["data"], retry.ToString());
                Assert.AreEqual("completed", (string)retry["status"]);
                Assert.IsTrue((bool)retry["already_applied"]);
                Assert.AreEqual(dataPath, (string)retry["terrain_data_path"]);
                Assert.AreEqual(1, CountFixtureTerrains(scope.Scene));
            }
        }

        [UnityTest]
        public IEnumerator FirstSaveWhileEstimatePendingKeepsTheSameApplication()
        {
            using (var scope = new UntitledRunnerSceneScope(this))
            {
                var pending = new TaskCompletionSource<TerrainWorldSizeEstimator.EstimateResult>();
                int calls = 0;
                Func<string, string, string, Task<TerrainWorldSizeEstimator.EstimateResult>> estimate = (p, t, o) =>
                {
                    calls++;
                    return pending.Task;
                };
                var request = Request();
                JObject first = null;
                JObject retryWhilePending = null;
                bool saved = false;
                try
                {
                    first = Track(GenerateTerrainTool.ApplyTerrainHeightmapCore(request, estimate));
                    saved = EditorSceneManager.SaveScene(scope.Scene, _folder + "/PendingFirstSave.unity");
                    retryWhilePending = Track(GenerateTerrainTool.ApplyTerrainHeightmapCore(request, estimate));
                }
                finally
                {
                    // Release every task even when setup or an assertion fails.
                    pending.TrySetResult(new TerrainWorldSizeEstimator.EstimateResult(128, "llm", "test"));
                }
                string id = (string)first["apply_task_id"];
                string pendingId = (string)retryWhilePending["apply_task_id"];
                yield return WaitForTask(id);
                if (pendingId != id) yield return WaitForTask(pendingId);
                var completed = ApplyThroughBridge(request);
                yield return WaitForTask((string)(completed["apply_task_id"] ?? completed["data"]?["apply_task_id"]));

                Assert.IsTrue(saved);
                Assert.AreEqual(id, pendingId);
                Assert.AreEqual("processing", (string)retryWhilePending["status"]);
                Assert.IsTrue((bool)retryWhilePending["reused"]);
                Assert.AreEqual(1, calls);
                Assert.AreEqual(id, (string)completed["apply_task_id"]);
                Assert.IsTrue((bool)completed["already_applied"]);
                Assert.AreEqual(1, CountFixtureTerrains(scope.Scene));
            }
        }

        [UnityTest]
        public IEnumerator SavingExistingSceneAsAnotherAssetKeepsApplicationsDistinct()
        {
            string pathA = _folder + "/SaveAsA.unity";
            string pathB = _folder + "/SaveAsB.unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(_scene, pathA, saveAsCopy: true));
            Scene reviewScene = EditorSceneManager.OpenScene(pathA, OpenSceneMode.Additive);
            try
            {
                Assert.IsTrue(SceneManager.SetActiveScene(reviewScene));
                var request = Request();
                request["world_size_meters"] = 0;
                var first = ApplyThroughBridge(request);
                string firstId = (string)first["apply_task_id"];
                yield return WaitForTask(firstId);
                string firstDataPath = TerrainApplyTaskTracker.GetTask(firstId).TerrainDataPath;
                Assert.IsTrue(EditorSceneManager.SaveScene(reviewScene));
                var originalHandle = reviewScene.handle;

                Assert.IsTrue(EditorSceneManager.SaveScene(reviewScene, pathB));
                Assert.AreEqual(originalHandle, reviewScene.handle, "Save As retains the scene instance but changes the asset identity.");
                foreach (var terrain in Resources.FindObjectsOfTypeAll<Terrain>())
                    if (terrain.gameObject.scene == reviewScene &&
                        AssetDatabase.GetAssetPath(terrain.terrainData).StartsWith(_folder + "/", StringComparison.Ordinal))
                        UnityEngine.Object.DestroyImmediate(terrain.gameObject);
                Assert.IsTrue(EditorSceneManager.SaveScene(reviewScene));
                var other = ApplyThroughBridge(request);
                string otherId = (string)other["apply_task_id"];
                yield return WaitForTask(otherId);
                Assert.AreNotEqual(firstId, otherId);
                Assert.AreNotEqual(firstDataPath, TerrainApplyTaskTracker.GetTask(otherId).TerrainDataPath);
                Assert.AreEqual(1, CountFixtureTerrains(reviewScene));

                SceneManager.SetActiveScene(_scene);
                Assert.IsTrue(EditorSceneManager.CloseScene(reviewScene, true));
                reviewScene = EditorSceneManager.OpenScene(pathA, OpenSceneMode.Additive);
                Assert.IsTrue(SceneManager.SetActiveScene(reviewScene));
                var originalRetry = ApplyThroughBridge(request);
                yield return WaitForTask((string)(originalRetry["apply_task_id"] ?? originalRetry["data"]?["apply_task_id"]));
                Assert.AreEqual(firstId, (string)originalRetry["apply_task_id"]);
                Assert.AreEqual(firstDataPath, (string)originalRetry["terrain_data_path"]);
                Assert.IsTrue((bool)originalRetry["already_applied"]);
                Assert.AreEqual(1, CountFixtureTerrains(reviewScene));
            }
            finally
            {
                SceneManager.SetActiveScene(_scene);
                if (reviewScene.IsValid() && reviewScene.isLoaded) EditorSceneManager.CloseScene(reviewScene, true);
            }
        }

        [UnityTest]
        public IEnumerator BridgeReturnsTopLevelFieldsForPendingAndCompletedRetries()
        {
            var request = Request();
            request["world_size_meters"] = 0;
            var first = ApplyThroughBridge(request);
            Assert.IsTrue((bool)first["success"], first.ToString());
            string id = (string)first["apply_task_id"];
            Assert.IsNotEmpty(id, "The initial Bridge response must expose apply_task_id.");
            var pending = ApplyThroughBridge(request);
            yield return WaitForTask(id);
            var completed = ApplyThroughBridge(request);
            // Drain even an incorrectly duplicated/wrapped task before asserting.
            yield return WaitForTask((string)(completed["apply_task_id"] ?? completed["data"]?["apply_task_id"]));

            Assert.IsNull(first["data"], first.ToString());
            Assert.AreEqual("processing", (string)first["status"]);
            Assert.IsTrue((bool)pending["success"], pending.ToString());
            Assert.IsNull(pending["data"], "Bridge must not add a wrapper when an application is reused.");
            Assert.AreEqual(id, (string)pending["apply_task_id"]);
            Assert.AreEqual("processing", (string)pending["status"]);
            Assert.IsTrue((bool)pending["reused"]);
            Assert.IsFalse((bool)pending["already_applied"]);
            Assert.IsTrue((bool)completed["success"], completed.ToString());
            Assert.IsNull(completed["data"], completed.ToString());
            Assert.AreEqual(id, (string)completed["apply_task_id"]);
            Assert.AreEqual("completed", (string)completed["status"]);
            Assert.IsTrue((bool)completed["reused"]);
            Assert.IsTrue((bool)completed["already_applied"]);
            Assert.AreEqual(TerrainApplyTaskTracker.GetTask(id).TerrainDataPath, (string)completed["terrain_data_path"]);
            Assert.AreEqual(1, CountFixtureTerrains(_scene));
        }

        [UnityTest]
        public IEnumerator BridgeLegacyApplicationSurvivesSceneRoundTripAndTrackerRestore()
        {
            string pathA = _folder + "/LegacyA.unity";
            string pathB = _folder + "/LegacyB.unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(_scene, pathA, saveAsCopy: true));
            Scene sceneA = EditorSceneManager.OpenScene(pathA, OpenSceneMode.Additive);
            Scene sceneB = default;
            try
            {
                Assert.IsTrue(SceneManager.SetActiveScene(sceneA));
                string legacyId = TerrainTaskTracker.CreateTask("pre-upgrade cross-scene fixture");
                _legacyTasks.Add(legacyId);
                TerrainTaskTracker.MarkTaskCompleted(legacyId, _folder + "/height.png");
                var request = Request();
                request["task_id"] = legacyId;
                request["world_size_meters"] = 0;
                var first = ApplyThroughBridge(request);
                Assert.IsTrue((bool)first["success"], first.ToString());
                string originalId = (string)first["apply_task_id"];
                yield return WaitForTask(originalId);
                string originalDataPath = TerrainApplyTaskTracker.GetTask(originalId).TerrainDataPath;
                Assert.IsTrue(EditorSceneManager.SaveScene(sceneA));

                // Simulate an applied task created before apply-task tracking existed.
                TerrainApplyTaskTracker.RemoveTask(originalId);
                var recovered = ApplyThroughBridge(request);
                Assert.IsTrue((bool)recovered["success"], recovered.ToString());
                Assert.IsTrue((bool)recovered["already_applied"], recovered.ToString());

                // Opening a saved copy also works when the batch runner owns an
                // untitled scene, which prevents creating another untitled scene.
                Assert.IsTrue(EditorSceneManager.SaveScene(sceneA, pathB, saveAsCopy: true));
                sceneB = EditorSceneManager.OpenScene(pathB, OpenSceneMode.Additive);
                Assert.IsTrue(SceneManager.SetActiveScene(sceneB));
                foreach (var terrain in Resources.FindObjectsOfTypeAll<Terrain>())
                    if (terrain.gameObject.scene == sceneB &&
                        AssetDatabase.GetAssetPath(terrain.terrainData).StartsWith(_folder + "/", StringComparison.Ordinal))
                        UnityEngine.Object.DestroyImmediate(terrain.gameObject);
                Assert.AreEqual(0, CountFixtureTerrains(sceneB));
                Assert.IsTrue(EditorSceneManager.SaveScene(sceneB));
                var other = ApplyThroughBridge(request);
                Assert.IsTrue((bool)other["success"], other.ToString());
                string otherId = (string)other["apply_task_id"];
                yield return WaitForTask(otherId);
                string otherDataPath = TerrainApplyTaskTracker.GetTask(otherId).TerrainDataPath;
                Assert.AreNotEqual(originalDataPath, otherDataPath);
                Assert.AreEqual(otherDataPath, TerrainTaskTracker.GetTask(legacyId).TerrainDataPath);

                // The backfilled record must be persisted, not merely cached until
                // the next script/domain reload.
                TerrainApplyTaskTracker.RestoreTasks();
                Assert.IsTrue(SceneManager.SetActiveScene(sceneA));
                var retry = ApplyThroughBridge(request);
                Assert.IsTrue((bool)retry["success"], retry.ToString());
                yield return WaitForTask((string)(retry["apply_task_id"] ?? retry["data"]?["apply_task_id"]));
                Assert.AreEqual(1, CountFixtureTerrains(sceneA));
                Assert.AreEqual(1, CountFixtureTerrains(sceneB));
                Assert.IsNull(recovered["data"], recovered.ToString());
                Assert.IsNotEmpty((string)recovered["apply_task_id"], "The legacy result must receive a persisted apply-task record.");
                Assert.AreEqual("completed", (string)recovered["status"]);
                Assert.AreEqual(originalDataPath, (string)recovered["terrain_data_path"]);
                Assert.IsNull(retry["data"], retry.ToString());
                Assert.AreEqual((string)recovered["apply_task_id"], (string)retry["apply_task_id"]);
                Assert.AreEqual("completed", (string)retry["status"]);
                Assert.IsTrue((bool)retry["already_applied"]);
                Assert.AreEqual(originalDataPath, (string)retry["terrain_data_path"]);
            }
            finally
            {
                SceneManager.SetActiveScene(_scene);
                if (sceneB.IsValid() && sceneB.isLoaded) EditorSceneManager.CloseScene(sceneB, true);
                if (sceneA.IsValid() && sceneA.isLoaded) EditorSceneManager.CloseScene(sceneA, true);
            }
        }

        [UnityTest]
        public IEnumerator BridgeLegacyHistorySurvivesLeavingSceneBeforeFirstRetry()
        {
            string pathA = _folder + "/LegacyHistoryA.unity";
            string pathB = _folder + "/LegacyHistoryB.unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(_scene, pathA, saveAsCopy: true));
            Assert.IsTrue(EditorSceneManager.SaveScene(_scene, pathB, saveAsCopy: true));
            Scene sceneA = EditorSceneManager.OpenScene(pathA, OpenSceneMode.Additive);
            Scene sceneB = default;
            try
            {
                Assert.IsTrue(SceneManager.SetActiveScene(sceneA));
                string legacyId = TerrainTaskTracker.CreateTask("pre-upgrade result in a closed scene");
                _legacyTasks.Add(legacyId);
                TerrainTaskTracker.MarkTaskCompleted(legacyId, _folder + "/height.png");
                var request = Request();
                request["task_id"] = legacyId;
                request["world_size_meters"] = 0;
                var first = ApplyThroughBridge(request);
                Assert.IsTrue((bool)first["success"], first.ToString());
                string originalId = (string)first["apply_task_id"];
                yield return WaitForTask(originalId);
                string originalDataPath = TerrainApplyTaskTracker.GetTask(originalId).TerrainDataPath;
                Assert.IsTrue(EditorSceneManager.SaveScene(sceneA));
                TerrainApplyTaskTracker.RemoveTask(originalId);

                // No fallback call in A before leaving: applying to B must retain
                // the old result even though A is closed and has no apply record.
                Assert.IsTrue(SceneManager.SetActiveScene(_scene));
                Assert.IsTrue(EditorSceneManager.CloseScene(sceneA, true));
                sceneB = EditorSceneManager.OpenScene(pathB, OpenSceneMode.Additive);
                Assert.IsTrue(SceneManager.SetActiveScene(sceneB));
                Assert.AreEqual(0, CountFixtureTerrains(sceneB));
                var other = ApplyThroughBridge(request);
                Assert.IsTrue((bool)other["success"], other.ToString());
                string otherId = (string)other["apply_task_id"];
                yield return WaitForTask(otherId);
                string otherDataPath = TerrainApplyTaskTracker.GetTask(otherId).TerrainDataPath;
                Assert.AreNotEqual(originalDataPath, otherDataPath);

                // Discard only the legacy task's memory cache and restore from its
                // serialized session record. This verifies history serialization.
                var cached = TerrainTaskTracker.GetTask(legacyId);
                var storeField = typeof(TerrainTaskTracker).GetField("Store", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(storeField);
                object store = storeField.GetValue(null);
                var forget = store.GetType().GetMethod("RemoveActiveTaskFromMemoryOnly");
                Assert.IsNotNull(forget);
                Assert.IsTrue((bool)forget.Invoke(store, new object[] { legacyId }));
                var restored = TerrainTaskTracker.GetTask(legacyId);
                Assert.IsNotNull(restored);
                Assert.AreNotSame(cached, restored);
                Assert.AreEqual(otherDataPath, restored.TerrainDataPath);

                sceneA = EditorSceneManager.OpenScene(pathA, OpenSceneMode.Additive);
                Assert.IsTrue(SceneManager.SetActiveScene(sceneA));
                var recovered = ApplyThroughBridge(request);
                Assert.IsTrue((bool)recovered["success"], recovered.ToString());
                yield return WaitForTask((string)(recovered["apply_task_id"] ?? recovered["data"]?["apply_task_id"]));
                Assert.AreEqual(1, CountFixtureTerrains(sceneA));
                Assert.AreEqual(1, CountFixtureTerrains(sceneB));
                Assert.IsNull(recovered["data"], recovered.ToString());
                Assert.AreEqual("completed", (string)recovered["status"]);
                Assert.IsTrue((bool)recovered["already_applied"]);
                Assert.AreEqual(originalDataPath, (string)recovered["terrain_data_path"]);
                Assert.AreEqual(otherDataPath, TerrainTaskTracker.GetTask(legacyId).TerrainDataPath,
                    "Recovering A must not overwrite the current legacy mapping to B.");

                TerrainApplyTaskTracker.RestoreTasks();
                var retry = ApplyThroughBridge(request);
                Assert.AreEqual((string)recovered["apply_task_id"], (string)retry["apply_task_id"]);
                Assert.AreEqual(originalDataPath, (string)retry["terrain_data_path"]);
                Assert.IsTrue((bool)retry["already_applied"]);
            }
            finally
            {
                SceneManager.SetActiveScene(_scene);
                if (sceneB.IsValid() && sceneB.isLoaded) EditorSceneManager.CloseScene(sceneB, true);
                if (sceneA.IsValid() && sceneA.isLoaded) EditorSceneManager.CloseScene(sceneA, true);
            }
        }

        [UnityTest]
        public IEnumerator ReopeningSavedSceneReusesCompletedApplication()
        {
            // Copy the runner scene without changing its path or dirty state. Opening
            // a saved copy also works when the runner already has an untitled scene.
            string scenePath = _folder + "/RetryScene.unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(_scene, scenePath, saveAsCopy: true));
            Scene reviewScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                Assert.IsTrue(SceneManager.SetActiveScene(reviewScene));
                var request = Request();
                request["world_size_meters"] = 0;
                request["session_id"] = "saved-scene-retry";
                request["application_id"] = "original";
                var first = Track(GenerateTerrainTool.ApplyTerrainHeightmap(request));
                Assert.IsTrue((bool)first["success"], first.ToString());
                string firstId = (string)first["apply_task_id"];
                yield return WaitForTask(firstId);
                string firstDataPath = TerrainApplyTaskTracker.GetTask(firstId).TerrainDataPath;
                Assert.AreEqual(1, CountFixtureTerrains(reviewScene));

                var firstHandle = reviewScene.handle;
                Assert.IsTrue(EditorSceneManager.SaveScene(reviewScene, scenePath));
                Assert.IsTrue(SceneManager.SetActiveScene(_scene));
                Assert.IsTrue(EditorSceneManager.CloseScene(reviewScene, true));
                reviewScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                Assert.IsTrue(SceneManager.SetActiveScene(reviewScene));
                Assert.AreNotEqual(firstHandle, reviewScene.handle, "The test must reopen a new scene instance.");
                Assert.AreEqual(1, CountFixtureTerrains(reviewScene));

                var retry = Track(GenerateTerrainTool.ApplyTerrainHeightmap(request));
                Assert.IsTrue((bool)retry["success"], retry.ToString());
                // Drain a wrongly-created task too, so a failing regression leaves no
                // background writer targeting the fixture after cleanup.
                yield return WaitForTask((string)retry["apply_task_id"]);
                Assert.AreEqual(firstId, (string)retry["apply_task_id"]);
                Assert.IsTrue((bool)retry["already_applied"]);
                Assert.AreEqual(firstDataPath, (string)retry["terrain_data_path"]);
                Assert.AreEqual(1, CountFixtureTerrains(reviewScene));
            }
            finally
            {
                SceneManager.SetActiveScene(_scene);
                if (reviewScene.IsValid() && reviewScene.isLoaded)
                    EditorSceneManager.CloseScene(reviewScene, true);
            }
        }

        [UnityTest]
        public IEnumerator LegacyTaskAllowsExplicitCopyAndKeepsEachApplicationRetryStable()
        {
            string legacyId = TerrainTaskTracker.CreateTask("fixture terrain");
            _legacyTasks.Add(legacyId);
            TerrainTaskTracker.MarkTaskCompleted(legacyId, _folder + "/height.png");
            var request = Request();
            request["task_id"] = legacyId;
            request["world_size_meters"] = 0;

            var original = Track(GenerateTerrainTool.ApplyTerrainHeightmap(request));
            Assert.IsTrue((bool)original["success"], original.ToString());
            string originalId = (string)original["apply_task_id"];
            yield return WaitForTask(originalId);
            string originalDataPath = TerrainApplyTaskTracker.GetTask(originalId).TerrainDataPath;
            Assert.AreEqual("applied", TerrainTaskTracker.GetTask(legacyId).Status);

            var originalRetry = Track(GenerateTerrainTool.ApplyTerrainHeightmap(request));
            Assert.AreEqual(originalId, (string)originalRetry["apply_task_id"]);
            Assert.IsTrue((bool)originalRetry["already_applied"]);
            Assert.AreEqual(1, CountFixtureTerrains(_scene));

            var copyRequest = (JObject)request.DeepClone();
            copyRequest["application_id"] = "intentional-copy";
            var copy = Track(GenerateTerrainTool.ApplyTerrainHeightmap(copyRequest));
            Assert.IsTrue((bool)copy["success"], copy.ToString());
            string copyId = (string)copy["apply_task_id"];
            Assert.NotNull(copyId, "An explicit copy must not be swallowed by the legacy applied-task guard.");
            var pendingRetry = Track(GenerateTerrainTool.ApplyTerrainHeightmap(copyRequest));
            yield return WaitForTask(copyId);
            var completedRetry = Track(GenerateTerrainTool.ApplyTerrainHeightmap(copyRequest));

            Assert.AreNotEqual(originalId, copyId);
            Assert.AreEqual(copyId, (string)pendingRetry["apply_task_id"]);
            Assert.AreEqual("processing", (string)pendingRetry["status"]);
            Assert.IsTrue((bool)pendingRetry["reused"]);
            Assert.AreEqual(copyId, (string)completedRetry["apply_task_id"]);
            Assert.IsTrue((bool)completedRetry["already_applied"]);
            Assert.AreNotEqual(originalDataPath, TerrainApplyTaskTracker.GetTask(copyId).TerrainDataPath);
            Assert.AreEqual(2, CountFixtureTerrains(_scene));

            var originalAfterCopy = Track(GenerateTerrainTool.ApplyTerrainHeightmap(request));
            Assert.AreEqual(originalId, (string)originalAfterCopy["apply_task_id"]);
            Assert.AreEqual(originalDataPath, (string)originalAfterCopy["terrain_data_path"]);
            Assert.IsTrue((bool)originalAfterCopy["already_applied"]);
            Assert.AreEqual(2, CountFixtureTerrains(_scene));
        }

        [UnityTest]
        public IEnumerator LegacyApplicationWithoutApplyRecordKeepsOriginalAfterExplicitCopy()
        {
            string legacyId = TerrainTaskTracker.CreateTask("pre-upgrade fixture terrain");
            _legacyTasks.Add(legacyId);
            TerrainTaskTracker.MarkTaskCompleted(legacyId, _folder + "/height.png");
            var request = Request();
            request["task_id"] = legacyId;
            request["world_size_meters"] = 0;
            var original = Track(GenerateTerrainTool.ApplyTerrainHeightmap(request));
            Assert.IsTrue((bool)original["success"], original.ToString());
            string originalId = (string)original["apply_task_id"];
            yield return WaitForTask(originalId);
            string originalDataPath = TerrainApplyTaskTracker.GetTask(originalId).TerrainDataPath;

            // Old versions retained the applied legacy task but no apply-task record.
            TerrainApplyTaskTracker.RemoveTask(originalId);
            var beforeCopy = Track(GenerateTerrainTool.ApplyTerrainHeightmap(request));
            Assert.IsTrue((bool)beforeCopy["already_applied"], beforeCopy.ToString());
            Assert.AreEqual(originalDataPath, (string)beforeCopy["terrain_data_path"]);
            Assert.AreEqual(1, CountFixtureTerrains(_scene));

            var copyRequest = (JObject)request.DeepClone();
            copyRequest["application_id"] = "post-upgrade-copy";
            var copy = Track(GenerateTerrainTool.ApplyTerrainHeightmap(copyRequest));
            Assert.IsTrue((bool)copy["success"], copy.ToString());
            string copyId = (string)copy["apply_task_id"];
            Assert.NotNull(copyId, "An explicit copy must start even for a legacy-only original.");
            yield return WaitForTask(copyId);
            Assert.AreNotEqual(originalDataPath, TerrainApplyTaskTracker.GetTask(copyId).TerrainDataPath);

            var afterCopy = Track(GenerateTerrainTool.ApplyTerrainHeightmap(request));
            Assert.IsTrue((bool)afterCopy["already_applied"], afterCopy.ToString());
            Assert.AreEqual(originalDataPath, (string)afterCopy["terrain_data_path"]);
            Assert.AreEqual(originalDataPath, TerrainTaskTracker.GetTask(legacyId).TerrainDataPath);
            Assert.AreEqual(2, CountFixtureTerrains(_scene));
        }

        [UnityTest]
        public IEnumerator PendingEstimateReturnsAndMcpRetriesReusePendingAndCompletedTask()
        {
            var pending = new TaskCompletionSource<TerrainWorldSizeEstimator.EstimateResult>();
            int calls = 0;
            Func<string, string, string, Task<TerrainWorldSizeEstimator.EstimateResult>> estimate = (p, t, o) => { calls++; return pending.Task; };
            var request = Request();
            var first = Track(GenerateTerrainTool.ApplyTerrainHeightmapCore(request, estimate));
            var second = Track(GenerateTerrainTool.ApplyTerrainHeightmapCore(request, estimate));
            // A pending backend has not prevented either call from returning a local task.
            bool wasPending = !pending.Task.IsCompleted;
            pending.SetResult(new TerrainWorldSizeEstimator.EstimateResult(128, "llm", "test"));
            Assert.IsTrue((bool)first["success"], first.ToString());
            yield return WaitForTask((string)first["apply_task_id"]);
            var completed = JObject.FromObject(GenerateTerrainTool.ApplyTerrainHeightmapCore(request, estimate));
            Assert.IsTrue(wasPending);
            Assert.AreEqual(1, calls);
            Assert.AreEqual((string)first["apply_task_id"], (string)second["apply_task_id"]);
            Assert.AreEqual((string)first["apply_task_id"], (string)completed["apply_task_id"]);
            Assert.IsTrue((bool)completed["already_applied"]);
            Assert.AreEqual(_initialRootCount + 1, _scene.rootCount);
        }

        [UnityTest]
        public IEnumerator PendingApplicationFailsWhenActiveSceneChanges()
        {
            // The EditMode runner may already have an untitled scene open.
            string otherScenePath = _folder + "/SwitchTarget.unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(_scene, otherScenePath, saveAsCopy: true));
            Scene otherScene = EditorSceneManager.OpenScene(otherScenePath, OpenSceneMode.Additive);
            try
            {
                if (SceneManager.GetActiveScene() != _scene)
                    Assert.IsTrue(SceneManager.SetActiveScene(_scene));
                var pending = new TaskCompletionSource<TerrainWorldSizeEstimator.EstimateResult>();
                var response = Track(GenerateTerrainTool.ApplyTerrainHeightmapCore(Request(), (p, t, o) => pending.Task));
                bool switched = SceneManager.SetActiveScene(otherScene);
                const string expectedError = "Active scene changed during terrain application";
                if (switched)
                    LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(expectedError));
                pending.SetResult(new TerrainWorldSizeEstimator.EstimateResult(128, "llm", "test"));

                Assert.IsTrue(switched);
                Assert.IsTrue((bool)response["success"], response.ToString());
                string id = (string)response["apply_task_id"];
                yield return WaitForTask(id, "failed");
                StringAssert.Contains(expectedError, TerrainApplyTaskTracker.GetTask(id).ErrorMessage);
                Assert.AreEqual(0, CountFixtureTerrains(_scene));
                Assert.AreEqual(0, CountFixtureTerrains(otherScene));
            }
            finally
            {
                SceneManager.SetActiveScene(_scene);
                EditorSceneManager.CloseScene(otherScene, true);
            }
        }

        [UnityTest]
        public IEnumerator ExplicitZeroSkipsEstimatorAndUsesPixelDimensions()
        {
            int calls = 0;
            var request = Request();
            request["world_size_meters"] = 0;
            var result = Track(GenerateTerrainTool.ApplyTerrainHeightmapCore(request, (p, t, o) =>
            {
                calls++;
                return Task.FromResult(new TerrainWorldSizeEstimator.EstimateResult(500, "llm", "unexpected"));
            }));
            Assert.IsTrue((bool)result["success"], result.ToString());
            yield return WaitForTask((string)result["apply_task_id"]);
            Assert.AreEqual(0, calls);
            var task = TerrainApplyTaskTracker.GetTask((string)result["apply_task_id"]);
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(task.TerrainDataPath);
            Assert.AreEqual(16f, data.size.x);
            Assert.AreEqual(16f, data.size.z);
        }

        [Test]
        public void RequestedSizeDistinguishesMissingNullAndExplicitZero()
        {
            Assert.IsNull(GenerateTerrainTool.ReadRequestedWorldSize(new JObject()));
            Assert.IsNull(GenerateTerrainTool.ReadRequestedWorldSize(new JObject { ["world_size_meters"] = JValue.CreateNull() }));
            Assert.AreEqual(0f, GenerateTerrainTool.ReadRequestedWorldSize(new JObject { ["world_size_meters"] = 0 }));
            Assert.Throws<ArgumentException>(() => GenerateTerrainTool.ReadRequestedWorldSize(new JObject { ["world_size_meters"] = -1 }));
        }

        [UnityTest]
        public IEnumerator NullWorldSizeUsesEstimatorAndCompletesApplication()
        {
            int calls = 0;
            var request = Request();
            request["world_size_meters"] = JValue.CreateNull();
            var result = Track(GenerateTerrainTool.ApplyTerrainHeightmapCore(request, (p, t, o) =>
            {
                calls++;
                return Task.FromResult(new TerrainWorldSizeEstimator.EstimateResult(256, "llm", "test"));
            }));
            Assert.IsTrue((bool)result["success"], result.ToString());
            yield return WaitForTask((string)result["apply_task_id"]);
            Assert.AreEqual(1, calls);
            var task = TerrainApplyTaskTracker.GetTask((string)result["apply_task_id"]);
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(task.TerrainDataPath);
            Assert.AreEqual(256f, data.size.x);
            Assert.AreEqual(256f, data.size.z);
        }

        [Test]
        public void ApplicationIdentityIncludesContentAndEffectiveSettings()
        {
            string path = PathUtils.ToAbsoluteAssetPath(_folder + "/height.png");
            var settings = new JObject { ["scene"] = _scene.handle.ToString(), ["world_size_meters"] = 0 };
            string key = GenerateTerrainTool.BuildApplicationKey(path, new byte[] { 1 }, settings);
            Assert.AreEqual(key, GenerateTerrainTool.BuildApplicationKey(path, new byte[] { 1 }, (JObject)settings.DeepClone()));
            Assert.AreNotEqual(key, GenerateTerrainTool.BuildApplicationKey(path, new byte[] { 2 }, settings));
            settings["world_size_meters"] = 200;
            Assert.AreNotEqual(key, GenerateTerrainTool.BuildApplicationKey(path, new byte[] { 1 }, settings));
            settings["world_size_meters"] = 0;
            settings["application_id"] = "another-copy";
            Assert.AreNotEqual(key, GenerateTerrainTool.BuildApplicationKey(path, new byte[] { 1 }, settings));
        }

        [Test]
        public void FailedApplicationCanBeRetried()
        {
            string key = Guid.NewGuid().ToString("N");
            string id = TerrainApplyTaskTracker.CreateTask(_folder + "/height.png", key);
            _tasks.Add(id);
            Assert.AreEqual(id, TerrainApplyTaskTracker.FindReusableTask(key).ApplyTaskId);
            TerrainApplyTaskTracker.MarkFailed(id, "test failure");
            Assert.IsNull(TerrainApplyTaskTracker.FindReusableTask(key));
        }
    }
}
