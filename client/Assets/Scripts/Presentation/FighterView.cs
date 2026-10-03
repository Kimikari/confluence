using Confluence.Client.Arena;
using Confluence.Client.Mapping;
using Confluence.Core.Rules;
using Confluence.Sim;
using Confluence.Sim.Combat;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;
using UnityEngine;

namespace Confluence.Client.Presentation
{
    /// <summary>
    /// Placeholder body of one duelist rendered from authoritative Sim state: a capsule matching the motor's hit
    /// capsule (radius, standing or crouched height), a visor showing the facing, a focus in hand, the guard's 120-degree
    /// frontal arc while guarding (brighter in the 0.12 s perfect window, spec L104), the melee strike's actual sweep
    /// (reach and arc half-angle of the committed profile, spec L100) during windup and strike, and a grey, toppled
    /// body when defeated. The local player's own body and visor are hidden in first person; guard and swing stay
    /// visible so the player sees their own committed actions. Temporary art (docs/notes/client.md).
    /// </summary>
    public sealed class FighterView : MonoBehaviour
    {
        public static readonly Color[] SideColors = { new Color(0.32f, 0.62f, 1f), new Color(1f, 0.58f, 0.25f) };

        private Transform _body;
        private Transform _visor;
        private Transform _focus;
        private GameObject _guard;
        private MeshRenderer _guardRenderer;
        private MeshRenderer[] _rimRenderers;
        private GameObject _swing;
        private MeshRenderer _swingRenderer;
        private Material _bodyMaterial;
        private Material _deadMaterial;
        private Material _guardMaterial;
        private Material _guardRimMaterial;
        private Material _perfectMaterial;
        private Material _perfectRimMaterial;
        private Material _windupMaterial;
        private Material _strikeMaterial;
        private Mesh _guardMesh;
        private Mesh _rimMesh;
        private Mesh _swingMesh;
        private float _swingReach;
        private float _swingHalfDeg;
        private bool _firstPerson;

        /// <summary>Guard band: radius beyond the capsule, bottom and top heights above the feet, and rim height (meters).</summary>
        public const float GuardMargin = 0.3f;
        public const float GuardBottom = 0.45f;
        public const float GuardTop = 1.75f;
        public const float GuardRimHeight = 0.04f;

        public int Index { get; private set; }

        public bool GuardShown => _guard != null && _guard.activeSelf;

        public bool SwingShown => _swing != null && _swing.activeSelf;

        public static FighterView Create(Transform parent, int index, ArenaMaterialLibrary materials, bool firstPerson)
        {
            var root = new GameObject("Fighter " + index);
            root.transform.SetParent(parent, false);
            FighterView view = root.AddComponent<FighterView>();
            view.Index = index;
            view._firstPerson = firstPerson;
            Color color = SideColors[index % SideColors.Length];
            view._bodyMaterial = materials.Flat(color, index == 0 ? SurfaceKind.Metal : SurfaceKind.Wood);
            view._deadMaterial = materials.Flat(new Color(0.35f, 0.35f, 0.35f), SurfaceKind.Stone);
            // The guard is a faint pane with bright rims so the guarding body stays readable at melee range; the local
            // player's own sweep is fainter because it lies across the first-person view.
            Color rim = Color.Lerp(color, Color.white, 0.4f);
            view._guardMaterial = materials.Glow(new Color(color.r, color.g, color.b, 0.12f));
            view._guardRimMaterial = materials.Glow(new Color(rim.r, rim.g, rim.b, 0.85f));
            view._perfectMaterial = materials.Glow(new Color(1f, 1f, 1f, 0.3f));
            view._perfectRimMaterial = materials.Glow(Color.white);
            float sweepAlpha = firstPerson ? 0.5f : 1f;
            view._windupMaterial = materials.Glow(new Color(1f, 0.85f, 0.3f, 0.35f * sweepAlpha));
            view._strikeMaterial = materials.Glow(new Color(1f, 0.3f, 0.2f, 0.6f * sweepAlpha));

            view._body = Primitive(PrimitiveType.Capsule, "Body", root.transform, view._bodyMaterial).transform;
            view._visor = Primitive(PrimitiveType.Cube, "Visor", root.transform, materials.Flat(Color.white, SurfaceKind.Glass)).transform;
            view._visor.localScale = new Vector3(0.42f, 0.12f, 0.12f);
            view._focus = Primitive(PrimitiveType.Cylinder, "Focus", root.transform, materials.Flat(new Color(0.45f, 0.3f, 0.18f), SurfaceKind.Wood)).transform;
            view._focus.localScale = new Vector3(0.05f, 0.55f, 0.05f);
            if (firstPerson)
            {
                view._body.gameObject.SetActive(false);
                view._visor.gameObject.SetActive(false);
                view._focus.gameObject.SetActive(false);
            }

            // Torso-height band of the 120-degree frontal arc (spec L104) with a rim along its top and bottom edges.
            float radius = CharacterMotor.Radius + GuardMargin;
            view._guardMesh = ShapeMeshes.CylinderSector(radius, GuardBottom, GuardTop, GuardState.HalfArcDeg, 24, "Guard arc");
            view._guard = MeshObject("Guard arc", root.transform, view._guardMesh, view._guardMaterial, out view._guardRenderer);
            view._rimMesh = ShapeMeshes.CylinderSector(radius + 0.005f, 0f, GuardRimHeight, GuardState.HalfArcDeg, 24, "Guard rim");
            view._rimRenderers = new MeshRenderer[2];
            for (int i = 0; i < view._rimRenderers.Length; i++)
            {
                GameObject edge = MeshObject("Guard rim", view._guard.transform, view._rimMesh, view._guardRimMaterial, out view._rimRenderers[i]);
                edge.transform.localPosition = new Vector3(0f, i == 0 ? GuardBottom : GuardTop - GuardRimHeight, 0f);
            }

            view._guard.SetActive(false);
            view._swing = MeshObject("Melee sweep", root.transform, null, view._strikeMaterial, out view._swingRenderer);
            view._swing.SetActive(false);
            return view;
        }

        /// <summary>Places the body at interpolated feet and shows the player's guard and melee state.</summary>
        public void Present(DuelPlayer p, Vector3 feet, float yawDeg, int tick)
        {
            transform.SetPositionAndRotation(feet, CoreSpace.YawRotation(yawDeg));
            float height = p.Motor.Height;
            bool alive = p.Alive;
            _body.localScale = new Vector3(CharacterMotor.Radius * 2f, height * 0.5f, CharacterMotor.Radius * 2f);
            _body.localPosition = new Vector3(0f, height * 0.5f, 0f);
            _body.localRotation = alive ? Quaternion.identity : Quaternion.Euler(80f, 0f, 0f);
            SetMaterial(_body, alive ? _bodyMaterial : _deadMaterial);
            _visor.localPosition = new Vector3(0f, p.Motor.EyeHeight, CharacterMotor.Radius * 0.85f);
            _visor.gameObject.SetActive(!_firstPerson && alive);

            // Focus: held at the side, raised across the body while guarding, thrust forward during a strike.
            Vector3 hand = new Vector3(0.32f, height * 0.55f, 0.2f);
            Quaternion grip = Quaternion.Euler(20f, 0f, 0f);
            if (p.Guard.Active)
            {
                hand = new Vector3(0f, height * 0.7f, 0.42f);
                grip = Quaternion.Euler(0f, 0f, 80f);
            }
            else if (p.Melee.Phase == MeleePhase.Strike)
            {
                hand = new Vector3(0.1f, height * 0.62f, 0.7f);
                grip = Quaternion.Euler(90f, 0f, 0f);
            }

            _focus.localPosition = hand;
            _focus.localRotation = grip;
            _focus.gameObject.SetActive(!_firstPerson && alive);

            bool guarding = alive && p.Guard.Active;
            _guard.SetActive(guarding);
            if (guarding)
            {
                _guard.transform.localScale = new Vector3(1f, height / GlobalRules.Movement.StandingHeight, 1f); // follows a crouch
                bool perfect = p.Guard.IsPerfect(tick);
                _guardRenderer.sharedMaterial = perfect ? _perfectMaterial : _guardMaterial;
                foreach (MeshRenderer edge in _rimRenderers)
                {
                    edge.sharedMaterial = perfect ? _perfectRimMaterial : _guardRimMaterial;
                }
            }

            PresentSwing(p, yawDeg, alive);
        }

        // The strike volume sweeps the committed aim (spec L100: no tracking after commitment), so the sector is drawn
        // in the committed yaw, from the strike origin height, out to the profile's reach.
        private void PresentSwing(DuelPlayer p, float bodyYawDeg, bool alive)
        {
            MeleePhase phase = p.Melee.Phase;
            bool show = alive && (phase == MeleePhase.Windup || phase == MeleePhase.Strike);
            _swing.SetActive(show);
            if (!show)
            {
                return;
            }

            MeleeProfile profile = p.Melee.Profile;
            float half = Mathf.Max(6f, MeleeProfiles.ArcHalfDeg(profile));
            float reach = profile.Reach + CharacterMotor.Radius;
            if (_swingMesh == null || !Mathf.Approximately(reach, _swingReach) || !Mathf.Approximately(half, _swingHalfDeg))
            {
                if (_swingMesh != null)
                {
                    ArenaMaterialLibrary.DestroyObject(_swingMesh);
                }

                _swingMesh = ShapeMeshes.Sector(CharacterMotor.Radius, reach, half, 16, "Melee sweep");
                _swing.GetComponent<MeshFilter>().sharedMesh = _swingMesh;
                _swingReach = reach;
                _swingHalfDeg = half;
            }

            _swing.transform.localPosition = new Vector3(0f, p.Motor.EyeHeight - SimRules.Melee.StrikeDropBelowEye, 0f);
            _swing.transform.localRotation = Quaternion.Euler(0f, p.Melee.CommittedYawDeg - bodyYawDeg, 0f);
            _swingRenderer.sharedMaterial = phase == MeleePhase.Strike ? _strikeMaterial : _windupMaterial;
        }

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                ArenaMaterialLibrary.DestroyObject(collider); // presentation only: the Sim owns collision (DECISIONS D-003)
            }

            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material, out MeshRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        private static void SetMaterial(Transform t, Material material)
        {
            MeshRenderer renderer = t.GetComponent<MeshRenderer>();
            if (renderer.sharedMaterial != material)
            {
                renderer.sharedMaterial = material;
            }
        }

        private void OnDestroy()
        {
            if (_guardMesh != null)
            {
                ArenaMaterialLibrary.DestroyObject(_guardMesh);
            }

            if (_rimMesh != null)
            {
                ArenaMaterialLibrary.DestroyObject(_rimMesh);
            }

            if (_swingMesh != null)
            {
                ArenaMaterialLibrary.DestroyObject(_swingMesh);
            }
        }
    }
}
