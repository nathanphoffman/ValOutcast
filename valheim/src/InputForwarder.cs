using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// Sends what you do at the Valheim keyboard and mouse to the player side (Outcast), which does the moving.
    /// Stops while any Valheim menu, chat or console is open.
    /// </summary>
    public class InputForwarder
    {
        private const float SendInterval = 1f / 60f;
        private float _nextSend;
        private float _yaw, _pitch;
        private int _weapon;

        private static bool Button(string name)
        {
            return ZInput.GetButton(name);
        }

        private static bool MenuOpen()
        {
            return Menu.IsVisible()
                || Console.IsVisible()
                || TextInput.IsVisible()
                || (Chat.instance != null && Chat.instance.HasFocus())
                || InventoryGui.IsVisible()
                || StoreGui.IsVisible()
                || Minimap.IsOpen();
        }

        public void Update(VoLink link)
        {
            // Sent whenever you are in a world, even before the other game has taken over: that is how it knows you are playing.
            if (link == null || !link.Connected || Player.m_localPlayer == null)
            {
                return;
            }

            bool open = MenuOpen();
            if (!open)
            {
                // Weapon keys: the hotbar number keys pick a slot, the mouse wheel goes to the next or previous weapon.
                for (int i = 1; i <= 8; i++)
                {
                    if (ZInput.GetButtonDown("Hotbar" + i) || ZInput.GetButtonDown("Hotbar" + i + "Alt")) _weapon = i;
                }
                float wheel = ZInput.GetMouseScrollWheel();
                if (wheel > 0.01f) _weapon = 10;
                else if (wheel < -0.01f) _weapon = 11;
            }

            if (!open && ZInput.IsMouseActive())
            {
                // Mouse moves add up between sends, so no turning is lost.
                Vector2 d = ZInput.GetMouseDelta() * PlayerController.m_mouseSens * Plugin.MouseDegreesPerCount;
                _yaw += -d.x;   // moving the mouse right turns right: yaw is positive to the left
                _pitch += d.y;  // moving the mouse up looks up
            }

            if (Time.realtimeSinceStartup < _nextSend) return;
            _nextSend = Time.realtimeSinceStartup + SendInterval;

            if (open)
            {
                // Menu open: send "no input" so the player stands still.
                link.SendInput(0, 0f, 0f, 0f, 0f, 0f);
                _yaw = _pitch = 0f;
                _weapon = 0;
                return;
            }

            uint buttons = 0;
            if (Button("Attack")) buttons |= VoLink.ButtonAttack;
            // Valheim calls right click "Block" and middle click "SecondaryAttack": both are Outcast's alternate fire.
            if (Button("SecondaryAttack") || Button("Block")) buttons |= VoLink.ButtonAltAttack;
            if (Button("Use") || Button("AltPlace")) buttons |= VoLink.ButtonUse;

            float forward = (Button("Forward") ? 1f : 0f) - (Button("Backward") ? 1f : 0f);
            float right = (Button("Right") ? 1f : 0f) - (Button("Left") ? 1f : 0f);
            float up = Button("Jump") ? 1f : (Button("Crouch") ? -1f : 0f);

            link.SendInput(buttons, forward, right, up, _yaw, _pitch, _weapon);
            _yaw = _pitch = 0f;
            _weapon = 0;
        }
    }
}
