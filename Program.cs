using Hybridizer.Runtime.CUDAImports;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;
using Short_Tools;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using static RayTracer.General;
using static Short_Tools.General;





namespace RayTracer
{
    internal static class General
    {
        public static char[,] map = new char[,]
        {
            { ' ', ' ', ' ', ' ', ' ', 'X', 'X', 'X' },
            { ' ', 'X', ' ', 'X', ' ', 'O', ' ', 'X' },
            { ' ', 'X', ' ', 'X', 'X', 'X', ' ', 'X' },
            { ' ', 'X', ' ', 'X', ' ', 'X', ' ', 'X' },
            { ' ', ' ', ' ', ' ', ' ', 'X', ' ', 'X' },
            { ' ', ' ', ' ', 'X', ' ', 'X', ' ', ' ' },
            { 'O', 'X', ' ', 'X', ' ', 'X', 'X', ' ' },
            { ' ', 'X', ' ', 'X', ' ', 'X', 'X', ' ' },
            { ' ', 'X', ' ', ' ', ' ', 'X', 'X', ' ' },
            { ' ', 'X', ' ', ' ', ' ', 'O', ' ', ' ' },
            { 'X', 'X', 'O', 'O', 'O', 'X', 'X', 'X' },
            { ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ' },
            { ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ' },
            { ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ' },
            { ' ', ' ', ' ', '!', ' ', ' ', ' ', ' ' },
            { ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ' },
            { ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ' },
            { ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ' },
        };


        public static Renderer renderer = new Renderer();
        public static Handler handler = new Handler();
        public static bool Running = true;

        public static BlackHole[] blackHoles;

        public static bool DoBlackHoles = true;
        public static int[] GPUMap;

        public static int[] randomValues = new int[256]; // 0 - 9 

        

        private static void Main(string[] args)
        {
            GPUMap = new int[map.Length];
            for (int i = 0; i < map.Length; i++)
            {
                int x = i % map.GetLength(0);
                int y = i / map.GetLength(0);
                GPUMap[i] = TileIsSolid(x, y);
            }
            Random random = new Random();
            for (int i = 0; i < randomValues.Length; i++)
            {
                randomValues[i] = random.Next(0, 10);
            }


            Renderer.Setup();

            








            Player.pos = new Vector2(0, 0);//new Vector2(map.GetLength(0), map.GetLength(1)) / 2;


            List<BlackHole> holes = new List<BlackHole>();
            //holes.Add(new BlackHole(Pos: new Vector2(14, 3), Mass: 0.01f)); // Mass: 0.1f
            blackHoles = holes.ToArray();




            renderer.Start();

            


            while (Running)
            {
                handler.HandleInputs(ref Running);
                Thread.Sleep(20);
            }





            // <<Disposal>> //
            renderer.Stop();
            Renderer.accelerator.Dispose();
            Renderer.context.Dispose();
        }


        public enum Solidness : int
        {
            Walkable,
            Window,
            Hidden,
            Solid
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int TileIsSolid(int x, int y)
        {
            if (0 > x || x >= map.GetLength(0)) { return (int)Solidness.Solid; }
            if (0 > y || y >= map.GetLength(1)) { return (int)Solidness.Solid; }

            if (map[x, y] == 'X') { return (int)Solidness.Solid; }
            if (map[x, y] == 'O') { return (int)Solidness.Hidden; }
            return (int)Solidness.Walkable;
        }
        public static int TileIsSolid(Vector2 pos)
        {
            return TileIsSolid((int)pos.X, (int)pos.Y);
        }
    }







    




    internal readonly struct RayInfo
    {
        public readonly Vector2 Dir;
        public readonly float? Dist;

        public RayInfo(Vector2 Dir, float? Dist) 
        {
            this.Dir = Dir;
            this.Dist = Dist;
        }
    }







    internal readonly struct BlackHole
    {
        public readonly Vector2 Pos;
        public readonly float Mass;

        public BlackHole(Vector2 Pos, float Mass)
        {
            this.Pos = Pos;
            this.Mass = Mass;
        }
    }











    internal static class Ray
    {
        public static RayInfo GetRay(Vector2 Dir, int xStart, int xEnd, int yStart, int yEnd)
        {
            float? LowestDist = null;



            //SlightlyLessJankHits(Dir, out LowestDist);
            bool hit = JankRayHits(Dir, out LowestDist);
            if (!hit) { LowestDist = null; }


            return new RayInfo(Dir, LowestDist);
        }





        private static Random randy = new Random();
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static bool JankRayHits(Vector2 Dir, out float? dist)
        {
            Vector2 OrigDir = Dir;
            dist = null;
            float maxdist = 30;
            Vector2 PrevPos = Player.pos;
            float iStep = 0.01f;
            for (float i = 0; i < maxdist; i += 0.01f)
            {
                //Vector2 pos = Player.pos + (Dir * i);
                Vector2 pos = PrevPos + Dir * iStep;
                PrevPos = pos;
                int solidness = General.TileIsSolid((int)pos.X, (int)pos.Y);
                if (solidness == (int)Solidness.Solid)
                {
                    dist = i;
                    return true;
                }
                if (solidness == (int)Solidness.Hidden)
                {
                    if (randy.Next(0, 10) != 0) { return false; }
                    dist = i;
                    return true;
                }


                if (DoBlackHoles)
                {
                    foreach (BlackHole hole in blackHoles)
                    {
                        Vector2 delta = hole.Pos - pos;
                        if (delta.LengthSquared() > maxdist * maxdist) { continue; }
                        Vector2 Force = -0.5f * MathF.Sqrt((delta.LengthSquared() - ((Vector2.Dot(Dir, delta) / delta.Length())))) * new Vector2(Dir.Y, Dir.X * hole.Mass);


                        Dir += Force / (MathF.Pow(delta.LengthSquared(), 2f));
                    }
                    Dir = Vector2.Normalize(Dir);

                    if (Vector2.Dot(Dir, OrigDir) < -0.2f) { return false; }
                }
            }
            return false;
        }








        private static bool TestDistx(Vector2 Dir, int x, ref float? dist)
        {
            float tempDist = x / Dir.X;
            if (dist is not null && tempDist > dist) { return true; }


            Vector2 pos = Player.pos + (Dir * tempDist);

            int solidness = TileIsSolid(pos);
            if (solidness == (int)Solidness.Solid)
            {
                dist = tempDist;
                return true;
            }
            if (solidness == (int)Solidness.Hidden)
            {
                if (randy.Next(0, 10) != 0) { return false; }
                dist = tempDist;
                return true;
            }
            return false;
        }


        private static bool TestDisty(Vector2 Dir, int y, ref float? dist)
        {
            float tempDist = y / Dir.Y;
            if (dist is not null && tempDist > dist) { return true; }
            
            Vector2 pos = Player.pos + (Dir * tempDist);

            int solidness = TileIsSolid(pos);
            if (solidness == (int)Solidness.Solid)
            {
                dist = tempDist;
                return true;
            }
            if (solidness == (int)Solidness.Hidden)
            {
                if (randy.Next(0, 10) != 0) { return false; }
                dist = tempDist;
                return true;
            }
            return false;
        }





        private static void SlightlyLessJankHits(Vector2 Dir, out float? dist)
        {
            dist = null;
            float maxdist = 50;

            if (Dir.X > 0)
            {
                for (int x = 0; x < maxdist; x++)
                {
                    if (TestDistx(Dir, x, ref dist))
                    {
                        break;
                    }
                }
                for (int y = 0; y < maxdist; y++)
                {
                    if (TestDisty(Dir, y, ref dist))
                    {
                        break;
                    }
                }
            }
            else
            {
                for (int x = 0; x > -maxdist; x--)
                {
                    if (TestDistx(Dir, x, ref dist))
                    {
                        break;
                    }
                }
                for (int y = 0; y > -maxdist; y--)
                {
                    if (TestDisty(Dir, y, ref dist))
                    {
                        break;
                    }
                }
            }
        }




















        static readonly float squareWidth = 1f;
        static readonly Vector2 squareVect = new Vector2(squareWidth, -squareWidth);


        private static bool RayHits(int x, int y, Vector2 Dir, out float? dist)
        {
            float? lowestDist = null;


            GetLambdas(Dir, new Vector2(0, 1), new Vector2(x, y), out float l1, out float l2);
            if (0 <= l1 && l1 <= squareWidth)
            {
                if (l2 > 0 && (lowestDist is null || l2 < lowestDist))
                {
                    lowestDist = l2;
                }
            }

            GetLambdas(Dir, new Vector2(1, 0), new Vector2(x, y), out l1, out l2);
            if (0 <= l1 && l1 <= squareWidth)
            {
                if (l2 > 0 && (lowestDist is null || l2 < lowestDist))
                {
                    lowestDist = l2;
                }
            }

            GetLambdas(Dir + squareVect, new Vector2(0, -1), new Vector2(x, y), out l1, out l2);
            if (0 <= l1 && l1 <= squareWidth)
            {
                if (l2 > 0 && (lowestDist is null || l2 < lowestDist))
                {
                    lowestDist = l2;
                }
            }

            GetLambdas(Dir + squareVect, new Vector2(-1, 0), new Vector2(x, y), out l1, out l2);
            if (0 <= l1 && l1 <= squareWidth)
            {
                if (l2 > 0 && (lowestDist is null || l2 < lowestDist))
                {
                    lowestDist = l2;
                }
            }


            dist = lowestDist;
            if (dist is null) { return false; }
            return true;
        }


        private static void GetLambdas(Vector2 rayDir, Vector2 squareDir, Vector2 squarePos, out float lambda1, out float lambda2)
        {
            float x1 = squareDir.X; float x2 = rayDir.X;
            float y1 = squareDir.Y; float y2 = rayDir.Y;
            float ax = squarePos.X; float ay = squarePos.Y;
            float px = Player.pos.X; float py = Player.pos.Y;


            float discrim = 1 / ((x2 * y1) - (x1 * y2));

            lambda1 = discrim * ((x2 * (py - ay)) - (y2 * (ax - px)));
            lambda2 = discrim * ((x1 * (py - ay)) - (y1 * (ax - px)));
        }
    }













    internal static class Player
    {
        public static Vector2 pos;
    }


















    public class NoGPUFoundException : Exception
    {
        public NoGPUFoundException() : base($"No GPU was found and so the program cannot continue.") { }
    }
}