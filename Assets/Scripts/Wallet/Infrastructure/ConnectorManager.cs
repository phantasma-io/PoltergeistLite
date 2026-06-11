using System;
using UnityEngine;
using PhantasmaPhoenix.Link;
using PhantasmaPhoenix.Link.WebSockets;
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
        public LinkDeeplinkEndpoint DeeplinkEndpoint { get; private set; }
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

            // v5 deeplink endpoint (spec §19): pairing + encrypted request URLs delivered by the
            // OS (Android intents / iOS universal links). Pairings persist via PlayerPrefs.
            DeeplinkEndpoint = new LinkDeeplinkEndpoint(walletLinkV5, PhantasmaLink, new PlayerPrefsLinkPairingStore());
            Application.deepLinkActivated += OnDeepLink;
            if (!string.IsNullOrEmpty(Application.absoluteURL))
            {
                OnDeepLink(Application.absoluteURL); // cold start via a deeplink
            }

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

#if UNITY_EDITOR
            // Dev-only deeplink simulator: a test driver connects here and plays the OS - it
            // sends deeplink URLs (pairing / requests) and receives the response URLs the wallet
            // would otherwise Application.OpenURL. Exercises the EXACT LinkDeeplinkEndpoint code
            // that Android/iOS feed via real intents; only the OS hop itself is simulated.
            server.WebSocket("/phantasma/v5/deeplink-sim", socket =>
            {
                while (socket.IsOpen)
                {
                    var msg = socket.Receive();
                    if (msg.CloseStatus != WebSocketCloseStatus.None)
                    {
                        continue;
                    }
                    if (msg.Bytes == null)
                    {
                        continue;
                    }

                    var url = System.Text.Encoding.UTF8.GetString(msg.Bytes);
                    PostToUi(() => DeeplinkEndpoint.TryHandle(url, responseUrl =>
                    {
                        try
                        {
                            socket.Send(responseUrl);
                        }
                        catch (Exception e)
                        {
                            Log.WriteWarning("deeplink-sim send failure: " + e.Message);
                        }
                    }));
                }
            });
#endif

            server.Start();
        }

        private void OnDeepLink(string url)
        {
            PostToUi(() => DeeplinkEndpoint.TryHandle(url, responseUrl => Application.OpenURL(responseUrl)));
        }

        void Update()
        {
            server.Tick();
        }

        void OnDestroy()
        {
            Application.deepLinkActivated -= OnDeepLink;
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
