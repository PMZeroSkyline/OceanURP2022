using System.Runtime.InteropServices;
using UnityEngine;

[StructLayout(LayoutKind.Sequential)]
public struct OceanPatch
{
    public const int Stride = sizeof(float) * 8;
    public Vector4 rect;
    public Vector4 edges;
}