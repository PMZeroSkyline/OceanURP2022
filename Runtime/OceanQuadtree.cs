using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

public class OceanQuadtree
{
    public struct Node
    {
        // public Vector4 rect;
        public int x, z, depth;
        public float error;
        public bool visible;
        public (int x, int z, int depth) Key => (x, z, depth);
    }

    public List<OceanPatch> Patches = new List<OceanPatch>();
    // public List<Node> leaves = new List<Node>();
    public Dictionary<(int x, int z, int depth), Node> leaves = new Dictionary<(int x, int z, int depth), Node>();
    Plane[] planes = new Plane[6];

    Camera camera;
    Vector2 origin;
    float domainSize;
    float seaLevel;
    float pixelScale;
    int maxDepth;
    float verticalExtent;

    public void EvaluateAndAddLeaf(Node node)
    {
        Vector4 rect = RectOf(node);

        var bounds = new Bounds(
            new Vector3(rect.x + rect.z * 0.5f, seaLevel, rect.y + rect.z * 0.5f),
            new Vector3(rect.z, verticalExtent * 2f, rect.z)
        );
        node.visible = GeometryUtility.TestPlanesAABB(planes, bounds);
        node.error = 0f;
        if (node.visible && node.depth < maxDepth)
        {
            float depth = 1f;
            if (!camera.orthographic)
            {
                Vector3 forward = camera.transform.forward;
                Vector3 absForward = new Vector3(
                    Mathf.Abs(forward.x),
                    Mathf.Abs(forward.y),
                    Mathf.Abs(forward.z)
                );
                float nearestDepth = Vector3.Dot(bounds.center - camera.transform.position, forward) - Vector3.Dot(bounds.extents, absForward);
                depth = Mathf.Max(camera.nearClipPlane, nearestDepth);
            }
            node.error = rect.z / OceanSettings.Cells * pixelScale / depth;
        }
        leaves.Add(node.Key, node);
    }
    public void Build(Camera view, OceanSettings settings, Vector3 center)
    {
        camera = view;
        seaLevel = center.y;
        verticalExtent = Mathf.Max(0f, settings.waveAmplitude) + 0.05f;
        domainSize = Mathf.Max(1f, settings.size);
        maxDepth = Mathf.Clamp(settings.maxDepth, 0, 10);
        origin = new Vector2(
            center.x - domainSize * 0.5f,
            center.z - domainSize * 0.5f
        );

        GeometryUtility.CalculateFrustumPlanes(camera, planes);
        Matrix4x4 projection = camera.projectionMatrix;
        pixelScale = 0.5f * Mathf.Max(
            camera.pixelWidth * Mathf.Abs(projection.m00),
            camera.pixelHeight * Mathf.Abs(projection.m11)
        );

        leaves.Clear();
        Patches.Clear();

        float size = Mathf.Max(1f, settings.size);
        float threshold = Mathf.Max(1f, settings.pixelsPerCell);
        int budget = Mathf.Clamp(settings.maxPatches, 1, 1024);

        EvaluateAndAddLeaf(new Node());

        while (leaves.Count + 3 <= budget)
        {
            Node candidate = default;
            float largestError = threshold;
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