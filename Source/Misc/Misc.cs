using System.Text.Json;
using System.Diagnostics;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace eft_dma_radar
{
    // Small & Miscellaneous Classes/Enums Go here

    #region Program Classes
    /// <summary>
    /// Custom Debug Stopwatch class to measure performance.
    /// </summary>
    public class DebugStopwatch
    {
        private readonly Stopwatch _sw;
        private readonly string _name;

        /// <summary>
        /// Constructor. Starts stopwatch.
        /// </summary>
        /// <param name="name">(Optional) Name of stopwatch.</param>
        public DebugStopwatch(string name = null)
        {
            _name = name;
            _sw = new Stopwatch();
            _sw.Start();
        }

        /// <summary>
        /// End stopwatch and display result to Debug Output.
        /// </summary>
        public void Stop()
        {
            _sw.Stop();
            TimeSpan ts = _sw.Elapsed;
            Debug.WriteLine($"{_name} Stopwatch Runtime: {ts.Ticks} ticks");
        }
    }
    /// <summary>
    /// Global Program Configuration (Config.json)
    /// </summary>
    public class Config
    {
        [JsonIgnore]
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions()
        {
            WriteIndented = true,
        };
        [JsonIgnore]
        private static readonly object _lock = new();
        /// <summary>
        /// Enables Vertical Sync in GUI Render.
        /// </summary>
        [JsonPropertyName("vsyncEnabled")]
        public bool Vsync { get; set; }
        /// <summary>
        /// Player/Teammates Aimline Length (Max: 1000)
        /// </summary>
        [JsonPropertyName("playerAimLine")]
        public int PlayerAimLineLength { get; set; }
        /// <summary>
        /// Last used 'Zoom' level.
        /// </summary>
        [JsonPropertyName("defaultZoom")]
        public int DefaultZoom { get; set; }
        /// <summary>
        /// UI Scale Value (50-200 , default: 100)
        /// </summary>
        [JsonPropertyName("uiScale")]
        public int UIScale { get; set; }
        /// <summary>
        /// Enables loot output on map.
        /// </summary>
        [JsonPropertyName("lootEnabled")]
        public bool LootEnabled { get; set; }
        /// <summary>
        /// Enables Aimview window in Main Window.
        /// </summary>
        [JsonPropertyName("aimviewEnabled")]
        public bool AimViewEnabled { get; set; }
        /// <summary>
        /// Hides player names & extended player info in Radar GUI.
        /// </summary>
        [JsonPropertyName("hideNames")]
        public bool HideNames { get; set; }
        /// <summary>
        /// Primary Teammate ACCT ID (for secondary Aimview)
        /// </summary>
        [JsonPropertyName("primaryTeammateAcctId")]
        public string PrimaryTeammateId { get; set; }
        /// <summary>
        /// Enables logging output to 'log.txt'
        /// </summary>
        [JsonPropertyName("loggingEnabled")]
        public bool LoggingEnabled { get; set; }
        /// <summary>
        /// Max game distance to render targets in Aimview, 
        /// and to display dynamic aimlines between two players.
        /// </summary>
        [JsonPropertyName("maxDistance")]
        public float MaxDistance { get; set; }
        /// <summary>
        /// 'Field of View' in degrees to display targets in the Aimview window.
        /// </summary>
        [JsonPropertyName("aimviewFOV")]
        public float AimViewFOV { get; set; }
        /// <summary>
        /// Minimum loot value (rubles) to display 'normal loot' on map.
        /// </summary>
        [JsonPropertyName("minLootValue")]
        public int MinLootValue { get; set; }
        /// <summary>
        /// Minimum loot value (rubles) to display 'important loot' on map.
        /// </summary>
        [JsonPropertyName("minImportantLootValue")]
        public int MinImportantLootValue { get; set; }

        public Config()
        {
            Vsync = true;
            PlayerAimLineLength = 1000;
            DefaultZoom = 100;
            UIScale = 100;
            LootEnabled = true;
            AimViewEnabled = false;
            HideNames = false;
            LoggingEnabled = false;
            MaxDistance = 325;
            AimViewFOV = 30;
            MinLootValue = 50000;
            MinImportantLootValue = 300000;
            PrimaryTeammateId = null;
        }

        /// <summary>
        /// Attempt to load Config.json
        /// </summary>
        /// <param name="config">'Config' instance to populate.</param>
        /// <returns></returns>
        public static bool TryLoadConfig(out Config config)
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists("Config.json")) throw new FileNotFoundException("Config.json does not exist!");
                    var json = File.ReadAllText("Config.json");
                    config = JsonSerializer.Deserialize<Config>(json);
                    return true;
                }
                catch
                {
                    config = null;
                    return false;
                }
            }
        }
        /// <summary>
        /// Save to Config.json
        /// </summary>
        /// <param name="config">'Config' instance</param>
        public static void SaveConfig(Config config)
        {
            lock (_lock)
            {
                var json = JsonSerializer.Serialize<Config>(config, _jsonOptions);
                File.WriteAllText("Config.json", json);
            }
        }
    }
    #endregion

    #region GUI Classes
    /// <summary>
    /// Defines map position for the 2D Map.
    /// </summary>
    public struct MapPosition
    {
        public MapPosition()
        {
        }
        /// <summary>
        /// Contains the Skia Interface (UI) Scaling Value.
        /// </summary>
        public float UIScale = 0;
        /// <summary>
        /// X coordinate on Bitmap.
        /// </summary>
        public float X = 0;
        /// <summary>
        /// Y coordinate on Bitmap.
        /// </summary>
        public float Y = 0;
        /// <summary>
        /// Unit 'height' as determined by Vector3.Z
        /// </summary>
        public float Height = 0;

        /// <summary>
        /// Get exact player location (with optional X,Y offsets).
        /// </summary>
        public SKPoint GetPoint(float xOff = 0, float yOff = 0)
        {
            return new SKPoint(X + xOff, Y + yOff);
        }
        /// <summary>
        /// Gets the point where the Aimline 'Line' ends. Applies UI Scaling internally.
        /// </summary>
        private SKPoint GetAimlineEndpoint(double radians, float aimlineLength)
        {
            aimlineLength *= UIScale;
            return new SKPoint((float)(this.X + Math.Cos(radians) * aimlineLength), (float)(this.Y + Math.Sin(radians) * aimlineLength));
        }

        /// <summary>
        /// Gets up arrow where loot is. IDisposable. Applies UI Scaling internally.
        /// </summary>
        private SKPath GetUpArrow(float size = 6)
        {
            size *= UIScale;
            SKPath path = new SKPath();
            path.MoveTo(X, Y);
            path.LineTo(X - size, Y + size);
            path.LineTo(X + size, Y + size);
            path.Close();

            return path;
        }

        /// <summary>
        /// Gets down arrow where loot is. IDisposable. Applies UI Scaling internally.
        /// </summary>
        private SKPath GetDownArrow(float size = 6)
        {
            size *= UIScale;
            SKPath path = new SKPath();
            path.MoveTo(X, Y);
            path.LineTo(X - size, Y - size);
            path.LineTo(X + size, Y - size);
            path.Close();

            return path;
        }
        /// <summary>
        /// Draws a Death Marker on this location.
        /// </summary>
        public void DrawDeathMarker(SKCanvas canvas)
        {
            float length = 6 * UIScale;
            canvas.DrawLine(new SKPoint(this.X - length, this.Y + length), new SKPoint(this.X + length, this.Y - length), SKPaints.PaintDeathMarker);
            canvas.DrawLine(new SKPoint(this.X - length, this.Y - length), new SKPoint(this.X + length, this.Y + length), SKPaints.PaintDeathMarker);
        }
        /// <summary>
        /// Draws an Exfil on this location.
        /// </summary>
        public void DrawExfil(SKCanvas canvas, Exfil exfil, float localPlayerHeight)
        {
            var heightDiff = this.Height - localPlayerHeight;
            if (heightDiff > 1.85) // exfil is above player
            {
                using var path = this.GetUpArrow(5);
                canvas.DrawPath(path, exfil.Status.GetPaint());
            }
            else if (heightDiff < -1.85) // exfil is below player
            {
                using var path = this.GetDownArrow(5);
                canvas.DrawPath(path, exfil.Status.GetPaint());
            }
            else // exfil is level with player
            {
                canvas.DrawCircle(this.GetPoint(), 4 * UIScale, exfil.Status.GetPaint());
            }
        }
        /// <summary>
        /// Draws a 'Hot' Grenade on this location.
        /// </summary>
        public void DrawGrenade(SKCanvas canvas)
        {
            canvas.DrawCircle(this.GetPoint(), 5 * UIScale, SKPaints.PaintGrenades);
        }
        /// <summary>
        /// Draws a loot item on this location.
        /// </summary>
        public void DrawLoot(SKCanvas canvas, string label, bool important, float heightDiff)
        {
            SKPaint paint = important ? SKPaints.PaintImportantLoot : SKPaints.PaintLoot;
            SKPaint text = important ? SKPaints.TextImportantLoot : SKPaints.TextLoot;
            if (heightDiff > 1.45) // loot is above player
            {
                using var path = this.GetUpArrow();
                canvas.DrawPath(path, paint);
            }
            else if (heightDiff < -1.45) // loot is below player
            {
                using var path = this.GetDownArrow();
                canvas.DrawPath(path, paint);
            }
            else // loot is level with player
            {
                canvas.DrawCircle(this.GetPoint(), 5 * UIScale, paint);
            }
            canvas.DrawText(label, this.GetPoint(7 * UIScale, 3 * UIScale), text);
        }
        /// <summary>
        /// Draws a Player Marker on this location.
        /// </summary>
        public void DrawPlayerMarker(SKCanvas canvas, Player player, int aimlineLength, int? mouseoverGrp)
        {
            var radians = player.Rotation.X.ToRadians();
            SKPaint paint;
            if (mouseoverGrp is not null
                && mouseoverGrp == player.GroupID) paint = SKPaints.PaintMouseoverGroup;
            else paint = player.GetPaint();
            canvas.DrawCircle(this.GetPoint(), 6 * UIScale, paint); // draw LocalPlayer marker
            canvas.DrawLine(this.GetPoint(),
                this.GetAimlineEndpoint(radians, aimlineLength),
                paint); // draw LocalPlayer aimline
        }
        /// <summary>
        /// Draws Player Text on this location.
        /// </summary>
        public void DrawPlayerText(SKCanvas canvas, Player player, string[] lines, int? mouseoverGrp)
        {
            SKPaint text;
            if (mouseoverGrp is not null
                && mouseoverGrp == player.GroupID) text = SKPaints.TextMouseoverGroup;
            else text = player.GetText();
            float spacing = 3 * UIScale;
            foreach (var line in lines)
            {
                canvas.DrawText(line, this.GetPoint(9 * UIScale, spacing), text); // draw line text
                spacing += 12 * UIScale;
            }
        }
        /// <summary>
        /// Draws tooltips on Map Markers
        /// </summary>
        public void DrawTooltip(SKCanvas canvas, Player player)
        {
            string[] lines = null;
            if (!player.IsAlive) // Get info about dead bodies ('X' markers)
            {
                if (player.Lvl != 0 || player.GroupID != -1) lines = new string[3]
                {
                    "Corpse",
                    string.Empty,
                    string.Empty
                };
                else lines = new string[2]
                {
                    "Corpse",
                    string.Empty
                };
                lines[1] += $"{player.Type}:{player.Name}";
                if (player.Lvl != 0) lines[2] += $"L:{player.Lvl} ";
                if (player.GroupID != -1) lines[2] += $"G:{player.GroupID} ";
            }
            else if (player.IsHumanHostileActive) // Enemy Human Players, display information
            {
                if (player.GroupID != -1 || player.KDA != -1f || player.Lvl != 0) lines = new string[3]
                {
                    string.Empty,
                    string.Empty,
                    string.Empty
                };
                else lines = new string[2]
                {
                    string.Empty,
                    string.Empty
                };
                lines[0] += player.Name;
                if (player.Gear is not null) // Get weapon info via GearManager
                {
                    string wep = "None";
                    GearItem gearItem = null;
                    if (!player.Gear.TryGetValue("FirstPrimaryWeapon", out gearItem))
                        if (!player.Gear.TryGetValue("SecondPrimaryWeapon", out gearItem))
                            player.Gear.TryGetValue("Holster", out gearItem);
                    if (gearItem is not null) wep = gearItem.Short; // Get 'short' weapon name/info
                    lines[1] += $"Wep:{wep}";
                }
                else lines[1] += "Wep:ERROR"; // GearManager failed
                if (player.Lvl != 0) lines[2] += $"L:{player.Lvl} ";
                if (player.GroupID != -1) lines[2] += $"G:{player.GroupID} ";
                if (player.KDA != -1f) lines[2] += $"KD: {player.KDA.ToString("n1")} ";
            }
            else return; // Cancel drawing, not interested in this player object
            // Strings constructed -> Begin Drawing
            float spacing = 3 * UIScale;
            float maxLength = 0;
            foreach (var line in lines)
            {
                var length = SKPaints.TextBoss.MeasureText(line);
                if (length > maxLength) maxLength = length;
            }
            var backer = new SKRect()
            {
                Bottom = Y + (3 + lines.Length * (lines.Length + 6)) * UIScale,
                Left = X + (9 * UIScale),
                Top = Y - (9 * UIScale),
                Right = X + (9 * UIScale) + maxLength + (6 * UIScale)
            };
            canvas.DrawRect(backer, SKPaints.PaintTransparentBacker); // Draw tooltip backer
            foreach (var line in lines) // Draw tooltip text
            {
                canvas.DrawText(line, this.GetPoint(11 * UIScale, spacing), SKPaints.TextWhite); // draw line text
                spacing += 12 * UIScale;
            }
        }
    }
    /// <summary>
    /// Contains long/short names for player gear.
    /// </summary>
    public class GearItem
    {
        public string Long { get; init; }
        public string Short { get; init; }
    }

    /// <summary>
    /// Defines a Map for use in the GUI.
    /// </summary>
    public class Map
    {
        /// <summary>
        /// Name of map (Ex: Customs)
        /// </summary>
        public readonly string Name;
        /// <summary>
        /// 'MapConfig' class instance
        /// </summary>
        public readonly MapConfig ConfigFile;
        /// <summary>
        /// File path to Map .JSON Config
        /// </summary>
        public readonly string ConfigFilePath;

        public Map(string name, MapConfig config, string configPath)
        {
            Name = name;
            ConfigFile = config;
            ConfigFilePath = configPath;
        }
    }

    /// <summary>
    /// Contains multiple map parameters used by the GUI.
    /// </summary>
    public class MapParameters
    {
        /// <summary>
        /// Contains the Skia Interface (UI) Scaling Value.
        /// </summary>
        public float UIScale;
        /// <summary>
        /// Contains the 'index' of which map layer to display.
        /// For example: Labs has 3 floors, so there is a Bitmap image for 'each' floor.
        /// Index is dependent on LocalPlayer height.
        /// </summary>
        public int MapLayerIndex;
        /// <summary>
        /// Rectangular 'zoomed' bounds of the Bitmap to display.
        /// </summary>
        public SKRect Bounds;
        /// <summary>
        /// Regular -> Zoomed 'X' Scale correction.
        /// </summary>
        public float XScale;
        /// <summary>
        /// Regular -> Zoomed 'Y' Scale correction.
        /// </summary>
        public float YScale;
    }

    /// <summary>
    /// Defines a .JSON Map Config File
    /// </summary>
    public class MapConfig
    {
        [JsonIgnore]
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions()
        {
            WriteIndented = true,
        };

        [JsonPropertyName("mapID")]
        public List<string> MapID { get; set; } // New property for map IDs

        [JsonPropertyName("x")]
        public float X { get; set; }

        [JsonPropertyName("y")]
        public float Y { get; set; }

        [JsonPropertyName("scale")]
        public float Scale { get; set; }

        // Updated to match new JSON format
        [JsonPropertyName("mapLayers")]
        public List<MapLayer> MapLayers { get; set; }

        public static MapConfig LoadFromFile(string file)
        {
            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<MapConfig>(json, _jsonOptions);
        }

        public void Save(Map map)
        {
            var json = JsonSerializer.Serialize(this, _jsonOptions);
            File.WriteAllText(map.ConfigFilePath, json);
        }
    }

    public class MapLayer
    {
        [JsonPropertyName("minHeight")]
        public float MinHeight { get; set; }

        [JsonPropertyName("filename")]
        public string Filename { get; set; }
    }
    /// <summary>
    /// Represents a PMC in the PMC History log.
    /// </summary>
    public class PlayerHistoryEntry
    {
        private readonly string _id;
        private readonly ListViewItem _view;
        /// <summary>
        /// Entry text
        /// </summary>
        public string Entry { get; }
        /// <summary>
        /// For insertion into a ListView control.
        /// </summary>
        public ListViewItem View
        {
            get => _view;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="id">Player BSG ID.</param>
        /// <param name="entry">Full History log entry.</param>
        public PlayerHistoryEntry(string id, string entry)
        {
            _id = id;
            Entry = entry;
            var view = new ListViewItem(
            new string[2]
            {
                entry,
                id
            });
            view.Tag = this; // Store ref to this object
            _view = view;
        }

        /// <summary>
        /// Returns player Acct ID.
        /// </summary>
        public override string ToString()
        {
            return _id;
        }
    }
    #endregion

    #region Memory Classes
    /// <summary>
    /// Top level object defining a scatter read operation. Create one of these in a local context.
    /// </summary>
    public class ScatterReadMap
    {
        private readonly List<ScatterReadRound> Rounds = new();
        /// <summary>
        /// Contains results from Scatter Read after Execute() is performed. First key is Index, Second Key ID.
        /// </summary>
        public Dictionary<int, Dictionary<int, ScatterReadEntry>> Results = new();

        /// <summary>
        /// Executes Scatter Read operation as defined per the map.
        /// </summary>
        public void Execute(int indexCount)
        {
            for (int i = 0; i < indexCount; i++) // Add dict for each index
            {
                Results.Add(i, new Dictionary<int, ScatterReadEntry>());
            }
            foreach (var round in Rounds)
            {
                round.Run();
            }
        }
        /// <summary>
        /// Add scatter read rounds to the operation. Each round is a successive scatter read, you may need multiple
        /// rounds if you have reads dependent on earlier scatter reads result(s).
        /// </summary>
        /// <returns>ScatterReadRound object.</returns>
        public ScatterReadRound AddRound()
        {
            var round = new ScatterReadRound(this);
            Rounds.Add(round);
            return round;
        }
    }

    /// <summary>
    /// Defines a scatter read round. Each round will execute a single scatter read. If you have reads that
    /// are dependent on previous reads (chained pointers for example), you may need multiple rounds.
    /// </summary>
    public class ScatterReadRound
    {
        private readonly ScatterReadMap _map;
        private readonly List<ScatterReadEntry> Entries = new();
        public ScatterReadRound(ScatterReadMap map)
        {
            _map = map;
        }

        /// <summary>
        /// Adds a single Scatter Read Entry.
        /// </summary>
        /// <param name="index">For loop index this is associated with.</param>
        /// <param name="id">Random ID number to identify the entry's purpose.</param>
        /// <param name="addr">Address to read from (you can pass a ScatterReadEntry from an earlier round, 
        /// and it will use the result).</param>
        /// <param name="type">Type of object to read.</param>
        /// <param name="size">Size of oject to read (ONLY for reference types, value types get size from
        /// Type). You canc pass a ScatterReadEntry from an earlier round and it will use the Result.</param>
        /// <param name="offset">Optional offset to add to address (usually in the event that you pass a
        /// ScatterReadEntry to the Addr field).</param>
        /// <returns></returns>
        public ScatterReadEntry AddEntry(int index, int id, object addr, Type type, object size = null, uint offset = 0x0)
        {
            if (size is null) size = (int)0;
            var entry = new ScatterReadEntry()
            {
                Index = index,
                Id = id,
                Addr = addr,
                Type = type,
                Size = size,
                Offset = offset
            };
            Entries.Add(entry);
            return entry;
        }

        /// <summary>
        /// Internal use only do not use.
        /// </summary>
        public void Run()
        {
            Memory.ReadScatter(Entries.ToArray(), _map.Results);
        }
    }

    /// <summary>
    /// Single scatter read entry. Use ScatterReadRound.AddEntry() to construct this class.
    /// </summary>
    public class ScatterReadEntry
    {
        /// <summary>
        /// for loop index this is associated with
        /// </summary>
        public int Index;
        /// <summary>
        /// Random identifier code (1 = PlayerBase, 2 = PlayerProfile, etc.)
        /// </summary>
        public int Id;
        /// <summary>
        /// Can be an ulong or another ScatterReadEntry
        /// </summary>
        public object Addr = (ulong)0x0;
        /// <summary>
        /// Offset amount to be added to Address.
        /// </summary>
        public uint Offset = 0x0;
        /// <summary>
        /// Defines the type. For value types is also used to determine the size.
        /// </summary>
        public Type Type;
        /// <summary>
        /// Can be an int32 or another ScatterReadEntry
        /// </summary>
        public object Size;
        /// <summary>
        /// Multiplies size by this value (ex: unity strings *2). Default: 1
        /// </summary>
        public int SizeMult = 1;
        /// <summary>
        /// Result is stored here, must cast to unbox.
        /// </summary>
        public object Result;
    }
    #endregion

    #region Custom EFT Classes
    /// <summary>
    /// Contains weapon info for Primary Weapons.
    /// </summary>
    public struct PlayerWeaponInfo
    {
        public string ThermalScope;
        public string AmmoType;

        public override string ToString()
        {
            var result = string.Empty;
            if (AmmoType is not null) result += AmmoType;
            if (ThermalScope is not null)
            {
                if (result != string.Empty) result += $", {ThermalScope}";
                else result += ThermalScope;
            }
            if (result == string.Empty) return null;
            return result;
        }
    }
    /// <summary>
    /// Defines Player Unit Type (Player,PMC,Scav,etc.)
    /// </summary>
    public enum PlayerType
    {
        /// <summary>
        /// Default value if a type cannot be established.
        /// </summary>
        Default,
        /// <summary>
        /// The primary player running this application/radar.
        /// </summary>
        LocalPlayer,
        /// <summary>
        /// Teammate of LocalPlayer.
        /// </summary>
        Teammate,
        /// <summary>
        /// Hostile/Enemy PMC.
        /// </summary>
        PMC,
        /// <summary>
        /// Normal AI Bot Scav.
        /// </summary>
        AIScav,
        /// <summary>
        /// Difficult AI Raider.
        /// </summary>
        AIRaider,
        /// <summary>
        /// Difficult AI Boss.
        /// </summary>
        AIBoss,
        /// <summary>
        /// Player controlled Scav.
        /// </summary>
        PScav,
        /// <summary>
        /// 'Special' Human Controlled Hostile PMC/Scav (on the watchlist, or a special account type).
        /// </summary>
        SpecialPlayer,
        /// <summary>
        /// Hostile/Enemy BEAR PMC.
        /// </summary>
        BEAR,
        /// <summary>
        /// Hostile/Enemy USEC PMC.
        /// </summary>
        USEC
    }
    /// <summary>
    /// Defines Role for an AI Bot Player.
    /// </summary>
    public struct AIRole
    {
        /// <summary>
        /// Name of Bot Player.
        /// </summary>
        public string Name;
        /// <summary>
        /// Type of Bot Player.
        /// </summary>
        public PlayerType Type;
    }
    #endregion

    #region EFT Enums
    /// <summary>
    /// Defines 'type' of AI Bot as determined by reading Offsets.PlayerSettings.Role
    /// </summary>
    public enum WildSpawnType : int // EFT.WildSpawnType
    {
        /// <summary>
        /// Sniper Scav.
        /// </summary>
        marksman = 1,

        /// <summary>
        /// Regular Scav.
        /// </summary>
        assault = 2,

        /// <summary>
        /// ???
        /// </summary>
        bossTest = 4,

        /// <summary>
        /// Reshala
        /// </summary>
        bossBully = 8,

        /// <summary>
        /// ???
        /// </summary>
        followerTest = 16,

        /// <summary>
        /// Reshala Guard.
        /// </summary>
        followerBully = 32,

        /// <summary>
        /// Killa
        /// </summary>
        bossKilla = 64,

        /// <summary>
        /// Shturman
        /// </summary>
        bossKojaniy = 128,

        /// <summary>
        /// Shturman Guard.
        /// </summary>
        followerKojaniy = 256,

        /// <summary>
        /// AI Raider
        /// </summary>
        pmcBot = 512,

        /// <summary>
        /// Normal Scav (cursed)
        /// </summary>
        cursedAssault = 1024,

        /// <summary>
        /// Gluhar
        /// </summary>
        bossGluhar = 2048,

        /// <summary>
        /// Gluhar Guard (Assault)
        /// </summary>
        followerGluharAssault = 4096,

        /// <summary>
        /// Gluhar Guard (Security)
        /// </summary>
        followerGluharSecurity = 8192,

        /// <summary>
        /// Gluhar Guard (Scout)
        /// </summary>
        followerGluharScout = 16384,

        /// <summary>
        /// Gluhar Guard (Sniper)
        /// </summary>
        followerGluharSnipe = 32768,

        /// <summary>
        /// Sanitar Guard
        /// </summary>
        followerSanitar = 65536,

        /// <summary>
        /// Sanitar
        /// </summary>
        bossSanitar = 131072,

        /// <summary>
        /// ???
        /// </summary>
        test = 262144,

        /// <summary>
        /// ???
        /// </summary>
        assaultGroup = 524288,

        /// <summary>
        /// Cultist
        /// </summary>
        sectantWarrior = 1048576,

        /// <summary>
        /// Cultist Priest (Boss)
        /// </summary>
        sectantPriest = 2097152,

        /// <summary>
        /// Tagilla
        /// </summary>
        bossTagilla = 4194304,

        /// <summary>
        /// Tagilla Guard?
        /// </summary>
        followerTagilla = 8388608,

        /// <summary>
        /// USEC Rogues
        /// </summary>
        exUsec = 16777216,

        /// <summary>
        /// Santa
        /// </summary>
        gifter = 33554432
    };

    [Flags]
    public enum MemberCategory : int
    {
        Default = 0, // Standard Account
        Developer = 1,
        UniqueId = 2, // EOD Account
        Trader = 4,
        Group = 8,
        System = 16,
        ChatModerator = 32,
        ChatModeratorWithPermamentBan = 64,
        UnitTest = 128,
        Sherpa = 256,
        Emissary = 512
    }
    #endregion
}
