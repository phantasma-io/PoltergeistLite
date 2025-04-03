using Phantasma.SDK;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

// Parsing and storing data received from GAME store.
public static class GameStore
{
    public static void Clear()
    {
        StoreNft.Clear();
    }

    public struct GameNftApiResponse
    {
        public GameNft[] nfts;
        public Dictionary<string, GameNftMeta> meta;

        public void Merge(GameNftApiResponse? source)
        {
            if(source == null)
            {
                return;
            }

            foreach (var nft in source.Value.nfts)
            {
                nfts = nfts.Append(nft).ToArray();
            }

            foreach (var m in source.Value.meta)
            {
                meta.Add(m.Key, m.Value);
            }
        }
    }

    public struct GameNft
    {
        public string ID;
        public string chainName;
        public string creatorAddress;
        public UInt64 mint;
        public string ownerAddress;

        public ParsedRom parsed_rom;

        public string ram;
        public string rom;
        public string series;
        public string status;
        public string pavillion_id;
        [JsonIgnore]
        public GameNftMeta? meta;
    }

    public struct ParsedRom
    {
        public UInt64 app_index;
        public string extra;
        public string img_url;
        public string info_url;
        public UInt64 item_id;
        public string metadata;
        public string mintedFor;
        public string seed;
        public UInt64 timestamp;
        public UInt64 type;

        public DateTime timestampDT()
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc)
                .AddSeconds(timestamp).ToLocalTime();
        }
    }

    public struct GameNftMeta
    {
        // meta
        public UInt64 available_from;
        public string current_hash;
        public string description_english;
        public string itemdefid;
        public UInt64 modified_timestamp;
        public string name_english;
        public UInt64 price_usd_cent;
        public string meta_type;
    }

    private static Hashtable StoreNft = new Hashtable();


    public static bool CheckIfNftLoaded(string id)
    {
        return StoreNft.Contains(id);
    }

    public static GameNft GetNft(string id)
    {
        return StoreNft.Contains(id) ? (GameNft)StoreNft[id] : new GameNft();
    }

    private static void LoadStoreNftFromApiResponse(GameNftApiResponse? apiResponse, Action<GameNft> callback)
    {
        if (apiResponse == null)
        {
            return;
        }

        for (int i = 0; i < apiResponse.Value.nfts.Length; i++)
        {
            var item = apiResponse.Value.nfts[i];

            if (!StoreNft.Contains(item.ID))
            {
                item.meta = apiResponse.Value.meta.Where(m => m.Key == item.parsed_rom.metadata).Select(m => m.Value).FirstOrDefault();
                StoreNft.Add(item.ID, item);
            }

            callback(item);
        }

        LogStoreNft();
    }

    public static IEnumerator LoadStoreNft(string[] ids, Action<GameNft> onItemLoadedCallback, Action onAllItemsLoadedCallback)
    {
        var url = "https://pavillionhub.com/api/nft_data?phantasma_ids=1&token=GAME&meta=1&ids=";

        var cacheContents = Cache.GetAsString("game-store-nft", Cache.FileType.JSON, 60 * 24);
        GameNftApiResponse? storeNft = null;
        try
        {
            storeNft = JsonConvert.DeserializeObject<GameNftApiResponse?>(cacheContents);
        }
        catch
        {
            Log.Write("Cache is corrupted, probably old version");
        }

        if (storeNft != null)
        {
            LoadStoreNftFromApiResponse(storeNft, onItemLoadedCallback);

            // Checking, that cache contains all needed NFTs.
            string[] missingIds = ids;
            for (int i = 0; i < ids.Length; i++)
            {
                if (CheckIfNftLoaded(ids[i]))
                {
                    missingIds = missingIds.Where(x => x != ids[i]).ToArray();
                }
            }
            ids = missingIds;

            if (ids.Length == 0)
            {
                onAllItemsLoadedCallback();
                yield break;
            }
        }

        var idList = "";
        for (int i = 0; i < ids.Length; i++)
        {
            if (String.IsNullOrEmpty(idList))
                idList += ids[i];
            else
                idList += "," + ids[i];
        }

        yield return WebClient.RESTRequestT<GameNftApiResponse>(url + idList, 0, (error, msg) =>
        {
            Log.Write("LoadStoreNft() error: " + error);
        },
        (response) =>
        {
            LoadStoreNftFromApiResponse(response, onItemLoadedCallback);

            if (storeNft != null)
            {
                // Cache already exists, need to add new nfts to existing cache.
                storeNft.Value.Merge(response);
            }
            else
            {
                storeNft = response;
            }
            if (storeNft != null)
                Cache.Add("game-store-nft", Cache.FileType.JSON, JsonConvert.SerializeObject(storeNft, Formatting.Indented));

            onAllItemsLoadedCallback();
        });
    }

    private static string NftToString(GameNft nft)
    {
        return "Item #: " + nft.ID + "\n" +
            "chainName: " + nft.chainName + "\n" +
            "creatorAddress: " + nft.creatorAddress + "\n" +
            "mint: " + nft.mint + "\n" +
            "ownerAddress: " + nft.ownerAddress + "\n" +
            "app_index: " + nft.parsed_rom.app_index + "\n" +
            "extra: " + nft.parsed_rom.extra + "\n" +
            "img_url: " + nft.parsed_rom.img_url + "\n" +
            "info_url: " + nft.parsed_rom.info_url + "\n" +
            "item_id: " + nft.parsed_rom.item_id + "\n" +
            "metadata: " + nft.meta + "\n" +
            "mintedFor: " + nft.parsed_rom.mintedFor + "\n" +
            "seed: " + nft.parsed_rom.seed + "\n" +
            "timestamp: " + nft.parsed_rom.timestamp + "\n" +
            "type: " + nft.parsed_rom.type + "\n" +
            "ram: " + nft.ram + "\n" +
            "rom: " + nft.rom + "\n" +
            "series: " + nft.series + "\n" +
            "status: " + nft.status + "\n" +
            "pavillion_id: " + nft.pavillion_id + "\n" +
            "available_from: " + nft.meta?.available_from + "\n" +
            "current_hash: " + nft.meta?.current_hash + "\n" +
            "description_english: " + nft.meta?.description_english + "\n" +
            "itemdefid: " + nft.meta?.itemdefid + "\n" +
            "modified_timestamp: " + nft.meta?.modified_timestamp + "\n" +
            "name_english: " + nft.meta?.name_english + "\n" +
            "price_usd_cent: " + nft.meta?.price_usd_cent + "\n" +
            "type: " + nft.meta?.meta_type;
    }

    public static void LogStoreNft()
    {
        foreach (DictionaryEntry entry in StoreNft)
        {
            Log.Write(NftToString((GameNft)entry.Value));
        }
    }
}
