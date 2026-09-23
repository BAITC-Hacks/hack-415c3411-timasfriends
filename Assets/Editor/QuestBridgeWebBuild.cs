using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace QuestBridge.Editor
{
    /// <summary>Reproducible first browser build, with no web-server compression dependency.</summary>
    public static class QuestBridgeWebBuild
    {
        public const string ScenePath = "Assets/QuestBridge/QuestBridge.unity";
        public const string OutputPath = "Builds/WebGL";
        public const string StatusPath = "Temp/questbridge-web-build.json";
        static bool queued;

        [Serializable]
        sealed class BuildStatus
        {
            public string state, output, startedUtc, finishedUtc, error;
            public ulong bytes;
            public int errors, warnings;
        }

        // Useful for live-Editor automation: return before the blocking BuildPipeline call begins.
        public static string QueuePreview()
        {
            RequireReady();
            if (queued) throw new InvalidOperationException("A QuestBridge web build is already queued.");
            queued = true;
            EditorApplication.delayCall += () =>
            {
                queued = false;
                try { BuildPreview(); }
                catch (Exception exception) { Debug.LogException(exception); }
            };
            return "Queued QuestBridge WebGL build: " + Path.GetFullPath(OutputPath);
        }

        [MenuItem("QuestBridge/Build Web Preview")]
        public static void BuildPreview()
        {
            RequireReady();
            var status = new BuildStatus
            {
                state = "building", output = Path.GetFullPath(OutputPath),
                startedUtc = DateTime.UtcNow.ToString("O")
            };
            WriteStatus(status);
            try
            {
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                    throw new BuildFailedException("Install WebGL Build Support for Unity 6000.3.7f1.");
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                    throw new BuildFailedException("Switch the active build platform to WebGL, let scripts compile, then build again.");
                if (!File.Exists(ScenePath)) throw new BuildFailedException("QuestBridge scene is missing.");

                PlayerSettings.WebGL.template = "PROJECT:QuestBridge";
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
                PlayerSettings.WebGL.decompressionFallback = false;
                PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
                PlayerSettings.WebGL.dataCaching = true;
                PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
                PlayerSettings.defaultWebScreenWidth = 1600;
                PlayerSettings.defaultWebScreenHeight = 900;
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory(OutputPath);

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath }, locationPathName = OutputPath,
                    target = BuildTarget.WebGL, options = BuildOptions.None
                });
                status.bytes = report.summary.totalSize;
                status.errors = (int)report.summary.totalErrors;
                status.warnings = (int)report.summary.totalWarnings;
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException("WebGL build ended with " + report.summary.result + ". See the Unity Console.");
                status.state = "succeeded";
                Debug.Log("QuestBridge WebGL ready: " + status.output + " (" + status.bytes + " bytes). Run scripts/serve_web.py.");
            }
            catch (Exception exception)
            {
                status.state = "failed";
                status.error = exception.Message;
                throw;
            }
            finally
            {
                status.finishedUtc = DateTime.UtcNow.ToString("O");
                WriteStatus(status);
            }
        }

        static void RequireReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before building QuestBridge.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Wait for Unity compilation/import or the current build to finish.");
        }

        static void WriteStatus(BuildStatus status)
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(StatusPath, JsonUtility.ToJson(status, true));
        }
    }
}
