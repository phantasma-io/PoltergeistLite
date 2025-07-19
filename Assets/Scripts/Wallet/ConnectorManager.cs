using System;
using UnityEngine;
using PhantasmaPhoenix.Link;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

            server = new LinkServer(PhantasmaLink);

            // redirect UI callbacks to Unity
            server.OnUI = action => WalletGUI.Instance.CallOnUIThread(action);

            // message back to Android intent
            server.OnMessageBack = json =>
            {
                IntentPluginManager.Instance.ReturnMessage(json);
            };

            // user messages (e.g. port conflict)
            server.OnUserMessage = msg =>
            {
                WalletGUI.MessageForUser(msg);
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
            WalletGUI.Instance.CallOnUIThread(() =>
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
    }
}
