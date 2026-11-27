// ================================================================
// SpriteFlash.shader — 受击闪白（SpriteRenderer 专用）
//
// 为什么必须自建 shader，而不能用 sr.color：
//   SpriteRenderer.color 是顶点色，与贴图做【乘算】。
//   白色 tint × 彩色贴图 = 原色，看不出闪白；想要提亮只能给 >1 的值，
//   但顶点色按 Color32 存储会被 clamp 到 1，同样无效。
//   所以"加色提亮"这一步只能在 fragment 里做。
//
// 与内置 Sprites/Default 的差异（只有两行）：
//   1) 新增 _FlashAmount（0..1）
//   2) 采样后 rgb = lerp(rgb, white, _FlashAmount)
//   其余（Blend One OneMinusSrcAlpha 预乘、ETC1 外部 alpha、PixelSnap、flip）
//   全部沿用 UnitySprites.cginc，保证与 SpriteRenderer 的渲染路径一致。
//
// 不破坏合批：_FlashAmount 走 MaterialPropertyBlock（每实例 per-renderer 数据），
//   不产生材质变体，同 shader 同贴图的 SpriteRenderer 仍可合批。
//
// 注意：_FlashAmount 必须用 [PerRendererData]，否则 MaterialPropertyBlock
//   只对声明了该标记的属性生效。这是最容易踩的坑——少这个标记，闪白静默失效。
// ================================================================
Shader "WarEra/SpriteFlash"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [PerRendererData] _FlashAmount ("Flash Amount", Range(0,1)) = 0

        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
        CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment SpriteFlashFrag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            float _FlashAmount;

            // 【函数名不能叫 SpriteFrag】UnitySprites.cginc 第 86 行已经定义了
            //   fixed4 SpriteFrag(v2f IN) : SV_Target
            // 再定义一次就是重定义，实测报错：
            //   Shader error in 'WarEra/SpriteFlash': redefinition of 'SpriteFrag' at line 66 (on metal)
            // 这个错误的表现极具迷惑性：Console 里只有一行 Shader error，材质仍是粉红/回退，
            // 而 UnitEntity.UpdateFlash 会走 else 分支（HasProperty 为 false）→ 退回 sr.color，
            // 于是"闪白还能看见一点"，很容易被误判为"能用"。实际上 shader 路径整条没生效。
            // 必须换名 + 同步改 #pragma fragment。
            fixed4 SpriteFlashFrag(v2f IN) : SV_Target
            {
                fixed4 c = SampleSpriteTexture(IN.texcoord) * IN.color;
                // 受击闪白：向纯白插值（不是乘白，乘白等于没效果）
                c.rgb = lerp(c.rgb, fixed3(1.0, 1.0, 1.0), _FlashAmount);
                c.rgb *= c.a;   // 预乘，与 Blend One OneMinusSrcAlpha 配套
                return c;
            }
        ENDCG
        }
    }

    Fallback "Sprites/Default"
}
