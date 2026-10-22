// Looks inside Valheim's assembly_valheim.dll, to find out how its code works before hooking into it.
//   dotnet run --project tools/valheim_il -- members <Type> <text>     methods and fields of a type whose name contains <text>
//                                                                      (<Type> may be * for every type)
//   dotnet run --project tools/valheim_il -- calls <Type> <Method>     the calls, field reads and strings in one method (its logic, roughly)
//   dotnet run --project tools/valheim_il -- il <Type> <Method>        the same with instruction offsets and jump targets
//   dotnet run --project tools/valheim_il -- callers <text>            every method that calls something whose name contains <text>
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

class Program
{
    static void Main(string[] a)
    {
        string home = Environment.GetEnvironmentVariable("HOME");
        string dll = Path.Combine(home, ".local/share/Steam/steamapps/common/Valheim/valheim_Data/Managed/assembly_valheim.dll");
        var asm = AssemblyDefinition.ReadAssembly(dll);
        if (a.Length < 2) { Console.WriteLine("usage: members <Type|*> <text> | calls <Type> <Method> | il <Type> <Method> | callers <text>"); return; }

        if (a[0] == "callers")
        {
            foreach (var t in asm.MainModule.Types) foreach (var m in t.Methods)
            {
                if (!m.HasBody) continue;
                if (m.Body.Instructions.Any(i => (i.OpCode.Code == Code.Call || i.OpCode.Code == Code.Callvirt) && i.Operand.ToString().Contains(a[1])))
                    Console.WriteLine(t.Name + "::" + m.Name);
            }
            return;
        }
        if (a[0] == "members")
        {
            foreach (var t in asm.MainModule.Types.Where(t => a[1] == "*" || t.Name == a[1]))
            {
                string text = a.Length > 2 ? a[2] : "";
                foreach (var m in t.Methods.Where(m => m.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0))
                    Console.WriteLine(t.Name + "::" + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
                foreach (var f in t.Fields.Where(f => f.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0))
                    Console.WriteLine(t.Name + " field " + (f.IsPublic ? "public " : "private ") + f.FieldType.Name + " " + f.Name);
            }
            return;
        }
        var type = asm.MainModule.GetType(a[1]);
        foreach (var m in type.Methods.Where(m => m.Name == a[2]))
        {
            if (!m.HasBody) continue;
            Console.WriteLine("== " + m.Name);
            foreach (var i in m.Body.Instructions)
            {
                bool interesting = i.OpCode.Code == Code.Call || i.OpCode.Code == Code.Callvirt || i.OpCode.Code == Code.Ldstr
                    || i.OpCode.Code == Code.Ldfld || i.OpCode.Code == Code.Stfld || i.OpCode.Code == Code.Ldsfld || i.OpCode.Code == Code.Ldc_R4;
                if (a[0] == "il") Console.WriteLine("  " + i.Offset.ToString("X4") + " " + i.OpCode + " " + (i.Operand is Instruction j ? "-> " + j.Offset.ToString("X4") : i.Operand));
                else if (interesting) Console.WriteLine("  " + i.OpCode + " " + i.Operand);
            }
        }
    }
}
