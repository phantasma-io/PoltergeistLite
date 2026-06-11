using System;
using UnityEngine;
using PhantasmaPhoenix.Link;
using PhantasmaPhoenix.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.Unity.Core.Logging;
using Poltergeist.Wallet;

namespace Poltergeist
{
    public class ConnectorManager : MonoBehaviour
    {
        public WalletConnector PhantasmaLink { get; private set; }
        public static ConnectorManager Instance { get; private set; }

        private LinkServer server;

        void Start()
        {
            Instance = this;
            PhantasmaLink = new WalletConnector();

            // Serve the v5 protocol (new generation) on /phantasma/v5 alongside the legacy
            // string protocol on /phantasma. WalletConnector implements the clean IWalletLinkV5Ops
            // for the v5 dispatcher while remaining a WalletLink for the legacy one; both reuse the
            // same internal wallet logic. Sessions persist via PlayerPrefs (spec §7 resume).
            var walletLinkV5 = new WalletLinkV5(PhantasmaLink, new PlayerPrefsLinkSessionStore());
            server = new LinkServer(PhantasmaLink, walletLinkV5);

            // redirect UI callbacks to Unity
            server.OnUI = action => PostToUi(action);

            // message back to Android intent
            server.OnMessageBack = json =>
            {
                IntentPluginManager.Instance.ReturnMessage(json);
            };

            // user messages (e.g. port conflict)
            server.OnUserMessage = msg =>
            {
                WalletApplicationContext.Instance.Messages.Push(msg);
            };

            server.Start();
        }

        void Update()
        {
            server.Tick();
        }

        void OnDestroy()
        {
            server?.Stop();
        }

        public void OnIntentInteraction(string msg)
        {
#if UNITY_ANDROID
            PostToUi(() =>
            {
                PhantasmaLink.Execute(msg, (id, root, success) =>
                {
                    ((JObject)root)["id"] = id;
                    ((JObject)root)["success"] = success;

                    var json = root.ToString(Formatting.None);
                    
                    try
                    {
                        IntentPluginManager.Instance.ReturnMessage(json);
                    }
                    catch (Exception e)
                    {
                        Log.WriteWarning("websocket send failure, while answering phantasma link request: " + msg + "\nExcepion: " + e.Message);
                    }
                });
            });
#endif
        }

        private static void PostToUi(Action action)
        {
            var ui = WalletUiBridge.Current;
            if (ui != null)
            {
                ui.PostToMainThread(action);
                return;
            }

            UnityTaskRunner.PostToMainThread(action);
        }
    }
}
