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
        /// <summary>Leave impulse in ship prograde/radial frame from current v → transfer VisViva.</summary>
        public double LeaveDvPrograde;
        public double LeaveDvRadial;
        /// <summary>Ship Atan2(Rz,Rx) at snapshot (map preview orientation).</summary>
        public double LeavePhaseRad;
    }

    /// <summary>
    /// Flight first: ship always coasts on a real ellipse. Hold thrust to fly.
    /// Hohmann buttons are optional planners, not the only way to move.
    /// Layer B: CircularAltitude Earth slots arm a 2-node impulse queue (leave+arrive).
    /// </summary>
    public sealed class SimWorld
    {
        public static readonly double[] WarpFactors = { 1.0, 60.0, 3600.0, 86400.0 };

        public const double ThrustMps2 = 4.0;
        public const double BoostMps2 = 12.0;

        /// <summary>Anomaly match threshold for node fire (rad).</summary>
        public const double NodeAnomalyTolRad = 0.05;

        /// <summary>If SimTime is past node.T by this many seconds, fire anyway (warp safety).</summary>
        public const double NodeOverdueSeconds = 60.0;

        public const double MoonSiderealPeriodSeconds = 27.321661 * 86400.0;

        public SimShip Ship { get; }
        public double SimTimeSeconds { get; private set; }
        public bool Paused { get; private set; }
        public int WarpIndex { get; private set; }
        public double WarpFactor => WarpFactors[WarpIndex];

        public FlightPhase Phase { get; private set; } = FlightPhase.Coast;
        public DestinationId Destination { get; private set; } = DestinationId.None;
        public Hohmann.Transfer ActiveTransfer { get; private set; }
        public double TransferElapsed { get; private set; }

        /// <summary>UI-selected orbit slot (preview only until TryDepartSelectedSlot / TryArm).</summary>
        public OrbitSlot SelectedSlot { get; private set; }

        /// <summary>Armed Layer B maneuver plan (nullable). Impulses fire in Tick.</summary>
        public ManeuverPlan ActivePlan { get; private set; }

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

        /// <summary>Preview only — never burns. Changing slot while a plan is live cancels it (no impulse).</summary>
        public void SelectSlot(OrbitSlot slot)
        {
            if (HasLivePlan)
            {
                if (slot == null || SelectedSlot == null || slot.Id != SelectedSlot.Id)
                    CancelPlan();
            }
            SelectedSlot = slot;
        }

        /// <summary>Drop armed nodes without applying any impulse.</summary>
        public void CancelPlan()
        {
            ClearActivePlanAndDestination();
            Phase = FlightPhase.Coast;
        }

        public bool HasLivePlan =>
            ActivePlan != null && !ActivePlan.AllConsumed;

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

            if (!TryComputeTransferFromCurrentState(slot.RadiusM, out var transfer, out var dvP, out var dvR, out var phase, out var fail))
            {
                result.Ok = false;
                result.FailRu = fail;
                return result;
            }

            result.Transfer = transfer;
            result.LeaveDvPrograde = dvP;
            result.LeaveDvRadial = dvR;
            result.LeavePhaseRad = phase;
            result.Ok = true;
            result.FailRu = "";
            return result;
        }

        /// <summary>
        /// One snapshot: current r,v → Hohmann geometry to circular R2.
        /// Leave Δv is vectorial (VisViva on transfer at r_now minus current v),
        /// not circular-orbit Δv applied onto an ellipse.
        /// </summary>
        bool TryComputeTransferFromCurrentState(
            double r2,
            out Hohmann.Transfer transfer,
            out double leaveDvP,
            out double leaveDvR,
            out double leavePhaseRad,
            out string failRu)
        {
            transfer = default;
            leaveDvP = 0.0;
            leaveDvR = 0.0;
            leavePhaseRad = 0.0;
            failRu = "";

            var o = Ship.Orbit;
            var r1 = o.RadiusM;
            if (r1 < GravityBody.Earth.RadiusM + 80_000.0)
            {
                failRu = "Слишком низко для ухода";
                return false;
            }
            if (r2 <= 0.0)
            {
                failRu = "Некорректный радиус слота";
                return false;
            }
            if (System.Math.Abs(r2 - r1) / System.Math.Max(r1, r2) < 1e-4)
            {
                failRu = "Уже на этом радиусе";
                return false;
            }

            var mu = o.Mu;
            transfer = Hohmann.Compute(mu, r1, r2);
            leavePhaseRad = System.Math.Atan2(o.Rz, o.Rx);

            // Desired velocity on transfer ellipse at current r (peri if raising, apo if lowering).
            var a = transfer.SemiMajor;
            var vDesMag = AstroMath.VisViva(mu, r1, a);
            var h = o.Rx * o.Vz - o.Rz * o.Vx;
            double tx, tz;
            if (h >= 0.0)
            {
                tx = -o.Rz / r1;
                tz = o.Rx / r1;
            }
            else
            {
                tx = o.Rz / r1;
                tz = -o.Rx / r1;
            }
            var vxDes = tx * vDesMag;
            var vzDes = tz * vDesMag;
            var dvx = vxDes - o.Vx;
            var dvz = vzDes - o.Vz;

            o.GetProgradeUnit(out var px, out _, out var pz);
            o.GetRadialUnit(out var rx, out _, out var rz);
            leaveDvP = dvx * px + dvz * pz;
            leaveDvR = dvx * rx + dvz * rz;
            transfer.DepartureDeltaV = System.Math.Sqrt(dvx * dvx + dvz * dvz);
            transfer.TotalDeltaV = transfer.DepartureDeltaV + transfer.ArrivalDeltaV;
            return true;
        }

        /// <summary>
        /// Build Layer B two-node Hohmann plan for SelectedSlot without arming.
        /// Fails honestly for MarkerOnly / non-Earth / null.
        /// </summary>
        public bool TryBuildPlanForSelectedSlot(out ManeuverPlan plan, out string failRu)
        {
            plan = null;
            failRu = "";
            // Same snapshot as HUD preview (after any SelectSlot Cancel).
            var preview = PreviewSelectedSlot();
            if (!preview.Ok)
            {
                failRu = string.IsNullOrEmpty(preview.FailRu) ? "Нет плана" : preview.FailRu;
                return false;
            }

            var transfer = preview.Transfer;
            var slot = preview.Slot;
            var now = SimTimeSeconds;
            var tof = transfer.TimeOfFlightSeconds;
            var tArr = now + tof;
            var nu0 = Ship.Orbit.TrueAnomalyRad;
            var raising = transfer.R2 >= transfer.R1;
            var sign = raising ? 1.0 : -1.0;

            var moonN = 2.0 * System.Math.PI / MoonSiderealPeriodSeconds;
            var moonAtArr = KeplerOrbit.WrapAngle(MoonAnomalyRad + tof * moonN);

            plan = new ManeuverPlan
            {
                Slot = slot,
                Transfer = transfer,
                LeavePhaseRad = preview.LeavePhaseRad,
                ArrivalSimTime = tArr,
                TargetMoonAnomalyAtArrival = moonAtArr,
                Nodes = new[]
                {
                    new BurnNode
                    {
                        T = now,
                        TrueAnomalyRad = KeplerOrbit.WrapAngle(nu0),
                        DvPrograde = preview.LeaveDvPrograde,
                        DvRadial = preview.LeaveDvRadial,
                        Consumed = false,
                    },
                    new BurnNode
                    {
                        T = tArr,
                        TrueAnomalyRad = KeplerOrbit.WrapAngle(nu0 + System.Math.PI),
                        DvPrograde = sign * transfer.ArrivalDeltaV,
                        DvRadial = 0.0,
                        Consumed = false,
                    },
                },
            };
            return true;
        }

        /// <summary>
        /// Arm Layer B plan for SelectedSlot. Does not burn yet (except immediate
        /// leave node if already at anomaly / time). Clears prior Destination path.
        /// </summary>
        public bool TryArmSelectedSlotPlan()
        {
            if (IsThrusting)
            {
                if (!HasInterrupt)
                    RaiseInterrupt("Сбросьте тягу перед авто-уходом");
                return false;
            }

            // Live plan: do not auto Cancel+re-arm (would fire a second leave from the ellipse).
            if (HasLivePlan)
            {
                if (!HasInterrupt)
                    RaiseInterrupt("Сначала «Отменить план», потом новый уход");
                return false;
            }

            if (!TryBuildPlanForSelectedSlot(out var plan, out var failRu))
            {
                if (!HasInterrupt)
                    RaiseInterrupt(failRu);
                return false;
            }

            var need = plan.Transfer.TotalDeltaV;
            if (Ship.AvailableDeltaV < need - 1e-3)
            {
                if (!HasInterrupt)
                    RaiseInterrupt("Не хватает Δv на уход+прибытие (план B, два импульса)");
                return false;
            }

            ActivePlan = plan;
            Destination = ClosestDestinationId(plan.Slot);
            _activeDestinationRu = plan.Slot.DisplayNameRu;
            ActiveTransfer = plan.Transfer;
            TransferElapsed = 0.0;
            Phase = FlightPhase.Coast;

            // Leave node: fire immediately when anomaly already matches (t = now).
            TryFireReadyPlanNodes();
            return true;
        }

        /// <summary>
        /// Geo (circ→circ): need total Δv. Lunar/Leo profile B: only departure burn;
        /// circularize at destination is optional (C) and separate.
        /// Keyboard L/G/H still use instant Δv1 (L0). Slot button uses Layer B nodes.
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

            // Keyboard path: clear any armed slot plan; instant Δv1 leave.
            ClearActivePlanOnly();

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
        /// Layer B: arm two-node Hohmann plan for SelectedSlot (leave + arrive impulses).
        /// Markers / null / insufficient fuel fail honestly. No continuous burn.
        /// </summary>
        public bool TryDepartSelectedSlot() => TryArmSelectedSlotPlan();

        void ClearActivePlanOnly()
        {
            ActivePlan = null;
        }

        void ClearActivePlanAndDestination()
        {
            ActivePlan = null;
            Destination = DestinationId.None;
            _activeDestinationRu = "";
            ActiveTransfer = default;
            TransferElapsed = 0.0;
        }

        static double AngleDiffAbs(double a, double b)
        {
            var d = KeplerOrbit.WrapAngle(a - b);
            if (d > System.Math.PI)
                d = 2.0 * System.Math.PI - d;
            return d;
        }

        /// <summary>
        /// Leave: anomaly or overdue OK. Arrive: never overdue-only — require anomaly
        /// near target and/or |r−R2|/R2 &lt; 2% (warp must not fire Δv2 mid-coast).
        /// </summary>
        bool NodeReady(in BurnNode node, int index, ManeuverPlan plan)
        {
            if (node.Consumed) return false;
            if (SimTimeSeconds < node.T) return false;

            var nu = Ship.Orbit.TrueAnomalyRad;
            var anomOk = AngleDiffAbs(nu, node.TrueAnomalyRad) < NodeAnomalyTolRad;

            if (index == 0)
            {
                if (anomOk) return true;
                return SimTimeSeconds >= node.T + NodeOverdueSeconds;
            }

            var r2 = plan.Transfer.R2;
            var r = Ship.Orbit.RadiusM;
            var nearR = r2 > 1.0 && System.Math.Abs(r - r2) / r2 < 0.02;
            return anomOk || nearR;
        }

        /// <summary>Fire the next unconsumed ready node (sequential). Impulse only.</summary>
        void TryFireReadyPlanNodes()
        {
            var plan = ActivePlan;
            if (plan == null || plan.Nodes == null) return;

            for (var i = 0; i < plan.Nodes.Length; i++)
            {
                var node = plan.Nodes[i];
                if (node.Consumed) continue;
                if (!NodeReady(node, i, plan)) return;

                var dvP = node.DvPrograde;
                var dvR = node.DvRadial;
                var mag = System.Math.Sqrt(dvP * dvP + dvR * dvR);
                if (!Ship.TryBurn(mag, out _))
                {
                    RaiseInterrupt("Топливо: не хватает на импульс плана B");
                    ClearActivePlanAndDestination();
                    return;
                }

                Ship.Orbit.ApplyDeltaV(dvP, dvR);
                node.Consumed = true;
                plan.Nodes[i] = node;
                Phase = FlightPhase.Coast;

                if (plan.AllConsumed)
                {
                    ClearActivePlanAndDestination();
                    Phase = FlightPhase.Arrived;
                    RaiseInterrupt("У цели (план B). C — циркуляризовать если нужно.");
                    WarpIndex = 0;
                }
                return; // one impulse per call; next node waits for its T/anomaly
            }
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

        /// <summary>Seconds until next unconsumed plan node T; ∞ if none / past T waiting on geometry.</summary>
        double TimeToNextPlanNode()
        {
            if (!HasLivePlan || ActivePlan.Nodes == null)
                return double.PositiveInfinity;
            for (var i = 0; i < ActivePlan.Nodes.Length; i++)
            {
                var n = ActivePlan.Nodes[i];
                if (n.Consumed) continue;
                var d = n.T - SimTimeSeconds;
                return d > 1e-6 ? d : double.PositiveInfinity;
            }
            return double.PositiveInfinity;
        }

        public void Tick(double realDeltaSeconds)
        {
            if (Paused || realDeltaSeconds <= 0.0) return;

            var thrusting = IsThrusting;
            // Thrust still caps warp; live plan does NOT — full 1/60/3600/86400 for Play.
            if (thrusting && WarpIndex > 1)
                WarpIndex = 1;

            var warp = thrusting ? System.Math.Min(WarpFactor, 60.0) : WarpFactor;
            var dt = realDeltaSeconds * warp;
            const double maxStep = 120.0;
            while (dt > 0.0)
            {
                var step = dt > maxStep ? maxStep : dt;
                // Land on node epoch so arrive window is not skipped at ×86400.
                if (HasLivePlan)
                {
                    var toNode = TimeToNextPlanNode();
                    if (toNode < step)
                        step = toNode;
                }
                dt -= step;
                Step(step, thrusting);
            }
        }

        void Step(double dt, bool thrusting)
        {
            SimTimeSeconds += dt;

            MoonAnomalyRad = KeplerOrbit.WrapAngle(
                MoonAnomalyRad + dt * (2.0 * System.Math.PI / MoonSiderealPeriodSeconds));

            // Layer B: timed impulse nodes (before thrust/coast so leave can fire cleanly).
            if (ActivePlan != null && !thrusting)
                TryFireReadyPlanNodes();

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

            // Keyboard L0 path only (no ActivePlan): arrive by radius proximity.
            if (ActivePlan == null)
            {
                var hasPlan = ActiveTransfer.TimeOfFlightSeconds > 0.0
                    && (Destination != DestinationId.None || !string.IsNullOrEmpty(_activeDestinationRu));
                if (hasPlan)
                {
                    TransferElapsed += dt;
                    var target = ActiveTransfer.R2;
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
            else
            {
                TransferElapsed += dt;
            }
        }

        public void GetMoonPositionMeters(out double x, out double y, out double z)
        {
            var r = GravityBody.MoonOrbitRadiusM;
            x = r * System.Math.Cos(MoonAnomalyRad);
            y = 0.0;
            z = r * System.Math.Sin(MoonAnomalyRad);
        }

        /// <summary>Moon phase marker for ActivePlan arrival (same-body circ at r_moon).</summary>
        public void GetPlanMoonTargetMeters(out double x, out double y, out double z, out bool ok)
        {
            ok = false;
            x = y = z = 0.0;
            if (ActivePlan == null || ActivePlan.Slot == null) return;
            var scale = System.Math.Max(GravityBody.MoonOrbitRadiusM, 1.0);
            if (System.Math.Abs(ActivePlan.Slot.RadiusM - GravityBody.MoonOrbitRadiusM) / scale > 1e-3)
                return;
            var r = GravityBody.MoonOrbitRadiusM;
            var a = ActivePlan.TargetMoonAnomalyAtArrival;
            x = r * System.Math.Cos(a);
            y = 0.0;
            z = r * System.Math.Sin(a);
            ok = true;
        }

        public string PhaseLabelRu
        {
            get
            {
                if (IsThrusting) return Boost ? "Жжём (форсаж)" : "Жжём";
                if (ActivePlan != null) return "План B (узлы)";
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
