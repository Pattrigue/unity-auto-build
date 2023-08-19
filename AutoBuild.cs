using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AutoBuild
{
    public sealed class AutoBuild : EditorWindow
    {
        private readonly Dictionary<BuildTarget, bool> targetsToBuild = new Dictionary<BuildTarget, bool>();
        private readonly List<BuildTarget> availableTargets = new List<BuildTarget>();
        
        [MenuItem("Tools/Auto Build")]
        public static void OnShowTools() => GetWindow<AutoBuild>("Auto Build", true);

        private static BuildTargetGroup GetTargetGroupForTarget(BuildTarget target) => target switch
        {
            BuildTarget.StandaloneOSX => BuildTargetGroup.Standalone,
            BuildTarget.StandaloneWindows => BuildTargetGroup.Standalone,
            BuildTarget.StandaloneWindows64 => BuildTargetGroup.Standalone,
            BuildTarget.StandaloneLinux64 => BuildTargetGroup.Standalone,
            BuildTarget.iOS => BuildTargetGroup.iOS,
            BuildTarget.Android => BuildTargetGroup.Android,
            BuildTarget.WebGL => BuildTargetGroup.WebGL,
            _ => BuildTargetGroup.Unknown
        };

        private static string GetBuildTargetDisplayName(BuildTarget target) => target switch
        {
            BuildTarget.StandaloneOSX => "Mac OS X",
            BuildTarget.StandaloneWindows => "Windows 32-bit",
            BuildTarget.StandaloneWindows64 => "Windows 64-bit",
            BuildTarget.StandaloneLinux64 => "Linux 64-bit",
            BuildTarget.iOS => "iOS",
            BuildTarget.Android => "Android",
            BuildTarget.WebGL => "WebGL",
            _ => target.ToString()
        };

        private void OnEnable()
        {
            availableTargets.Clear();
            
            Array buildTargets = Enum.GetValues(typeof(BuildTarget));
            
            foreach (object buildTargetValue in buildTargets)
            {
                BuildTarget target = (BuildTarget)buildTargetValue;

                // skip if unsupported
                if (!BuildPipeline.IsBuildTargetSupported(GetTargetGroupForTarget(target), target))
                {
                    continue;
                }

                availableTargets.Add(target);

                // add the target if not in the build list
                targetsToBuild.TryAdd(target, true);
            }

            // check if any targets have gone away
            if (targetsToBuild.Count > availableTargets.Count)
            {
                // build the list of removed targets
                List<BuildTarget> targetsToRemove = new List<BuildTarget>();
                
                foreach (BuildTarget target in targetsToBuild.Keys)
                {
                    if (!availableTargets.Contains(target))
                    {
                        targetsToRemove.Add(target);
                    }
                }

                // cleanup the removed targets
                foreach (BuildTarget target in targetsToRemove)
                {
                    targetsToBuild.Remove(target);
                }
            }
        }

        private void OnGUI()
        {
            // GUIStyle for a gray box
            GUIStyle grayBoxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(10, 10, 10, 10)
            };

            // GUIStyle for the large bold label
            GUIStyle largeBoldLabel = new GUIStyle(EditorStyles.largeLabel)
            {
                fontStyle = FontStyle.Bold
            };

            EditorGUILayout.BeginVertical(grayBoxStyle);  

            // Auto build title
            GUILayout.Label("Auto Build", largeBoldLabel);
            EditorGUILayout.Space(5f);

            GUILayout.Label("Platforms to Build", EditorStyles.boldLabel);

            // Display the build targets
            int numEnabled = 0;

            foreach (BuildTarget target in availableTargets)
            {
                string displayName = GetBuildTargetDisplayName(target);

                targetsToBuild[target] = EditorGUILayout.Toggle(displayName, targetsToBuild[target]);

                if (targetsToBuild[target])
                {
                    numEnabled++;
                }
            }
            
            EditorGUILayout.Space(5f);

            EditorUserBuildSettings.development = EditorGUILayout.Toggle("Development Build", EditorUserBuildSettings.development);
            
            if (numEnabled > 0)
            {
                // Attempt to build?
                string prompt = numEnabled == 1 ? "BUILD 1 PLATFORM" : $"BUILD {numEnabled} PLATFORMS";
                EditorGUILayout.Space(5f);
                
                GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 16,
                    fixedHeight = 40, 
                    fontStyle = FontStyle.Bold 
                };

                if (GUILayout.Button(prompt, buttonStyle))
                {
                    List<BuildTarget> selectedTargets = new List<BuildTarget>();

                    foreach (var target in availableTargets)
                    {
                        if (targetsToBuild[target])
                        {
                            selectedTargets.Add(target);
                        }
                    }

                    EditorCoroutineUtility.StartCoroutine(PerformBuild(selectedTargets), this);
                }
            }

            EditorGUILayout.EndVertical(); 
        }

        private static IEnumerator PerformBuild(IReadOnlyList<BuildTarget> targetsToBuild)
        {
            // show the progress display
            int buildAllProgressID = Progress.Start("Build All", "Building all selected platforms", Progress.Options.Sticky);
            Progress.ShowDetails();
            
            yield return new EditorWaitForSeconds(1f);

            BuildTarget originalTarget = EditorUserBuildSettings.activeBuildTarget;

            // build each target
            for (int targetIndex = 0; targetIndex < targetsToBuild.Count; ++targetIndex)
            {
                BuildTarget buildTarget = targetsToBuild[targetIndex];

                Progress.Report(buildAllProgressID, targetIndex + 1, targetsToBuild.Count);
                int buildTaskProgressID = Progress.Start($"Build {GetBuildTargetDisplayName(buildTarget)}", null, Progress.Options.Sticky, buildAllProgressID);
                
                yield return new EditorWaitForSeconds(1f);

                // perform the build
                if (!BuildIndividualTarget(buildTarget))
                {
                    Progress.Finish(buildTaskProgressID, Progress.Status.Failed);
                    Progress.Finish(buildAllProgressID, Progress.Status.Failed);

                    if (EditorUserBuildSettings.activeBuildTarget != originalTarget)
                    {
                        EditorUserBuildSettings.SwitchActiveBuildTargetAsync(GetTargetGroupForTarget(originalTarget), originalTarget);
                    }

                    yield break;
                }

                Progress.Finish(buildTaskProgressID);
                
                yield return new EditorWaitForSeconds(1f);
            }

            Progress.Finish(buildAllProgressID);

            if (EditorUserBuildSettings.activeBuildTarget != originalTarget)
            {
                EditorUserBuildSettings.SwitchActiveBuildTargetAsync(GetTargetGroupForTarget(originalTarget), originalTarget);
            }
            
            yield return null;
        }

        private static bool BuildIndividualTarget(BuildTarget target)
        {
            BuildPlayerOptions options = new BuildPlayerOptions();

            // get the list of scenes
            List<string> scenes = new List<string>();
            
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    scenes.Add(scene.path);
                }
            }

            string folderName = $"{PlayerSettings.productName} {GetBuildTargetDisplayName(target)}";
                
            // configure the build
            options.scenes = scenes.ToArray();
            options.target = target;
            options.targetGroup = GetTargetGroupForTarget(target);
            
            // set the location path name
            if (target is BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64)
            {
                options.locationPathName = Path.Combine("Builds", folderName, $"{PlayerSettings.productName}.exe");
            }
            else if (target == BuildTarget.StandaloneLinux64)
            {
                options.locationPathName = Path.Combine("Builds", folderName, $"{PlayerSettings.productName}.x86_64");
            }
            else if (target == BuildTarget.StandaloneOSX)
            {
                options.locationPathName = Path.Combine("Builds", folderName, $"{PlayerSettings.productName}.app");
            }
            else
            {
                options.locationPathName = Path.Combine("Builds", folderName, PlayerSettings.productName);
            }

            options.options = BuildPipeline.BuildCanBeAppended(target, options.locationPathName) == CanAppendBuild.Yes 
                ? BuildOptions.AcceptExternalModificationsToPlayer 
                : BuildOptions.None;

            // start the build
            if (!Directory.Exists(Path.GetDirectoryName(options.locationPathName)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(options.locationPathName)!);
            }
            
            EditorUserBuildSettings.SetBuildLocation(target, options.locationPathName);
            BuildReport report = BuildPipeline.BuildPlayer(options);

            // was the build successful?
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"Build for {target.ToString()} completed in {report.summary.totalTime.Seconds} seconds");
                return true;
            }

            Debug.LogError($"Build for {target.ToString()} failed");
        
            return false;
        }
    }
}
