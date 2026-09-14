namespace Pleiades.CoreSim
{
    /// <summary>
    /// Circular Keplerian orbit in the ecliptic XZ plane (Y = up for camera).
    /// Position: (r·cos θ, 0, r·sin θ) in meters.
    /// </summary>
    public sealed class KeplerOrbit
    {
        public GravityBody Central { get; private set; }
        public double RadiusM { get; private set; }
        public double TrueAnomalyRad { get; private set; }
        public bool Prograde { get; private set; }

        public double Mu => Central.Mu;
        public double SpeedMps => AstroMath.CircularSpeed(Mu, RadiusM);
        public double PeriodS => AstroMath.Period(Mu, RadiusM);
        public double AngularRateRadS => PeriodS > 0.0 ? 2.0 * System.Math.PI / PeriodS : 0.0;

        public KeplerOrbit(GravityBody central, double radiusM, double trueAnomalyRad = 0.0, bool prograde = true)
        {
            Central = central;
            RadiusM = radiusM;
            TrueAnomalyRad = trueAnomalyRad;
            Prograde = prograde;
        }

        public static KeplerOrbit Circular(GravityBody central, double altitudeM, double anomalyRad = 0.0)
        {
            return new KeplerOrbit(central, central.RadiusM + altitudeM, anomalyRad, true);
        }

        public void GetPositionMeters(out double x, out double y, out double z)
        {
            double c = System.Math.Cos(TrueAnomalyRad);
            double s = System.Math.Sin(TrueAnomalyRad);
            x = RadiusM * c;
            y = 0.0;
            z = RadiusM * s;
        }

        /// <summary>Unit prograde direction in XZ (tangential).</summary>
        public void GetProgradeUnit(out double dx, out double dy, out double dz)
        {
            double sign = Prograde ? 1.0 : -1.0;
            dx = -System.Math.Sin(TrueAnomalyRad) * sign;
            dy = 0.0;
            dz = System.Math.Cos(TrueAnomalyRad) * sign;
        }

        public void Propagate(double dtSeconds)
        {
            if (dtSeconds == 0.0 || AngularRateRadS == 0.0) return;
            double dTheta = AngularRateRadS * dtSeconds * (Prograde ? 1.0 : -1.0);
            TrueAnomalyRad = WrapAngle(TrueAnomalyRad + dTheta);
        }

        /// <summary>
        /// Instantaneous prograde burn: change circular radius via vis-viva energy jump
        /// approximated as new circular orbit at current radius with adjusted speed →
        /// for sprint-1 we raise/lower circular altitude directly by Δv mapping.
        /// Δv applied: new v = v_circ + Δv_signed; then a from vis-viva at current r,
        /// and we snap to circular at apo/peri average (simple circularize helper).
        /// </summary>
        public void BurnPrograde(double deltaVMps)
        {
            if (RadiusM <= 0.0) return;
            double v0 = SpeedMps;
            double v1 = v0 + deltaVMps;
            if (v1 <= 0.0) return;

            // Energy → semi-major: v² = μ(2/r − 1/a) ⇒ 1/a = 2/r − v²/μ
            double invA = 2.0 / RadiusM - (v1 * v1) / Mu;
            if (invA <= 0.0)
            {
                // Escape / unbound — clamp to large circular for sprint-1
                RadiusM = System.Math.Max(RadiusM, Central.RadiusM * 2.0);
                return;
            }

            double a = 1.0 / invA;
            // After burn on circular, new orbit is elliptical; for continuous circular
            // play slice we immediately circularize conceptually at current r only when
            // Δv matches Hohmann legs (handled by SimWorld). Here: set radius to a
            // if burn was circularizing, else keep r and treat as elliptical mean.
            // Sprint-1 map view uses circular radius; adopt a as new circular radius
            // when |a − r| is the intended transfer effect at arrival.
            RadiusM = System.Math.Max(a, Central.RadiusM + 100_000.0);
        }

        /// <summary>Set circular orbit radius directly (after Hohmann arrival circularize).</summary>
        public void SetCircularRadius(double radiusM)
        {
            RadiusM = System.Math.Max(radiusM, Central.RadiusM + 50_000.0);
        }

        public void SetAnomaly(double rad) => TrueAnomalyRad = WrapAngle(rad);

        public static double WrapAngle(double rad)
        {
            const double twoPi = 2.0 * System.Math.PI;
            rad %= twoPi;
            if (rad < 0.0) rad += twoPi;
            return rad;
        }
    }
}
