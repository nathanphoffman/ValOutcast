// Checks that every Valheim function and field the plugin hooks into or calls still exists in the installed game.
// Valheim updates often rename things, and a missing one would only show up as an error at runtime.
// Usage: tools/check_valheim_api.sh [path to Valheim folder]
using System;
using System.IO;
using System.Linq;
using System.Reflection;

class Program
{
    static int Main(string[] args)
    {
        string valheim = args.Length > 0 ? args[0] : Path.Combine(Environment.GetEnvironmentVariable("HOME"), ".local/share/Steam/steamapps/common/Valheim");
        string managed = Path.Combine(valheim, "valheim_Data", "Managed");
        string bepinex = Path.Combine(valheim, "BepInEx", "core");
        var paths = Directory.GetFiles(managed, "*.dll").Concat(Directory.GetFiles(bepinex, "*.dll")).ToList();
        using var mlc = new MetadataLoadContext(new PathAssemblyResolver(paths), "mscorlib");
        var valheimAsm = mlc.LoadFromAssemblyPath(Path.Combine(managed, "assembly_valheim.dll"));
        var utilsAsm = mlc.LoadFromAssemblyPath(Path.Combine(managed, "assembly_utils.dll"));

        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        int missing = 0;

        Type Find(string name) => valheimAsm.GetType(name) ?? utilsAsm.GetType(name);
        void Method(string type, string name, int? parameters = null)
        {
            Type t = Find(type);
            bool ok = false;
            for (Type c = t; c != null && !ok; c = c.BaseType)
                ok = c.GetMethods(All).Any(m => m.Name == name && (parameters == null || m.GetParameters().Length == parameters));
            Report(ok, type + "." + name + "()");
        }
        void Field(string type, string name, string fieldType = null)
        {
            Type t = Find(type);
            FieldInfo f = null;
            for (Type c = t; c != null && f == null; c = c.BaseType) f = c.GetField(name, All);
            Report(f != null && (fieldType == null || f.FieldType.Name == fieldType), type + "." + name);
        }
        void Type(string type) => Report(Find(type) != null, type);
        void Property(string type, string name)
        {
            Type t = Find(type);
            bool ok = false;
            for (Type c = t; c != null && !ok; c = c.BaseType) ok = c.GetProperty(name, All) != null;
            Report(ok, type + "." + name);
        }
        void Report(bool ok, string what)
        {
            if (!ok) { Console.WriteLine("MISSING: " + what); missing++; }
        }

        // Harmony patches
        Method("Player", "OnSpawned"); Method("Chat", "SendText");
        Method("ObjectDB", "Awake"); Method("ObjectDB", "CopyOtherDB"); Method("ObjectDB", "UpdateRegisters"); Method("ZNetScene", "Awake");
        Field("ObjectDB", "m_items"); Field("ObjectDB", "m_recipes"); Field("ZNetScene", "m_namedPrefabs"); Field("Player", "m_customData");
        Method("Player", "FixedUpdate", 0);
        Method("Player", "TakeInput", 0);
        Method("Player", "Interact", 3);
        Method("Player", "GetHoverObject", 0);
        Method("Hud", "IsPieceSelectionVisible", 0);
        Method("Player", "SetControls", 12);
        Field("VisEquipment", "m_rightItemInstance", "GameObject");
        Field("VisEquipment", "m_leftItemInstance", "GameObject");
        Method("Humanoid", "HideHandItems", 2);
        Method("Humanoid", "ShowHandItems", 2);
        Type("IDestructible");
        Type("WearNTear");
        Field("HitData", "m_hitCollider");
        Field("HitData", "m_toolTier");
        Method("ZNetScene", "GetPrefab", 1);
        Method("ZInput", "GetButtonDown", 1);
        Method("ZInput", "GetMouseScrollWheel", 0);
        Method("PlayerController", "TakeInput", 1);
        Method("Character", "RPC_Damage", 2);
        // Player and camera
        Field("Player", "m_localPlayer", "Player");
        Method("Character", "SetLookDir");
        Field("Character", "m_body", "Rigidbody");
        Method("GameCamera", "UpdateCamera", 1);
        Field("Character", "m_visual", "GameObject");
        Field("Character", "m_maxAirAltitude", "Single");
        Field("HitData", "m_hitType");
        Field("Character", "m_eye");
        Property("GameCamera", "instance");
        // Creatures and damage
        Method("Character", "GetAllCharacters", 0);
        Method("Character", "IsPlayer", 0);
        Method("Character", "IsDead", 0);
        Method("Character", "IsTamed", 0);
        Method("Character", "GetRadius", 0);
        Method("Character", "GetHeight", 0);
        Method("Character", "GetCenterPoint", 0);
        Method("Character", "GetHealthPercentage", 0);
        Method("Character", "GetZDOID", 0);
        Method("Character", "Damage", 1);
        Method("BaseAI", "IsEnemy", 2);
        Method("HitData", "GetTotalDamage", 0);
        foreach (var f in new[] { "m_damage", "m_point", "m_dir", "m_pushForce", "m_attacker" }) Field("HitData", f);
        Field("HitData+DamageTypes", "m_slash", "Single");
        // World and terrain
        Method("ZoneSystem", "GetGroundHeight", 2);
        Field("ZoneSystem", "s_instance");
        // Input and menus
        Method("ZInput", "GetButton", 1);
        Method("ZInput", "GetMouseDelta", 0);
        Method("ZInput", "IsMouseActive", 0);
        Field("PlayerController", "m_mouseSens", "Single");
        foreach (var (type, m) in new[] { ("Menu", "IsVisible"), ("Console", "IsVisible"), ("TextInput", "IsVisible"), ("InventoryGui", "IsVisible"), ("StoreGui", "IsVisible"), ("Minimap", "IsOpen") })
            Method(type, m, 0);
        Method("Chat", "HasFocus", 0);
        Type("HotkeyBar");
        Property("EnvMan", "instance");
        Field("EnvMan", "m_dirLight", "Light");

        Console.WriteLine(missing == 0 ? "all Valheim hooks found" : missing + " Valheim hooks missing");
        return missing == 0 ? 0 : 1;
    }
}
