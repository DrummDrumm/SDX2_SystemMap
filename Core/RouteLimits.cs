using System;

namespace SDX2_SystemMap;

/// <summary>
/// How hard the autopilot may fly, set in the route panel: a top speed, and a thrust cap given either as a share of
/// the drives' thrust or as the largest drive signature (Spectrum) you're willing to show.
/// </summary>
public sealed class RouteLimits
{
    public const double MinFraction = 0.02, MinThrustPct = 5;
    /// <summary>Top speed, m/s; 0 = Flip and Burn's cap.</summary>
    public double MaxSpeed;
    /// <summary>False: the thrust cap is <see cref="ThrustPct"/>. True: it follows from <see cref="SignatureKm"/>.</summary>
    public bool BySignature;
    /// <summary>Thrust caps, % of the drives' thrust: burning toward a point, and braking.</summary>
    public double ThrustPct = 100, BrakePct = 100;
    /// <summary>Largest drive signature while burning and while braking, as the exhaust cone's Weak range in km: how far
    /// behind the drives a baseline sensor first picks the ship up (gain × the all-round range, twice the Strong range);
    /// 0 = no cap.</summary>
    public double SignatureKm, BrakeSignatureKm;
    /// <summary>Route legs: true = parabolic (curve through each direction change without stopping), false = flip and burn (slow to turn at each).</summary>
    public bool Parabolic;

    public RouteLimits Clone() => (RouteLimits)MemberwiseClone();

    /// <summary>The share of the drives' thrust the autopilot may use burning toward a point. weight = the ship's summed drive signal weight.</summary>
    public double ThrustFraction(double weight, double gain = 4) => Fraction(ThrustPct, SignatureKm, weight, gain);
    /// <summary>The share of the drives' thrust the autopilot may use braking.</summary>
    public double BrakeFraction(double weight, double gain = 4) => Fraction(BrakePct, BrakeSignatureKm, weight, gain);

    private double Fraction(double pct, double km, double weight, double gain)
    {
        if (!BySignature) return Clamp(pct / 100, MinThrustPct / 100, 1);
        if (weight <= 0 || km <= 0) return 1;
        return Clamp(DriveSignature.FractionForWeakCone(km, weight, gain), MinFraction, 1);
    }

    /// <summary>The speed cap to fly: the lower of ours and Flip and Burn's.</summary>
    public double SpeedCap(double fnbCap) => MaxSpeed > 0 && (fnbCap <= 0 || MaxSpeed < fnbCap) ? MaxSpeed : fnbCap;

    private static double Clamp(double v, double lo, double hi) => double.IsNaN(v) ? hi : Math.Max(lo, Math.Min(hi, v));
}

/// <summary>
/// SDX2 drive signatures as Spectrum (workshop 3580536771) works them out, checked against its code 2026-11-15.
/// Each drive emits an Optical directional emission of MaxStrength × thrust % (3x3 3e9, 5x5 6e9, 7x7 12e9, 9x9 18e9,
/// Torch 1e9), gain 4 (Torch 2). Drives facing one way merge into one emission. Spectrum's HUD then shows, for the grid:
///  - all round (sphere icon): the strongest single emission's strength, from drives, antenna/beacon broadcast
///    (strength for its radius), active radar or power draw (MW × 1e6);
///  - exhaust cone (arrow icon): the strongest directional emission × gain².
/// Each as two ranges, range = √(strength / threshold / 4π): bright = strong detection (threshold 0.4), dim = weak (0.1,
/// twice as far). With W = Σ weight × thrust share, the drives' bright all-round range is 24.43·√W km.
/// </summary>
public static class DriveSignature
{
    public const double ThermalK = 24.43, ExhaustK = 97.72;
    /// <summary>Spectrum's thresholds at baseline (1.0x) sensors (Utils.BaseThreshold ×1, ×4, ×16): each tier is seen at half
    /// the range of the one below. Weak = distance only, Strong = faction and colour, Detailed = also the ship core.</summary>
    public const double StrengthPerWeight = 3e9, WeakThreshold = 0.1, StrongThreshold = 0.4, DetailedThreshold = 1.6;
    /// <summary>Sensor sensitivities from the SDX2 wiki: none (suit, or no detector for that band) 0.5x range, baseline
    /// (Radio Detector, Lidar, Sensor Array) 1.0x, directional (Radio Dish, Optical Scope, Sensor Panel) 1.4x,
    /// advanced (Advanced Radio Detector, Advanced Optical Dish; 15° forward) 2.0x. Same-band detectors don't stack.</summary>
    public const double NoSensor = 0.5, Baseline = 1.0, Directional = 1.4, Advanced = 2.0;

    /// <summary>How a sensor with this range multiplier sees an emission of this strength from this far away.</summary>
    public static SignalTier Classify(double strength, double km, double sensorRange = Baseline)
    {
        if (strength <= 0) return SignalTier.None;
        double r = Math.Max(1, km * 1000), got = strength / (4 * Math.PI * r * r) * sensorRange * sensorRange;
        return got > DetailedThreshold ? SignalTier.Detailed : got > StrongThreshold ? SignalTier.Strong : got > WeakThreshold ? SignalTier.Weak : SignalTier.None;
    }

    /// <summary>"Detailed / Strong / Weak" ranges in km for an emission, at baseline sensors.</summary>
    public static string Tiers(double strength) =>
        strength <= 0 ? "0" : Km(RangeKm(strength, DetailedThreshold)) + "/" + Km(RangeKm(strength, StrongThreshold)) + "/" + Km(RangeKm(strength, WeakThreshold));
    private static string Km(double km) => km < 10 ? km.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : km.ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A drive's signal weight from its subtype (3x3 = 1, 5x5 = 2, 7x7 = 4, 9x9 = 6, Torch = ⅓); 0 if not a fusion drive.</summary>
    public static double Weight(string subtype)
    {
        if (subtype == null || !subtype.StartsWith("sdx_drive", StringComparison.Ordinal)) return 0;
        if (IsTorch(subtype)) return 1.0 / 3;
        if (subtype.Contains("9x9")) return 6;
        if (subtype.Contains("7x7")) return 4;
        if (subtype.Contains("5x5")) return 2;
        return 1;
    }

    /// <summary>The drive's exhaust-cone gain: 4, the Torch 2.</summary>
    public static double Gain(string subtype) => IsTorch(subtype) ? 2 : 4;
    private static bool IsTorch(string subtype) => subtype != null && subtype.IndexOf("Torch", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Detection range in km of an emission strength at a sensor threshold.</summary>
    public static double RangeKm(double strength, double threshold = StrongThreshold) =>
        strength <= 0 ? 0 : Math.Sqrt(strength / threshold / 4 / Math.PI) / 1000;
    /// <summary>The strength that is detected at this range (Spectrum's GetStrengthForRange).</summary>
    public static double StrengthForKm(double km, double threshold = StrongThreshold) => threshold * 4 * Math.PI * km * 1000 * km * 1000;

    /// <summary>The drives' own bright all-round range, km.</summary>
    public static double Thermal(double weight, double fraction) => ThermalK * Math.Sqrt(Math.Max(0, weight * fraction));
    /// <summary>The drives' bright exhaust-cone range for gain 4, km.</summary>
    public static double Exhaust(double weight, double fraction) => ExhaustK * Math.Sqrt(Math.Max(0, weight * fraction));

    /// <summary>The drives' Weak all-round range (first detection, baseline sensors), km: twice the Strong one.</summary>
    public static double WeakAllRound(double weight, double fraction) => 2 * Thermal(weight, fraction);
    /// <summary>The thrust share that gives this Weak all-round range from the drives (can be over 1).</summary>
    public static double FractionForWeak(double weakKm, double weight) => FractionFor(weakKm / 2, weight);
    /// <summary>The drives' Weak exhaust-cone range (first detection from behind the drives, baseline sensors), km:
    /// gain × the Weak all-round range (gain 4, Torch 2).</summary>
    public static double WeakCone(double weight, double fraction, double gain = 4) => gain * WeakAllRound(weight, fraction);
    /// <summary>The thrust share that gives this Weak exhaust-cone range (can be over 1).</summary>
    public static double FractionForWeakCone(double weakKm, double weight, double gain = 4) => FractionForWeak(weakKm / (gain > 0 ? gain : 4), weight);

    /// <summary>The thrust share that gives this bright all-round range from the drives (can be over 1: the cap doesn't bite).</summary>
    public static double FractionFor(double thermalKm, double weight) =>
        weight <= 0 ? 1 : (thermalKm / ThermalK) * (thermalKm / ThermalK) / weight;
}

/// <summary>How well a detector sees a signal (SDX2 wiki, Signals and Detection).</summary>
public enum SignalTier { None, Weak, Strong, Detailed }

/// <summary>What the ship shows Spectrum besides the drives' thrust, for the route panel's signature figures.</summary>
public sealed class SignatureInfo
{
    /// <summary>Summed drive signal weight (see <see cref="DriveSignature.Weight"/>); 0 = no working fusion drives.</summary>
    public double Weight;
    /// <summary>The drives' exhaust-cone gain (strength-weighted, as Spectrum merges them).</summary>
    public double Gain = 4;
    /// <summary>Strongest all-round emission that isn't the drives, and what it is. All of these are Radio band (antenna
    /// and beacon broadcast, power draw, radar); the drives are Optical, so a different detector picks each up.</summary>
    public double Floor;
    public string FloorWhat;
    /// <summary>Spectrum's own HUD figures for this ship now (strengths), or -1 when Spectrum isn't reachable.</summary>
    public double LiveAllRound = -1, LiveCone = -1;

    public double DriveStrength(double fraction) => Weight * DriveSignature.StrengthPerWeight * Math.Max(0, fraction);
    /// <summary>The all-round strength Spectrum's HUD will show while the drives run at this share of thrust.</summary>
    public double AllRound(double fraction) => Math.Max(DriveStrength(fraction), Floor);
    public double Cone(double fraction) => DriveStrength(fraction) * Gain * Gain;
}
