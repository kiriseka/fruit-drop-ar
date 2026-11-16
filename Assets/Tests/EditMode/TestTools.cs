using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// Development tool. Runs the automated tests from a menu item, EditMode first and then PlayMode, and writes a
/// short result to Temp/TestResults.log that ends with "Tests done". The Test Runner window shows the same tests.
/// </summary>
[InitializeOnLoad]
public static class TestTools
{
    const string k_LogPath = "Temp/TestResults.log";
    const string k_ThenPlayModeKey = "TestTools.ThenPlayMode";

    static readonly TestRunnerApi s_Api;

    // Registered again after every script reload, because a PlayMode run reloads scripts on its way in.
    static TestTools()
    {
        s_Api = ScriptableObject.CreateInstance<TestRunnerApi>();
        s_Api.RegisterCallbacks(new Callbacks());
    }

    [MenuItem("Fruit Drop/Run Tests")]
    static void RunAll()
    {
        File.WriteAllText(k_LogPath, "");
        SessionState.SetBool(k_ThenPlayModeKey, true);
        Run(TestMode.EditMode);
    }

    static void Run(TestMode mode)
    {
        s_Api.Execute(new ExecutionSettings(new Filter { testMode = mode }));
    }

    class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun)
        {
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (!result.HasChildren && result.TestStatus == TestStatus.Failed)
                File.AppendAllText(k_LogPath, $"FAILED {result.Test.FullName}: {result.Message}\n");
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            bool wasEditMode = result.Test.TestMode == TestMode.EditMode;
            File.AppendAllText(k_LogPath,
                $"{(wasEditMode ? "EditMode" : "PlayMode")}: {result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped, {result.Duration:F1} s\n");

            if (wasEditMode && SessionState.GetBool(k_ThenPlayModeKey, false))
            {
                SessionState.EraseBool(k_ThenPlayModeKey);

                // On the next update, once this run has been wound up.
                EditorApplication.CallbackFunction next = null;
                next = () =>
                {
                    EditorApplication.update -= next;
                    Run(TestMode.PlayMode);
                };
                EditorApplication.update += next;
                return;
            }

            File.AppendAllText(k_LogPath, "Tests done\n");
        }
    }
}
