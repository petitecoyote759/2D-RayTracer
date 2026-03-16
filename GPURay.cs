using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static RayTracer.General;

namespace RayTracer
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GPURay
    {
        public float xDir;
        public float yDir;
        public float distance;
    }


        
    internal static class GPURayFunctions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetSolidness(int x, int y, int width, int height, ArrayView<int> map)
        {
            if (0 > x || x >= width) { return (int)Solidness.Solid; }
            if (0 > y || y >= height) { return (int)Solidness.Solid; }

            return map[x + (y * width)];
        }

        public static void RunRays(Index1D i, ArrayView<int> map, ArrayView<float> angles, ArrayView<GPURay> output, float pX, float pY, int mapWidth, int mapHeight)
        {
            GPURay ray = new GPURay();

            ray.xDir = MathF.Cos(angles[i]);
            ray.yDir = MathF.Sin(angles[i]);


            float distance = -1f;
            float maxDist = 30f;
            float iStep = 0.01f;

            float rayX = pX;
            float rayY = pY;

            float prevX = pX;
            float prevY = pY;

            for (float step = 0; step < maxDist; step += iStep)
            {
                //Vector2 pos = Player.pos + (Dir * i);
                rayX += ray.xDir * iStep;
                rayY += ray.yDir * iStep;

                if ((prevX % 1) == (rayX % 1) && (prevY % 1) == (rayY % 1)) { continue; }

                prevX = rayX; prevY = rayY;

                int solidness = GetSolidness((int)rayX, (int)rayY, mapWidth, mapHeight, map);
                if (solidness == 3) // Solidness.Solid
                {
                    distance = step;
                    break;
                }
                if (solidness == 2) // Solidness.Hidden
                {
                    int modifier = (int)(rayX + rayY) * (int)(step / iStep);
                    if ((modifier & 1) == 0) { break; }
                    distance = step;
                    break;
                }
            }


            ray.distance = distance;
            output[i] = ray;
        }
    }
}
