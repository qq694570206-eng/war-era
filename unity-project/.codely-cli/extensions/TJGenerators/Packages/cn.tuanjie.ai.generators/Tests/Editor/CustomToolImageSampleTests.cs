using System.Collections;
using Codely.Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityTcp.Editor.Tools;

namespace TJGenerators.Editor.Tests
{
    /// <summary>
    /// Manual EditMode samples for image-family CustomTools (non-GUI).
    /// Image generation now runs on the MCP backend (generate_image -> check_task);
    /// Unity-side coverage = import_image_from_url (single texture, sprite and layers).
    /// One submit per scenario (query + wait in the same test) to avoid burning credits.
    /// </summary>
    [Explicit("Manual EditMode sample — select and Run in Test Runner after login")]
    public class CustomToolImageSampleTests : CustomToolSampleTestBase
    {
        private static readonly string[] ImagePathKeys = { "image_path" };
        private static readonly string[] Layer0Keys = { "layer_0_path" };

        // 永久 CDN URL（file_upload 产物，用于 import_image_from_url 样例）
        private const string SampleCdnUrlA =
            "https://tuanjie-ai-prd.tos-cn-shanghai.volces.com/mcp-upload/a1458a8423827fe0854bf198044b8d15/2025-07-16183254.png";
        private const string SampleCdnUrlB =
            "https://tuanjie-ai-prd.tos-cn-shanghai.volces.com/mcp-upload/beedb586a762783a7eab1e51cebbbe41/2025-07-16183330.png";

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator ImportImage_Single_WaitUntilCompleted()
        {
            var submit = ImportImageFromUrlTool.ImportImageFromUrl(new JObject
            {
                ["image_url"] = SampleCdnUrlA,
                ["prompt"] = "import sample: sci-fi drone cockpit front view",
                ["output_path"] = SampleAssetPath("ImportImage_Single_WaitDone", ".png")
            });

            AssertSubmitOk(submit);
            string taskId = GetTaskId(submit);
            AssertQueryFound(
                ImportImageFromUrlTool.QueryImportStatus(new JObject { ["task_id"] = taskId }),
                taskId);

            yield return WaitUntilTerminalStatus(
                taskId,
                id => ImportImageFromUrlTool.QueryImportStatus(new JObject { ["task_id"] = id }),
                ImagePathKeys);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator ImportImage_MultiLayers_WaitUntilCompleted()
        {
            var submit = ImportImageFromUrlTool.ImportImageFromUrl(new JObject
            {
                ["image_urls"] = SampleCdnUrlA + "," + SampleCdnUrlB,
                ["names"] = "layer_0,layer_1",
                ["prompt"] = "import sample: two views as pseudo layers",
                ["output_path"] = "Assets/TJGenerators/History/TestImportLayers_Sample"
            });

            AssertSubmitOk(submit);
            string taskId = GetTaskId(submit);
            AssertQueryFound(
                ImportImageFromUrlTool.QueryImportStatus(new JObject { ["task_id"] = taskId }),
                taskId);

            yield return WaitUntilTerminalStatus(
                taskId,
                id => ImportImageFromUrlTool.QueryImportStatus(new JObject { ["task_id"] = id }),
                Layer0Keys);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator ImportSprite_PreservesPlaceholderReference()
        {
            string path = SampleAssetPath("Sprite_Import", ".png");
            var submit = ImportImageFromUrlTool.ImportImageFromUrl(new JObject
            {
                ["image_url"] = SampleCdnUrlA,
                ["import_type"] = "sprite",
                ["output_path"] = path
            });
            AssertSubmitOk(submit);
            var placeholder = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(path);
            Assert.IsNotNull(placeholder);
            yield return WaitUntilTerminalStatus(GetTaskId(submit),
                id => ImportImageFromUrlTool.QueryImportStatus(new JObject { ["task_id"] = id }), ImagePathKeys);
            Assert.AreEqual(placeholder, UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(path));
        }
    }
}
