using System;
using NUnit.Framework;
using TJGenerators.PostProcessing;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TJGenerators.Editor.Tests
{
    public class MotionControllerTests
    {
        private string _folder;
        private string _motion;

        [SetUp]
        public void SetUp()
        {
            string name = "MotionControllerTest_" + Guid.NewGuid().ToString("N");
            _folder = "Assets/" + name;
            AssetDatabase.CreateFolder("Assets", name);
            _motion = _folder + "/Motion.anim";
            var clip = new AnimationClip { name = "Wave" };
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x"),
                AnimationCurve.Linear(0, 0, 1, 1));
            AssetDatabase.CreateAsset(clip, _motion);
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(_folder);

        private void AssertController(string path, bool loop)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            Assert.NotNull(controller);
            var state = controller.layers[0].stateMachine.defaultState;
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<AnimationClip>(_motion), state.motion);
            Assert.AreEqual(loop ? 1 : 0, state.transitions.Length);
            if (loop)
            {
                Assert.AreEqual(state, state.transitions[0].destinationState);
                Assert.IsTrue(state.transitions[0].hasExitTime);
                Assert.AreEqual(1f, state.transitions[0].exitTime);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Controller_RespectsLoopFlag(bool loop)
        {
            string path = RiggedModelPostProcess.CreateSingleClipLoopAnimatorControllerFromMotionClip(
                _folder, "Character", _motion, loop);
            AssertController(path, loop);
        }

        [Test]
        public void LegacyOverload_KeepsLoopingDefault()
        {
            string path = RiggedModelPostProcess.CreateSingleClipLoopAnimatorControllerFromMotionClip(
                _folder, "Character", _motion);
            AssertController(path, true);
        }

        [Test]
        public void MissingClip_DoesNotCreateController()
        {
            string missing = _folder + "/missing.fbx";
            Assert.IsNull(RiggedModelPostProcess.CreateSingleClipLoopAnimatorControllerFromMotionClip(_folder, "Character", missing));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<AnimatorController>(_folder + "/Character_Controller.controller"));
        }

        [Test]
        public void AddOrUpdate_AppendsMultipleStatesAndPromotesIdleDefault()
        {
            string walkPath = _folder + "/Walk.anim";
            string idlePath = _folder + "/Idle.anim";
            string attackPath = _folder + "/Attack.anim";

            foreach (var (p, n) in new[] { (walkPath, "Walk"), (idlePath, "Idle"), (attackPath, "Attack") })
            {
                var c = new AnimationClip { name = n };
                AnimationUtility.SetEditorCurve(c,
                    EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x"),
                    AnimationCurve.Linear(0, 0, 1, 1));
                AssetDatabase.CreateAsset(c, p);
            }

            string ctrlPath = RiggedModelPostProcess.AddOrUpdateMotionClipInAnimatorController(
                _folder, "Hero", walkPath, "walk", loop: true);
            Assert.NotNull(ctrlPath);
            Assert.AreEqual(new[] { "walk" }, RiggedModelPostProcess.GetControllerStateNames(ctrlPath));

            // Appending idle preserves walk and promotes idle to defaultState
            ctrlPath = RiggedModelPostProcess.AddOrUpdateMotionClipInAnimatorController(
                _folder, "Hero", idlePath, "idle", loop: true);
            // Appending attack preserves walk + idle and keeps idle as defaultState
            ctrlPath = RiggedModelPostProcess.AddOrUpdateMotionClipInAnimatorController(
                _folder, "Hero", attackPath, "attack", loop: false);
            // Updating walk in-place does not create a duplicate state
            ctrlPath = RiggedModelPostProcess.AddOrUpdateMotionClipInAnimatorController(
                _folder, "Hero", walkPath, "walk", loop: true);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            Assert.NotNull(controller);
            var sm = controller.layers[0].stateMachine;
            Assert.AreEqual(3, sm.states.Length);
            Assert.AreEqual("idle", sm.defaultState.name);
            CollectionAssert.AreEquivalent(new[] { "walk", "idle", "attack" },
                RiggedModelPostProcess.GetControllerStateNames(ctrlPath));

            // replaceController=true resets cleanly to only the new state
            ctrlPath = RiggedModelPostProcess.AddOrUpdateMotionClipInAnimatorController(
                _folder, "Hero", attackPath, "attack", loop: false, replaceController: true);
            Assert.AreEqual(new[] { "attack" }, RiggedModelPostProcess.GetControllerStateNames(ctrlPath));
        }

        [Test]
        public void ApplyPackedMetallicSmoothness_EnablesShaderSpecificKeyword()
        {
            var packed = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                var standardShader = Shader.Find("Standard");
                if (standardShader != null)
                {
                    var mat = new Material(standardShader);
                    try
                    {
                        RiggedModelPostProcess.ApplyPackedMetallicSmoothness(mat, packed);
                        Assert.IsTrue(mat.IsKeywordEnabled("_METALLICGLOSSMAP"));
                        Assert.IsFalse(mat.IsKeywordEnabled("_METALLICSPECGLOSSMAP"));
                        Assert.AreSame(packed, mat.GetTexture("_MetallicGlossMap"));
                    }
                    finally { UnityEngine.Object.DestroyImmediate(mat); }
                }

                var urpLit = Shader.Find("Universal Render Pipeline/Lit");
                if (urpLit != null)
                {
                    var mat = new Material(urpLit);
                    try
                    {
                        RiggedModelPostProcess.ApplyPackedMetallicSmoothness(mat, packed);
                        Assert.IsTrue(mat.IsKeywordEnabled("_METALLICSPECGLOSSMAP"));
                        Assert.IsFalse(mat.IsKeywordEnabled("_METALLICGLOSSMAP"));
                        Assert.AreSame(packed, mat.GetTexture("_MetallicGlossMap"));
                    }
                    finally { UnityEngine.Object.DestroyImmediate(mat); }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(packed);
            }
        }
    }
}
