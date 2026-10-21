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
        private const float DegreesPerMouseCount = 0.11f;
        private float _nextSend;
        private float _yaw, _pitch;

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
            if (link == null || !link.Connected || !PlayerFollow.Active || Player.m_localPlayer == null)
            {
                return;
            }

            bool open = MenuOpen();
            if (!open && ZInput.IsMouseActive())
            {
                // Mouse moves add up between sends, so no turning is lost.
                Vector2 d = ZInput.GetMouseDelta() * PlayerController.m_mouseSens * DegreesPerMouseCount;
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
                return;
            }

            uint buttons = 0;
            if (Button("Attack")) buttons |= VoLink.ButtonAttack;
            if (Button("SecondaryAttack")) buttons |= VoLink.ButtonAltAttack;
            if (Button("Use") || Button("AltPlace")) buttons |= VoLink.ButtonUse;

            float forward = (Button("Forward") ? 1f : 0f) - (Button("Backward") ? 1f : 0f);
            float right = (Button("Right") ? 1f : 0f) - (Button("Left") ? 1f : 0f);
            float up = Button("Jump") ? 1f : (Button("Crouch") ? -1f : 0f);

            link.SendInput(buttons, forward, right, up, _yaw, _pitch);
            _yaw = _pitch = 0f;
        }
    }
}
