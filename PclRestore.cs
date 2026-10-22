using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Resources;
using System.Security.Cryptography;
using System.Windows.Forms;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class PclRestore
{
    private const string OriginalHash = "6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386";
    private const string PatchedHash = "7C101797426766A84EC8C9DE9FBB4B262D1AF2605671C9D0FA18C76F172C5171";
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
        string path = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : FindTarget(
#if TEST
            args.Contains("--test-empty-search")
#else
            false
#endif
        );
        if (path == null)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择 PCL 2.99.0.0 正式版程序";
                dialog.Filter = "PCL 程序|Plain Craft Launcher 2.exe|可执行程序|*.exe";
                if (dialog.ShowDialog() != DialogResult.OK) return 0;
                path = dialog.FileName;
            }
        }
        path = Path.GetFullPath(path);
        var hash = Hash(path);
        if (hash == PatchedHash)
        {
            if (!args.Contains("--no-restart")) StartLauncher(path);
            Show("该 PCL 已恢复下载功能。" + (args.Contains("--no-restart") ? "" : "已启动 PCL。"), false, args);
            return 0;
        }
        if (hash != OriginalHash)
            throw new InvalidOperationException("程序不是已核对的 PCL 2.99.0.0 正式版，未做任何修改。当前 SHA-256：" + hash);
        CloseTargetProcess(path);

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
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PCL2-2.14-original.exe"))
        using (var memory = new MemoryStream())
        {
            stream.CopyTo(memory);
            donor = memory.ToArray();
        }
        string temp = path + ".pclrestore.tmp";
        string backup = path + ".pcl2-2.99-original.bak";
        if (File.Exists(backup)) throw new IOException("备份文件已存在：" + backup);
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
        using (var check = AssemblyDefinition.ReadAssembly(temp))
            if (check.Name.Version.ToString() != "2.99.0.0") throw new InvalidDataException("补丁验证失败");
        File.Replace(temp, path, backup);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        }
        finally { Directory.Delete(dependencies, true); }
        bool restart = !args.Contains("--no-restart");
        if (restart)
            StartLauncher(path);
        Show("已恢复 PCL 2.99.0.0 的 MC 下载入口和整合包安装。" +
            (restart ? "已启动 PCL。" : "") + "\n\n原程序备份：\n" +
            path + ".pcl2-2.99-original.bak", false, args);
        return 0;
    }

    private static string FindTarget(bool testEmptySearch)
    {
        var candidates = new List<string>();
        candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plain Craft Launcher 2.exe"));
        if (testEmptySearch) return null;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.ProcessName.IndexOf("PCL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    process.ProcessName.IndexOf("Plain Craft", StringComparison.OrdinalIgnoreCase) >= 0)
                    candidates.Add(process.MainModule.FileName);
            }
            catch { }
            finally { process.Dispose(); }
        }
        string[] roots = {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar")
        };
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            ScanFolder(root, 2, candidates);
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                string hash = Hash(candidate);
                if (hash == OriginalHash || hash == PatchedHash) return candidate;
            }
            catch { }
        }
        return null;
    }

    private static void StartLauncher(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) });
        }
        catch (Exception e)
        {
            throw new IOException("程序已修改，但启动失败。请手动打开：" + path + "。原因：" + e.Message, e);
        }
    }

    private static void ScanFolder(string folder, int depth, List<string> candidates)
    {
        if (!Directory.Exists(folder)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    var linked = ReadShortcut(file);
                    if (linked != null) candidates.Add(linked);
                }
                else if (Path.GetFileName(file).Equals("Plain Craft Launcher 2.exe", StringComparison.OrdinalIgnoreCase))
                    candidates.Add(file);
            }
            if (depth > 0)
                foreach (var child in Directory.EnumerateDirectories(folder)) ScanFolder(child, depth - 1, candidates);
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private static string ReadShortcut(string path)
    {
        object shell = null, shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            shell = Activator.CreateInstance(shellType);
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            return shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
        }
        catch { return null; }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.ReleaseComObject(shortcut);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }

    private static void CloseTargetProcess(string path)
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!string.Equals(process.MainModule.FileName, path, StringComparison.OrdinalIgnoreCase)) continue;
                if (!process.CloseMainWindow() || !process.WaitForExit(10000))
                    throw new IOException("请先关闭此 PCL 窗口，然后重试：" + path);
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
    }

    private static string Hash(string path)
    {
        using (var sha = SHA256.Create())
        using (var input = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
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
