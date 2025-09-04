using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Emberglass.Patches.Shared;

namespace Emberglass;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
internal class Plugin : BasePlugin
{
    public static Plugin Instance { get; set; }
    public static ManualLogSource Logger { get; set; }
    public Plugin()
    {
        Logger = Log;
        Instance = this;
    }
    public override void Load()
    {
        GameBootstrapPatch.Initialize();

        Logger.LogInfo($"{MyPluginInfo.PLUGIN_NAME}[{MyPluginInfo.PLUGIN_VERSION}] loaded!");
    }
    public override bool Unload()
    {
        GameBootstrapPatch.Uninitialize();

        return true;
    }
}