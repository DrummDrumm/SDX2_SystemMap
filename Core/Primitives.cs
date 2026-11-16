using System;

namespace SDX2_SystemMap;

/// <summary>A world position or direction in metres (double precision: SDX2 spans ±50,000 km).</summary>
public struct V3
{
    public double X, Y, Z;
    public V3(double x, double y, double z) { X = x; Y = y; Z = z; }

    public static readonly V3 Zero = new V3(0, 0, 0);
    public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3 operator -(V3 a) => new V3(-a.X, -a.Y, -a.Z);
    public static V3 operator *(V3 a, double k) => new V3(a.X * k, a.Y * k, a.Z * k);
    public static V3 operator *(double k, V3 a) => a * k;
    public static V3 operator /(V3 a, double k) => new V3(a.X / k, a.Y / k, a.Z / k);

    public double Dot(V3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public V3 Cross(V3 b) => new V3(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double LengthSq => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSq);
    public V3 Normalized { get { double l = Length; return l > 1e-12 ? this / l : Zero; } }
    public V3 Flat => new V3(X, 0, Z);

    public static V3 Lerp(V3 a, V3 b, double t) => a + (b - a) * t;
    public static double Distance(V3 a, V3 b) => (a - b).Length;

    /// <summary>Distance from p to the segment a-b, and where along it (0..1) the closest point is.</summary>
    public static double SegmentDistance(V3 a, V3 b, V3 p, out double t)
    {
        var ab = b - a;
        double l2 = ab.LengthSq;
        t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, (p - a).Dot(ab) / l2));
        return Distance(a + ab * t, p);
    }

    public override string ToString() => $"({X:0},{Y:0},{Z:0})";
}

/// <summary>Straight (not premultiplied) RGBA colour.</summary>
public struct MapColor
{
    public byte R, G, B, A;
    public MapColor(int r, int g, int b, int a = 255) { R = (byte)r; G = (byte)g; B = (byte)b; A = (byte)a; }

    public static MapColor Rgb(int rgb, int a = 255) => new MapColor((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255, a);

    public MapColor WithAlpha(float k) => new MapColor(R, G, B, (int)Math.Max(0, Math.Min(255, A * k)));

    public static MapColor Lerp(MapColor a, MapColor b, float t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return new MapColor((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t), (int)(a.A + (b.A - a.A) * t));
    }

    public static readonly MapColor White = new MapColor(255, 255, 255);
    public static readonly MapColor Black = new MapColor(0, 0, 0);
}

public enum TextAnchor { Left, Center, Right }

/// <summary>
/// The few 2D primitives the map needs. The game draws them with screen sprites (GameCanvas), the test runner with
/// GDI+ (GdiMapCanvas), so the whole scene can be rendered and checked outside the game.
/// </summary>
public interface IMapCanvas
{
    /// <summary>Filled rectangle; rotation (radians) is around its centre.</summary>
    void Rect(float x, float y, float w, float h, MapColor col, float rotation = 0);
    void Line(float x1, float y1, float x2, float y2, float width, MapColor col);
    void Disc(float cx, float cy, float r, MapColor col);
    /// <summary>Text whose top is at y; px = line height in pixels.</summary>
    void Text(string text, float x, float y, float px, MapColor col, TextAnchor anchor = TextAnchor.Left);
    float TextWidth(string text, float px);
    /// <summary>Only draw inside this rectangle until PopClip.</summary>
    void PushClip(float x, float y, float w, float h);
    void PopClip();
}

/// <summary>A screen rectangle.</summary>
public struct Box
{
    public float X, Y, W, H;
    public Box(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
    public float Right => X + W;
    public float Bottom => Y + H;
    public bool Contains(float px, float py) => px >= X && px <= X + W && py >= Y && py <= Y + H;
    public bool Overlaps(Box b) => X < b.X + b.W && b.X < X + W && Y < b.Y + b.H && b.Y < Y + H;
    public Box Inflate(float d) => new Box(X - d, Y - d, W + 2 * d, H + 2 * d);
}

public static class Fmt
{
    private static readonly System.Globalization.CultureInfo C = System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>"850 m", "12.4 km", "1,250 km", "12.5 Mm".</summary>
    public static string Dist(double m)
    {
        double a = Math.Abs(m);
        if (a < 1000) return m.ToString("0", C) + " m";
        if (a < 100e3) return (m / 1000).ToString("0.0", C) + " km";
        if (a < 10e6) return (m / 1000).ToString("#,0", C) + " km";
        return (m / 1e6).ToString("0.00", C) + " Mm";
    }

    public static string Speed(double ms) => Math.Abs(ms) < 1000 ? ms.ToString("0", C) + " m/s" : (ms / 1000).ToString("0.00", C) + " km/s";

    /// <summary>"45 s", "12 m 05 s", "3 h 12 m".</summary>
    public static string Time(double s)
    {
        if (double.IsNaN(s) || double.IsInfinity(s) || s < 0) return "--";
        if (s < 60) return s.ToString("0", C) + " s";
        if (s < 3600) return ((int)(s / 60)).ToString(C) + " m " + ((int)(s % 60)).ToString("00", C) + " s";
        return ((int)(s / 3600)).ToString(C) + " h " + ((int)(s % 3600 / 60)).ToString("00", C) + " m";
    }

    public static string Num(double v, string f) => v.ToString(f, C);
}
