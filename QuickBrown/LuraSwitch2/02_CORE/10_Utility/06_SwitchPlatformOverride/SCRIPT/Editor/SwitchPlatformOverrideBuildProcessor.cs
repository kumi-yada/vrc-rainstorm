#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace QuickBrown.LuraSwitch.Editor
{
    internal sealed class SwitchPlatformOverrideBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private const string DefineSymbol = "LURASWITCH_BUILD_FORCE_MOBILEPREVIEW";
        private const string LegacyDefineSymbol = "LURASWITCH_BUILD_FORCE_QUESTPREVIEW";

        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report == null)
            {
                return;
            }

            if (IsStandaloneTarget(report.summary.platform))
            {
                return;
            }

            AddDefine(report.summary.platformGroup);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report == null)
            {
                return;
            }

            RemoveDefine(report.summary.platformGroup);
        }

        private static bool IsStandaloneTarget(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneOSX:
                case BuildTarget.StandaloneLinux64:
                    return true;
                default:
                    return false;
            }
        }

        private static void AddDefine(BuildTargetGroup group)
        {
            if (group == BuildTargetGroup.Unknown)
            {
                return;
            }

#pragma warning disable CS0618
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
#pragma warning restore CS0618

            if (string.IsNullOrEmpty(defines))
            {
#pragma warning disable CS0618
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, DefineSymbol);
#pragma warning restore CS0618
                return;
            }

            var parts = defines
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            if (parts.Contains(DefineSymbol))
            {
                return;
            }

            var next = string.Join(";", parts.Concat(new[] { DefineSymbol }));
#pragma warning disable CS0618
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, next);
#pragma warning restore CS0618
        }

        private static void RemoveDefine(BuildTargetGroup group)
        {
            if (group == BuildTargetGroup.Unknown)
            {
                return;
            }

#pragma warning disable CS0618
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
#pragma warning restore CS0618

            if (string.IsNullOrEmpty(defines))
            {
                return;
            }

            var parts = defines
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s) && s != DefineSymbol && s != LegacyDefineSymbol)
                .ToArray();

            var next = string.Join(";", parts);
#pragma warning disable CS0618
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, next);
#pragma warning restore CS0618
        }
    }
}
#endif
