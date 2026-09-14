namespace Pleiades.CoreSim
{
    public enum DestinationId
    {
        None = 0,
        Geo = 1,
        Lunar = 2,
    }

    public enum FlightPhase
    {
        IdleLeo,
        TransferCoast,
        Arrived,
    }

    /// <summary>
    /// Sim clock, warp, pause, Hohmann departures, fuel/arrival interrupts.
    /// Warp factors: 1 / 60 / 3600 / 86400. Ecliptic XZ.
    /// </summary>
    public sealed class SimWorld
    {
        public static readonly double[] WarpFactors = { 1.0, 60.0, 3600.0, 86400.0 };

        public SimShip Ship { get; }
        public double SimTimeSeconds { get; private set; }
        public bool Paused { get; private set; }
        public int WarpIndex { get; private set; }
        public double WarpFactor => WarpFactors[WarpIndex];

        public FlightPhase Phase { get; private set; } = FlightPhase.IdleLeo;
        public DestinationId Destination { get; private set; } = DestinationId.None;
        public Hohmann.Transfer ActiveTransfer { get; private set; }
        public double TransferElapsed { get; private set; }
        public double TransferCoastRadiusLerpStart { get; private set; }
        public double TransferCoastRadiusLerpEnd { get; private set; }

        public string LastInterruptRu { get; private set; } = "";
        public bool HasInterrupt { get; private set; }

        // Moon mean anomaly for map (simple circular)
        public double MoonAnomalyRad { get; private set; }

        public SimWorld()
        {
            Ship = new SimShip();
            SimTimeSeconds = 0.0;
            Paused = false;
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

        public bool CanDepart(DestinationId dest)
        {
            if (Phase != FlightPhase.IdleLeo && Phase != FlightPhase.Arrived)
                return false;
            if (dest == DestinationId.None) return false;
            var transfer = TransferFor(dest);
            return Ship.AvailableDeltaV >= transfer.DepartureDeltaV - 1e-3;
        }

        public Hohmann.Transfer PreviewTransfer(DestinationId dest) => TransferFor(dest);

        static Hohmann.Transfer TransferFor(DestinationId dest)
        {
            switch (dest)
            {
                case DestinationId.Geo: return Hohmann.LeoToGeo();
                case DestinationId.Lunar: return Hohmann.LeoToLunar();
                default: return default;
            }
        }

        public bool TryDepart(DestinationId dest)
        {
            if (!CanDepart(dest)) return false;

            var transfer = TransferFor(dest);
            if (!Ship.TryBurn(transfer.DepartureDeltaV, out _))
            {
                RaiseInterrupt("Топливо: не хватает на уход (Δv отлёта)");
                return false;
            }

            // Fuel interrupt check after burn
            if (Ship.FuelKg <= 0.0)
            {
                RaiseInterrupt("Топливо: бак пуст");
            }

            Destination = dest;
            ActiveTransfer = transfer;
            TransferElapsed = 0.0;
            TransferCoastRadiusLerpStart = Ship.Orbit.RadiusM;
            TransferCoastRadiusLerpEnd = transfer.R2;
            Phase = FlightPhase.TransferCoast;

            // Place ship on transfer: start at periapsis of Hohmann (current LEO radius)
            Ship.Orbit.SetCircularRadius(transfer.R1);
            return true;
        }

        public void Tick(double realDeltaSeconds)
        {
            if (Paused || realDeltaSeconds <= 0.0) return;

            double dt = realDeltaSeconds * WarpFactor;
            SimTimeSeconds += dt;

            // Moon ~27.3 day period
            const double moonPeriod = 27.321661 * 86400.0;
            MoonAnomalyRad = KeplerOrbit.WrapAngle(
                MoonAnomalyRad + dt * (2.0 * System.Math.PI / moonPeriod));

            if (Phase == FlightPhase.TransferCoast)
            {
                TransferElapsed += dt;
                double u = ActiveTransfer.TimeOfFlightSeconds > 0.0
                    ? TransferElapsed / ActiveTransfer.TimeOfFlightSeconds
                    : 1.0;
                if (u > 1.0) u = 1.0;

                // Approximate transfer radius along elliptical Hohmann (r from vis-viva geometry)
                double r = LerpTransferRadius(ActiveTransfer.R1, ActiveTransfer.R2, ActiveTransfer.SemiMajor, u);
                Ship.Orbit.SetCircularRadius(r);

                // Propagate anomaly roughly with local circular rate (visual)
                Ship.Orbit.Propagate(dt);

                if (u >= 1.0)
                    CompleteArrival();
            }
            else
            {
                Ship.Orbit.Propagate(dt);
            }
        }

        /// <summary>
        /// Sample transfer orbit radius vs true anomaly fraction 0..1 (π radians of transfer).
        /// r = a(1−e²)/(1+e cos ν); ν = π·u.
        /// </summary>
        static double LerpTransferRadius(double r1, double r2, double a, double u)
        {
            double e = System.Math.Abs(r2 - r1) / (r1 + r2);
            if (r2 < r1)
            {
                // Going inward: start at apo
                double nu = System.Math.PI * (1.0 - u);
                double denom = 1.0 + e * System.Math.Cos(nu);
                if (System.Math.Abs(denom) < 1e-9) return r2;
                return a * (1.0 - e * e) / denom;
            }
            else
            {
                double nu = System.Math.PI * u;
                double denom = 1.0 + e * System.Math.Cos(nu);
                if (System.Math.Abs(denom) < 1e-9) return r2;
                return a * (1.0 - e * e) / denom;
            }
        }

        void CompleteArrival()
        {
            double arrivalDv = ActiveTransfer.ArrivalDeltaV;
            if (!Ship.TryBurn(arrivalDv, out _))
            {
                RaiseInterrupt("Топливо: не хватает на циркуляризацию");
                // Still snap orbit so player sees arrival
            }
            else if (Ship.FuelKg <= 1.0)
            {
                RaiseInterrupt("Топливо: почти пусто после прибытия");
            }

            Ship.Orbit.SetCircularRadius(ActiveTransfer.R2);
            Phase = FlightPhase.Arrived;

            string destName = Destination == DestinationId.Geo ? "ГСО" : "лунная орбита";
            RaiseInterrupt("Прибытие: циркуляризация на " + destName + ". Варп → 1×");
            WarpIndex = 0;
        }

        public void GetMoonPositionMeters(out double x, out double y, out double z)
        {
            double r = GravityBody.MoonOrbitRadiusM;
            x = r * System.Math.Cos(MoonAnomalyRad);
            y = 0.0;
            z = r * System.Math.Sin(MoonAnomalyRad);
        }

        public string PhaseLabelRu
        {
            get
            {
                switch (Phase)
                {
                    case FlightPhase.IdleLeo: return "Низкая орбита";
                    case FlightPhase.TransferCoast: return "Перелёт (Хоман)";
                    case FlightPhase.Arrived: return "На орбите цели";
                    default: return "—";
                }
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
                    default: return "—";
                }
            }
        }
    }
}
