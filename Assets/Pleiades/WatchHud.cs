using UnityEngine;
using Pleiades.CoreSim;

namespace Pleiades
{
    public sealed class WatchHud : MonoBehaviour
    {
        SimWorld _world;
        float _payloadSlider;
        float _debugFuelSlider = -1f;
        int _planTarget; // 0 lunar, 1 geo, 2 leo
        GUIStyle _title;
        GUIStyle _body;
        GUIStyle _warn;
        GUIStyle _debug;
        bool _stylesReady;

        static readonly string[] PlanNames = { "Луна (орбита Земли на r_moon)", "ГСО", "LEO" };
        static readonly DestinationId[] PlanIds = { DestinationId.Lunar, DestinationId.Geo, DestinationId.Leo };

        public void Bind(SimWorld world)
        {
            _world = world;
            _payloadSlider = 0f;
            _debugFuelSlider = -1f;
            _planTarget = 0;
        }

        void EnsureStyles()
        {
            if (_stylesReady) return;
            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.95f, 0.9f, 0.6f) }
            };
            _body = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = new Color(0.85f, 0.9f, 1f) }
            };
            _warn = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = new Color(1f, 0.45f, 0.35f) }
            };
            _debug = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(1f, 0.7f, 0.3f) }
            };
            _stylesReady = true;
        }

        void OnGUI()
        {
            if (_world == null) return;
            EnsureStyles();

            const float pad = 12f;
            var w = 420f;
            GUI.Box(new Rect(pad, pad, w, 560f), GUIContent.none);

            float y = pad + 8f;
            var x = pad + 10f;
            const float line = 20f;

            GUI.Label(new Rect(x, y, w - 20f, 24f), "ПЛЕЯДЫ — полёт + планер", _title);
            y += line + 6f;

            var ship = _world.Ship;
            var o = ship.Orbit;
            GUI.Label(new Rect(x, y, w - 20f, line), $"Фаза: {_world.PhaseLabelRu}  ·  цель: {_world.DestinationLabelRu}", _body); y += line;
            GUI.Label(new Rect(x, y, w - 20f, line),
                $"Время: {FormatTime(_world.SimTimeSeconds)}  |  {(_world.Paused ? "ПАУЗА" : "ХОД")}  варп ×{_world.WarpFactor:0}", _body);
            y += line;

            var altKm = (o.RadiusM - GravityBody.Earth.RadiusM) / 1000.0;
            GUI.Label(new Rect(x, y, w - 20f, line),
                $"r {o.RadiusM / 1000.0:0} км   h≈{altKm:0} км   v {o.SpeedMps:0} м/с", _body);
            y += line;

            var apo = o.ApoapsisM;
            var apoStr = double.IsInfinity(apo) ? "∞" : (apo / 1000.0).ToString("0");
            GUI.Label(new Rect(x, y, w - 20f, line),
                $"пери {o.PeriapsisM / 1000.0:0} км   апо {apoStr} км   e {o.Eccentricity:0.000}", _body);
            y += line;

            GUI.Label(new Rect(x, y, w - 20f, line),
                $"Δv {ship.AvailableDeltaV / 1000.0:0.00} км/с   LH2 {ship.FuelKg / 1000.0:0.0}/{ship.FuelCapacityKg / 1000.0:0} т   сухая {ship.DryMassKg / 1000.0:0.0} т", _body);
            y += line;

            GUI.Label(new Rect(x, y, w - 20f, line),
                _world.IsThrusting
                    ? $"тяга {_world.CurrentThrustMps2:0.0} м/с²  W/S Q/E  Shift"
                    : "W/S проград/ретро · Q/E радиал · Shift форсаж", _body);
            y += line + 8f;

            // --- Autoplanner ---
            GUI.Label(new Rect(x, y, w - 20f, line), "Автопланер (Гоман от текущего r, колодец Земли)", _title);
            y += line + 2f;

            if (GUI.Button(new Rect(x, y, 100f, 24f), "Цель: Луна")) _planTarget = 0;
            if (GUI.Button(new Rect(x + 105f, y, 90f, 24f), "Цель: ГСО")) _planTarget = 1;
            if (GUI.Button(new Rect(x + 200f, y, 90f, 24f), "Цель: LEO")) _planTarget = 2;
            y += 28f;

            GUI.Label(new Rect(x, y, w - 20f, line), $"План → {PlanNames[_planTarget]}", _body);
            y += line;

            var plan = _world.PreviewTransfer(PlanIds[_planTarget]);
            GUI.Label(new Rect(x, y, w - 20f, line),
                $"Уход {plan.DepartureDeltaV / 1000.0:0.00} км/с · Прибытие {plan.ArrivalDeltaV / 1000.0:0.00} км/с · Итого {plan.TotalDeltaVKmS:0.00}", _body);
            y += line;
            GUI.Label(new Rect(x, y, w - 20f, line),
                $"TOF {FormatTime(plan.TimeOfFlightSeconds)}   (исполнить уход жжёт только Δv1)", _body);
            y += line + 4f;

            if (GUI.Button(new Rect(x, y, 200f, 28f), "Исполнить уход"))
                _world.TryDepart(PlanIds[_planTarget]);
            y += 34f;

            if (GUI.Button(new Rect(x, y, 220f, 28f), "Цирк. вокруг Земли (C)"))
                _world.TryCircularizeHere();
            y += 34f;

            GUI.Label(new Rect(x, y, w - 20f, 36f),
                "«Орбита Луны» = круговая вокруг Земли на r_moon (не вокруг Луны — SOI позже).",
                _body);
            y += 40f;

            // payload
            GUI.Label(new Rect(x, y, w - 20f, line), $"Груз: {_payloadSlider:0} кг", _body);
            y += line;
            var newPayload = GUI.HorizontalSlider(new Rect(x, y, w - 40f, 18f), _payloadSlider, 0f, 20000f);
            if (Mathf.Abs(newPayload - _payloadSlider) > 0.5f)
            {
                _payloadSlider = newPayload;
                ship.SetPayloadKg(_payloadSlider);
            }
            y += line + 8f;

            // debug fuel
            if (_debugFuelSlider < 0f)
                _debugFuelSlider = (float)ship.FuelKg;
            GUI.Label(new Rect(x, y, w - 20f, line), $"debug LH2 (пополнение Δv): {_debugFuelSlider / 1000f:0.0} т", _debug);
            y += line;
            var newFuel = GUI.HorizontalSlider(new Rect(x, y, w - 40f, 18f), _debugFuelSlider, 0f, (float)ship.FuelCapacityKg);
            if (Mathf.Abs(newFuel - _debugFuelSlider) > 1f)
            {
                _debugFuelSlider = newFuel;
                ship.SetFuelKg(_debugFuelSlider);
            }
            y += line + 6f;

            GUI.Label(new Rect(x, y, w - 20f, 50f),
                "Пробел пауза · 1–4 варп · F камера · L/G/H цели · C цирк. Земли\nРучная тяга остаётся. Тяга на варпе >60 → ×60.",
                _body);

            if (_world.HasInterrupt)
            {
                GUI.Box(new Rect(pad, pad + 570f, w, 70f), GUIContent.none);
                GUI.Label(new Rect(x, pad + 578f, w - 20f, 54f), "⚠ " + _world.LastInterruptRu, _warn);
            }
        }

        static string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var d = (int)(seconds / 86400.0);
            seconds -= d * 86400.0;
            var h = (int)(seconds / 3600.0);
            seconds -= h * 3600.0;
            var m = (int)(seconds / 60.0);
            var s = (int)(seconds - m * 60.0);
            if (d > 0) return $"{d}д {h:00}:{m:00}:{s:00}";
            return $"{h:00}:{m:00}:{s:00}";
        }
    }
}
