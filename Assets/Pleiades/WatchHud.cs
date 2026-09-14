using UnityEngine;
using Pleiades.CoreSim;

namespace Pleiades
{
    /// <summary>Russian watch-style OnGUI HUD for sprint-1 Play slice.</summary>
    public sealed class WatchHud : MonoBehaviour
    {
        SimWorld _world;
        float _payloadSlider;
        GUIStyle _title;
        GUIStyle _body;
        GUIStyle _warn;
        bool _stylesReady;

        public void Bind(SimWorld world)
        {
            _world = world;
            _payloadSlider = 0f;
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
                normal = { textColor = new Color(1f, 0.45f, 0.35f) },
                wordWrap = true
            };
            _stylesReady = true;
        }

        void OnGUI()
        {
            if (_world == null) return;
            EnsureStyles();

            const float pad = 12f;
            float w = 360f;
            GUI.Box(new Rect(pad, pad, w, 420f), GUIContent.none);

            float y = pad + 8f;
            float x = pad + 10f;
            float line = 22f;

            GUI.Label(new Rect(x, y, w - 20f, 24f), "ПЛЕЯДЫ — хронометр", _title);
            y += line + 4f;

            var ship = _world.Ship;
            GUI.Label(new Rect(x, y, w - 20f, line), $"Корабль: {ship.Name}", _body); y += line;
            GUI.Label(new Rect(x, y, w - 20f, line), $"Фаза: {_world.PhaseLabelRu}", _body); y += line;
            GUI.Label(new Rect(x, y, w - 20f, line), $"Цель: {_world.DestinationLabelRu}", _body); y += line;
            GUI.Label(new Rect(x, y, w - 20f, line),
                $"Время: {FormatTime(_world.SimTimeSeconds)}  |  {(_world.Paused ? "ПАУЗА" : "ХОД")}", _body);
            y += line;
            GUI.Label(new Rect(x, y, w - 20f, line),
                $"Варп: ×{_world.WarpFactor:0}   (1/2/3/4)", _body);
            y += line;

            double altKm = (ship.Orbit.RadiusM - GravityBody.Earth.RadiusM) / 1000.0;
            GUI.Label(new Rect(x, y, w - 20f, line),
                $"Орбита r: {ship.Orbit.RadiusM / 1000.0:0} км  (h≈{altKm:0} км)", _body);
            y += line;
            GUI.Label(new Rect(x, y, w - 20f, line),
                $"Топливо: {ship.FuelKg / 1000.0:0.00} / {ship.FuelCapacityKg / 1000.0:0} т", _body);
            y += line;
            GUI.Label(new Rect(x, y, w - 20f, line),
                $"Δv запас: {ship.AvailableDeltaV / 1000.0:0.000} км/с  (Isp {ship.IspSeconds:0} с)", _body);
            y += line;
            GUI.Label(new Rect(x, y, w - 20f, line),
                $"Масса: сухая {ship.DryMassKg / 1000.0:0.0} т | мокрая {ship.WetMassKg / 1000.0:0.0} т", _body);
            y += line + 4f;

            GUI.Label(new Rect(x, y, w - 20f, line), $"Полезная нагрузка: {_payloadSlider:0} кг", _body);
            y += line;
            float newPayload = GUI.HorizontalSlider(new Rect(x, y, w - 40f, 18f), _payloadSlider, 0f, 20000f);
            if (Mathf.Abs(newPayload - _payloadSlider) > 0.5f)
            {
                _payloadSlider = newPayload;
                ship.SetPayloadKg(_payloadSlider);
            }
            y += line + 6f;

            var geo = Hohmann.LeoToGeo();
            var lun = Hohmann.LeoToLunar();
            GUI.Label(new Rect(x, y, w - 20f, line),
                $"ГСО Хоман: {geo.TotalDeltaVKmS:0.00} км/с  TOF {FormatTime(geo.TimeOfFlightSeconds)}", _body);
            y += line;
            GUI.Label(new Rect(x, y, w - 20f, line),
                $"Луна Хоман: {lun.TotalDeltaVKmS:0.00} км/с  TOF {FormatTime(lun.TimeOfFlightSeconds)}", _body);
            y += line + 4f;

            if (GUI.Button(new Rect(x, y, 150f, 28f), "Отлёт → ГСО (G)"))
            {
                _world.ClearInterrupt();
                _world.TryDepart(DestinationId.Geo);
            }
            if (GUI.Button(new Rect(x + 160f, y, 150f, 28f), "Отлёт → Луна (L)"))
            {
                _world.ClearInterrupt();
                _world.TryDepart(DestinationId.Lunar);
            }
            y += 34f;

            GUI.Label(new Rect(x, y, w - 20f, 60f),
                "Space пауза · 1–4 варп · колёсико зум · ПКМ панорама · Esc сброс прерывания",
                _body);
            y += 48f;

            if (_world.HasInterrupt)
            {
                GUI.Box(new Rect(pad, pad + 430f, w, 70f), GUIContent.none);
                GUI.Label(new Rect(x, pad + 438f, w - 20f, 54f), "⚠ " + _world.LastInterruptRu, _warn);
            }

            // Module strip
            float mx = Screen.width - 280f;
            float my = pad;
            GUI.Box(new Rect(mx - 8f, my, 270f, 220f), GUIContent.none);
            GUI.Label(new Rect(mx, my + 6f, 250f, 22f), "Модули", _title);
            float myy = my + 32f;
            foreach (var m in ship.Modules)
            {
                GUI.Label(new Rect(mx, myy, 250f, 18f), $"· {m.DisplayNameRu}  ({m.MassKg / 1000.0:0.0} т)", _body);
                myy += 18f;
            }
        }

        static string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int d = (int)(seconds / 86400.0);
            seconds -= d * 86400.0;
            int h = (int)(seconds / 3600.0);
            seconds -= h * 3600.0;
            int m = (int)(seconds / 60.0);
            int s = (int)(seconds - m * 60.0);
            if (d > 0) return $"{d}д {h:00}:{m:00}:{s:00}";
            return $"{h:00}:{m:00}:{s:00}";
        }
    }
}
