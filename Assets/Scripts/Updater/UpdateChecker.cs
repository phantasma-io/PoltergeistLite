using System.Collections;
using System.Text.RegularExpressions;
using PhantasmaPhoenix.Unity.Core.Logging;
using Poltergeist.Wallet;
using UnityEngine;
using UnityEngine.Networking;

public class UpdateChecker : MonoBehaviour
{
    public string githubOwner = "phantasma-io"; // Your GitHub username
    public string githubRepo = "PoltergeistLite"; // Your repository name
    public string currentVersion = "1.7.0"; // Current version of your game

    private const string GITHUB_RELEASES_URL = "https://github.com/";
    private static string URL = "";

    public static string UPDATE_URL => URL;


    // Start is called before the first frame update
    private void Start()
    {
#if !UNITY_ANDROID && !UNITY_IOS
        URL = GITHUB_RELEASES_URL + githubOwner + "/" + githubRepo + "/releases/latest";
        currentVersion = Application.version;
        StartCoroutine(CheckForUpdates());
#endif
    }


    private IEnumerator CheckForUpdates()
    {
        using (UnityWebRequest www =
               UnityWebRequest.Get(GITHUB_RELEASES_URL + githubOwner + "/" + githubRepo + "/releases/latest"))
        {
            www.SetRequestHeader("User-Agent", "Unity");
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Log.WriteWarning(www.error);
            }
            else
            {
                string htmlContent = www.downloadHandler.text;
                string versionPattern = @"<h1 data-view-component=""true"" class=""d-inline mr-3"">(.*?)</h1>";
                Match versionMatch = Regex.Match(htmlContent, versionPattern);

                if (versionMatch.Success)
                {
                    string latestVersion = versionMatch.Groups[1].Value;
                    string latestVersionNoPrefix =
                        latestVersion.StartsWith("v") ? latestVersion.Substring(1) : latestVersion;
                    string currentVersionNoPrefix = Application.version.StartsWith("v")
                        ? Application.version.Substring(1)
                        : Application.version;

                    System.Version latestVer = new System.Version(latestVersionNoPrefix);
                    System.Version currentVer = new System.Version(currentVersionNoPrefix);

                    if (latestVer > currentVer)
                    {
                        var message =
                            $"A new version {latestVersionNoPrefix} of the wallet is available (you have {currentVersion}). Please update the wallet.\n\n{URL}";
                        ShowUpdatePromptAsync(message).Forget(e => Log.WriteWarning($"Update prompt failed: {e}"));
                    }
                }
                else
                {
                    Log.WriteWarning("Could not find version information.");
                }
            }
        }
    }

    private static async System.Threading.Tasks.Task ShowUpdatePromptAsync(string message)
    {
        var ui = WalletUiBridge.Current;
        if (ui == null)
        {
            WalletApplicationContext.Instance.Messages.Push(message, "Update Available", MessageKind.Default);
            return;
        }

        var open = await ui.ConfirmAsync("Update Available", message, "Open", "Later");
        if (open)
        {
            Application.OpenURL(UPDATE_URL);
        }
    }
}
