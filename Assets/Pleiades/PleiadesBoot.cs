using UnityEngine;
using Pleiades.CoreSim;

namespace Pleiades
{
    public sealed class PleiadesBoot : MonoBehaviour
    {
        public static PleiadesBoot Instance { get; private set; }

        public SimWorld World { get; private set; }
        public MapView Map { get; private set; }
        public WatchHud Hud { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (Object.FindAnyObjectByType<PleiadesBoot>() != null)
                return;
            var go = new GameObject("PleiadesBoot");
            go.AddComponent<PleiadesBoot>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            World = new SimWorld();

            EnsureCamera();
            Map = gameObject.AddComponent<MapView>();
            Map.Bind(World);
            Hud = gameObject.AddComponent<WatchHud>();
            Hud.Bind(World);
        }

        static void EnsureCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                cam = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
                camGo.AddComponent<AudioListener>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
            cam.orthographic = true;
            cam.orthographicSize = 24f;
            cam.transform.position = new Vector3(0f, 200f, 0f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20000f;
        }

        void Update()
        {
            if (World == null) return;
            HandleInput();
            World.Tick(Time.unscaledDeltaTime);
            if (Map != null) Map.Refresh();
            FollowCamera();
        }

        void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.Space))
                World.TogglePause();

            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
                World.SetWarpIndex(0);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
                World.SetWarpIndex(1);
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
                World.SetWarpIndex(2);
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
                World.SetWarpIndex(3);

            if (Input.GetKeyDown(KeyCode.G))
                World.TryDepart(DestinationId.Geo);
            if (Input.GetKeyDown(KeyCode.L))
                World.TryDepart(DestinationId.Lunar);
            if (Input.GetKeyDown(KeyCode.F))
                World.FollowShip = !World.FollowShip;
            if (Input.GetKeyDown(KeyCode.C))
                Circularize();

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return))
                World.ClearInterrupt();

            var p = 0.0;
            var r = 0.0;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) p += 1.0;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) p -= 1.0;
            if (Input.GetKey(KeyCode.E)) r += 1.0;
            if (Input.GetKey(KeyCode.Q)) r -= 1.0;
            World.CmdPrograde = p;
            World.CmdRadial = r;
            World.Boost = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            var cam = Camera.main;
            if (cam == null) return;

            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                var size = cam.orthographicSize * (scroll > 0f ? 0.85f : 1.15f);
                cam.orthographicSize = Mathf.Clamp(size, 8f, 2500f);
            }

            if (Input.GetMouseButton(1))
            {
                World.FollowShip = false;
                var dx = Input.GetAxis("Mouse X");
                var dy = Input.GetAxis("Mouse Y");
                var scale = cam.orthographicSize * 0.05f;
                cam.transform.position += new Vector3(-dx * scale, 0f, -dy * scale);
            }
        }

        void Circularize()
        {
            var o = World.Ship.Orbit;
            var vCirc = AstroMath.CircularSpeed(o.Mu, o.RadiusM);
            var sp = o.SpeedMps;
            if (sp < 1e-6) return;
            var need = vCirc - sp;
            if (!World.Ship.TryBurn(System.Math.Abs(need), out _))
            {
                World.SetPaused(true);
                return;
            }
            o.ApplyDeltaV(need, 0.0);
        }

        void FollowCamera()
        {
            if (!World.FollowShip) return;
            var cam = Camera.main;
            if (cam == null) return;
            World.Ship.Orbit.GetPositionMeters(out var x, out _, out var z);
            var ux = (float)AstroMath.MetersToUnits(x);
            var uz = (float)AstroMath.MetersToUnits(z);
            var p = cam.transform.position;
            cam.transform.position = new Vector3(
                Mathf.Lerp(p.x, ux, 0.18f),
                p.y,
                Mathf.Lerp(p.z, uz, 0.18f));
        }
    }
}
