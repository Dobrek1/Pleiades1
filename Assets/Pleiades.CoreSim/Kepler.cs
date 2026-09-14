namespace Pleiades.CoreSim
{
    /// <summary>
    /// Two-body state in ecliptic XZ (Y unused). Not circular-only.
    /// Coast = Kepler. Burn = add Δv or acceleration along a body axis.
    /// </summary>
    public sealed class KeplerOrbit
    {
        public GravityBody Central { get; private set; }
        public double Rx, Rz;
        public double Vx, Vz;

        public double Mu => Central.Mu;

        public double RadiusM
        {
            get
            {
                var r = System.Math.Sqrt(Rx * Rx + Rz * Rz);
                return r < 1.0 ? 1.0 : r;
            }
        }

        public double SpeedMps => System.Math.Sqrt(Vx * Vx + Vz * Vz);

        public double TrueAnomalyRad => System.Math.Atan2(Rz, Rx);

        public double SemiMajorM
        {
            get
            {
                var r = RadiusM;
                var v2 = Vx * Vx + Vz * Vz;
                var invA = 2.0 / r - v2 / Mu;
                if (invA <= 1e-18) return double.PositiveInfinity;
                return 1.0 / invA;
            }
        }

        public double Eccentricity
        {
            get
            {
                EccVec(out var ex, out var ez);
                return System.Math.Sqrt(ex * ex + ez * ez);
            }
        }

        public double PeriapsisM
        {
            get
            {
                var a = SemiMajorM;
                var e = Eccentricity;
                if (double.IsInfinity(a)) return RadiusM;
                return a * (1.0 - e);
            }
        }

        public double ApoapsisM
        {
            get
            {
                var a = SemiMajorM;
                var e = Eccentricity;
                if (double.IsInfinity(a) || e >= 1.0) return double.PositiveInfinity;
                return a * (1.0 + e);
            }
        }

        public double PeriodS
        {
            get
            {
                var a = SemiMajorM;
                if (double.IsInfinity(a) || a <= 0.0) return 0.0;
                return AstroMath.Period(Mu, a);
            }
        }

        public KeplerOrbit(GravityBody central, double rx, double rz, double vx, double vz)
        {
            Central = central;
            Rx = rx;
            Rz = rz;
            Vx = vx;
            Vz = vz;
        }

        public static KeplerOrbit Circular(GravityBody central, double altitudeM, double anomalyRad = 0.0)
        {
            var r = central.RadiusM + altitudeM;
            var v = AstroMath.CircularSpeed(central.Mu, r);
            var c = System.Math.Cos(anomalyRad);
            var s = System.Math.Sin(anomalyRad);
            return new KeplerOrbit(central, r * c, r * s, -v * s, v * c);
        }

        public void GetPositionMeters(out double x, out double y, out double z)
        {
            x = Rx;
            y = 0.0;
            z = Rz;
        }

        public void GetProgradeUnit(out double dx, out double dy, out double dz)
        {
            var sp = SpeedMps;
            if (sp < 1e-9)
            {
                dx = -Rz / RadiusM;
                dy = 0.0;
                dz = Rx / RadiusM;
                return;
            }
            dx = Vx / sp;
            dy = 0.0;
            dz = Vz / sp;
        }

        public void GetRadialUnit(out double dx, out double dy, out double dz)
        {
            var r = RadiusM;
            dx = Rx / r;
            dy = 0.0;
            dz = Rz / r;
        }

        public void ApplyDeltaV(double dvPrograde, double dvRadial)
        {
            GetProgradeUnit(out var px, out _, out var pz);
            GetRadialUnit(out var rx, out _, out var rz);
            Vx += px * dvPrograde + rx * dvRadial;
            Vz += pz * dvPrograde + rz * dvRadial;
        }

        public void ApplyAccel(double accPrograde, double accRadial, double dt)
        {
            ApplyDeltaV(accPrograde * dt, accRadial * dt);
        }

        public void Propagate(double dtSeconds)
        {
            if (dtSeconds == 0.0) return;
            var mu = Mu;
            var r = RadiusM;
            var v2 = Vx * Vx + Vz * Vz;
            var energy = 0.5 * v2 - mu / r;

            if (energy >= 0.0)
            {
                HyperbolicStep(mu, dtSeconds);
                return;
            }

            var a = -mu / (2.0 * energy);
            EccVec(out var ex, out var ez);
            var e = System.Math.Sqrt(ex * ex + ez * ez);
            var n = System.Math.Sqrt(mu / (a * a * a));

            if (e < 1e-6)
            {
                var phase = System.Math.Atan2(Rz, Rx) + n * dtSeconds;
                var v = System.Math.Sqrt(mu / a);
                Rx = a * System.Math.Cos(phase);
                Rz = a * System.Math.Sin(phase);
                Vx = -v * System.Math.Sin(phase);
                Vz = v * System.Math.Cos(phase);
                return;
            }

            var nu = TrueAnomalyFromEcc(ex, ez, e, r);
            var E = System.Math.Acos(Clamp((e + System.Math.Cos(nu)) / (1.0 + e * System.Math.Cos(nu)), -1, 1));
            if (nu > System.Math.PI) E = 2.0 * System.Math.PI - E;
            var M = WrapAngle(E - e * System.Math.Sin(E) + n * dtSeconds);
            E = SolveKepler(M, e);
            var cosE = System.Math.Cos(E);
            var sinE = System.Math.Sin(E);
            var rPqwX = a * (cosE - e);
            var rPqwZ = a * System.Math.Sqrt(1.0 - e * e) * sinE;
            var rmag = a * (1.0 - e * cosE);
            var vf = System.Math.Sqrt(mu * a) / rmag;
            var vPqwX = -vf * sinE;
            var vPqwZ = vf * System.Math.Sqrt(1.0 - e * e) * cosE;
            var w = System.Math.Atan2(ez, ex);
            var cw = System.Math.Cos(w);
            var sw = System.Math.Sin(w);
            Rx = cw * rPqwX - sw * rPqwZ;
            Rz = sw * rPqwX + cw * rPqwZ;
            Vx = cw * vPqwX - sw * vPqwZ;
            Vz = sw * vPqwX + cw * vPqwZ;
        }

        public void SetCircularRadius(double radiusM)
        {
            var r = System.Math.Max(radiusM, Central.RadiusM + 80_000.0);
            var phase = TrueAnomalyRad;
            var v = AstroMath.CircularSpeed(Mu, r);
            var c = System.Math.Cos(phase);
            var s = System.Math.Sin(phase);
            Rx = r * c;
            Rz = r * s;
            Vx = -v * s;
            Vz = v * c;
        }

        public void SetAnomaly(double rad)
        {
            var r = RadiusM;
            var v = SpeedMps;
            var c = System.Math.Cos(rad);
            var s = System.Math.Sin(rad);
            GetProgradeUnit(out var px, out _, out var pz);
            var sign = px * (-s) + pz * c;
            Rx = r * c;
            Rz = r * s;
            if (sign < 0)
            {
                Vx = v * s;
                Vz = -v * c;
            }
            else
            {
                Vx = -v * s;
                Vz = v * c;
            }
        }

        void EccVec(out double ex, out double ez)
        {
            var r = RadiusM;
            var v2 = Vx * Vx + Vz * Vz;
            var rv = Rx * Vx + Rz * Vz;
            ex = ((v2 - Mu / r) * Rx - rv * Vx) / Mu;
            ez = ((v2 - Mu / r) * Rz - rv * Vz) / Mu;
        }

        double TrueAnomalyFromEcc(double ex, double ez, double e, double r)
        {
            if (e < 1e-8) return WrapAngle(System.Math.Atan2(Rz, Rx));
            var cnu = Clamp((ex * Rx + ez * Rz) / (e * r), -1, 1);
            var nu = System.Math.Acos(cnu);
            if (Rx * Vx + Rz * Vz < 0) nu = 2.0 * System.Math.PI - nu;
            return nu;
        }

        void HyperbolicStep(double mu, double dt)
        {
            var r = RadiusM;
            var a = -mu / (r * r * r);
            var vx = Vx + a * Rx * dt;
            var vz = Vz + a * Rz * dt;
            Rx += 0.5 * (Vx + vx) * dt;
            Rz += 0.5 * (Vz + vz) * dt;
            Vx = vx;
            Vz = vz;
        }

        static double SolveKepler(double M, double e)
        {
            var E = e < 0.8 ? M : System.Math.PI;
            for (var i = 0; i < 16; i++)
            {
                var d = (E - e * System.Math.Sin(E) - M) / (1.0 - e * System.Math.Cos(E));
                E -= d;
                if (System.Math.Abs(d) < 1e-12) break;
            }
            return E;
        }

        public static double WrapAngle(double rad)
        {
            const double twoPi = 2.0 * System.Math.PI;
            rad %= twoPi;
            if (rad < 0.0) rad += twoPi;
            return rad;
        }

        static double Clamp(double x, double lo, double hi) =>
            x < lo ? lo : (x > hi ? hi : x);
    }
}
