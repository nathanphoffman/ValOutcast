using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// Sends what you do at the Valheim keyboard and mouse to the player side (Outcast), which does the moving.
    /// Stops while any Valheim menu, chat or console is open: Outcast stands still and does not turn, and Valheim has the mouse
    /// cursor (its menus handle that themselves). That is what makes the inventory, chests, crafting and the hammer's menu usable.
    /// </summary>
    public class InputForwarder
    {
        private const float SendInterval = 1f / 60f;
        private float _nextSend;
        private float _yaw, _pitch;
        private int _weapon;
        private bool _giveAll;

        private static readonly KeyCode[] AbilityKeys =
        {
            KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5, KeyCode.F6, KeyCode.F7
        };

        private static bool Button(string name)
        {
            return ZInput.GetButton(name);
        }

        /// <summary>Asks the player side for a weapon change (see the weapon request in protocol/PROTOCOL.md). Sent with the next input packet.</summary>
        public void RequestWeapon(int request)
        {
            _weapon = request;
        }

        /// <summary>The cheat code was typed: asks the player side to give everything (weapon request 14), sent as soon as no menu or chat box is open.</summary>
        public void RequestGiveAll()
        {
            _giveAll = true;
        }

        internal static bool MenuOpen()
        {
            return Menu.IsVisible()
                || Console.IsVisible()
                || TextInput.IsVisible()
                || (Chat.instance != null && Chat.instance.HasFocus())
                || InventoryGui.IsVisible()
                || StoreGui.IsVisible()
                || Hud.IsPieceSelectionVisible()
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
            if (!open && !ToolMode.Active)
            {
                // Weapon keys (in tool mode they belong to Valheim's hotbar): the hotbar number keys pick a slot, the mouse wheel goes to the next or previous weapon.
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
            if (!ToolMode.Active)   // in tool mode the buttons belong to Valheim's item
            {
                if (Button("Attack")) buttons |= VoLink.ButtonAttack;
                // Valheim calls right click "Block" and middle click "SecondaryAttack": both are Outcast's alternate fire.
                if (Button("SecondaryAttack") || Button("Block")) buttons |= VoLink.ButtonAltAttack;
                if (Button("Use") || Button("AltPlace")) buttons |= VoLink.ButtonUse;
                // Force powers, on the same keys as Outcast: F1 push, F2 pull, F3 speed, F4 mind trick, F5 heal, F6 grip, F7 lightning.
                for (int i = 0; i < AbilityKeys.Length; i++)
                {
                    if (ZInput.GetKey(AbilityKeys[i])) buttons |= VoLink.ButtonAbility(i + 1);
                }
            }

            float forward = (Button("Forward") ? 1f : 0f) - (Button("Backward") ? 1f : 0f);
            float right = (Button("Right") ? 1f : 0f) - (Button("Left") ? 1f : 0f);
            float up = Button("Jump") ? 1f : (Button("Crouch") ? -1f : 0f);

            if (_giveAll) { _weapon = 14; _giveAll = false; }
            link.SendInput(buttons, forward, right, up, _yaw, _pitch, _weapon);
            _yaw = _pitch = 0f;
            _weapon = 0;
        }
    }
}
