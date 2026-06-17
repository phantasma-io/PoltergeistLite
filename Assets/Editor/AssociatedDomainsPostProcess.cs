using UnityEditor;
using UnityEditor.Callbacks;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

// associated-domains (universal links for link.phantasma.info) is the ONE Apple capability that Unity
// Player Settings cannot express - verified against Unity 6 docs: the Player Settings auto-entitlements
// only cover Game Center, and the official API for associated domains is
// iOS.Xcode.ProjectCapabilityManager.AddAssociatedDomains. So this minimal post-process adds JUST that.
// Everything else (bundle id, signing team + automatic signing, camera usage, URL schemes) lives in
// Player Settings, not here.
public static class AssociatedDomainsPostProcess
{
    const string Domain = "applinks:link.phantasma.info";

    [PostProcessBuild]
    public static void OnPostprocessBuild(BuildTarget target, string buildPath)
    {
#if UNITY_IOS
        if (target != BuildTarget.iOS) return;

        string pbxPath = PBXProject.GetPBXProjectPath(buildPath);
        var proj = new PBXProject();
        proj.ReadFromFile(pbxPath);

        // Attach the entitlement to the main app target (not UnityFramework).
        string mainGuid = proj.GetUnityMainTargetGuid();
        var caps = new ProjectCapabilityManager(pbxPath, "Unity-iPhone/PoltergeistLite.entitlements", null, mainGuid);
        caps.AddAssociatedDomains(new[] { Domain });
        caps.WriteToFile();
#endif
    }
}
