using System.Numerics;
using RT.Util.ExtensionMethods;

namespace EfisBlobs;

public partial class MainPage : ContentPage
{
    private ScreenPainter _painter = new();

    public MainPage()
    {
        InitializeComponent();
        gfx.Drawable = _painter;

        //var timer = Dispatcher.CreateTimer();
        //timer.Interval = TimeSpan.FromSeconds(0.001);
        //timer.Tick += (s, e) => gfx.Invalidate();
        //timer.Start();

        if (Gyroscope.Default.IsSupported)
        {
            Gyroscope.Default.ReadingChanged += (_, args) =>
            {
                _painter.Gyroscope(args.Reading);
                gfx.Invalidate();
            };
            Gyroscope.Default.Start(SensorSpeed.Fastest);
        }
        if (Barometer.Default.IsSupported)
        {
            Barometer.Default.ReadingChanged += (_, args) =>
            {
                _painter.Barometer(args.Reading);
                gfx.Invalidate();
            };
            Barometer.Default.Start(SensorSpeed.Fastest);
        }
        if (Compass.Default.IsSupported)
        {
            Compass.Default.ReadingChanged += (_, args) =>
            {
                _painter.Compass(args.Reading);
                gfx.Invalidate();
            };
            Compass.Default.Start(SensorSpeed.Fastest);
        }
        //if (Magnetometer.Default.IsSupported)
        //Microsoft.Maui.Devices.Sensors.Location
    }

    private void ContentPage_SizeChanged(object sender, EventArgs e)
    {
        _painter.Width = (float)gfx.Width;
        _painter.Height = (float)gfx.Height;
        gfx.Invalidate();
    }
}



public class ScreenPainter : IDrawable
{
    // todo: stop displaying if last update is too old
    public float Width, Height;
    public float FOV = 70f / 180f * MathF.PI;

    private Quaternion _orientation = Quaternion.Identity;
    private Blob[] _blobs;
    private FpsCounter _fpsDraw = new(), _fpsGyroscope = new(), _fpsBarometer = new(), _fpsCompass = new();
    private History _baroAltitude = new(), _compassHdg = new();

    public ScreenPainter()
    {
        _blobs = new Blob[200];
        for (int i = 0; i < _blobs.Length; i++)
            _blobs[i] = Blob.CreateRandom();
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Width <= 0 || Height <= 0)
            return;
        _fpsDraw.CountFrame();
        canvas.FontColor = Colors.Gray;
        canvas.DrawString($"draw: {_fpsDraw.AvgFps:0.0}   gyro: {_fpsGyroscope.AvgFps:0.0}   baro: {_fpsBarometer.AvgFps:0.0}   compass: {_fpsCompass.AvgFps:0.0}", new Rect(0, 0, Width, Height), HorizontalAlignment.Left, VerticalAlignment.Bottom);

        var wh = Math.Max(Width, Height);
        foreach (var b in _blobs)
        {
            var vec = Vector3.Transform(b.Location, Quaternion.Inverse(_orientation));
            if (vec.Z >= 0)
                continue;
            var cx = Width / 2 + vec.X / FOV * wh;
            var cy = Height / 2 - vec.Y / FOV * wh;
            var cr = b.AngularSize / FOV * wh;
            if (cx + cr > 0 && cy + cr > 0 && cx - cr < Width && cy - cr < Height)
            {
                canvas.FillColor = b.Color;
                canvas.FillCircle(cx, cy, cr);
            }
        }

        var hdg = _compassHdg.Last();
        if (hdg != null)
        {
            canvas.FontColor = Colors.Yellow;
            canvas.FontSize = 14;
            canvas.DrawString($"{hdg:0}°", new Rect(0, 0, Width, 20), HorizontalAlignment.Center, VerticalAlignment.Top);
        }

        var baroalt = _baroAltitude.Last();
        if (baroalt != null)
        {
            canvas.FontColor = Colors.White;
            canvas.FontSize = 20;
            canvas.FillColor = Colors.DarkBlue;
            canvas.FillRectangle(0, Height / 2 - 15, 70, 30);
            canvas.DrawString($"{baroalt:#,0}", new Rect(0, 0, Width, Height), HorizontalAlignment.Left, VerticalAlignment.Center);
        }
    }

    public void Gyroscope(GyroscopeData d)
    {
        var dt = (float)_fpsGyroscope.CountFrame();
        if (dt > 0)
        {
            var axis = d.AngularVelocity;
            var angle = axis.Length(); // rad/sec
            _orientation = Quaternion.Multiply(_orientation, Quaternion.CreateFromAxisAngle(axis / angle, angle * dt));
        }
    }

    public void Barometer(BarometerData d)
    {
        _fpsBarometer.CountFrame();
        var T0 = 288.15;
        var p0 = 101325;
        var p = d.PressureInHectopascals * 100;
        var alt = 504.7446 * T0 * (1 - Math.Exp((Math.Log(p) - Math.Log(p0)) / 5.2561)); // feet
        _baroAltitude.AddValue(alt);
    }

    private IFilter _compassFilter = Filters.BesselD40;

    public void Compass(CompassData d)
    {
        _fpsCompass.CountFrame();
        _compassHdg.AddValue(_compassFilter.Step(d.HeadingMagneticNorth));
    }
}



public class Blob
{
    public Vector3 Location;
    public float AngularSize; // radians
    public Color Color;

    public static Blob CreateRandom()
    {
        var b = new Blob();
        b.AngularSize = Random.Shared.NextSingle(1.5f, 3f) / 180f * MathF.PI;
        b.Color = Random.Shared.NextSingle() < 0.333f ? Colors.Red : Random.Shared.NextSingle() < 0.5f ? Colors.Lime : Colors.Yellow;
        var u = Random.Shared.NextSingle();
        var v = Random.Shared.NextSingle();
        var w = Random.Shared.NextSingle();
        var randrot = new Quaternion(MathF.Sqrt(1 - u) * MathF.Sin(2 * MathF.PI * v), MathF.Sqrt(1 - u) * MathF.Cos(2 * MathF.PI * v), MathF.Sqrt(u) * MathF.Sin(2 * MathF.PI * w), MathF.Sqrt(u) * MathF.Cos(2 * MathF.PI * w));
        b.Location = Vector3.Transform(new Vector3(0, 0, -1f), randrot);
        if (Math.Abs(b.Location.Length() - 1) > 0.01)
            throw new Exception();
        return b;
    }
}

public class FpsCounter
{
    private Queue<double> _times = new();
    private DateTime _last;
    public double CountFrame()
    {
        var t = DateTime.UtcNow;
        var dt = 0.0;
        if (_last != default)
        {
            dt = (DateTime.UtcNow - _last).TotalSeconds;
            _times.Enqueue(dt);
        }
        while (_times.Count > 100)
            _times.Dequeue();
        _last = t;
        return dt;
    }
    public double AvgFps => _times.Count < 5 ? 0 : 1.0 / _times.Average();
}

public class History
{
    private Queue<(DateTime t, double val)> _history = new();

    public void AddValue(double val)
    {
        _history.Enqueue((DateTime.UtcNow, val));
        while (_history.Peek().t < DateTime.UtcNow.AddSeconds(-70))
            _history.Dequeue();
    }

    public double? Last()
    {
        if (_history.Count == 0) return null;
        var last = _history.Last();
        if (last.t < DateTime.UtcNow.AddSeconds(-1))
            return null;
        return last.val;
    }

    // rate of change over 1 second
    // rate of change over 30 seconds (or value 30 seconds ago?)
}