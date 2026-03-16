using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;
using Short_Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace RayTracer
{
    internal class Renderer : ShortRenderer
    {
        // <<GPU Stuff>> //
        public static Context context;
        public static Accelerator accelerator;

        // map, rays in, rays out
        public static MemoryBuffer1D<int, Stride1D.Dense> GPUMap;
        public static MemoryBuffer1D<float, Stride1D.Dense> rayAngles;
        private static float[] rayAnglesCPU;
        public static MemoryBuffer1D<GPURay, Stride1D.Dense> outputRays;
        private static GPURay[] outputRaysCPU;

        public static Action<Index1D, ArrayView<int>, ArrayView<float>, ArrayView<GPURay>, float, float, int, int> RunRayCalculations;


        public Renderer() : base(Flag.Auto_Draw_Clear)
        {
        }
        public static void Setup()
        { 
            // <<GPU Selection>> //
            context = Context.Create(builder => builder.Default().EnableAlgorithms());


            Device device;
            PriorityQueue<Device, float> deviceQueue = new PriorityQueue<Device, float>();
            foreach (Device tempDevice in context)
            {
                if (tempDevice.AcceleratorType == AcceleratorType.Cuda)
                {
                    deviceQueue.Enqueue(tempDevice, 0f);
                }
                else if (tempDevice.AcceleratorType == AcceleratorType.OpenCL)
                {
                    deviceQueue.Enqueue(tempDevice, 1f);
                }
                else
                {
                    deviceQueue.Enqueue(tempDevice, 2f);
                }
            }
            if (deviceQueue.Count == 0) { return; }
            device = deviceQueue.Dequeue();
            //device = context.GetPreferredDevice(false);


            // <<Accelerator Creation>> //
            accelerator = device.AcceleratorType switch
            {
                AcceleratorType.CPU => context.CreateCPUAccelerator(0),
                AcceleratorType.OpenCL => context.CreateCLAccelerator(0),
                AcceleratorType.Cuda => context.CreateCudaAccelerator(0),

                _ => throw new NoGPUFoundException() // something has gone horribly wrong, there should always be an accelerator
            };



            RunRayCalculations = 
                accelerator.LoadAutoGroupedStreamKernel
                <Index1D, ArrayView<int>, ArrayView<float>, ArrayView<GPURay>, float, float, int, int>
                (GPURayFunctions.RunRays);


            GPUMap = accelerator.Allocate1D(General.GPUMap);

            rayCount = 0;
            for (float angle = 0; angle < 2 * MathF.PI; angle += MathF.PI / (360f * raysPerDegree))
            {
                rayCount++;
            }

            rayAnglesCPU = new float[rayCount];
            rayAngles = accelerator.Allocate1D(rayAnglesCPU);
            outputRaysCPU = new GPURay[rayCount];
            outputRays = accelerator.Allocate1D(outputRaysCPU);
        }


        public float zoom = 70;
        const float raysPerDegree = 4;
        static int rayCount = -1;



        public override void Render()
        {
            General.handler.Move(dt);



            int xStart = (int)(Player.pos.X - (screenwidth / zoom)) - 1;
            int xEnd = (int)(Player.pos.X + (screenwidth / zoom)) + 1;
            int yStart = (int)(Player.pos.Y - (screenheight / zoom)) - 1;
            int yEnd = (int)(Player.pos.Y + (screenheight / zoom)) + 1;

            SDL2.SDL.SDL_SetRenderDrawColor(SDLrenderer, 255, 0, 0, 255);


            //<Index1D, ArrayView<int>, ArrayView<float>, ArrayView<GPURay>, float, float, int, int>
            // map, in angle, out rays

            int i = 0;
            for (float angle = 0; angle < 2 * MathF.PI; angle += MathF.PI / (360f * raysPerDegree))
            {
                rayAnglesCPU[i] = angle;
                outputRaysCPU[i] = new GPURay();
                i++;
            }

            rayAngles.CopyFromCPU(rayAnglesCPU);
            outputRays.CopyFromCPU(outputRaysCPU);

            // All data is ready to process
            RunRayCalculations((int)outputRays.Length, GPUMap.View, rayAngles.View, outputRays.View, Player.pos.X, Player.pos.Y, General.map.GetLength(0), General.map.GetLength(1));

            outputRaysCPU = outputRays.GetAsArray1D();
            foreach (GPURay ray in outputRaysCPU)
            { 
                if (ray.distance != -1)
                {
                    Vector2 hitPoint = ray.distance * new Vector2(ray.xDir, ray.yDir);
                    SDL2.SDL.SDL_RenderDrawPoint(SDLrenderer,
                        (int)(zoom * hitPoint.X) + (screenwidth / 2),
                        (int)(zoom * hitPoint.Y) + (screenheight / 2));
                }
            }

            SDL2.SDL.SDL_SetRenderDrawColor(SDLrenderer, 255, 255, 0, 255);

            SDL2.SDL.SDL_Rect player = new SDL2.SDL.SDL_Rect()
            {
                x = (int)((screenwidth - (zoom / 2)) / 2),
                y = (int)((screenheight - (zoom / 2)) / 2),
                h = (int)zoom / 2,
                w = (int)zoom / 2
            };
            SDL2.SDL.SDL_RenderDrawRect(SDLrenderer, ref player);

            SDL2.SDL.SDL_SetRenderDrawColor(SDLrenderer, 0, 0, 0, 255);
        }
    }



    internal class Handler : ShortHandler
    {
        public static Dictionary<string, bool> ActiveKeys = new Dictionary<string, bool>()
        {
            { "w", false },
            { "a", false },
            { "s", false },
            { "d", false }
        };


        public override void Handle(string inp, bool down)
        {
            if (ActiveKeys.ContainsKey(inp)) { ActiveKeys[inp] = down; }

            if (inp == "MouseWheel")
            {
                if (down)
                {
                    General.renderer.zoom *= 1.1f;
                }
                else
                {
                    General.renderer.zoom /= 1.1f;
                }
            }
        }

        const float speed = 3f;

        public void Move(long dt)
        {
            float delta = dt / 1000f;

            if (ActiveKeys["w"])
            {
                Player.pos.Y -= speed * delta;
                if (General.TileIsSolid(Player.pos) == (int)General.Solidness.Solid)
                {
                    Player.pos.Y += speed * delta;
                }
            }
            if (ActiveKeys["a"])
            {
                Player.pos.X -= speed * delta;
                if (General.TileIsSolid(Player.pos) == (int)General.Solidness.Solid)
                {
                    Player.pos.X += speed * delta;
                }
            }
            if (ActiveKeys["s"])
            {
                Player.pos.Y += speed * delta;
                if (General.TileIsSolid(Player.pos) == (int)General.Solidness.Solid)
                {
                    Player.pos.Y -= speed * delta;
                }
            }
            if (ActiveKeys["d"])
            {
                Player.pos.X += speed * delta;
                if (General.TileIsSolid(Player.pos) == (int)General.Solidness.Solid)
                {
                    Player.pos.X -= speed * delta;
                }
            }
        }
    }
}
