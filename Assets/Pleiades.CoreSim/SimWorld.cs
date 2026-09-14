namespace Pleiades.CoreSim
{
    public enum DestinationId
    {
        None = 0,
        Geo = 1,
        Lunar = 2,
        Leo = 3,
    }

    public enum FlightPhase
    {
        Coast,
        Burn,
        Arrived,
    }

    /// <summary>
    /// Flight first: ship always coasts on a real ellipse. Hold thrust to fly.
    /// Hohmann buttons are optional planners, not the only way to move.
    /// </summary>
    public sealed class SimWorld
    {
        public static readonly double[] WarpFactors = { 1.0, 60.0, 3600.0, 86400.0 };

        public const double ThrustMps2 = 4.0;
        public const double BoostMps2 = 12.0;

        public SimShip Ship { get; }
        public double SimTimeSeconds { get; private set; }
        public bool Paused { get; private set; }
        public int WarpIndex { get; private set; }
        public double WarpFactor => WarpFactors[WarpIndex];

        public FlightPhase Phase { get; private set; } = FlightPhase.Coast;
        public DestinationId Destination { get; private set; } = DestinationId.None;
        public Hohmann.Transfer ActiveTransfer { get; private set; }
        public double TransferElapsed { get; private set; }

        public string LastInterruptRu { get; private set; } = "";
        public bool HasInterrupt { get; private set; }

        public double MoonAnomalyRad { get; private set; }

        public double CmdPrograde;
        public double CmdRadial;
        public bool Boost;
        public bool FollowShip = true;

        public bool IsThrusting => CmdPrograde != 0.0 || CmdRadial != 0.0;
        public double CurrentThrustMps2 =>
            IsThrusting ? (Boost ? BoostMps2 : ThrustMps2) : 0.0;

        public SimWorld()
        {
            Ship = new SimShip();
            SimTimeSeconds = 0.0;
            Paused = true;
            WarpIndex = 0;
            MoonAnomalyRad = 1.2;
        }

        public void TogglePause() => Paused = !Paused;
        public void SetPaused(bool p) => Paused = p;

        public void SetWarpIndex(int index)
        {
            if (index < 0) index = 0;
            if (index >= WarpFactors.Length) index = WarpFactors.Length - 1;
            WarpIndex = index;
        }

        public void ClearInterrupt()
        {
            HasInterrupt = false;
            LastInterruptRu = "";
        }

        void RaiseInterrupt(string ru)
        {
            HasInterrupt = true;
            LastInterruptRu = ru;
            Paused = true;
            WarpIndex = 0;
        }

        /// <summary>
        /// Geo (circ→circ): need total Δv. Lunar/Leo profile B: only departure burn;
        /// circularize at destination is optional (C) and separate.
        /// </summary>
        public double RequiredDeltaVForDepart(DestinationId dest)
        {
            var transfer = PreviewTransfer(dest);
            if (dest == DestinationId.Geo)
                return transfer.TotalDeltaV;
            return transfer.DepartureDeltaV;
        }

        public bool CanDepart(DestinationId dest)
        {
            if (dest == DestinationId.None) return false;
            if (IsThrusting) return false;
            var need = RequiredDeltaVForDepart(dest);
            return Ship.AvailableDeltaV >= need - 1e-3;
        }

        public Hohmann.Transfer PreviewTransfer(DestinationId dest)
        {
            var r1 = Ship.Orbit.RadiusM;
            var rLeo = GravityBody.Earth.RadiusM + 200_000.0;
            switch (dest)
            {
                case DestinationId.Geo:
                    return Hohmann.Compute(GravityBody.Earth.Mu, r1, GravityBody.GeoStationaryRadiusM);
                case DestinationId.Lunar:
                    return Hohmann.Compute(GravityBody.Earth.Mu, r1, GravityBody.MoonOrbitRadiusM);
                case DestinationId.Leo:
                    return Hohmann.Compute(GravityBody.Earth.Mu, r1, rLeo);
                default:
                    return default;
            }
        }

        public bool TryDepart(DestinationId dest)
        {
            if (!CanDepart(dest))
            {
                if (!HasInterrupt)
                {
                    var msg = dest == DestinationId.Geo
                        ? "Не хватает Δv на оба импульса Гомана (от текущего r)"
                        : "Не хватает Δv на уход (профиль B: циркуляризация отдельно)";
                    RaiseInterrupt(msg);
                }
                return false;
            }

            var transfer = PreviewTransfer(dest);
            if (!Ship.TryBurn(transfer.DepartureDeltaV, out _))
            {
                RaiseInterrupt("Топливо: не хватает на уход");
                return false;
            }

            var dv = transfer.R2 >= transfer.R1
                ? transfer.DepartureDeltaV
                : -transfer.DepartureDeltaV;
            Ship.Orbit.ApplyDeltaV(dv, 0.0);

            Destination = dest;
            ActiveTransfer = transfer;
            TransferElapsed = 0.0;
            Phase = FlightPhase.Coast;
            return true;
        }

        public bool TryCircularizeHere()
        {
            var o = Ship.Orbit;
            var vCirc = AstroMath.CircularSpeed(o.Mu, o.RadiusM);
            var need = vCirc - o.SpeedMps;
            var abs = System.Math.Abs(need);
            if (abs < 0.5) return true;
            if (!Ship.TryBurn(abs, out _))
            {
                RaiseInterrupt("Топливо: не хватает на циркуляризацию. Эллипс сохранён.");
                return false;
            }
            o.ApplyDeltaV(need, 0.0);
            return true;
        }

        public void Tick(double realDeltaSeconds)
        {
            if (Paused || realDeltaSeconds <= 0.0) return;

            var thrusting = IsThrusting;
            if (thrusting && WarpIndex > 1)
                WarpIndex = 1;

            var warp = thrusting ? System.Math.Min(WarpFactor, 60.0) : WarpFactor;
            var dt = realDeltaSeconds * warp;
            const double maxStep = 120.0;
            while (dt > 0.0)
            {
                var step = dt > maxStep ? maxStep : dt;
                dt -= step;
                Step(step, thrusting);
            }
        }

        void Step(double dt, bool thrusting)
        {
            SimTimeSeconds += dt;

            const double moonPeriod = 27.321661 * 86400.0;
            MoonAnomalyRad = KeplerOrbit.WrapAngle(
                MoonAnomalyRad + dt * (2.0 * System.Math.PI / moonPeriod));

            if (thrusting)
            {
                Phase = FlightPhase.Burn;
                var acc = Boost ? BoostMps2 : ThrustMps2;
                var need = acc * dt;
                if (!Ship.TryBurn(need, out _))
                {
                    CmdPrograde = 0;
                    CmdRadial = 0;
                    RaiseInterrupt("Топливо: бак пуст, тяга оборвалась");
                    thrusting = false;
                }
                else
                {
                    Ship.Orbit.ApplyAccel(acc * CmdPrograde, acc * CmdRadial, dt);
                }
            }
            else if (Phase == FlightPhase.Burn)
            {
                Phase = FlightPhase.Coast;
            }

            Ship.Orbit.Propagate(dt);

            var r = Ship.Orbit.RadiusM;
            var minR = GravityBody.Earth.RadiusM + 80_000.0;
            if (r < minR)
            {
                Ship.Orbit.SetCircularRadius(minR);
                RaiseInterrupt("Перицентр в атмосфере. Орбита поднята, чтобы не зарыться.");
            }

            if (Destination != DestinationId.None && ActiveTransfer.TimeOfFlightSeconds > 0.0)
            {
                TransferElapsed += dt;
                var target = ActiveTransfer.R2;
                // Profile B: arrive on transfer ellipse when |r-R2|/R2 < 2% (no auto-circularize).
                if (System.Math.Abs(r - target) / target < 0.02)
                {
                    Destination = DestinationId.None;
                    Phase = FlightPhase.Arrived;
                    RaiseInterrupt("У цели (эллипс). C — циркуляризовать здесь, если хватит Δv.");
                    WarpIndex = 0;
                }
            }
        }

        public void GetMoonPositionMeters(out double x, out double y, out double z)
        {
            var r = GravityBody.MoonOrbitRadiusM;
            x = r * System.Math.Cos(MoonAnomalyRad);
            y = 0.0;
            z = r * System.Math.Sin(MoonAnomalyRad);
        }

        public string PhaseLabelRu
        {
            get
            {
                if (IsThrusting) return Boost ? "Жжём (форсаж)" : "Жжём";
                if (Ship.Orbit.Eccentricity >= 1.0) return "Гипербола / уход";
                return "Дрейф";
            }
        }

        public string DestinationLabelRu
        {
            get
            {
                switch (Destination)
                {
                    case DestinationId.Geo: return "ГСО";
                    case DestinationId.Lunar: return "Луна";
                    case DestinationId.Leo: return "LEO";
                    default: return "свободный полёт";
                }
            }
        }
    }
}
