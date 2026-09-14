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

    public struct SlotPlanResult
    {
        public bool Ok;
        public string FailRu;
        public OrbitSlot Slot;
        public Hohmann.Transfer Transfer;
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

        /// <summary>UI-selected orbit slot (preview only until TryDepartSelectedSlot).</summary>
        public OrbitSlot SelectedSlot { get; private set; }

        /// <summary>Display name from last slot depart; preferred by DestinationLabelRu.</summary>
        string _activeDestinationRu = "";

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

        public void SelectSlot(OrbitSlot slot)
        {
            SelectedSlot = slot;
        }

        public SlotPlanResult PreviewSelectedSlot() => PreviewSlot(SelectedSlot);

        public SlotPlanResult PreviewSlot(OrbitSlot slot)
        {
            var result = new SlotPlanResult { Slot = slot };
            if (slot == null)
            {
                result.Ok = false;
                result.FailRu = "Слот не выбран";
                return result;
            }

            if (slot.Kind == OrbitSlotKind.MarkerOnly)
            {
                result.Ok = false;
                result.FailRu = "Скоро / нет плана (маркер, не орбита)";
                return result;
            }

            if (slot.Body != OrbitBodyId.Earth)
            {
                result.Ok = false;
                result.FailRu = "Cross-body пока недоступен";
                return result;
            }

            result.Transfer = Hohmann.Compute(
                GravityBody.Earth.Mu,
                Ship.Orbit.RadiusM,
                slot.RadiusM);
            result.Ok = true;
            result.FailRu = "";
            return result;
        }

        /// <summary>
        /// Geo (circ→circ): need total Δv. Lunar/Leo profile B: only departure burn;
        /// circularize at destination is optional (C) and separate.
        /// Layer 0 slot depart: always profile B (Δv1 only), including GSO.
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
            _activeDestinationRu = "";
            ActiveTransfer = transfer;
            TransferElapsed = 0.0;
            Phase = FlightPhase.Coast;
            return true;
        }

        /// <summary>
        /// Layer 0: apply only Δv1 (departure) toward SelectedSlot circular target.
        /// Markers / null / insufficient fuel fail honestly.
        /// </summary>
        public bool TryDepartSelectedSlot()
        {
            var plan = PreviewSelectedSlot();
            if (!plan.Ok)
            {
                if (!HasInterrupt)
                    RaiseInterrupt(string.IsNullOrEmpty(plan.FailRu) ? "Нет плана" : plan.FailRu);
                return false;
            }

            if (IsThrusting)
            {
                if (!HasInterrupt)
                    RaiseInterrupt("Сбросьте тягу перед авто-уходом");
                return false;
            }

            var transfer = plan.Transfer;
            var need = transfer.DepartureDeltaV;
            if (Ship.AvailableDeltaV < need - 1e-3)
            {
                if (!HasInterrupt)
                    RaiseInterrupt("Не хватает Δv на уход (профиль B: циркуляризация отдельно)");
                return false;
            }

            if (!Ship.TryBurn(transfer.DepartureDeltaV, out _))
            {
                RaiseInterrupt("Топливо: не хватает на уход");
                return false;
            }

            var dv = transfer.R2 >= transfer.R1
                ? transfer.DepartureDeltaV
                : -transfer.DepartureDeltaV;
            Ship.Orbit.ApplyDeltaV(dv, 0.0);

            var slot = plan.Slot;
            Destination = ClosestDestinationId(slot);
            _activeDestinationRu = slot.DisplayNameRu;
            ActiveTransfer = transfer;
            TransferElapsed = 0.0;
            Phase = FlightPhase.Coast;
            return true;
        }

        static DestinationId ClosestDestinationId(OrbitSlot slot)
        {
            if (slot == null || slot.Kind != OrbitSlotKind.CircularAltitude)
                return DestinationId.None;

            var r = slot.RadiusM;
            var rLeo = GravityBody.Earth.RadiusM + 200_000.0;
            if (ApproxRadius(r, rLeo)) return DestinationId.Leo;
            if (ApproxRadius(r, GravityBody.GeoStationaryRadiusM)) return DestinationId.Geo;
            if (ApproxRadius(r, GravityBody.MoonOrbitRadiusM)) return DestinationId.Lunar;
            return DestinationId.None;
        }

        static bool ApproxRadius(double a, double b)
        {
            var scale = System.Math.Max(System.Math.Abs(b), 1.0);
            return System.Math.Abs(a - b) / scale < 1e-6;
        }

        /// <summary>
        /// Circularize around current center at current r: kill radial velocity and
        /// set |v_tan|=v_circ (vector), not |v|~v_circ. Near a on an ellipse |v|~v_circ
        /// so the old prograde-only burn was a no-op.
        /// </summary>
        public bool TryCircularizeHere()
        {
            var o = Ship.Orbit;
            var r = o.RadiusM;
            var vRad = (o.Rx * o.Vx + o.Rz * o.Vz) / r;
            if (o.Eccentricity < 1e-3 && System.Math.Abs(vRad) < 0.5)
                return true;

            var vCirc = AstroMath.CircularSpeed(o.Mu, r);
            var c = o.Rx / r;
            var s = o.Rz / r;
            // Preserve orbit sense (sign of angular momentum h = Rx Vz - Rz Vx).
            var h = o.Rx * o.Vz - o.Rz * o.Vx;
            double vxDes, vzDes;
            if (h >= 0.0)
            {
                vxDes = -vCirc * s;
                vzDes = vCirc * c;
            }
            else
            {
                vxDes = vCirc * s;
                vzDes = -vCirc * c;
            }

            var dvx = vxDes - o.Vx;
            var dvz = vzDes - o.Vz;
            var need = System.Math.Sqrt(dvx * dvx + dvz * dvz);
            if (need < 0.5)
            {
                o.SetCircularRadius(r);
                return true;
            }

            if (!Ship.TryBurn(need, out _))
            {
                RaiseInterrupt("Топливо: не хватает на циркуляризацию. Эллипс сохранён.");
                return false;
            }

            // Charge real |dv|, then snap to circular state at this r (peri=apo=r).
            o.SetCircularRadius(r);
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

            // Active plan: keyboard DestinationId and/or slot ActiveDestinationRu
            // (parking/MEO may have Destination=None but still need arrival).
            var hasPlan = ActiveTransfer.TimeOfFlightSeconds > 0.0
                && (Destination != DestinationId.None || !string.IsNullOrEmpty(_activeDestinationRu));
            if (hasPlan)
            {
                TransferElapsed += dt;
                var target = ActiveTransfer.R2;
                // Profile B: arrive on transfer ellipse when |r-R2|/R2 < 2% (no auto-circularize).
                if (System.Math.Abs(r - target) / target < 0.02)
                {
                    Destination = DestinationId.None;
                    _activeDestinationRu = "";
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
                if (!string.IsNullOrEmpty(_activeDestinationRu))
                    return _activeDestinationRu;
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
