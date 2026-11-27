using System.Collections;
using Codely.Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityTcp.Editor.Tools;

namespace TJGenerators.Editor.Tests
{
    /// <summary>
    /// Manual EditMode samples for MCP sprite-sequence import (non-GUI).
    /// Agent path: MCP generate_sprite_animation → import_sprite_sequence_from_url.
    /// Legacy Unity generate_sprite_sequence / auto / router CustomTools were removed.
    /// </summary>
    [Explicit("Manual EditMode sample — select and Run in Test Runner after login")]
    public class CustomToolSpriteSequenceSampleTests : CustomToolSampleTestBase
    {
        // Permanent CDN PNG used to exercise import + CutoutAndSlice (any PNG is sliced as cols×rows).
        private const string SampleSheetCdnUrl =
            "https://tuanjie-ai-prd.tos-cn-shanghai.volces.com/mcp-upload/a1458a8423827fe0854bf198044b8d15/2025-07-16183254.png";

        private static readonly string[] ImportPathKeys = { "folder_path", "animation_clip_path" };

        [Test]
        public void Import_MissingSheetUrl_Fails()
        {
            AssertSubmitFailed(ImportSpriteSequenceFromUrlTool.ImportSpriteSequenceFromUrl(new JObject
            {
                ["fps"] = 12
            }));
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Import_SheetUrl_WaitUntilCompleted()
        {
            var submit = ImportSpriteSequenceFromUrlTool.ImportSpriteSequenceFromUrl(new JObject
            {
                ["sheet_url"] = SampleSheetCdnUrl,
                ["prompt"] = "import sample: slice sheet to animation",
                ["fps"] = 12,
                ["loop"] = true,
                ["cols"] = 4,
                ["rows"] = 4,
                ["preserve_existing_alpha"] = true,
                ["output_path"] = SampleAssetPath("ImportSpriteSequence_WaitDone", ".png")
            });

            AssertSubmitOk(submit);
            string taskId = GetTaskId(submit);
            AssertQueryFound(
                ImportSpriteSequenceFromUrlTool.QueryImportStatus(new JObject { ["task_id"] = taskId }),
                taskId);

            yield return WaitUntilTerminalStatus(
                taskId,
                id => ImportSpriteSequenceFromUrlTool.QueryImportStatus(new JObject { ["task_id"] = id }),
                ImportPathKeys);
        }
    }
}
