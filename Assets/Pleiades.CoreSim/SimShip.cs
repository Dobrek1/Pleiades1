using System.Collections.Generic;

namespace Pleiades.CoreSim
{
    public sealed class ShipModule
    {
        public string Id { get; }
        public string DisplayNameRu { get; }
        public double MassKg { get; }

        public ShipModule(string id, string displayNameRu, double massKg)
        {
            Id = id;
            DisplayNameRu = displayNameRu;
            MassKg = massKg;
        }
    }

    /// <summary>
    /// «Ржавая баржа» B+C: dry 20500 kg, LH2 23000 kg, Isp 900 s, LEO start.
    /// Modules sum to dry (without payload).
    /// </summary>
    public sealed class SimShip
    {
        public string Name { get; } = "Ржавая баржа";
        public double IspSeconds { get; } = 900.0;
        public double FuelKg { get; private set; }
        public double FuelCapacityKg { get; } = 23_000.0;
        public double PayloadKg { get; private set; }

        public IReadOnlyList<ShipModule> Modules { get; }
        public KeplerOrbit Orbit { get; private set; }

        public SimShip()
        {
            // Dry = 20500 kg (brief), not the old ~29 t stack.
            Modules = new List<ShipModule>
            {
                new ShipModule("frame", "Каркас", 5_500.0),
                new ShipModule("cabin1", "Каюта 1", 1_800.0),
                new ShipModule("cabin2", "Каюта 2", 1_800.0),
                new ShipModule("cabin3", "Каюта 3", 1_800.0),
                new ShipModule("cabin4", "Каюта 4", 1_800.0),
                new ShipModule("hold", "Трюм", 2_200.0),
                new ShipModule("engine", "Двигатель", 3_200.0),
                new ShipModule("lh2", "Бак LH2", 1_400.0),
                new ShipModule("radiator", "Радиатор", 1_000.0),
            };
            FuelKg = FuelCapacityKg;
            PayloadKg = 0.0;
            Orbit = KeplerOrbit.Circular(GravityBody.Earth, 200_000.0, 0.0);
        }

        public double DryMassKg
        {
            get
            {
                double m = PayloadKg;
                for (int i = 0; i < Modules.Count; i++)
                    m += Modules[i].MassKg;
                return m;
            }
        }

        public double WetMassKg => DryMassKg + FuelKg;

        /// <summary>Temporary debug pool (1000 km/s) on top of rocket Δv — not production propellant.</summary>
        public const double DebugExtraDeltaVMps = 1_000_000.0;
        double _debugExtraDeltaVLeft = DebugExtraDeltaVMps;

        public double RocketDeltaV =>
            AstroMath.RocketDeltaV(IspSeconds, WetMassKg, DryMassKg);

        public double DebugExtraDeltaVLeft => _debugExtraDeltaVLeft;

        public double AvailableDeltaV => RocketDeltaV + _debugExtraDeltaVLeft;

        public void SetPayloadKg(double kg)
        {
            if (kg < 0.0) kg = 0.0;
            if (kg > 50_000.0) kg = 50_000.0;
            PayloadKg = kg;
        }

        /// <summary>Debug refill only — clamp to capacity.</summary>
        public void SetFuelKg(double kg)
        {
            if (kg < 0.0) kg = 0.0;
            if (kg > FuelCapacityKg) kg = FuelCapacityKg;
            FuelKg = kg;
        }

        /// <summary>Refill temporary debug Δv pool (default 1000 km/s).</summary>
        public void SetDebugExtraDeltaV(double mps)
        {
            if (mps < 0.0) mps = 0.0;
            _debugExtraDeltaVLeft = mps;
        }

        public bool TryBurn(double deltaVMps, out double fuelUsed)
        {
            fuelUsed = 0.0;
            if (deltaVMps <= 0.0) return true;
            if (AvailableDeltaV + 1e-6 < deltaVMps) return false;

            var rocket = RocketDeltaV;
            var fromRocket = deltaVMps <= rocket ? deltaVMps : rocket;
            var fromDebug = deltaVMps - fromRocket;

            if (fromRocket > 0.0)
            {
                fuelUsed = AstroMath.FuelForDeltaV(IspSeconds, WetMassKg, fromRocket);
                if (fuelUsed > FuelKg + 1e-6) return false;
                FuelKg -= fuelUsed;
                if (FuelKg < 0.0) FuelKg = 0.0;
            }

            if (fromDebug > 0.0)
                _debugExtraDeltaVLeft -= fromDebug;
            if (_debugExtraDeltaVLeft < 0.0) _debugExtraDeltaVLeft = 0.0;
            return true;
        }

        public void SetOrbit(KeplerOrbit orbit) => Orbit = orbit;
    }
}
