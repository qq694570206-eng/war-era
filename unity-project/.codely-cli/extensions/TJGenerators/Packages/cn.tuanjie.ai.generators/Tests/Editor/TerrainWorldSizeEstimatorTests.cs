using NUnit.Framework;
using System.Collections;
using UnityEngine.TestTools;
using TJGenerators.Utils;

namespace TJGenerators.Editor.Tests
{
    public class TerrainWorldSizeEstimatorTests
    {
        [TestCase("a landscape with 10 mountains")]
        [TestCase("a valley with 3 mesas")]
        [TestCase("use a 200mm model")]
        [TestCase("10 kmountains")]
        public void ParseExplicit_DoesNotConsumeWordPrefixes(string prompt)
        {
            Assert.IsFalse(TerrainWorldSizeEstimator.TryParseExplicitMeters(prompt, out _, out _));
        }

        [TestCase("生成200米的小岛", 200f)]
        [TestCase("宽200m的小岛", 200f)]
        [TestCase("a 200 meters island", 200f)]
        [TestCase("200 metres wide", 200f)]
        [TestCase("2 kilometres wide", 2000f)]
        [TestCase("2KM valley", 2000f)]
        [TestCase("生成0米地形", 0f)]
        public void ParseExplicit_AcceptsWholeUnitsAndAdjacentChinese(string prompt, float expected)
        {
            Assert.IsTrue(TerrainWorldSizeEstimator.TryParseExplicitMeters(prompt, out float meters, out _));
            Assert.AreEqual(expected, meters, 0.01f);
        }

        [UnityTest]
        public IEnumerator EstimateAsync_ExplicitSizeDoesNotNeedCredentials()
        {
            var task = TerrainWorldSizeEstimator.EstimateAsync("生成200米小岛", null);
            while (!task.IsCompleted) yield return null;
            var result = task.GetAwaiter().GetResult();
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(200f, result.Meters);
            Assert.AreEqual("user_text", result.Source);
        }

        [Test]
        public void ParseExplicit_MetersAndKm()
        {
            Assert.IsTrue(TerrainWorldSizeEstimator.TryParseExplicitMeters("做一个 200米 的小岛", out float m, out _));
            Assert.AreEqual(200f, m, 0.01f);

            Assert.IsTrue(TerrainWorldSizeEstimator.TryParseExplicitMeters("2km mountain valley", out float km, out _));
            Assert.AreEqual(2000f, km, 0.01f);
        }

        [Test]
        public void ParseLlm_JsonObject()
        {
            const string text = "{\"world_size_meters\": 180, \"reason\": \"flat island\"}";
            Assert.IsTrue(TerrainWorldSizeEstimator.TryParseLlmMeters(text, out float meters, out string reason));
            Assert.AreEqual(180f, meters, 0.01f);
            Assert.AreEqual("flat island", reason);
        }

        [Test]
        public void ParseLlm_FencedJson()
        {
            const string text = "```json\n{\"world_size_meters\": 128}\n```";
            Assert.IsTrue(TerrainWorldSizeEstimator.TryParseLlmMeters(text, out float meters, out _));
            Assert.AreEqual(128f, meters, 0.01f);
        }

        [Test]
        public void EstimateSync_ExplicitText_NoNetwork()
        {
            var r = TerrainWorldSizeEstimator.EstimateSync("请生成 150米 平坦海岛", token: null);
            Assert.IsTrue(r.Succeeded);
            Assert.AreEqual("user_text", r.Source);
            Assert.AreEqual(150f, r.Meters, 0.01f);
        }

        [Test]
        public void EstimateSync_NoToken_NoExplicit_FailsWithoutHeuristic()
        {
            var r = TerrainWorldSizeEstimator.EstimateSync("flat tropical island", token: null);
            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual("failed", r.Source);
        }
    }
}
