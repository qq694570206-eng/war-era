#if UNITY_EDITOR
namespace TJGenerators.Pipeline
{
    /// <summary>
    /// 由 CustomTool 等 Host 实现：指定 3D 模型下载的固定 Assets 路径，跳过 History 槽位分配。
    /// </summary>
    public interface IModelDownloadPathProvider
    {
        /// <param name="resolvedSavePath">Pipeline 根据 URL 扩展名解析后的保存路径（可只取其扩展名）。</param>
        /// <returns>非空则直接写入该 Assets 相对路径；null/empty 则走默认 History 逻辑。</returns>
        string GetModelDownloadPath(string resolvedSavePath);
    }

    /// <summary>
    /// 可选 Host 能力：控制 3D 模型导入时的后处理、尺寸归一化与接地行为。
    /// </summary>
    public interface IModelImportOptionsProvider
    {
        /// <summary>仅作为动画剪辑源导入（跳过静态材质提取、顶点数校验和单剪辑自动控制器）。</summary>
        bool IsMotionOnlyImport { get; }

        /// <summary>Prefab 绑定时的归一化目标尺寸（米）；&lt;= 0 使用默认值。</summary>
        float TargetModelSize { get; }

        /// <summary>是否按高度归一化并把模型底部贴齐 Prefab 局部 y=0（适用于人形角色）。</summary>
        bool GroundModelToOriginY { get; }
    }
}
#endif
