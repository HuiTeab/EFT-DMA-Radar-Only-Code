using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace eft_dma_radar
{
    internal static class TarkovMarketManager
    {
        /// <summary>
        /// Contains all Tarkov Loot mapped via BSGID String.
        /// </summary>
        public static ReadOnlyDictionary<string, LootItem> AllItems { get; }

        #region Static_Constructor
        static TarkovMarketManager()
        {
            TrakovDevResponse jsonResponse;
            
            var jsonItems = new List<TrakovDevResponse>();
            // Initialize AllItems Dictionary.
            var allItems = new Dictionary<string, LootItem>(StringComparer.OrdinalIgnoreCase);
            //$response = Invoke-RestMethod -Uri "https://api.tarkov.dev/graphql" -Method Post -Body $body -ContentType "application/json"
            if (!File.Exists("api_tarkov_dev_market.json") ||
            File.GetLastWriteTime("api_tarkov_dev_market.json").AddHours(24) < DateTime.Now) // only update every 24h
            {
                using (var client = new HttpClient())
                {
                    //Create body and content-type
                    var body = new
                    {
                        query = @"query {
                            items {
                                id
                                name
                                shortName
                                normalizedName
                                basePrice
                                avg24hPrice
                                low24hPrice
                                high24hPrice
                            }
                        }"
                    };
                    var jsonBody = JsonSerializer.Serialize(body);
                    var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                    //Send request
                    var response = client.PostAsync("https://api.tarkov.dev/graphql", content).Result;
                    //Read response
                    var json = response.Content.ReadAsStringAsync().Result;

                    //using var req = client.GetAsync("https://market_master.filter-editor.com/data/marketData_en.json").Result;
                    //string json = req.Content.ReadAsStringAsync().Result;
                    jsonResponse = JsonSerializer.Deserialize<TrakovDevResponse>(json);
                    jsonItems = new List<TrakovDevResponse>();
                    File.WriteAllText("api_tarkov_dev_market.json", json);
                }
            }
            else
            {
                var jsonFile = File.ReadAllText("api_tarkov_dev_market.json");
                jsonResponse = JsonSerializer.Deserialize<TrakovDevResponse>(jsonFile);
                jsonItems = new List<TrakovDevResponse>();
                //jsonItems = JsonSerializer.Deserialize<List<TrakovDevResponse>>(jsonFile);
            }
            if (jsonResponse != null)
            {
                jsonItems.Add(jsonResponse);
            }
            if (jsonItems is not null)
            {
                // Assuming you want to filter items based on a specific condition
                // Example: Filtering items where id is not null or empty
                var jsonItemsFiltered = jsonItems; //.Where(x => !string.IsNullOrEmpty(x.data.items[0].id));

                foreach (var item in jsonItemsFiltered)
                {
                    //Debug.WriteLine($"TarkovMarketManager: TarkovMarketItem: {item}");
                    foreach (var item2 in item.data.items)
                    {
                        //Debug.WriteLine($"TarkovMarketManager: TarkovMarketItem: {item2}");
                        // Assuming GetItemValue method requires 'id' as a parameter
                        var value = GetItemValue(item2);

                        allItems.TryAdd(item2.id, new LootItem()
                        {
                            Label = $"[{FormatNumber(value)}] {item2.shortName}",
                            Item = item2
                        });
                    }
                }
                AllItems = new(allItems); // update readonly ref
            }

            else throw new NullReferenceException("jsonItems");


        }
        #endregion

        #region Methods

        private static string FormatNumber(int num)
        {
            if (num >= 1000000)
                return (num / 1000000D).ToString("0.##") + "M";
            else if (num >= 1000)
                return (num / 1000D).ToString("0") + "K";

            else return num.ToString();
        }

        private static int GetItemValue(TrakovDevMarketItem item)
        {
            if (item.avg24hPrice > item.basePrice)
                return (int)item.avg24hPrice;
            else
                return (int)item.avg24hPrice;
        }
        #endregion
    }

    #region Classes
    /// <summary>
    /// Class JSON Representation of Tarkov Market Data.
    /// </summary>
    public class TarkovMarketItem
    {
        public string uid { get; set; }
        public string name { get; set; } = "null";
        public List<string> tags { get; set; }
        public string shortName { get; set; } = "null";
        public int price { get; set; }
        public int basePrice { get; set; }
        public int avg24hPrice { get; set; } = 0;
        public int avg7daysPrice { get; set; }
        public string traderName { get; set; }
        public int traderPrice { get; set; } = 0;
        public string traderPriceCur { get; set; }
        public DateTime updated { get; set; }
        public int slots { get; set; }
        public double diff24h { get; set; }
        public double diff7days { get; set; }
        public string icon { get; set; }
        public string link { get; set; }
        public string wikiLink { get; set; }
        public string img { get; set; }
        public string imgBig { get; set; }
        public string bsgId { get; set; }
        public bool isFunctional { get; set; }
        public string itemType { get; set; }
        public string reference { get; set; }
        public string apiKey { get; set; }
    }

    /// <summary>
    /// New Class to hold Tarkov Market Data.
    /// </summary>
    public class TrakovDevMarketItem
    {
        public string id { get; set; }
        public string name { get; set; }
        public string shortName { get; set; }
        public string normalizedName { get; set; }
        public int basePrice { get; set; }
        public int? avg24hPrice { get; set; } // Nullable int to handle null values
        public int? low24hPrice { get; set; } // Nullable int to handle null values
        public int? high24hPrice { get; set; } // Nullable int to handle null values
    }

    public class TrakovDevData
    {
        public List<TrakovDevMarketItem> items { get; set; }
    }

    public class TrakovDevResponse
    {
        public TrakovDevData data { get; set; }
    }


    #endregion
}