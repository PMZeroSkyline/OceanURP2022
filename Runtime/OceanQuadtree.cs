using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

[StructLayout(LayoutKind.Sequential)]
public struct OceanPatch
{
    public const int Stride = sizeof(float) * 8;
    public Vector4 rect;
    public Vector4 edges;
}

public class OceanQuadtree
{
    // One cache per domain, shared by all camera trees. No texture readback per frame.
    public sealed class ShoreField
    {
        Texture2D source;
        uint revision;
        float size, border;
        Vector2[][] ranges;

        public void Update(Texture2D texture, float domainSize)
        {
            if (texture == null)
            {
                source = null;
                ranges = null;
                return;
            }
            if (source == texture && size == domainSize && revision == texture.updateCount) return;
            source = texture;
            size = domainSize;
            ranges = null;
            if (!texture.isReadable || texture.format != TextureFormat.RGBAFloat) return;
            revision = texture.updateCount;
            int resolution = 1, depth = 0;
            while (resolution * 2 <= Mathf.Min(256, Mathf.Min(texture.width, texture.height)))
            { resolution *= 2; depth++; }
            ranges = new Vector2[depth + 1][];
            ranges[depth] = new Vector2[resolution * resolution];
            for (int i = 0; i < ranges[depth].Length; i++)
                ranges[depth][i] = new Vector2(float.PositiveInfinity, float.NegativeInfinity);
            var pixels = texture.GetPixelData<Color>(0);
            for (int z = 0; z < texture.height; z++)
            for (int x = 0; x < texture.width; x++)
            {
                Color data = pixels[z * texture.width + x];
                if (data.b < .5f) continue;
                var uv = new Vector2((x + .5f) / texture.width, (z + .5f) / texture.height);
                float distance = Vector2.Distance(uv, new Vector2(data.r, data.g)) * size;
                if (data.a > .5f) distance = -distance;
                int i = z * resolution / texture.height * resolution + x * resolution / texture.width;
                Vector2 range = ranges[depth][i];
                ranges[depth][i] = new Vector2(Mathf.Min(range.x, distance), Mathf.Max(range.y, distance));
            }
            // Min/max, not averaged distances: a small island must survive coarse queries.
            for (int level = depth - 1; level >= 0; level--)
            {
                int width = 1 << level;
                ranges[level] = new Vector2[width * width];
                for (int z = 0; z < width; z++)
                for (int x = 0; x < width; x++)
                {
                    Vector2 range = new Vector2(float.PositiveInfinity, float.NegativeInfinity);
                    for (int dz = 0; dz < 2; dz++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        Vector2 child = ranges[level + 1][(z * 2 + dz) * width * 2 + x * 2 + dx];
                        range.x = Mathf.Min(range.x, child.x);
                        range.y = Mathf.Max(range.y, child.y);
                    }
                    ranges[level][z * width + x] = range;
                }
            }
            border = size * 1.5f / Mathf.Min(texture.width, texture.height);
        }

        public bool TryGetRange(Node node, out Vector2 range)
        {
            range = default;
            if (ranges == null) return false;
            int level = Mathf.Min(node.depth, ranges.Length - 1);
            int shift = node.depth - level;
            range = ranges[level][(node.z >> shift) * (1 << level) + (node.x >> shift)];
            if (range.x > range.y) return false;
            // Include the bilinear footprint around the node's sampled texel centres.
            range += new Vector2(-border, border);
            return true;
        }
    }

    public struct Node
    {
        public int x, z, depth;
        public float error;
        public bool visible;
        public (int x, int z, int depth) Key => (x, z, depth);
    }

    public List<OceanPatch> Patches = new List<OceanPatch>();
    public Dictionary<(int x, int z, int depth), Node> leaves = new Dictionary<(int x, int z, int depth), Node>();
    Plane[] planes = new Plane[6];

    Vector3 cameraPosition, cameraForward, absForward;
    float nearClip;
    bool orthographic;
    Vector2 origin;
    float domainSize;
    float seaLevel;
    float pixelScale;
    int maxDepth;
    Vector3 boundsPadding;
    Vector3 offshorePadding;
    ShoreField shore;
    float threshold, waveCurvature, waveBound, shoreCell, shoreStart, shoreEnd, shoreRange, shoreCurvature;

    public void EvaluateAndAddLeaf(Node node)
    {
        Vector4 rect = RectOf(node);
        Vector2 coast = default;
        bool hasCoast = shore != null && shore.TryGetRange(node, out coast);
        bool coastal = hasCoast && shoreCell > 0 && coast.y >= shoreStart && coast.x <= shoreRange;

        var bounds = new Bounds(
            new Vector3(rect.x + rect.z * 0.5f, seaLevel, rect.y + rect.z * 0.5f),
            new Vector3(rect.z, 0f, rect.z) + boundsPadding * 2f
        );
        node.visible = GeometryUtility.TestPlanesAABB(planes, bounds);
        node.error = 0f;
        if (node.visible && node.depth < maxDepth)
        {
            float depth = 1f;
            if (!orthographic)
            {
                // Culling keeps the full displacement bound; offshore error does not inherit a tall shore crest.
                Vector3 errorExtent = new Vector3(rect.z * .5f, 0, rect.z * .5f)
                    + (coastal ? boundsPadding : offshorePadding);
                float nearestDepth = Vector3.Dot(bounds.center - cameraPosition, cameraForward)
                    - Vector3.Dot(errorExtent, absForward);
                // A conservative crest bound can reach the camera while the actual patch is metres away.
                // Keep it for culling, but do not let it monopolise refinement at near-clip distance.
                Vector3 restPoint = new Vector3(Mathf.Clamp(cameraPosition.x, rect.x, rect.x + rect.z),
                    seaLevel, Mathf.Clamp(cameraPosition.z, rect.y, rect.y + rect.z));
                float restDistance = Vector3.Distance(cameraPosition, restPoint);
                depth = Mathf.Max(nearClip, Mathf.Max(nearestDepth, restDistance * .5f));
            }
            float cell = rect.z / OceanSettings.Cells;
            float pixelsPerMetre = pixelScale / depth;
            // Linear interpolation error is bounded by curvature * segmentLength^2 / 8.
            // Flat water therefore does not consume the same budget as a breaking crest.
            float geometricError = Mathf.Min(waveBound, waveCurvature * cell * cell * .125f);
            if (hasCoast && coast.y < 0) geometricError = 0;
            node.error = geometricError * pixelsPerMetre / Mathf.Max(.25f, threshold * .25f);
            if (coastal)
            {
                float swellError = shoreCurvature * cell * cell * .125f * pixelsPerMetre;
                node.error = Mathf.Max(node.error, swellError / Mathf.Max(.25f, threshold * .25f));
                if (coast.x <= shoreEnd)
                {
                    // Preserve the curl at close range, relax once its triangles are subpixel.
                    float crestError = Mathf.Min(cell / shoreCell,
                        cell * pixelsPerMetre / Mathf.Max(1f, threshold * .5f));
                    // Spend a limited budget where the target-sized crest is actually visible.
                    // Without this weight, distant broad bands can outrank a nearby curl's last split.
                    crestError *= Mathf.Max(.125f, shoreCell * pixelsPerMetre);
                    node.error = Mathf.Max(node.error, crestError);
                }
            }
        }
        leaves.Add(node.Key, node);
    }
    public void Build(Camera view, OceanSettings settings, Vector3 center, Vector3 displacementExtent,
        ShoreField shoreField = null)
    {
        cameraPosition = view.transform.position;
        cameraForward = view.transform.forward;
        absForward = new Vector3(Mathf.Abs(cameraForward.x), Mathf.Abs(cameraForward.y), Mathf.Abs(cameraForward.z));
        nearClip = view.nearClipPlane;
        orthographic = view.orthographic;
        shore = shoreField;
        seaLevel = center.y;
        boundsPadding = displacementExtent + Vector3.one * 0.05f;
        domainSize = Mathf.Max(1f, settings.size);
        maxDepth = Mathf.Clamp(settings.maxDepth, 0, 10);
        origin = new Vector2(
            center.x - domainSize * 0.5f,
            center.z - domainSize * 0.5f
        );

        GeometryUtility.CalculateFrustumPlanes(view, planes);
        Matrix4x4 projection = view.projectionMatrix;
        pixelScale = 0.5f * Mathf.Max(
            view.pixelWidth * Mathf.Abs(projection.m00),
            view.pixelHeight * Mathf.Abs(projection.m11)
        );

        leaves.Clear();
        Patches.Clear();

        threshold = Mathf.Max(1f, settings.pixelsPerCell);
        int budget = Mathf.Clamp(settings.maxPatches, 1, 1024);
        waveCurvature = waveBound = 0;
        offshorePadding = Vector3.one * .05f;
        if (settings.waves != null)
        for (int i = 0; i < Mathf.Min(settings.waves.Length, OceanSettings.MaxWaves); i++)
        {
            OceanSettings.Wave wave = settings.waves[i];
            float amplitude = Mathf.Max(0, wave.amplitude);
            float k = 2f * Mathf.PI / Mathf.Max(.1f, wave.wavelength);
            float extent = amplitude * (1f + Mathf.Clamp01(wave.horizontalRatio));
            waveCurvature += extent * k * k;
            waveBound += extent * 2f;
            float angle = wave.direction * Mathf.Deg2Rad;
            float horizontal = amplitude * Mathf.Clamp01(wave.horizontalRatio);
            offshorePadding += new Vector3(Mathf.Abs(Mathf.Cos(angle)) * horizontal,
                amplitude, Mathf.Abs(Mathf.Sin(angle)) * horizontal);
        }
        float wavelength = Mathf.Max(.1f, settings.shoreWavelength);
        bool hasShore = settings.shoreLut != null && (settings.shoreAmplitude > 0 || settings.shoreForward > 0);
        shoreCell = hasShore ? wavelength / 128f : 0;
        shoreStart = -.5f; // The submerged inland skirt does not need crest density.
        shoreRange = Mathf.Max(.1f, settings.shoreRange);
        shoreEnd = Mathf.Min(shoreRange, wavelength * Mathf.Max(1f,
            settings.shorePhaseOffset + .4f * settings.shoreVariation));
        shoreCurvature = hasShore ? 4f * Mathf.PI * Mathf.PI
            * Mathf.Max(0f, settings.shoreAmplitude) * settings.shoreLutMaxHeight / wavelength : 0;

        EvaluateAndAddLeaf(new Node());

        while (leaves.Count + 3 <= budget)
        {
            Node candidate = default;
            float largestError = 1f;
            bool found = false;

            foreach (Node node in leaves.Values)
            {
                if (node.error <= largestError)
                    continue;

                candidate = node;
                largestError = node.error;
                found = true;
            }

            if (!found)
                break;

            Node next = FindSplitDependency(candidate);
            Split(next);
        }

        foreach (Node node in leaves.Values)
        {
            if (!node.visible)
                continue;
            
            Vector4 rect = RectOf(node);
            float cell = rect.z / OceanSettings.Cells;
            Vector4 edges = Vector4.one * cell;

            for (int side = 0; side < 4; side++)
            {
                if (TryGetCoarserOrEqualNeighbour(node, side, out Node neighbour) && neighbour.depth < node.depth)
                {
                    edges[side] = cell * 2f;
                }
            }
            Patches.Add(new OceanPatch
            {
               rect = rect,
               edges = edges 
            });
        }
    }
    Node FindSplitDependency(Node node)
    {
        for (int side = 0; side < 4; side++)
        {
            if (TryGetCoarserOrEqualNeighbour(node, side, out Node neighbour) && neighbour.depth < node.depth)
            {
                return FindSplitDependency(neighbour);
            }
        }
        return node;
    }
    bool TryGetCoarserOrEqualNeighbour(Node node, int side, out Node neighbour)
    {
        // This is written specifically for sequential partitioning rather than for general use.
        int x = node.x;
        int z = node.z;

        switch (side)
        {
            case 0: x--; break;
            case 1: x++; break;
            case 2: z--; break;
            case 3: z++; break;
        }

        neighbour = default;

        int gridSize = 1 << node.depth;
        if (x < 0 || z < 0 || x >= gridSize || z >= gridSize)
        {
            return false;
        }

        if (leaves.TryGetValue((x, z, node.depth), out neighbour))
        {
            return true;
        }

        if (node.depth == 0)
        {
            return false;
        }

        return leaves.TryGetValue((x >> 1, z >> 1, node.depth - 1), out neighbour);
    }
    void Split(Node node)
    {
        leaves.Remove(node.Key);
        for (int z = 0; z < 2; z++)
        {
            for (int x = 0; x < 2; x++)
            {
                EvaluateAndAddLeaf(new Node
                {
                   x = node.x * 2 + x,
                   z = node.z * 2 + z,
                   depth = node.depth + 1
                });
            }
        }
    }
    Vector4 RectOf(Node node)
    {
        float size = domainSize / (1 << node.depth);
        return new Vector4(
            origin.x + node.x * size,
            origin.y + node.z * size,
            size,
            node.depth
        );
    }
}
