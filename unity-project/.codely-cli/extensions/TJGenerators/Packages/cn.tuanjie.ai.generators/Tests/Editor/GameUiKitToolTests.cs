using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Codely.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using NUnit.Framework;
using UnityTcp.Editor.Tools;

namespace TJGenerators.Editor.Tests
{
    public class GameUiKitToolTests
    {
        [TestCase(typeof(GenerateGameUiKitTool), "generate_game_ui_kit")]
        [TestCase(typeof(GenerateSpriteTool), "generate_sprite")]
        public void McpGenerationIsNotRegisteredInUnity(Type type, string toolName)
        {
            foreach (var method in type.GetMethods())
                foreach (var attribute in CustomAttributeData.GetCustomAttributes(method))
                    if (attribute.AttributeType.Name == "CustomToolAttribute")
                        Assert.AreNotEqual(toolName, attribute.ConstructorArguments[0].Value);
        }

        [TestCase("main/button,main_button")]
        [TestCase("button,Button.png")]
        public void SpriteLayersRejectInvalidNamesBeforeImport(string names)
        {
            var result = (Dictionary<string, object>)ImportImageFromUrlTool.ImportImageFromUrl(new JObject
            {
                ["image_urls"] = "https://example.com/base.png,https://example.com/button.png",
                ["import_type"] = "sprite", ["names"] = names
            });
            Assert.AreEqual(false, result["success"]);
            StringAssert.Contains("layer names", (string)result["message"]);
        }

        [UnityTest]
        public IEnumerator ImportStaticSpriteAndLayers()
        {
            var name = "SpriteImportTest_" + Guid.NewGuid().ToString("N");
            var source = Path.Combine(Path.GetTempPath(), name + ".png");
            var outputFolder = "Assets/" + name;
            var path = outputFolder + "/icon.png";
            var url = new Uri(source).AbsoluteUri;
            var texture = new Texture2D(32, 16, TextureFormat.RGBA32, false);
            texture.SetPixel(16, 8, Color.white);
            texture.Apply();
            File.WriteAllBytes(source, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            try
            {
                var submit = (Dictionary<string, object>)ImportImageFromUrlTool.ImportImageFromUrl(new JObject
                {
                    ["image_url"] = url, ["import_type"] = "sprite", ["output_path"] = path
                });
                Assert.AreEqual(true, submit["success"], submit["message"] as string);
                Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    AssetDatabase.LoadAssetAtPath<Sprite>(path), out string guid, out long localId));
                yield return WaitForImport((string)submit["task_id"]);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string finalGuid, out long finalId));
                Assert.AreEqual(guid, finalGuid);
                Assert.AreEqual(localId, finalId);
                Assert.AreEqual(32, sprite.texture.width);

                var layers = (Dictionary<string, object>)ImportImageFromUrlTool.ImportImageFromUrl(new JObject
                {
                    ["image_urls"] = url + "," + url, ["names"] = "base,button",
                    ["import_type"] = "sprite", ["output_path"] = outputFolder + "/layers"
                });
                Assert.AreEqual(true, layers["success"], layers["message"] as string);
                yield return WaitForImport((string)layers["task_id"]);
                foreach (var layer in new[] { "base", "button" })
                    Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(outputFolder + "/layers/" + layer + ".png"));
            }
            finally
            {
                File.Delete(source);
                AssetDatabase.DeleteAsset(outputFolder);
            }
        }

        private static IEnumerator WaitForImport(string id)
        {
            var task = ImportImageTaskTracker.GetTask(id);
            var deadline = EditorApplication.timeSinceStartup + 30;
            while (task.Status == "importing" && EditorApplication.timeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual("completed", task.Status, task.ErrorMessage);
        }
    }
}
