using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TheMovies.EditorTools
{
    /// <summary>
    /// Stops Android builds that would start as a flat 2D window on the Quest because OpenXR or
    /// "Meta Quest Support" is not enabled. Tries to fix the settings automatically first.
    /// </summary>
    sealed class QuestBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            if (XRConfigurator.CheckQuest(out string problem) != XRConfigurator.Readiness.NotReady)
                return;

            Debug.LogWarning("[TheMovies] " + problem + " Trying to enable OpenXR / Meta Quest Support automatically.");
            XRConfigurator.Configure();
            if (XRConfigurator.CheckQuest(out problem) != XRConfigurator.Readiness.NotReady)
            {
                throw new BuildFailedException(
                    "The Movies: OpenXR / Meta Quest Support wurde gerade aktiviert. Bitte den Build einfach noch einmal starten.");
            }

            throw new BuildFailedException(
                "The Movies: Die App würde auf der Quest nur als flaches Fenster starten. " + problem +
                " Bitte unter Edit > Project Settings > XR Plug-in Management (Reiter Android) 'OpenXR' anhaken, " +
                "unter OpenXR 'Meta Quest Support' aktivieren und unter 'Project Validation' auf 'Fix All' klicken.");
        }
    }
}
