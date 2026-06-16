using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.VM;
using Poltergeist.Wallet;
using PhantasmaPhoenix.Core.Extensions;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.Unity.Core;
using PhantasmaPhoenix.NFT;
using PhantasmaPhoenix.NFT.Extensions;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Unity.Core.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Numerics;

namespace Poltergeist
{
    public partial class AccountManager
    {
        // Start is called before the first frame update
        void Start()
        {
            Settings.Load();

            if (AccountManager.Instance.Settings.initialWindowWidth > 0 && AccountManager.Instance.Settings.initialWindowHeight > 0)
            {
                Screen.SetResolution(AccountManager.Instance.Settings.initialWindowWidth, AccountManager.Instance.Settings.initialWindowHeight, false);
            }

            UpdateRPCURL();

            LoadNexus();

            // Version 1 - original account version used in PG up to version 1.9.
            // Version 2 - new account version.
            // var walletVersion = PlayerPrefs.GetInt(WalletVersionTag, 1);

            var wallets = PlayerPrefs.GetString(WalletTag, "");
            Accounts = new List<Account>();

            if (!string.IsNullOrEmpty(wallets))
            {
                var bytes = Base16.Decode(wallets);
                try
                {
                    List<Account> accountsTemp = new List<Account>();
                    var reader = new BinaryReader(new MemoryStream(bytes));
                    var size = reader.ReadVarInt();
                    for (int i = 0; i < (int)size; i++)
                    {
                        var account = new Account();
                        account.UnserializeData(reader);
                        accountsTemp.Add(account);
                    }

                    Accounts = accountsTemp; //  = Serialization.Unserialize<Account[]>(bytes).ToList();
                }
                catch (Exception e)
                {
                    Log.WriteFatalError("Error deserializing accounts: " + e);
                }
            }

            AccountsAreReadyToBeUsed = true;
            LoadHiddenWallets();

            if (Settings.lastShownInformationScreen == 0)
            {
                Settings.lastShownInformationScreen = 1;

                WalletApplicationContext.Instance.Messages.Push(@"A note for existing Poltergeist wallet users!

If you already have a previous (older, not 'Light') version of Poltergeist installed on your device, then you will need to:

1. Open your previous version of Poltergeist
2. Export your wallets onto your clipboard:
  * Press 'Manage' button available on main screen
  * Press 'Export' button and enter password to encrypt exported accounts, press 'Confirm'
3. Close old app
4. Open the new version of Poltergeist Lite
5. Import wallets data:
  * Press 'Manage' on main screen and then press 'Import'. Paste exported accounts from clipboard and press 'Confirm'. You will need to enter password which you used in the previous step. You will be presented with a list of accounts being imported, press 'Confirm'

Happy Poltergeisting!

Regards,
The Phoenix team", "Notice");
            }
        }

        public void SaveAccounts()
        {
            PlayerPrefs.SetInt(WalletVersionTag, 3);
            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);
            Accounts.ForEach(acc => acc.version = 3);

            writer.WriteVarInt(Accounts.Count);
            foreach (var account in Accounts)
            {
                account.SerializeData(writer);
            }

            var bytes = stream.ToArray();//Serialization.Serialize(Accounts.ToArray());
            PlayerPrefs.SetString(WalletTag, Base16.Encode(bytes));
            PruneHiddenWallets();
            SaveHiddenWalletsInternal(false);
            PlayerPrefs.Save();
        }

    }
}
