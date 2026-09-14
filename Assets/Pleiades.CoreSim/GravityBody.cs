namespace Pleiades.CoreSim
{
    public sealed class GravityBody
    {
        public string Name { get; }
        public double MassKg { get; }
        public double RadiusM { get; }
        public double Mu => AstroMath.Mu(MassKg);

        public GravityBody(string name, double massKg, double radiusM)
        {
            Name = name;
            MassKg = massKg;
            RadiusM = radiusM;
        }

        public static GravityBody Earth { get; } = new GravityBody(
            "Земля",
            5.972e24,
            6_371_000.0);

        public static GravityBody Moon { get; } = new GravityBody(
            "Луна",
            7.342e22,
            1_737_400.0);

        /// <summary>Mean Earth–Moon distance.</summary>
        public const double MoonOrbitRadiusM = 384_400_000.0;

        /// <summary>Geostationary radius from Earth center (~42 164 km).</summary>
        public const double GeoStationaryRadiusM = 42_164_000.0;
    }
}
