# OceanURP2022

海面由外海 Gerstner 波、岸浪 LUT 和四叉树 Patch 构成。默认材质提供基础漫反射、动态细节法线和可选泡沫着色；反射、折射等水体光学由后续材质实现。

`OceanDomain` 组件上的 **Surface Material** 指定实际绘制材质，可直接更换。**Animate In Editor** 也在此组件，关闭后停在当前时刻；播放模式使用运行时间。`OceanSettings` 只保存海域、LOD、几何波浪、Bake 和 LUT 参数，不含材质或预览开关。材质的细节法线和泡沫参数独立于这些几何设置；即使关闭所有几何波，默认细节法线仍会运动。

海域位于世界 XZ 平面，尺寸为正方形，只使用对象位置。长度以米计，波浪传播速度以米/秒计。设置 **Ground Layers** 后，在 Domain 点击 **Bake Height and Coast** 生成高度及海岸数据；修改海域位置或尺寸后重新 Bake。

自定义材质从现有 `Shader/OceanPatch.shader` 复制并改名即可，不需要新增渲染组件或另一套海面求值代码：

1. 保留 `#include "OceanWaves.hlsl"`（复制到其他文件夹时改为实际相对路径或 `Assets/OceanURP2022/Shader/OceanWaves.hlsl`）。该文件声明 Domain 注入的数据、纹理和 Patch 结构；自定义材质参数使用自己的 `UnityPerMaterial`。
2. Domain 绘制材质的第 0 个 Pass，保留原 Shader 的顶点输入、`#pragma target 4.5`、实例 ID 和 `_Patches` 读取。顶点阶段保留 `OceanEvaluatePatch` 调用，将输出的 `positionWS`、`normalWS`、`foam` 传到片元。输入 Patch UV 为 0～1；输出位置为米制世界坐标，法线包含全部几何位移及其混合导数。绕过此调用会丢失 LOD 边界修补。
3. 片元阶段调用以下两个通用函数，然后自行实现颜色、光照和水体光学。函数使用屏幕导数，只用于片元阶段；细节过滤不改变几何或 LOD。

```hlsl
float3 normal = OceanSurfaceNormal(
    positionWS.xz, normalWS, _WaveTime,
    detailScale, detailStrength, detailSpeed, normalTS);
float foamMask = OceanSurfaceFoam(
    foam, positionWS.xz, _WaveTime,
    foamScale, foamSpeed, foamBreakup);
```

- `positionWS.xz`：位移后的世界 XZ，单位米；`_WaveTime`：Domain 提供的秒数。
- `detailScale` / `foamScale`：基础空间频率，单位周/米；`detailSpeed` / `foamSpeed`：基础时间频率，单位周/秒，各内部波再乘各自倍率。
- `detailStrength`：无量纲坡度强度。`normalTS` 为已解码的法线贴图值，贴图 UV 沿世界 XZ 排列；没有贴图时传 `float3(0, 0, 1)`。函数返回单位世界法线，保留翻卷面的朝向。
- `foam`：顶点输出的几何泡沫遮罩；`foamBreakup` 为 0～1 的破碎程度。返回值仍是 0～1 遮罩，颜色与最终强度由材质决定。默认材质的 **Foam Strength** 为 0，调高才显示泡沫。

需要任意未位移世界 XZ 处的几何结果时，可调用 `OceanEvaluateSurface(worldXZ, positionWS, normalWS, foam)`；它不包含 Patch 边界修补。双面卷浪的几何法线可能朝下，不能通过强制法线 Y 为正来修正；默认材质仅在双面照明时根据正反面翻转法线。

自定义岸浪使用 Settings 中的 **Shore Stages**：

1. 基于参考形态修改时，点击 **Import Reference as Editable Stages**。从零画时，手动建立若干 Stage，例如 Life 为 `0、0.35、0.65、1`；空列表会使用参考纹理。
2. **Life** 是 LUT 的纵坐标，范围 0～1，可安排形成、前倾翻卷、破碎、消退；它不是秒数。不同 Stage 的 Life 必须唯一，生成器按 Life 排序并在阶段之间插值。希望波从平面出现并回到平面时，将 Life 0 和 1 的位移曲线设为零。
3. 每个 Stage 的 **Forward / Height / Foam** 曲线横轴都是一周期内的相位 U，范围 0～1。Forward 纵轴是沿岸浪前进方向的位移/波长，正值向岸；Height 纵轴是垂直位移/波长，正值向上；Foam 纵轴是 0～1 遮罩。
4. 想加高某阶段的浪，抬高 Height 曲线波峰；想使浪头前倾，改变同一相位附近的 Forward。两者组合决定轮廓，过大的前移可能让轮廓自身相交。最终前移约为 `Forward × Shore Wavelength × Shore Forward`，抬升约为 `Height × Shore Wavelength × Shore Amplitude`，随后再参与坡岸贴合及外海混合。
5. 三条曲线都必须覆盖 U=0 和 U=1，首尾值相同，关键帧和切线为有限值。运行时还会把每周期首尾各 6% 的 RGB 平滑衰减至零，确保生命周期切换时位置和导数连续；主要浪峰与翻卷形态放在中间 88%。此宽度不随 LUT 分辨率改变。
6. 编辑完成后点击 **Generate Shore LUT**。生成结果作为子资源保存在当前 Settings 中，直接用于绘制。提高 **Shore LUT Resolution** 后也需重新 Generate。**Use Reference Profile** 会清空自定义 Stage 并重新生成参考轮廓。

LUT 的 RGB 分别保存前移/波长、抬升/波长、泡沫。运行时采用双三次重建并计算导数；增加 LUT 分辨率不能代替几何细分，也不会自动消除曲线自相交。

原始轮廓与噪声来自用户提供的 FluxFluid Unity 参考项目：`Assets/FluxFluid/Runtime/Resources/FluidFlux/T_DefaultWaveProfile_Forard.png`、`T_SmoothNoise.png`。生成器处理参考 PNG 的纵轴方向，编辑后的 Stage 与生成 LUT 均使用递增的 Life，无需自行翻转。

网格分配优先考虑近岸翻卷和屏幕误差，平缓外海按波形曲率减少细分，保留总 Patch 预算与 2:1 平衡；单纯平面可退到一个 Patch。更改相机方向不会衰减几何波幅。参考 LUT 的部分翻卷阶段存在轮廓自交：周期接缝已用包络连续化，但网格加密不能消除参考轮廓自身的交叠。
