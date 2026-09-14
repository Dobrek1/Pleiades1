using UnityEngine;
using Pleiades.CoreSim;

namespace Pleiades
{
    /// <summary>
    /// Top-down ecliptic map: Earth, Moon, orbits, ship. Camera looks down +Y.
    /// 1 uu = 1000 km (AstroMath.MetersPerUnit).
    /// </summary>
    public sealed class MapView : MonoBehaviour
    {
        SimWorld _world;
        Transform _earth;
        Transform _moon;
        Transform _ship;
        LineRenderer _leoRing;
        LineRenderer _geoRing;
        LineRenderer _moonOrbitRing;
        LineRenderer _transferArc;

        static readonly Color EarthColor = new Color(0.2f, 0.45f, 0.9f);
        static readonly Color MoonColor = new Color(0.75f, 0.75f, 0.7f);
        static readonly Color ShipColor = new Color(1f, 0.85f, 0.2f);
        static readonly Color OrbitColor = new Color(0.4f, 0.7f, 1f, 0.55f);
        static readonly Color GeoColor = new Color(0.3f, 1f, 0.6f, 0.4f);
        static readonly Color MoonOrbitColor = new Color(0.7f, 0.7f, 0.7f, 0.35f);
        static readonly Color TransferColor = new Color(1f, 0.5f, 0.15f, 0.8f);

        public void Bind(SimWorld world) => _world = world;

        void Start()
        {
            BuildPrimitives();
        }

        void BuildPrimitives()
        {
            _earth = CreateSphere("Earth", EarthColor, (float)AstroMath.MetersToUnits(GravityBody.Earth.RadiusM));
            _moon = CreateSphere("Moon", MoonColor, (float)AstroMath.MetersToUnits(GravityBody.Moon.RadiusM));
            _ship = CreateSphere("Ship", ShipColor, 0.8f);

            double rLeo = GravityBody.Earth.RadiusM + 200_000.0;
            _leoRing = CreateRing("LeoOrbit", OrbitColor, (float)AstroMath.MetersToUnits(rLeo), 128);
            _geoRing = CreateRing("GeoOrbit", GeoColor, (float)AstroMath.MetersToUnits(GravityBody.GeoStationaryRadiusM), 128);
            _moonOrbitRing = CreateRing("MoonOrbit", MoonOrbitColor, (float)AstroMath.MetersToUnits(GravityBody.MoonOrbitRadiusM), 192);
            _transferArc = CreateLine("TransferArc", TransferColor, 64);
        }

        Transform CreateSphere(string name, Color color, float radiusUu)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * Mathf.Max(radiusUu * 2f, 0.5f);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                // Unlit-ish via default material tint (works without URP shader graph)
                r.material.color = color;
            }
            return go.transform;
        }

        LineRenderer CreateRing(string name, Color color, float radiusUu, int segments)
        {
            var lr = CreateLine(name, color, segments + 1);
            for (int i = 0; i <= segments; i++)
            {
                float a = (float)(i * 2.0 * System.Math.PI / segments);
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radiusUu, 0f, Mathf.Sin(a) * radiusUu));
            }
            return lr;
        }

        LineRenderer CreateLine(string name, Color color, int count)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = count;
            lr.loop = false;
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.35f;
            lr.startColor = color;
            lr.endColor = color;
            lr.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply") ?? Shader.Find("Standard"));
            lr.startWidth = 0.35f;
            lr.endWidth = 0.35f;
            return lr;
        }

        public void Refresh()
        {
            if (_world == null || _earth == null) return;

            _earth.position = Vector3.zero;

            _world.GetMoonPositionMeters(out double mx, out double my, out double mz);
            _moon.position = new Vector3(
                (float)AstroMath.MetersToUnits(mx),
                (float)AstroMath.MetersToUnits(my),
                (float)AstroMath.MetersToUnits(mz));

            _world.Ship.Orbit.GetPositionMeters(out double sx, out double sy, out double sz);
            _ship.position = new Vector3(
                (float)AstroMath.MetersToUnits(sx),
                (float)AstroMath.MetersToUnits(sy),
                (float)AstroMath.MetersToUnits(sz));

            UpdateTransferArc();
        }

        void UpdateTransferArc()
        {
            if (_transferArc == null) return;
            bool show = _world.Phase == FlightPhase.TransferCoast;
            _transferArc.enabled = show;
            if (!show) return;

            var t = _world.ActiveTransfer;
            int n = _transferArc.positionCount;
            for (int i = 0; i < n; i++)
            {
                double u = i / (double)(n - 1);
                double r = SampleTransferR(t, u);
                double nu = System.Math.PI * u; // departure anomaly ~0 for sprint-1 visual
                double baseAnom = 0.0;
                float x = (float)AstroMath.MetersToUnits(r * System.Math.Cos(baseAnom + nu));
                float z = (float)AstroMath.MetersToUnits(r * System.Math.Sin(baseAnom + nu));
                _transferArc.SetPosition(i, new Vector3(x, 0.1f, z));
            }
        }

        static double SampleTransferR(Hohmann.Transfer t, double u)
        {
            double e = System.Math.Abs(t.R2 - t.R1) / (t.R1 + t.R2);
            double a = t.SemiMajor;
            double nu = System.Math.PI * u;
            if (t.R2 < t.R1) nu = System.Math.PI * (1.0 - u);
            double denom = 1.0 + e * System.Math.Cos(nu);
            if (System.Math.Abs(denom) < 1e-9) return t.R2;
            return a * (1.0 - e * e) / denom;
        }
    }
}
