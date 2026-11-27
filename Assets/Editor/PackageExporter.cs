using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class PackageExporter
{
    const string OutDir = "Entrega";
    const string OutFile = "ACG_Final_Project.unitypackage";

    static readonly string[] Roots =
    {
        "Assets/Scenes/SampleScene.unity",
        "Assets/Scenes/ShaderShowcase.unity",
        "Assets/Scenes/Generated",
        "Assets/Cinematic",
        "Assets/Editor",
        "Assets/shaders",
        "Assets/bunny",
        "Assets/Zombunny",
        "Assets/Showcase",
    };

    [MenuItem("ACG/Exportar unitypackage de entrega")]
    public static void Export()
    {
        var paths = new List<string>();
        foreach (var p in Roots)
            if (AssetDatabase.IsValidFolder(p) || File.Exists(p)) paths.Add(p);

        Directory.CreateDirectory(OutDir);
        string file = Path.Combine(OutDir, OutFile);
        AssetDatabase.ExportPackage(paths.ToArray(), file,
            ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies);
        Debug.Log("Paquete exportado: " + Path.GetFullPath(file));
    }
}
