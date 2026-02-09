using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace com.tgs.assetdependencymanager.editor
{
	internal class DependencyManagerMenuitem
	{
		private const string MENUITEM_MANAGER = "Assets/TGS/Asset Dependencies/Manager";
		private const string MENUITEM_IMPORTER = "Assets/TGS/Asset Dependencies/Import Package";
		private const string MENUITEM_EXPORTER = "Assets/TGS/Asset Dependencies/Export Package";
		private const string MENUITEM_MANAGER_TOP = "TGS/Asset Dependencies/Manager";
		private const string MENUITEM_IMPORTER_TOP = "TGS/Asset Dependencies/Import Package";
		private const string MENUITEM_EXPORTER_TOP = "TGS/Asset Dependencies/Export Package";

		[MenuItem(MENUITEM_MANAGER, true)]
		static bool Context_DuplicateAsset_Validation() => Selection.assetGUIDs.Length >= 1;

		[MenuItem(MENUITEM_MANAGER)]
		[MenuItem(MENUITEM_MANAGER_TOP)]
		static void Context_DuplicateAsset()
		{
			// Support multiple selected assets
			var selectedAssets = Selection.assetGUIDs
				.Select(guid => AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid)))
				.ToList();
			if (selectedAssets.Count == 0)
			{
				selectedAssets.Add(null);
			}

			var window = DependencyManager.GetWindow<DependencyManager>();
			window.titleContent = new GUIContent("Asset Dependency Manager");
			DependencyManager.OriginalAssets = selectedAssets;
		}

		[MenuItem(MENUITEM_IMPORTER, true)]
		static bool Context_ImportPackage_Validation() =>
			Selection.assetGUIDs.Length == 1 &&
			AssetDatabase.IsValidFolder(AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]));

		[MenuItem(MENUITEM_IMPORTER)]
		[MenuItem(MENUITEM_IMPORTER_TOP)]
		static void Context_ImportPackage()
		{
			if (Selection.assetGUIDs.Length != 1) return;

			string folderPath = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]);
			string packagePath = EditorUtility.OpenFilePanel(
				$"Select .{DependencyManager.ASSET_PACKAGE_EXTENSION} file to import", "",
				DependencyManager.ASSET_PACKAGE_EXTENSION);
			if (!string.IsNullOrEmpty(packagePath))
			{
				Debug.Log($"Selected package: {packagePath} to import into {folderPath}");
				DependencyManager.ImportPackage(packagePath, folderPath);
			}
		}
		
		[MenuItem(MENUITEM_EXPORTER, true)]
		static bool Context_ExportPackage_Validation() =>
			Selection.assetGUIDs.Length >= 1 &&
			Selection.assetGUIDs.All(guid => !AssetDatabase.IsValidFolder(AssetDatabase.GUIDToAssetPath(guid)));

		[MenuItem(MENUITEM_EXPORTER)]
		[MenuItem(MENUITEM_EXPORTER_TOP)]
		static void Context_ExportPackage()
		{
			if (Selection.assetGUIDs.Length < 1) return;

			var selectedAssets = Selection.assetGUIDs
				.Select(guid => AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid)))
				.ToList();
			
			DependencyManager.ExportSelectedPackages(selectedAssets);
		}
	}

	public class DependencyManager : EditorWindow
	{
		public const string DUPLICATED_PATH_SUFFIX = "_Duplicated_Assets";
		public const string EXPORTED_PACKAGE_SUFFIX = "_Exported_Assets";
		public const string DEPENDENCIES_PATH = "_Dependencies";
		public const string DEPENDENCIES_BUNDLES_PATH = "Packages/studio.";
		public static readonly string[] DEPENDENCIES_COMMON_BUNDLE_PATHS = { "com.tgs.ugf" };
		public const string ASSET_PACKAGE_EXTENSION = "tgspackage";

		public const int COLUMN_DEFAULT_SIZE = 220;

		public const string EMPTY_GUID = "00000000000000000000000000000000";

		private Vector2 _scrollPosition;
		private int _selectedTab;
		private bool _previewFoldout = true;
		private int _objectPickerId = -1;
		private int _objectPickerIndex = -1;
		private static readonly string[] Tabs = { "Copy To", "Replace References", "Export & Import" };

		private static List<Object> _originalAssets;

		public static List<Object> OriginalAssets
		{
			get => _originalAssets;
			set
			{
				_originalAssets = value;
				_assetGameDependencies = null;
				_assetSystemDependencies = null;
				_uniqueAssetGameDependencies = null;
				_uniqueAssetGameDependenciesReplace = null;
			}
		}

		public static DefaultAsset NewAssetPath;
		public static bool GameDependenciesFoldout;
		public static bool SystemDependenciesFoldout;
		public static bool NewGameDependenciesFoldout;

		private static List<string> _assetGameDependencies;

		public static List<string> AssetGameDependencies
		{
			get
			{
				if (OriginalAssets == null || OriginalAssets.Count == 0)
					return new List<string>();
				if (_assetGameDependencies == null)
				{
					var paths = OriginalAssets
						.Select(a => AssetDatabase.GetAssetPath(a))
						.Where(p => !string.IsNullOrEmpty(p))
						.ToArray();
					_assetGameDependencies = GetGameAssetDependencies(paths).Distinct().ToList();   
					
					// Remove the asset itself from dependencies
					foreach (var path in paths)
					{
						if (_assetGameDependencies != null)
							_assetGameDependencies = _assetGameDependencies.Where(dep => dep != path).ToList();
					}
				}

				return _assetGameDependencies;
			}
		}

		private static Object[] _uniqueAssetGameDependenciesReplace;
		private static bool[] _uniqueAssetGameDependenciesRemove;

		private static List<string> _uniqueAssetGameDependencies;

		public static List<string> UniqueAssetGameDependencies
		{
			get
			{
				if (AssetGameDependencies == null)
				{
					_uniqueAssetGameDependencies = null;
					_uniqueAssetGameDependenciesReplace = null;
					_uniqueAssetGameDependenciesRemove = null;
				}
				else if (_uniqueAssetGameDependencies == null)
				{
					_uniqueAssetGameDependencies = AssetGameDependencies.Distinct().ToList();
					_uniqueAssetGameDependenciesReplace = new Object[AssetGameDependencies.Count];
					_uniqueAssetGameDependenciesRemove = new bool[AssetGameDependencies.Count];
				}

				return _uniqueAssetGameDependencies;
			}
		}

		private static List<string> _assetSystemDependencies;

		public static List<string> AssetSystemDependencies
		{
			get
			{
				if (OriginalAssets == null || OriginalAssets.Count == 0)
					return new List<string>();
				if (_assetSystemDependencies == null)
				{
					var paths = OriginalAssets
						.Select(a => AssetDatabase.GetAssetPath(a))
						.Where(p => !string.IsNullOrEmpty(p))
						.ToArray();
					_assetSystemDependencies = GetSystemAssetDependencies(paths).Distinct().ToList();
				}

				return _assetSystemDependencies;
			}
		}

		private bool _initStyles;
		private GUIStyle _itemDescriptionStyle;
		private GUIStyle _itemTitleStyle;
		private GUIStyle _itemSubtitleStyle;


		private void InitStyles()
		{
			_initStyles = true;

			// Item Description
			_itemDescriptionStyle = new GUIStyle(GUI.skin.label)
			{
				fontSize = 10,
				fontStyle = FontStyle.Italic
			};

			// Title
			_itemTitleStyle = new GUIStyle(EditorStyles.boldLabel)
			{
				fontSize = 14
			};

			// Subtitle
			_itemSubtitleStyle = new GUIStyle(EditorStyles.boldLabel)
			{
				fontSize = 12
			};
		}

		private void OnGUI()
		{
			_scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

			if (!_initStyles) InitStyles();

			DrawSelectedAssetsSection();
			DrawDependenciesSection();
			DrawTabsSection();
			EditorGUILayout.EndScrollView();
		}

		private void DrawSelectedAssetsSection()
		{
			GUILayout.Space(12);
			GUILayout.Label("Selected Assets", _itemTitleStyle);

			EnsureOriginalAssetsList();
			EditorGUILayout.BeginVertical(GUI.skin.box);
			for (int i = 0; i < OriginalAssets.Count; i++)
			{
				EditorGUILayout.BeginHorizontal();
				var oldAsset = OriginalAssets[i];
				var newAsset = EditorGUILayout.ObjectField(
					$"Asset {i + 1}", oldAsset, typeof(Object), false) as Object;
				if (newAsset != oldAsset)
				{
					OriginalAssets[i] = newAsset;
					ResetDependencyCaches();
				}

				if (GUILayout.Button("Browse", GUILayout.Width(70f)))
				{
					_objectPickerIndex = i;
					_objectPickerId = i + 1000;
					EditorGUIUtility.ShowObjectPicker<Object>(oldAsset, false, string.Empty, _objectPickerId);
				}

				if (GUILayout.Button("Select", GUILayout.Width(60f)))
				{
					if (oldAsset != null)
					{
						Selection.activeObject = oldAsset;
						EditorGUIUtility.PingObject(oldAsset);
					}
				}

				EditorGUILayout.EndHorizontal();
			}
			EditorGUILayout.EndVertical();

			using (new EditorGUILayout.HorizontalScope())
			{
				EditorGUI.BeginDisabledGroup(OriginalAssets == null || OriginalAssets.Count == 0);
				if (GUILayout.Button("-", GUILayout.Width(24f)))
				{
					OriginalAssets.RemoveAt(OriginalAssets.Count - 1);
					ResetDependencyCaches();
				}
				EditorGUI.EndDisabledGroup();

				if (GUILayout.Button("+", GUILayout.Width(24f)))
				{
					EnsureOriginalAssetsList();
					OriginalAssets.Add(null);
					ResetDependencyCaches();
				}

				GUILayout.FlexibleSpace();
			}

			HandleObjectPicker();
		}

		private void DrawDependenciesSection()
		{
			GUILayout.Space(10);
			var gameCount = AssetGameDependencies?.Count ?? 0;
			var gameLabel = $"Game-Related Dependencies ({gameCount})";
			if (gameCount > 0)
			{
				GameDependenciesFoldout = EditorGUILayout.Foldout(GameDependenciesFoldout, gameLabel);
				if (GameDependenciesFoldout)
				{
					foreach (var dep in AssetGameDependencies)
					{
						GUILayout.Label("   * " + dep, _itemDescriptionStyle);
					}
				}
			}
			else
			{
				EditorGUILayout.LabelField(gameLabel, _itemDescriptionStyle);
			}

			var systemCount = AssetSystemDependencies?.Count ?? 0;
			var systemLabel = $"System & Common & Script Dependencies ({systemCount})";
			if (systemCount > 0)
			{
				SystemDependenciesFoldout = EditorGUILayout.Foldout(SystemDependenciesFoldout, systemLabel);
				if (SystemDependenciesFoldout)
				{
					foreach (var dep in AssetSystemDependencies)
					{
						GUILayout.Label("   * " + dep, _itemDescriptionStyle);
					}
				}
			}
			else
			{
				EditorGUILayout.LabelField(systemLabel, _itemDescriptionStyle);
			}
		}

		private void DrawTabsSection()
		{
			GUILayout.Space(12);
			_selectedTab = GUILayout.Toolbar(_selectedTab, Tabs);
			GUILayout.Space(10);

			using (new EditorGUILayout.VerticalScope("box"))
			{
				switch (_selectedTab)
				{
					case 0:
						DrawCopyToTab();
						break;
					case 1:
						DrawReplaceReferencesTab();
						break;
					case 2:
						DrawExportImportTab();
						break;
				}
			}
		}

		private void DrawCopyToTab()
		{
			EditorGUILayout.LabelField("Copy To", _itemTitleStyle);
			using (new EditorGUILayout.HorizontalScope())
			{
				NewAssetPath = EditorGUILayout.ObjectField("Destination", NewAssetPath, typeof(DefaultAsset), false) as DefaultAsset;
				if (GUILayout.Button("Browse", GUILayout.Width(80f)))
				{
					var selected = EditorUtility.OpenFolderPanel("Select Destination Folder", Application.dataPath, string.Empty);
					if (!string.IsNullOrEmpty(selected))
					{
						var assetPath = TryGetAssetPathFromFullPath(selected);
						if (string.IsNullOrEmpty(assetPath))
						{
							Debug.LogWarning("Selected folder must be inside the project Assets folder.");
						}
						else
						{
							NewAssetPath = AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetPath);
						}
					}
				}
			}

			var previewItems = BuildDestinationPreview();
			_previewFoldout = EditorGUILayout.Foldout(_previewFoldout, $"Preview ({previewItems.Count})");
			if (_previewFoldout)
			{
				foreach (var item in previewItems)
				{
					GUILayout.Label("   * " + item, _itemDescriptionStyle);
				}
			}

			GUILayout.Space(16);
			using (new EditorGUILayout.HorizontalScope())
			{
				GUILayout.FlexibleSpace();
				EditorGUI.BeginDisabledGroup(
					OriginalAssets == null || OriginalAssets.Count == 0 || NewAssetPath == null);

				if (GUILayout.Button("Duplicate Asset & Dependencies", GUILayout.Width(220)))
				{
					AssetDatabase.StartAssetEditing();
					try
					{
						DuplicateAsset(OriginalAssets.ToArray(), NewAssetPath);
						Debug.Log($"DONE! New assets at {AssetDatabase.GetAssetPath(NewAssetPath)}/{DUPLICATED_PATH_SUFFIX}");
					}
					catch (OperationCanceledException)
					{
						Debug.Log("Operation canceled.");
					}
					finally
					{
						AssetDatabase.StopAssetEditing();
						EditorUtility.ClearProgressBar();
					}
				}

				EditorGUI.EndDisabledGroup();
				GUILayout.FlexibleSpace();
			}
		}

		private void DrawReplaceReferencesTab()
		{
			var originalAssetPaths = GetOriginalAssetPaths();

			if (AssetGameDependencies != null && AssetGameDependencies.Count > 0)
			{
				GUILayout.Label("Replace References", _itemTitleStyle);

				EditorGUILayout.BeginHorizontal();
				GUILayout.Space(20);

				GUILayout.Label("Original", _itemSubtitleStyle, GUILayout.Width(COLUMN_DEFAULT_SIZE + 50 + 30));
				GUILayout.Label("New", _itemSubtitleStyle, GUILayout.Width(COLUMN_DEFAULT_SIZE));
				GUILayout.Label("Remove Reference?", _itemSubtitleStyle, GUILayout.Width(COLUMN_DEFAULT_SIZE));

				GUILayout.FlexibleSpace();
				EditorGUILayout.EndHorizontal();
				int startOffset = "Packages/".Length;
				string packageName = "";

				StringBuilder contentSb = new StringBuilder();
				foreach (var path in originalAssetPaths)
				{
					if (!string.IsNullOrEmpty(path) && File.Exists(path))
					{
						contentSb.AppendLine(File.ReadAllText(path));
					}
				}

				string originalAssetContent = contentSb.ToString();

				for (int i = 0; i < UniqueAssetGameDependencies.Count; i++)
				{
					string dependency = UniqueAssetGameDependencies[i];

					var name = dependency.Substring(startOffset, dependency.IndexOf("/", startOffset) - startOffset);
					if (packageName != name)
					{
						packageName = name;
						GUILayout.Label($"{packageName.ToUpper()}", _itemSubtitleStyle, GUILayout.Width(300));
					}

					EditorGUILayout.BeginHorizontal();
					GUILayout.Space(20);

					EditorGUI.BeginDisabledGroup(true);
					var original = AssetDatabase.LoadAssetAtPath(dependency, typeof(Object));
					EditorGUILayout.ObjectField(original, original.GetType(), false,
						GUILayout.Width(COLUMN_DEFAULT_SIZE + 50));
					EditorGUI.EndDisabledGroup();
					if (GUILayout.Button("*"))
					{
						Selection.activeObject = original;
						EditorGUIUtility.PingObject(original);
					}

					GUILayout.Label("-->", _itemDescriptionStyle, GUILayout.Width(30));

					bool directDependency =
						originalAssetContent.Contains(AssetDatabase.GUIDFromAssetPath(dependency).ToString());

					if (directDependency)
					{
						_uniqueAssetGameDependenciesReplace[i] = EditorGUILayout.ObjectField(
							_uniqueAssetGameDependenciesReplace[i], original.GetType(), false,
							GUILayout.Width(COLUMN_DEFAULT_SIZE));

						_uniqueAssetGameDependenciesRemove[i] = EditorGUILayout.Toggle("",
							_uniqueAssetGameDependenciesRemove[i], GUILayout.Width(80));
					}
					else
					{
						GUILayout.Label("(Internal dependency)", _itemDescriptionStyle, GUILayout.Width(COLUMN_DEFAULT_SIZE));
					}

					if (_uniqueAssetGameDependenciesRemove[i])
						_uniqueAssetGameDependenciesReplace[i] = null;

					if (directDependency && _uniqueAssetGameDependenciesReplace[i] == null &&
						_uniqueAssetGameDependenciesRemove[i] == false)
						GUILayout.Label("... ignored", _itemDescriptionStyle);

					GUILayout.FlexibleSpace();
					EditorGUILayout.EndHorizontal();
				}
			}

			GUILayout.Space(16);
			GUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			EditorGUI.BeginDisabledGroup(OriginalAssets == null || OriginalAssets.Count < 1 ||
										 AssetGameDependencies == null || AssetGameDependencies.Count < 1 ||
										 _uniqueAssetGameDependenciesReplace == null ||
										 _uniqueAssetGameDependenciesReplace.Length < 1);

			if (GUILayout.Button("Replace References", GUILayout.Width(200)))
			{
				try
				{
					AssetDatabase.StartAssetEditing();

					List<(string originalGuid, string newGuid)> guidTable =
						new List<(string originalGuid, string newGuid)>();

					for (int i = 0; i < AssetGameDependencies.Count; i++)
					{
						if (_uniqueAssetGameDependenciesReplace[i] == null &&
							_uniqueAssetGameDependenciesRemove[i] == false)
							continue;

						string originalGUID = AssetDatabase.GUIDFromAssetPath(AssetGameDependencies[i]).ToString();
						string newGUID = AssetDatabase
							.GUIDFromAssetPath(AssetDatabase.GetAssetPath(_uniqueAssetGameDependenciesReplace[i]))
							.ToString();

						guidTable.Add((originalGUID, newGUID));
					}

					foreach (var asset in OriginalAssets)
					{
						ReplaceReferences(asset, guidTable);
					}

					Debug.Log("DONE!!! All references were replaced");

					UpdateToolScreen();
				}
				catch (OperationCanceledException)
				{
					Debug.Log("The Operation was canceled.");
				}
				finally
				{
					AssetDatabase.StopAssetEditing();

					AssetDatabase.SaveAssets();
					AssetDatabase.Refresh();

					EditorUtility.ClearProgressBar();

					UpdateToolScreen();
				}
			}

			EditorGUI.EndDisabledGroup();

			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		private void DrawExportImportTab()
		{
			EditorGUILayout.LabelField("Export & Import", _itemTitleStyle);
			GUILayout.Space(6);
			GUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();

			EditorGUI.BeginDisabledGroup(
				OriginalAssets == null || OriginalAssets.Count == 0);

			if (GUILayout.Button("Export TGS Package", GUILayout.Width(200)))
			{
				ExportSelectedPackages(OriginalAssets);
			}

			EditorGUI.EndDisabledGroup();
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.Space(10);
			GUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("Import TGS Package", GUILayout.Width(200)))
			{
				var packagePath = EditorUtility.OpenFilePanel(
					$"Select .{ASSET_PACKAGE_EXTENSION} file to import", string.Empty, ASSET_PACKAGE_EXTENSION);
				if (!string.IsNullOrEmpty(packagePath))
				{
					var folderPath = EditorUtility.OpenFolderPanel("Select Destination Folder", Application.dataPath, string.Empty);
					if (!string.IsNullOrEmpty(folderPath))
					{
						ImportPackage(packagePath, folderPath);
					}
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		private void HandleObjectPicker()
		{
			if (_objectPickerId < 0 || _objectPickerIndex < 0)
			{
				return;
			}

			if (Event.current.commandName == "ObjectSelectorUpdated" ||
				Event.current.commandName == "ObjectSelectorClosed")
			{
				if (EditorGUIUtility.GetObjectPickerControlID() == _objectPickerId)
				{
					var picked = EditorGUIUtility.GetObjectPickerObject() as Object;
					if (_objectPickerIndex >= 0 && _objectPickerIndex < OriginalAssets.Count)
					{
						OriginalAssets[_objectPickerIndex] = picked;
						ResetDependencyCaches();
					}
				}

				if (Event.current.commandName == "ObjectSelectorClosed")
				{
					_objectPickerId = -1;
					_objectPickerIndex = -1;
				}
			}
		}

		private void EnsureOriginalAssetsList()
		{
			if (OriginalAssets == null)
			{
				OriginalAssets = new List<Object>();
			}
		}

		private static string[] GetOriginalAssetPaths()
		{
			if (OriginalAssets == null || OriginalAssets.Count == 0)
			{
				return Array.Empty<string>();
			}

			var originalAssetPaths = new string[OriginalAssets.Count];
			for (int i = 0; i < OriginalAssets.Count; i++)
			{
				originalAssetPaths[i] = AssetDatabase.GetAssetPath(OriginalAssets[i]);
			}

			return originalAssetPaths;
		}

		private List<string> BuildDestinationPreview()
		{
			var previewItems = new List<string>();
			if (NewAssetPath == null || OriginalAssets == null || OriginalAssets.Count == 0)
			{
				return previewItems;
			}

			var destinationPath = AssetDatabase.GetAssetPath(NewAssetPath);
			if (string.IsNullOrEmpty(destinationPath))
			{
				return previewItems;
			}

			var basePath = Path.Combine(destinationPath, DUPLICATED_PATH_SUFFIX).Replace("\\", "/");
			foreach (var asset in OriginalAssets)
			{
				if (asset == null)
				{
					continue;
				}

				var sourcePath = AssetDatabase.GetAssetPath(asset);
				if (string.IsNullOrEmpty(sourcePath))
				{
					continue;
				}

				var fileName = Path.GetFileName(sourcePath);
				if (!string.IsNullOrEmpty(fileName))
				{
					previewItems.Add(basePath + "/" + fileName);
				}
			}

			foreach (var dependency in AssetGameDependencies)
			{
				var fileName = Path.GetFileName(dependency);
				if (!string.IsNullOrEmpty(fileName))
				{
					previewItems.Add(Path.Combine(basePath, DEPENDENCIES_PATH, fileName).Replace("\\", "/"));
				}
			}

			return previewItems.Distinct().ToList();
		}

		private static string TryGetAssetPathFromFullPath(string fullPath)
		{
			if (string.IsNullOrEmpty(fullPath))
			{
				return null;
			}

			var projectAssetsPath = Path.GetFullPath(Application.dataPath);
			var normalized = Path.GetFullPath(fullPath);
			if (!normalized.StartsWith(projectAssetsPath, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			var relative = "Assets" + normalized.Substring(projectAssetsPath.Length);
			return relative.Replace("\\", "/");
		}

		private static void ResetDependencyCaches()
		{
			_assetGameDependencies = null;
			_assetSystemDependencies = null;
			_uniqueAssetGameDependencies = null;
			_uniqueAssetGameDependenciesReplace = null;
		}

		// ========================== DUPLICATE ASSET ============================


		private static void DuplicateAsset(Object[] assets, DefaultAsset destination)
		{
			var paths = assets.Select(a => AssetDatabase.GetAssetPath(a)).ToArray();
			string destPath = AssetDatabase.GetAssetPath(destination);
			CopyAssetDeep(paths, destPath);
			AssetDatabase.ImportAsset(Path.Combine(destPath, DUPLICATED_PATH_SUFFIX), ImportAssetOptions.ImportRecursive);
		}

		private static void CopyAssetDeep(string[] originalAssetsPath, string newAssetPath)
		{
			newAssetPath = Path.Combine(newAssetPath, DUPLICATED_PATH_SUFFIX);
			CopyAssetsAndDependencies(originalAssetsPath, newAssetPath);
			// .meta handling
			var metaFiles = GetFilesRecursively(newAssetPath, f => f.EndsWith(".meta"));
			var guidTable = new List<(string originalGuid, string newGuid)>();
			try
			{
				for (int i = 0; i < metaFiles.Count; i++)
				{
					if (EditorUtility.DisplayCancelableProgressBar("Duplicating...", "Processing .meta files",
							(float)i / metaFiles.Count)) throw new OperationCanceledException();
					var lines = File.ReadAllLines(metaFiles[i]);
					if (lines.Length > 1)
					{
						var orig = lines[1].Substring(6);
						string newGuid = GUID.Generate().ToString().Replace("-", "");
						guidTable.Add((orig, newGuid));
					}
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			var allFiles = GetFilesRecursively(newAssetPath);
			try
			{
				for (int i = 0; i < allFiles.Count; i++)
				{
					if (IgnoreFileFormat(allFiles[i])) continue;
					if (EditorUtility.DisplayCancelableProgressBar("Duplicating...",
							$"Replacing GUID in {Path.GetFileName(allFiles[i])}", (float)i / allFiles.Count))
						throw new OperationCanceledException();
					var content = File.ReadAllText(allFiles[i]);
					foreach (var (orig, neu) in guidTable) content = content.Replace(orig, neu);
					File.WriteAllText(allFiles[i], content);
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}

		private static void CopyAssetsAndDependencies(string[] sourceAssetPaths, string destAssetPath)
		{
			if (!Directory.Exists(destAssetPath)) Directory.CreateDirectory(destAssetPath);
			try
			{
				for (int i = 0; i < sourceAssetPaths.Length; i++)
				{
					var file = new FileInfo(sourceAssetPaths[i]);
					if (EditorUtility.DisplayCancelableProgressBar("Duplicating...", "Copying " + file.Name,
							(float)i / sourceAssetPaths.Length)) throw new OperationCanceledException();
					var dst = Path.Combine(destAssetPath, file.Name);
					if (!File.Exists(dst)) file.CopyTo(dst, false);
					var meta = new FileInfo(sourceAssetPaths[i] + ".meta");
					if (meta.Exists && !File.Exists(dst + ".meta")) meta.CopyTo(dst + ".meta", false);
				}

				var deps = GetGameAssetDependencies(sourceAssetPaths);
				if (deps.Length > 0)
				{
					var dp = Path.Combine(destAssetPath, DEPENDENCIES_PATH);
					if (!Directory.Exists(dp)) Directory.CreateDirectory(dp);
					for (int i = 0; i < deps.Length; i++)
					{
						var file = new FileInfo(deps[i]);
						if (EditorUtility.DisplayCancelableProgressBar("Duplicating...", "Copying " + file.Name,
								(float)i / deps.Length)) throw new OperationCanceledException();
						var dst = Path.Combine(dp, file.Name);
						if (!File.Exists(dst)) file.CopyTo(dst, false);
						var meta = new FileInfo(deps[i] + ".meta");
						if (meta.Exists && !File.Exists(dst + ".meta")) meta.CopyTo(dst + ".meta", false);
					}
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
		}

		// ========================== REPLACE REFERENCES ============================

		private void UpdateToolScreen()
		{
			// Gimmick to reload the window
			var tmpAsset = OriginalAssets;
			OriginalAssets = null;
			OriginalAssets = tmpAsset;

			_assetGameDependencies = null;
		}

		private static void ReplaceReferences(Object originalAsset, List<(string originalGUID, string newGUID)> guidTable)
		{
			List<string> assetsToReplace = new List<string>();
			assetsToReplace.Add(AssetDatabase.GetAssetPath(originalAsset));

			// Look for .meta file
			if (File.Exists(assetsToReplace[0] + ".meta"))
				assetsToReplace.Add(assetsToReplace[0] + ".meta");

			StringBuilder builder = null;

			try
			{
				builder = new StringBuilder();
				builder.AppendLine("Started replacing GUID references");

				foreach (string asset in assetsToReplace)
				{
					// Ignore file formats -- Usually binary files
					if (IgnoreFileFormat(asset))
						continue;

					builder.Append(Environment.NewLine);
					builder.AppendLine(string.Format("> {0}: ", asset));

					for (int i = 0; i < guidTable.Count; i++)
					{
						if (EditorUtility.DisplayCancelableProgressBar("Replacing References!!!",
								"Replacing GUID references: " + Path.GetFileName(asset), (float)(i) / guidTable.Count))
						{
							EditorUtility.ClearProgressBar();
							throw new OperationCanceledException("The operation was canceled");
						}

						string content = File.ReadAllText(asset);
						if (content.Contains(guidTable[i].originalGUID))
						{
							builder.AppendLine(string.Format("   * {0} -> {1}", guidTable[i].originalGUID,
								guidTable[i].newGUID));

							// Trying to remove a reference
							// Regex Format: {fileID: -6152337235465821566, guid: cf8006376f04ba64bbac2cd54ae8a4cd, type 3}
							// (.*?\n?.*?) -->  non-greedy line break
							string regexMatch = @"\{fileID.*" + guidTable[i].originalGUID + @"(.*?\n?.*?)\}";
							if (guidTable[i].newGUID == EMPTY_GUID && Regex.Match(content, regexMatch).Success)
							{
								content = Regex.Replace(content, regexMatch, "");
							}
							else
							{
								content = content.Replace(guidTable[i].originalGUID, guidTable[i].newGUID);
							}

							File.WriteAllText(asset, content);
						}
					}
				}
			}
			finally
			{
				if (builder != null)
					Debug.Log(builder.ToString());
				EditorUtility.ClearProgressBar();
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}

		// ========================== EXPORTER ============================

		public static void ExportSelectedPackages(List<Object> assets)
		{
			string selectedFilePath = EditorUtility.SaveFilePanel("Select Exported UnityPackage Location", "Assets",
				"ExportedPackage", ASSET_PACKAGE_EXTENSION);
			string selectedPath = Path.GetDirectoryName(selectedFilePath);

			if (string.IsNullOrEmpty(selectedPath) || !Directory.Exists(selectedPath))
			{
				Debug.LogError("Invalid Destination folder");
				return;
			}

			AssetDatabase.StartAssetEditing();
			var paths = assets.Select(a => AssetDatabase.GetAssetPath(a)).ToArray();
			var selectedPathInner = Path.Combine(selectedPath, EXPORTED_PACKAGE_SUFFIX);
			try
			{
				if (!Directory.Exists(selectedPathInner))
					Directory.CreateDirectory(selectedPathInner);

				CopyAssetDeep(paths, selectedPathInner);
				using (var zipToOpen = new FileStream(selectedFilePath, FileMode.Create))
				using (var archive =
					   new System.IO.Compression.ZipArchive(zipToOpen, System.IO.Compression.ZipArchiveMode.Create))
				{
					var zipRootPath = Path.Combine(selectedPathInner, DUPLICATED_PATH_SUFFIX);
					foreach (var filePath in Directory.GetFiles(zipRootPath, "*", SearchOption.AllDirectories))
					{
						var entryName = filePath.Substring(zipRootPath.Length)
							.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
						var entry = archive.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
						using (var entryStream = entry.Open())
						using (var fileStream = File.OpenRead(filePath))
						{
							fileStream.CopyTo(entryStream);
						}
					}
				}

				// AssetDatabase.ExportPackage(paths, Path.Combine(selectedPath, $"{selectedFilePath}"), ExportPackageOptions.Interactive | ExportPackageOptions.Recurse);
				Debug.Log($"DONE! Exported package at {Path.Combine(selectedPath, $"{selectedFilePath}")}");
			}
			catch (OperationCanceledException)
			{
				Debug.Log("Operation canceled.");
			}
			finally
			{
				if (Directory.Exists(selectedPathInner))
				{
					Debug.Log($"Removing tmp folder: {selectedPathInner}");
					Directory.Delete(selectedPathInner, true);
				}

				AssetDatabase.StopAssetEditing();
				EditorUtility.ClearProgressBar();
			}
		}

		// ========================== IMPORTER ============================

		public static void ImportPackage(string packagePath, string destinationPath)
		{
			Debug.Log($"Selected destination folder: {destinationPath}");
			
			// Extract to a temp folder first to avoid Unity auto-import
			var tempExtractPath =
				Path.Combine(destinationPath, $"tgspackage_tmp_extract_{DateTime.Now:yyyyMMdd_HHmmssfff}");
			try
			{
				AssetDatabase.StartAssetEditing();
				
				ExtractZip(packagePath, tempExtractPath);
				var packageFiles = Directory
					.GetFiles(tempExtractPath, "*", SearchOption.TopDirectoryOnly)
					.Where(f => !f.EndsWith(".meta"))
					.ToArray();
				
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				AssetDatabase.StopAssetEditing();
				
				AssetDatabase.StartAssetEditing();
				
				CopyAssetDeep(packageFiles, destinationPath);
				AssetDatabase.ImportAsset(Path.Combine(destinationPath, DUPLICATED_PATH_SUFFIX), ImportAssetOptions.ImportRecursive);
			}   
			finally
			{
				if (Directory.Exists(tempExtractPath))
				{
					Directory.Delete(tempExtractPath, true);
					var meta = tempExtractPath + ".meta";
					if (File.Exists((meta))) File.Delete(meta);
				}
				
				AssetDatabase.StopAssetEditing();
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}
			
			
			AssetDatabase.Refresh();
		}

		// ========================== HELPER METHODS ============================

		// Original helper methods below unchanged
		private static bool IgnoreFileFormat(string filePath)
		{
			if (filePath.EndsWith(".png") || filePath.EndsWith(".jpg") || filePath.EndsWith(".hdr")) return true;
			if (filePath.EndsWith(".wav") || filePath.EndsWith(".mp3") || filePath.EndsWith(".ogg") ||
				filePath.EndsWith(".aif")) return true;
			if (filePath.EndsWith(".mp4") || filePath.EndsWith(".gp8") || filePath.EndsWith(".mpeg") ||
				filePath.EndsWith(".avi") || filePath.EndsWith(".webm") || filePath.EndsWith(".asf")) return true;
			if (filePath.EndsWith(".ttf") || filePath.EndsWith(".otf")) return true;
			if (filePath.EndsWith(".fbx")) return true;
			return false;
		}

		private static bool IsSystemOrCommon(string dependency) => !dependency.Contains(DEPENDENCIES_BUNDLES_PATH) ||
																   DEPENDENCIES_COMMON_BUNDLE_PATHS.Any(common =>
																	   dependency.Contains(common));

		private static string[] GetSystemAssetDependencies(params string[] assets)
		{
			var deps = AssetDatabase.GetDependencies(assets, false).Where(d => IsSystemOrCommon(d)).ToList();
			deps.Sort();
			return deps.ToArray();
		}

		private static string[] GetGameAssetDependencies(params string[] assets)
		{
			assets = assets.Select(a => a.Replace("\\", "/")).ToArray();
			List<string> dependencies = AssetDatabase.GetDependencies(assets, true).ToList();

			// Remove original assets from dependencies    ||    Remove dependencies outside Bundles folder
			dependencies.RemoveAll(d => (Array.IndexOf(assets, d) > -1) || IsSystemOrCommon(d));
			dependencies.Sort();

			return dependencies.ToArray();
		}

		private static List<string> GetFilesRecursively(string path, Func<string, bool> criteria = null,
			List<string> files = null)
		{
			files ??= new List<string>();
			files.AddRange(Directory.GetFiles(path).Where(f => criteria == null || criteria(f)));
			foreach (var dir in Directory.GetDirectories(path)) GetFilesRecursively(dir, criteria, files);
			return files;
		}
		
		public static void ExtractZip(string packagePath, string destinationPath)
		{
			using (var archive = new ZipArchive(File.OpenRead(packagePath), ZipArchiveMode.Read))
			{
				foreach (var entry in archive.Entries)
				{
					// Construct full path
					var entryPath = Path.Combine(destinationPath, entry.FullName);
					// Skip directory entries
					if (string.IsNullOrEmpty(entry.Name))
					{
						Directory.CreateDirectory(entryPath);
						continue;
					}
					// Ensure folder exists
					Directory.CreateDirectory(Path.GetDirectoryName(entryPath));
					// Extract file
					using (var inStream = entry.Open())
					using (var outStream = File.Create(entryPath))
						inStream.CopyTo(outStream);
				}
			}
		}
	}
}
