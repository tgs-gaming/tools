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
		private string _duplicateSubFolder = "_Duplicated_Assets";
		private bool _keepFolderStructure = false;
		private string _dependenciesFolder = DEPENDENCIES_PATH;
		private string _externalDestinationPath;
		private int _objectPickerId = -1;
		private int _objectPickerIndex = -1;
		private string[] _cachedDirectDepsInput;
		private HashSet<string> _cachedDirectDeps;
		private string[] _cachedCodeRefsInput;
		private CodeReferencesInfo _cachedCodeRefs;
		private bool _codeReferencesFoldout = true;
		private readonly Dictionary<string, string> _namespaceRenames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, string> _asmdefFileRenames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, string> _rootNamespaceRenames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, string> _asmdefNameRenames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private static readonly string[] Tabs = { "Copy To", "Replace References", "Export & Import" };

		private static List<Object> _originalAssets;

		public static List<Object> OriginalAssets
		{
			get => _originalAssets;
			set
			{
				_originalAssets = value;
				_assetGameDependencies = null;
				_assetTGSPackageDependencies = null;
				_assetSystemDependencies = null;
				_assetReplaceDependencies = null;
				_uniqueAssetReplaceDependencies = null;
				_uniqueAssetReplaceDependenciesReplace = null;
				_uniqueAssetReplaceDependenciesRemove = null;
			}
		}

		public static void OpenWithImportDefaults(string packageRoot, string destinationPath, string duplicateSubFolder)
		{
			var window = GetWindow<DependencyManager>();
			window.titleContent = new GUIContent("Asset Dependency Manager");
			window._selectedTab = 0;
			window.ApplyImportDefaults(packageRoot, destinationPath, duplicateSubFolder);
			window.Show();
		}

		public static DefaultAsset NewAssetPath;
		public static bool GameDependenciesFoldout;
		public static bool TGSPackageDependenciesFoldout;
		public static bool SystemDependenciesFoldout;
		public static bool NewGameDependenciesFoldout;

		private static List<string> _assetGameDependencies;
		private static List<string> _assetTGSPackageDependencies;

		public static List<string> AssetGameDependencies
		{
			get
			{
				if (OriginalAssets == null || OriginalAssets.Count == 0)
					return new List<string>();
				if (_assetGameDependencies == null)
				{
					var paths = ResolveSelectedAssetPaths(OriginalAssets).ToArray();
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

		public static List<string> AssetTGSPackageDependencies
		{
			get
			{
				if (OriginalAssets == null || OriginalAssets.Count == 0)
					return new List<string>();
				if (_assetTGSPackageDependencies == null)
				{
					var paths = ResolveSelectedAssetPaths(OriginalAssets).ToArray();
					_assetTGSPackageDependencies = GetTGSPackageDependencies(paths).Distinct().ToList();

					foreach (var path in paths)
					{
						if (_assetTGSPackageDependencies != null)
							_assetTGSPackageDependencies = _assetTGSPackageDependencies.Where(dep => dep != path).ToList();
					}
				}

				return _assetTGSPackageDependencies;
			}
		}

		private static Object[] _uniqueAssetReplaceDependenciesReplace;
		private static bool[] _uniqueAssetReplaceDependenciesRemove;

		private static List<string> _assetReplaceDependencies;
		private static List<string> _uniqueAssetReplaceDependencies;

		public static List<string> AssetReplaceDependencies
		{
			get
			{
				if (_assetReplaceDependencies == null)
				{
					_assetReplaceDependencies = AssetGameDependencies
						.Concat(AssetTGSPackageDependencies)
						.Distinct(StringComparer.OrdinalIgnoreCase)
						.ToList();
				}

				return _assetReplaceDependencies;
			}
		}

		public static List<string> UniqueAssetReplaceDependencies
		{
			get
			{
				if (AssetReplaceDependencies == null)
				{
					_uniqueAssetReplaceDependencies = null;
					_uniqueAssetReplaceDependenciesReplace = null;
					_uniqueAssetReplaceDependenciesRemove = null;
				}
				else if (_uniqueAssetReplaceDependencies == null)
				{
					_uniqueAssetReplaceDependencies = AssetReplaceDependencies.Distinct().ToList();
					_uniqueAssetReplaceDependenciesReplace = new Object[_uniqueAssetReplaceDependencies.Count];
					_uniqueAssetReplaceDependenciesRemove = new bool[_uniqueAssetReplaceDependencies.Count];
				}

				return _uniqueAssetReplaceDependencies;
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
					var paths = ResolveSelectedAssetPaths(OriginalAssets).ToArray();
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

		private void OnEnable()
		{
			EditorApplication.projectChanged += OnProjectChanged;
		}

		private void OnDisable()
		{
			EditorApplication.projectChanged -= OnProjectChanged;
		}

		private void OnProjectChanged()
		{
			ResetDependencyCaches();
			ResetDirectDependencyCache();
			ResetCodeReferencesCache();
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

		private void ApplyImportDefaults(string packageRoot, string destinationPath, string duplicateSubFolder)
		{
			var assetPath = NormalizeAssetPath(packageRoot);
			var selectedAsset = string.IsNullOrEmpty(assetPath)
				? null
				: AssetDatabase.LoadAssetAtPath<Object>(assetPath);
			OriginalAssets = new List<Object> { selectedAsset };
			ResetDependencyCaches();
			ResetDirectDependencyCache();
			ResetCodeReferencesCache();

			NewAssetPath = null;
			_externalDestinationPath = null;
			if (!string.IsNullOrEmpty(destinationPath))
			{
				var destinationAssetPath = NormalizeAssetPath(destinationPath);
				if (!string.IsNullOrEmpty(destinationAssetPath))
				{
					NewAssetPath = AssetDatabase.LoadAssetAtPath<DefaultAsset>(destinationAssetPath);
				}
				else
				{
					_externalDestinationPath = destinationPath;
				}
			}

			if (!string.IsNullOrWhiteSpace(duplicateSubFolder))
			{
				_duplicateSubFolder = duplicateSubFolder.Trim();
			}
		}

		private static string NormalizeAssetPath(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}

			if (path.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
			{
				return path.Replace("\\", "/");
			}

			return TryGetAssetPathFromFullPath(path);
		}

		private string GetDestinationPath()
		{
			if (!string.IsNullOrEmpty(_externalDestinationPath))
			{
				return _externalDestinationPath;
			}

			return NewAssetPath == null ? null : AssetDatabase.GetAssetPath(NewAssetPath);
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
					ResetDirectDependencyCache();
					ResetCodeReferencesCache();
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
					EnsureOriginalAssetsList();
					ResetDependencyCaches();
					ResetDirectDependencyCache();
					ResetCodeReferencesCache();
				}
				EditorGUI.EndDisabledGroup();

				if (GUILayout.Button("+", GUILayout.Width(24f)))
				{
					EnsureOriginalAssetsList();
					OriginalAssets.Add(null);
					ResetDependencyCaches();
					ResetDirectDependencyCache();
					ResetCodeReferencesCache();
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

			var tgsPackageCount = AssetTGSPackageDependencies?.Count ?? 0;
			var tgsPackageLabel = $"TGS Package Dependencies ({tgsPackageCount})";
			if (tgsPackageCount > 0)
			{
				TGSPackageDependenciesFoldout = EditorGUILayout.Foldout(TGSPackageDependenciesFoldout, tgsPackageLabel);
				if (TGSPackageDependenciesFoldout)
				{
					foreach (var dep in AssetTGSPackageDependencies)
					{
						GUILayout.Label("   * " + dep, _itemDescriptionStyle);
					}
				}
			}
			else
			{
				EditorGUILayout.LabelField(tgsPackageLabel, _itemDescriptionStyle);
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
				if (string.IsNullOrEmpty(_externalDestinationPath))
				{
					var newDestination = EditorGUILayout.ObjectField("Destination", NewAssetPath, typeof(DefaultAsset), false) as DefaultAsset;
					if (newDestination != NewAssetPath)
					{
						NewAssetPath = newDestination;
						if (NewAssetPath != null)
						{
							_externalDestinationPath = null;
						}
					}
				}
				else
				{
					EditorGUI.BeginDisabledGroup(true);
					EditorGUILayout.TextField("Destination", _externalDestinationPath);
					EditorGUI.EndDisabledGroup();
				}

				if (GUILayout.Button("Browse", GUILayout.Width(80f)))
				{
					var selected = EditorUtility.OpenFolderPanel("Select Destination Folder", Application.dataPath, string.Empty);
					if (!string.IsNullOrEmpty(selected))
					{
						var assetPath = TryGetAssetPathFromFullPath(selected);
						if (string.IsNullOrEmpty(assetPath))
						{
							NewAssetPath = null;
							_externalDestinationPath = selected;
						}
						else
						{
							NewAssetPath = AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetPath);
							_externalDestinationPath = null;
						}
					}
				}
			}

			_duplicateSubFolder = EditorGUILayout.TextField("SubFolder", _duplicateSubFolder);
			_keepFolderStructure = EditorGUILayout.Toggle("Keep folder Structure", _keepFolderStructure);
			_dependenciesFolder = EditorGUILayout.TextField("Dependencies Folder", _dependenciesFolder);

			var codeReferencesComplete = DrawCodeReferencesSection();

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
					OriginalAssets == null || OriginalAssets.Count == 0 || string.IsNullOrEmpty(GetDestinationPath()) || !codeReferencesComplete);

				if (GUILayout.Button("Duplicate Asset & Dependencies", GUILayout.Width(220)))
				{
					AssetDatabase.StartAssetEditing();
					try
					{
						var destinationPath = GetDestinationPath();
						DuplicateAsset(OriginalAssets.ToArray(), destinationPath, GetDuplicateSubFolder(),
							GetDependenciesFolder(), _keepFolderStructure);
						var resolvedDestination = BuildDestinationBasePath(
							destinationPath,
							GetDuplicateSubFolder());
						Debug.Log($"DONE! New assets at {resolvedDestination}");
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
			var directDependencyPaths = GetDirectDependencyPathsCached(originalAssetPaths);

			if (AssetReplaceDependencies != null && AssetReplaceDependencies.Count > 0)
			{
				GUILayout.Label("Replace References", _itemTitleStyle);

				EditorGUILayout.BeginHorizontal();
				GUILayout.Space(20);

				GUILayout.Label("Original", _itemSubtitleStyle, GUILayout.Width(COLUMN_DEFAULT_SIZE + 50 + 30));
				GUILayout.Label("New", _itemSubtitleStyle, GUILayout.Width(COLUMN_DEFAULT_SIZE));
				GUILayout.Label("Remove Reference?", _itemSubtitleStyle, GUILayout.Width(COLUMN_DEFAULT_SIZE));

				GUILayout.FlexibleSpace();
				EditorGUILayout.EndHorizontal();
				string packageName = "";

				for (int i = 0; i < UniqueAssetReplaceDependencies.Count; i++)
				{
					string dependency = UniqueAssetReplaceDependencies[i];

					var name = GetDependencyGroupLabel(dependency);
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

					bool directDependency = directDependencyPaths.Contains(dependency);

					if (directDependency)
					{
						_uniqueAssetReplaceDependenciesReplace[i] = EditorGUILayout.ObjectField(
							_uniqueAssetReplaceDependenciesReplace[i], original.GetType(), false,
							GUILayout.Width(COLUMN_DEFAULT_SIZE));

						_uniqueAssetReplaceDependenciesRemove[i] = EditorGUILayout.Toggle("",
							_uniqueAssetReplaceDependenciesRemove[i], GUILayout.Width(80));
					}
					else
					{
						GUILayout.Label("(Internal dependency)", _itemDescriptionStyle, GUILayout.Width(COLUMN_DEFAULT_SIZE));
					}

					if (_uniqueAssetReplaceDependenciesRemove[i])
						_uniqueAssetReplaceDependenciesReplace[i] = null;

					if (directDependency && _uniqueAssetReplaceDependenciesReplace[i] == null &&
						_uniqueAssetReplaceDependenciesRemove[i] == false)
						GUILayout.Label("... ignored", _itemDescriptionStyle);

					GUILayout.FlexibleSpace();
					EditorGUILayout.EndHorizontal();
				}
			}

			GUILayout.Space(16);
			GUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			EditorGUI.BeginDisabledGroup(OriginalAssets == null || OriginalAssets.Count < 1 ||
										 AssetReplaceDependencies == null || AssetReplaceDependencies.Count < 1 ||
										 _uniqueAssetReplaceDependenciesReplace == null ||
										 _uniqueAssetReplaceDependenciesReplace.Length < 1);

			if (GUILayout.Button("Replace References", GUILayout.Width(200)))
			{
				try
				{
					AssetDatabase.StartAssetEditing();

					List<(string originalGuid, string newGuid)> guidTable =
						new List<(string originalGuid, string newGuid)>();

					for (int i = 0; i < AssetReplaceDependencies.Count; i++)
					{
						if (_uniqueAssetReplaceDependenciesReplace[i] == null &&
							_uniqueAssetReplaceDependenciesRemove[i] == false)
							continue;

						string originalGUID = AssetDatabase.GUIDFromAssetPath(AssetReplaceDependencies[i]).ToString();
						string newGUID = _uniqueAssetReplaceDependenciesRemove[i]
							? EMPTY_GUID
							: AssetDatabase
								.GUIDFromAssetPath(AssetDatabase.GetAssetPath(_uniqueAssetReplaceDependenciesReplace[i]))
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

		private bool DrawCodeReferencesSection()
		{
			var codeRefs = GetCodeReferencesCached();
			if (codeRefs == null || !codeRefs.HasAny)
			{
				return true;
			}

			GUILayout.Space(8);
			using (new EditorGUILayout.VerticalScope("box"))
			{
				_codeReferencesFoldout = EditorGUILayout.Foldout(_codeReferencesFoldout, "Code References", true);
				if (_codeReferencesFoldout)
				{
					using (new EditorGUILayout.HorizontalScope())
					{
						GUILayout.Label("Current", _itemSubtitleStyle, GUILayout.MinWidth(200f));
						GUILayout.Label("-->", _itemDescriptionStyle, GUILayout.Width(24f));
						GUILayout.Label("Rename To", _itemSubtitleStyle);
					}

					DrawPatternRenameSection("Namespaces", codeRefs.NamespacePatterns, _namespaceRenames);
					DrawPatternRenameSection("Assembly Definition filenames", codeRefs.AsmdefFilePatterns, _asmdefFileRenames);
					DrawPatternRenameSection("Root Namespaces", codeRefs.RootNamespacePatterns, _rootNamespaceRenames);
					DrawPatternRenameSection("Asmdef Names", codeRefs.AsmdefNamePatterns, _asmdefNameRenames);
				}
			}

			return AreCodeReferenceRenamesComplete(codeRefs);
		}

		private void DrawPatternRenameSection(string title, List<string> patterns, Dictionary<string, string> renames)
		{
			if (patterns == null || patterns.Count == 0)
			{
				return;
			}

			GUILayout.Space(4);
			EditorGUILayout.LabelField(title, _itemSubtitleStyle);
			foreach (var pattern in patterns)
			{
				EditorGUILayout.BeginHorizontal();
				GUILayout.Label(pattern, GUILayout.MinWidth(200f));
				GUILayout.Label("-->", _itemDescriptionStyle, GUILayout.Width(24f));
				if (!renames.TryGetValue(pattern, out var newValue) || string.IsNullOrEmpty(newValue))
				{
					newValue = pattern;
					renames[pattern] = newValue;
				}

				newValue = EditorGUILayout.TextField(newValue ?? string.Empty);
				renames[pattern] = newValue;
				EditorGUILayout.EndHorizontal();
			}
		}

		private void DrawExportImportTab()
		{
			EditorGUILayout.LabelField("Export & Import", _itemTitleStyle);
			var codeReferencesComplete = DrawCodeReferencesSection();
			GUILayout.Space(6);
			GUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();

			EditorGUI.BeginDisabledGroup(
				OriginalAssets == null || OriginalAssets.Count == 0 || !codeReferencesComplete);

			if (GUILayout.Button("Export TGS Package", GUILayout.Width(200)))
			{
				ExportSelectedPackages(OriginalAssets, GetDuplicateSubFolder(), GetDependenciesFolder(), _keepFolderStructure);
			}

			EditorGUI.EndDisabledGroup();
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.Space(10);
			GUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			EditorGUI.BeginDisabledGroup(!codeReferencesComplete);
			if (GUILayout.Button("Import TGS Package", GUILayout.Width(200)))
			{
				var packagePath = EditorUtility.OpenFilePanel(
					$"Select .{ASSET_PACKAGE_EXTENSION} file to import", string.Empty, ASSET_PACKAGE_EXTENSION);
				if (!string.IsNullOrEmpty(packagePath))
				{
					var folderPath = EditorUtility.OpenFolderPanel("Select Destination Folder", Application.dataPath, string.Empty);
					if (!string.IsNullOrEmpty(folderPath))
					{
						ImportPackage(packagePath, folderPath, GetDuplicateSubFolder());
					}
				}
			}
			EditorGUI.EndDisabledGroup();
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
						ResetDirectDependencyCache();
						ResetCodeReferencesCache();
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

			if (OriginalAssets.Count == 0)
			{
				OriginalAssets.Add(null);
			}
		}

		private static string[] GetOriginalAssetPaths()
		{
			return ResolveSelectedAssetPaths(OriginalAssets).ToArray();
		}

		private HashSet<string> GetDirectDependencyPathsCached(string[] originalAssetPaths)
		{
			if (originalAssetPaths == null || originalAssetPaths.Length == 0)
			{
				_cachedDirectDepsInput = Array.Empty<string>();
				_cachedDirectDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				return _cachedDirectDeps;
			}

			var normalized = originalAssetPaths.Where(path => !string.IsNullOrEmpty(path))
				.Select(path => path.Replace("\\", "/"))
				.OrderBy(path => path)
				.ToArray();

			if (_cachedDirectDepsInput != null &&
				_cachedDirectDeps != null &&
				normalized.SequenceEqual(_cachedDirectDepsInput))
			{
				return _cachedDirectDeps;
			}

			_cachedDirectDepsInput = normalized;
			_cachedDirectDeps = GetDirectDependencyPaths(normalized);
			return _cachedDirectDeps;
		}

		private static HashSet<string> GetDirectDependencyPaths(string[] originalAssetPaths)
		{
			if (originalAssetPaths == null || originalAssetPaths.Length == 0)
			{
				return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}

			var deps = AssetDatabase.GetDependencies(originalAssetPaths, false);
			return new HashSet<string>(deps, StringComparer.OrdinalIgnoreCase);
		}

		private List<string> BuildDestinationPreview()
		{
			var previewItems = new List<string>();
			if (OriginalAssets == null || OriginalAssets.Count == 0)
			{
				return previewItems;
			}

			var destinationPath = GetDestinationPath();
			if (string.IsNullOrEmpty(destinationPath))
			{
				return previewItems;
			}

			var basePath = BuildDestinationBasePath(destinationPath);
			var dependenciesBasePath = BuildDependenciesBasePath(basePath, GetDependenciesFolder());
			BuildSelectionPlan(OriginalAssets?.ToArray(), out var folderRoots, out var fileRoots, out var selectedFiles,
				out var selectedFileAnchorRoots);
			var rootPaths = folderRoots.Concat(fileRoots).ToList();
			var dependencyPaths = AssetGameDependencies
				.Concat(AssetTGSPackageDependencies)
				.Where(dep => !selectedFiles.Contains(dep))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			Dictionary<string, string> selectedFileRoots = null;
			Dictionary<string, string> dependencyRoots = null;
			if (_keepFolderStructure)
			{
				BuildPerAssetStructureRoots(selectedFiles, dependencyPaths, selectedFileAnchorRoots,
					out selectedFileRoots, out dependencyRoots);
			}

			if (_keepFolderStructure)
			{
				foreach (var selectedFile in selectedFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
				{
					if (!selectedFileRoots.TryGetValue(selectedFile, out var structureRoot))
					{
						if (!selectedFileAnchorRoots.TryGetValue(selectedFile, out structureRoot))
						{
							structureRoot = Path.GetDirectoryName(selectedFile)?.Replace("\\", "/") ?? string.Empty;
						}
					}

					var relativePath = GetRelativePathFromStructureRoot(selectedFile, structureRoot);
					if (string.IsNullOrEmpty(relativePath))
					{
						relativePath = Path.GetFileName(selectedFile);
					}

					if (!string.IsNullOrEmpty(relativePath))
					{
						var renamedRelative = RenameAsmdefFilenameInPath(relativePath);
						previewItems.Add(Path.Combine(basePath, renamedRelative).Replace("\\", "/"));
					}
				}

				var dependencyPreviewItems = new List<(string RelativePath, string PreviewPath)>();
				foreach (var dependency in dependencyPaths)
				{
					if (!dependencyRoots.TryGetValue(dependency, out var structureRoot))
					{
						structureRoot = string.Empty;
					}

					var relativePath = GetRelativePathFromStructureRoot(dependency, structureRoot);
					if (string.IsNullOrEmpty(relativePath))
					{
						relativePath = GetDependencyRelativePath(dependency, rootPaths);
					}

					if (!string.IsNullOrEmpty(relativePath))
					{
						var renamedRelativePath = RenameAsmdefFilenameInPath(relativePath);
						dependencyPreviewItems.Add((
							renamedRelativePath.Replace("\\", "/"),
							Path.Combine(dependenciesBasePath, renamedRelativePath).Replace("\\", "/")));
					}
				}

				foreach (var dependencyItem in dependencyPreviewItems
					.OrderBy(item => Path.GetDirectoryName(item.RelativePath)?.Replace("\\", "/") ?? string.Empty,
						StringComparer.OrdinalIgnoreCase)
					.ThenBy(item => Path.GetFileName(item.RelativePath), StringComparer.OrdinalIgnoreCase)
					.ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
				{
					previewItems.Add(dependencyItem.PreviewPath);
				}
			}
			else
			{
				foreach (var rootPath in rootPaths)
				{
					if (AssetDatabase.IsValidFolder(rootPath))
					{
						var folderName = Path.GetFileName(rootPath);
						if (!string.IsNullOrEmpty(folderName))
						{
							previewItems.Add(Path.Combine(basePath, folderName).Replace("\\", "/"));
						}

						foreach (var filePath in EnumerateFolderAssets(rootPath))
						{
							var relative = filePath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
							var renamedRelative = RenameAsmdefFilenameInPath(relative);
							previewItems.Add(Path.Combine(basePath, folderName, renamedRelative).Replace("\\", "/"));
						}
					}
					else
					{
						var fileName = Path.GetFileName(rootPath);
						if (!string.IsNullOrEmpty(fileName))
						{
							var renamedFileName = RenameAsmdefFilenameInPath(fileName);
							previewItems.Add(Path.Combine(basePath, renamedFileName).Replace("\\", "/"));
						}
					}
				}

				foreach (var dependency in dependencyPaths
					.OrderBy(dep => Path.GetFileName(dep), StringComparer.OrdinalIgnoreCase)
					.ThenBy(dep => dep, StringComparer.OrdinalIgnoreCase))
				{
					var fileName = Path.GetFileName(dependency);
					if (!string.IsNullOrEmpty(fileName))
					{
						var renamedFileName = RenameAsmdefFilenameInPath(fileName);
						previewItems.Add(Path.Combine(dependenciesBasePath, renamedFileName).Replace("\\", "/"));
					}
				}
			}

			return previewItems.Distinct().ToList();
		}

		private string GetDuplicateSubFolder()
		{
			return string.IsNullOrWhiteSpace(_duplicateSubFolder) ? string.Empty : _duplicateSubFolder.Trim();
		}

		private string GetDependenciesFolder()
		{
			return NormalizeDependenciesFolder(_dependenciesFolder);
		}

		private string RenameAsmdefFilenameInPath(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return path;
			}

			var directory = Path.GetDirectoryName(path);
			var fileName = Path.GetFileNameWithoutExtension(path);
			var extension = Path.GetExtension(path);
			if (!string.Equals(extension, ".asmdef", StringComparison.OrdinalIgnoreCase))
			{
				return path;
			}

			var renamed = ApplyPatternRename(fileName, _asmdefFileRenames);
			if (string.IsNullOrEmpty(renamed))
			{
				renamed = fileName;
			}

			var newFileName = renamed + extension;
			if (string.IsNullOrEmpty(directory))
			{
				return newFileName;
			}

			return Path.Combine(directory, newFileName);
		}

		private string BuildDestinationBasePath(string destinationPath)
		{
			return BuildDestinationBasePath(destinationPath, GetDuplicateSubFolder());
		}

		private static string BuildDestinationBasePath(string destinationPath, string duplicateSubFolder)
		{
			return string.IsNullOrEmpty(duplicateSubFolder)
				? destinationPath.Replace("\\", "/")
				: Path.Combine(destinationPath, duplicateSubFolder).Replace("\\", "/");
		}

		private static string BuildDependenciesBasePath(string basePath, string dependenciesFolder)
		{
			var normalizedDependenciesFolder = NormalizeDependenciesFolder(dependenciesFolder);
			if (string.IsNullOrEmpty(normalizedDependenciesFolder))
			{
				return basePath;
			}

			return Path.Combine(basePath, normalizedDependenciesFolder).Replace("\\", "/");
		}

		private static string NormalizeDependenciesFolder(string dependenciesFolder)
		{
			if (string.IsNullOrWhiteSpace(dependenciesFolder))
			{
				return string.Empty;
			}

			return dependenciesFolder.Trim().Replace("\\", "/").Trim('/');
		}

		private static string GetDependencyRelativePath(string dependencyPath, IEnumerable<string> rootPaths)
		{
			if (string.IsNullOrEmpty(dependencyPath))
			{
				return string.Empty;
			}

			var normalizedDependency = dependencyPath.Replace("\\", "/");
			if (rootPaths != null)
			{
				var normalizedRoots = rootPaths
					.Where(path => !string.IsNullOrEmpty(path))
					.Select(path => path.Replace("\\", "/"))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.OrderByDescending(path => path.Length)
					.ToList();

				foreach (var rootPath in normalizedRoots)
				{
					if (AssetDatabase.IsValidFolder(rootPath))
					{
						if (TryGetRelativePath(normalizedDependency, rootPath, out var relativeFromFolder))
						{
							return relativeFromFolder;
						}
					}
					else
					{
						var rootDirectory = Path.GetDirectoryName(rootPath)?.Replace("\\", "/");
						if (TryGetRelativePath(normalizedDependency, rootDirectory, out var relativeFromFileRoot))
						{
							return relativeFromFileRoot;
						}
					}
				}
			}

			if (normalizedDependency.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
			{
				return normalizedDependency.Substring("Assets/".Length);
			}

			if (normalizedDependency.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
			{
				return normalizedDependency.Substring("Packages/".Length);
			}

			return Path.GetFileName(normalizedDependency);
		}

		private static string DetermineKeepStructureRoot(IEnumerable<string> selectedFiles, IEnumerable<string> dependencyPaths)
		{
			var allPaths = new List<string>();
			if (selectedFiles != null)
			{
				allPaths.AddRange(selectedFiles.Where(path => !string.IsNullOrEmpty(path)));
			}

			if (dependencyPaths != null)
			{
				allPaths.AddRange(dependencyPaths.Where(path => !string.IsNullOrEmpty(path)));
			}

			if (allPaths.Count == 0)
			{
				return string.Empty;
			}

			var normalizedPaths = allPaths
				.Select(path => path.Replace("\\", "/"))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			var commonRoot = GetCommonPathPrefix(normalizedPaths);
			if (string.IsNullOrEmpty(commonRoot))
			{
				return string.Empty;
			}

			if (!AssetDatabase.IsValidFolder(commonRoot))
			{
				commonRoot = Path.GetDirectoryName(commonRoot)?.Replace("\\", "/") ?? string.Empty;
			}

			return commonRoot?.TrimEnd('/') ?? string.Empty;
		}

		private static string GetRelativePathFromStructureRoot(string fullPath, string structureRoot)
		{
			if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(structureRoot))
			{
				return string.Empty;
			}

			var normalizedPath = fullPath.Replace("\\", "/");
			var normalizedRoot = structureRoot.Replace("\\", "/").TrimEnd('/');
			return TryGetRelativePath(normalizedPath, normalizedRoot, out var relativePath)
				? relativePath
				: string.Empty;
		}

		private static string GetCommonPathPrefix(List<string> paths)
		{
			if (paths == null || paths.Count == 0)
			{
				return string.Empty;
			}

			var splitPaths = paths
				.Select(path => path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
				.ToList();

			if (splitPaths.Count == 0)
			{
				return string.Empty;
			}

			var minLen = splitPaths.Min(parts => parts.Length);
			if (minLen == 0)
			{
				return string.Empty;
			}

			var commonParts = new List<string>();
			for (int i = 0; i < minLen; i++)
			{
				var current = splitPaths[0][i];
				if (splitPaths.All(parts => string.Equals(parts[i], current, StringComparison.OrdinalIgnoreCase)))
				{
					commonParts.Add(current);
				}
				else
				{
					break;
				}
			}

			return commonParts.Count == 0 ? string.Empty : string.Join("/", commonParts);
		}

		private static bool TryGetRelativePath(string fullPath, string rootPath, out string relativePath)
		{
			relativePath = null;
			if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(rootPath))
			{
				return false;
			}

			if (string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase))
			{
				relativePath = Path.GetFileName(fullPath);
				return !string.IsNullOrEmpty(relativePath);
			}

			var prefix = rootPath.EndsWith("/", StringComparison.Ordinal) ? rootPath : rootPath + "/";
			if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			relativePath = fullPath.Substring(prefix.Length).TrimStart('/', '\\');
			return !string.IsNullOrEmpty(relativePath);
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

		private static bool IsProjectAssetPath(string path, out string assetPath)
		{
			assetPath = null;
			if (string.IsNullOrEmpty(path))
			{
				return false;
			}

			if (path.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
			{
				assetPath = path.Replace("\\", "/");
				return true;
			}

			assetPath = TryGetAssetPathFromFullPath(path);
			return !string.IsNullOrEmpty(assetPath);
		}

		private static void ResetDependencyCaches()
		{
			_assetGameDependencies = null;
			_assetTGSPackageDependencies = null;
			_assetSystemDependencies = null;
			_assetReplaceDependencies = null;
			_uniqueAssetReplaceDependencies = null;
			_uniqueAssetReplaceDependenciesReplace = null;
			_uniqueAssetReplaceDependenciesRemove = null;
		}

		private void ResetDirectDependencyCache()
		{
			_cachedDirectDepsInput = null;
			_cachedDirectDeps = null;
		}

		private void ResetCodeReferencesCache()
		{
			_cachedCodeRefsInput = null;
			_cachedCodeRefs = null;
		}

		private static List<string> ResolveSelectedRootPaths(IEnumerable<Object> assets)
		{
			var results = new List<string>();
			if (assets == null)
			{
				return results;
			}

			foreach (var asset in assets)
			{
				if (asset == null)
				{
					continue;
				}

				var path = AssetDatabase.GetAssetPath(asset);
				if (string.IsNullOrEmpty(path))
				{
					continue;
				}

				if (!results.Contains(path))
				{
					results.Add(path);
				}
			}

			return results;
		}

		private CodeReferencesInfo GetCodeReferencesCached()
		{
			var selectedAssetPaths = ResolveSelectedAssetPaths(OriginalAssets)
				.Where(path => !string.IsNullOrEmpty(path))
				.ToArray();
			var dependencyPaths = GetGameAndTGSPackageDependencies(selectedAssetPaths)
				.Where(path => !string.IsNullOrEmpty(path))
				.ToArray();
			var selectedPaths = selectedAssetPaths
				.Concat(dependencyPaths)
				.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
							   path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase))
				.Select(path => path.Replace("\\", "/"))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(path => path)
				.ToArray();

			if (selectedPaths.Length == 0)
			{
				_cachedCodeRefsInput = Array.Empty<string>();
				_cachedCodeRefs = new CodeReferencesInfo();
				return _cachedCodeRefs;
			}

			if (_cachedCodeRefsInput != null &&
				_cachedCodeRefs != null &&
				selectedPaths.SequenceEqual(_cachedCodeRefsInput))
			{
				return _cachedCodeRefs;
			}

			var namespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var asmdefFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var rootNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var asmdefNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var path in selectedPaths)
			{
				if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
				{
					foreach (var ns in ParseNamespaces(path))
					{
						namespaces.Add(ns);
					}
				}
				else if (path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase))
				{
					var fileName = Path.GetFileNameWithoutExtension(path);
					if (!string.IsNullOrEmpty(fileName))
					{
						asmdefFiles.Add(fileName);
					}

					if (TryReadAsmdefStringValue(path, "rootNamespace", out var rootNamespace) &&
						!string.IsNullOrEmpty(rootNamespace))
					{
						rootNamespaces.Add(rootNamespace);
					}

					if (TryReadAsmdefStringValue(path, "name", out var asmdefName) &&
						!string.IsNullOrEmpty(asmdefName))
					{
						asmdefNames.Add(asmdefName);
					}
				}
			}

			_cachedCodeRefsInput = selectedPaths;
			_cachedCodeRefs = new CodeReferencesInfo
			{
				NamespacePatterns = BuildPatternList(namespaces),
				AsmdefFilePatterns = BuildPatternList(asmdefFiles),
				RootNamespacePatterns = BuildPatternList(rootNamespaces),
				AsmdefNamePatterns = BuildPatternList(asmdefNames)
			};
			return _cachedCodeRefs;
		}

		private static List<string> ResolveSelectedAssetPaths(IEnumerable<Object> assets)
		{
			var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (assets == null)
			{
				return results.ToList();
			}

			foreach (var asset in assets)
			{
				if (asset == null)
				{
					continue;
				}

				var path = AssetDatabase.GetAssetPath(asset);
				if (string.IsNullOrEmpty(path))
				{
					continue;
				}

				if (AssetDatabase.IsValidFolder(path))
				{
					foreach (var assetPath in EnumerateFolderAssets(path))
					{
						results.Add(assetPath);
					}
				}
				else
				{
					results.Add(path);
				}
			}

			return results.ToList();
		}

		private static List<string> EnumerateFolderAssets(string folderPath)
		{
			var assets = new List<string>();
			if (string.IsNullOrEmpty(folderPath))
			{
				return assets;
			}

			var guids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
			foreach (var guid in guids)
			{
				var assetPath = AssetDatabase.GUIDToAssetPath(guid);
				if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
				{
					continue;
				}

				assets.Add(assetPath);
			}

			return assets;
		}

		private static DependencyManager GetOpenWindow()
		{
			return Resources.FindObjectsOfTypeAll<DependencyManager>().FirstOrDefault();
		}

		private static List<string> ParseNamespaces(string assetPath)
		{
			var results = new List<string>();
			if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath))
			{
				return results;
			}

			var content = File.ReadAllText(assetPath);
			var matches = Regex.Matches(content, @"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.Multiline);
			foreach (Match match in matches)
			{
				if (match.Success && match.Groups.Count > 1)
				{
					var value = match.Groups[1].Value.Trim();
					if (!string.IsNullOrEmpty(value))
					{
						results.Add(value);
					}
				}
			}

			return results;
		}

		private static bool TryReadAsmdefStringValue(string assetPath, string key, out string value)
		{
			value = null;
			if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath))
			{
				return false;
			}

			var content = File.ReadAllText(assetPath);
			var match = Regex.Match(content, $"\"{Regex.Escape(key)}\"\\s*:\\s*\"([^\"]+)\"");
			if (!match.Success || match.Groups.Count < 2)
			{
				return false;
			}

			value = match.Groups[1].Value.Trim();
			return !string.IsNullOrEmpty(value);
		}

		private static List<string> BuildPatternList(IEnumerable<string> values)
		{
			var items = values
				.Where(value => !string.IsNullOrEmpty(value))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (items.Count <= 1)
			{
				return items;
			}

			var commonPrefix = GetCommonPrefix(items);
			if (!string.IsNullOrEmpty(commonPrefix))
			{
				return new List<string> { commonPrefix };
			}

			var grouped = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (var item in items)
			{
				var parent = GetParentPrefix(item);
				if (string.IsNullOrEmpty(parent))
				{
					parent = item;
				}

				if (!grouped.TryGetValue(parent, out var list))
				{
					list = new List<string>();
					grouped[parent] = list;
				}

				list.Add(item);
			}

			var results = new List<string>();
			foreach (var entry in grouped.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
			{
				if (entry.Value.Count > 1)
				{
					results.Add(entry.Key);
				}
				else
				{
					results.Add(entry.Value[0]);
				}
			}

			return results;
		}

		private static string GetCommonPrefix(List<string> items)
		{
			if (items == null || items.Count == 0)
			{
				return string.Empty;
			}

			var split = items.Select(item => item.Split('.')).ToList();
			var minSegments = split.Any(parts => parts.Length > 1) ? 2 : 1;
			var prefix = new List<string>(split[0]);
			for (int i = prefix.Count - 1; i >= 0; i--)
			{
				if (split.All(parts => parts.Length > i && parts[i] == prefix[i]))
				{
					continue;
				}

				prefix.RemoveAt(i);
			}

			if (prefix.Count < minSegments)
			{
				return string.Empty;
			}

			return string.Join(".", prefix);
		}

		private static string GetParentPrefix(string value)
		{
			var lastDot = value.LastIndexOf('.');
			return lastDot > 0 ? value.Substring(0, lastDot) : string.Empty;
		}

		private static string ReplaceAsmdefStringValue(string content, string key, Dictionary<string, string> renames)
		{
			if (string.IsNullOrEmpty(content) || renames == null || renames.Count == 0)
			{
				return content;
			}

			var pattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*\"([^\"]+)\"";
			return Regex.Replace(content, pattern, match =>
			{
				var value = match.Groups[1].Value;
				var renamed = ApplyPatternRename(value, renames);
				return match.Value.Replace(value, renamed);
			});
		}

		private static string ReplaceAsmdefReferences(string content, Dictionary<string, string> renames)
		{
			if (string.IsNullOrEmpty(content) || renames == null || renames.Count == 0)
			{
				return content;
			}

			var pattern = "\"references\"\\s*:\\s*\\[(?<body>[^\\]]*)\\]";
			return Regex.Replace(content, pattern, match =>
			{
				var body = match.Groups["body"].Value;
				var replacedBody = Regex.Replace(body, "\"([^\"]+)\"", innerMatch =>
				{
					var value = innerMatch.Groups[1].Value;
					var renamed = ApplyPatternRename(value, renames);
					return innerMatch.Value.Replace(value, renamed);
				});
				return match.Value.Replace(body, replacedBody);
			}, RegexOptions.Singleline);
		}

		private string ReplaceNamespacePatterns(string content)
		{
			if (string.IsNullOrEmpty(content) || _namespaceRenames == null || _namespaceRenames.Count == 0)
			{
				return content;
			}

			var updated = content;
			foreach (var entry in _namespaceRenames)
			{
				if (string.IsNullOrEmpty(entry.Key) || string.IsNullOrEmpty(entry.Value))
				{
					continue;
				}

				var pattern = $"(?<![A-Za-z0-9_]){Regex.Escape(entry.Key)}(?=\\b|\\.)";
				updated = Regex.Replace(updated, pattern, entry.Value);
			}

			return updated;
		}

		private static bool AreRenamesComplete(List<string> patterns, Dictionary<string, string> renames)
		{
			if (patterns == null || patterns.Count == 0)
			{
				return true;
			}

			foreach (var pattern in patterns)
			{
				if (!renames.TryGetValue(pattern, out var value) || string.IsNullOrWhiteSpace(value))
				{
					return false;
				}
			}

			return true;
		}

		private bool AreCodeReferenceRenamesComplete(CodeReferencesInfo codeRefs)
		{
			if (codeRefs == null || !codeRefs.HasAny)
			{
				return true;
			}

			return AreRenamesComplete(codeRefs.NamespacePatterns, _namespaceRenames) &&
				   AreRenamesComplete(codeRefs.AsmdefFilePatterns, _asmdefFileRenames) &&
				   AreRenamesComplete(codeRefs.RootNamespacePatterns, _rootNamespaceRenames) &&
				   AreRenamesComplete(codeRefs.AsmdefNamePatterns, _asmdefNameRenames);
		}

		private static string ApplyPatternRename(string value, Dictionary<string, string> renames)
		{
			if (string.IsNullOrEmpty(value) || renames == null || renames.Count == 0)
			{
				return value;
			}

			string bestMatch = null;
			foreach (var key in renames.Keys)
			{
				if (string.IsNullOrEmpty(key))
				{
					continue;
				}

				if (value.StartsWith(key, StringComparison.OrdinalIgnoreCase))
				{
					if (bestMatch == null || key.Length > bestMatch.Length)
					{
						bestMatch = key;
					}
				}
			}

			if (bestMatch == null)
			{
				return value;
			}

			if (!renames.TryGetValue(bestMatch, out var newValue) || string.IsNullOrEmpty(newValue))
			{
				return value;
			}

			return newValue + value.Substring(bestMatch.Length);
		}

		private class CodeReferencesInfo
		{
			public List<string> NamespacePatterns = new List<string>();
			public List<string> AsmdefFilePatterns = new List<string>();
			public List<string> RootNamespacePatterns = new List<string>();
			public List<string> AsmdefNamePatterns = new List<string>();

			public bool HasAny =>
				NamespacePatterns.Count > 0 ||
				AsmdefFilePatterns.Count > 0 ||
				RootNamespacePatterns.Count > 0 ||
				AsmdefNamePatterns.Count > 0;
		}

		// ========================== DUPLICATE ASSET ============================


		private void DuplicateAsset(Object[] assets, string destinationPath, string duplicateSubFolder,
			string dependenciesFolder, bool keepFolderStructure)
		{
			if (assets == null || assets.Length == 0)
			{
				return;
			}

			if (string.IsNullOrEmpty(destinationPath))
			{
				return;
			}

			var copiedFiles = CopySelectionToDestination(assets, destinationPath, duplicateSubFolder,
				dependenciesFolder, keepFolderStructure, true);
			ApplyCodeReferenceRenames(copiedFiles);
			RemapGuids(copiedFiles.ToList());
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			var importPath = BuildDestinationBasePath(destinationPath, duplicateSubFolder);
			if (IsProjectAssetPath(importPath, out var assetPath))
			{
				AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ImportRecursive);
			}
		}

		private static HashSet<string> CopySelectionToDestination(Object[] assets, string destinationPath, string duplicateSubFolder,
			string dependenciesFolder, bool keepFolderStructure, bool includeDependencies)
		{
			var copiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (assets == null || assets.Length == 0)
			{
				return copiedFiles;
			}

			BuildSelectionPlan(assets, out var folderRoots, out var fileRoots, out var selectedFiles,
				out var selectedFileAnchorRoots);
			var basePath = BuildDestinationBasePath(destinationPath, duplicateSubFolder);
			var dependenciesBasePath = BuildDependenciesBasePath(basePath, dependenciesFolder);
			var dependencies = includeDependencies && selectedFiles.Count > 0
				? GetGameAndTGSPackageDependencies(selectedFiles.ToArray())
					.Where(dep => !selectedFiles.Contains(dep))
					.Where(dep => !AssetDatabase.IsValidFolder(dep))
					.ToList()
				: new List<string>();
			Dictionary<string, string> selectedFileRoots = null;
			Dictionary<string, string> dependencyRoots = null;
			if (keepFolderStructure)
			{
				BuildPerAssetStructureRoots(selectedFiles, dependencies, selectedFileAnchorRoots,
					out selectedFileRoots, out dependencyRoots);
			}
			var rootPaths = folderRoots.Concat(fileRoots).ToList();

			if (keepFolderStructure)
			{
				foreach (var selectedFile in selectedFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
				{
					if (!selectedFileRoots.TryGetValue(selectedFile, out var structureRoot))
					{
						if (!selectedFileAnchorRoots.TryGetValue(selectedFile, out structureRoot))
						{
							structureRoot = Path.GetDirectoryName(selectedFile)?.Replace("\\", "/") ?? string.Empty;
						}
					}

					var relativePath = GetRelativePathFromStructureRoot(selectedFile, structureRoot);
					if (string.IsNullOrEmpty(relativePath))
					{
						relativePath = Path.GetFileName(selectedFile);
					}

					if (string.IsNullOrEmpty(relativePath))
					{
						continue;
					}

					var destinationPathForSelection = Path.Combine(basePath, relativePath);
					CopyFileWithMeta(selectedFile, Path.GetDirectoryName(destinationPathForSelection), copiedFiles,
						destinationPathForSelection);
				}
			}
			else
			{
				foreach (var folderRoot in folderRoots)
				{
					CopyFolderRoot(folderRoot, basePath, copiedFiles, false, string.Empty);
				}

				foreach (var fileRoot in fileRoots)
				{
					CopyFileRoot(fileRoot, basePath, copiedFiles, false, string.Empty);
				}
			}

			if (dependencies.Count > 0)
			{
				if (!Directory.Exists(dependenciesBasePath))
				{
					Directory.CreateDirectory(dependenciesBasePath);
				}

				foreach (var dep in dependencies)
				{
					if (keepFolderStructure)
					{
						var structureRoot = dependencyRoots != null && dependencyRoots.TryGetValue(dep, out var mappedRoot)
							? mappedRoot
							: string.Empty;
						var relativePath = GetRelativePathFromStructureRoot(dep, structureRoot);
						if (string.IsNullOrEmpty(relativePath))
						{
							relativePath = GetDependencyRelativePath(dep, rootPaths);
						}

						if (string.IsNullOrEmpty(relativePath))
						{
							relativePath = Path.GetFileName(dep);
						}

						var destinationPathForDependency = Path.Combine(dependenciesBasePath, relativePath);
						CopyFileWithMeta(dep, Path.GetDirectoryName(destinationPathForDependency), copiedFiles,
							destinationPathForDependency);
					}
					else
					{
						CopyFileWithMeta(dep, dependenciesBasePath, copiedFiles);
					}
				}
			}

			return copiedFiles;
		}

		private static void CopyFileRoot(string sourcePath, string basePath, HashSet<string> copiedFiles,
			bool keepFolderStructure, string keepStructureRoot)
		{
			if (string.IsNullOrEmpty(sourcePath))
			{
				return;
			}

			var fileName = Path.GetFileName(sourcePath);
			if (string.IsNullOrEmpty(fileName))
			{
				return;
			}

			if (keepFolderStructure)
			{
				var relativePath = GetRelativePathFromStructureRoot(sourcePath, keepStructureRoot);
				if (!string.IsNullOrEmpty(relativePath))
				{
					var destinationPath = Path.Combine(basePath, relativePath);
					CopyFileWithMeta(sourcePath, Path.GetDirectoryName(destinationPath), copiedFiles, destinationPath);
					return;
				}
			}

			if (!Directory.Exists(basePath))
			{
				Directory.CreateDirectory(basePath);
			}

			CopyFileWithMeta(sourcePath, basePath, copiedFiles);
		}

		private static void CopyFolderRoot(string folderPath, string basePath, HashSet<string> copiedFiles,
			bool keepFolderStructure, string keepStructureRoot)
		{
			if (string.IsNullOrEmpty(folderPath))
			{
				return;
			}

			var folderName = Path.GetFileName(folderPath);
			if (string.IsNullOrEmpty(folderName))
			{
				return;
			}

			var destRoot = Path.Combine(basePath, folderName);
			var folderAssets = EnumerateFolderAssets(folderPath);
			var total = folderAssets.Count;

			for (int i = 0; i < total; i++)
			{
				if (EditorUtility.DisplayCancelableProgressBar("Duplicating...", "Copying " + Path.GetFileName(folderAssets[i]),
						(float)i / total)) throw new OperationCanceledException();
				string destPath;
				if (keepFolderStructure)
				{
					var relativePath = GetRelativePathFromStructureRoot(folderAssets[i], keepStructureRoot);
					destPath = string.IsNullOrEmpty(relativePath)
						? Path.Combine(destRoot, Path.GetFileName(folderAssets[i]))
						: Path.Combine(basePath, relativePath);
				}
				else
				{
					var relative = folderAssets[i].Substring(folderPath.Length)
						.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
					destPath = Path.Combine(destRoot, relative);
				}
				CopyFileWithMeta(folderAssets[i], Path.GetDirectoryName(destPath), copiedFiles, destPath);
			}

			EditorUtility.ClearProgressBar();
		}

		private static void CopyFileWithMeta(string sourcePath, string destDir, HashSet<string> copiedFiles, string destPathOverride = null)
		{
			if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destDir))
			{
				return;
			}

			if (!Directory.Exists(destDir))
			{
				Directory.CreateDirectory(destDir);
			}

			var fileName = Path.GetFileName(sourcePath);
			if (string.IsNullOrEmpty(fileName))
			{
				return;
			}

			var destPath = destPathOverride ?? Path.Combine(destDir, fileName);
			if (!File.Exists(destPath))
			{
				File.Copy(sourcePath, destPath, false);
			}
			copiedFiles.Add(destPath);

			var metaPath = sourcePath + ".meta";
			if (File.Exists(metaPath))
			{
				var destMeta = destPath + ".meta";
				if (!File.Exists(destMeta))
				{
					File.Copy(metaPath, destMeta, false);
				}
				copiedFiles.Add(destMeta);
			}
		}

		private void ApplyCodeReferenceRenames(HashSet<string> copiedFiles)
		{
			if (copiedFiles == null || copiedFiles.Count == 0)
			{
				return;
			}

			RenameAsmdefFiles(copiedFiles);
			UpdateAsmdefContents(copiedFiles);
			UpdateCodeNamespaces(copiedFiles);
		}

		private void RenameAsmdefFiles(HashSet<string> copiedFiles)
		{
			var asmdefFiles = copiedFiles
				.Where(path => path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase))
				.ToList();

			foreach (var asmdefPath in asmdefFiles)
			{
				var directory = Path.GetDirectoryName(asmdefPath);
				var fileName = Path.GetFileNameWithoutExtension(asmdefPath);
				if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
				{
					continue;
				}

				var renamed = ApplyPatternRename(fileName, _asmdefFileRenames);
				if (string.IsNullOrEmpty(renamed) || string.Equals(renamed, fileName, StringComparison.Ordinal))
				{
					continue;
				}

				var newPath = Path.Combine(directory, renamed + ".asmdef");
				if (File.Exists(newPath))
				{
					continue;
				}

				File.Move(asmdefPath, newPath);
				copiedFiles.Remove(asmdefPath);
				copiedFiles.Add(newPath);

				var metaPath = asmdefPath + ".meta";
				if (File.Exists(metaPath))
				{
					var newMetaPath = newPath + ".meta";
					if (!File.Exists(newMetaPath))
					{
						File.Move(metaPath, newMetaPath);
					}
					copiedFiles.Remove(metaPath);
					copiedFiles.Add(newMetaPath);
				}
			}
		}

		private void UpdateAsmdefContents(HashSet<string> copiedFiles)
		{
			foreach (var asmdefPath in copiedFiles.Where(path => path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)))
			{
				if (!File.Exists(asmdefPath))
				{
					continue;
				}

				var content = File.ReadAllText(asmdefPath);
				var updated = ReplaceAsmdefStringValue(content, "name", _asmdefNameRenames);
				updated = ReplaceAsmdefStringValue(updated, "rootNamespace", _rootNamespaceRenames);
				updated = ReplaceAsmdefReferences(updated, _asmdefNameRenames);

				if (!string.Equals(content, updated, StringComparison.Ordinal))
				{
					File.WriteAllText(asmdefPath, updated);
				}
			}
		}

		private void UpdateCodeNamespaces(HashSet<string> copiedFiles)
		{
			foreach (var codePath in copiedFiles.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
			{
				if (!File.Exists(codePath))
				{
					continue;
				}

				var content = File.ReadAllText(codePath);
				var updated = ReplaceNamespacePatterns(content);
				if (!string.Equals(content, updated, StringComparison.Ordinal))
				{
					File.WriteAllText(codePath, updated);
				}
			}
		}

		private static HashSet<string> CopyExtractedPackage(string sourceRoot, string destinationPath, string duplicateSubFolder)
		{
			var copiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (string.IsNullOrEmpty(sourceRoot) || !Directory.Exists(sourceRoot))
			{
				return copiedFiles;
			}

			var basePath = BuildDestinationBasePath(destinationPath, duplicateSubFolder);
			if (!Directory.Exists(basePath))
			{
				Directory.CreateDirectory(basePath);
			}

			var sourceFiles = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
			var total = sourceFiles.Length;
			try
			{
				for (int i = 0; i < total; i++)
				{
					if (EditorUtility.DisplayCancelableProgressBar("Importing...", "Copying " + Path.GetFileName(sourceFiles[i]),
							(float)i / total)) throw new OperationCanceledException();
					var relative = sourceFiles[i].Substring(sourceRoot.Length)
						.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
					var destPath = Path.Combine(basePath, relative);
					var destDir = Path.GetDirectoryName(destPath);
					if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
					{
						Directory.CreateDirectory(destDir);
					}

					if (!File.Exists(destPath))
					{
						File.Copy(sourceFiles[i], destPath, false);
					}
					copiedFiles.Add(destPath);
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			return copiedFiles;
		}

		private static void BuildSelectionPlan(Object[] assets, out List<string> folderRoots, out List<string> fileRoots,
			out HashSet<string> selectedFiles, out Dictionary<string, string> selectedFileAnchorRoots)
		{
			folderRoots = new List<string>();
			fileRoots = new List<string>();
			selectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			selectedFileAnchorRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			if (assets == null)
			{
				return;
			}

			foreach (var asset in assets)
			{
				if (asset == null)
				{
					continue;
				}

				var path = AssetDatabase.GetAssetPath(asset);
				if (string.IsNullOrEmpty(path))
				{
					continue;
				}

				if (AssetDatabase.IsValidFolder(path))
				{
					var normalizedFolderPath = path.Replace("\\", "/");
					if (!folderRoots.Contains(path))
					{
						folderRoots.Add(path);
					}

					foreach (var assetPath in EnumerateFolderAssets(path))
					{
						selectedFiles.Add(assetPath);
						if (!selectedFileAnchorRoots.ContainsKey(assetPath))
						{
							selectedFileAnchorRoots[assetPath] = normalizedFolderPath;
						}
					}
				}
				else
				{
					if (!fileRoots.Contains(path))
					{
						fileRoots.Add(path);
					}

					selectedFiles.Add(path);
					if (!selectedFileAnchorRoots.ContainsKey(path))
					{
						selectedFileAnchorRoots[path] = Path.GetDirectoryName(path)?.Replace("\\", "/") ?? string.Empty;
					}
				}
			}
		}

		private static void RemapGuids(List<string> copiedFiles)
		{
			if (copiedFiles == null || copiedFiles.Count == 0)
			{
				return;
			}

			var metaFiles = copiedFiles.Where(f => f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).ToList();
			var guidTable = new List<(string originalGuid, string newGuid)>();
			try
			{
				for (int i = 0; i < metaFiles.Count; i++)
				{
					if (EditorUtility.DisplayCancelableProgressBar("Duplicating...", "Processing .meta files",
							(float)i / metaFiles.Count)) throw new OperationCanceledException();

					if (TryGetGuidFromMeta(metaFiles[i], out var originalGuid))
					{
						string newGuid = GUID.Generate().ToString().Replace("-", "");
						guidTable.Add((originalGuid, newGuid));
					}
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			if (guidTable.Count == 0)
			{
				return;
			}

			var allFiles = copiedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			try
			{
				for (int i = 0; i < allFiles.Count; i++)
				{
					if (!allFiles[i].EndsWith(".meta", StringComparison.OrdinalIgnoreCase) &&
						IgnoreFileFormat(allFiles[i])) continue;
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
		}

		private static bool TryGetGuidFromMeta(string metaPath, out string guid)
		{
			guid = null;
			if (string.IsNullOrEmpty(metaPath) || !File.Exists(metaPath))
			{
				return false;
			}

			foreach (var line in File.ReadAllLines(metaPath))
			{
				if (line.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
				{
					guid = line.Substring("guid:".Length).Trim();
					return !string.IsNullOrEmpty(guid);
				}
			}

			return false;
		}

		// ========================== REPLACE REFERENCES ============================

		private void UpdateToolScreen()
		{
			// Gimmick to reload the window
			var tmpAsset = OriginalAssets;
			OriginalAssets = null;
			OriginalAssets = tmpAsset;

			_assetGameDependencies = null;
			_assetTGSPackageDependencies = null;
			_assetReplaceDependencies = null;
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

		public static void ExportSelectedPackages(List<Object> assets, string duplicateSubFolder = "_Duplicated_Assets",
			string dependenciesFolder = DEPENDENCIES_PATH, bool keepFolderStructure = true)
		{
			if (assets == null || assets.Count == 0)
			{
				return;
			}

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

				var copiedFiles = CopySelectionToDestination(assets.ToArray(), selectedPathInner, duplicateSubFolder,
					dependenciesFolder, keepFolderStructure, true);
				var window = GetOpenWindow();
				if (window != null)
				{
					window.ApplyCodeReferenceRenames(copiedFiles);
				}
				RemapGuids(copiedFiles.ToList());
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				using (var zipToOpen = new FileStream(selectedFilePath, FileMode.Create))
				using (var archive =
					   new System.IO.Compression.ZipArchive(zipToOpen, System.IO.Compression.ZipArchiveMode.Create))
				{
					var zipRootPath = Path.Combine(selectedPathInner, duplicateSubFolder);
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

		public static void ImportPackage(string packagePath, string destinationPath, string duplicateSubFolder = "_Duplicated_Assets")
		{
			Debug.Log($"Selected destination folder: {destinationPath}");
			
			// Extract to a temp folder first to avoid Unity auto-import
			var tempExtractPath =
				Path.Combine(destinationPath, $"tgspackage_tmp_extract_{DateTime.Now:yyyyMMdd_HHmmssfff}");
			try
			{
				AssetDatabase.StartAssetEditing();
				
				ExtractZip(packagePath, tempExtractPath);
				
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				AssetDatabase.StopAssetEditing();
				
				AssetDatabase.StartAssetEditing();
				
				var copiedFiles = CopyExtractedPackage(tempExtractPath, destinationPath, duplicateSubFolder);
				var window = GetOpenWindow();
				if (window != null)
				{
					window.ApplyCodeReferenceRenames(copiedFiles);
				}
				RemapGuids(copiedFiles.ToList());
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				var importPath = BuildDestinationBasePath(destinationPath, duplicateSubFolder);
				AssetDatabase.ImportAsset(importPath, ImportAssetOptions.ImportRecursive);
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

		private static bool IsTGSPackagePath(string dependency)
		{
			return !string.IsNullOrEmpty(dependency) &&
				   dependency.IndexOf("TGSPackageManager", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static bool IsSystemOrCommon(string dependency) => !dependency.Contains(DEPENDENCIES_BUNDLES_PATH) ||
											   DEPENDENCIES_COMMON_BUNDLE_PATHS.Any(common =>
												   dependency.Contains(common));

		private static string[] GetSystemAssetDependencies(params string[] assets)
		{
			var deps = AssetDatabase.GetDependencies(assets, false)
				.Where(d => IsSystemOrCommon(d) && !IsTGSPackagePath(d))
				.ToList();
			deps.Sort();
			return deps.ToArray();
		}

		private static string[] GetGameAssetDependencies(params string[] assets)
		{
			assets = assets.Select(a => a.Replace("\\", "/")).ToArray();
			List<string> dependencies = AssetDatabase.GetDependencies(assets, true).ToList();

			// Remove original assets from dependencies    ||    Remove dependencies outside Bundles folder
			dependencies.RemoveAll(d => (Array.IndexOf(assets, d) > -1) || IsSystemOrCommon(d) || IsTGSPackagePath(d));
			dependencies.Sort();

			return dependencies.ToArray();
		}

		private static string[] GetTGSPackageDependencies(params string[] assets)
		{
			assets = assets.Select(a => a.Replace("\\", "/")).ToArray();
			List<string> dependencies = AssetDatabase.GetDependencies(assets, true).ToList();

			dependencies.RemoveAll(d => (Array.IndexOf(assets, d) > -1) || !IsTGSPackagePath(d));
			dependencies.Sort();

			return dependencies.ToArray();
		}

		private static string[] GetGameAndTGSPackageDependencies(params string[] assets)
		{
			return GetGameAssetDependencies(assets)
				.Concat(GetTGSPackageDependencies(assets))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}

		private static string GetDependencyGroupLabel(string dependency)
		{
			if (string.IsNullOrEmpty(dependency))
			{
				return "UNKNOWN";
			}

			var normalized = dependency.Replace("\\", "/");

			if (IsTGSPackagePath(normalized))
			{
				const string embeddedMarker = "/embedded_packages/";
				var embeddedIndex = normalized.IndexOf(embeddedMarker, StringComparison.OrdinalIgnoreCase);
				if (embeddedIndex >= 0)
				{
					var packageStart = embeddedIndex + embeddedMarker.Length;
					if (packageStart < normalized.Length)
					{
						var packageEnd = normalized.IndexOf("/", packageStart, StringComparison.Ordinal);
						if (packageEnd > packageStart)
						{
							return normalized.Substring(packageStart, packageEnd - packageStart);
						}

						return normalized.Substring(packageStart);
					}
				}

				const string packagesMarker = "/Packages/";
				var packagesIndex = normalized.IndexOf(packagesMarker, StringComparison.OrdinalIgnoreCase);
				if (packagesIndex >= 0)
				{
					var packageStart = packagesIndex + packagesMarker.Length;
					if (packageStart < normalized.Length)
					{
						var packageEnd = normalized.IndexOf("/", packageStart, StringComparison.Ordinal);
						if (packageEnd > packageStart)
						{
							return normalized.Substring(packageStart, packageEnd - packageStart);
						}

						return normalized.Substring(packageStart);
					}
				}
			}

			if (normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
			{
				var startOffset = "Packages/".Length;
				var nextSlash = normalized.IndexOf("/", startOffset, StringComparison.Ordinal);
				return nextSlash > startOffset
					? normalized.Substring(startOffset, nextSlash - startOffset)
					: normalized.Substring(startOffset);
			}

			if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
			{
				var startOffset = "Assets/".Length;
				var nextSlash = normalized.IndexOf("/", startOffset, StringComparison.Ordinal);
				return nextSlash > startOffset
					? normalized.Substring(startOffset, nextSlash - startOffset)
					: normalized.Substring(startOffset);
			}

			return Path.GetFileName(normalized);
		}

		private static void BuildPerAssetStructureRoots(IEnumerable<string> selectedFiles, IEnumerable<string> dependencies,
			Dictionary<string, string> selectedFileAnchorRoots,
			out Dictionary<string, string> selectedFileRoots, out Dictionary<string, string> dependencyRoots)
		{
			selectedFileRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			dependencyRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			var selectedList = selectedFiles?
				.Where(path => !string.IsNullOrEmpty(path))
				.Select(path => path.Replace("\\", "/"))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
				.ToList() ?? new List<string>();

			var dependencySet = new HashSet<string>(
				dependencies?
					.Where(path => !string.IsNullOrEmpty(path))
					.Select(path => path.Replace("\\", "/"))
				?? Enumerable.Empty<string>(),
				StringComparer.OrdinalIgnoreCase);

			foreach (var selectedFile in selectedList)
			{
				if (!selectedFileAnchorRoots.TryGetValue(selectedFile, out var anchorRoot))
				{
					anchorRoot = Path.GetDirectoryName(selectedFile)?.Replace("\\", "/") ?? string.Empty;
				}

				var dependenciesForAsset = GetGameAndTGSPackageDependencies(selectedFile)
					.Where(dep => !string.IsNullOrEmpty(dep))
					.Select(dep => dep.Replace("\\", "/"))
					.Where(dep => !selectedList.Contains(dep))
					.Where(dep => dependencySet.Contains(dep))
					.ToList();

				string structureRoot;
				if (dependenciesForAsset.Count == 0)
				{
					structureRoot = anchorRoot;
				}
				else
				{
					structureRoot = DetermineKeepStructureRoot(new[] { selectedFile }, dependenciesForAsset);
					if (string.IsNullOrEmpty(structureRoot))
					{
						structureRoot = anchorRoot;
					}
				}

				selectedFileRoots[selectedFile] = structureRoot;

				foreach (var dependencyPath in dependenciesForAsset)
				{
					if (!dependencyRoots.ContainsKey(dependencyPath))
					{
						dependencyRoots[dependencyPath] = structureRoot;
					}
				}
			}
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
