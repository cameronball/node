//#define DISABLE_UNITY_IAP_INSTALLER
//#define DEBUG_IGNORE_IAPSERVICE_CHECK

// GUI is required for successful installation in some Unitys.
// TODO Test & disable if after 2020_1_OR_NEWER
// Works around release-management bug where some Unity 2019 releases may lack UNITY_2018_4_OR_NEWER symbol
#if UNITY_2018_4_OR_NEWER || UNITY_2019_1_OR_NEWER || (UNITY_5_3_OR_NEWER && !UNITY_5_6_OR_NEWER)
// Install interactive: IAP Installer GUI requires user to drive installs.
#define USE_GUI_UNITY_IAP_INSTALLER
// Whether to ask the AssetDatabase to import interactively or not.
#define FORCE_INTERACTIVE_INSTALL
// #define DEBUG_SHOW_MENU_MANUAL_GUI
// #define DEBUG_USE_TEST_ASSETS
#endif // UNITY_2018_3_OR_NEWER

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.Serialization;
using Assert = UnityEngine.Assertions.Assert;

namespace UnityEditor.Purchasing
{
    static class UnityIAPInstaller
    {
        internal static bool m_Trace = false; // Logging
        private static bool m_Preview = false; // Execute steps without deleting files

        static readonly string k_ServiceName = "IAP";
        static readonly string k_PackageName = "Unity IAP";

// The initial prompt delays the AssetDatabase.ImportAssetPackage call until after all
// assemblies are loaded. Without this delay in Unity 5.5 (on Windows specifically),
// the editor crashes when the method is called with DidReloadScripts.
#if UNITY_EDITOR_WIN && UNITY_5_5_OR_NEWER
        static bool m_EnableInstallerPrompt = true;
#else
        static bool m_EnableInstallerPrompt = false;
#endif

#if FORCE_INTERACTIVE_INSTALL
        private static readonly bool k_ForceInteractiveInstall = true;
#else
        private static readonly bool k_ForceInteractiveInstall = false;
#endif

        /// <summary>
        /// Artifact stores files and custom commands required to install project components of Unity IAP.
        /// </summary>
        internal class Artifact
        {
            private readonly string _relativePath;
            private readonly Func<bool> _shouldInstall;
            private readonly bool _installInteractively;
            private readonly string _displayName;

            public Artifact(string relativePath, Func<bool> shouldInstall, bool installInteractively, string displayName)
            {
                _relativePath = relativePath;
                _shouldInstall = shouldInstall;
                _installInteractively = installInteractively;
                _displayName = displayName;
            }

            public bool ShouldInstall()
            {
                return _shouldInstall == null || _shouldInstall();
            }

            public bool CanInstall()
            {
                return AssetExists(AssetPath());
            }

            public string AssetPath()
            {
                return GetAssetPath(_relativePath);
            }

            /// <summary>
            /// Whether to display import dialog. Is overridable using k_ForceInteractiveInstall.
            /// </summary>
            /// <returns></returns>
            public bool InstallInteractively()
            {
                return k_ForceInteractiveInstall || _installInteractively;
            }

            public string DisplayName()
            {
                return _displayName;
            }

            public override string ToString()
            {
                return "Artifact: RelativePath=" + _relativePath + " ShouldInstall=" + ShouldInstall();
            }
        }

        /// <summary>
        /// Install assets.
        /// NOTE: For Unity LESS THAN OR EQUAL TO 5.4, only the last Artifact can be interactive, else it will interrupt
        /// a latter-listed artifact. This is due to the Installer having access to AssetDatabase.ImportPackageCallback
        /// and ImportPackageFailedCallback only on Unity 5.5+.
        /// </summary>
        internal static readonly Artifact[] k_Artifacts =
        {
#if DEBUG_USE_TEST_ASSETS
            new Artifact("Resources/two.unitypackage", null, false, "Unity IAP"),
#else
            // E.g.: new Artifact("Sample.unitypackage", () => ShouldInstallSamplePackage() == true, false),
            // NOTE: Install ALL packages "interactively" otherwise face crashing in 2018.4. TBD whether we remove this functionality.
            new Artifact("Plugins/UnityPurchasing/UnityIAP.unitypackage", null, true, "Unity IAP"),
#endif
        };

        static readonly string k_InstallerFile = "Plugins/UnityPurchasing/Editor/UnityIAPInstaller.cs";
        static readonly string k_ObsoleteFilesCSVFile = "Plugins/UnityPurchasing/Editor/ObsoleteFilesOrDir.csv";
        static readonly string k_ObsoleteFilesCSVPostFile = "Plugins/UnityPurchasing/Editor/ObsoleteFilesOrDirPost.csv";
        static readonly string k_ObsoleteGUIDsCSVFile = "Plugins/UnityPurchasing/Editor/ObsoleteGUIDs.csv";
        static readonly string k_IAPHelpURL = "https://docs.unity3d.com/Manual/UnityIAPSettingUp.html";
        static readonly string k_ProjectHelpURL = "https://docs.unity3d.com/Manual/SettingUpProjectServices.html";

        /// <summary>
        /// Install step.
        /// Prevent multiple simultaneous installs
        ///  0 or none   = installation not started
        ///  1           = installation starting
        ///  2 or higher = installing artifact (2 - thisIndex)
        /// </summary>
        static readonly string k_PrefsKey_ImportingAssetPackage = "UnityIAPInstaller_ImportingAssetPackage";
        static readonly string k_PrefsKey_LastAssetPackageImport = "UnityIAPInstaller_LastAssetPackageImportDateTimeBinary";
        static readonly double k_MaxLastImportReasonableTicks = 60 * 10000000; // Installs started n seconds from 'now' are not considered 'simultaneous'

        static readonly string[] k_ObsoleteFilesOrDirectories = GetFromCSV(GetAbsoluteFilePath(k_ObsoleteFilesCSVFile));
        static readonly string[] k_ObsoleteFilesOrDirectoriesPost = GetFromCSV(GetAbsoluteFilePath(k_ObsoleteFilesCSVPostFile));
        static readonly string[] k_ObsoleteGUIDs = GetFromCSV(GetAbsoluteFilePath(k_ObsoleteGUIDsCSVFile));

        static readonly bool k_RunningInBatchMode = Environment.CommandLine.ToLower().Contains(" -batchmode");

#if USE_GUI_UNITY_IAP_INSTALLER
        static readonly bool k_UsingGUI = true;
#else
        static readonly bool k_UsingGUI = false;
#endif

        private static bool HasUnityPurchasing()
        {
            return DoesMethodExist("UnityPurchasing", "Initialize");
        }

#if UNITY_5_3 || UNITY_5_3_OR_NEWER
        static readonly bool k_IsIAPSupported = true;
#else
        static readonly bool k_IsIAPSupported = false;
#endif

#if UNITY_5_5_OR_NEWER && false // Service window prevents this from working properly. Disabling for now.
        static readonly bool k_IsEditorSettingsSupported = true;
#else
        static readonly bool k_IsEditorSettingsSupported = false;
#endif

        private enum DialogKind
        {
            Installer = 0,
            CanceledInstaller,
            ProjectConfig,
            EnableService,
            EnableServiceManually,
            CanceledEnableService,
            DeleteAssets,
            CanceledDeleteAssets,
            UnityRequirement,
            MissingPackage,
        }

        private class Dialog
        {
            public Dialog(DialogKind kind, string[] fields)
            {
                _kind = kind;
                _fields = fields;
            }

            private readonly DialogKind _kind;
            private readonly string[] _fields;

            public static bool DisplayDialog(DialogKind dialogKind)
            {
                var dialog = k_Dialogs[(int)dialogKind];
                Assert.AreEqual(dialog._kind, dialogKind);

                if (k_RunningInBatchMode)
                {
                    if (dialog._fields[0] != null && dialog._fields[0].Length != 0)
                    {
                        Debug.Log(dialog._fields[0]);
                    }
                    return true;
                }

                return EditorUtility.DisplayDialog(
                    dialog._fields[1],
                    dialog._fields[2],
                    dialog._fields[3],
                    dialog._fields[4]);
            }
        }

        private static readonly Dialog[] k_Dialogs =
        {
            new Dialog(DialogKind.Installer, new [] {
                null,
                k_PackageName + " Installer",
                "The " + k_PackageName + " installer will determine if your project is configured properly " +
                "before importing the " + k_PackageName + " asset package.\n\n" +
                "Would you like to run the " + k_PackageName + " installer now?",
                "Install Now",
                "Cancel",
            }),
            new Dialog(DialogKind.CanceledInstaller, new [] {
                string.Format("User declined to run the {0} installer. Canceling installer process now...", k_PackageName),
                k_PackageName + " Installer",
                "The " + k_PackageName + " installer has been canceled. " +
                "Please import the " + k_PackageName + " asset package again to continue the install.",
                "OK",
                null,
            }),
            new Dialog(DialogKind.ProjectConfig, new [] {
                "Unity Project ID is not currently set. Canceling installer process now...",
                k_PackageName + " Installer",
                "A Unity Project ID is not currently configured for this project.\n\n" +
                "Before the " + k_ServiceName + " service can be enabled, a Unity Project ID must first be " +
                "linked to this project. Once linked, please import the " + k_PackageName + " asset package again" +
                "to continue the install.\n\n" +
                "Select 'Help...' to see further instructions.",
                "OK",
                "Help...",
            }),
            new Dialog(DialogKind.EnableService, new [] {
                string.Format("The {0} service is currently disabled. Enabling the {0} Service now...", k_ServiceName),
                k_PackageName + " Installer",
                "The " + k_ServiceName + " service is currently disabled.\n\n" +
                "To avoid encountering errors when importing the " + k_PackageName + " asset package, " +
                "the " + k_ServiceName + " service must be enabled first before importing the latest " +
                k_PackageName + " asset package.\n\n" +
                "Would you like to enable the " + k_ServiceName + " service now?",
                "Enable Now",
                "Cancel",
            }),
            new Dialog(DialogKind.EnableServiceManually, new [] {
                string.Format("The {0} service is currently disabled. Canceling installer process now...", k_ServiceName),
                k_PackageName + " Installer",
                "The " + k_ServiceName + " service is currently disabled.\n\n" +
                "Canceling the install process now to avoid encountering errors when importing the " +
                k_PackageName + " asset package. The " + k_ServiceName + " service must be enabled first " +
                "before importing the latest " + k_PackageName + " asset package.\n\n" +
                "Please enable the " + k_ServiceName + " service through the Services window. " +
                "Then import the " + k_PackageName + " asset package again to continue the install.\n\n" +
                "Select 'Help...' to see further instructions.",
                "OK",
                "Help...",
            }),
            new Dialog(DialogKind.CanceledEnableService, new [] {
                string.Format("User declined to enable the {0} service. Canceling installer process now...", k_ServiceName),
                k_PackageName + " Installer",
                "The " + k_PackageName + " installer has been canceled.\n\n" +
                "Please enable the " + k_ServiceName + " service through the Services window. " +
                "Then import the " + k_PackageName + " asset package again to continue the install.\n\n" +
                "Select 'Help...' to see further instructions.",
                "OK",
                "Help...",
            }),
            new Dialog(DialogKind.DeleteAssets, new [] {
                string.Format("Found obsolete {0} assets. Deleting obsolete assets now...", k_PackageName),
                k_PackageName + " Installer",
                "Found obsolete assets from an older version of the " + k_PackageName + " asset package.\n\n" +
                "The Installer must remove these assets. Note that you may see warning and error messages until installation completes.\n\n" +
                "Would you like to remove these obsolete " + k_PackageName + " assets now?",
                "Delete Now",
                "Cancel",
            }),
            new Dialog(DialogKind.CanceledDeleteAssets, new [] {
                string.Format("User declined to remove obsolete {0} assets. Canceling installer process now...", k_PackageName),
                k_PackageName + " Installer",
                "The " + k_PackageName + " installer has been canceled.\n\n" +
                "Please delete any previously imported " + k_PackageName + " assets from your project. " +
                "Then import the " + k_PackageName + " asset package again to continue the install.",
                "OK",
                null,
            }),
            new Dialog(DialogKind.UnityRequirement, new [] {
                "Installer requires Unity 5.3 or higher, cancelling now...",
                k_PackageName + " Installer",
                "The " + k_PackageName + " installer has been canceled.\n\n" +
                "Requires Unity 5.3 or higher.",
                "OK",
                null,
            }),
            new Dialog(DialogKind.MissingPackage, new [] {
                "Installer corrupt, missing package. Cancelling now...",
                k_PackageName + " Installer",
                "The " + k_PackageName + " installer has been canceled.\n\n" +
                "This installer is corrupt, and is missing one or more unitypackages.",
                "OK",
                null,
            }),
        };

#if !DISABLE_UNITY_IAP_INSTALLER
        [Callbacks.DidReloadScripts]
#endif
        /// <summary>
        /// * Install may be called multiple times during the AssetDatabase.ImportPackage
        ///   process. Detect this and avoid restarting installation.
        /// * Install may fail unexpectedly in the middle due to crash. Detect
        ///   this heuristically with a timestamp, deleting mutex for multiple
        ///   install detector.
        /// </summary>
        private static void Install()
        {
            if (m_Trace) Debug.Log("(FIRE DidReloadScripts) Install: B4");

            // Detect and fix interrupted installation
            FixInterruptedInstall();

            if (k_RunningInBatchMode)
            {
                Debug.LogFormat("Preparing to install the {0} asset package...", k_PackageName);
            }

            // Detect multiple calls to this method and ignore
            if (HasInstallStep())
            {
                // Resume installing
                if (k_ForceInteractiveInstall)
                {
                    if (m_Trace) Debug.Log("Install: continuing interactive install");
                }
                else
                {
                    if (m_Trace) Debug.Log("Install: subscribing to callbacks");
                    SubscribeCallbacks();
                }
            }
            else if (!k_IsIAPSupported)
            {
                // Abort if IAP is not available for this version of Unity
                Dialog.DisplayDialog(DialogKind.UnityRequirement);
                OnComplete(false);
            }
            else if (m_EnableInstallerPrompt && !Dialog.DisplayDialog(DialogKind.Installer))
            {
                Dialog.DisplayDialog(DialogKind.CanceledInstaller);
                OnComplete(false);
            }
            else if (!DeleteObsoleteAssets(k_ObsoleteFilesOrDirectories, k_ObsoleteGUIDs, false))
            {
                OnComplete(false);
            }
            else if (!EnablePurchasingService())
            {
                OnComplete(false);
            }
            else
            {
                // Start installing

                if (k_UsingGUI)
                {
                    ManualInstallerWindow.Init();
                }
                else
                {
                    if (m_Trace) Debug.Log("Install() -> about to import package ...");

                    SetInstallStep(1);
                    OnStep();
                }
            }

            if (m_Trace) Debug.Log("Install: AF");
        }

        internal static bool HasInstallStep()
        {
            return PlayerPrefs.HasKey(k_PrefsKey_ImportingAssetPackage);
        }

        /// <returns>See k_PrefsKey_ImportingAssetPackage</returns>
        internal static int GetInstallStep()
        {
            return PlayerPrefs.GetInt(k_PrefsKey_ImportingAssetPackage);
        }

        /// <summary>
        /// Records the fact the installation started for a given asset
        /// </summary>
        /// <param name="installStep">See k_PrefsKey_ImportingAssetPackage</param>
        internal static int SetInstallStep(int installStep)
        {
            if (m_Trace) Debug.Log("SetInstallStep: " + installStep);
            PlayerPrefs.SetInt(k_PrefsKey_ImportingAssetPackage, installStep);

            return installStep;
        }

        private static void ClearInstallStep()
        {
            PlayerPrefs.DeleteKey(k_PrefsKey_ImportingAssetPackage);
        }

        /// <summary>
        /// Resubscribe to "I'm done installing" callback on each Reload, as it's lost on each Reload.
        /// </summary>
        private static void SubscribeCallbacks()
        {
            if (m_Trace) Debug.Log("UnityIAPInstaller.SubscribeCallbacks");
            EditorApplication.delayCall += OnStepCallback;
        }

        private static void UnsubscribeCallbacks()
        {
            if (m_Trace) Debug.Log("UnityIAPInstaller.UnsubscribeCallbacks");
            EditorApplication.delayCall -= OnStepCallback;
        }

        /// <summary>
        /// Detects and fixes the interrupted install.
        /// </summary>
        private static void FixInterruptedInstall()
        {
            if (!PlayerPrefs.HasKey(k_PrefsKey_LastAssetPackageImport))
            {
                if (m_Trace) Debug.Log("FixInterruptedInstall - returning, not changing anything");
                return;
            }

            var lastImportDateTimeBinary = PlayerPrefs.GetString(k_PrefsKey_LastAssetPackageImport);

            long lastImportLong = 0;
            try {
                lastImportLong = Convert.ToInt64(lastImportDateTimeBinary);
            } catch (SystemException) {
                // Ignoring exception converting long
                // By default '0' value will trigger install-cleanup
            }

            var lastImport = DateTime.FromBinary(lastImportLong);
            double dt = Math.Abs(DateTime.UtcNow.Ticks - lastImport.Ticks);

            if (dt > k_MaxLastImportReasonableTicks)
            {
                Debug.Log("Installer detected interrupted installation (" + dt / 10000000 + " seconds ago). Reenabling install.");

                // Fix it!
                PlayerPrefs.DeleteKey(k_PrefsKey_ImportingAssetPackage);
                PlayerPrefs.DeleteKey(k_PrefsKey_LastAssetPackageImport);
            }

            // else dt is not too large, installation okay to proceed
            if (m_Trace) Debug.Log("FixInterruptedInstall - continuing, installing and not 'interrupted'");
        }

        public static void OnStepCallback()
        {
            if (m_Trace) Debug.Log("OnStepCallback B4");
            UnsubscribeCallbacks();
            OnStep();
            if (m_Trace) Debug.Log("OnStepCallback A4");
        }

        /// <summary>
        /// Kicks off installation, depending upon step-counter
        /// </summary>
        internal static void OnStep()
        {
            if (m_Trace) Debug.Log("OnStep B4");

            var isInstalling = false;

            // can be zero, or any of the indices in k_PackageFiles (+1)
            var importStep = GetInstallStep(); // TODO encapsulate read/write
            var nextImportStep = importStep + 1; // 1, 2, ...

            var packageIndex = importStep - 1; // 0, 1, ...

            if (m_Trace) Debug.LogFormat("OnStep packageIndex={0}", packageIndex);

            // Install if there's a package yet to be installed, then try to install it
            if (packageIndex >= 0 && packageIndex <= k_Artifacts.Length - 1)
            {
                var artifact = k_Artifacts[packageIndex];

                if (m_Trace) Debug.LogFormat("OnStep: Installing: packageIndex={0} artifact={1}", packageIndex, artifact);

                if (artifact.CanInstall())
                {
                    // Record fact installation has started
                    SetInstallStep(nextImportStep);
                    // Record time installation started
                    PlayerPrefs.SetString(k_PrefsKey_LastAssetPackageImport, DateTime.UtcNow.ToBinary().ToString());

                    if (artifact.ShouldInstall())
                    {
                        if (m_Trace) Debug.Log("OnStep: Artifact.ShouldInstall passed: importing ...");

                        // Start async ImportPackage operation, causing one or more
                        // Domain Reloads as a side-effect
                        if (k_RunningInBatchMode)
                        {
                            AssetDatabase.ImportPackage(artifact.AssetPath(), false); // Batch mode is not interactive
                        }
                        else
                        {
                            var interactive = artifact.InstallInteractively();
                            AssetDatabase.ImportPackage(artifact.AssetPath(), interactive);
                        }

                        if (!k_ForceInteractiveInstall)
                        {
                            // TODO keep or disable this #if  ???
                            // All in-memory values hereafter may be cleared due to Domain
                            // Reloads by async ImportPackage operation
                            SubscribeCallbacks();
                        }
                    }
                    else
                    {
                        if (m_Trace) Debug.Log("OnStep: Artifact.ShouldInstall failed: moving on to next artifact");

                        OnStep(); // WARNING: recursion
                    }

                    isInstalling = true;
                }
                else
                {
                    Dialog.DisplayDialog(DialogKind.MissingPackage);
                }
            }

            if (!isInstalling)
            {
                // No more packages to be installed
                OnComplete();
            }

            if (m_Trace) Debug.Log("OnStep AF");
        }

        /// <summary>
        /// Cleanup the installation. Remove subset of "obsolete" files which are to only be removed post-install.
        /// </summary>
        /// <param name="deleteObsoleteFilesPost">default 'true', set to false to skip deleting obsolete 'post install' files</param>
        internal static void OnComplete(bool deleteObsoleteFilesPost = true)
        {
            if (HasInstallStep())
            {
                // Cleanup mutexes for next install
                PlayerPrefs.DeleteKey(k_PrefsKey_ImportingAssetPackage);
                PlayerPrefs.DeleteKey(k_PrefsKey_LastAssetPackageImport);

                if (k_RunningInBatchMode)
                {
                    Debug.LogFormat("Successfully imported the {0} asset package.", k_PackageName);
                }
            }

            if (k_RunningInBatchMode)
            {
                Debug.LogFormat("Deleting {0} package installer files...", k_PackageName);
            }

            if (m_Preview)
            {
                Debug.Log("Preview: delete artifact assets here");
            }
            else
            {
                // Some assets with tricky dependency requirements should be deleted POST install. Delete them here.
                if (deleteObsoleteFilesPost)
                {
                    DeleteObsoleteAssets(k_ObsoleteFilesOrDirectoriesPost, null, true);
                }

                foreach (var asset in k_Artifacts)
                {
                    AssetDatabase.DeleteAsset(asset.AssetPath());
                }

                AssetDatabase.DeleteAsset(GetAssetPath(k_InstallerFile));
                AssetDatabase.DeleteAsset(GetAssetPath(k_ObsoleteFilesCSVFile));
                AssetDatabase.DeleteAsset(GetAssetPath(k_ObsoleteFilesCSVPostFile));
                AssetDatabase.DeleteAsset(GetAssetPath(k_ObsoleteGUIDsCSVFile));

                AssetDatabase.Refresh();
                SaveAssets();
            }

            if (k_RunningInBatchMode)
            {
                Debug.LogFormat("{0} asset package install complete.", k_PackageName);
                EditorApplication.Exit(0);
            }
        }

        /// <summary>
        /// Reduce logspam during exceptions.
        /// </summary>
        private static bool LoggedExceptionDoesMethodExists;

        private static bool DoesMethodExist(string typeName, string methodName)
        {
            return DoesMethodExist(null, typeName, methodName);
        }

        private static bool DoesMethodExist(string namespaceName, string typeName, string methodName)
        {
            try
            {
                IEnumerable<Type> aTypeList;

                if (namespaceName != null)
                {
                    aTypeList = (from assembly in AppDomain.CurrentDomain.GetAssemblies()
                        from type in assembly.GetTypes()
                        where type.Namespace == namespaceName && type.Name == typeName && type.GetMethods().Any(m => m.Name == methodName)
                        select type);
                }
                else
                {
                    aTypeList = (from assembly in AppDomain.CurrentDomain.GetAssemblies()
                        from type in assembly.GetTypes()
                        where type.Name == typeName && type.GetMethods().Any(m => m.Name == methodName)
                        select type);
                }

                var result = aTypeList.FirstOrDefault() != null;
                return result;
            }
            catch (Exception e)
            {
                if (!LoggedExceptionDoesMethodExists)
                {
                    LoggedExceptionDoesMethodExists = true;
                    Debug.LogException(e);
                }

                return false;
            }
        }

#if DEBUG_SHOW_MENU_MANUAL_GUI
        // Alt: Use CMD+I to pop up.
        [MenuItem("Window/Unity IAP/Debug/IAP Installer ... %i")]
        public static void DebugShowGUI()
        {
            ManualInstallerWindow.Init();
        }

        [MenuItem("Window/Unity IAP/Debug/Zero Install Step")]
        public static void DebugZeroInstallStep()
        {
            SetInstallStep(0);
        }

        [MenuItem("Window/Unity IAP/Debug/Clear Install Step")]
        public static void DebugClearInstallStep()
        {
            PlayerPrefs.DeleteKey(k_PrefsKey_ImportingAssetPackage);
        }

        [MenuItem("Window/Unity IAP/Debug/Show Install Step")]
        public static void DebugShowInstallStep()
        {
            if (HasInstallStep())
            {
                Debug.Log("InstallStep: " + GetInstallStep());
            }
            else
            {
                Debug.Log("No InstallStep");
            }
        }
#endif


        private static bool EnablePurchasingService ()
        {
#if DEBUG_IGNORE_IAPSERVICE_CHECK
            var dummy = 1;
            if (dummy == 1)
            {
                return true;
            }
#endif

            if (HasUnityPurchasing())
            {
                // Service is enabled, early return
                return true;
            }

            if (!k_IsEditorSettingsSupported)
            {
                if (!Dialog.DisplayDialog(DialogKind.EnableServiceManually))
                {
                    Application.OpenURL(k_IAPHelpURL);
                }

                return false;
            }

#if UNITY_2018_1_OR_NEWER
            string projectId = CloudProjectSettings.projectId;
#else
            string projectId = PlayerSettings.cloudProjectId;
#endif

            if (string.IsNullOrEmpty(projectId))
            {
                if (!Dialog.DisplayDialog(DialogKind.ProjectConfig))
                {
                    Application.OpenURL(k_ProjectHelpURL);
                }

                return false;
            }

            if (Dialog.DisplayDialog(DialogKind.EnableService))
            {
#if UNITY_5_5_OR_NEWER
                Analytics.AnalyticsSettings.enabled = true;
                PurchasingSettings.enabled = true;
#endif

                SaveAssets();
                return true;
            }

            if (!Dialog.DisplayDialog(DialogKind.CanceledEnableService))
            {
                Application.OpenURL(k_IAPHelpURL);
            }

            return false;
        }

        private static string GetAssetPath (string path)
        {
            return string.Concat("Assets/", path);
        }

        private static string GetAbsoluteFilePath (string path)
        {
            return Path.Combine(Application.dataPath, path.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string[] GetFromCSV (string filePath)
        {
            var lines = new List<string>();
            int row = 0;

            if (File.Exists(filePath))
            {
                try
                {
                    using (var reader = new StreamReader(filePath))
                    {
                        while (!reader.EndOfStream)
                        {
                            var line = reader.ReadLine();
                            if (line == null)
                            {
                                continue;
                            }

                            var splitLines = line.Split(',');
                            lines.Add(splitLines[0].Trim().Trim('"'));
                            row++;
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            return lines.ToArray();
        }

        private static bool AssetExists (string path)
        {
            if (path.Length > 7)
                path = path.Substring(7);
            else return false;

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                path = path.Replace("/", @"\");
            }

            path = Path.Combine(Application.dataPath, path);

            return File.Exists(path) || Directory.Exists(path);
        }

        private static bool AssetsExist (string[] legacyAssetPaths, string[] legacyAssetGUIDs, out string[] existingAssetPaths)
        {
            var paths = new List<string>();

            for (int i = 0; legacyAssetPaths != null && i < legacyAssetPaths.Length; i++)
            {
                if (AssetExists(legacyAssetPaths[i]))
                {
                    paths.Add(legacyAssetPaths[i]);
                }
            }

            for (int i = 0; legacyAssetGUIDs != null && i < legacyAssetGUIDs.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(legacyAssetGUIDs[i]);

                if (AssetExists(path) && !paths.Contains(path))
                {
                    paths.Add(path);
                }
            }

            existingAssetPaths = paths.ToArray();

            return paths.Count > 0;
        }

        /// <summary>
        /// Delete one or more assets represented by path name or GUID. Conditionally present a user-interface around
        /// this process.
        /// </summary>
        /// <param name="paths"></param>
        /// <param name="guids"></param>
        /// <param name="silent">Whether </param>
        /// <returns></returns>
        private static bool DeleteObsoleteAssets (string[] paths, string[] guids, bool silent)
        {
            var assets = new string[0];

            if (!AssetsExist(paths, guids, out assets)) return true;

            if (silent || Dialog.DisplayDialog(DialogKind.DeleteAssets))
            {
                if (m_Preview)
                {
                    Debug.Log("Preview: delete obsolete assets");
                }
                else
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        FileUtil.DeleteFileOrDirectory(assets[i]);
                    }

                    AssetDatabase.Refresh();
                    SaveAssets();
                }

                return true;
            }

            if (!silent)
            {
                Dialog.DisplayDialog(DialogKind.CanceledDeleteAssets);
            }
            return false;
        }

        /// <summary>
        /// Solves issues seen in projects when deleting other files in projects
        /// after installation but before project is closed and reopened.
        /// Script continue to live as compiled entities but are not stored in
        /// the AssetDatabase.
        /// </summary>
        private static void SaveAssets()
        {
            if (m_Trace) Debug.Log("SaveAssets B4");
#if UNITY_5_5_OR_NEWER
            AssetDatabase.SaveAssets(); // Not reliable prior to major refactoring in Unity 5.5.
#else
            EditorApplication.SaveAssets(); // Reliable, but removed in Unity 5.5.
#endif
            if (m_Trace) Debug.Log("SaveAssets AF");
        }
    }

    /// <summary>
    /// Alternative manual unitypackage import controller.
    /// Intended for use when:
    /// * Using Unity 2018.4+ to avoid crashing due to its sensitivity to non-interactive script driven unitypackage
    ///   importation.
    /// * Higher-level pre-checks, for the Unity IAP Installer, have been completed. E.g. the Purchasing Service
    ///   has been confirmed to be enabled.
    /// Supports:
    /// * Using the Artifact.ShouldInstall API to block moving to "next" installation
    /// </summary>
    public class ManualInstallerWindow : EditorWindow
    {
        public bool [] Installed;

        /// <summary>
        /// Whether to disable flow-control, allowing the user to install assets in any order of their choosing.
        /// To workaround unanticipated use-cases.
        /// </summary>
        public bool OverrideInstallFlow;

        /// <summary>
        /// Optimization. Caches the install step so as to avoid repeatedly fetching from disk in a tight loop.
        /// </summary>
        public int LastSeenInstallStep = -1;

        /// <summary>
        /// When the GUI is onscreen and the user is proceeding through an 'obsolete files' cleanup, the domain
        /// will be reloaded. LastUpdate is a temp used to count the duration between the GUI being torn-down, and
        /// the GUI being restored. If the duration is brief, then we will do nothing, because we know that this was
        /// the result of a domain reload. However, if the duration is significant, such as one second, seen in
        /// the k_UserClosedDialogWaitHeuristicSeconds variable, then we assume this was a user-driven GUI tear-down,
        /// and we will cleanup the installer's intermediate files, accordingly.
        /// </summary>
        private static DateTime LastUpdate;
        private static readonly double k_UserClosedDialogWaitHeuristicSeconds = 1;

        public DateTime NextReadTime = DateTime.UtcNow.Add(TimeSpan.FromSeconds(k_StepDonenessTestWaitSeconds));
        private static readonly double k_StepDonenessTestWaitSeconds = 0.5;
        private static bool NeedReadLastInstallStep = true;

        /// <summary>
        /// In UTC
        /// </summary>
        public DateTime LastImportTime;
        private static readonly long ImportDelayTicks = 15000000; // In 100's of nanos. 1 sec == 10000000.

        private static ManualInstallerWindow Instance;
        static readonly string k_ManualInstallerWindowTitle = "Unity IAP Installer";

        public bool NextAssetShouldInstall;

        public static void Init()
        {
            if (UnityIAPInstaller.m_Trace) Debug.Log("ManualInstallerWindow.Init");

            var window = ScriptableObject.CreateInstance(typeof(ManualInstallerWindow)) as ManualInstallerWindow;
            window.titleContent = new GUIContent(k_ManualInstallerWindowTitle);
            window.Installed = new bool[UnityIAPInstaller.k_Artifacts.Length];
            window.ShowUtility();

            // Save this reference for uniquification purposes.
            // On domain-reload the enclosing method may be called, regenerating this window.
            // See the Update method, below, for a mechanism that will close all but one window.
            // Other techniques have undesirable side-effect of leaving one window up, such as EditorWindow.GetWindow().
            Instance = window;

            Instance.minSize = new Vector2(300, 200);
            Instance.maxSize = new Vector2(300, 300);

            NeedReadLastInstallStep = true;

            DescheduleOnDestroyCallback();
        }

        // Internal. See https://github.cds.internal.unity3d.com/unity/game-foundation/pull/97/files for more.
        private static GUIStyle s_SubHeaderStyle;
        private static GUIStyle subHeaderStyle
        {
            get
            {
                if (s_SubHeaderStyle == null)
                {
                    s_SubHeaderStyle = new GUIStyle(GUI.skin.label);
                    s_SubHeaderStyle.wordWrap = true;
                    s_SubHeaderStyle.fontSize = 11;

                }

                return s_SubHeaderStyle;
            }
        }

        void OnGUI()
        {
            // TODO initialize "Install" bool array with current package-index state

            var now = DateTime.UtcNow;

            var slowPollTimeout = now > NextReadTime;

            if (NeedReadLastInstallStep || slowPollTimeout)
            {
                NeedReadLastInstallStep = false;
                NextReadTime = now.Add(TimeSpan.FromSeconds(k_StepDonenessTestWaitSeconds));
                LastSeenInstallStep = UnityIAPInstaller.GetInstallStep();
            }

            GUILayout.Label("Please import unitypackages in sequence", EditorStyles.boldLabel);

            EditorGUILayout.LabelField(
                "Thanks for installing Unity IAP! \n" +
                "\n" +
                "Using the Unity Distribution Platform (UDP) package requires it to be imported separately. \n",
                subHeaderStyle);

            // Compute what is the "next" asset to display
            int nextAsset = -1;
            int i;

            for (i = 0; i < Installed.Length; i++)
            {
                if (Installed[i] == false)
                {
                    nextAsset = i;
                    break;
                }
            }

            // Compute whether the dependencies are fulfilled for the next asset
            if (slowPollTimeout)
            {
                if (nextAsset >= 0 && nextAsset <= UnityIAPInstaller.k_Artifacts.Length - 1)
                {
                    NextAssetShouldInstall = UnityIAPInstaller.k_Artifacts[nextAsset].ShouldInstall();
                }
                else
                {
                    NextAssetShouldInstall = true;
                }
            }

            string nextAssetName = null;
            if (nextAsset >= 0 && nextAsset < UnityIAPInstaller.k_Artifacts.Length)
            {
                nextAssetName = UnityIAPInstaller.k_Artifacts[nextAsset].DisplayName();
            }

            // Compute whether the "Next" text should be disabled

            var nextDisabled = OverrideInstallFlow;
            if (!nextDisabled && now.Ticks - LastImportTime.Ticks < ImportDelayTicks)
            {
                nextDisabled = true;
            }

            if (!nextDisabled && !NextAssetShouldInstall)
            {
                // Disabling next until ShouldInstall is true
                nextDisabled = true;
            }

            if (!OverrideInstallFlow && nextAssetName != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.Space();

                if (!NextAssetShouldInstall)
                {
                    EditorGUILayout.LabelField("Please complete previous package importation ...", subHeaderStyle);
                }
                else
                {
                    var progressString = String.Format("[{0}/{1}]", nextAsset + 1, UnityIAPInstaller.k_Artifacts.Length);
                    EditorGUILayout.LabelField("Click Next to import " + nextAssetName + ". " + progressString, subHeaderStyle);
                }
            }

            // Creates space around the artifact list
            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));

            EditorGUILayout.Space();

            // Compute an 'enabled' flag for each asset

            i = 0;

            // Show the single-artifact import button if we're in override mode
            if (OverrideInstallFlow)
            {
                foreach (var artifact in UnityIAPInstaller.k_Artifacts)
                {
                    // Enable installation of assets if the user wishes to override.
                    var disabled = !OverrideInstallFlow;

                    // Enable installation of the "next" asset only
                    if (disabled && i == nextAsset)
                    {
                        disabled = false;
                    }

#if UNITY_EDITOR_5_4_OR_NEWER
                    using (new EditorGUI.DisabledScope( disabled ))
#else
                    EditorGUI.BeginDisabledGroup(disabled);
#endif
                    {
                        InstallItemGUI(ref Installed[i], i, artifact.DisplayName());
                    }
#if !UNITY_EDITOR_5_4_OR_NEWER
                    EditorGUI.EndDisabledGroup();
#endif

                    i++;
                }
            }
            else
            {
                EditorGUILayout.Space();

                // "Next" button.

                // Extract a "next" rect from the layout engine
                var buttonRect = EditorGUILayout.GetControlRect(null);

                // Set button text based upon next step
                var nextStepText = "Next >>";
                if (nextAsset == -1)
                {
                    nextStepText = "Close, and clean up installer scripts >>";
                }

                bool nextPressed;

                // Locks all installations until the per-install pre-check has passed.
#if UNITY_EDITOR_5_4_OR_NEWER
                using (new EditorGUI.DisabledScope( nextDisabled ))
#else
                EditorGUI.BeginDisabledGroup (nextDisabled);
#endif
                {
                    nextPressed = GUI.Button(buttonRect, nextStepText);
                }
#if !UNITY_EDITOR_5_4_OR_NEWER
                EditorGUI.EndDisabledGroup();
#endif

                if (nextPressed)
                {
                    if (nextAsset == -1)
                    {
                        if (UnityIAPInstaller.m_Trace) Debug.Log("Completed installation, closing dialog");

                        // Terminate GUI. Cleanup installation.

                        Close(); // Calls OnDestroy, below.

                        return;
                    }

                    Installed[nextAsset] = true;
                    InstallAsset(nextAsset);
                }
            }

            EditorGUILayout.EndVertical();

            OverrideInstallFlow = EditorGUILayout.ToggleLeft("Need to go back a step or reinstall? Click here!", OverrideInstallFlow);

            // Overriding clears record of installation
            if (OverrideInstallFlow)
            {
                Installed[0] = false;

                for (i = 0; i < Installed.Length; i++)
                {
                    Installed[i] = false;
                }
            }
        }

        void OnDestroy()
        {
            if (UnityIAPInstaller.m_Trace) Debug.Log("Install GUI Destroyed");

            // Defer
            DelayedOnDestroy();
        }

        /// <summary>
        /// We are either:
        /// * at the end of the lifecycle for the installer and should delete the installer assets, or
        /// * in the middle of reopening this EditorWindow being rebuilt after a domain reload and should do nothing.
        /// </summary>
        void DelayedOnDestroy()
        {
            // Begin polling to check if the window was closed manually
            EditorApplication.delayCall += OnDelayedOnDestroyCallback;
        }

        /// <summary>
        /// Ensure we deschedule ourselves from the "are we done yet" callback. That callback is used to help
        /// us see if our GUI received an OnDestroy message from the User, or from a spurious Domain Reload.
        /// </summary>
        private static void DescheduleOnDestroyCallback()
        {
            EditorApplication.delayCall -= OnDelayedOnDestroyCallback;
        }

        /// <summary>
        /// Defers cleaning up the install. Is a callback / tick to watch for whether enough time has elapsed without
        /// having the GUI on screen, in order to justify the hypothesis that the user closed the dialog and started
        /// this OnDestroy phase. If the clock runs out then we cleanup the install. However, elsewhere, if the clock
        /// does not run out, then we should stop this callback.
        /// </summary>
        private static void OnDelayedOnDestroyCallback()
        {
            if (UnityIAPInstaller.m_Trace) Debug.Log("OnDelayedOnDestroyCallback");

            DescheduleOnDestroyCallback();

            var t = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(k_UserClosedDialogWaitHeuristicSeconds));

            if (t.Ticks < LastUpdate.Ticks)
            {
                // Still waiting to determine if this was a user-driven OnDestroy or not.
                // Will abort if not, and if dialog is restored. Else will delete assets.
                // See also
                EditorApplication.delayCall += OnDelayedOnDestroyCallback;
            }
            else
            {
                if (UnityIAPInstaller.m_Trace) Debug.Log("Heuristic triggered: wait-time elapsed, clean up installation ...");

                // Delete some assets

                // Should we clean up lingering obsolete files?
                // If the ManualInstallerWindow.Instance is null
                // then the user has likely closed the window quickly, possibly having installed.
                var finishedInstalling = false;

                if (ManualInstallerWindow.Instance != null && ManualInstallerWindow.Instance.Installed != null)
                {
                    if (ManualInstallerWindow.Instance.Installed[ManualInstallerWindow.Instance.Installed.Length - 1] == true)
                    {
                        finishedInstalling = true;
                    }
                }
                else if (UnityIAPInstaller.HasInstallStep())
                {
                    var step = UnityIAPInstaller.GetInstallStep();
                    if (step >= UnityIAPInstaller.k_Artifacts.Length)
                    {
                        finishedInstalling = true;
                    }
                }

                if (finishedInstalling)
                {
                    if (UnityIAPInstaller.m_Trace) Debug.Log("User did install everything, cleaning up!");
                }
                else
                {
                    if (UnityIAPInstaller.m_Trace) Debug.Log("User did not install everything, not cleaning up.");
                }

                UnityIAPInstaller.OnComplete(finishedInstalling);
            }
        }

        /// <summary>
        /// Draw a one-way latching toggle GUI, with ability to start asset installation.
        /// </summary>
        /// <param name="marked"></param>
        /// <param name="index">zero-based asset index</param>
        /// <param name="assetName"></param>
        private void InstallItemGUI(ref bool marked, int index, string assetName)
        {
            var oldMark = marked;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(assetName);

                GUILayout.FlexibleSpace();
                marked = GUILayout.Button("Import");

            }

            // Start install if user changed toggle
            if (oldMark == false && marked)
            {
                Debug.Log("Installing " + assetName);

                InstallAsset(index);
            }
            else
            {
                // One-way latch: prevent un-toggling.
                marked = oldMark;
            }
        }

        /// <summary>
        /// Trigger installation of an asset
        /// </summary>
        /// <param name="index">zero-based asset index</param>
        private void InstallAsset(int index)
        {
            LastImportTime = DateTime.UtcNow;

            // Reset install to nth asset
            LastSeenInstallStep = UnityIAPInstaller.SetInstallStep(index + 1);

            if (UnityIAPInstaller.m_Trace) Debug.LogFormat("InstallAsset index {0}, LastSeenInstallStep {1}", index, LastSeenInstallStep);

            // Ask the enclosing UnityIAPInstaller class to install the "next" package.
            UnityIAPInstaller.OnStep();
        }

        /// <summary>
        /// GUI repaint. Called when the GUI is onscreen.
        /// </summary>
        public void Update()
        {
            // Gain access to a shared static variable and set with self
            if (Instance != this && this != null)
            {
                // Found non-this reference in ManualInstallerWindow.Instance, overwriting with this

                if (Instance != null)
                {
                    Instance.Close();
                }

                Instance = this;
            }

            LastUpdate = DateTime.UtcNow;

            DescheduleOnDestroyCallback();
        }

        /// <summary>
        /// For on-top focus management.
        /// See https://docs.unity3d.com/ScriptReference/EditorWindow.ShowUtility.html
        /// </summary>
        void OnInspectorUpdate()
        {
            Repaint();
        }
    }
}
