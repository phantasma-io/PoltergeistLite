using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using PhantasmaPhoenix.Unity.Core;
using PhantasmaPhoenix.Unity.Core.Logging;

// Parsing and storing data received from TTRS store.
public static class TtrsStore
{
    public static void Clear()
    {
        StoreNft.Clear();
    }

    public struct Nft
    {
        [JsonIgnore]
        public string id;
        public string item; // "item": "371"
        public string url; // "url": "http://www.22series.com/api/store/part_info?id=371"
        public string img; // "img": "http://www.22series.com/api/store/part_img?id=371"
        public string type; // "type": "Item"
        public UInt64 source; // "source": 0
        public UInt64 source_data; // "source_data": 2
        public UInt64 timestamp; // "timestamp": 1581797657
        public UInt64 mint; // "mint": 3

        public ItemInfo item_info;

        public DateTime timestampDT()
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc)
                .AddSeconds(timestamp).ToLocalTime();
        }
        public void Postprocess(string id)
        {
            this.id = id;

            if (item_info.rarity == 5) // Fixing ttrs rarity gap.
                item_info.rarity = 4;
        }
    }

    public struct ItemInfo
    {
        public string name_english; // "name_english": "Akuna Front Spoiler (Carbon Fibre)"
        public string make; // "make": "Kaya"
        public string model; // "model": "Akuna"
        public string part; // "part": "Front Spoiler"
        public string material; // "material": "Aluminium"
        public string image_url; // "image_url": "http://www.22series.com/api/store/part_img?id=371"
        public string description_english; // "description_english": "Make: Kaya<br/>Model: Akuna<br/>Part: Aluminium Front Spoiler<br/>Aerodynamic Adjustable<br/>Finish: Clear (High Gloss)<br/>Part No: KA-3301-AERO-SP-FR-Carbon-Fibre"
        public string display_type_english; // "display_type_english": "Part"
        public UInt64 itemdefid; // "itemdefid": 371
        public UInt64 season; // "season": 1
        public UInt64 rarity; // "rarity": 3
        public string body_part; // "body_part": "AeroSpoilerFront"
        public string model_asset; // "model_asset": "ka-3301-aero-sp-fr-carbon-fibre"
        public string type; // "type": "kaya akuna"
        public string parent_types; // "parent_types": "kaya akuna"
        public string series; // "series": ""
        public string extra; // "extra": "Aerodynamic Adjustable"
        public string color; // "color": "Clear"
        public string finish; // "finish": "High Gloss"
        public UInt64 mint_limit; // "mint_limit": 0
    }

    private static Hashtable StoreNft = new Hashtable();


    public static bool CheckIfNftLoaded(string id)
    {
        return StoreNft.Contains(id);
    }

    public static Nft GetNft(string id)
    {
        return StoreNft.Contains(id) ? (Nft)StoreNft[id] : new Nft();
    }

    private static void LoadStoreNftFromApiResponse(Dictionary<string, Nft> storeNft, Action<Nft> callback)
    {
        if (storeNft == null)
        {
            return;
        }

        foreach (var (id, item) in storeNft)
        {
            if (!StoreNft.Contains(id))
            {
                item.Postprocess(id);
                StoreNft.Add(id, item);
            }

            callback(item);
        }
    }

    public static IEnumerator LoadStoreNft(string[] ids, Action<Nft> onItemLoadedCallback, Action onAllItemsLoadedCallback)
    {
        var url = "https://www.22series.com/api/store/nft";

        var cacheContents = Cache.GetAsString("ttrs-store-nft", Cache.FileType.JSON, 60 * 24);
        Dictionary<string, Nft>? storeNft = null;
        try
        {
            storeNft = JsonConvert.DeserializeObject<Dictionary<string, Nft>?>(cacheContents);
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
                idList += "\"" + ids[i] + "\"";
            else
                idList += ",\"" + ids[i] + "\"";
        }

        yield return WebClient.RESTPost<Dictionary<string, Nft>>(url, "{\"ids\":[" + idList + "]}", (error, msg) =>
        {
            Log.Write("LoadStoreNft() error: " + error);
        },
        (response) =>
        {
            if (response != null)
            {
                LoadStoreNftFromApiResponse(response, onItemLoadedCallback);

                if (storeNft != null)
                {
                    // Cache already exists, need to add new nfts to existing cache.
                    foreach (var item in response)
                    {
                        storeNft.Add(item.Key, item.Value);
                    }
                }
                else
                {
                    storeNft = response;
                }
                if (storeNft != null)
                    Cache.Add("ttrs-store-nft", Cache.FileType.JSON, JsonConvert.SerializeObject(storeNft, Formatting.Indented));
            }
            onAllItemsLoadedCallback();
        });
    }

    private static string NftToString(Nft nft)
    {
        return "Item #: " + nft.item + "\n" +
            "URL: " + nft.url + "\n" +
            "Image: " + nft.img + "\n" +
            "Type: " + nft.item_info.type + "\n" +
            "Source: " + nft.source + "\n" +
            "Source Data: " + nft.source_data + "\n" +
            "Timestamp: " + nft.timestamp + "\n" +
            "Mint: " + nft.mint + "\n" +
            "Name (English): " + nft.item_info.name_english + "\n" +
            "Make: " + nft.item_info.make + "\n" +
            "Model: " + nft.item_info.model + "\n" +
            "Part: " + nft.item_info.part + "\n" +
            "Material: " + nft.item_info.material + "\n" +
            "ImageUrl: " + nft.item_info.image_url + "\n" +
            "Description (English): " + nft.item_info.description_english + "\n" +
            "Display Type (English): " + nft.item_info.display_type_english + "\n" +
            "ItemDefId: " + nft.item_info.itemdefid + "\n" +
            "Season: " + nft.item_info.season + "\n" +
            "Rarity: " + nft.item_info.rarity + "\n" +
            "BodyPart: " + nft.item_info.body_part + "\n" +
            "ModelAsset: " + nft.item_info.model_asset + "\n" +
            "Type: " + nft.item_info.type + "\n" +
            "Parent Types: " + nft.item_info.parent_types + "\n" +
            "Series: " + nft.item_info.series + "\n" +
            "Extra: " + nft.item_info.extra + "\n" +
            "Color: " + nft.item_info.color + "\n" +
            "Finish: " + nft.item_info.finish + "\n" +
            "MintLimit: " + nft.item_info.mint_limit;
    }

    public static void LogStoreNft()
    {
        foreach (DictionaryEntry entry in StoreNft)
        {
            Log.Write(NftToString((Nft)entry.Value));
        }
    }
}
