namespace Pleiades.CoreSim
{
    /// <summary>Hohmann transfer between two coplanar circular orbits.</summary>
    public static class Hohmann
    {
        public struct Transfer
        {
            public double R1;
            public double R2;
            public double SemiMajor;
            public double DepartureDeltaV;
            public double ArrivalDeltaV;
            public double TotalDeltaV;
            public double TimeOfFlightSeconds;

            public double TotalDeltaVKmS => TotalDeltaV / 1000.0;
        }

        public static Transfer Compute(double mu, double r1, double r2)
        {
            var t = new Transfer { R1 = r1, R2 = r2 };
            if (mu <= 0.0 || r1 <= 0.0 || r2 <= 0.0)
                return t;

            t.SemiMajor = 0.5 * (r1 + r2);
            double v1 = AstroMath.CircularSpeed(mu, r1);
            double v2 = AstroMath.CircularSpeed(mu, r2);
            double vPeri = AstroMath.VisViva(mu, r1, t.SemiMajor);
            double vApo = AstroMath.VisViva(mu, r2, t.SemiMajor);

            // Departure: circular → transfer peri/apo
            t.DepartureDeltaV = System.Math.Abs(vPeri - v1);
            // Arrival: transfer → circular
            t.ArrivalDeltaV = System.Math.Abs(v2 - vApo);
            t.TotalDeltaV = t.DepartureDeltaV + t.ArrivalDeltaV;
            t.TimeOfFlightSeconds = System.Math.PI * System.Math.Sqrt(
                t.SemiMajor * t.SemiMajor * t.SemiMajor / mu);

            return t;
        }

        /// <summary>LEO 200 km → GSO. Expected total ≈ 3.94 km/s.</summary>
        public static Transfer LeoToGeo()
        {
            double rLeo = GravityBody.Earth.RadiusM + 200_000.0;
            return Compute(GravityBody.Earth.Mu, rLeo, GravityBody.GeoStationaryRadiusM);
        }

        /// <summary>LEO 200 km → lunar distance (simplified Earth-centric Hohmann to Moon orbit).</summary>
        public static Transfer LeoToLunar()
        {
            double rLeo = GravityBody.Earth.RadiusM + 200_000.0;
            return Compute(GravityBody.Earth.Mu, rLeo, GravityBody.MoonOrbitRadiusM);
        }
    }
}
