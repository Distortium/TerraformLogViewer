using System;
using System.Collections.Generic;

public class StarModel
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Size { get; set; }
    public double Delay { get; set; }
}

public static class StarsViewModel
{
    // Generates deterministic star positions so the UI is consistent across renders
    public static List<StarModel> GenerateStars(int count = 30, int width = 1200, int height = 800)
    {
        var stars = new List<StarModel>();
        var rnd = new Random(42); // fixed seed for consistent layout
        for (int i = 0; i < count; i++)
        {
            stars.Add(new StarModel
            {
                Left = rnd.Next(20, Math.Max(100, width - 20)),
                Top = rnd.Next(20, Math.Max(100, height - 20)),
                Size = rnd.Next(2, 6),
                Delay = Math.Round(rnd.NextDouble() * 3.0, 2)
            });
        }
        return stars;
    }
}