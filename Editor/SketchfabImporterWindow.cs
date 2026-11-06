using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

public class SketchfabImporterWindow : EditorWindow
{
    private const string ApiTokenPrefKey = "Sketchfab_ApiToken";
    private const string DefaultDownloadFolder = "Assets/ImportedModels";
    private const string DefaultCreditsPath = "Assets/CREDITS.md";

    private string apiToken = "";
    private string modelUrlOrUid = "";
    private string targetFolder = DefaultDownloadFolder;
    private string creditsFilePath = DefaultCreditsPath;
    private bool isProcessing = false;

    [MenuItem("Tools/Sketchfab Importer")]
    public static void ShowWindow()
    {
        var window = GetWindow<SketchfabImporterWindow>("Sketchfab Importer");
        window.minSize = new Vector2(450, 280);
    }

    private void OnEnable()
    {
        apiToken = EditorPrefs.GetString(ApiTokenPrefKey, "");
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Sketchfab Downloader & Attribution Tool", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        EditorGUI.BeginChangeCheck();
        apiToken = EditorGUILayout.PasswordField("API Token", apiToken);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetString(ApiTokenPrefKey, apiToken);
        }

        if (string.IsNullOrWhiteSpace(apiToken))
        {
            EditorGUILayout.HelpBox("Retrieve your API Token from Sketchfab > Settings > Password & API.", MessageType.Info);
        }

        EditorGUILayout.Space(4);
        modelUrlOrUid = EditorGUILayout.TextField("Model URL or UID", modelUrlOrUid);
        targetFolder = EditorGUILayout.TextField("Import Folder", targetFolder);
        creditsFilePath = EditorGUILayout.TextField("Credits File Path", creditsFilePath);

        EditorGUILayout.Space(12);

        using (new EditorGUI.DisabledScope(isProcessing || string.IsNullOrWhiteSpace(apiToken) || string.IsNullOrWhiteSpace(modelUrlOrUid)))
        {
            if (GUILayout.Button("Download, Extract & Credit Model", GUILayout.Height(32)))
            {
                RunImportProcess();
            }
        }
    }

    private async void RunImportProcess()
    {
        isProcessing = true;
        try
        {
            var uid = ExtractUid(modelUrlOrUid);
            EditorUtility.DisplayProgressBar("Sketchfab Importer", "Fetching model metadata...", 0.1f);
            var metadata = await FetchMetadataAsync(uid, apiToken);

            EditorUtility.DisplayProgressBar("Sketchfab Importer", "Requesting download URL...", 0.3f);
            var downloadUrl = await FetchDownloadUrlAsync(uid, apiToken);

            var safeTitle = SanitizeFolderName(metadata.name);
            var modelDestinationDir = Path.Combine(targetFolder, safeTitle).Replace('\\', '/');
            var tempZipPath = Path.Combine(Application.temporaryCachePath, $"{uid}.zip");

            EditorUtility.DisplayProgressBar("Sketchfab Importer", "Downloading model archive...", 0.5f);
            await DownloadArchiveAsync(downloadUrl, tempZipPath, progress =>
            {
                EditorUtility.DisplayProgressBar("Sketchfab Importer", $"Downloading: {Mathf.RoundToInt(progress * 100)}%", 0.5f + (progress * 0.3f));
            });

            EditorUtility.DisplayProgressBar("Sketchfab Importer", "Extracting archive...", 0.85f);
            ExtractZipArchive(tempZipPath, modelDestinationDir);

            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }

            EditorUtility.DisplayProgressBar("Sketchfab Importer", "Updating credits...", 0.95f);
            AppendCredit(creditsFilePath, metadata, modelDestinationDir);

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Success", $"Model '{metadata.name}' imported to {modelDestinationDir} and credited in {creditsFilePath}!", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            isProcessing = false;
            Repaint();
        }
    }

    private static string ExtractUid(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Model URL or UID cannot be empty.");
        }

        var match = Regex.Match(input.Trim(), @"([0-9a-fA-F]{32})");
        if (!match.Success)
        {
            throw new ArgumentException($"Could not extract a valid 32-character Sketchfab UID from '{input}'.");
        }

        return match.Groups[1].Value;
    }

    private static string SanitizeFolderName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return "SketchfabModel";
        }
        return cleaned;
    }

    private static async Task<ModelInfo> FetchMetadataAsync(string uid, string token)
    {
        var url = $"https://api.sketchfab.com/v3/models/{uid}";
        using var request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Authorization", $"Token {token}");

        var op = request.SendWebRequest();
        while (!op.isDone) await Task.Yield();

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new InvalidOperationException($"Failed to fetch metadata [{request.responseCode}]: {request.error}\n{request.downloadHandler.text}");
        }

        var info = JsonUtility.FromJson<ModelInfo>(request.downloadHandler.text);
        if (info == null || string.IsNullOrEmpty(info.name))
        {
            throw new InvalidOperationException("Failed to parse Sketchfab model metadata.");
        }

        return info;
    }

    private static async Task<string> FetchDownloadUrlAsync(string uid, string token)
    {
        var url = $"https://api.sketchfab.com/v3/models/{uid}/download";
        using var request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Authorization", $"Token {token}");

        var op = request.SendWebRequest();
        while (!op.isDone) await Task.Yield();

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new InvalidOperationException($"Failed to request download URL [{request.responseCode}]: {request.error}\n{request.downloadHandler.text}");
        }

        var downloadResp = JsonUtility.FromJson<DownloadResponse>(request.downloadHandler.text);
        if (downloadResp?.gltf != null && !string.IsNullOrEmpty(downloadResp.gltf.url))
        {
            return downloadResp.gltf.url;
        }

        if (downloadResp?.source != null && !string.IsNullOrEmpty(downloadResp.source.url))
        {
            return downloadResp.source.url;
        }

        throw new InvalidOperationException("No compatible glTF or source download URL found for this model.");
    }

    private static async Task DownloadArchiveAsync(string url, string destinationPath, Action<float> progressCallback)
    {
        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET);
        request.downloadHandler = new DownloadHandlerFile(destinationPath);

        var op = request.SendWebRequest();
        while (!op.isDone)
        {
            progressCallback(op.progress);
            await Task.Yield();
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new InvalidOperationException($"Failed to download archive: {request.error}");
        }
    }

    private static void ExtractZipArchive(string zipFilePath, string extractDirectory)
    {
        if (!Directory.Exists(extractDirectory))
        {
            Directory.CreateDirectory(extractDirectory);
        }

        using var archive = ZipFile.OpenRead(zipFilePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                var dirPath = Path.Combine(extractDirectory, entry.FullName);
                Directory.CreateDirectory(dirPath);
                continue;
            }

            var destinationPath = Path.Combine(extractDirectory, entry.FullName);
            var entryDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(entryDir) && !Directory.Exists(entryDir))
            {
                Directory.CreateDirectory(entryDir);
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void AppendCredit(string creditsPath, ModelInfo model, string localPath)
    {
        var directory = Path.GetDirectoryName(creditsPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var authorName = !string.IsNullOrEmpty(model.user?.displayName)
            ? model.user.displayName
            : model.user?.username ?? "Unknown Author";

        var authorUrl = !string.IsNullOrEmpty(model.user?.profileUrl)
            ? model.user.profileUrl
            : $"https://sketchfab.com/{model.user?.username}";

        var licenseName = model.license != null && !string.IsNullOrEmpty(model.license.label)
            ? model.license.label
            : "CC Attribution";

        var licenseUrl = model.license?.url ?? "";
        var modelUrl = !string.IsNullOrEmpty(model.viewerUrl) ? model.viewerUrl : "https://sketchfab.com";
        var dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var sb = new StringBuilder();
        if (!File.Exists(creditsPath))
        {
            sb.AppendLine("# Third-Party Credits");
            sb.AppendLine();
        }

        sb.AppendLine($"### [{model.name}]({modelUrl})");
        sb.AppendLine($"- **Author:** [{authorName}]({authorUrl})");
        if (!string.IsNullOrEmpty(licenseUrl))
        {
            sb.AppendLine($"- **License:** [{licenseName}]({licenseUrl})");
        }
        else
        {
            sb.AppendLine($"- **License:** {licenseName}");
        }
        sb.AppendLine($"- **Local Directory:** `{localPath}`");
        sb.AppendLine($"- **Imported:** {dateStr}");
        sb.AppendLine();

        File.AppendAllText(creditsPath, sb.ToString(), Encoding.UTF8);
    }

    [Serializable]
    private class ModelInfo
    {
        public string name;
        public string viewerUrl;
        public UserInfo user;
        public LicenseInfo license;
    }

    [Serializable]
    private class UserInfo
    {
        public string username;
        public string displayName;
        public string profileUrl;
    }

    [Serializable]
    private class LicenseInfo
    {
        public string label;
        public string url;
    }

    [Serializable]
    private class DownloadResponse
    {
        public DownloadFormat gltf;
        public DownloadFormat source;
    }

    [Serializable]
    private class DownloadFormat
    {
        public string url;
        public long size;
        public int expires;
    }
}
