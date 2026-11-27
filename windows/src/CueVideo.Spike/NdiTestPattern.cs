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

    /// <summary>
    /// Several senders with clearly different pictures (hue plus a large digit), so
    /// switching sources visibly changes what the operator sees.
    /// </summary>
    public static int RunMulti(string[] args)
    {
        var count = args.Length > 0 && int.TryParse(args[0], out var parsedCount) ? parsedCount : 6;
        var seconds = args.Length > 1 && int.TryParse(args[1], out var parsedSeconds) ? parsedSeconds : 7200;

        if (!NdiRuntime.Initialize())
        {
            Console.WriteLine($"NDI runtime : {NdiRuntime.Error}");
            return 2;
        }

        const int width = 640;
        const int height = 360;
        const int stride = width * 4;

        var senders = new List<NdiSender>();
        var bases = new byte[count][];
        var frames = new byte[count][];

        for (var index = 0; index < count; index++)
        {
            var name = SourceName(index);
            senders.Add(NdiSender.Create(name));
            bases[index] = new byte[stride * height];
            frames[index] = new byte[stride * height];
            FillBase(bases[index], width, height, (index * 57) % 360);
            Console.WriteLine($"  NDI source '{name}'");
        }

        Console.WriteLine($"{count} NDI sources at {width}x{height}20 for {seconds}s. Ctrl+C to stop.");

        var until = DateTime.UtcNow.AddSeconds(seconds);
        var started = DateTime.UtcNow;
        var lastConnections = Enumerable.Repeat(-1, count).ToArray();

        while (DateTime.UtcNow < until)
        {
            var elapsed = (DateTime.UtcNow - started).TotalSeconds;

            for (var index = 0; index < count; index++)
            {
                var frame = frames[index];
                Buffer.BlockCopy(bases[index], 0, frame, 0, frame.Length);

                // A moving white bar, offset per source so the pictures are visibly alive.
                var barWidth = 26;
                var barX = (int)((elapsed * (90 + index * 15) + index * 50) % (width + barWidth)) - barWidth;
                for (var x = Math.Max(0, barX); x < Math.Min(width, barX + barWidth); x++)
                {
                    for (var y = 0; y < height; y++)
                    {
                        var i = y * stride + x * 4;
                        frame[i] = 240;
                        frame[i + 1] = 240;
                        frame[i + 2] = 240;
                    }
                }

                DrawDigit(frame, width, height, stride, index + 1);

                senders[index].Send(frame, width, height, stride, 20, 1);
            }

            // Report receiver connections when they change, so a demo can show one receiver
            // serving several tiles.
            var reported = false;
            for (var index = 0; index < count; index++)
            {
                var connections = senders[index].ConnectionCount();
                if (connections == lastConnections[index]) continue;

                lastConnections[index] = connections;
                Console.WriteLine($"connections {SourceName(index)}: {connections}");
                reported = true;
            }

            if (reported) Console.Out.Flush();

            Thread.Sleep(50);
        }

        foreach (var sender in senders) sender.Dispose();
        return 0;
    }

    private static string SourceName(int index) => $"CueVideo CAM{index + 1}";

    private static void FillBase(byte[] buffer, int width, int height, double hue)
    {
        var (r, g, b) = HsvToRgb(hue, 0.55, 0.85);
        var (darkR, darkG, darkB) = HsvToRgb(hue, 0.65, 0.28);

        for (var y = 0; y < height; y++)
        {
            var t = (double)y / height;
            var rowB = (byte)(darkB + (b - darkB) * t);
            var rowG = (byte)(darkG + (g - darkG) * t);
            var rowR = (byte)(darkR + (r - darkR) * t);

            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                buffer[i] = rowB;
                buffer[i + 1] = rowG;
                buffer[i + 2] = rowR;
                buffer[i + 3] = 255;
            }
        }
    }

    private static void DrawDigit(byte[] buffer, int width, int height, int stride, int digit)
    {
        var digitWidth = width / 6;
        var digitHeight = height / 2;
        var segment = Math.Max(6, digitWidth / 5);
        var left = (width - digitWidth) / 2;
        var top = (height - digitHeight) / 2;

        // Horizontal segments are width x thickness, vertical ones thickness x half height.
        var half = digitHeight / 2;

        void Horizontal(int col, int row)
        {
            FillRect(buffer, stride, width, height,
                left + (col == 1 ? 0 : 0),
                top + (row == 0 ? 0 : row == 1 ? half - segment / 2 : digitHeight - segment),
                digitWidth, segment);
        }

        void Vertical(int col, int row)
        {
            FillRect(buffer, stride, width, height,
                left + (col == 0 ? 0 : digitWidth - segment),
                top + (row == 0 ? 0 : half),
                segment, half);
        }

        switch (digit)
        {
            case 1:
                Vertical(1, 0);
                Vertical(1, 1);
                break;
            case 2:
                Horizontal(0, 0);
                Vertical(1, 0);
                Horizontal(0, 1);
                Vertical(0, 1);
                Horizontal(0, 2);
                break;
            case 3:
                Horizontal(0, 0);
                Vertical(1, 0);
                Horizontal(0, 1);
                Vertical(1, 1);
                Horizontal(0, 2);
                break;
            case 4:
                Vertical(0, 0);
                Vertical(1, 0);
                Horizontal(0, 1);
                Vertical(1, 1);
                break;
            case 5:
                Horizontal(0, 0);
                Vertical(0, 0);
                Horizontal(0, 1);
                Vertical(1, 1);
                Horizontal(0, 2);
                break;
            case 6:
                Horizontal(0, 0);
                Vertical(0, 0);
                Horizontal(0, 1);
                Vertical(0, 1);
                Vertical(1, 1);
                Horizontal(0, 2);
                break;
        }
    }

    private static void FillRect(byte[] buffer, int stride, int width, int height, int x, int y, int w, int h)
    {
        var x0 = Math.Max(0, x);
        var x1 = Math.Min(width, x + w);
        var y0 = Math.Max(0, y);
        var y1 = Math.Min(height, y + h);

        for (var py = y0; py < y1; py++)
        {
            for (var px = x0; px < x1; px++)
            {
                var i = py * stride + px * 4;
                buffer[i] = 250;
                buffer[i + 1] = 250;
                buffer[i + 2] = 250;
            }
        }
    }

    private static (byte R, byte G, byte B) HsvToRgb(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60) % 2 - 1));
        var m = value - c;

        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return ((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
