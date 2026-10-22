using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class PclRestore
{
    private enum TargetState { Unsupported, Original, Patched }
    private sealed class MethodCheck
    {
        public readonly string TypeName, MethodName, Original, Patched;
        public readonly int ParameterCount;
        public MethodCheck(string typeName, string methodName, int parameterCount, string original, string patched)
        {
            TypeName = typeName; MethodName = methodName; ParameterCount = parameterCount;
            Original = original; Patched = patched;
        }
    }
    private static readonly MethodCheck[] Checks = {
        new MethodCheck("PCL.ModModpack", "ModpackInstall", 0,
            "BA5BFBB572C37F358B6D275016D396B1BDECEF936D018E592D46B0F23EF4AF55",
            "BD59E9DAEC7F5E529689E907B64D9852BBB80B57BA1A7500C671C0F0B854FEEC"),
        new MethodCheck("PCL.ModModpack", "ModpackInstall", 3,
            "681A25BC3222D42FB202BB67DB4B1F9E28E5BFD5D56469F0E5FFD0797394BAEF",
            "7462027C47E4E7932099327461D96E2E73CD237890BCBF538EE4D0967C5D273C"),
        new MethodCheck("PCL.PageDownloadInstall", "LoaderInit", 0,
            "CD615129B5EF24B4292C7E6306A414F30BACBC70419C76AEA015684F54F36895",
            "D46414774DBA8F98EF33C237D847210C6FE878A87F96D4F649E8A6ACE3504279"),
        new MethodCheck("PCL.PageDownloadModify", "LoadInstanceFinished", 1,
            "184A7EF06D5018065431C080F4C99397809ADD14F6939F48FD67353B46A9A607",
            "B678C8D1A7A1A575D619304916A72FEBE2E547D561948308E010894095109A65"),
        new MethodCheck("PCL.PageDownloadResourceDetail", "InitializeStackResourceVersions", 1,
            "D775585031CB6C3C262F4C6428429AA4CDC3CA2540355DA90DC3C3DC8F2535C5",
            "B2859D41C367969F26AB0AF02132E3E9FDB0B3FFE692030A863FB8D4AAB2E8F9")
    };
    private static ModuleDefinition source;
    private static ModuleDefinition target;
    private static Dictionary<string, TypeDefinition> types;

    [STAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            if (!e.Name.StartsWith("Mono.Cecil,", StringComparison.Ordinal)) return null;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mono.Cecil.dll"))
            {
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                return Assembly.Load(bytes);
            }
        };
        try { return Run(args); }
        catch (Exception e)
        {
            Show("处理失败：" + (args.Contains("--quiet") ? e.ToString() : e.Message), true, args);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plain Craft Launcher 2.exe");
        if (!File.Exists(path))
            throw new FileNotFoundException("当前目录没有 Plain Craft Launcher 2.exe。请把补丁放到 PCL 启动器所在目录再运行。");
        var state = CheckTarget(path);
        if (state == TargetState.Patched)
        {
            Show("这个 PCL 已经恢复了下载功能。直接打开 PCL 即可。", false, args);
            return 0;
        }
        if (state != TargetState.Original)
            throw new InvalidOperationException("此 PCL 的下载代码结构与补丁不兼容，未做任何修改。请针对这个版本更新补丁。");
        EnsureTargetNotRunning(path);
        string backup = CreateBackupPath(path);

        string dependencies = Path.Combine(Path.GetTempPath(), "PCLRestore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dependencies);
        try
        {
        using (var dependenciesAssembly = AssemblyDefinition.ReadAssembly(path))
        {
            var resource = dependenciesAssembly.MainModule.Resources.OfType<EmbeddedResource>()
                .Single(r => r.Name == "PCL.Resources.resources");
            using (var memory = new MemoryStream(resource.GetResourceData()))
            using (var reader = new ResourceReader(memory))
                foreach (System.Collections.DictionaryEntry entry in reader)
                {
                    var bytes = entry.Value as byte[];
                    if (bytes != null && bytes.Length > 2 && bytes[0] == 0x4D && bytes[1] == 0x5A)
                        File.WriteAllBytes(Path.Combine(dependencies, entry.Key + ".dll"), bytes);
                }
        }
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(dependencies);
        var readerOptions = new ReaderParameters { AssemblyResolver = resolver };
        using (resolver)
        {
        byte[] donor;
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PCL2-2.14.zip"))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
        using (var entryStream = archive.GetEntry("Plain Craft Launcher 2.exe").Open())
        using (var memory = new MemoryStream())
        {
            entryStream.CopyTo(memory);
            donor = memory.ToArray();
        }
        string temp = path + ".pclrestore.tmp";
        try
        {
        using (var donorStream = new MemoryStream(donor))
        using (var originalStream = File.OpenRead(path))
        using (var donorAssembly = AssemblyDefinition.ReadAssembly(donorStream, readerOptions))
        using (var patchedAssembly = AssemblyDefinition.ReadAssembly(originalStream, readerOptions))
        {
            source = donorAssembly.MainModule;
            target = patchedAssembly.MainModule;
            types = target.GetTypes().ToDictionary(t => t.FullName);

            Copy("PCL.ModModpack", "ModpackInstall", 0);
            Copy("PCL.ModModpack", "ModpackInstall", 3);
            Copy("PCL.PageDownloadInstall", "LoaderInit", 0);
            Copy("PCL.PageDownloadModify", "LoadInstanceFinished", 1);
            Copy("PCL.PageDownloadResourceDetail", "InitializeStackResourceVersions", 1);

            patchedAssembly.Write(temp);
        }
        if (CheckTarget(temp) != TargetState.Patched) throw new InvalidDataException("补丁验证失败");
        File.Replace(temp, path, backup);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        }
        finally { Directory.Delete(dependencies, true); }
        Show("已恢复 MC 下载入口和整合包安装。现在可以照常打开 PCL。\n\n原程序备份：\n" +
            backup, false, args);
        return 0;
    }

    private static string CreateBackupPath(string path)
    {
        var version = AssemblyName.GetAssemblyName(path).Version.ToString();
        var prefix = path + ".original-" + version;
        for (int number = 1; number <= 100; number++)
        {
            var candidate = prefix + (number == 1 ? "" : "-" + number) + ".bak";
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException("备份文件过多，请先整理当前目录中的 PCL 备份。");
    }

    private static void EnsureTargetNotRunning(string path)
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (string.Equals(process.MainModule.FileName, path, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("请先关闭当前目录中的 PCL，再运行补丁。");
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
    }
    private static TargetState CheckTarget(string path)
    {
        using (var assembly = AssemblyDefinition.ReadAssembly(path))
        {
            if (assembly.Name.Name != "Plain Craft Launcher 2" ||
                assembly.Name.Version.CompareTo(new Version(2, 99, 0, 0)) < 0) return TargetState.Unsupported;
            var typeMap = assembly.MainModule.GetTypes().ToDictionary(t => t.FullName);
            bool original = true, patched = true;
            foreach (var check in Checks)
            {
                TypeDefinition owner;
                if (!typeMap.TryGetValue(check.TypeName, out owner)) return TargetState.Unsupported;
                var method = owner.Methods.SingleOrDefault(m => m.Name == check.MethodName &&
                    m.Parameters.Count == check.ParameterCount);
                if (method == null || !method.HasBody) return TargetState.Unsupported;
                string fingerprint = Fingerprint(method);
                original &= fingerprint == check.Original;
                patched &= fingerprint == check.Patched;
            }
            if (original) return TargetState.Original;
            if (patched) return TargetState.Patched;
            return TargetState.Unsupported;
        }
    }

    private static string Fingerprint(MethodDefinition method)
    {
        var value = string.Join("\n", method.Body.Instructions.Select(i => i.ToString())) + "\nEH\n" +
            string.Join("\n", method.Body.ExceptionHandlers.Select(h => h.HandlerType + ":" +
                (h.TryStart == null ? -1 : h.TryStart.Offset) + ":" +
                (h.TryEnd == null ? -1 : h.TryEnd.Offset) + ":" +
                (h.HandlerStart == null ? -1 : h.HandlerStart.Offset) + ":" +
                (h.HandlerEnd == null ? -1 : h.HandlerEnd.Offset)));
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "");
    }

    private static void Show(string message, bool error, string[] args)
    {
        if (args.Contains("--quiet")) Console.WriteLine(message);
        else MessageBox.Show(message, "PCL 下载功能恢复工具", MessageBoxButtons.OK,
            error ? MessageBoxIcon.Error : MessageBoxIcon.Information);
    }

    private static void Copy(string typeName, string methodName, int parameterCount)
    {
        var oldType = source.GetTypes().SingleOrDefault(t => t.FullName == typeName);
        if (oldType == null) throw new InvalidDataException("旧版缺少类型：" + typeName);
        TypeDefinition newType;
        if (!types.TryGetValue(typeName, out newType)) throw new InvalidDataException("新版缺少类型：" + typeName);
        var oldMethod = oldType.Methods.SingleOrDefault(m => m.Name == methodName && m.Parameters.Count == parameterCount);
        var newMethod = newType.Methods.SingleOrDefault(m => m.Name == methodName && m.Parameters.Count == parameterCount);
        if (oldMethod == null || newMethod == null) throw new InvalidDataException("找不到目标方法：" + typeName + "." + methodName + "(" + parameterCount + ")");
        var oldBody = oldMethod.Body;
        var body = new Mono.Cecil.Cil.MethodBody(newMethod);
        body.InitLocals = oldBody.InitLocals;
        body.MaxStackSize = oldBody.MaxStackSize;
        foreach (var variable in oldBody.Variables)
            body.Variables.Add(new VariableDefinition(MapType(variable.VariableType)));
        var instructionMap = new Dictionary<Instruction, Instruction>();
        foreach (var instruction in oldBody.Instructions)
        {
            var clone = Instruction.Create(OpCodes.Nop);
            clone.OpCode = instruction.OpCode;
            body.Instructions.Add(clone);
            instructionMap.Add(instruction, clone);
        }
        foreach (var instruction in oldBody.Instructions)
        {
            var operand = instruction.Operand;
            var clone = instructionMap[instruction];
            if (operand is Instruction) clone.Operand = instructionMap[(Instruction)operand];
            else if (operand is Instruction[]) clone.Operand = ((Instruction[])operand).Select(i => instructionMap[i]).ToArray();
            else if (operand is VariableDefinition) clone.Operand = body.Variables[oldBody.Variables.IndexOf((VariableDefinition)operand)];
            else if (operand is ParameterDefinition) clone.Operand = newMethod.Parameters[oldMethod.Parameters.IndexOf((ParameterDefinition)operand)];
            else if (operand is MethodReference) clone.Operand = MapMethod((MethodReference)operand);
            else if (operand is FieldReference) clone.Operand = MapField((FieldReference)operand);
            else if (operand is TypeReference) clone.Operand = MapType((TypeReference)operand);
            else clone.Operand = operand;
        }
        foreach (var handler in oldBody.ExceptionHandlers)
            body.ExceptionHandlers.Add(new ExceptionHandler(handler.HandlerType) {
                TryStart = instructionMap[handler.TryStart],
                TryEnd = handler.TryEnd == null ? null : instructionMap[handler.TryEnd],
                HandlerStart = instructionMap[handler.HandlerStart],
                HandlerEnd = handler.HandlerEnd == null ? null : instructionMap[handler.HandlerEnd],
                FilterStart = handler.FilterStart == null ? null : instructionMap[handler.FilterStart],
                CatchType = handler.CatchType == null ? null : MapType(handler.CatchType)
            });
        newMethod.Body = body;
    }

    private static TypeReference MapType(TypeReference type)
    {
        if (type is GenericInstanceType)
        {
            var generic = (GenericInstanceType)type;
            var result = new GenericInstanceType(MapType(generic.ElementType));
            foreach (var arg in generic.GenericArguments) result.GenericArguments.Add(MapType(arg));
            return result;
        }
        if (type is ArrayType)
        {
            var array = (ArrayType)type;
            return new ArrayType(MapType(array.ElementType), array.Rank);
        }
        if (type is ByReferenceType) return new ByReferenceType(MapType(((ByReferenceType)type).ElementType));
        if (type is PointerType) return new PointerType(MapType(((PointerType)type).ElementType));
        if (type is GenericParameter) return type;
        TypeDefinition definition;
        if (types.TryGetValue(type.FullName, out definition)) return definition;
        return target.ImportReference(type);
    }

    private static MethodReference MapMethod(MethodReference method)
    {
        if (method is GenericInstanceMethod)
        {
            var old = (GenericInstanceMethod)method;
            var mapped = new GenericInstanceMethod(MapMethod(old.ElementMethod));
            foreach (var arg in old.GenericArguments) mapped.GenericArguments.Add(MapType(arg));
            return mapped;
        }
        var element = method.DeclaringType is GenericInstanceType ? ((GenericInstanceType)method.DeclaringType).ElementType : method.DeclaringType;
        TypeDefinition owner;
        if (!types.TryGetValue(element.FullName, out owner)) return target.ImportReference(method);
        string name = method.Name;
        if (owner.FullName == "PCL.PageDownloadInstall" && name == "_Lambda$__R3-3") name = "_Lambda$__R3-4";
        var found = owner.Methods.FirstOrDefault(m => m.Name == name &&
            m.Parameters.Count == method.Parameters.Count &&
            m.Parameters.Select(p => p.ParameterType.FullName)
                .SequenceEqual(method.Parameters.Select(p => p.ParameterType.FullName)));
        if (found == null) throw new InvalidDataException("缺少方法：" + method.FullName);
        if (!(method.DeclaringType is GenericInstanceType)) return found;
        var result = new MethodReference(found.Name, MapType(method.ReturnType), MapType(method.DeclaringType));
        result.HasThis = found.HasThis;
        result.ExplicitThis = found.ExplicitThis;
        result.CallingConvention = found.CallingConvention;
        foreach (var param in method.Parameters) result.Parameters.Add(new ParameterDefinition(MapType(param.ParameterType)));
        foreach (var generic in method.GenericParameters) result.GenericParameters.Add(new GenericParameter(generic.Name, result));
        return result;
    }

    private static FieldReference MapField(FieldReference field)
    {
        var element = field.DeclaringType is GenericInstanceType ? ((GenericInstanceType)field.DeclaringType).ElementType : field.DeclaringType;
        TypeDefinition owner;
        if (!types.TryGetValue(element.FullName, out owner)) return target.ImportReference(field);
        var found = owner.Fields.FirstOrDefault(f => f.Name == field.Name);
        if (found == null) throw new InvalidDataException("缺少字段：" + field.FullName);
        if (!(field.DeclaringType is GenericInstanceType)) return found;
        return new FieldReference(found.Name, MapType(field.FieldType), MapType(field.DeclaringType));
    }
}
