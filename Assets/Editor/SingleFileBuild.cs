using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

public class SingleFileBuild
{
    [MenuItem("Build/Build Single File WebGL")]
    public static void Build()
    {
        string buildPath = "Builds/SingleFileBuild";
        string finalFilePath = "Builds/SingleFileBuild/index.html";

        // 1. Configure Build Options
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        buildPlayerOptions.scenes = new[] { "Assets/Scenes/MainScene.unity" }; // Ensure this path is correct
        buildPlayerOptions.locationPathName = buildPath;
        buildPlayerOptions.target = BuildTarget.WebGL;
        buildPlayerOptions.options = BuildOptions.None;

        // 2. Build Player
        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);

        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log("Build succeeded: " + report.summary.totalSize + " bytes");
            BundleToSingleFile(buildPath, finalFilePath);
        }
        else
        {
            Debug.LogError("Build failed");
        }
    }

    private static void BundleToSingleFile(string buildDir, string outputPath)
    {
        string indexHtmlPath = Path.Combine(buildDir, "index.html");
        if (!File.Exists(indexHtmlPath))
        {
            Debug.LogError("index.html not found in build directory.");
            return;
        }

        string htmlContent = File.ReadAllText(indexHtmlPath);
        string buildFolder = Path.Combine(buildDir, "Build");
        
        // Find the loader script
        var loaderFiles = Directory.GetFiles(buildFolder, "*.loader.js");
        if (loaderFiles.Length == 0)
        {
            Debug.LogError("Loader script not found.");
            return;
        }
        string loaderJsPath = loaderFiles[0];
        string loaderJsContent = File.ReadAllText(loaderJsPath);

        // Find other files
        string frameworkJsPath = Directory.GetFiles(buildFolder, "*.framework.js").FirstOrDefault();
        string wasmPath = Directory.GetFiles(buildFolder, "*.wasm").FirstOrDefault();
        string dataPath = Directory.GetFiles(buildFolder, "*.data").FirstOrDefault();

        if (frameworkJsPath == null || wasmPath == null || dataPath == null)
        {
            Debug.LogError("Missing build files (framework, wasm, or data).");
            return;
        }

        // Read and encode content
        string frameworkJsContent = File.ReadAllText(frameworkJsPath);
        string wasmBase64 = Convert.ToBase64String(File.ReadAllBytes(wasmPath));
        string dataBase64 = Convert.ToBase64String(File.ReadAllBytes(dataPath));

        // Embed content into HTML
        // 1. Embed Loader JS
        htmlContent = Regex.Replace(htmlContent, "<script src=\"Build/.*\\.loader\\.js\"></script>", $"<script>{loaderJsContent}</script>");

        // 2. Prepare Blob URL generation script
        StringBuilder blobScript = new StringBuilder();
        blobScript.AppendLine("<script>");
        blobScript.AppendLine("var unityBlobUrls = {};");
        blobScript.AppendLine("function base64ToBlob(base64, type) {");
        blobScript.AppendLine("    var binary = atob(base64);");
        blobScript.AppendLine("    var len = binary.length;");
        blobScript.AppendLine("    var buffer = new ArrayBuffer(len);");
        blobScript.AppendLine("    var view = new Uint8Array(buffer);");
        blobScript.AppendLine("    for (var i = 0; i < len; i++) {");
        blobScript.AppendLine("        view[i] = binary.charCodeAt(i);");
        blobScript.AppendLine("    }");
        blobScript.AppendLine("    return new Blob([buffer], {type: type});");
        blobScript.AppendLine("}");
        
        blobScript.AppendLine($"unityBlobUrls.framework = URL.createObjectURL(base64ToBlob('{Convert.ToBase64String(Encoding.UTF8.GetBytes(frameworkJsContent))}', 'application/javascript'));");
        blobScript.AppendLine($"unityBlobUrls.code = URL.createObjectURL(base64ToBlob('{wasmBase64}', 'application/wasm'));");
        blobScript.AppendLine($"unityBlobUrls.data = URL.createObjectURL(base64ToBlob('{dataBase64}', 'application/octet-stream'));");
        blobScript.AppendLine("</script>");

        // Insert Blob script before the config script
        htmlContent = htmlContent.Replace("<script>", blobScript.ToString() + "<script>");

        // 3. Modify the Unity config to use Blob URLs
        // We need to find where createUnityInstance is called and modify the config object
        // This is a bit tricky with Regex as the config object format can vary.
        // A safer bet is to intercept the config object if possible, or replace the file names in the config.
        
        // Standard template usually has:
        // createUnityInstance(canvas, config, ...
        // config = { dataUrl: "...", frameworkUrl: "...", codeUrl: "...", ... }
        
        // We will try to replace the property values using Regex
        htmlContent = Regex.Replace(htmlContent, "dataUrl: \"[^\"]*\"", "dataUrl: unityBlobUrls.data");
        htmlContent = Regex.Replace(htmlContent, "frameworkUrl: \"[^\"]*\"", "frameworkUrl: unityBlobUrls.framework");
        htmlContent = Regex.Replace(htmlContent, "codeUrl: \"[^\"]*\"", "codeUrl: unityBlobUrls.code");
        
        // Also replace symbolsUrl and streamingAssetsUrl if they exist, though we might not have them bundled here.
        // For a simple scene, usually just data, framework, code are critical.

        File.WriteAllText(outputPath, htmlContent);
        Debug.Log("Single file build created at: " + outputPath);
        
        // Clean up extra files if desired, but for now we leave them in the folder so the user can see what happened.
    }
}
