using UnityEditor;

namespace com.tgs.mcpforunity.editor.Tools.Build
{
    public static class BuildTargetMapping
    {
        public static bool TryResolveBuildTarget(string name, out BuildTarget target)
        {
            if (string.IsNullOrEmpty(name))
            {
                target = EditorUserBuildSettings.activeBuildTarget;
                return true;
            }

            switch (name.ToLowerInvariant())
            {
                case "windows64": target = BuildTarget.StandaloneWindows64; return true;
                case "windows": case "windows32": target = BuildTarget.StandaloneWindows; return true;
                case "osx": case "macos": target = BuildTarget.StandaloneOSX; return true;
                case "linux64": case "linux": target = BuildTarget.StandaloneLinux64; return true;
                case "android": target = BuildTarget.Android; return true;
                case "ios": target = BuildTarget.iOS; return true;
                case "webgl": target = BuildTarget.WebGL; return true;
                case "uwp": target = BuildTarget.WSAPlayer; return true;
                case "tvos": target = BuildTarget.tvOS; return true;
                // BuildTarget.VisionOS exists only in Unity 2023.2+ and late 2022.3 patches
#if UNITY_2023_2_OR_NEWER
                case "visionos": target = BuildTarget.VisionOS; return true;
#endif
                default:
                    if (System.Enum.TryParse(name, true, out target))
                        return true;
                    target = default;
                    return false;
            }
        }

        public static BuildTargetGroup GetTargetGroup(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneOSX:
                case BuildTarget.StandaloneLinux64:
                    return BuildTargetGroup.Standalone;
                case BuildTarget.iOS: return BuildTargetGroup.iOS;
                case BuildTarget.Android: return BuildTargetGroup.Android;
                case BuildTarget.WebGL: return BuildTargetGroup.WebGL;
                case BuildTarget.WSAPlayer: return BuildTargetGroup.WSA;
                case BuildTarget.tvOS: return BuildTargetGroup.tvOS;
#if UNITY_2023_2_OR_NEWER
                case BuildTarget.VisionOS: return BuildTargetGroup.VisionOS;
#endif
                default: return BuildTargetGroup.Unknown;
            }
        }

        public static string TryResolveBuildTargetGroup(string name, out BuildTargetGroup targetGroup)
        {
            if (!TryResolveBuildTarget(name, out var buildTarget))
            {
                targetGroup = BuildTargetGroup.Unknown;
                return $"Unknown build target: '{name}'. Valid targets: windows64, osx, linux64, android, ios, webgl, uwp, tvos, visionos";
            }
            targetGroup = GetTargetGroup(buildTarget);
            return null;
        }

        public static string GetDefaultOutputPath(BuildTarget target, string productName)
        {
            string basePath = $"Builds/{target}";
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return $"{basePath}/{productName}.exe";
                case BuildTarget.StandaloneOSX:
                    return $"{basePath}/{productName}.app";
                case BuildTarget.StandaloneLinux64:
                    return $"{basePath}/{productName}.x86_64";
                case BuildTarget.Android:
                    return EditorUserBuildSettings.buildAppBundle
                        ? $"{basePath}/{productName}.aab"
                        : $"{basePath}/{productName}.apk";
                case BuildTarget.iOS:
                case BuildTarget.WebGL:
                    return $"{basePath}/{productName}";
                default:
                    return $"{basePath}/{productName}";
            }
        }

        public static int ResolveSubtarget(string subtarget)
        {
#if UNITY_2021_2_OR_NEWER
            if (string.IsNullOrEmpty(subtarget))
                return (int)StandaloneBuildSubtarget.Player;
            string lower = subtarget.ToLowerInvariant();
            if (lower == "server")
                return (int)StandaloneBuildSubtarget.Server;
            return (int)StandaloneBuildSubtarget.Player;
#else
            return 0;
#endif
        }
    }
}
