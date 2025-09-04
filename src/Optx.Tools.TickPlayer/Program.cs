using Optx.Data.Storage;

namespace Optx.Tools.TickPlayer;

public class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: TickPlayer <tick-file> [rate-multiplier]");
            Console.WriteLine("Example: TickPlayer data.bin 10.0");
            return;
        }

        var filePath = args[0];
        var rateMultiplier = args.Length > 1 ? double.Parse(args[1]) : 1.0;

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"File not found: {filePath}");
            return;
        }

        try
        {
            using var reader = new TickReader(filePath);
            
            Console.WriteLine($"Playing back ticks from {filePath} at {rateMultiplier}x speed");
            Console.WriteLine("Press Ctrl+C to stop");

            var tickCount = 0;
            var startTime = DateTime.UtcNow;
            var baseDelayMs = (int)(1000 / rateMultiplier); // Base delay between ticks

            while (reader.ReadTick(out var tick))
            {
                Console.WriteLine($"[{tickCount:D6}] {tick.Symbol.ToString()}: {tick.Price:F2} @ {tick.Quantity} ({tick.Type})");
                
                tickCount++;
                
                if (rateMultiplier > 0 && rateMultiplier < double.MaxValue)
                {
                    await Task.Delay(Math.Max(1, baseDelayMs));
                }
                
                if (tickCount % 1000 == 0)
                {
                    var elapsed = DateTime.UtcNow - startTime;
                    var ticksPerSecond = tickCount / elapsed.TotalSeconds;
                    Console.WriteLine($"--- Processed {tickCount} ticks, {ticksPerSecond:F0} ticks/sec ---");
                }
            }

            var totalElapsed = DateTime.UtcNow - startTime;
            Console.WriteLine($"Playback complete: {tickCount} ticks in {totalElapsed:g}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}