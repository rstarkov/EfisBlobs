﻿using System.Numerics;
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
        if (Magnetometer.Default.IsSupported)
        {
            Magnetometer.Default.ReadingChanged += (_, args) =>
            {
                _painter.Magnetometer(args.Reading);
                gfx.Invalidate();
            };
            Magnetometer.Default.Start(SensorSpeed.Fastest);
        }

        if (false)
        {
            var geotimer = Dispatcher.CreateTimer();
            geotimer.Interval = TimeSpan.FromSeconds(0.1);
            geotimer.Tick += async (s, e) =>
            {
                geotimer.Stop();
                var loc = await Geolocation.Default.GetLocationAsync(new() { RequestFullAccuracy = true, DesiredAccuracy = GeolocationAccuracy.Best, Timeout = TimeSpan.FromSeconds(10) });
                geotimer.Start();
                if (loc == null)
                    return;
                _painter.Geolocation(loc);
                gfx.Invalidate();
            };
            geotimer.Start();
        }
        if (false)
        {
            new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        var loc = Geolocation.Default.GetLocationAsync(new() { RequestFullAccuracy = true, DesiredAccuracy = GeolocationAccuracy.Best, Timeout = TimeSpan.FromSeconds(10) }).GetAwaiter().GetResult();
                        if (loc == null)
                            continue;
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            _painter.Geolocation(loc);
                            gfx.Invalidate();
                        });
                    }
                    catch { }
                }
            })
            { IsBackground = true }.Start();
        }
    }

    private void ContentPage_SizeChanged(object sender, EventArgs e)
    {
        _painter.Width = (float)gfx.Width;
        _painter.Height = (float)gfx.Height;
        gfx.Invalidate();
    }

    private async void gfx_StartInteraction(object sender, TouchEventArgs e)
    {
        if (e.Touches.Any(t => t.X <= 70 && t.Y >= gfx.Height / 2 - 30 && t.Y <= gfx.Height / 2 + 30))
        {
            var str = await DisplayPromptAsync("Pressure setting", "Enter QNH or QFE in millibars (hPa):", initialValue: _painter.QNH.ToString());
            if (str == null)
                return;
            if (int.TryParse(str, out var qnh) && qnh > 800 && qnh < 1500)
                _painter.QNH = qnh;
            else
                await DisplayAlert("Pressure setting", "Please enter an integer between 800 and 1500.", "OK");
        }
    }
}



public class ScreenPainter : IDrawable
{
    // todo: stop displaying if last update is too old
    public float Width, Height;
    public float FOV = 70f / 180f * MathF.PI;

    private Quaternion _orientation = Quaternion.Identity;
    private Blob[] _blobs;
    private FpsCounter _fpsDraw = new(), _fpsGyroscope = new(), _fpsBarometer = new(), _fpsCompass = new(), _fpsGps = new(), _fpsMag = new();
    private History _baroAltitude = new(), _compassHdg = new(), _gpsAltitude = new(), _gpsSpeed = new();
    private Vector3 _magnetic;
    public int QNH = 1013;

    public ScreenPainter()
    {
        var minDist = 10f * MathF.PI / 180f; // minimum angular distance between the edges of blobs
        _blobs = new Blob[200];
        for (int i = 0; i < _blobs.Length; i++)
        {
            int retries = 0;
        again:;
            _blobs[i] = Blob.CreateRandom();
            if (i >= 150)
            {
                // _blobs[i].Trace = new();
                _blobs[i].AngularRadius = 0.5f / 180f * MathF.PI;
                _blobs[i].Color = Colors.White;
            }
            if (_blobs[0..i].Any(b => Math.Asin(Vector3.Cross(b.Location, _blobs[i].Location).Length()) < b.AngularRadius + _blobs[i].AngularRadius + minDist))
            {
                retries++;
                if (retries > 500)
                {
                    minDist *= 0.9f;
                    retries = 0;
                }
                goto again;
            }
        }
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Width <= 0 || Height <= 0)
            return;
        _fpsDraw.CountFrame();
        canvas.FontColor = Colors.Gray;
        canvas.DrawString($"draw: {_fpsDraw.AvgFps:0.0}   gyro: {_fpsGyroscope.AvgFps:0.0}   baro: {_fpsBarometer.AvgFps:0.0}   compass: {_fpsCompass.AvgFps:0.0}   mag: {_fpsMag.AvgFps:0.0}   gps: {_fpsGps.AvgFps:0.0}", new Rect(0, 0, Width, Height), HorizontalAlignment.Left, VerticalAlignment.Bottom);

        var wh = Math.Max(Width, Height);

        foreach (var b in _blobs.Where(b => b.Trace != null))
        {
            const int seconds = 1;
            while (b.Trace.Count > 0 && b.Trace.Peek().t < DateTime.UtcNow.AddSeconds(-seconds))
                b.Trace.Dequeue();
            PointF prev = default;
            foreach (var pt in b.Trace)
            {
                if (prev != default)
                {
                    canvas.StrokeColor = new Color(1f - (float)(DateTime.UtcNow - pt.t).TotalSeconds / seconds);
                    canvas.DrawLine(prev, pt.pos);
                }
                prev = pt.pos;
            }
        }

        foreach (var b in _blobs)
        {
            var vec = Vector3.Transform(b.Location, Quaternion.Inverse(_orientation));
            if (vec.Z >= 0)
                continue;
            var cx = Width / 2 + vec.X / FOV * wh;
            var cy = Height / 2 - vec.Y / FOV * wh;
            var cr = b.AngularRadius / FOV * wh;
            if (cx + cr > 0 && cy + cr > 0 && cx - cr < Width && cy - cr < Height)
            {
                canvas.FillColor = b.Color;
                canvas.FillCircle(cx, cy, cr);
            }
            if (b.Trace != null)
                b.Trace.Enqueue((DateTime.UtcNow, new PointF(cx, cy)));
        }

        var hdg = _compassHdg.Last();
        if (hdg != null)
        {
            canvas.FontColor = Colors.Yellow;
            canvas.FontSize = 14;
            canvas.DrawString($"{hdg % 360:0}°", new Rect(0, 0, Width, 20), HorizontalAlignment.Center, VerticalAlignment.Top);
        }

        var baroalt = _baroAltitude.Last();
        if (baroalt != null)
        {
            canvas.FontColor = Colors.White;
            canvas.FontSize = 20;
            canvas.FillColor = Colors.DarkBlue;
            canvas.FillRectangle(0, Height / 2 - 15, 70, 30);
            canvas.DrawString($"{baroalt:#,0} ft", new Rect(0, 0, Width, Height), HorizontalAlignment.Left, VerticalAlignment.Center);
        }

        var gpsalt = _gpsAltitude.Last(2);
        if (gpsalt != null)
        {
            canvas.FontColor = Colors.White;
            canvas.FontSize = 20;
            canvas.FillColor = Colors.DarkBlue;
            canvas.FillRectangle(Width - 70, Height / 2 - 15, 70, 30);
            canvas.DrawString($"{gpsalt:#,0}", new Rect(0, 0, Width, Height), HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        if (_magnetic != default)
        {
            var mv = Vector3.Transform(_magnetic, Quaternion.Inverse(_orientation));
            var mvu = mv / mv.Length();
            canvas.FillColor = Colors.Black;
            canvas.FillCircle(40, 40, 38);
            canvas.StrokeColor = Colors.Gray;
            canvas.DrawCircle(40, 40, 35);
            var mvu2 = new Vector2(mvu.X, -mvu.Y);
            canvas.DrawLine(40, 40, 40 + mvu2.X / mvu2.Length() * 33, 40 + mvu2.Y / mvu2.Length() * 33);
            canvas.StrokeSize = 2f;
            canvas.StrokeColor = Colors.Yellow;
            canvas.DrawLine(40, 40, 40 + mvu2.X * 33, 40 + mvu2.Y * 33);
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
        var p0 = QNH * 100;
        var p = d.PressureInHectopascals * 100;
        var alt = 504.7446 * T0 * (1 - Math.Exp((Math.Log(p) - Math.Log(p0)) / 5.2561)); // feet
        _baroAltitude.AddValue(alt);
    }

    private IFilter _compassFilter = Filters.BesselD40;

    public void Compass(CompassData d)
    {
        _fpsCompass.CountFrame();
        var hdg = d.HeadingMagneticNorth;
        // 359-0 wraparound doesn't play well with filtering - undo wraparound
        var last = _compassHdg.Last();
        if (last != null)
        {
            while (hdg - last.Value > 180) hdg -= 360;
            while (hdg - last.Value < -180) hdg += 360;
        }
        _compassHdg.AddValue(_compassFilter.Step(hdg));
    }

    public void Magnetometer(MagnetometerData d)
    {
        _fpsMag.CountFrame();
        _magnetic = d.MagneticField;
    }

    public void Geolocation(Location d)
    {
        if (d.Accuracy != null && d.Accuracy > 50)
            return;
        _fpsGps.CountFrame();
        if (d.Altitude != null)
            _gpsAltitude.AddValue(d.Altitude.Value * 3.2808399);
        //_gpsSpeed.AddValue(0);
    }
}



public class Blob
{
    public Vector3 Location;
    public float AngularRadius; // radians
    public Color Color;
    public Queue<(DateTime t, PointF pos)> Trace = null;

    public static Blob CreateRandom()
    {
        var b = new Blob();
        b.AngularRadius = Random.Shared.NextSingle(2.5f, 5f) / 180f * MathF.PI;
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

    public double? Last(double secondsAgeLimit = 1)
    {
        if (_history.Count == 0) return null;
        var last = _history.Last();
        if (last.t < DateTime.UtcNow.AddSeconds(-secondsAgeLimit))
            return null;
        return last.val;
    }

    // rate of change over 1 second
    // rate of change over 30 seconds (or value 30 seconds ago?)
}
