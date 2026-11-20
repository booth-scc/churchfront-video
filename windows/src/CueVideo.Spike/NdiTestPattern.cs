using CueVideo.Ndi;

namespace CueVideo.Spike;

/// <summary>Sends a moving test pattern over NDI so the UI has something to show.</summary>
internal static class NdiTestPattern
{
    public static int Run(string[] args)
    {
        var name = args.Length > 0 ? args[0] : "CueVideo Test Pattern";
        var seconds = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : 120;

        if (!NdiRuntime.Initialize())
        {
            Console.WriteLine($"NDI runtime : {NdiRuntime.Error}");
            return 2;
        }

        const int width = 1280;
        const int height = 720;
        const int stride = width * 4;
        const int barWidth = 60;

        var baseFrame = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * stride + x * 4;
                baseFrame[i] = (byte)(x * 255 / width);            // B
                baseFrame[i + 1] = (byte)(y * 255 / height);       // G
                baseFrame[i + 2] = (byte)((x + y) * 255 / (width + height)); // R
                baseFrame[i + 3] = 255;
            }
        }

        using var sender = NdiSender.Create(name);
        Console.WriteLine($"NDI test pattern '{name}' at {width}x{height}30 for {seconds}s. Ctrl+C to stop.");

        var frame = new byte[baseFrame.Length];
        var until = DateTime.UtcNow.AddSeconds(seconds);
        var count = 0;

        while (DateTime.UtcNow < until)
        {
            Buffer.BlockCopy(baseFrame, 0, frame, 0, frame.Length);

            var barX = (int)((DateTime.UtcNow.TimeOfDay.TotalSeconds * 240) % (width + barWidth)) - barWidth;
            for (var x = Math.Max(0, barX); x < Math.Min(width, barX + barWidth); x++)
            {
                for (var y = 0; y < height; y++)
                {
                    var i = y * stride + x * 4;
                    frame[i] = 245;
                    frame[i + 1] = 245;
                    frame[i + 2] = 245;
                }
            }

            sender.Send(frame, width, height, stride, 30, 1);
            count++;

            var target = until - DateTime.UtcNow;
            if (count % 30 == 0) Console.WriteLine($"  sent {count} frames, {target.TotalSeconds:0}s left");
            Thread.Sleep(33);
        }

        return 0;
    }
}
