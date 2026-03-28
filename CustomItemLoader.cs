using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions; // Regex for stripping colors
using System.Text;
using System;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Oxide.Core.Plugins;
using Oxide.Core;
using UnityEngine;
using Oxide.Game.Rust.Cui;

namespace Oxide.Plugins
{
    [Info("CustomItemLoader", "EchoChamber", "1.00.000")] // バージョンを明確化
    [Description("Loads and manages custom item definitions (JSON) with effect verification, UI, and logging.")]
    public class CustomItemLoader : RustPlugin
    {
    // === Added: JSON watcher safety ===
    static readonly System.Collections.Generic.HashSet<string> JsonWatchIgnore =
        new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "prefab_categories.json",
            "effects_verified.json",
            "verified_effects.json",
            "jsoncheck_result.json"
        };

    private readonly System.Collections.Generic.Dictionary<string, System.DateTime> _lastJsonTouch =
        new System.Collections.Generic.Dictionary<string, System.DateTime>(System.StringComparer.OrdinalIgnoreCase);

    private const int JsonReloadDebounceMs = 800;
    // === /Added ===


        // --- helper: safe upsert for preset effect mappings (uMod-safe) ---
        private void PresetEffect(string key, string prefabPath)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(prefabPath)) return;
            // presetEffectPaths is readonly and initialized at declaration
            presetEffectPaths[key] = prefabPath;
        }


        // === uMod publication: Lang messages (EN/JA) ===
        protected override void LoadDefaultMessages()
        {
            var en = new Dictionary<string, string>
            {
                ["NoPermission"] = "You do not have permission.",
                ["GiveUsage"] = "Usage: /cil give <shortname> [amount]"
            };
            lang.RegisterMessages(en, this);

            var ja = new Dictionary<string, string>
            {
                ["NoPermission"] = "このコマンドを実行する権限がありません。",
                ["GiveUsage"] = "使い方: /cil give <shortname> [amount]"
            };
            lang.RegisterMessages(ja, this, "ja");
        }

        private string Lang(string key, string playerId = null, params object[] args)
        {
            try { return string.Format(lang.GetMessage(key, this, playerId), args); }
            catch { return key; }
        }

    private const string EmbeddedPrefabList = @"# Minimal seed (slim)
# いくつかの代表的なプレハブのみ
[effects]
assets/bundled/prefabs/fx/impacts/add_wood.prefab
assets/bundled/prefabs/fx/impacts/add_metal.prefab
assets/prefabs/tools/flashlight/effects/turn_on.prefab
assets/prefabs/deployable/lantern/effects/lantern_on.prefab
";
        // format: "<effectKind>:<shortname>" → PrefabPath
        // JSONで customEffects を書かなくてもこの表に記述しておけば自動で補完される（例: "ignite:lightsaber" など）
        // CustomItemLoader.cs (構造強化・安定版)
        // 自動整備: using順最適化・階層バランス修正・防御補完追加
        // CustomItemLoader.cs v1.00.000 (integrated logging full suite)

        #region フィールド定義
        // --- Auto-update for prefab_categories.json & extended fx list ---
        private HashSet<string> fxCategoryEffects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Timer fxCatSaveDebounce;
        private bool cfgAutoUpdatePrefabCategories = true; // config

        [PluginReference] private Plugin EpicLoot;  // Bridge用参照
        [PluginReference] private Plugin ItemPerks; // Bridge用参照
        [PluginReference] private Plugin ZLevelsRemastered; // ZL連携参照

        // 注意：
        // XPerience の関数は多くが object を返すことがあるため、型変換に注意が必要です。
        // Skills のスキル名は文字列（例："mining"）指定です。
        private PluginConfig config;
        private System.Random rng;
        private string currentLogFilter = null; // null=ALL
        private readonly Queue<string> cilMsgBuffer = new Queue<string>();
        private int CilBufferSize => Math.Max(50, (config?.LogBufferSize ?? 300));
        private bool debugMode = false; // デバッグモードの初期値
        private readonly HashSet<string> missingPrefabsWarned = new();
        private readonly HashSet<string> fallbackNoticeSent = new();
        private readonly Dictionary<string, string[]> effectFallbackChain = new(StringComparer.OrdinalIgnoreCase)
        {
            { "parry", new[] { "critical", "swing" } },
            { "critical", new[] { "swing" } },
            { "swing", new[] { "ignite", "activate" } },
            { "ignite", new[] { "activate" } },
            { "activate", new[] { "ignite" } },
            { "glow", new[] { "ignite" } },
            { "glowcore", new[] { "glow" } },
            { "glowaura", new[] { "glow" } },
            { "glowspark", new[] { "glow", "ignite" } },
            { "hum", new[] { "glow", "ignite" } },
            { "deactivate", new[] { "parry", "critical" } }
        };
        private readonly Dictionary<string, CustomItemDefinition> customItems = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> shortnameSource = new(StringComparer.OrdinalIgnoreCase);
        private const string DataFolderName = "CustomItemLoader"; // データフォルダ名
        private string DataFolderPath => Path.Combine(Interface.Oxide.DataDirectory, DataFolderName);
        private const string LIGHTSABER_SHORTNAME = "lightsaber"; // デフォルトのライトセーバーショートネーム
        private readonly HashSet<ulong> equipped = new HashSet<ulong>(); // 装備中のプレイヤーID
        private readonly Dictionary<ulong, Timer> holdLoopTimers = new(); // ホールドエフェクト用タイマー
        // 疑似ループ用タイマー（glow/hum）
        private readonly Dictionary<ulong, Timer> glowLoopTimers = new();
        private readonly Dictionary<ulong, Timer> humLoopTimers  = new();

        // === JSON Auto-Reload Watcher ===
        private System.IO.FileSystemWatcher jsonWatcher;
        private DateTime jsonLastChangeUtc;
        private bool jsonReloadQueued;
        
                private readonly Dictionary<ulong, Timer> ambientLoopTimers = new Dictionary<ulong, Timer>();
private readonly Dictionary<ulong, UnityEngine.GameObject> handGlow = new Dictionary<ulong, UnityEngine.GameObject>();
// ビームデータ構造体
        private class BeamData
        {
            public GameObject go;
            public LineRenderer lr;
            public Vector3 currentEnd;
            public Timer timer;
        }
private readonly Dictionary<ulong, BeamData> activeBeams = new();
        private bool pendingEpicLootExport = false;
        private bool pendingItemPerksExport = false;
        private readonly HashSet<string> hookWarned = new(); // Bridge hook 呼び出し警告済み
        private readonly Dictionary<ulong, List<ItemModProjectile>> playerProjectileMods = new();

        
        // エフェクト検証状態（alias -> EffectInfo）
        private readonly Dictionary<string, EffectInfo> effectRegistry
            = new Dictionary<string, EffectInfo>(StringComparer.OrdinalIgnoreCase);
#endregion

        #region プラグインライフサイクル

        private T GetConfig<T>(string key, T def)
        {
            try
            {
                if (Config[key] is T v) return v;
                return (T)System.Convert.ChangeType(Config[key], typeof(T));
            }
            catch { }
            Config[key] = def;
            SaveConfig();
            return def;
        }
void Init()
        {
rng = new System.Random();
            permission.RegisterPermission("customitemloader.forcejump", this); // forcejump コマンドの権限を明示
            // チャットコマンド登録（RustPlugin 用）
            cmd.AddChatCommand("forcejump", this, "CommandForceJump");
            cmd.AddChatCommand("cil",       this, "CmdCil");
            cmd.AddChatCommand("cilist",    this, "CmdCil");

            LoadConfig();
            cfgAutoShowItemInfoUI = (config?.AutoShowItemInfoUI) ?? true;
            cfgItemInfoAutoHideSeconds = (config?.ItemInfoAutoHideSeconds) ?? 25f;
            cfgAutoUpdatePrefabCategories = (config?.AutoUpdatePrefabCategories) ?? true;
            CIL_LoadPrefabCategories();
            CIL_CleanupPrefabCategories(true);
        
            permission.RegisterPermission("customitemloader.give", this);
            cmd.AddConsoleCommand("cil.give", this, "CmdCilGiveConsole");
}

        private void OnPluginLoaded(Plugin pl)
        {
            if (pl?.Name == "EpicLoot")
            {
                EpicLoot = pl;
                pendingEpicLootExport = true;
            }
            else if (pl?.Name == "ItemPerks")
            {
                ItemPerks = pl;
                pendingItemPerksExport = true;
            }
            AttemptExports(); // ロードされたプラグインへのエクスポートを試みる
        }

        private void OnServerInitialized()
        {
            StartJsonWatcher();
        
            
            EnsureDataFolder();
            GenerateLightsaberTemplate();
// プリセットの初期化（起動時に一度）
            InitPresetEffects();
// --- Prefab Categories 初期生成 ---
        string prefabCategoriesPath = Path.Combine(Interface.Oxide.DataDirectory, "CustomItemLoader", "prefab_categories.json");
        if (!File.Exists(prefabCategoriesPath))
        {
            var defaultData = new Dictionary<string, List<string>>
            {
                { "effects", new List<string>
                    {
                        "assets/bundled/prefabs/fx/impacts/add_cloth.prefab",
                        "assets/bundled/prefabs/fx/impacts/add_metal.prefab",
                        "assets/bundled/prefabs/fx/impacts/add_wood.prefab"
                    }
                }
            };

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(prefabCategoriesPath));
                File.WriteAllText(prefabCategoriesPath, JsonConvert.SerializeObject(defaultData, Formatting.Indented));
                LogCil("Init", $"[初回生成] prefab_categories.json を作成しました: {prefabCategoriesPath}");
            }
            catch (Exception ex)
            {
                LogCil("Init", $"[エラー] prefab_categories.json の初期生成に失敗: {ex.Message}");
            }
        }

        // 既知エフェクトリストを更新
        PopulateKnownFxList();
        // --- FXリスト初期更新 ---
        try
        {
            string fxListPath = Path.Combine(Interface.Oxide.DataDirectory, "CustomItemLoader", "fxlist.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(fxListPath));

            var namesFromPool = (GameManifest.Current?.pooledStrings != null
                ? GameManifest.Current.pooledStrings.Select(x => Convert.ToString(x))
                : System.Linq.Enumerable.Empty<string>());
            var allFx = namesFromPool
                .Where(p => !string.IsNullOrEmpty(p) && p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) && p.Contains("/fx/"))
                .OrderBy(p => p)
                .ToList();

            // Fallback: if pooledStrings did not yield paths, use prefab_categories.json + FindPrefab
            if (allFx.Count == 0)
            {
                try
                {
                    string jsonPath = Path.Combine(Interface.Oxide.DataDirectory, "CustomItemLoader", "prefab_categories.json");
                    if (File.Exists(jsonPath))
                    {
                        var data = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(jsonPath));
                        if (data != null)
                        {
                            var gm2 = GameManager.server;
                            allFx = data.Values
                                .Where(v => v != null)
                                .SelectMany(v => v)
                                .Where(p => !string.IsNullOrEmpty(p) && gm2 != null && gm2.FindPrefab(p) != null)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .OrderBy(p => p)
                                .ToList();
                        }
                    }
                }
                catch { /* ignore fallback errors */ }
            }

            File.WriteAllLines(fxListPath, allFx);

            LogCil("Init", $"fxlist.txt を更新しました（{allFx.Count} 件）: {fxListPath}");

            // 既知FXリストも更新
            knownFxNames.Clear(); foreach (var _fx in allFx) knownFxNames.Add(_fx); foreach (var e in fxCategoryEffects) knownFxNames.Add(e);
        }
        catch (Exception ex)
        {
            LogCil("Init", $"fxlist.txt 更新に失敗: {ex.Message}");
        }

    
            LogInfo("OnServerInitialized -> ValidateAllEffects");

            // データフォルダとテンプレートファイルの確認・生成（開始時に実施済み）

            LoadAllCustomItems();
            InitializeLightsaberEffects(); // エフェクト初期検証

            // 起動時ログ初期フィルタ設定
            if (!string.IsNullOrEmpty(config.LogDefaultFilter))
                currentLogFilter = config.LogDefaultFilter.ToUpperInvariant();

            timer.Once(10f, () =>
            {
                LogInfo("Delayed ValidateAllEffects (10s)");
                ValidateAllEffectsPhase(); // 分離メソッド呼び出し
                LogInfo( "Delayed ValidateAllEffects done");
            // サンプルJSON生成＆エラーチェックは初期化最後だけ実施
            
            

            });
        }

        private void Unload()
        {
            StopJsonWatcher(); // アクティブなビームを全て停止
            foreach (var kvp in activeBeams)
            {
                StopPersistentBeam(BasePlayer.FindByID(kvp.Key));
            }
            activeBeams.Clear();

            // ホールドループタイマーを全て停止
            foreach (var kvp in holdLoopTimers)
            {
                kvp.Value?.Destroy();
            }
            holdLoopTimers.Clear();

            // glow/hum 疑似ループを全停止
            foreach (var kv in glowLoopTimers) { kv.Value?.Destroy(); }
            glowLoopTimers.Clear();
            foreach (var kv in humLoopTimers) { kv.Value?.Destroy(); }
            humLoopTimers.Clear();

            // 装備解除時にパッシブエフェクトをリセット
            foreach (ulong userID in equipped.ToList()) // ToList() でコピーを作成して反復処理中にコレクション変更を防ぐ
            {
                BasePlayer player = BasePlayer.FindByID(userID);
                if (player != null)
                {
                    RemovePassiveEffects(player);
                }
            }
            equipped.Clear();

            LogInfo( "CustomItemLoader unloaded.");
        }

        #endregion

        #region Config (設定管理)

        protected override void LoadDefaultConfig()
        {
            LogInfo( "Creating a new configuration file.");
            config = new PluginConfig();
            Config.WriteObject(config, true);
            // デフォルト値を設定
            config.LightsaberShortname = "lightsaber";
            config.PersistentBeam = false;
            config.BeamWidthStart = 0.08f;
            config.BeamWidthEnd = 0.08f;
            config.BeamLength = 1.5f;
            config.BeamUpdateInterval = 0.05f;
            config.BeamJitter = 0.01f;
            config.BeamSmoothing = 0.5f;
            config.BeamColor = "#4FD4FF"; // デフォルト色

            config.EnableEffectFallback = true;

            config.ForceJumpUpwardVelocity = 10f;
            config.ForceJumpForwardBoost = 5f;

            // エフェクトのデフォルトパス
            config.IgniteEffect = "assets/prefabs/tools/flashlight/effects/turn_on.prefab";
            config.GlowLoopEffect = "assets/prefabs/tools/flashlight/effects/turn_on.prefab";
            config.HumLoopEffect = "assets/prefabs/tools/flashlight/effects/turn_on.prefab";
            config.SwingEffect = "assets/bundled/prefabs/fx/ricochet/ricochet1.prefab";
            config.CriticalEffect = "assets/bundled/prefabs/fx/ricochet/ricochet1.prefab";
            config.ParryEffect = "assets/bundled/prefabs/fx/ricochet/ricochet1.prefab";
            config.ActivateEffectOverride = ""; // 空文字でデフォルトエフェクトを使用
            config.DeactivateEffectOverride = ""; // 空文字でデフォルトエフェクトを使用

            // ログ設定
            config.LogBufferSize = 300;
            config.LogDefaultFilter = ""; // デフォルトはフィルタなし
            config.LogAutoAppendFile = false;
            config.LogStripColor = true;
            config.LogAutoFileName = "cil_log.txt";

            
            // extra defaults for UI/Auto
            config.AutoShowItemInfoUI = true;
            config.ItemInfoAutoHideSeconds = 25f;
            config.AutoUpdatePrefabCategories = true;
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<PluginConfig>();
                if (config == null)
                {
                    LogWarn("[Config] 設定ファイルが空/破損のためデフォルトを生成します: oxide/config/CustomItemLoader.json");
                    LoadDefaultConfig();
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to load config: {ex.Message}. Loading default config.");
                LoadDefaultConfig();
                SaveConfig();
            }

            // typed -> runtime fields
            cfgAutoShowItemInfoUI = (config?.AutoShowItemInfoUI) ?? true;
            cfgItemInfoAutoHideSeconds = (config?.ItemInfoAutoHideSeconds) ?? 25f;
            cfgAutoUpdatePrefabCategories = (config?.AutoUpdatePrefabCategories) ?? true;

            // after cfg is set, load & cleanup categories
            CIL_LoadPrefabCategories();
            CIL_CleanupPrefabCategories(true);

            LogInfo($"Config loaded. LogBufferSize: {config.LogBufferSize}");
        }


        protected override void SaveConfig()
        {
            if (config == null)
            {
                Puts("[Config] Skip SaveConfig because 'config' is null (delayed load).");
                return;
            }
            Config.WriteObject(config, true);
        }


        public class PluginConfig
        {
// --- NICE mode: ライトセイバーのプリセットを派手寄りに上書き（名称は変えない） ---
[JsonProperty("NiceMode")]
public bool NiceMode { get; set; } = true;

            // ライトセーバー関連
            [JsonProperty("LightsaberShortname")]
            public string LightsaberShortname { get; set; } = "lightsaber";

            // ビーム関連
            [JsonProperty("PersistentBeam")]
            public bool PersistentBeam { get; set; } = false;
            [JsonProperty("BeamWidthStart")]
            public float BeamWidthStart { get; set; } = 0.08f;
            [JsonProperty("BeamWidthEnd")]
            public float BeamWidthEnd { get; set; } = 0.08f;
            [JsonProperty("BeamLength")]
            public float BeamLength { get; set; } = 1.5f;
            [JsonProperty("BeamUpdateInterval")]
            public float BeamUpdateInterval { get; set; } = 0.05f;
            [JsonProperty("BeamJitter")]
            public float BeamJitter { get; set; } = 0.01f;
            [JsonProperty("BeamSmoothing")]
            public float BeamSmoothing { get; set; } = 0.5f;
            [JsonProperty("BeamColor")]
            public string BeamColor { get; set; } = "#4FD4FF";

            // 動作フラグ
            [JsonProperty("EnableEffectFallback")]
            public bool EnableEffectFallback { get; set; } = true;

            // フォースジャンプ
            [JsonProperty("ForceJumpUpwardVelocity")]
            public float ForceJumpUpwardVelocity { get; set; } = 10f;
            [JsonProperty("ForceJumpForwardBoost")]
            public float ForceJumpForwardBoost { get; set; } = 5f;

            // エフェクトプレハブパス (デフォルト)
            [JsonProperty("IgniteEffect")]
            public string IgniteEffect { get; set; } = "assets/prefabs/tools/flashlight/effects/turn_on.prefab";
            [JsonProperty("GlowLoopEffect")]
            public string GlowLoopEffect { get; set; } = "assets/prefabs/deployable/lantern/effects/lantern_on.prefab";
            [JsonProperty("HumLoopEffect")]
            public string HumLoopEffect { get; set; } = "assets/prefabs/tools/flashlight/effects/turn_on.prefab";
            [JsonProperty("SwingEffect")]
            public string SwingEffect { get; set; } = "assets/bundled/prefabs/fx/weapon/sword_swoosh.prefab";
            [JsonProperty("CriticalEffect")]
            public string CriticalEffect { get; set; } = "assets/bundled/prefabs/fx/electric_spark.prefab";
            [JsonProperty("ParryEffect")]
            public string ParryEffect { get; set; } = "assets/bundled/prefabs/fx/ricochet/ricochet1.prefab";
            [JsonProperty("ActivateEffectOverride")]
            public string ActivateEffectOverride { get; set; } = "";
            [JsonProperty("DeactivateEffectOverride")]
            public string DeactivateEffectOverride { get; set; } = "";

            // ログ設定
            [JsonProperty("LogBufferSize")]
            public int LogBufferSize { get; set; } = 300;
            [JsonProperty("LogDefaultFilter")]
            public string LogDefaultFilter { get; set; } = "";
            [JsonProperty("LogAutoAppendFile")]
            public bool LogAutoAppendFile { get; set; } = false;
            [JsonProperty("LogStripColor")]
            public bool LogStripColor { get; set; } = true;
            [JsonProperty("LogAutoFileName")]
public string LogAutoFileName { get; set; } = "cil_log.txt";


// == UI / Auto settings ==
[JsonProperty("AutoShowItemInfoUI")]
public bool AutoShowItemInfoUI { get; set; } = true;

[JsonProperty("ItemInfoAutoHideSeconds")]
public float ItemInfoAutoHideSeconds { get; set; } = 25f;

[JsonProperty("AutoUpdatePrefabCategories")]
public bool AutoUpdatePrefabCategories { get; set; } = true;


        }

        // エフェクト情報構造体


        
        // エフェクト情報構造体
        private class EffectInfo
        {
            public string Path;
            public bool Exists;
        }
#endregion

        #region データ構造 (CustomItemDefinition)

        // CustomItemDefinition の正しい定義と内部クラス/プロパティ
        public class CustomItemDefinition
        {
            [JsonProperty("muteAllSounds")]
            public bool muteAllSounds { get; set; } = false;
            [JsonProperty("shortname")]
            public string shortname { get; set; }
            [JsonProperty("parentShortname")]
            public string parentShortname { get; set; }
            [JsonProperty("maxStackSize")]
            public int maxStackSize { get; set; } = 1;
            [JsonProperty("category")]
            public string category { get; set; } = "Weapon";
            [JsonProperty("defaultName")]
            public string defaultName { get; set; }
            [JsonProperty("defaultSkinId")]
            public ulong defaultSkinId { get; set; } = 0;
            [JsonProperty("dropOnDeath")]
            public bool dropOnDeath { get; set; } = false;
            [JsonProperty("tags")]
            public string[] tags { get; set; }

            // パッシブ効果 (Dictionary<string, float> のみ)
            [JsonProperty("passiveEffects")]
            public Dictionary<string, float> passiveEffects { get; set; }

            // PerkとLoot (EpicLoot/ItemPerks連携用)
            [JsonProperty("perks")]
            public Dictionary<string, float> perks { get; set; }
            [JsonProperty("exportPerks")] // export用perks (perksと重複しないように)
            public Dictionary<string, float> exportPerks { get; set; }

            [JsonProperty("loots")]
            public Dictionary<string, float> loots { get; set; }

            // セットボーナス (EpicLoot用)
            [JsonProperty("setId")]
            public string setId { get; set; }
            [JsonProperty("setBonuses")]
            public Dictionary<int, string> setBonuses { get; set; } // threshold, perk_id

            [JsonProperty("description")]
            public string description { get; set; }
            [JsonProperty("showInfoOnGive")]
            public bool showInfoOnGive { get; set; } = true;
            [JsonProperty("rarity")]
            public string rarity { get; set; }

            // 視覚効果
            [JsonProperty("visualEffects")]
            public VisualEffects visualEffects { get; set; }

            // カスタムエフェクト
            [JsonProperty("customEffects")]
            public CustomEffects customEffects { get; set; }

            // エフェクトパスのオーバーライド (定義自体でパスを指定)
            [JsonProperty("igniteEffect")]
            public string igniteEffect { get; set; }
            [JsonProperty("glowLoopEffect")]
            public string glowLoopEffect { get; set; }
            [JsonProperty("glowCoreEffect")]
            public string glowCoreEffect { get; set; }
            [JsonProperty("glowAuraEffect")]
            public string glowAuraEffect { get; set; }
            [JsonProperty("glowSparkEffect")]
            public string glowSparkEffect { get; set; }
            [JsonProperty("humLoopEffect")]
            public string humLoopEffect { get; set; }
            [JsonProperty("swingEffect")]
            public string swingEffect { get; set; }
            [JsonProperty("criticalEffect")]
            public string criticalEffect { get; set; }
            [JsonProperty("parryEffect")]
            public string parryEffect { get; set; }

            [JsonProperty("additionalPerkChances")] // ItemPerks用
            public Dictionary<string, float> additionalPerkChances { get; set; }

            
            
            // --- UI 表示用 任意説明文 ---
            [JsonProperty("descriptionLines")]
            public List<string> descriptionLines { get; set; }
            // --- UI 表示用 任意ステータス（表示名→値）---
            [JsonProperty("uiStats")]
            public Dictionary<string, string> uiStats { get; set; }

            // --- UI 色/アイコン（任意） ---
            [JsonProperty("uiHeaderColor")] public string uiHeaderColor { get; set; } = "#7FE9FF";
            [JsonProperty("uiAccentColor")] public string uiAccentColor { get; set; } = "#FFA500";
            [JsonProperty("iconTint")] public string iconTint { get; set; } = "#7FE9FF";
            [JsonProperty("iconSprite")] public string iconSprite { get; set; } // 例: assets/content/icons/???
// --- ZLevels 連携（任意） ---
            [JsonProperty("zlMinLevel")] // 例: {"mining":10,"woodcutting":5}
            public Dictionary<string, double> zlMinLevel { get; set; }
            [JsonProperty("zlGatherBonusPct")] // 0.15 = +15% 追加採取（ZLの処理後に上乗せ）
            public float zlGatherBonusPct { get; set; } = 0f;
// 特定のエフェクトオーバーライドがあるかチェック
            public bool HasAnyEffectOverride()
            {
                return !(string.IsNullOrEmpty(igniteEffect)
                        && string.IsNullOrEmpty(glowLoopEffect)
                        && string.IsNullOrEmpty(humLoopEffect)
                        && string.IsNullOrEmpty(swingEffect)
                        && string.IsNullOrEmpty(criticalEffect)
                        && string.IsNullOrEmpty(parryEffect)
                        && string.IsNullOrEmpty(glowCoreEffect)
                        && string.IsNullOrEmpty(glowAuraEffect)
                        && string.IsNullOrEmpty(glowSparkEffect));
            }

            public string ListEffectOverrides()
            {
                var list = new List<string>();
                if (!string.IsNullOrEmpty(igniteEffect)) list.Add("ignite");
                if (!string.IsNullOrEmpty(glowLoopEffect)) list.Add("glow");
                if (!string.IsNullOrEmpty(glowCoreEffect)) list.Add("glowcore");
                if (!string.IsNullOrEmpty(glowAuraEffect)) list.Add("glowaura");
                if (!string.IsNullOrEmpty(glowSparkEffect)) list.Add("glowspark");
                if (!string.IsNullOrEmpty(humLoopEffect)) list.Add("hum");
                if (!string.IsNullOrEmpty(swingEffect)) list.Add("swing");
                if (!string.IsNullOrEmpty(criticalEffect)) list.Add("critical");
                if (!string.IsNullOrEmpty(parryEffect)) list.Add("parry");
                return string.Join(",", list);
            }
        }

        public class VisualEffects
        {
            [JsonProperty("bladeColor")]
            public string bladeColor { get; set; } = "#FFFFFF"; // HTMLカラーコード
            [JsonProperty("glowIntensity")]
            public float glowIntensity { get; set; } = 1f;
            [JsonProperty("bladeLength")]
            public float bladeLength { get; set; } = 1.0f;
            [JsonProperty("trailEffect")]
            public bool trailEffect { get; set; } = false;
            [JsonProperty("sparkEffect")]
            public bool sparkEffect { get; set; } = false;
            [JsonProperty("sparkChance")]
            public float sparkChance { get; set; } = 0.05f;
            [JsonProperty("auraStep")]
            public int auraStep { get; set; } = 2;
        }

        public class CustomEffects
        {
            [JsonProperty("glowBurst")]
            public int glowBurst { get; set; } = 3;

            [JsonProperty("glowInterval")]
            public float glowInterval { get; set; } = 0.08f;

            [JsonProperty("glowRadius")]
            public float glowRadius { get; set; } = 0.30f;

            [JsonProperty("glowOffsetY")]
            public float glowOffsetY { get; set; } = 1.0f;

            
            [JsonProperty("glowLoopEffect")]
            public string glowLoopEffect { get; set; }
[JsonProperty("holdEffect")]
            public string holdEffect { get; set; }
            [JsonProperty("holdEffectBone")]
            public string holdEffectBone { get; set; } // 例: "r_hand", "l_hand"
        }

        #endregion

        
// --- CustomItemDefinition 取得ヘルパー（欠落補填） ---
private CustomItemDefinition GetDefinition(string shortname)
{
    if (string.IsNullOrEmpty(shortname)) return null;
    CustomItemDefinition def;
    if (customItems.TryGetValue(shortname, out def)) return def;
    return null;
}

// 既定ライトセーバー定義取得（将来の拡張用の薄いラッパ）
private CustomItemDefinition GetLightsaberDefinition()
{
    string ls = config?.LightsaberShortname ?? LIGHTSABER_SHORTNAME;
    return GetDefinition(ls);
}

#region Oxide Hooks (ゲームイベントフック)

        // アイテム装備時
        private void OnItemEquipped(Item item, BasePlayer player)
        {
            if (item == null || player == null) return;
            CustomItemDefinition def = ResolveDefinitionForItem(item);
            if (def == null) return;

            
            // ZLevels 要求がある場合は装備時にチェック（任意設定）
            if (def.zlMinLevel != null && ZLevelsRemastered != null && ZLevelsRemastered.IsLoaded)
            {
                if (!ZL_MeetsRequirement(player, def.zlMinLevel))
                {
                    SendPlayerMsg(player, "<color=#FFA500>[CIL] 要求ZLevels未達のため装備効果は無効です</color>");
                    return;
                }
            }
if (IsLightsaber(item))
            {
                LogCil("DEBUG", $"Player {player.displayName} equipped lightsaber: {item.info.shortname}");
                equipped.Add(player.userID);
                HandleEquip(player, def);
            }
        }

        // アイテム装備解除時
        private void OnItemUnequipped(Item item, BasePlayer player)
        {
            if (item == null || player == null) return;
            CustomItemDefinition def = ResolveDefinitionForItem(item);
            if (def == null) return;

            if (IsLightsaber(item))
            {
                LogCil("DEBUG", $"Player {player.displayName} unequipped lightsaber: {item.info.shortname}");
                equipped.Remove(player.userID);
                HandleUnequip(player);
            }
        }

        // ダメージ計算前 (パッシブエフェクト用)
        private object OnEntityTakeDamage(BasePlayer victim, HitInfo info)
        {
            if (victim == null || info == null || victim.IsDead()) return null;

            // ダメージ軽減パッシブエフェクト
            if (equipped.Contains(victim.userID))
            {
                CustomItemDefinition def = GetDefinition(config.LightsaberShortname ?? LIGHTSABER_SHORTNAME);
                if (def?.passiveEffects != null && def.passiveEffects.TryGetValue("damagereduction", out float reduction))
                {
                    if (reduction > 0)
                    {
                        info.damageTypes.ScaleAll(1f - reduction);
                        LogCil("DEBUG", $"Applied damage reduction {reduction * 100}% to {victim.displayName}");
                    }
                }
            }
            return null;
        }

        
        // ZLevels 側からの拡張フック（ZLevels 内で Interface.CallHook される）
        private void OnZLevelDispenserGather(ResourceDispenser dispenser, BasePlayer player, Item item, int prevAmount, int newAmount, bool isPowerTool)
        {
            try
            {
                if (player == null || item == null) return;
                var active = player.GetActiveItem();
                if (active == null) return;

                var def = GetDefinition(active.info.shortname);
                if (def == null) return;

                // 任意: JSONの zlGatherBonusPct (0.0〜1.0) を使用して、ZLの適用後に上乗せ
                float pct = def.zlGatherBonusPct;
                if (pct > 0f)
                {
                    int add = (int)Math.Floor(newAmount * pct);
                    if (add > 0)
                    {
                        item.amount = newAmount + add;
                        if (debugMode) LogInfo($"[ZLBridge] {active.info.shortname} 採取ボーナス +{add} ({pct:P0})");
                        var fx = ResolveEffect("glowspark", def);
                        if (!string.IsNullOrEmpty(fx))
                        {
                            SpawnEffect(fx, player.transform.position);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogWarn($"[ZLBridge] OnZLevelDispenserGather 例外: {ex.Message}");
            }
        }

#endregion

        #region メソッド定義

        #region ▶ データ管理・初期化

        private void EnsureDataFolder()
        {
            if (!Directory.Exists(DataFolderPath))
            {
                Directory.CreateDirectory(DataFolderPath);
                LogInfo($"データフォルダ作成: {DataFolderPath}");
            }
        }

        private void GenerateLightsaberTemplate()
        {
            var templateFile = Path.Combine(DataFolderPath, "lightsaber.json");
            if (!File.Exists(templateFile))
            {
                var sampleJson = @"{
  ""muteAllSounds"": true,
  ""shortname"": ""lightsaber"",
  ""parent"": ""longsword"",
  ""stackSize"": 1,
  ""category"": ""Weapon"",
  ""displayName"": ""Lightsaber"",
  ""skinId"": 2420243660
}";
                File.WriteAllText(templateFile, sampleJson.Trim());
                LogInfo("lightsaber.json サンプルJSONを生成しました。");
            }
        }

        /// <summary>
        /// 特定のshortname（例: lightsaber）に対して効果名（igniteなど）とプレハブパスを対応付け、
        /// customEffectsが省略された場合にも、最低限のエフェクトが適用されるようにプリセットする。
        /// 主にデフォルト装備の "lightsaber" 用に自動設定される。
        /// </summary>
        private void InitPresetEffects()
        {
            // Use only confirmed-present prefabs (turn_on + ricochet1)
            PresetEffect("ignite:lightsaber",  "assets/prefabs/tools/flashlight/effects/turn_on.prefab");
            PresetEffect("hum:lightsaber",     "assets/prefabs/tools/flashlight/effects/turn_on.prefab");
            PresetEffect("glow:lightsaber",    "assets/prefabs/tools/flashlight/effects/turn_on.prefab");
            PresetEffect("swing:lightsaber",   "assets/bundled/prefabs/fx/ricochet/ricochet1.prefab"); // fallback: guaranteed spark
            PresetEffect("critical:lightsaber","assets/bundled/prefabs/fx/ricochet/ricochet1.prefab");
            PresetEffect("glowspark:lightsaber","assets/bundled/prefabs/fx/ricochet/ricochet1.prefab");
            PresetEffect("parry:lightsaber",   "assets/bundled/prefabs/fx/ricochet/ricochet1.prefab");
            PresetEffect("activate:lightsaber","assets/prefabs/tools/flashlight/effects/turn_on.prefab");
            PresetEffect("deactivate:lightsaber","assets/bundled/prefabs/fx/ricochet/ricochet1.prefab");
            // aura系は環境依存が強いので既定は置かない（JSONで上書き可）
        }

        
        // --- Start JSON watcher ---
        private void StartJsonWatcher()
        {
            try
            {
                if (jsonWatcher != null) return;
                var dir = DataFolderPath;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                jsonWatcher = new FileSystemWatcher(dir, "*.json");
                jsonWatcher.IncludeSubdirectories = false;
                jsonWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
                jsonWatcher.Changed += OnJsonChanged;
                jsonWatcher.Created += OnJsonChanged;
                jsonWatcher.Renamed += OnJsonRenamed;
                jsonWatcher.EnableRaisingEvents = true;
                LogInfo("[Watcher] data/CustomItemLoader/*.json を監視開始");
            }
            catch (Exception ex)
            {
                LogWarn($"[Watcher] 監視開始に失敗: {ex.Message}");
            }
        }

        private void StopJsonWatcher()
        {
            try
            {
                if (jsonWatcher != null)
                {
                    jsonWatcher.EnableRaisingEvents = false;
                    jsonWatcher.Changed -= OnJsonChanged;
                    jsonWatcher.Created -= OnJsonChanged;
                    jsonWatcher.Renamed -= OnJsonRenamed;
                    jsonWatcher.Dispose();
                    jsonWatcher = null;
                    LogInfo("[Watcher] 監視停止");
                }
            }
            catch { }
        }

        
private void OnJsonChanged(object sender, System.IO.FileSystemEventArgs e)
{
    var name = System.IO.Path.GetFileName(e.FullPath);
    if (!name.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase)) return;
    if (JsonWatchIgnore.Contains(name)) return;

    var now = System.DateTime.UtcNow;
    System.DateTime last;
    if (_lastJsonTouch.TryGetValue(name, out last))
    {
        if ((now - last).TotalMilliseconds < JsonReloadDebounceMs) return;
    }
    _lastJsonTouch[name] = now;

    jsonLastChangeUtc = now;
    if (!jsonReloadQueued)
    {
        jsonReloadQueued = true;
        NextTick(DebouncedReloadJson);
    }
// --- NICE-mode override (name stays "lightsaber") ---
if (config != null && config.NiceMode)
{
    // 安全で派手に見える既定プリセットを上書き
    PresetEffect("ignite:lightsaber",    "assets/prefabs/tools/flashlight/effects/turn_on.prefab");
    PresetEffect("hum:lightsaber",       "assets/prefabs/tools/flashlight/effects/turn_on.prefab");
    PresetEffect("glow:lightsaber",      "assets/prefabs/tools/flashlight/effects/turn_on.prefab");
    PresetEffect("glowcore:lightsaber",  "assets/bundled/prefabs/fx/electric_spark.prefab");
    PresetEffect("glowaura:lightsaber",  "assets/bundled/prefabs/fx/impacts/add_metal.prefab");
    PresetEffect("glowspark:lightsaber", "assets/bundled/prefabs/fx/ricochet/ricochet1.prefab");
    PresetEffect("swing:lightsaber",     "assets/bundled/prefabs/fx/weapon/sword_swoosh.prefab");
    PresetEffect("critical:lightsaber",  "assets/bundled/prefabs/fx/electric_spark.prefab");
    PresetEffect("parry:lightsaber",     "assets/bundled/prefabs/fx/ricochet/ricochet1.prefab");
    PresetEffect("activate:lightsaber",  "assets/prefabs/tools/flashlight/effects/turn_on.prefab");
    PresetEffect("deactivate:lightsaber","assets/bundled/prefabs/fx/ricochet/ricochet1.prefab");
}

}



        private void HandleEquip(BasePlayer player, CustomItemDefinition def = null)
        {
            def = def ?? GetDefinition(config.LightsaberShortname ?? LIGHTSABER_SHORTNAME);
            if (def == null) return;

            // エフェクト再生
            PlayIgnite(player, def);
            StartAmbientLoop(player, def);
            StartHoldEffect(player, def);

            
            // [CIL_PATCH mute-glowlight] StartHandGlowLight(player, def);// パッシブエフェクト適用
            ApplyPassiveEffects(player);

            // 持続ビーム開始
            StartPersistentBeam(player, def);
        }

        private void HandleUnequip(BasePlayer player)
        {
            StopAmbientLoop(player);
            StopHoldEffect(player);
            StopPersistentBeam(player);
                        // [CIL_PATCH mute-glowlight] StopHandGlowLight(player);
            RemovePassiveEffects(player);
            var def = GetDefinition(config.LightsaberShortname ?? LIGHTSABER_SHORTNAME);
            PlayDeactivate(player, def); // 停止エフェクト再生
        }

        
        

        

        private void StartAmbientLoop(BasePlayer player, CustomItemDefinition def = null) 
{
    var fxGlow = ResolveEffect("glow", def);
// 取得できても存在しない場合は customEffects に切替
if (string.IsNullOrEmpty(fxGlow) || (GameManager.server != null && GameManager.server.FindPrefab(fxGlow) == null))
{
    var cfx = def?.customEffects?.glowLoopEffect;
    if (!string.IsNullOrEmpty(cfx)) fxGlow = cfx;
}
// 最終存在チェック
if (string.IsNullOrEmpty(fxGlow) || (GameManager.server != null && GameManager.server.FindPrefab(fxGlow) == null))
    return;

    // Stop existing loop first
    StopAmbientLoop(player);

    int burst = Math.Max(1, def?.customEffects?.glowBurst ?? 3);
    float interval = Math.Max(0.03f, def?.customEffects?.glowInterval ?? 0.08f);
    float radius = Math.Max(0f, def?.customEffects?.glowRadius ?? 0.30f);
    float oy = def?.customEffects?.glowOffsetY ?? 1.0f;

    ambientLoopTimers[player.userID] = timer.Every(interval, () =>
    {
        if (!equipped.Contains(player.userID) || player == null || player.transform == null)
        {
            StopAmbientLoop(player);
            return;
        }

        Vector3 basePos = player.transform.position + Vector3.up * oy;

        for (int i = 0; i < burst; i++)
        {
            float ang = UnityEngine.Random.Range(0f, 6.28318f);
            float r = radius * UnityEngine.Random.Range(0.2f, 1.0f);
            Vector3 pos = basePos + new Vector3(Mathf.Cos(ang) * r, 0, Mathf.Sin(ang) * r);
            SpawnEffect(fxGlow, pos);
        }
    });
}


        private void StopAmbientLoop(BasePlayer player)
        {
            Timer tmr;
            if (ambientLoopTimers.TryGetValue(player.userID, out tmr))
            {
                if (tmr != null) tmr.Destroy();
                ambientLoopTimers.Remove(player.userID);
            }
        }

        private void StartGlowLoop(BasePlayer player, CustomItemDefinition def = null)
        {
            StartAmbientLoop(player, def);
        }

        private void StopGlowLoop(BasePlayer player)
        {
            StopAmbientLoop(player);
        }

        private void StartHumLoop(BasePlayer player, CustomItemDefinition def = null)
        {
            if (def != null && def.muteAllSounds) return;
            StartAmbientLoop(player, def);
        }

        private void StopHumLoop(BasePlayer player)
        {
            StopAmbientLoop(player);
        }

private void StartHoldEffect(BasePlayer player, CustomItemDefinition def = null)
        {
            if (def != null && def.muteAllSounds) return;
            def = def ?? GetDefinition(config.LightsaberShortname ?? LIGHTSABER_SHORTNAME);
            if (def?.customEffects == null || string.IsNullOrEmpty(def.customEffects.holdEffect)) return;

            // 既存のタイマーがあれば停止
            StopHoldEffect(player);

            // エフェクトがアタッチされる GameObject を取得または作成
            // プレイヤーのボーンにアタッチする場合の例 (より高度なアプローチ)
            Transform effectTransform = player.model?.FindBone(def.customEffects.holdEffectBone ?? "r_hand") ?? player.eyes.transform;

            holdLoopTimers[player.userID] = timer.Every(0.1f, () =>
            {
                // プレイヤーが装備を解除した、死亡した、またはゲームオブジェクトが破壊された場合、エフェクトを停止
                if (!equipped.Contains(player.userID) || player.IsDead() || effectTransform == null)
                {
                    StopHoldEffect(player);
                    return;
                }
                SpawnEffect(def.customEffects.holdEffect, effectTransform.position);
            });
            // 初回実行
            SpawnEffect(def.customEffects.holdEffect, effectTransform.position);
        }

        private void StopHoldEffect(BasePlayer player)
        {
            if (holdLoopTimers.TryGetValue(player.userID, out var timerInstance))
            {
                timerInstance?.Destroy();
                holdLoopTimers.Remove(player.userID);
            }
        }

        #endregion

        
// --- Lightsaber判定ヘルパー ---
private bool IsLightsaber(Item item)
{
    if (item == null) return false;
    var sn = item.info?.shortname;
    if (string.IsNullOrEmpty(sn)) return false;

    string ls = config?.LightsaberShortname ?? LIGHTSABER_SHORTNAME;
    return sn.Equals(ls, StringComparison.OrdinalIgnoreCase);
}

#region ▶ パッシブエフェクト

        private void ApplyPassiveEffects(BasePlayer player)
        {
            CustomItemDefinition def = GetDefinition(config.LightsaberShortname ?? LIGHTSABER_SHORTNAME);
            if (def?.passiveEffects == null || def.passiveEffects.Count == 0) return;

            foreach (var kv in def.passiveEffects)
            {
                switch (kv.Key.ToLowerInvariant())
                {
                    case "runspeed":
                        player.ClientRPCPlayer(null, player, "SetPlayerSpeed", "Movement", kv.Value);
                        LogCil("DEBUG", $"Applied runspeed {kv.Value} to {player.displayName}");
                        break;
                    case "damagereduction":
                        // OnEntityTakeDamage で処理されるため、ここでは何もしない
                        break;
                    case "nightvision":
                        // Nightvision は Rust の ClientRPC で直接制御できない場合が多い。
                        // クライアント側Modやカスタム実装が必要。
                        // 一例として、Puts でログを出すだけにするか、既存のNightmodeプラグインとの連携を検討。
                        LogWarn( $"Nightvision passive effect is not directly supported by this plugin.");
                        player.SendConsoleCommand("graphics.nightmode", kv.Value > 0.5f ? 1 : 0); // 簡易的な nightmode コマンド連携例
                        break;
                    case "projectile_velocity":
                    case "projectile_damage":
                    case "projectile_accuracy":
                        // Projectile mods are usually handled by attaching ItemModProjectile.
                        // This requires more complex item modification on the server side
                        // and might not be directly controlled via passive effects.
                        // For a simple plugin, this would typically involve creating and applying
                        // ItemModProjectile instances when the item is equipped.
                        LogWarn( $"Projectile effects ({kv.Key}) are not directly implemented via passive effects.");
                        break;
                    default:
                        LogWarn( $"Unknown passive effect: {kv.Key}");
                        break;
                }
            }
        }

        private void RemovePassiveEffects(BasePlayer player)
        {
            // スピードリセット
            player.ClientRPCPlayer(null, player, "SetPlayerSpeed", "Movement", 1f);
            LogCil("DEBUG", $"Reset player speed for {player.displayName}");

            // Nightvision リセット
            player.SendConsoleCommand("graphics.nightmode", 0);
        }

        #endregion

        #region ▶ 持続ビーム (LineRenderer)

        private void StartPersistentBeam(BasePlayer player, CustomItemDefinition def = null)
        {
            StopPersistentBeam(player); // 既にビームがあれば停止
            if (!config.PersistentBeam) return;

            def = def ?? GetDefinition(config.LightsaberShortname ?? LIGHTSABER_SHORTNAME);
            if (def == null) return;

            Color bladeColor = ColorUtility.TryParseHtmlString(config.BeamColor, out var c) ? c : Color.cyan;
            if (def.visualEffects?.bladeColor != null && ColorUtility.TryParseHtmlString(def.visualEffects.bladeColor, out var defColor))
                bladeColor = defColor;

            // GameObjectの作成とLineRendererの追加
            var go = new GameObject("LightsaberBeam");
            LineRenderer lr = go.AddComponent<LineRenderer>();

            // マテリアルの設定 (Rustの既存のシェーダーを試す)
            // 注: Unityの標準的なマテリアルやシェーダーはRustサーバー環境で利用できない可能性があります。
            // 既存のRustアセットからマテリアルを取得するか、シンプルなパーティクルシェーダーなど、
            // サーバー環境で利用可能なものを指定する必要があります。
            // 以下の例はUnity標準のもので、Rustサーバーでは動かない可能性があります。
            // 動作しない場合は、この部分をRustゲーム内の既存のメッシュやエフェクトからマテリアルを取得するロジックに置き換える必要があります。
            lr.material = new Material(Shader.Find("Sprites/Default")); // 仮のマテリアル

            lr.startColor = bladeColor;
            lr.endColor = bladeColor;
            lr.startWidth = config.BeamWidthStart;
            lr.endWidth = config.BeamWidthEnd;
            lr.positionCount = 2; // 開始点と終了点

            // 光源の追加 (オプション)
            Light glowLight = go.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = bladeColor;
            glowLight.intensity = def.visualEffects?.glowIntensity ?? 1f;
            glowLight.range = def.visualEffects?.bladeLength ?? config.BeamLength;
            glowLight.shadows = LightShadows.None; // パフォーマンスのため影なし

            BeamData beamData = new BeamData
            {
                go = go,
                lr = lr,
                currentEnd = Vector3.zero // 初期値
            };
            activeBeams[player.userID] = beamData;

            beamData.timer = timer.Every(config.BeamUpdateInterval, () => UpdatePersistentBeam(player, beamData, def));
            UpdatePersistentBeam(player, beamData, def); // 初回更新
        }

        private void UpdatePersistentBeam(BasePlayer player, BeamData beamData, CustomItemDefinition def)
        {
            // プレイヤーが切断、死亡、装備解除されたらビームを停止
            if (!player.IsConnected || player.IsDead() || !equipped.Contains(player.userID) || beamData.go == null)
            {
                StopPersistentBeam(player);
                return;
            }

            // プレイヤーの手の位置にビームの開始点を設定
            var boneTransform = player.model?.FindBone("r_hand"); // 右手ボーンを見つける試み
            if (boneTransform == null)
            {
                boneTransform = player.eyes.transform; // フォールバックとして目の位置を使用
                LogWarn( $"Right hand bone not found for lightsaber beam on {player.displayName}. Using player eyes transform.");
            }

            // 手からのオフセット調整 (数値は調整してください)
            Vector3 startPos = boneTransform.position + boneTransform.right * 0.05f + boneTransform.up * 0.1f;
            Vector3 targetEndPos = startPos + boneTransform.forward * (def?.visualEffects?.bladeLength ?? config.BeamLength);

            // ジッター効果の適用
            Vector3 jitter = new Vector3(
                (float)(rng.NextDouble() * 2 - 1) * config.BeamJitter,
                (float)(rng.NextDouble() * 2 - 1) * config.BeamJitter,
                (float)(rng.NextDouble() * 2 - 1) * config.BeamJitter
            );
            targetEndPos += jitter;

            // スムージング (線形補間)
            if (beamData.currentEnd == Vector3.zero) beamData.currentEnd = targetEndPos; // 初回
            beamData.currentEnd = Vector3.Lerp(beamData.currentEnd, targetEndPos, config.BeamSmoothing);

            beamData.lr.SetPosition(0, startPos);
            beamData.lr.SetPosition(1, beamData.currentEnd);

            // GameObject自体も開始点と回転に合わせる
            beamData.go.transform.position = startPos;
            beamData.go.transform.rotation = boneTransform.rotation;

            // 光源の位置も調整
            Light glowLight = beamData.go.GetComponent<Light>();
            if (glowLight != null)
                glowLight.transform.position = startPos;
        }

        private void StopPersistentBeam(BasePlayer player)
        {
            if (activeBeams.TryGetValue(player.userID, out var beamData))
            {
                beamData.timer?.Destroy(); // タイマーを破棄
                if (beamData.go != null) GameObject.Destroy(beamData.go); // ゲームオブジェクトを破棄
                activeBeams.Remove(player.userID);
            }
        }

        #endregion

        #region ▶ エフェクト処理・検証

        private void InitializeLightsaberEffects() => ValidateAllEffectsPhase();


        // Helper: alias key builder and prefab usability checker
        private static string MakeAliasKey(string kind, string shortname = null)
            => string.IsNullOrEmpty(shortname) ? kind : $"{kind}:{shortname}";

        private bool IsPrefabUsable(string aliasKey, string path)
        {
            var gm = GameManager.server;
            bool exists = gm != null && gm.FindPrefab(path) != null;
            effectRegistry[aliasKey] = new EffectInfo { Path = path, Exists = exists };
            if (!exists)
            {
                if (missingPrefabsWarned.Add(path))
                    LogInfo($"[EffectVerify] Prefab missing: alias={aliasKey} path={path}");
                return false;
            }
            return true;
        }


        private void ValidateAllEffectsPhase()
        {
            effectRegistry.Clear();
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"ignite", config.IgniteEffect},
                {"glow", config.GlowLoopEffect},
                {"hum", config.HumLoopEffect},
                {"swing", config.SwingEffect},
                {"critical", config.CriticalEffect},
                {"parry", config.ParryEffect},
                {"activate", string.IsNullOrEmpty(config.ActivateEffectOverride)? config.IgniteEffect : config.ActivateEffectOverride},
                {"deactivate", string.IsNullOrEmpty(config.DeactivateEffectOverride)? config.ParryEffect : config.DeactivateEffectOverride}
            };

            // 各カスタムアイテム定義のエフェクトオーバーライドを追加
            foreach (var def in customItems.Values)
            {
                if (!string.IsNullOrEmpty(def.igniteEffect)) map[$"ignite:{def.shortname}"] = def.igniteEffect;
                if (!string.IsNullOrEmpty(def.glowLoopEffect)) map[$"glow:{def.shortname}"] = def.glowLoopEffect;
                                if (!string.IsNullOrEmpty(def.customEffects?.glowLoopEffect)) map[$"glow:{def.shortname}"] = def.customEffects.glowLoopEffect;if (!string.IsNullOrEmpty(def.humLoopEffect)) map[$"hum:{def.shortname}"] = def.humLoopEffect;
                if (!string.IsNullOrEmpty(def.swingEffect)) map[$"swing:{def.shortname}"] = def.swingEffect;
                if (!string.IsNullOrEmpty(def.criticalEffect)) map[$"critical:{def.shortname}"] = def.criticalEffect;
                if (!string.IsNullOrEmpty(def.parryEffect)) map[$"parry:{def.shortname}"] = def.parryEffect;
                if (!string.IsNullOrEmpty(def.glowCoreEffect)) map[$"glowcore:{def.shortname}"] = def.glowCoreEffect;
                if (!string.IsNullOrEmpty(def.glowAuraEffect)) map[$"glowaura:{def.shortname}"] = def.glowAuraEffect;
                if (!string.IsNullOrEmpty(def.glowSparkEffect)) map[$"glowspark:{def.shortname}"] = def.glowSparkEffect;
            }

            var gm = GameManager.server;
            foreach (var kv in map)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                bool exists = gm != null && gm.FindPrefab(kv.Value) != null;
                effectRegistry[kv.Key] = new EffectInfo { Path = kv.Value, Exists = exists };
                if (!exists)
                    LogInfo($"[EffectVerify] Prefab missing: alias={kv.Key} path={kv.Value}");
            }
        }

                private string ResolveEffectDirect(string kind, CustomItemDefinition def = null)
        {
            string shortname = def?.shortname;

            // 1) プリセット優先
            if (!string.IsNullOrEmpty(shortname))
            {
                var presetKey = MakeAliasKey(kind, shortname);
                if (presetEffectPaths.TryGetValue(presetKey, out var presetPath) && !string.IsNullOrEmpty(presetPath))
                {
                    if (IsPrefabUsable(presetKey, presetPath)) return presetPath;
                }
            }

            // 2) アイテム定義のオーバーライド
            string path = null;
            if (def != null)
            {
                path = kind switch
                {
                    "ignite"    => def.igniteEffect,
                    "glow"      => def.customEffects?.glowLoopEffect ?? def.glowLoopEffect,
                    "glowcore"  => def.glowCoreEffect ?? def.customEffects?.glowLoopEffect ?? def.glowLoopEffect,
                    "glowaura"  => def.glowAuraEffect,
                    "glowspark" => def.glowSparkEffect,
                    "hum"       => def.humLoopEffect,
                    "swing"     => def.swingEffect,
                    "critical"  => def.criticalEffect,
                    "parry"     => def.parryEffect,
                    _ => null
                };
                if (!string.IsNullOrEmpty(path))
                {
                    var aliasKey = MakeAliasKey(kind, shortname);
                    if (IsPrefabUsable(aliasKey, path)) return path;
                }
            }

            // 3) グローバル設定（activate/deactivate含む）
            path = kind switch
            {
                "ignite"     => config.IgniteEffect,
                "glow"       => config.GlowLoopEffect,
                "glowcore"   => config.GlowLoopEffect,
                "glowaura"   => null,
                "glowspark"  => null,
                "hum"        => config.HumLoopEffect,
                "swing"      => config.SwingEffect,
                "critical"   => config.CriticalEffect,
                "parry"      => config.ParryEffect,
                "activate"   => string.IsNullOrEmpty(config.ActivateEffectOverride)   ? config.IgniteEffect : config.ActivateEffectOverride,
                "deactivate" => string.IsNullOrEmpty(config.DeactivateEffectOverride) ? config.ParryEffect  : config.DeactivateEffectOverride,
                _ => null
            };
            if (!string.IsNullOrEmpty(path))
            {
                var aliasKey = MakeAliasKey(kind);
                if (IsPrefabUsable(aliasKey, path)) return path;
            }

            return null;
        }

/// <summary>
        /// エフェクト名とshortnameを受け取り、プリセット/フォールバックを参照して適切なエフェクトパスを返す。
        /// 対応しない場合はnullまたは代替エフェクトを返す。
        /// </summary>
        /// <param name="effectName">例: ignite, hum</param>
        /// <param name="shortname">アイテムshortname（例: lightsaber）</param>
        
        private string ResolveEffect(string kind, CustomItemDefinition def = null) {
            // Per-item sound mute policy
            if (def != null && def.muteAllSounds)
            {
                if (kind == "hum" || kind == "hold" || kind == "activate" || kind == "deactivate" || kind == "swing")
                    return null;
            }
// Fast path from registry cache
            try {
                string _sn = def != null ? def.shortname : null;
                var _keySpec = MakeAliasKey(kind, _sn);
                EffectInfo _ei1;
                if (effectRegistry.TryGetValue(_keySpec, out _ei1) && _ei1.Exists && !string.IsNullOrEmpty(_ei1.Path)) return _ei1.Path;
                EffectInfo _ei2;
                if (effectRegistry.TryGetValue(kind, out _ei2) && _ei2.Exists && !string.IsNullOrEmpty(_ei2.Path)) return _ei2.Path;
            } catch {}

            
// 明示的な無効化（このアイテム定義で空文字が指定された場合はフォールバックも行わない）
if (def != null)
{
    if (kind == "hum" && def.humLoopEffect != null && def.humLoopEffect.Trim().Length == 0)
        return null;
}
var path = ResolveEffectDirect(kind, def);
            if (!string.IsNullOrEmpty(path)) return path;

            if (config != null && config.EnableEffectFallback && effectFallbackChain.TryGetValue(kind, out var chain))
            {
                foreach (var alt in chain)
                {
                    var altPath = ResolveEffectDirect(alt, def);
                    if (!string.IsNullOrEmpty(altPath))
                    {
                        if (fallbackNoticeSent.Add(MakeAliasKey(kind, def?.shortname)))
                            LogWarn($"[EffectFallback] {kind} -> {alt} (using fallback effect)");
                        return altPath;
                    }
                }
            }
            return null; // 全てのパスが見つからない場合
        }

        /// <summary>
        /// 指定されたパスのエフェクトを、指定プレイヤーの位置にスポーンさせる。
        /// 条件に応じて近距離 or 遠距離に対応。
        /// </summary>
        /// <param name="player">対象プレイヤー</param>
        /// <param name="path">エフェクトのprefabパス</param>
        /// <param name="isLocal">自身視点かどうか</param>
        
        // ===== Prefab Categories Auto-update =====
        private void CIL_LoadPrefabCategories()
        {
            try
            {
                var dir = System.IO.Path.Combine(Interface.Oxide.DataDirectory, "CustomItemLoader");
                var file = System.IO.Path.Combine(dir, "prefab_categories.json");
                fxCategoryEffects.Clear();
                if (!System.IO.File.Exists(file)) return;
                var json = System.IO.File.ReadAllText(file);
                var node = Newtonsoft.Json.Linq.JObject.Parse(json);
                var arr = node["effects"] as Newtonsoft.Json.Linq.JArray;
                if (arr != null)
                {
                    foreach (var t in arr)
                    {
                        var s = t?.ToString();
                        if (!string.IsNullOrEmpty(s)) fxCategoryEffects.Add(s);
                    }
                }
            }
            catch (System.Exception ex)
            {
                LogWarn($"[FxCat] load failed: {ex.Message}");
            }
        }

        private void CIL_SavePrefabCategories()
        {
            try
            {
                var dir = System.IO.Path.Combine(Interface.Oxide.DataDirectory, "CustomItemLoader");
                System.IO.Directory.CreateDirectory(dir);
                var file = System.IO.Path.Combine(dir, "prefab_categories.json");
                var list = new List<string>(fxCategoryEffects);
                list.Sort(System.StringComparer.OrdinalIgnoreCase);
                var obj = new Dictionary<string, object> { ["effects"] = list };
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(file, json);
                LogInfo($"[FxCat] updated prefab_categories.json (count={list.Count})");
            }
            catch (System.Exception ex)
            {
                LogWarn($"[FxCat] save failed: {ex.Message}");
            }
        }

        private void CIL_DebounceSaveFxCat()
        {
            try { fxCatSaveDebounce?.Destroy(); } catch {}
            fxCatSaveDebounce = timer.Once(1.0f, () => { CIL_SavePrefabCategories(); });
        }

        private void CIL_MarkEffectFromUsage(string path)
        {
            if (!cfgAutoUpdatePrefabCategories) return;
            if (string.IsNullOrEmpty(path)) return;
            if (IsUnsafeEffectPath(path)) return;
            if (!IsSafeEffectPath(path)) return;
            try
            {
                var gm = GameManager.server;
                if (gm != null && gm.FindPrefab(path) != null)
                {
                    if (fxCategoryEffects.Add(path))
                        CIL_DebounceSaveFxCat();
                }
            }
            catch {}
        }

        // ===== Safe/Unsafe effect path helpers and cleanup =====
        private bool IsUnsafeEffectPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            path = path.ToLowerInvariant();
            return path.StartsWith("assets/content/");
        }
        private bool IsSafeEffectPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            path = path.ToLowerInvariant();
            if (path.StartsWith("assets/bundled/prefabs/fx/")) return true;
            if (path.StartsWith("assets/prefabs/") && path.Contains("/effects/")) return true;
            return false;
        }
        private void CIL_RemoveEffectFromCategories(string path, string reason = null)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (fxCategoryEffects.Remove(path))
                {
                    LogInfo($"[FxCat] removed '{path}'{(string.IsNullOrEmpty(reason) ? "" : $" ({reason})")}");
                    CIL_DebounceSaveFxCat();
                }
            }
            catch { }
        }
        private void CIL_CleanupPrefabCategories(bool removeUnsafeToo = true)
        {
            try
            {
                var gm = GameManager.server;
                var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in fxCategoryEffects)
                {
                    if (removeUnsafeToo && IsUnsafeEffectPath(p)) continue;
                    if (!IsSafeEffectPath(p)) continue;
                    if (gm != null && gm.FindPrefab(p) == null) continue;
                    next.Add(p);
                }
                if (next.Count != fxCategoryEffects.Count)
                {
                    fxCategoryEffects = next;
                    CIL_SavePrefabCategories();
                    LogInfo($"[FxCat] cleanup done. count={fxCategoryEffects.Count}");
                }
            }
            catch (System.Exception ex)
            {
                LogWarn($"[FxCat] cleanup failed: {ex.Message}");
            }
        }

        // --- added: CIFP ingest stub (compile-safe) ---
        private void Cifp_IngestUsedEffect(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                var cifp = plugins.Find("CustomItemFxPreloader");
                if (cifp == null || !cifp.IsLoaded) return;
                // Try common hook names (non-fatal if missing)
                try { cifp.Call("FxIngestUsedEffect", path); } catch {}
                try { cifp.CallHook("CIFP_IngestUsedEffect", path); } catch {}
                try { cifp.CallHook("OnCILIngestEffect", path); } catch {}
            }
            catch { }
        }
private void SpawnEffect(string prefab, Vector3 pos) {
            CIL_MarkEffectFromUsage(prefab);
            Cifp_IngestUsedEffect(prefab);
            if (string.IsNullOrEmpty(prefab)) return;
            var gm = GameManager.server;
            bool allowAmbient = prefab != null && prefab.IndexOf("/fx/ambient/", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (gm != null && gm.FindPrefab(prefab) == null && !allowAmbient)
            {
                if (missingPrefabsWarned.Add(prefab))
                    LogWarn($"Prefab 未検出: {prefab}");
                return; // 存在しない（かつ ambient でもない）プレハブはスポーンしない
            }
            try { Effect.server.Run(prefab, pos); }
            catch (System.Exception ex) { CIL_RemoveEffectFromCategories(prefab, ex.Message); LogWarn($"Effect run failed: {prefab} - {ex.Message}"); }
        }

        private void PlayIgnite(BasePlayer player, CustomItemDefinition def = null)
        {
            if (def != null && def.muteAllSounds) return;
            // igniteEffect が空文字なら起動エフェクト自体を無効化（フォールバックもしない）
            if (def != null && def.igniteEffect != null && def.igniteEffect.Trim().Length == 0)
                return;
            var fx = ResolveEffect("activate", def); // アクティベートエフェクトを再生
            if (!string.IsNullOrEmpty(fx))
                SpawnEffect(fx, player.transform.position + Vector3.up * 1.1f); // プレイヤーの上方にスポーン
        }

        private void PlayDeactivate(BasePlayer player, CustomItemDefinition def = null)
        {
            if (def != null && def.muteAllSounds) return;
            var fx = ResolveEffect("deactivate", def); // デアクティベートエフェクトを再生
            if (!string.IsNullOrEmpty(fx))
                SpawnEffect(fx, player.transform.position + Vector3.up * 1.1f);
        }

        #endregion

        #region ▶ Bridge (EpicLoot / ItemPerks 連携)

        private void CacheDependencies()
        {
            EpicLoot = plugins.Find("EpicLoot");
            ItemPerks = plugins.Find("ItemPerks");
        }

        private bool TryCall(Plugin plugin, string hook, out object result, params object[] args)
        {
            result = null;
            if (plugin == null || !plugin.IsLoaded) return false;
            try
            {
                result = plugin.CallHook(hook, args);
                return true;
            }
            catch (Exception ex)
            {
                if (hookWarned.Add(plugin.Name + "." + hook))
                    LogWarn($"[Bridge] Hook呼び出し失敗 {plugin.Name}.{hook}: {ex.ToString()}");
                return false;
            }
        }

        private void QueueExports()
        {
            CacheDependencies();
            if (EpicLoot != null && EpicLoot.IsLoaded) pendingEpicLootExport = true;
            if (ItemPerks != null && ItemPerks.IsLoaded) pendingItemPerksExport = true;
            AttemptExports();
        }

        private void AttemptExports()
        {
            if (pendingEpicLootExport) ExportToEpicLootAll();
            if (pendingItemPerksExport) ExportToItemPerksAll();
        }

        private void ExportToEpicLootAll()
        {
            int sentItems = 0, sentPerks = 0, sentSets = 0, skipped = 0, failures = 0;
            foreach (var def in customItems.Values)
            {
                try
                {
                    bool any = false;

                    if (!string.IsNullOrEmpty(def.rarity))
                    {
                        TryCall(EpicLoot, "EpicLoot_RegisterRarity", out _, def.rarity);
                        any = true;
                    }

                    // perkDictの割り当て (null coalescing operator でより簡潔に)
                    Dictionary<string, float> perkDict =
                        def.exportPerks?.ToDictionary(k => k.Key, k => k.Value) ??
                        def.perks?.ToDictionary(k => k.Key, k => k.Value) ??
                        new Dictionary<string, float>(); // nullの代わりに空の辞書を割り当て

                    if (perkDict.Count > 0)
                    {
                        foreach (var pk in perkDict)
                        {
                            TryCall(EpicLoot, "EpicLoot_AddPerkInstance", out _, def.shortname, pk.Key, pk.Value);
                            sentPerks++;
                        }
                        any = true;
                    }

                    if (!string.IsNullOrEmpty(def.setId) && def.setBonuses != null && def.setBonuses.Count > 0)
                    {
                        foreach (var threshold in def.setBonuses)
                        {
                            TryCall(EpicLoot, "EpicLoot_AddSetBonus", out _, def.setId, threshold.Key, threshold.Value);
                            sentSets++;
                        }
                        any = true;
                    }

                    if (any) sentItems++; else skipped++;
                }
                catch (Exception ex)
                {
                    failures++;
                    LogWarn($"[Bridge] EpicLoot export失敗 item={def.shortname} ex={ex.ToString()}");
                }
            }
            LogInfo($"[Bridge] EpicLoot export items={sentItems} perks={sentPerks} setBonuses={sentSets} skipped={skipped} fail={failures}");
            pendingEpicLootExport = false;
        }

        private void ExportToItemPerksAll()
        {
            int sentItems = 0, sentPerks = 0, extraChances = 0, skipped = 0, failures = 0;
            foreach (var def in customItems.Values)
            {
                try
                {
                    bool any = false;

                    // perkDictの割り当て (null coalescing operator でより簡潔に)
                    Dictionary<string, float> perkDict =
                        def.exportPerks?.ToDictionary(k => k.Key, k => k.Value) ??
                        def.perks?.ToDictionary(k => k.Key, k => k.Value) ??
                        new Dictionary<string, float>(); // nullの代わりに空の辞書を割り当て

                    if (perkDict.Count > 0)
                    {
                        foreach (var pk in perkDict)
                        {
                            TryCall(ItemPerks, "ItemPerks_RegisterPerkInstance", out _, def.shortname, pk.Key, pk.Value);
                            sentPerks++;
                        }
                        any = true;
                    }

                    if (def.additionalPerkChances != null && def.additionalPerkChances.Count > 0)
                    {
                        TryCall(ItemPerks, "ItemPerks_SetAdditionalChances", out _, def.shortname, def.additionalPerkChances);
                        extraChances++;
                        any = true;
                    }

                    if (any) sentItems++; else skipped++;
                }
                catch (Exception ex)
                {
                    failures++;
                    LogWarn($"[Bridge] ItemPerks export失敗 item={def.shortname} ex={ex.ToString()}");
                }
            }
            LogInfo($"[Bridge] ItemPerks export items={sentItems} perks={sentPerks} addChances={extraChances} skipped={skipped} fail={failures}");
            pendingItemPerksExport = false;
        }

        #endregion

        #region ▶ コマンド処理

        private void CommandForceJump(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "customitemloader.forcejump"))
            {
                SendPlayerMsg(player, Lang("NoPermission", player.UserIDString));
                return;
            }
            if (!equipped.Contains(player.userID))
            {
                SendPlayerMsg(player, "ライトセーバーを装備していません。");
                return;
            }

            Vector3 jumpVelocity = player.transform.up * config.ForceJumpUpwardVelocity + player.transform.forward * config.ForceJumpForwardBoost;
            player.ClientRPCPlayer(null, player, "ForcePositionTo", player.transform.position + jumpVelocity);
            SendPlayerMsg(player, "<color=#00ff00>フォースジャンプ！</color>");
        }

        /// <summary>
        /// /cil コマンドのルート処理。引数に応じて fxlist や scanfx などの処理を呼び出す。
        /// fxlist → 登録済みエフェクト一覧出力
        /// scanfx → Rustのエフェクト定義を読み出し knownFxNames に登録
        /// </summary>
        private void CmdCil(BasePlayer player, string command, string[] args)
        {
        if (args.Length > 0 && args[0] == "fxlist")
        {
            SendReply(player, "[CIL] 登録済みエフェクト一覧を表示します。");
            ShowFxList(player, new string[0]);
            return;
        }
            if (args == null || args.Length == 0)
            {
                SendPlayerMsg(player, "カスタムアイテムローダーコマンド:");
                SendPlayerMsg(player, "/cil give <shortname> [amount] - アイテム配布（要権限）");
                SendPlayerMsg(player, "/cil list - アイテム定義リスト");
                SendPlayerMsg(player, "/cil info <shortname> - アイテム説明表示");
                SendPlayerMsg(player, "/cil verify - 定義検証");
                SendPlayerMsg(player, "/cil effects - エフェクト状態");
                SendPlayerMsg(player, "/cil jsoncheck [file] - JSON検査（行・列番号）");
                SendPlayerMsg(player, "/cil config - 設定を再読み込み");
                SendPlayerMsg(player, "/cil log [filter] - ログ表示 (フィルタ)");
                SendPlayerMsg(player, "/cil log clear - ログクリア");
                SendPlayerMsg(player, "/cil log save - ログ保存");
                SendPlayerMsg(player, "/cil debug - デバッグモード (トグル)");
                return;
            }

            switch (args[0].ToLowerInvariant())
            {

                case "fxlist":
                    // /cil fxlist [all]
                    if (args.Length > 1 && args[1].Equals("all", StringComparison.OrdinalIgnoreCase))
                        ShowFxList(player, new []{"all"});
                    else
                        ShowFxList(player, new string[0]);
                    break;

                case "list":
                    SendPlayerMsg(player, $"カスタムアイテム定義 ({customItems.Count} 個):");
                    if (customItems.Count == 0)
                    {
                        SendPlayerMsg(player, "定義なし。JSONファイルがCustomItemDataフォルダにあることを確認してください。");
                    }
                    else
                    {
                        foreach (var def in customItems.Values)
                        {
                            var src = shortnameSource.TryGetValue(def.shortname, out var s) ? $" ({s})" : "";
                            SendPlayerMsg(player, $"- {def.shortname} [parent:{def.parentShortname ?? "N/A"}] {src}");
                        }
                    }
                    break;
                case "verify":
                    RunVerify(player);
                    break;
                case "effects":
                    OutputEffectStatus(player);
                    break;
                case "config":
{
    LoadConfig();
    cfgAutoShowItemInfoUI = (config?.AutoShowItemInfoUI) ?? true;
    cfgItemInfoAutoHideSeconds = (config?.ItemInfoAutoHideSeconds) ?? 25f;
    cfgAutoUpdatePrefabCategories = (config?.AutoUpdatePrefabCategories) ?? true;
    CIL_LoadPrefabCategories();
    CIL_CleanupPrefabCategories(true);
    LoadAllCustomItems();
    InitializeLightsaberEffects();
    SendPlayerMsg(player, "設定再読み込み完了。");
    break;
}
                


case "log":
                    HandleLogCommand(player, args);
                    break;
                case "debug":
                    ToggleDebugMode(player);
                    break;
case "give":
{
    if (!permission.UserHasPermission(player.UserIDString, "customitemloader.give"))
    {
        SendPlayerMsg(player, Lang("NoPermission", player.UserIDString));
        break;
    }
    if (args.Length < 2)
    {
        SendPlayerMsg(player, Lang("GiveUsage", player.UserIDString));
        break;
    }
    var shortname = args[1];
    int amount = 1;
    if (args.Length > 2 && !int.TryParse(args[2], out amount)) amount = 1;
    GiveCustomItem(player, shortname, amount);
    break;
}

                case "info":
                    if (args.Length < 2) { SendPlayerMsg(player, "使い方: /cil info <shortname>"); break; }
                    var infoDef = GetDefinition(args[1]);
                    if (infoDef == null) { SendPlayerMsg(player, $"未定義: {args[1]}"); break; }
                    ShowItemInfo(player, infoDef);
                    break;
                case "jsoncheck":
                    RunJsonCheck(player, args);
                    break;
                default:
                        SendPlayerMsg(player, "不明なコマンド。 /cil でヘルプ。");
                        break;
            }
        }

        private void RunVerify(BasePlayer caller)
        {
            int missingParent = 0;
            int badPassive = 0;
            foreach (var def in customItems.Values)
            {
                string parent = string.IsNullOrEmpty(def.parentShortname) ? def.shortname : def.parentShortname;
                if (ItemManager.FindItemDefinition(parent) == null)
                    missingParent++;

                if (def.passiveEffects != null)
                {
                    foreach (var v in def.passiveEffects.Values)
                    {
                        // NormalizePassiveEffects で float に変換済みなので、ここでは float であることを期待
                        if (!(v is float)) badPassive++;
                    }
                }
            }
            SendPlayerMsg(caller, $"Verify: total={customItems.Count} missingParent={missingParent} badPassiveValues={badPassive}");

            if (missingParent == 0 && badPassive == 0)
                SendPlayerMsg(caller, "OK: 問題なし");

            SendPlayerMsg(caller,
                $"Bridge: EpicLoot={(EpicLoot != null && EpicLoot.IsLoaded ? "OK" : "-")} ItemPerks={(ItemPerks != null && ItemPerks.IsLoaded ? "OK" : "-")} pending={(pendingEpicLootExport || pendingItemPerksExport ? "yes" : "no")}");
        }

        private void OutputEffectStatus(BasePlayer pl)
        {
            int missing = effectRegistry.Values.Count(v => !v.Exists);
            SendPlayerMsg(pl, $"Effects: total={effectRegistry.Count} missing={missing}");

            int shown = 0;
            foreach (var kv in effectRegistry)
            {
                if (shown++ >= 15)
                {
                    SendPlayerMsg(pl, "... more省略");
                    break;
                }
                SendPlayerMsg(pl, $"{kv.Key} => {(kv.Value.Exists ? "OK" : "MISSING")} {kv.Value.Path}");
            }
        }

        private void HandleLogCommand(BasePlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                SendPlayerMsg(player, "ログ表示コマンド: /cil log [filter] | /cil log clear | /cil log save");
                OutputLogBuffer(player, currentLogFilter);
                return;
            }

            switch (args[1].ToLowerInvariant())
            {
                case "clear":
                    cilMsgBuffer.Clear();
                    SendPlayerMsg(player, "ログバッファをクリアしました。");
                    break;
                case "save":
                    SaveLogToFile();
                    SendPlayerMsg(player, $"ログをファイルに保存しました: {config.LogAutoFileName}");
                    break;
                case "fxseed":
                    // EmbeddedPrefabList から categories を再生成してから fxlist を再出力
                    ForceRebuildCategories(player);
                    ShowFxList(player, new string[0]);
                    break;
                default:
                    currentLogFilter = args[1].ToUpperInvariant();
                    SendPlayerMsg(player, $"ログフィルタ設定: {currentLogFilter}");
                    OutputLogBuffer(player, currentLogFilter);
                    break;
            }
        }

        private void ToggleDebugMode(BasePlayer player)
        {
            debugMode = !debugMode;
            SendPlayerMsg(player, $"デバッグモード: {(debugMode ? "有効" : "無効")}");
            LogCil("DEBUG", $"Debug mode toggled to {debugMode}");
        }

        #endregion

        #region ▶ ロギング補助メソッド

        // コンソールメッセージとチャットメッセージの両方に利用
        private void SendPlayerMsg(BasePlayer player, string message)
{
    if (player != null)
    {
        SendReply(player, message);
    }
}

        // 内部ログ関数 (ログレベルとフィルタリング対応)
        private void LogCil(string level, string message)
        {
            string msg = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";

            // バッファ追加
            cilMsgBuffer.Enqueue(msg);
            while (cilMsgBuffer.Count > CilBufferSize) cilMsgBuffer.Dequeue();

            // ファイル追記
            if (config != null && config.LogAutoAppendFile)
            {
                EnsureLogFolder();
                var path = Path.Combine(Interface.Oxide.DataDirectory, "CustomItemLoader", config.LogAutoFileName);
                File.AppendAllText(path, (config.LogStripColor ? StripColors(msg) : msg) + Environment.NewLine);
            }

            // 重要ログ or デバッグ時のみコンソール出力
            if (debugMode || level == "ERROR" || level == "WARN")
                Puts(msg);
        }

        // ラッパー（Oxide出力）
        private void LogInfo(string message)  => LogCil("INFO",  message);
        private void LogWarn(string message)  => LogCil("WARN",  message);
        private void LogError(string message) => LogCil("ERROR", message);

        // バッファ出力
        private void OutputLogBuffer(BasePlayer player, string filter)
        {
            if (cilMsgBuffer.Count == 0)
            {
                SendPlayerMsg(player, "ログバッファは空です。");
                return;
            }
            SendPlayerMsg(player, $"--- ログバッファ (フィルタ: {filter ?? "なし"}) ---");
            foreach (var msg in cilMsgBuffer)
            {
                if (filter == null || msg.ToUpperInvariant().Contains(filter))
                    SendPlayerMsg(player, msg);
            }
            SendPlayerMsg(player, "--- ログ終了 ---");
        }

        private void EnsureLogFolder()
        {
            var logDir = Path.Combine(Interface.Oxide.DataDirectory, "CustomItemLoader");
            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);
        }

        private void SaveLogToFile()
        {
            EnsureLogFolder();
            var path = Path.Combine(Interface.Oxide.DataDirectory, "CustomItemLoader", config.LogAutoFileName);
            File.WriteAllLines(path, cilMsgBuffer.Select(s => config.LogStripColor ? StripColors(s) : s));
        }

        // Rustカラータグを除去するヘルパー
        private string StripColors(string str)
            => Regex.Replace(str, "<color=#[0-9a-fA-F]{6}>|</color>", "");

#endregion
        #region ▶ UI表示補助（アイテム説明）
        private void ShowItemInfo(BasePlayer player, CustomItemDefinition def)
        {
            if (player == null || def == null) return;

            string title = !string.IsNullOrEmpty(def.defaultName) ? def.defaultName : def.shortname;
            SendPlayerMsg(player, $"<size=14><color=#66CCFF>『{title}』</color></size>");

            if (!string.IsNullOrEmpty(def.description))
                SendPlayerMsg(player, def.description);

            if (def.tags != null && def.tags.Length > 0)
                SendPlayerMsg(player, $"タグ: {string.Join(", ", def.tags)}");
        }
        #endregion
    

                /// <summary>
                #region ▶ JSON検査
        private void RunJsonCheck(BasePlayer player, string[] args)
        {
            EnsureDataFolder();
            string pattern = (args != null && args.Length > 1 && !string.IsNullOrEmpty(args[1])) ? args[1] : "*.json";
            string[] files;
            try
            {
                files = Directory.GetFiles(DataFolderPath, pattern, SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                SendPlayerMsg(player, $"JSON検査: ファイル列挙に失敗しました: {ex.Message}");
                return;
            }

            if (files.Length == 0)
            {
                SendPlayerMsg(player, $"JSON検査: 対象ファイルがありません ({pattern})");
                return;
            }

            int ok = 0, fail = 0;
            var lines = new List<string>();
            lines.Add($"=== JSON Check at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            lines.Add($"Folder = {DataFolderPath}, Pattern = {pattern}");
            foreach (var f in files)
            {
                string err;
                int line, col;
                if (ValidateJsonFile(f, out err, out line, out col))
                {
                    ok++;
                    lines.Add($"OK  : {Path.GetFileName(f)}");
                }
                else
                {
                    fail++;
                    lines.Add($"FAIL: {Path.GetFileName(f)} (line {line}, col {col}) -> {err}");
                }
            }

            // 保存
            try
            {
                var outPath = Path.Combine(DataFolderPath, "jsoncheck_result.txt");
                File.WriteAllLines(outPath, lines);
                SendPlayerMsg(player, $"JSON検査完了: OK={ok} FAIL={fail} 詳細: jsoncheck_result.txt");
            }
            catch (Exception ex)
            {
                SendPlayerMsg(player, $"JSON検査結果の保存に失敗: {ex.Message}");
            }
        }

        // 純粋な構文検査（行・列番号を返す）
        private bool ValidateJsonFile(string path, out string error, out int line, out int col)
        {
            error = null; line = 0; col = 0;
            try
            {
                using (var sr = new StreamReader(path))
                using (var reader = new Newtonsoft.Json.JsonTextReader(sr))
                {
                    reader.DateParseHandling = Newtonsoft.Json.DateParseHandling.None;
                    while (reader.Read()) { /* 全トークン走査 */ }
                }
            }
            catch (Newtonsoft.Json.JsonReaderException jre)
            {
                error = jre.Message;
                line = jre.LineNumber;
                col = jre.LinePosition;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                line = 0;
                col = 0;
                return false;
            }

            // 追加の軽いスキーマ検査（shortname の有無など）
            try
            {
                var text = File.ReadAllText(path);
                var obj = Newtonsoft.Json.JsonConvert.DeserializeObject<CustomItemDefinition>(text);
                if (obj == null)
                {
                    error = "トップレベルがオブジェクトではありません";
                    return false;
                }
                if (string.IsNullOrEmpty(obj.shortname))
                {
                    error = "必須フィールド shortname がありません";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = $"型変換エラー: {ex.Message}";
                return false;
            }

            return true;
        }
        #endregion


        private void ShowFxList(BasePlayer player, string[] args)
        {
            EnsureCategoryJson();

            var filePath = Path.Combine(DataFolderPath, "fxlist.txt");
            var jsonPath = Path.Combine(DataFolderPath, "prefab_categories.json");
            var gm = GameManager.server;
            var lines = new List<string>();
            int ok = 0, missing = 0, total = 0;

            bool listAll = (args != null && args.Length > 0 && args[0] == "all");

            if (!listAll)
            {
                if (!File.Exists(jsonPath))
                {
                    lines.Add("[FXLIST] prefab_categories.jsonが見つかりません。");
                }
                else
                {
                    var json = File.ReadAllText(jsonPath);
                    var data = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json);

                    foreach (var category in data.Keys)
                    {
                        lines.Add($">>> {category} <<<");
                        foreach (var prefab in data[category])
                        {
                            bool exists = gm != null && gm.FindPrefab(prefab) != null;
                            string status = exists ? "OK" : "MISSING";
                            string usable = exists ? "使用可" : "使用不可";
                            string line = $"{prefab} ({status}, {usable})";
                            lines.Add(line);
                            if (exists) ok++; else missing++;
                            total++;
                        }
                    }
                }
            }
            else
            {
                var unionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // 1) prefab_categories.json (loaded into fxCategoryEffects)
                foreach (var s in fxCategoryEffects) unionSet.Add(s);

                // 2) customItems の customEffects / effect overrides
                foreach (var def in customItems.Values)
                {
                    if (def == null) continue;
                    if (def.customEffects != null)
                    {
                        if (!string.IsNullOrEmpty(def.customEffects.holdEffect)) unionSet.Add(def.customEffects.holdEffect);
                    }
                    if (!string.IsNullOrEmpty(def.igniteEffect)) unionSet.Add(def.igniteEffect);
                    if (!string.IsNullOrEmpty(def.glowLoopEffect)) unionSet.Add(def.glowLoopEffect);
                    if (!string.IsNullOrEmpty(def.glowCoreEffect)) unionSet.Add(def.glowCoreEffect);
                    if (!string.IsNullOrEmpty(def.glowAuraEffect)) unionSet.Add(def.glowAuraEffect);
                    if (!string.IsNullOrEmpty(def.glowSparkEffect)) unionSet.Add(def.glowSparkEffect);
                    if (!string.IsNullOrEmpty(def.humLoopEffect)) unionSet.Add(def.humLoopEffect);
                    if (!string.IsNullOrEmpty(def.swingEffect)) unionSet.Add(def.swingEffect);
                    if (!string.IsNullOrEmpty(def.criticalEffect)) unionSet.Add(def.criticalEffect);
                    if (!string.IsNullOrEmpty(def.parryEffect)) unionSet.Add(def.parryEffect);
                }

                // 3) effectRegistry にある既知パス
                foreach (var kv in effectRegistry)
                {
                    if (kv.Value != null && !string.IsNullOrEmpty(kv.Value.Path))
                        unionSet.Add(kv.Value.Path);
                }

                var list = new List<string>(unionSet);
                list.Sort(StringComparer.OrdinalIgnoreCase);
                lines.Add($">>> all-sources (categories + custom + cache) <<<");
                foreach (var prefab in list)
                {
                    bool exists = gm != null && gm.FindPrefab(prefab) != null;
                    string status = exists ? "OK" : "MISSING";
                    string usable = exists ? "使用可" : "使用不可";
                    string line = $"{prefab} ({status}, {usable})";
                    lines.Add(line);
                    if (exists) ok++; else missing++;
                    total++;
                }
            }

            lines.Insert(0, $"[FXLIST] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - カテゴリPrefabリスト");
            lines.Add($"[FXLIST] OK: {ok}, MISSING: {missing}, 使用可: {ok}, 使用不可: {missing}, TOTAL: {total}");
            lines.Add("[FXLIST] 出力終了");
            File.WriteAllLines(filePath, lines);

            SendReply(player, $"[CIL] fxlist を出力: OK={ok} / MISSING={missing} / TOTAL={total}");
        }

        
        private void EnsureCategoryJson()
        {
            var jsonPath = Path.Combine(DataFolderPath, "prefab_categories.json");
            if (File.Exists(jsonPath)) return;

            var categories = new Dictionary<string, List<string>>
            {
                { "effects", new List<string>
                    {
                        "assets/bundled/prefabs/fx/impacts/add_wood.prefab",
                        "assets/bundled/prefabs/fx/impacts/add_metal.prefab",
                        "assets/prefabs/tools/flashlight/effects/turn_on.prefab",
                        "assets/prefabs/deployable/lantern/effects/lantern_on.prefab"
                    }
                }
            };

            var json = JsonConvert.SerializeObject(categories, Formatting.Indented);
            File.WriteAllText(jsonPath, json);
            CIL_LoadPrefabCategories();
            CIL_CleanupPrefabCategories(true);
        }

        // ===== ZLevels 連携ヘルパ =====
        private bool ZL_MeetsRequirement(BasePlayer player, Dictionary<string, double> minLevels)
        {
            try
            {
                if (minLevels == null || minLevels.Count == 0) return true;
                if (ZLevelsRemastered == null || !ZLevelsRemastered.IsLoaded) return true;

                var info = ZLevelsRemastered.Call("api_GetPlayerInfo", player.userID) as string;
                if (string.IsNullOrEmpty(info)) return true;

                // api_GetPlayerInfo は '|' 区切りで以下順に返却:
                // ACQUIRE_LEVEL|ACQUIRE_POINTS|CRAFTING_LEVEL|CRAFTING_POINTS|CUI|LAST_DEATH|
                // MINING_LEVEL|MINING_POINTS|ENABLED|SKINNING_LEVEL|SKINNING_POINTS|WOODCUTTING_LEVEL|WOODCUTTING_POINTS|XP_MULT
                var a = info.Split('|');
                double acq = ParseDoubleSafe(a, 0);
                double craft = ParseDoubleSafe(a, 2);
                double mining = ParseDoubleSafe(a, 6);
                double skin = ParseDoubleSafe(a, 9);
                double wood = ParseDoubleSafe(a, 11);

                foreach (var kv in minLevels)
                {
                    var key = kv.Key.Trim().ToLowerInvariant();
                    double req = kv.Value;
                    double cur =
                        key switch {
                            "a" or "acquire" => acq,
                            "c" or "crafting" => craft,
                            "m" or "mining" => mining,
                            "s" or "skinning" => skin,
                            "wc" or "woodcutting" => wood,
                            _ => 0
                        };
                    if (req > 0 && cur < req) return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                LogWarn($"[ZLBridge] ZL_MeetsRequirement 例外: {ex.Message}");
                return true; // 失敗時は通す
            }
        }

        private double ParseDoubleSafe(string[] arr, int index)
        {
            try
            {
                if (arr == null || index < 0 || index >= arr.Length) return 0;
                if (double.TryParse(arr[index], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
                return 0;
            }
            catch { return 0; }
        }

        
        // ===== CIL Item Info UI =====
        [ChatCommand("cilinfo")]
        private void CmdCilInfo(BasePlayer player, string cmd, string[] args)
        {
            try
            {
                var item = player?.GetActiveItem();
                if (item == null)
                {
                    SendPlayerMsg(player, "[CIL] 手に持っているアイテムがありません。");
                    return;
                }
                ShowItemInfoUI(player, item);
            }
            catch (Exception ex)
            {
                LogWarn($"[ItemInfoUI] cilinfo 例外: {ex.Message}");
            }
        }

        // == Auto-show toggle & throttle ==
        private bool cfgAutoShowItemInfoUI = true; // 設定が無ければ既定ON（後でConfigから読む）
        private readonly Dictionary<ulong, float> _lastShowAt = new Dictionary<ulong, float>();

private const string UI_ITEMINFO = "CIL_ItemInfo";
        private float cfgItemInfoAutoHideSeconds = 25f;

                // 自動表示: アクティブアイテム切替時にUIを表示
        private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            // 先に全ループを確実に停止（装備切替時の残音対策）
            StopHumLoop(player);
            StopHoldEffect(player);
            try
            {
                if (!cfgAutoShowItemInfoUI || player == null || newItem == null) return;
                var now = UnityEngine.Time.realtimeSinceStartup;
                float last;
                if (_lastShowAt.TryGetValue(player.userID, out last) && (now - last) < 1.0f) return; // スパム防止
                _lastShowAt[player.userID] = now;
                ShowItemInfoUI(player, newItem);
            

try
{
    var was = (oldItem != null) && IsLightsaber(oldItem);
    var isNow = (newItem != null) && IsLightsaber(newItem);

    if (was && !isNow)
    {
        equipped.Remove(player.userID);
        HandleUnequip(player);
        LogCil("DEBUG", "[Equip] old lightsaber -> unequip handled via OnActiveItemChanged");
    }
    if (isNow && !was)
    {
        equipped.Add(player.userID);
        var defNow = ResolveDefinitionForItem(newItem);
        HandleEquip(player, defNow);
        LogCil("DEBUG", "[Equip] new lightsaber -> equip handled via OnActiveItemChanged");
    }
}
catch (System.Exception ex)
{
    LogWarn("[Equip] OnActiveItemChanged equip/unequip handling error: " + ex.Message);
}
}
            catch { }
        }

        // UI用: アイテムから定義を解決（shortname優先、なければskinIdで照合）
        private CustomItemDefinition ResolveDefinitionForItem(Item item)
        {
            if (item == null) return null;
            var def = GetDefinition(item.info.shortname);
            if (def != null) return def;
            try
            {
                var skin = item.skin; // ulong
                if (skin != 0)
                {
                    foreach (var d in customItems.Values)
                    {
                        if (d != null && d.defaultSkinId > 0 && (ulong)d.defaultSkinId == skin)
                            return d;
                    }
                }
            }
            catch { }
            return null;
        }

private void ShowItemInfoUI(BasePlayer player, Item item)
        {
            // カスタムアイテム以外ではパネルを表示しない
            try
            {
                var active = player?.GetActiveItem();
                var sn = active?.info?.shortname;
                var def = GetDefinition(sn);
                if (def == null)
                {
                    HideItemInfoUI(player);
                    return;
                }
            }
            catch { HideItemInfoUI(player); return; }

            if (player != null) try { CuiHelper.DestroyUi(player, UI_ITEMINFO); } catch {}

            try
            {
                CuiHelper.DestroyUi(player, UI_ITEMINFO);

                var def = ResolveDefinitionForItem(item);
                var name = (def?.defaultName ?? item.info.displayName?.english) ?? item.info.shortname;
                var stats = new List<KeyValuePair<string,string>>();

                if (def != null && def.uiStats != null)
                {
                    foreach (var kv in def.uiStats)
                        stats.Add(new KeyValuePair<string,string>(kv.Key, kv.Value));
                }
                else if (def != null && def.passiveEffects != null)
                {
                    foreach (var kv in def.passiveEffects)
                    {
                        // 簡易整形（既知キーだけ人間向け表示）
                        var key = kv.Key.ToLowerInvariant();
                        string label = key switch {
                            "runspeed" => "移動速度",
                            "damagereduction" => "被ダメ軽減",
                            _ => kv.Key
                        };
                        string val = kv.Value >= 0 ? $"+{kv.Value:P0}" : $"{kv.Value:P0}";
                        stats.Add(new KeyValuePair<string,string>(label, val));
                    }
                }

                var descLines = (def?.descriptionLines != null && def.descriptionLines.Count > 0)
                    ? def.descriptionLines
                    : new List<string> { "カスタムアイテムの詳細情報。" };

                var cont = new CuiElementContainer();
                // パネル
                cont.Add(new CuiPanel {
                    Image = { Color = "0 0 0 0.85" },
                    RectTransform = { AnchorMin = "0.80 0.12", AnchorMax = "0.98 0.55" },
                    CursorEnabled = false
                }, "Overlay", UI_ITEMINFO);

                // タイトル
                cont.Add(new CuiLabel {
                    Text = { Text = name, FontSize = 18, Align = TextAnchor.MiddleLeft },
                    RectTransform = { AnchorMin = "0.02 0.90", AnchorMax = "0.98 0.98" }
                }, UI_ITEMINFO);

                // 左：ステータス一覧
                float y = 0.82f
                ;
                foreach (var kv in stats)
                {
                    cont.Add(new CuiLabel {
                        Text = { Text = $"{kv.Key}", FontSize = 14, Align = TextAnchor.MiddleLeft },
                        RectTransform = { AnchorMin = $"0.03 {y-0.05}", AnchorMax = $"0.32 {y}" }
                    }, UI_ITEMINFO);
                    cont.Add(new CuiLabel {
                        Text = { Text = $"{kv.Value}", FontSize = 14, Align = TextAnchor.MiddleRight },
                        RectTransform = { AnchorMin = $"0.33 {y-0.05}", AnchorMax = $"0.58 {y}" }
                    }, UI_ITEMINFO);
                    y -= 0.06f;
                    if (y < 0.40f) break;
                }

                // 右：説明
                float y2 = 0.82f;
                foreach (var line in descLines)
                {
                    cont.Add(new CuiLabel {
                        Text = { Text = line, FontSize = 13, Align = TextAnchor.MiddleLeft },
                        RectTransform = { AnchorMin = $"0.60 {y2-0.05}", AnchorMax = $"0.96 {y2}" }
                    }, UI_ITEMINFO);
                    y2 -= 0.06f;
                    if (y2 < 0.22f) break;
                }

                // 閉じるボタン
                cont.Add(new CuiButton {
                    Button = { Color = "0.2 0.6 0.9 0.9", Close = UI_ITEMINFO },
                    RectTransform = { AnchorMin = "0.90 0.02", AnchorMax = "0.98 0.08" },
                    Text = { Text = "閉じる", FontSize = 14, Align = TextAnchor.MiddleCenter }
                }, UI_ITEMINFO);

                CuiHelper.AddUi(player, cont);

                // 自動消去
                timer.Once(Mathf.Max(2f, cfgItemInfoAutoHideSeconds), () => { if (player != null) CuiHelper.DestroyUi(player, UI_ITEMINFO); });
            }
            catch (Exception ex)
            {
                LogWarn($"[ItemInfoUI] ShowItemInfoUI 例外: {ex.Message}");
            }
        }

        
        // ===== UI 色/表記ヘルパ =====
        private string HexToRGBA(string hex, float a = 1f)
        {
            if (string.IsNullOrEmpty(hex)) return $"1 1 1 {a:0.##}";
            hex = hex.Trim().TrimStart('#');
            byte r=255,g=255,b=255;
            if (hex.Length == 6)
            {
                r = byte.Parse(hex.Substring(0,2), System.Globalization.NumberStyles.HexNumber);
                g = byte.Parse(hex.Substring(2,2), System.Globalization.NumberStyles.HexNumber);
                b = byte.Parse(hex.Substring(4,2), System.Globalization.NumberStyles.HexNumber);
            }
            return $"{r/255f:0.###} {g/255f:0.###} {b/255f:0.###} {a:0.##}";
        }

        private string AsPercent(float v, bool treatFactor=false)
        {
            if (treatFactor) return ((v-1f)*100f).ToString("+0;-0") + "%";
            return (v*100f).ToString("+0;-0") + "%";
        }

        private string MapPerkLabel(string raw)
        {
            var k = (raw ?? "").ToLowerInvariant();
            if (k == "vampiric" || k == "lifesteal") return "ライフスティール";
            if (k == "critchance") return "クリティカル率";
            if (k == "critdamage") return "クリティカル倍率";
            if (k == "thorns") return "スパイク(反射)";
            if (k == "movementspeed") return "移動速度";
            return raw ?? "";
        }

        private string ColorForStat(string keyLower, float sign)
        {
            if (keyLower.Contains("damage")) return sign >= 0 ? "1 0.4 0.3 1" : "0.7 0.7 0.7 1"; // ダメ系
            if (keyLower.Contains("speed") || keyLower.Contains("move")) return "0.2 0.9 0.4 1";
            if (keyLower.Contains("reduction") || keyLower.Contains("def") || keyLower.Contains("armor")) return "0.3 0.6 1 1";
            if (keyLower.Contains("crit")) return "1 0.8 0.2 1";
            if (keyLower.Contains("vamp") || keyLower.Contains("life")) return "1 0.3 0.6 1";
            return sign >= 0 ? "1 1 1 1" : "0.8 0.6 0.6 1";
        }

        #endregion // メソッド定義


        // ▼ EmbeddedPrefabList から prefab_categories.json を強制再生成するユーティリティ
        private void ForceRebuildCategories(BasePlayer player)
        {
            try
            {
                var jsonPath = Path.Combine(DataFolderPath, "prefab_categories.json");
                if (File.Exists(jsonPath))
                {
                    File.Delete(jsonPath);
                }
                EnsureCategoryJson(); // ファイルが無い状態で呼ぶと EmbeddedPrefabList から生成される
                SendReply(player, "[CIL] prefab_categories.json をEmbeddedリストから再生成しました。");
            }
            catch (System.Exception ex)
            {
                SendReply(player, "[CIL] 再生成に失敗しました: " + ex.Message);
            }
        }

private void CmdCilGiveConsole(ConsoleSystem.Arg arg)
{
    if (!arg.IsAdmin)
    {
        arg.ReplyWith("管理者権限が必要です。");
        return;
    }
    if (arg.Args == null || arg.Args.Length < 2)
    {
        arg.ReplyWith("使い方: cil.give <playername> <shortname> [amount]");
        return;
    }
    string playerName = arg.Args[0];
    string shortname = arg.Args[1];
    int amount = 1;
    if (arg.Args.Length > 2 && !int.TryParse(arg.Args[2], out amount)) amount = 1;
    var target = BasePlayer.Find(playerName);
    if (target == null)
    {
        arg.ReplyWith($"プレイヤー「{playerName}」が見つかりません。");
        return;
    }
    GiveCustomItem(target, shortname, amount);
}

private void GiveCustomItem(BasePlayer player, string shortname, int amount)
{
    CustomItemDefinition def = GetDefinition(shortname);
    Item item;

    if (def != null)
    {
        // “新規shortname”は生成時だけ parent を使う（例: lightsaber → longsword）
        var baseShort = string.IsNullOrEmpty(def.parentShortname) ? def.shortname : def.parentShortname;
        item = ItemManager.CreateByName(baseShort, amount, def.defaultSkinId);

        if (item == null)
        {
            SendReply(player, $"アイテム生成に失敗しました: {baseShort}");
            return;
        }

        // 表示名上書き（任意）
        if (!string.IsNullOrEmpty(def.defaultName))
        {
            item.name = def.defaultName;
            item.MarkDirty();
        }
    }
    else
    {
        var defItem = ItemManager.FindItemDefinition(shortname);
        if (defItem == null)
        {
            SendReply(player, $"アイテム '{shortname}' は見つかりません。");
            return;
        }
        item = ItemManager.CreateByName(shortname, amount);
        if (item == null)
        {
            SendReply(player, $"アイテム生成に失敗しました: {shortname}");
            return;
        }
    }

    player.GiveItem(item);
    SendReply(player, $"アイテム '{shortname}' ×{amount.ToString()} を配布しました。");
    if (def != null && def.showInfoOnGive)
        ShowItemInfo(player, def);
}


// SendReply(player, $"アイテム '{shortname}' ×{amount.ToString()} を配布しました。");

        private readonly Dictionary<string, string> presetEffectPaths = new Dictionary<string, string>();
        private readonly HashSet<string> knownFxNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // FX名キャッシュ

        /// <summary>
        /// Rustの公開APIで全エフェクト一覧は取得できないため、当プラグインでは
        /// Data/CustomItemLoader/prefab_categories.json から既知のパスを収集する。
        /// </summary>
        private void PopulateKnownFxList()
        {
            knownFxNames.Clear();
            try
            {
                var jsonPath = Path.Combine(DataFolderPath, "prefab_categories.json");
                if (File.Exists(jsonPath))
                {
                    var data = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(jsonPath));
                    if (data != null)
                    {
                        foreach (var list in data.Values)
                        {
                            if (list == null) continue;
                            foreach (var p in list)
                            {
                                if (!string.IsNullOrEmpty(p)) knownFxNames.Add(p);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogWarn($"[CIL] PopulateKnownFxList 失敗: {ex.Message}");
            }
        }

    

// --- moved into class scope (v0.00.0005) ---
private void StartHandGlowLight(BasePlayer player, CustomItemDefinition def = null)
{
    try {
        def = def ?? GetDefinition(config.LightsaberShortname ?? LIGHTSABER_SHORTNAME);
        if (player == null || def == null) return;
        // [CIL_PATCH mute-glowlight] StopHandGlowLight(player);

        var bone = player.model?.FindBone("r_hand") ?? player.eyes.transform;
        var go = new UnityEngine.GameObject("CIL_HandLight");
        go.transform.SetParent(bone, false);
        go.transform.localPosition = new UnityEngine.Vector3(0.05f, 0.1f, 0.0f);

        var light = go.AddComponent<UnityEngine.Light>();
        UnityEngine.Color c;
        if (!UnityEngine.ColorUtility.TryParseHtmlString(def.visualEffects?.bladeColor ?? config.BeamColor, out c))
            c = UnityEngine.Color.cyan;
        light.type = UnityEngine.LightType.Point;
        light.color = c;
        light.intensity = def.visualEffects?.glowIntensity ?? 1f;
        light.range = def.visualEffects?.bladeLength ?? config.BeamLength;
        light.shadows = UnityEngine.LightShadows.None;

        handGlow[player.userID] = go;
    } catch (System.Exception ex) {
        LogWarn($"StartHandGlowLight error: {ex.Message}");
    }
}


private void StopHandGlowLight(BasePlayer player)
{
    try {
        if (player == null) return;
        UnityEngine.GameObject go;
        if (handGlow.TryGetValue(player.userID, out go))
        {
            if (go != null) UnityEngine.GameObject.Destroy(go);
            handGlow.Remove(player.userID);
        }
    } catch {}
}


        // --- added: ensure UI hide helper exists ---
        private void HideItemInfoUI(BasePlayer player)
        {
            try { CuiHelper.DestroyUi(player, UI_ITEMINFO); } catch {}
        }


// ===== CIL_FxVerifier v0.00.0001 (appended) =====
// ========== FX 検証・保存ユーティリティ ==========
private bool _fxVerifierLoaded = false;
private HashSet<string> verifiedEffects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

private void EnsureFxVerifierLoaded()
{
    if (_fxVerifierLoaded) return;
    try
    {
        var loaded = Interface.Oxide.DataFileSystem.ReadObject<HashSet<string>>("CustomItemLoader/verified_effects");
        if (loaded != null) verifiedEffects = loaded;
    }
    catch { /* 初回は未作成でOK */ }
    _fxVerifierLoaded = true;
}

private void SaveVerifiedEffects()
{
    Interface.Oxide.DataFileSystem.WriteObject("CustomItemLoader/verified_effects", verifiedEffects);
}

private static bool PrefabExists(string path)
{
    try
    {
        return GameManager.server?.FindPrefab(path) != null;
    }
    catch { return false; }
}

private static IEnumerable<string> LoadFxCandidates(string filter = null)
{
    var list = new List<string>();
    try
    {
        var fxPath = System.IO.Path.Combine(Interface.Oxide.DataDirectory, "CustomItemLoader", "fxlist.txt");
        if (System.IO.File.Exists(fxPath))
        {
            foreach (var line in System.IO.File.ReadAllLines(fxPath))
            {
                var s = (line ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(s)) continue;
                if (!s.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(filter) && s.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                // シーン依存になりやすいパスは除外（必要に応じて調整）
                if (s.StartsWith("assets/content/effects/", StringComparison.OrdinalIgnoreCase)) continue;

                list.Add(s);
            }
        }
    }
    catch (Exception ex)
    {
        Interface.Oxide.LogWarning($"[FxVerify] 候補読込で例外: {ex.Message}");
    }
    return list;
}

private void RunEffectForPlayer(BasePlayer player, string prefabPath)
{
    if (player == null || string.IsNullOrEmpty(prefabPath)) return;
    var pos = player.transform.position + player.eyes?.HeadForward() * 1.5f ?? UnityEngine.Vector3.up;
    try
    {
        Effect.server.Run(prefabPath, pos, UnityEngine.Vector3.up, player.net?.connection);
    }
    catch (Exception ex)
    {
        Puts($"[FxVerify] 実行例外: {prefabPath} :: {ex.Message}");
    }
}

// --- 単発テスト: /ciltestfx <prefabPath> [count] [intervalSec] ---
[ChatCommand("ciltestfx")]
private void CmdCilTestFx(BasePlayer player, string command, string[] args)
{
    if (player == null) return;
    if (args == null || args.Length == 0)
    {
        SendReply(player, "使用方法: /ciltestfx <prefabPath> [count=1] [interval=0.2]");
        return;
    }

    EnsureFxVerifierLoaded();

    var path = string.Join(" ", args[0]).Trim();
    int count = 1;
    float interval = 0.2f;
    if (args.Length >= 2) int.TryParse(args[1], out count);
    if (args.Length >= 3) float.TryParse(args[2], out interval);
    if (count < 1) count = 1;
    if (interval < 0.05f) interval = 0.05f;

    if (!PrefabExists(path))
    {
        SendReply(player, $"[FxVerify] 見つかりません: {path}");
        return;
    }

    SendReply(player, $"[FxVerify] 実行: {path} x{count} every {interval:0.00}s");
    for (int i = 0; i < count; i++)
    {
        int k = i;
        timer.Once(k * interval, () =>
        {
            RunEffectForPlayer(player, path);
        });
    }

    // OK だったものは手動でも登録できるように
    verifiedEffects.Add(path);
    SaveVerifiedEffects();
}

// --- 連続検証: /cilfxverify [filter] [intervalSec] ---
[ChatCommand("cilfxverify")]
private void CmdCilFxVerify(BasePlayer player, string command, string[] args)
{
    if (player == null) return;
    EnsureFxVerifierLoaded();

    string filter = null;
    float interval = 0.15f;
    if (args != null && args.Length >= 1) filter = string.IsNullOrWhiteSpace(args[0]) ? null : args[0];
    if (args != null && args.Length >= 2) float.TryParse(args[1], out interval);
    if (interval < 0.05f) interval = 0.05f;

    var candidates = LoadFxCandidates(filter).ToList();
    if (candidates.Count == 0)
    {
        SendReply(player, "[FxVerify] 候補がありません。fxlist.txt を確認してください。");
        return;
    }

    int ok = 0, miss = 0;
    SendReply(player, $"[FxVerify] 開始: {candidates.Count}件 (filter='{filter ?? "なし"}', interval={interval:0.00}s)");

    for (int i = 0; i < candidates.Count; i++)
    {
        string path = candidates[i];
        int k = i;
        timer.Once(k * interval, () =>
        {
            if (PrefabExists(path))
            {
                verifiedEffects.Add(path);
                ok++;
                RunEffectForPlayer(player, path);
                Puts($"[FxVerify] OK: {path}");
            }
            else
            {
                miss++;
                Puts($"[FxVerify] Missing: {path}");
            }
        });
    }

    timer.Once(candidates.Count * interval + 0.25f, () =>
    {
        SaveVerifiedEffects();
        SendReply(player, $"[FxVerify] 完了 OK:{ok} NG:{miss} 合計:{candidates.Count} 保存先: oxide/data/CustomItemLoader/verified_effects.json");
    });
}

// --- 許可カタログへ保存: /cilfxsave ---
// verified_effects.json の内容を prefab_categories.json の "effects" に書き戻します。
[ChatCommand("cilfxsave")]
private void CmdCilFxSave(BasePlayer player, string command, string[] args)
{
    if (player == null) return;
    EnsureFxVerifierLoaded();

    // 既存を読み取り
    var obj = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    try
    {
        var existing = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, object>>("CustomItemLoader/prefab_categories");
        if (existing != null) obj = existing;
    }
    catch { /* なくてもOK */ }

    // 書き戻し
    obj["effects"] = verifiedEffects.ToList();
    Interface.Oxide.DataFileSystem.WriteObject("CustomItemLoader/prefab_categories", obj);

    SendReply(player, $"[FxVerify] 保存しました: prefab_categories.json / effects = {verifiedEffects.Count} 件");
}

// ===== End of CIL_FxVerifier =====

        // ==== CIL パッチ: FxImportEffects 受け口（挿入専用・既存構造不変） ====
        // 目的: CustomItemFxPreloader からの publish を受け取り、effects カタログへ統合保存する。
        // 取付位置: CustomItemLoader クラスの終端 '}' の直前に「このメソッド全体」を挿入。
        // 依存: 追加の using は不要（本体に含まれているため）。
        // 変更方針: 既存コードは一切変更しない / 追記のみ。

        // --- ここから挿入 ---
        object FxImportEffects(System.Collections.Generic.List<string> fxList)
        {
            if (fxList == null || fxList.Count == 0) return 0;

            var dataDir = System.IO.Path.Combine(Interface.Oxide.DataDirectory, "CustomItemLoader");
            try { System.IO.Directory.CreateDirectory(dataDir); } catch {}

            var path = System.IO.Path.Combine(dataDir, "prefab_categories.json");

            // 既存JSONを読み取り（存在しなければ空オブジェクト）
            Newtonsoft.Json.Linq.JObject node;
            try
            {
                if (System.IO.File.Exists(path))
                {
                    var txt = System.IO.File.ReadAllText(path);
                    node = Newtonsoft.Json.Linq.JObject.Parse(txt);
                }
                else
                {
                    node = new Newtonsoft.Json.Linq.JObject();
                }
            }
            catch
            {
                node = new Newtonsoft.Json.Linq.JObject();
            }

            // 既存 effects を取り出し＋重複排除
            var set = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            var arr = node["effects"] as Newtonsoft.Json.Linq.JArray;
            if (arr != null)
            {
                foreach (var t in arr)
                {
                    var s = t?.ToString();
                    if (!string.IsNullOrEmpty(s)) set.Add(s);
                }
            }
            int before = set.Count;

            foreach (var s in fxList)
            {
                if (!string.IsNullOrEmpty(s)) set.Add(s);
            }

            // 書き戻し（昇順ソート）
            var outList = new System.Collections.Generic.List<string>(set);
            outList.Sort(System.StringComparer.OrdinalIgnoreCase);
            node["effects"] = Newtonsoft.Json.Linq.JArray.FromObject(outList);

            try
            {
                System.IO.File.WriteAllText(path, node.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch (System.Exception ex)
            {
                Puts($"[CustomItemLoader] FxImportEffects: 書き込み失敗: {ex.Message}");
                return 0;
            }

            Puts($"[CustomItemLoader] FxImportEffects: +{(set.Count - before)} 件  total={set.Count} -> prefab_categories.json");

            // 既存の内部キャッシュも更新（即時反映）
            try
            {
                CIL_LoadPrefabCategories();
                CIL_CleanupPrefabCategories(true);
            }
            catch {}

            return (set.Count - before);
        }
        // --- ここまで挿入 ---
        // --- 追記: CIFPへ使用FXを集約依頼（CIL負担軽減） ---
//         private void Cifp_IngestUsedEffect(string path)
//         {
//             if (string.IsNullOrEmpty(path)) return;
//             var cifp = plugins.Find("CustomItemFxPreloader");
//             try { cifp?.Call("FxIngestUsedEffect", path); } catch {}
//         }

        // --- 追記: CIFPからのカタログ受信（Hook受け口） ---
        object OnCilFxCatalogReady(System.Collections.Generic.List<string> verified)
            => FxImportEffects(verified);



    private void OnJsonRenamed(object sender, System.IO.RenamedEventArgs e)
    {
        var name = System.IO.Path.GetFileName(e.FullPath);
        if (!name.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase)) return;
        if (JsonWatchIgnore.Contains(name)) return;

        jsonLastChangeUtc = System.DateTime.UtcNow;
        if (!jsonReloadQueued)
        {
            jsonReloadQueued = true;
            NextTick(DebouncedReloadJson);
        }
    }

        // Bulk load & validate all custom item JSONs (safe no-op if none)
        private void LoadAllCustomItems()
        {
            try
            {
                EnsureDataFolder();
                EnsureCategoryJson();
                var files = System.IO.Directory.GetFiles(DataFolderPath, "*.json", System.IO.SearchOption.TopDirectoryOnly);
                var skip = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
                {
                    "prefab_categories.json",
                    "effects_verified.json",
                    "verified_effects.json"
                };
                int ok=0, fail=0, duplicate=0, missingParent=0;
                foreach (var path in files)
                {
                    var name = System.IO.Path.GetFileName(path);
                    if (skip.Contains(name)) continue;

                    string err; int line, col;
                    if (ValidateJsonFile(path, out err, out line, out col))
                    {
                        // 事前にJSONを読み取り、shortname等を確認しつつ登録
                        try
                        {
                            var txt = System.IO.File.ReadAllText(path);
                            var jo = Newtonsoft.Json.Linq.JObject.Parse(txt);

                            if (jo["parentShortname"] == null && jo["parent"] != null) jo["parentShortname"] = jo["parent"];
                            if (jo["defaultName"] == null && jo["displayName"] != null) jo["defaultName"] = jo["displayName"];
                            if (jo["defaultSkinId"] == null && jo["skinId"] != null) jo["defaultSkinId"] = jo["skinId"];

                            var def = jo.ToObject<CustomItemDefinition>();

                            if (def != null && !string.IsNullOrEmpty(def.shortname))
                            {
                                // Duplicate判定
                                if (customItems.ContainsKey(def.shortname))
                                {
                                    LogWarn($"[LoadJson] Duplicate: {def.shortname} ← {name} (既に {shortnameSource[def.shortname]} により登録済み。上書きします)");
                                    duplicate++;
                                }

                                // parentShortname の存在チェック（なければ自身shortnameでLookup）
                                string parent = string.IsNullOrEmpty(def.parentShortname) ? def.shortname : def.parentShortname;
                                if (ItemManager.FindItemDefinition(parent) == null)
                                {
                                    missingParent++;
                                    LogWarn($"[LoadJson] MissingParent: {def.shortname} (parent='{parent}') ← {name}");
                                }

                                customItems[def.shortname] = def;
                                shortnameSource[def.shortname] = name;
                                ok++;
                                LogInfo($"[LoadJson] OK: {def.shortname} ← {name}");
                            }
                            else
                            {
                                fail++;
                                LogWarn($"[LoadJson] FAIL: shortname 未設定 ← {name}");
                            }
                        }
                        catch (System.Exception exLoad)
                        {
                            fail++;
                            LogWarn($"[LoadJson] FAIL: {name} 例外: {exLoad.Message}");
                        }
                    }
                    else
                    {
                        fail++;
                        LogWarn($"[LoadJson] FAIL: {name} : {err} (line {line}, col {col})");
                    }
                }

                LogWarn($"JSON読み込み完了 OK:{ok} FAIL:{fail} Duplicate:{duplicate} MissingParent:{missingParent} TOTAL:{files.Length} registered:{customItems.Count}");
            }
            catch (System.Exception ex)
            {
                LogError($"[LoadAll] 例外: {ex.Message}");
            }
        }
    void DebouncedReloadJson()
    {
        var elapsed = (System.DateTime.UtcNow - jsonLastChangeUtc).TotalSeconds;
        if (elapsed < 0.5)
        {
            timer.Once(0.5f, DebouncedReloadJson);
            return;
        }

        jsonReloadQueued = false;

        try
        {
            if (jsonWatcher != null) jsonWatcher.EnableRaisingEvents = false;
            LoadAllCustomItems();
            LogInfo("[Watcher] 変更検知によりアイテムJSONを再読込しました");
        }
        catch (System.Exception ex)
        {
            LogError($"[Watcher] 自動再読込中にエラー: {ex.Message}");
        }
        finally
        {
            if (jsonWatcher != null) jsonWatcher.EnableRaisingEvents = true;
        }
    }

}
}
