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
    /// Sprint-1 barge «Ржавая баржа»: Isp 900 s, ~18 t LH2, LEO start.
    /// Modules: frame, cabins×4, hold, engine, LH2 tank, radiator.
    /// </summary>
    public sealed class SimShip
    {
        public string Name { get; } = "Ржавая баржа";
        public double IspSeconds { get; } = 900.0;
        public double FuelKg { get; private set; }
        public double FuelCapacityKg { get; } = 18_000.0;
        public double PayloadKg { get; private set; }

        public IReadOnlyList<ShipModule> Modules { get; }
        public KeplerOrbit Orbit { get; private set; }

        public SimShip()
        {
            Modules = new List<ShipModule>
            {
                new ShipModule("frame", "Каркас", 8_000.0),
                new ShipModule("cabin1", "Каюта 1", 2_500.0),
                new ShipModule("cabin2", "Каюта 2", 2_500.0),
                new ShipModule("cabin3", "Каюта 3", 2_500.0),
                new ShipModule("cabin4", "Каюта 4", 2_500.0),
                new ShipModule("hold", "Трюм", 3_000.0),
                new ShipModule("engine", "Двигатель", 4_500.0),
                new ShipModule("lh2", "Бак LH2", 2_000.0),
                new ShipModule("radiator", "Радиатор", 1_500.0),
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

        public double AvailableDeltaV =>
            AstroMath.RocketDeltaV(IspSeconds, WetMassKg, DryMassKg);

        public void SetPayloadKg(double kg)
        {
            if (kg < 0.0) kg = 0.0;
            if (kg > 50_000.0) kg = 50_000.0;
            PayloadKg = kg;
        }

        public bool TryBurn(double deltaVMps, out double fuelUsed)
        {
            fuelUsed = 0.0;
            if (deltaVMps <= 0.0) return true;
            if (AvailableDeltaV + 1e-6 < deltaVMps) return false;

            fuelUsed = AstroMath.FuelForDeltaV(IspSeconds, WetMassKg, deltaVMps);
            if (fuelUsed > FuelKg + 1e-6) return false;
            FuelKg -= fuelUsed;
            if (FuelKg < 0.0) FuelKg = 0.0;
            return true;
        }

        public void SetOrbit(KeplerOrbit orbit) => Orbit = orbit;
    }
}
