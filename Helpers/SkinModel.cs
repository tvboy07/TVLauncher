using System;
using System.Numerics;

namespace TVLauncher.Helpers;

public class SkinModel
{
    // Modelo Steve (Classic - 4px arms)
    public static readonly float[] ClassicModelVertices = new float[]
    {
        // Head (8x8x8)
        -4, -8, -4,   4, -8, -4,   4, 0, -4,  -4, 0, -4,   // Front
        -4, -8,  4,  -4, 0,  4,   4, 0,  4,   4, -8,  4,   // Back
        -4, -8, -4,  -4, -8,  4,   4, -8,  4,   4, -8, -4,   // Bottom
        -4, 0, -4,    4, 0, -4,    4, 0,  4,   -4, 0,  4,    // Top
        -4, -8, -4,  -4, 0, -4,   -4, 0,  4,   -4, -8,  4,   // Left
         4, -8, -4,   4, -8,  4,    4, 0,  4,    4, 0, -4,    // Right
    };
    
    // Modelo Alex (Slim - 3px arms)
    public static readonly float[] SlimModelVertices = new float[]
    {
        // Head (8x8x8)
        -4, -8, -4,   4, -8, -4,   4, 0, -4,  -4, 0, -4,
        -4, -8,  4,  -4, 0,  4,   4, 0,  4,   4, -8,  4,
        -4, -8, -4,  -4, -8,  4,   4, -8,  4,   4, -8, -4,
        -4, 0, -4,    4, 0, -4,    4, 0,  4,   -4, 0,  4,
        -4, -8, -4,  -4, 0, -4,   -4, 0,  4,   -4, -8,  4,
         4, -8, -4,   4, -8,  4,    4, 0,  4,    4, 0, -4,
    };
    
    // UV coordinates para skin de 64x64
    public static readonly float[] SkinUVs = new float[]
    {
        // Head front
        8/64f, 8/64f, 16/64f, 8/64f, 16/64f, 16/64f, 8/64f, 16/64f,
        // Head back
        24/64f, 8/64f, 32/64f, 8/64f, 32/64f, 16/64f, 24/64f, 16/64f,
        // Head bottom
        16/64f, 0/64f, 8/64f, 0/64f, 8/64f, 8/64f, 16/64f, 8/64f,
        // Head top
        8/64f, 0/64f, 0/64f, 0/64f, 0/64f, 8/64f, 8/64f, 8/64f,
        // Head left
        0/64f, 8/64f, 8/64f, 8/64f, 8/64f, 16/64f, 0/64f, 16/64f,
        // Head right
        16/64f, 8/64f, 24/64f, 8/64f, 24/64f, 16/64f, 16/64f, 16/64f,
    };
    
    public static Matrix4x4 CreateRotationMatrix(float angleX, float angleY)
    {
        Matrix4x4 rotationX = Matrix4x4.CreateRotationX(angleX);
        Matrix4x4 rotationY = Matrix4x4.CreateRotationY(angleY);
        return rotationX * rotationY;
    }
    
    public static Matrix4x4 CreateProjectionMatrix(float fov, float aspectRatio, float near, float far)
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(fov, aspectRatio, near, far);
    }
    
    public static Matrix4x4 CreateViewMatrix()
    {
        return Matrix4x4.CreateLookAt(new Vector3(0, 0, 30), Vector3.Zero, Vector3.UnitY);
    }
}
