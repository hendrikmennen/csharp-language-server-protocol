using System.Runtime.CompilerServices;
using DiffEngine;
using Microsoft.CodeAnalysis;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifySourceGenerators.Initialize();
        VerifierSettings.AddExtraSettings(
            settings => settings.Converters.Add(new SyntaxTreeConverter()));

        // The Roslyn formatter is not stable across compiler versions when it comes to the
        // spacing around the null-forgiving operator: older/newer formatters emit `null !`
        // where others emit `null!`. Both compile identically, so normalise it to keep the
        // generator snapshots valid regardless of which SDK/Roslyn version runs the tests.
        VerifierSettings.ScrubLinesWithReplace(
            static line => line
                          .Replace("null !", "null!", StringComparison.Ordinal)
                          .Replace("default !", "default!", StringComparison.Ordinal)
        );

        DiffRunner.Disabled = true;
        DerivePathInfo(
            static (sourceFile, directory, type, method) =>
            {
                static string GetTypeName(Type type) => type.IsNested ? $"{type.ReflectedType!.Name}.{type.Name}" : type.Name;
                var typeName = GetTypeName(type);
                return new(Path.Combine(Path.GetDirectoryName(sourceFile)!, "snapshots"), typeName, method.Name);
            }
        );
    }

    class SyntaxTreeConverter :
        WriteOnlyJsonConverter<SyntaxTree>
    {
        public override void Write(VerifyJsonWriter writer, SyntaxTree value) =>
            writer.WriteValue(
                $@"//HintName: {value.FilePath.Replace("\\", "/", StringComparison.OrdinalIgnoreCase)}
{value.GetText()}"
            );
    }
}
