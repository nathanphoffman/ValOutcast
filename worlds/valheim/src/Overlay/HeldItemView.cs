using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// First-person view of the item held in tool mode. Valheim has no first-person view: a held item hangs at the player's side,
    /// well below the camera's field of view. So a copy of the item is drawn in front of the camera instead, moved the way the real
    /// item moves relative to the player's eyes (so it swings when you attack), and lifted into view. The real body and items are
    /// hidden while this is on (they keep animating, which is where the copy gets its movement from).
    /// </summary>
    public static class HeldItemView
    {
        /// <summary>How far the copy is moved from where the real item is, in the camera's right, up and forward directions (meters).</summary>
        public static Vector3 Shift = new Vector3(0f, 0.4f, 0.55f);

        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> RightItem =
            AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> LeftItem =
            AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftItemInstance");

        private class Slot
        {
            public GameObject Source;
            public GameObject Clone;
        }

        private static readonly Slot Right = new Slot();
        private static readonly Slot Left = new Slot();
        private static readonly List<Renderer> Hidden = new List<Renderer>();
        private static float _nextScan;
        private static Animator _animator;
        private static AnimatorCullingMode _oldCulling;

        /// <summary>Call every frame in tool mode, after the camera has been placed.</summary>
        public static void Update(Player player, Transform camera)
        {
            VisEquipment equipment = player.GetComponent<VisEquipment>();

            if (equipment == null || player.m_eye == null) return;

            if (_animator == null)
            {
                // The body is hidden below, and Unity may stop animating what nobody sees: keep it going, the copy follows its movement.
                _animator = player.GetComponentInChildren<Animator>();

                if (_animator != null)
                {
                    _oldCulling = _animator.cullingMode;
                    _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }
            }

            Sync(Right, RightItem(equipment));
            Sync(Left, LeftItem(equipment));

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.5f;   // items change when equipped: new pieces are hidden within half a second

                foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>(false))
                {
                    if (renderer.enabled)
                    {
                        renderer.enabled = false;
                        Hidden.Add(renderer);
                    }
                }
            }

            Pose(Right, player, camera);
            Pose(Left, player, camera);
        }

        /// <summary>Call when tool mode is off: puts everything back.</summary>
        public static void Clear()
        {
            if (Hidden.Count == 0 && Right.Clone == null && Left.Clone == null && _animator == null) return;

            foreach (Renderer renderer in Hidden)
            {
                if (renderer != null) renderer.enabled = true;
            }

            Hidden.Clear();
            Destroy(Right);
            Destroy(Left);

            if (_animator != null) _animator.cullingMode = _oldCulling;
            _animator = null;
        }

        private static void Destroy(Slot slot)
        {
            if (slot.Clone != null) Object.Destroy(slot.Clone);
            slot.Clone = null;
            slot.Source = null;
        }

        private static void Sync(Slot slot, GameObject source)
        {
            if (slot.Source == source && (source == null || slot.Clone != null)) return;
            Destroy(slot);
            slot.Source = source;

            if (source == null) return;

            GameObject copy = Object.Instantiate(source);

            // Only the looks: no scripts, colliders or physics.
            foreach (Component component in copy.GetComponentsInChildren<Component>(true))
            {
                if (component is MonoBehaviour || component is Collider || component is Rigidbody || component is Joint) Object.Destroy(component);
            }

            foreach (Renderer renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            copy.SetActive(true);
            slot.Clone = copy;
        }

        private static void Pose(Slot slot, Player player, Transform camera)
        {
            if (slot.Clone == null || slot.Source == null) return;
            Transform source = slot.Source.transform;
            Quaternion body = player.transform.rotation;

            // Where the real item is, in the player's own frame, seen from the eyes...
            Vector3 local = Quaternion.Inverse(body) * (source.position - player.m_eye.position);

            // ...drawn the same way in front of the camera, lifted into view.
            slot.Clone.transform.position = camera.position + camera.rotation * (local + Shift);
            slot.Clone.transform.rotation = camera.rotation * Quaternion.Inverse(body) * source.rotation;
        }
    }
}
