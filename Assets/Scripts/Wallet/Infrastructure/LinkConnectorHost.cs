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
    // Owns and hosts every Phantasma Link entry point for the wallet: the local LinkServer
    // (loopback), the v5 relay client (cross-device pairings / large payloads) and the deeplink
    // endpoint (OS intents / universal links). Lives in the scene as a singleton; socket
    // callbacks arrive on worker threads and are marshalled back to the UI thread.
    public class LinkConnectorHost : MonoBehaviour
    {
        public WalletConnector PhantasmaLink { get; private set; }
        public LinkDeeplinkEndpoint DeeplinkEndpoint { get; private set; }
        public LinkRelayClient RelayClient { get; private set; }
        public static LinkConnectorHost Instance { get; private set; }

        private LinkServer server;

        private void Start()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            PhantasmaLink = new WalletConnector();

            // Serve the v5 protocol on /phantasma/v5 alongside the legacy string protocol on
            // /phantasma. WalletConnector implements the clean IWalletLinkV5Ops for the v5
            // dispatcher while remaining a WalletLink for the legacy one; both reuse the same
            // internal wallet logic. Sessions persist via PlayerPrefs (spec §7 resume).
            var walletLinkV5 = new WalletLinkV5(PhantasmaLink, new PlayerPrefsLinkSessionStore());
            server = new LinkServer(PhantasmaLink, walletLinkV5);

            var pairingStore = new PlayerPrefsLinkPairingStore();
            RelayClient = CreateRelayClient(walletLinkV5, pairingStore);

            // v5 deeplink endpoint (spec §17): pairing + encrypted request URLs delivered by the
            // OS (Android intents / iOS universal links). Pairings persist via PlayerPrefs.
            DeeplinkEndpoint = new LinkDeeplinkEndpoint(walletLinkV5, PhantasmaLink, pairingStore, RelayClient);
            Application.deepLinkActivated += OnDeepLink;
            if (!string.IsNullOrEmpty(Application.absoluteURL))
            {
                OnDeepLink(Application.absoluteURL); // cold start via a deeplink
            }

            // Re-join the relay topics of persisted pairings so an already-paired dApp can reach
            // the wallet over the relay right after launch (topic mailboxes drain on subscribe,
            // covering requests published while the wallet was down).
            RelayClient.EnsureConnected();

            WireServerCallbacks();
            ConfigureEditorDeeplinkSim();

            server.Start();
        }

        // v5 relay client (spec §16): outbound WebSocket for pairings that carry a relay URL
        // (cross-device QR, big payloads). The log sink is required - the client absorbs
        // connection failures by silent reconnection, so without it a dead relay link has no
        // visible symptom in the wallet.
        private LinkRelayClient CreateRelayClient(WalletLinkV5 walletLinkV5, PlayerPrefsLinkPairingStore pairingStore)
        {
            return new LinkRelayClient(
                walletLinkV5,
                pairingStore,
                new RelayWebSocketClient(),
                action => PostToUi(action),
                reconnectDelaysMs: null,
                log: message => Log.Write("RelayClient: " + message));
        }

        private void WireServerCallbacks()
        {
            server.OnUI = action => PostToUi(action);
            server.OnMessageBack = json => IntentPluginManager.Instance.ReturnMessage(json);
            server.OnUserMessage = msg => WalletApplicationContext.Instance.Messages.Push(msg);
        }

        // Dev-only deeplink simulator: a test driver connects here and plays the OS - it sends
        // deeplink URLs (pairing / requests) and receives the response URLs the wallet would
        // otherwise hand to Application.OpenURL. Exercises the exact LinkDeeplinkEndpoint code
        // that Android/iOS feed via real intents; only the OS hop itself is simulated.
        private void ConfigureEditorDeeplinkSim()
        {
#if UNITY_EDITOR
            server.WebSocket("/phantasma/v5/deeplink-sim", socket =>
            {
                while (socket.IsOpen)
                {
                    var msg = socket.Receive();
                    if (msg.CloseStatus != WebSocketCloseStatus.None || msg.Bytes == null)
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
        }

        private void OnDeepLink(string url)
        {
            PostToUi(() => DeeplinkEndpoint.TryHandle(url, responseUrl => Application.OpenURL(responseUrl)));
        }

        private void Update()
        {
            server?.Tick();
        }

        private void OnDestroy()
        {
            Application.deepLinkActivated -= OnDeepLink;
            RelayClient?.Dispose();
            server?.Stop();
            if (Instance == this)
            {
                Instance = null;
            }
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
