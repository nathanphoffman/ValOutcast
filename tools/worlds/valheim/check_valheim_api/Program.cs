// Checks that every Valheim function and field the plugin hooks into or calls still exists in the installed game.
// Valheim updates often rename things, and a missing one would only show up as an error at runtime.
// Usage: tools/worlds/valheim/check_valheim_api.sh [path to Valheim folder]
using System;
using System.IO;
using System.Linq;
using System.Reflection;

class Program
{
    const BindingFlags AllMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    static Assembly valheimAsm, utilsAsm;
    static int missing;

    static Type Find(string name) => valheimAsm.GetType(name) ?? utilsAsm.GetType(name);

    // Check that a method exists on the game type, optionally with the expected argument count.
    static void Method(string type, string name, int? parameters = null)
    {
        Type gameType = Find(type);
        bool found = false;

        for (Type current = gameType; current != null && !found; current = current.BaseType)
            found = current.GetMethods(AllMembers).Any(m => m.Name == name && (parameters == null || m.GetParameters().Length == parameters));
        Report(found, type + "." + name + "()");
    }

    // Check that a field exists on the game type, and optionally has the expected type.
    static void Field(string type, string name, string fieldType = null)
    {
        Type gameType = Find(type);
        FieldInfo field = null;

        for (Type current = gameType; current != null && field == null; current = current.BaseType) field = current.GetField(name, AllMembers);
        Report(field != null && (fieldType == null || field.FieldType.Name == fieldType), type + "." + name);
    }

    static void GameType(string type) => Report(Find(type) != null, type);

    static void Property(string type, string name)
    {
        Type gameType = Find(type);
        bool found = false;

        // Walk up the base classes, since the property may be declared on a parent type.
        for (Type current = gameType; current != null && !found; current = current.BaseType) found = current.GetProperty(name, AllMembers) != null;
        Report(found, type + "." + name);
    }

    static void Report(bool found, string what)
    {
        if (!found) { Console.WriteLine("MISSING: " + what); missing++; }
    }

    static void CheckHarmonyPatches()
    {
        Method("Player", "OnSpawned"); Method("Chat", "SendText");
        Method("ObjectDB", "Awake"); Method("ObjectDB", "CopyOtherDB"); Method("ObjectDB", "UpdateRegisters"); Method("ZNetScene", "Awake");
        Method("Tameable", "Tame", 0); Method("Character", "SetTamed", 1); Method("MonsterAI", "SetFollowTarget", 1);
        Field("ItemDrop", "s_instances"); Field("ItemDrop", "m_nview");
        Field("Projectile", "m_vel"); Field("Projectile", "m_owner");
        Field("MonsterAI", "m_targetCreature"); Field("MonsterAI", "m_targetStatic");
        Method("BaseAI", "SetAlerted", 1); Method("Character", "SetMoveDir", 1); Method("Character", "Heal", 2);
        Method("Character", "GetMaxHealth", 0); Method("Character", "GetLookDir", 0);
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
        GameType("IDestructible");
        GameType("WearNTear");
        Field("HitData", "m_hitCollider");
        Field("HitData", "m_toolTier");
        Method("ZNetScene", "GetPrefab", 1);
        Method("ZInput", "GetButtonDown", 1);
        Method("ZInput", "GetMouseScrollWheel", 0);
        Method("PlayerController", "TakeInput", 1);
        Method("Character", "RPC_Damage", 2);
    }

    static void CheckPlayerAndCamera()
    {
        Field("Player", "m_localPlayer", "Player");
        Method("Character", "SetLookDir");
        Field("Character", "m_body", "Rigidbody");
        Method("GameCamera", "UpdateCamera", 1);
        Field("Character", "m_visual", "GameObject");
        Field("Character", "m_maxAirAltitude", "Single");
        Field("HitData", "m_hitType");
        Field("Character", "m_eye");
        Property("GameCamera", "instance");
    }

    static void CheckCreaturesAndDamage()
    {
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

        foreach (var field in new[] { "m_damage", "m_point", "m_dir", "m_pushForce", "m_attacker" }) Field("HitData", field);
        Field("HitData+DamageTypes", "m_slash", "Single");
    }

    static void CheckWorldAndTerrain()
    {
        Method("ZoneSystem", "GetGroundHeight", 2);
        Field("ZoneSystem", "s_instance");
    }

    static void CheckInputAndMenus()
    {
        Method("ZInput", "GetButton", 1);
        Method("ZInput", "GetKey", 2);
        Method("Character", "ApplyPushback", 2);
        Method("ZInput", "GetMouseDelta", 0);
        Method("ZInput", "IsMouseActive", 0);
        Field("PlayerController", "m_mouseSens", "Single");

        foreach (var (type, m) in new[] { ("Menu", "IsVisible"), ("Console", "IsVisible"), ("TextInput", "IsVisible"), ("InventoryGui", "IsVisible"), ("StoreGui", "IsVisible"), ("Minimap", "IsOpen") })
            Method(type, m, 0);
        Method("Chat", "HasFocus", 0);
        GameType("HotkeyBar");
        Property("EnvMan", "instance");
        Field("EnvMan", "m_dirLight", "Light");
    }

    static int Main(string[] args)
    {
        string valheim = args.Length > 0 ? args[0] : Path.Combine(Environment.GetEnvironmentVariable("HOME"), ".local/share/Steam/steamapps/common/Valheim");
        string managed = Path.Combine(valheim, "valheim_Data", "Managed");
        string bepinex = Path.Combine(valheim, "BepInEx", "core");
        var paths = Directory.GetFiles(managed, "*.dll").Concat(Directory.GetFiles(bepinex, "*.dll")).ToList();
        using var loadContext = new MetadataLoadContext(new PathAssemblyResolver(paths), "mscorlib");
        valheimAsm = loadContext.LoadFromAssemblyPath(Path.Combine(managed, "assembly_valheim.dll"));
        utilsAsm = loadContext.LoadFromAssemblyPath(Path.Combine(managed, "assembly_utils.dll"));
        CheckHarmonyPatches();
        CheckPlayerAndCamera();
        CheckCreaturesAndDamage();
        CheckWorldAndTerrain();
        CheckInputAndMenus();
        Console.WriteLine(missing == 0 ? "all Valheim hooks found" : missing + " Valheim hooks missing");

        return missing == 0 ? 0 : 1;
    }
}
