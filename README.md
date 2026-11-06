# Sketchfab Importer for Unity

A Unity Editor tool that downloads, extracts, and automatically adds CC/attribution credits for 3D models from Sketchfab directly into your Unity project.

## Installation via Unity Package Manager (UPM)

1. Open your Unity Project (2021.3 or newer).
2. Go to **Window > Package Manager**.
3. Click the **+** icon in the upper-left corner and select **Add package from git URL...**.
4. Enter the Git repository URL:
   ```text
   https://github.com/gorostiagamanuel/unity-sketchfab-importer.git
   ```
5. Click **Add**.

## Prerequisites

Sketchfab provides model downloads in `.gltf` / `.bin` archive format. To ensure Unity automatically renders and compiles glTF models upon import, install a glTF importer in your project, such as:
- **glTFast** (`com.unity.cloud.gltfast` or via OpenUPM)
- **UnityGLTF**

## Usage

1. Open **Tools > Sketchfab Importer** from the Unity top menu bar.
2. Enter your Sketchfab API Token (found in Sketchfab under **Settings > Password & API**). The token is saved in EditorPrefs so you only need to enter it once.
3. Paste a Sketchfab model URL (e.g. `https://sketchfab.com/3d-models/...`) or a 32-character model UID.
4. (Optional) Customize the destination import folder (default: `Assets/ImportedModels`) or credits file path (default: `Assets/CREDITS.md`).
5. Click **Download, Extract & Credit Model**.

The tool will:
- Query the Sketchfab Data API v3 for model metadata (author, title, license).
- Request the download link and stream the archive to disk.
- Extract the contents directly into your project's import folder.
- Append formatted Markdown attribution to your credits file.
- Trigger an `AssetDatabase.Refresh()` so all imported textures, materials, and models are indexed immediately.

## License

MIT
