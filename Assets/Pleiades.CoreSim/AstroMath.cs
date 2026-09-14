namespace Pleiades.CoreSim
{
    /// <summary>
    /// Pure orbital / rocket math. Units: SI meters, kg, s unless noted.
    /// Sim display units: 1 uu = MetersPerUnit meters = 1000 km.
    /// </summary>
    public static class AstroMath
    {
        public const double G = 6.67430e-11;
        public const double G0 = 9.80665;

        /// <summary>1 Unity unit = 1e6 m = 1000 km.</summary>
        public const double MetersPerUnit = 1e6;

        public static double Mu(double massKg) => G * massKg;

        /// <summary>Vis-viva: v² = μ (2/r − 1/a).</summary>
        public static double VisViva(double mu, double r, double a)
        {
            if (r <= 0.0 || a == 0.0) return 0.0;
            double v2 = mu * (2.0 / r - 1.0 / a);
            return v2 > 0.0 ? System.Math.Sqrt(v2) : 0.0;
        }

        /// <summary>Circular orbit speed √(μ/r).</summary>
        public static double CircularSpeed(double mu, double r)
        {
            if (r <= 0.0) return 0.0;
            return System.Math.Sqrt(mu / r);
        }

        /// <summary>Orbital period for semi-major axis a: T = 2π √(a³/μ).</summary>
        public static double Period(double mu, double a)
        {
            if (a <= 0.0 || mu <= 0.0) return 0.0;
            return 2.0 * System.Math.PI * System.Math.Sqrt(a * a * a / mu);
        }

        /// <summary>Tsiolkovsky: Δv = Isp·g0·ln(m0/mf).</summary>
        public static double RocketDeltaV(double ispSeconds, double massWetKg, double massDryKg)
        {
            if (ispSeconds <= 0.0 || massWetKg <= massDryKg || massDryKg <= 0.0)
                return 0.0;
            return ispSeconds * G0 * System.Math.Log(massWetKg / massDryKg);
        }

        /// <summary>Fuel mass needed for Δv: mf = m0 · exp(−Δv/(Isp·g0)); fuel = m0 − mf.</summary>
        public static double FuelForDeltaV(double ispSeconds, double massWetKg, double deltaV)
        {
            if (ispSeconds <= 0.0 || massWetKg <= 0.0 || deltaV <= 0.0)
                return 0.0;
            double ratio = System.Math.Exp(-deltaV / (ispSeconds * G0));
            return massWetKg * (1.0 - ratio);
        }

        /// <summary>Sphere of influence radius ≈ a · (m/M)^(2/5).</summary>
        public static double SphereOfInfluence(double orbitSemiMajorM, double bodyMassKg, double parentMassKg)
        {
            if (orbitSemiMajorM <= 0.0 || bodyMassKg <= 0.0 || parentMassKg <= 0.0)
                return 0.0;
            return orbitSemiMajorM * System.Math.Pow(bodyMassKg / parentMassKg, 0.4);
        }

        public static double MetersToUnits(double meters) => meters / MetersPerUnit;
        public static double UnitsToMeters(double units) => units * MetersPerUnit;
    }
}
