using System;
using System.IO;
using NUnit.Framework;
using TJGenerators.PostProcessing;
using TJGenerators.Utils;
using UnityEditor;
using UnityEngine;

namespace TJGenerators.Editor.Tests
{
    public class SpriteSequenceAlphaTests
    {
        private string _folder;
        private string _output;

        [SetUp]
        public void SetUp()
        {
            string name = "AlphaSequenceTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            _folder = "Assets/" + name;
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_output)) AssetDatabase.DeleteAsset(_output);
            _output = null;
            AssetDatabase.DeleteAsset(_folder);
        }

        private Texture2D Slice(bool transparent, bool preserveAlpha)
        {
            var pixels = new Color32[128];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = transparent ? Color.clear : Color.green;
            pixels[3 * 16 + 3] = transparent ? Color.green : Color.red;
            pixels[3 * 16 + 11] = transparent ? Color.green : Color.red;
            string path = WriteSource(16, 8, pixels);
            var result = SpriteSequencePostProcess.CutoutAndSlice(path, 0.16f, 0.04f, 2, 1, 12, true,
                writeCutoutBackToAsset: false,
                preserveExistingAlpha: preserveAlpha);
            _output = result.OutputDirectory;
            Assert.AreEqual(2, result.ExportedCount);
            Assert.NotNull(AssetDatabase.LoadAssetAtPath<AnimationClip>(result.AnimationClipPath));
            return SpriteSequencePostProcess.LoadReadableTextureFromAssetPath(result.SpriteAssetPaths[0]);
        }

        [TestCase(5, 4, 4, 1)]
        [TestCase(4, 5, 1, 0)]
        public void VisibleGridRemainder_FailsBeforeOverwritingSource(int width, int height, int x, int y)
        {
            var pixels = new Color32[width * height];
            pixels[y * width + x] = new Color32(255, 0, 0, 1);
            pixels[width + 1] = Color.green;
            string path = WriteSource(width, height, pixels);
            string absolutePath = PathUtils.ToAbsoluteAssetPath(path);
            byte[] original = File.ReadAllBytes(absolutePath);

            var exception = Assert.Throws<InvalidOperationException>(() =>
            {
                var result = SpriteSequencePostProcess.CutoutAndSlice(path, 0.16f, 0.04f, 2, 2, 12, true,
                    writeCutoutBackToAsset: true, preserveExistingAlpha: false);
                _output = result.OutputDirectory;
            });
            StringAssert.Contains("nontransparent pixels outside the slice grid", exception.Message);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(absolutePath));
        }

        [Test]
        public void TransparentGridRemainder_KeepsFramePixelsAndPlaybackOrder()
        {
            var markers = new Color32[]
            {
                new Color32(0, 255, 0, 255),
                new Color32(255, 0, 0, 1),
                new Color32(0, 0, 255, 255),
                new Color32(255, 255, 0, 255),
            };
            var pixels = new Color32[25];
            pixels[15] = markers[0];
            pixels[17] = markers[1];
            pixels[5] = markers[2];
            pixels[7] = markers[3];
            string path = WriteSource(5, 5, pixels);
            var result = SpriteSequencePostProcess.CutoutAndSlice(path, 0.16f, 0.04f, 2, 2, 12, true,
                writeCutoutBackToAsset: false, preserveExistingAlpha: true);
            _output = result.OutputDirectory;

            Assert.AreEqual(markers.Length, result.ExportedCount);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(result.AnimationClipPath);
            Assert.NotNull(clip);
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, SpriteSequencePostProcess.CreateSpriteRendererBinding());
            Assert.AreEqual(markers.Length, keys.Length);
            for (int i = 0; i < markers.Length; i++)
            {
                Assert.AreEqual(result.SpriteAssetPaths[i], AssetDatabase.GetAssetPath(keys[i].value));
                var frame = SpriteSequencePostProcess.LoadReadableTextureFromAssetPath(result.SpriteAssetPaths[i]);
                Assert.NotNull(frame);
                try
                {
                    Assert.AreEqual(2, frame.width);
                    Assert.AreEqual(2, frame.height);
                    Assert.AreEqual(markers[i], frame.GetPixels32()[0]);
                }
                finally { UnityEngine.Object.DestroyImmediate(frame); }
            }
        }

        private string WriteSource(int width, int height, Color32[] pixels)
        {
            string path = _folder + "/source.png";
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(PathUtils.ToAbsoluteAssetPath(path), texture.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path);
            return path;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ExistingAlpha_PreservesGreenForegroundOnlyWhenRequested(bool preserveAlpha)
        {
            var frame = Slice(true, preserveAlpha);
            try
            {
                Assert.AreEqual(0f, frame.GetPixel(0, 0).a);
                Assert.AreEqual(preserveAlpha ? 1f : 0f, frame.GetPixel(3, 3).a);
                if (preserveAlpha) Assert.AreEqual(Color.green, frame.GetPixel(3, 3));
            }
            finally { UnityEngine.Object.DestroyImmediate(frame); }
        }

        [Test]
        public void OpaqueGreenScreen_StillKeysBackgroundInAutomaticMode()
        {
            var frame = Slice(false, true);
            try
            {
                Assert.AreEqual(0f, frame.GetPixel(0, 0).a);
                Assert.AreEqual(Color.red, frame.GetPixel(3, 3));
            }
            finally { UnityEngine.Object.DestroyImmediate(frame); }
        }
    }
}
