// Runs VRChat's real UdonSharp compiler (UdonSharpCompilerV1) outside Unity.
// Usage: UdonSharpHeadless <sdkRoot> <unityDataDir> <builtDllDir> <outDir> <script.cs>...
// Normally run through compile.sh. Only setup that Unity's editor would do is faked here;
// the compile itself is UdonSharpCompilerV1's own Roslyn -> bind -> emit -> Udon assembly pipeline.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

public static class Program
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly List<string> probeDirs = new List<string>();

    public static int Main(string[] args)
    {
        string sdk = args[0], unityData = args[1], builtDir = args[2], outDir = args[3];
        bool runTests = args.Contains("--test");
        string[] scripts = args.Skip(4).Where(a => a != "--test").Select(Path.GetFullPath).ToArray();
        string managed = Path.Combine(unityData, "Managed");

        probeDirs.AddRange(new[]
        {
            builtDir, Path.Combine(managed, "UnityEngine"), managed,
            Path.Combine(sdk, "base/Runtime/VRCSDK/Plugins"), Path.Combine(sdk, "base/Runtime/VRCSDK/Plugins/Harmony"), Path.Combine(sdk, "base/Runtime/VRCSDK/Plugins/DOTween"),
            Path.Combine(sdk, "base/Runtime/VRCSDK/Dependencies/Managed"),
            Path.Combine(sdk, "worlds/Runtime/Udon/External"), Path.Combine(sdk, "worlds/Runtime/VRCSDK/Plugins"),
            Path.Combine(sdk, "worlds/Editor/Udon/External"), Path.Combine(sdk, "worlds/Integrations/UdonSharp/Runtime/Plugins"),
        });
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            if (name.Name == "VRCCore-Standalone") name = new AssemblyName("VRCCore-Editor");
            foreach (string dir in probeDirs)
            {
                string p = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
            }
            return null;
        };

        return Run(sdk, unityData, builtDir, outDir, scripts, runTests);
    }

    static int Run(string sdk, string unityData, string builtDir, string outDir, string[] scripts, bool runTests)
    {
        // Unity's log handler is native; send logs to the console instead.
        UnityEngine.Debug.unityLogger.logHandler = new ConsoleLogHandler();

        // Inside Unity every plugin DLL is loaded; Udon finds its node registries by scanning them.
        foreach (string dir in new[] { "worlds/Editor/Udon/External", "worlds/Runtime/Udon/External", "worlds/Runtime/VRCSDK/Plugins", "base/Runtime/VRCSDK/Plugins" })
            foreach (string dll in Directory.GetFiles(Path.Combine(sdk, dir), "*.dll"))
            {
                string name = Path.GetFileNameWithoutExtension(dll);
                if (name == "VRCCore-Standalone" || name.Contains("Algolia")) continue;
                try { Assembly.Load(name); } catch (Exception e) { Console.WriteLine($"[load] {name}: {e.GetType().Name}"); }
            }

        // Unity also has all of its engine modules and package assemblies loaded.
        foreach (string dll in Directory.GetFiles(Path.Combine(unityData, "Managed/UnityEngine"), "UnityEngine.*.dll")
                     .Concat(Directory.GetFiles(builtDir, "*.dll")))
        {
            try { Assembly.Load(Path.GetFileNameWithoutExtension(dll)); } catch (Exception e) { Console.WriteLine($"[load] {dll}: {e.GetType().Name}"); }
        }

        List<MetadataReference> refs = MetadataReferences(sdk, unityData, builtDir);
        string[] defines = Defines();

        // Unity would have compiled the user scripts into Assembly-CSharp; U# reflects on it.
        Assembly userAssembly = RoslynCompile("Assembly-CSharp", scripts, refs, defines);
        if (userAssembly == null) return 1;
        Assembly libAssembly = Assembly.Load("UdonSharp.Lib");

        Assembly us = typeof(UdonSharp.Compiler.UdonSharpCompileOptions).Assembly;
        Type udonInterface = us.GetType("UdonSharp.Compiler.Udon.CompilerUdonInterface");
        Type contextType = us.GetType("UdonSharp.Compiler.CompilationContext");
        Type compilerType = typeof(UdonSharp.Compiler.UdonSharpCompilerV1);

        // AssemblyCacheInit normally queries the AssetDatabase; fill in what it would find.
        var usAssemblies = ImmutableArray.Create(libAssembly, userAssembly);
        SetStatic(udonInterface, "_udonSharpAssemblies", usAssemblies);
        SetStatic(udonInterface, "_udonSharpAssemblyDefinitions", ImmutableArray<UdonSharpEditor.UdonSharpAssemblyDefinition>.Empty);
        udonInterface.GetProperty("ExternAssemblySet", Any).SetValue(null, new HashSet<Assembly>(
            AppDomain.CurrentDomain.GetAssemblies().Where(a => a != libAssembly && a != userAssembly)));
        SetStatic(udonInterface, "_assemblyInitRan", true);
        SetStatic(contextType, "_udonSharpAssemblyNames", new HashSet<string> { "UdonSharp.Lib" });
        SetStatic(contextType, "_metadataReferences", refs);

        var options = new UdonSharp.Compiler.UdonSharpCompileOptions
        {
            CurrentBuildTarget = UnityEditor.BuildTarget.StandaloneWindows64,
            IsEditorBuild = false,
        };
        object context = Activator.CreateInstance(contextType, Any, null, new object[] { options }, null);

        // Script assemblies: the user scripts (Assembly-CSharp) and UdonSharp.Lib's U# sources.
        Type scriptAsmType = contextType.GetNestedType("ScriptAssembly", Any);
        IList scriptAssemblies = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(scriptAsmType));
        string libDir = Path.Combine(sdk, "worlds/Integrations/UdonSharp/Runtime/Libraries");
        foreach (string[] files in new[] { scripts, Directory.GetFiles(libDir, "*.cs", SearchOption.AllDirectories) })
        {
            object sa = Activator.CreateInstance(scriptAsmType, true);
            ((List<string>)scriptAsmType.GetProperty("SourceFiles").GetValue(sa)).AddRange(files);
            ((List<string>)scriptAsmType.GetProperty("Defines").GetValue(sa)).AddRange(defines);
            scriptAssemblies.Add(sa);
        }

        // Root programs: one UdonSharpProgramAsset per user script, created without Unity's native constructor.
        Type infoType = compilerType.GetNestedType("ProgramAssetInfo", Any);
        IDictionary lookup = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), infoType));
        var assets = new Dictionary<string, UdonSharp.UdonSharpProgramAsset>();
        foreach (string script in scripts)
        {
            var asset = (UdonSharp.UdonSharpProgramAsset)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(UdonSharp.UdonSharpProgramAsset));
            object info = Activator.CreateInstance(infoType, true);
            infoType.GetField("programAsset").SetValue(info, asset);
            infoType.GetField("scriptClass").SetValue(info, userAssembly.GetType(Path.GetFileNameWithoutExtension(script)));
            lookup.Add(script, info);
            assets.Add(script, asset);
        }

        // EmitAllPrograms reads CurrentJob.CompileOptions.
        Type jobType = compilerType.GetNestedType("CompileJob", Any);
        object job = Activator.CreateInstance(jobType, true);
        jobType.GetProperty("CompileOptions").SetValue(job, options);
        jobType.GetProperty("Context").SetValue(job, context);
        compilerType.GetProperty("CurrentJob", Any).SetValue(null, job);

        if (Environment.GetEnvironmentVariable("DUMP_NODES") is string dumpPath)
        {
            udonInterface.GetMethod("CacheInit", Any).Invoke(null, null);
            var nodes = (HashSet<string>)udonInterface.GetField("_nodeDefinitionLookup", Any).GetValue(null);
            File.WriteAllLines(dumpPath, nodes.OrderBy(n => n));
            Console.WriteLine($"dumped {nodes.Count} node definitions");
        }

        MethodInfo compile = compilerType.GetMethods(Any).Single(m => m.Name == "Compile" && m.GetParameters().Length == 3);
        compile.Invoke(null, new object[] { context, lookup, scriptAssemblies });

        // Report.
        int errors = (int)contextType.GetProperty("ErrorCount").GetValue(context);
        foreach (object diag in (IEnumerable)contextType.GetProperty("Diagnostics").GetValue(context))
        {
            Type dt = diag.GetType();
            var loc = (Location)dt.GetProperty("Location").GetValue(diag);
            string where = loc != null && loc.IsInSource
                ? $"{Path.GetFileName(loc.SourceTree.FilePath)}({loc.GetLineSpan().StartLinePosition.Line + 1})" : "";
            Console.WriteLine($"[U#] {dt.GetProperty("Severity").GetValue(diag)} {where}: {dt.GetProperty("Message").GetValue(diag)}");
        }

        Directory.CreateDirectory(outDir);
        foreach (var kv in assets)
        {
            string name = Path.GetFileNameWithoutExtension(kv.Key);
            var program = kv.Value.GetRealProgram();
            if (program == null)
            {
                Console.WriteLine($"FAIL  {name}: no Udon program produced");
                errors++;
                continue;
            }
            var mb = ((Array)contextType.GetProperty("ModuleBindings").GetValue(context)).Cast<object>()
                .First(m => (string)m.GetType().GetField("filePath").GetValue(m) == kv.Key);
            string generated = (string)mb.GetType().GetField("assembly").GetValue(mb);
            File.WriteAllText(Path.Combine(outDir, name + ".uasm"), generated ?? "");
            Console.WriteLine($"OK    {name}: Udon program assembled, {program.ByteCode.Length} bytes of bytecode, " +
                $"{program.EntryPoints.GetExportedSymbols().Length} entry points");
        }

        Console.WriteLine(errors == 0 ? "UDONSHARP COMPILE SUCCEEDED" : $"UDONSHARP COMPILE FAILED ({errors} errors)");
        if (errors > 0 || !runTests) return errors == 0 ? 0 : 1;

        // Run the shared movement tests on the compiled program inside VRChat's Udon VM.
        var movement = assets.First(kv => Path.GetFileNameWithoutExtension(kv.Key) == "SourceMovement").Value;
        var wrapperFactory = new VRC.Udon.Wrapper.UdonDefaultWrapperFactory(new Rig.PassThroughSecurityFilter());
        Rig.Init(movement.GetRealProgram(), wrapperFactory.GetWrapper());
        Console.WriteLine("\nRunning movement tests on the compiled Udon program in the Udon VM");
        return MovementTests.RunAll();
    }

    static void SetStatic(Type t, string field, object value)
    {
        FieldInfo f = t.GetField(field, Any) ?? throw new MissingFieldException(t.Name, field);
        f.SetValue(null, value);
    }

    static string[] Defines() => new[]
    {
        "UNITY_2022_3_22", "UNITY_2022_3", "UNITY_2022", "UNITY_5_3_OR_NEWER", "UNITY_2017_1_OR_NEWER", "UNITY_2018_1_OR_NEWER",
        "UNITY_2019_1_OR_NEWER", "UNITY_2020_1_OR_NEWER", "UNITY_2021_1_OR_NEWER", "UNITY_2021_3_OR_NEWER", "UNITY_2022_1_OR_NEWER",
        "UNITY_2022_2_OR_NEWER", "UNITY_2022_3_OR_NEWER", "UNITY_STANDALONE_WIN", "UNITY_STANDALONE", "ENABLE_MONO",
        "NET_STANDARD_2_1", "NET_STANDARD", "UDONSHARP", "VRC_SDK_VRCSDK3", "COMPILER_UDONSHARP",
    };

    static List<MetadataReference> MetadataReferences(string sdk, string unityData, string builtDir)
    {
        var paths = new List<string> { Path.Combine(unityData, "NetStandard/ref/2.1.0/netstandard.dll") };
        paths.AddRange(Directory.GetFiles(Path.Combine(unityData, "NetStandard/compat/2.1.0/shims/netfx"), "*.dll"));
        paths.AddRange(Directory.GetFiles(Path.Combine(unityData, "Managed/UnityEngine"), "UnityEngine.*.dll"));
        paths.AddRange(Directory.GetFiles(Path.Combine(sdk, "base/Runtime/VRCSDK/Plugins"), "*.dll").Where(p => !p.EndsWith("VRCCore-Standalone.dll") && !p.EndsWith("-Editor.dll")));
        paths.AddRange(Directory.GetFiles(Path.Combine(sdk, "worlds/Runtime/Udon/External"), "*.dll"));
        paths.Add(Path.Combine(sdk, "worlds/Runtime/VRCSDK/Plugins/VRCSDK3.dll"));
        paths.Add(Path.Combine(sdk, "worlds/Runtime/VRCSDK/Plugins/VRCEconomy.dll"));
        paths.Add(Path.Combine(sdk, "base/Runtime/VRCSDK/Plugins/VRCCore-Editor.dll"));
        foreach (string n in new[] { "VRC.Udon", "VRC.Udon.Serialization.OdinSerializer", "UdonSharp.Runtime", "UnityEngine.UI", "Unity.TextMeshPro" })
            paths.Add(Path.Combine(builtDir, n + ".dll"));
        return paths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
    }

    static Assembly RoslynCompile(string name, string[] files, List<MetadataReference> refs, string[] defines)
    {
        var parse = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9).WithPreprocessorSymbols(defines);
        var trees = files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, f));
        var comp = CSharpCompilation.Create(name, trees, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var ms = new MemoryStream();
        var result = comp.Emit(ms);
        foreach (var d in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            Console.WriteLine("[C#] " + d);
        return result.Success ? Assembly.Load(ms.ToArray()) : null;
    }

    class ConsoleLogHandler : UnityEngine.ILogHandler
    {
        public void LogFormat(UnityEngine.LogType logType, UnityEngine.Object context, string format, params object[] args)
            => Console.WriteLine($"[Unity {logType}] " + string.Format(format, args));
        public void LogException(Exception exception, UnityEngine.Object context) => Console.WriteLine("[Unity Exception] " + exception);
    }
}
