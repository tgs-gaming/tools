using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Sonity;
using ugf.components;

namespace studio.tgs.audioimporter.editor
{
    public class SonityAudioImporter : EditorWindow
    {
        private List<AudioImportData> _audioImportList = new List<AudioImportData>();
        private Vector2 _scrollPosition;
        private GUIStyle _headerStyle;
        private string _gameName = "";
        private bool _isDragActive;
        private Rect _dropArea;
        private GameObject _sonityAudioPrefab;

        [MenuItem("TGS/Audio/Sonity Audio Importer")]
        public static void ShowWindow()
        {
            var window = GetWindow<SonityAudioImporter>();
            window.titleContent = new GUIContent("Audio Importer");
            window.minSize = new Vector2(500, 450);
            window.Show();
        }

        private void OnGUI()
        {
            InitializeStyles();

            EditorGUILayout.LabelField("Audio Importer", _headerStyle);
            EditorGUILayout.Space();

            DrawGeneralSettings();
            DrawDragAndDropArea();
            DrawAudioList();
            DrawBottomButtons();

            HandleDragAndDrop();
        }

        private void InitializeStyles()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.largeLabel)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }
        }

        private void DrawGeneralSettings()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("General Settings", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _gameName = EditorGUILayout.TextField("Game Name", _gameName);

            if (string.IsNullOrWhiteSpace(_gameName))
            {
                var warningStyle = new GUIStyle(EditorStyles.miniLabel);
                warningStyle.normal.textColor = Color.red;
                EditorGUILayout.LabelField("⚠", warningStyle, GUILayout.Width(20));
            }
            EditorGUILayout.EndHorizontal();

            _sonityAudioPrefab = (GameObject)EditorGUILayout.ObjectField(
                "Sonity Audio Prefab",
                _sonityAudioPrefab,
                typeof(GameObject),
                false
            );

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        private void DrawDragAndDropArea()
        {
            var dropAreaStyle = new GUIStyle(EditorStyles.helpBox);
            if (_isDragActive)
            {
                dropAreaStyle.normal.background = MakeTex(2, 2, new Color(0.5f, 0.8f, 1f, 0.3f));
            }

            _dropArea = GUILayoutUtility.GetRect(0.0f, 60.0f, GUILayout.ExpandWidth(true));

            GUI.Box(_dropArea, GetDropAreaContent(), dropAreaStyle);
        }

        private GUIContent GetDropAreaContent()
        {
            if (_isDragActive)
            {
                return new GUIContent("Drop audio files here...", "Release to add audio files");
            }
            else
            {
                return new GUIContent("Drag and drop audio files here \n\n Supported formats: WAV, MP3, OGG, AIFF, AIF", "Drag audio files from your file explorer");
            }
        }

        private void HandleDragAndDrop()
        {
            var currentEvent = Event.current;

            switch (currentEvent.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (_dropArea.Contains(currentEvent.mousePosition))
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        _isDragActive = true;

                        if (currentEvent.type == EventType.DragPerform)
                        {
                            DragAndDrop.AcceptDrag();
                            ProcessDroppedFiles(DragAndDrop.paths);
                            _isDragActive = false;
                            currentEvent.Use();
                        }
                    }
                    else
                    {
                        _isDragActive = false;
                    }
                    break;

                case EventType.DragExited:
                    _isDragActive = false;
                    break;
            }
        }

        private void ProcessDroppedFiles(string[] paths)
        {
            if (paths == null || paths.Length == 0)
                return;

            var supportedExtensions = new HashSet<string> { ".wav", ".mp3", ".ogg", ".aiff", ".aif" };
            var audioFiles = paths.Where(path =>
                !string.IsNullOrEmpty(path) &&
                supportedExtensions.Contains(Path.GetExtension(path).ToLower())
            ).ToArray();

            if (audioFiles.Length == 0)
            {
                EditorUtility.DisplayDialog("No Audio Files", "No supported audio files were found. Supported formats: WAV, MP3, OGG, AIFF, AIF", "OK");
                return;
            }

            foreach (var filePath in audioFiles)
            {
                ProcessAudioFile(filePath);
            }

            if (audioFiles.Length > 1)
            {
                Debug.Log($"[Audio Importer] Added {audioFiles.Length} audio files via drag & drop");
            }
        }

        private void ProcessAudioFile(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                var targetPath = "";

                if (path.StartsWith(Application.dataPath))
                {
                    targetPath = "Assets" + path.Substring(Application.dataPath.Length);
                }
                else
                {
                    var fileName = Path.GetFileName(path);
                    targetPath = Path.Combine("Assets", fileName);

                    if (File.Exists(targetPath))
                    {
                        var result = EditorUtility.DisplayDialogComplex(
                            "Existing file",
                            $"'{fileName}' already exists in the project. Do you want to replace it?",
                            "Yes",
                            "Keep both",
                            "Cancel"
                        );

                        switch (result)
                        {
                            case 0:
                                FileUtil.DeleteFileOrDirectory(targetPath);
                                FileUtil.CopyFileOrDirectory(path, targetPath);
                                break;
                            case 1:
                                targetPath = GetUniqueFilePath(targetPath);
                                FileUtil.CopyFileOrDirectory(path, targetPath);
                                break;
                            case 2:
                                return;
                        }
                    }
                    else
                    {
                        FileUtil.CopyFileOrDirectory(path, targetPath);
                    }
                }

                AssetDatabase.Refresh();

                var audioClip = AssetDatabase.LoadAssetAtPath<AudioClip>(targetPath);

                if (audioClip != null)
                {
                    AddAudioToImportList(audioClip, targetPath);
                }
                else
                {
                    Debug.LogError($"Failed to load AudioClip at path: {targetPath}");
                }
            }
        }

        private string GetUniqueFilePath(string originalPath)
        {
            var directory = Path.GetDirectoryName(originalPath);
            var fileName = Path.GetFileNameWithoutExtension(originalPath);
            var extension = Path.GetExtension(originalPath);
            var counter = 1;

            var newPath = originalPath;
            while (File.Exists(newPath))
            {
                newPath = Path.Combine(directory, $"{fileName}_{counter}{extension}");
                counter++;
            }

            return newPath;
        }

        private void AddAudioToImportList(AudioClip audioClip, string audioPath)
        {
            if (_audioImportList.Exists(x => x.AudioClip == audioClip))
                return;

            AudioImportData newAudio = new AudioImportData
            {
                AudioClip = audioClip,
                AudioSourcePath = audioPath,
                AudioName = "",
                AudioFolder = ""
            };

            _audioImportList.Add(newAudio);
        }

        private void DrawAudioList()
        {
            if (_audioImportList.Count == 0)
            {
                EditorGUILayout.HelpBox("No audio selected. Drag & drop audio files to add them.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"Audios to import ({_audioImportList.Count}):", EditorStyles.boldLabel);

            if (!IsAllDataValid())
            {
                EditorGUILayout.HelpBox("Fill in all fields before importing.", MessageType.Warning);
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));

            for (int i = 0; i < _audioImportList.Count; i++)
            {
                DrawAudioItem(_audioImportList[i], i);
            }

            EditorGUILayout.EndScrollView();
        }

        private bool IsAllDataValid()
        {
            return !string.IsNullOrWhiteSpace(_gameName) &&
                   _audioImportList.Count > 0 &&
                   _audioImportList.All(audio => audio.IsValid());
        }

        private void DrawAudioItem(AudioImportData audioData, int index)
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.LabelField("Clip:", audioData.AudioClip.name, EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            audioData.AudioName = EditorGUILayout.TextField("Audio Name", audioData.AudioName);

            if (string.IsNullOrWhiteSpace(audioData.AudioName))
            {
                var warningStyle = new GUIStyle(EditorStyles.miniLabel);
                warningStyle.normal.textColor = Color.red;
                EditorGUILayout.LabelField("⚠", warningStyle, GUILayout.Width(20));
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            audioData.AudioFolder = EditorGUILayout.TextField("Audio Folder", audioData.AudioFolder);

            if (string.IsNullOrWhiteSpace(audioData.AudioFolder))
            {
                var warningStyle = new GUIStyle(EditorStyles.miniLabel);
                warningStyle.normal.textColor = Color.red;
                EditorGUILayout.LabelField("⚠", warningStyle, GUILayout.Width(20));
            }
            EditorGUILayout.EndHorizontal();

            audioData.ConfigPreset = (AudioConfigPreset)EditorGUILayout.EnumPopup("Configuration Preset", audioData.ConfigPreset);

            if (GUILayout.Button("Remove", GUILayout.Width(80)))
            {
                RemoveAudioFile(_audioImportList[index]);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        private void DrawBottomButtons()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Clear", GUILayout.Width(80)))
            {
                ClearAudioImporter();
            }

            GUILayout.FlexibleSpace();

            bool allValid = IsAllDataValid();
            GUI.enabled = allValid && _audioImportList.Count > 0;

            if (GUILayout.Button("Import", GUILayout.Width(80)))
            {
                ImportAudioFiles();
            }

            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private void ClearAudioImporter() 
        {
            if (_audioImportList.Count > 0) 
            {
                var audioImportListCopy = new List<AudioImportData>(_audioImportList);
                foreach (var audio in audioImportListCopy)
                {
                    RemoveAudioFile(audio);
                }
            }

            _gameName = "";
            _sonityAudioPrefab = null;
        }

        private void RemoveAudioFile(AudioImportData audioImportData) 
        {
            _audioImportList.Remove(audioImportData);
            AssetDatabase.DeleteAsset(audioImportData.AudioSourcePath);
        }

        private void ImportAudioFiles()
        {
            if (!IsAllDataValid())
            {
                string errorMessage = "Please, fill in all fields before importing:\n";

                if (string.IsNullOrWhiteSpace(_gameName))
                {
                    errorMessage += "- Game Name\n";
                }

                foreach (var audio in _audioImportList)
                {
                    if (!audio.IsValid())
                    {
                        errorMessage += $"- Audio configuration: {audio.AudioClip.name}\n";
                    }
                }

                EditorUtility.DisplayDialog("Incomplete configuration", errorMessage, "OK");
                return;
            }

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (AudioImportData audioData in _audioImportList)
                {
                    ProcessAudioImport(audioData);
                }

                UpdateSonityAudioPrefab();

                EditorUtility.DisplayDialog(
                    "Import completed",
                    $"{_audioImportList.Count} audio(s) successfully imported into the game: {_gameName}!", 
                    "OK");

                _audioImportList.Clear();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Audio Importer] Import error: {e.Message}");
                EditorUtility.DisplayDialog(
                    "Error",
                    $"An error occurred while importing: {e.Message}", 
                    "OK");
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
        }

        private void ProcessAudioImport(AudioImportData audioData)
        {
            CreateSonitySoundContainer(audioData);
            CreateSonitySoundEvent(audioData);
            ConfigureAndMoveAudioFile(audioData);

            Debug.Log($"[Audio Importer] Imported audio: {audioData.AudioName} | Type: {audioData.ConfigPreset} | Game: {_gameName} | Folder: {audioData.AudioFolder}");
        }

        private void ConfigureAndMoveAudioFile(AudioImportData audioData)
        {
            var sourcePath = audioData.AudioSourcePath;
            var fileName = Path.GetFileName(sourcePath);
            var targetPath = $"Packages/studio.tgs.{_gameName.ToLower()}assets/Runtime/Audio/{audioData.AudioFolder}/{fileName}";

            var audioImporter = AssetImporter.GetAtPath(sourcePath) as AudioImporter;
            if (audioImporter != null)
            {
                var settings = audioImporter.defaultSampleSettings;

                switch (audioData.ConfigPreset)
                {
                    case AudioConfigPreset.Music:
                        settings.loadType = AudioClipLoadType.Streaming;
                        settings.quality = 0.75f;
                        audioImporter.preloadAudioData = false;
                        break;
                    case AudioConfigPreset.SFX2D:
                        settings.loadType = AudioClipLoadType.DecompressOnLoad;
                        settings.quality = 1f;
                        audioImporter.preloadAudioData = true;
                        break;
                }

                settings.compressionFormat = AudioCompressionFormat.Vorbis;

                audioImporter.defaultSampleSettings = settings;
                audioImporter.forceToMono = false;
                audioImporter.loadInBackground = false;

                audioImporter.SaveAndReimport();
                AssetDatabase.Refresh();
            }

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            var metaFile = ".meta";
            
            File.Copy(sourcePath, targetPath, true);
            File.Delete(sourcePath);
            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);
            
            File.Copy(sourcePath + metaFile, targetPath + metaFile, true);
            File.Delete(sourcePath + metaFile);
            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);
        }

        private void CreateSonitySoundContainer(AudioImportData audioData)
        {
            var containerPath = $"Packages/studio.tgs.{_gameName.ToLower()}pilottab/Runtime/Audio/AudioScripts/{audioData.AudioFolder}/{audioData.AudioName}_SC.asset";

            var containerDirectory = Path.GetDirectoryName(containerPath);
            if (!Directory.Exists(containerDirectory))
            {
                Directory.CreateDirectory(containerDirectory);
            }

            var soundContainer = CreateInstance<SoundContainer>();
            soundContainer.name = $"{audioData.AudioName}_SC";
        
            ApplySoundContainerPreset(soundContainer, audioData.ConfigPreset);
        
            soundContainer.internals.audioClips = new AudioClip[] { audioData.AudioClip };

            AssetDatabase.CreateAsset(soundContainer, containerPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private void ApplySoundContainerPreset(SoundContainer container, AudioConfigPreset preset)
        {
            switch (preset)
            {
                case AudioConfigPreset.Music:
                    container.internals.data.priority = 1f;
                    container.internals.data.neverStealVoice = true;
                    container.internals.data.neverStealVoiceEffects = true;
                    break;

                case AudioConfigPreset.SFX2D:
                    container.internals.data.pitchRandomEnable = true;
                    break;
            }
        }

        private void CreateSonitySoundEvent(AudioImportData audioData)
        {
            var eventPath = $"Packages/studio.tgs.{_gameName.ToLower()}pilottab/Runtime/Audio/AudioScripts/{audioData.AudioFolder}/{audioData.AudioName}_SE.asset";

            var eventDirectory = Path.GetDirectoryName(eventPath);
            if (!Directory.Exists(eventDirectory))
            {
                Directory.CreateDirectory(eventDirectory);
            }

            var containerPath = $"Packages/studio.tgs.{_gameName.ToLower()}pilottab/Runtime/Audio/AudioScripts/{audioData.AudioFolder}/{audioData.AudioName}_SC.asset";
            var soundContainer = AssetDatabase.LoadAssetAtPath<SoundContainer>(containerPath);

            if (soundContainer == null)
            {
                Debug.LogError($"Unable to load SoundContainer: {containerPath}");
                return;
            }

            var soundEvent = CreateInstance<SoundEvent>();
            soundEvent.name = $"{audioData.AudioName}_SE";

            soundEvent.internals.soundContainers = new SoundContainer[] { soundContainer };

            AssetDatabase.CreateAsset(soundEvent, eventPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private void UpdateSonityAudioPrefab()
        {
            if (_sonityAudioPrefab == null)
            {
                Debug.LogWarning("[Audio Importer] No Sonity Audio Prefab assigned. Skipping prefab update.");
                return;
            }

            try
            {
                var audioController = _sonityAudioPrefab.GetComponentInChildren<SonityAudioController>();

                if (audioController == null)
                {
                    Debug.LogError("[Audio Importer] Sonity Audio Controller not found in the prefab hierarchy.");
                    return;
                }

                foreach (var audioData in _audioImportList)
                {
                    var soundEventPath = $"Packages/studio.tgs.{_gameName.ToLower()}pilottab/Runtime/Audio/AudioScripts/{audioData.AudioFolder}/{audioData.AudioName}_SE.asset";
                    var soundEvent = AssetDatabase.LoadAssetAtPath<SoundEvent>(soundEventPath);

                    if (soundEvent != null)
                    {
                        audioController.soundEvents.RemoveAll(element => element != null && element.name == soundEvent.name);
                        audioController.soundEvents.Add(soundEvent);
                    }
                    else
                    {
                        Debug.LogWarning($"[Audio Importer] SoundEvent not found at path: {soundEventPath}");
                    }
                }

                PrefabUtility.SavePrefabAsset(_sonityAudioPrefab);

                Debug.Log($"[Audio Importer] Updated Sonity Audio Prefab with {_audioImportList.Count} sound events");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Audio Importer] Error updating Sonity Audio Prefab: {e.Message}");
            }
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            var pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;
            var result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private void OnDestroy() => ClearAudioImporter();
    }
}
