"""Copy the UdonSharp editor sources and apply the few patches needed to run them outside Unity.
Only API-surface differences (.NET Standard vs Unity's .NET Framework editor profile) and
Unity-native-only calls are patched; compiler logic is untouched."""
import os, shutil, sys
sdk, out = sys.argv[1], sys.argv[2]
src = os.path.join(sdk, 'worlds/Integrations/UdonSharp/Editor')
shutil.rmtree(out, ignore_errors=True)
shutil.copytree(src, out, ignore=shutil.ignore_patterns('*.meta'))

def patch(rel, old, new, count=1):
    p = os.path.join(out, rel)
    s = open(p, encoding='utf-8-sig').read()
    if s.count(old) < 1:
        sys.exit(f'patch failed: {rel}: {old!r}')
    s = s.replace(old, new) if count == 0 else s.replace(old, new, count)
    open(p, 'w', encoding='utf-8').write(s)

# .NET Standard has these on AssemblyBuilder instead of AppDomain.
patch('Serialization/Formatters/UdonSharpBehaviourFormatterEmitter.cs',
      'AppDomain.CurrentDomain.DefineDynamicAssembly(', 'AssemblyBuilder.DefineDynamicAssembly(')
patch('Serialization/Formatters/UdonSharpBehaviourFormatterEmitter.cs',
      'DefineDynamicModule(RUNTIME_ASSEMBLY_NAME, true)', 'DefineDynamicModule(RUNTIME_ASSEMBLY_NAME)')

# Mono-only workaround reads a private Mono AppDomain field that .NET doesn't have.
patch('UdonSharpUtils.cs',
      'AssemblyLoadEventHandler handler = info.GetValue(AppDomain.CurrentDomain) as AssemblyLoadEventHandler;',
      'AssemblyLoadEventHandler handler = info?.GetValue(AppDomain.CurrentDomain) as AssemblyLoadEventHandler;')

# UdonSharpEditorManager's static constructor hooks Unity editor callbacks (native only).
# The compiler only touches it to silence constructor warnings while reading field defaults.
patch('Compiler/UdonSharpCompilerV1.cs', 'UdonSharpEditorManager.ConstructorWarningsDisabled = true;', '', 0)
patch('Compiler/UdonSharpCompilerV1.cs', 'UdonSharpEditorManager.ConstructorWarningsDisabled = false;', '', 0)

# Reading field defaults constructs the behaviour; MonoBehaviour's constructor is native.
# C# runs field initializers before the base constructor, so run the constructor on an
# uninitialized object and ignore the native base call failing afterwards.
patch('Compiler/UdonSharpCompilerV1.cs', 'component = Activator.CreateInstance(asmType);',
      'component = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(asmType);\n'
      '                    try { asmType.GetConstructor(Type.EmptyTypes).Invoke(component, null); }\n'
      '                    catch (TargetInvocationException e) when (e.InnerException is System.Security.SecurityException) { }')

# The editor debug-info cache (maps Udon runtime errors to source lines) keys a dictionary on
# Unity objects, whose equality is native. Not needed to compile.
patch('Compiler/UdonSharpCompilerV1.cs', 'if (moduleEmitContext.DebugInfo != null)\n', 'if (false)\n')
