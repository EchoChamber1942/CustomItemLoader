CustomItemLoader (CIL)
Bilingual README-style Specification Summary
日本語 / English

==================================
[JP] 概要
==================================

CustomItemLoader（CIL）は、Rust/uMod 用のカスタムアイテム定義ローダーです。
JSON ファイルで独自アイテムを定義し、ゲーム内へ読み込み、付与、検証、FX管理、他プラグイン連携を行えます。

主な用途:
- カスタム武器・装備・特殊アイテムの定義
- JSONベースでの管理
- エフェクト（FX）やUI表示の拡張
- 外部プラグインとの連携
- 自動サンプル生成とデータ監視による再読み込み
==================================
[EN] Overview
==================================
CustomItemLoader (CIL) is a custom item definition loader for Rust/uMod.
It lets you define custom items in JSON, load them into the server, grant them in-game, verify definitions, manage FX, and integrate with other plugins.

Main purposes:
- Define custom weapons, gear, and special items
- Manage items through JSON files
- Extend visual FX and UI presentation
- Integrate with external plugins
- Auto-generate starter files and reload data when files change

==================================
[JP] 推奨・任意連携プラグイン
==================================

1. CustomItemDefinitions（推奨）
   CILが読み込んだアイテム定義を中央レジストリとして登録できます。
   他のプラグイン（ショップ、ルートボックス、UI、スキル系など）から、
   item ID / shortname / rarity などを安定したAPIで参照できます。

2. EpicLoot（任意）
   rarity、perks、set 情報をエクスポートし、
   EpicLoot の進行やルートテーブルに参加させられます。

3. ItemPerks（任意）
   JSON定義の perks / exportPerks をもとに、
   コード変更なしでカスタムアイテムのパーク登録が可能です。

4. ZLevelsRemastered（任意）
   zlMinLevel と zlGatherBonusPct を使って、
   最低レベル制限や追加採集ボーナスを適用できます。

5. CustomItemFxPreloader（任意）
   CILで使用されたFXパスを安全なフック層で通知し、
   外部側で事前ロード、キャッシュ、解析ができます。

==================================
[EN] Integrations / Recommended Plugins
==================================
1. CustomItemDefinitions (recommended)
   CIL can push loaded item definitions into a central registry.
   Other plugins such as shops, loot systems, UI tools, or skill plugins
   can query item IDs, shortnames, rarity, and more through a stable API.

2. EpicLoot (optional)
   Exports rarity, perks, and set data so custom items can participate
   in EpicLoot progression and loot tables.

3. ItemPerks (optional)
   Registers perk data for custom items directly from JSON
   using perks / exportPerks, without changing plugin code.

4. ZLevelsRemastered (optional)
   Supports minimum level requirements and gather bonuses
   through zlMinLevel and zlGatherBonusPct.

5. CustomItemFxPreloader (optional)
   Reports used FX paths through a safe hook layer so another plugin
   can preload, cache, or analyze the effects.

==================================
[JP] 権限
==================================
Oxide の権限システムを使用します。

付与:
oxide.grant <user or group> <name or steam id> <permission>

剥奪:
oxide.revoke <user or group> <name or steam id> <permission>

定義済み権限:
- customitemloader.give
  /cil give を使って定義済みカスタムアイテムを付与可能
- customitemloader.forcejump
  ライトセーバー装備時の /forcejump を使用可能
==================================
[EN] Permissions
==================================

This plugin uses the Oxide permission system.

Grant:
oxide.grant <user or group> <name or steam id> <permission>

Revoke:
oxide.revoke <user or group> <name or steam id> <permission>

Defined permissions:
- customitemloader.give
  Allows use of /cil give to spawn defined custom items
- customitemloader.forcejump
  Allows use of /forcejump while a lightsaber is equipped

==================================
[JP] チャットコマンド
==================================

/cil
  ヘルプ表示とサブコマンド一覧

/cilist
  /cil の別名

/cil list
  読み込み済みカスタムアイテム一覧を表示

/cil info <shortname>
  指定アイテムの詳細情報を表示

/cil give <shortname> [amount]
  呼び出し元にカスタムアイテムを付与

/cil verify
  全定義の検証を実行

/cil effects
  FX登録状況や不足・無効FXの概要を表示

/cil fxlist [all]
  fxlist.txt を元に既知FX名を表示

/cil jsoncheck [file]
  JSONファイルの構文検証と行・列エラー表示

/cil config
  設定と全カスタムアイテム定義を再読み込み

/cil log [filter]
  内部ログバッファ表示

/cil log clear
  内部ログバッファ消去

/cil log save
  内部ログバッファ保存

/cil debug
  デバッグモード切替

/cilinfo
  現在装備中アイテムの情報UI/データ表示

/cilfxsave
  prefab category / FX 情報をデータファイルへ保存

/cilfxverify
  フィルタや間隔付きでFX検証を実行

/ciltestfx <prefabPath> [count] [interval]
  指定FXを繰り返し再生してデバッグ

/forcejump
  前方＋上方向へ Force Jump を実行

==================================
[EN] Chat Commands
==================================
/cil
  Shows help and the sub-command list

/cilist
  Alias of /cil

/cil list
  Lists all loaded custom item definitions

/cil info <shortname>
  Shows detailed information for the selected custom item

/cil give <shortname> [amount]
  Gives the item to the caller

/cil verify
  Runs a verification pass on all item definitions

/cil effects
  Shows a summary of effect registration and missing/invalid FX

/cil fxlist [all]
  Lists known FX names from fxlist.txt

/cil jsoncheck [file]
  Validates JSON files and reports line/column errors

/cil config
  Reloads config and all custom item definitions

/cil log [filter]
  Shows the internal log buffer

/cil log clear
  Clears the internal log buffer

/cil log save
  Saves the internal log buffer

/cil debug
  Toggles debug mode

/cilinfo
  Shows UI/data for the player's currently equipped item

/cilfxsave
  Saves current prefab category / FX info to data files

/cilfxverify
  Runs FX verification with optional filters and intervals

/ciltestfx <prefabPath> [count] [interval]
  Repeatedly plays a test effect for debugging

/forcejump
  Performs a configurable forward/upward Force Jump

==================================
[JP] データ保存先と自動生成ファイル
==================================

データ保存先:
oxide/data/CustomItemLoader/

初回実行時に自動で行う内容:
- データフォルダ作成
- サンプルアイテム lightsaber.json 生成
- 安全なFX例を含む prefab_categories.json 生成
- ゲームのマニフェスト由来FXをまとめた fxlist.txt 生成
- CustomItemFxPreloader がある場合は検証データを読み込み

これらのファイル作成後は自由に編集可能です。
データフォルダは監視されており、JSON保存時に短い待機後、自動再読み込みされます。

==================================
[EN] Data Folder & Auto-generated Files
==================================

Data path:
oxide/data/CustomItemLoader/

On first run, the plugin automatically:
- Creates the data folder
- Generates a sample item file: lightsaber.json
- Creates prefab_categories.json with safe FX examples
- Builds fxlist.txt from known FX prefab paths
- Loads effect verification data from CustomItemFxPreloader if available

After that, you can edit or replace the files freely.
The data folder is watched, and saved JSON files are reloaded automatically after a short debounce.

==================================
[JP] Item JSON 形式
==================================

oxide/data/CustomItemLoader/ 内の各 .json ファイルが1つのカスタムアイテムを定義します。

必須・主要フィールド:
- shortname
  カスタムアイテム固有ID。/cil give や他プラグイン参照に使用
- parentShortname
  ベースとなるRustアイテム shortname
  旧キー parent も互換対応
- maxStackSize
  最大スタック数（既定: 1）
- category
  任意カテゴリ文字列（既定: Weapon）
- defaultName
  表示名
  旧キー displayName も互換対応
- defaultSkinId
  適用するWorkshop skin ID
  旧キー skinId も互換対応

よく使う任意フィールド:
- description
- rarity
- muteAllSounds
- passiveEffects
- perks
- exportPerks
- loots
- setId
- setBonuses
- visualEffects
- customEffects
- igniteEffect
- glowLoopEffect
- humLoopEffect
- swingEffect
- criticalEffect
- parryEffect
- descriptionLines
- uiStats
- uiHeaderColor
- uiAccentColor
- iconTint
- iconSprite
- zlMinLevel
- zlGatherBonusPct

==================================
[EN] Item JSON Format
==================================

Each .json file inside oxide/data/CustomItemLoader/ defines one custom item.

Core fields:
- shortname
  Unique custom item ID used by /cil give and other plugins
- parentShortname
  Base Rust item shortname to clone from
  Legacy key parent is still accepted
- maxStackSize
  Maximum stack size (default: 1)
- category
  Optional category text (default: Weapon)
- defaultName
  Display name shown to players
  Legacy key displayName is still accepted
- defaultSkinId
  Workshop skin ID applied to the item
  Legacy key skinId is still accepted

Useful optional fields:
- description
- rarity
- muteAllSounds
- passiveEffects
- perks
- exportPerks
- loots
- setId
- setBonuses
- visualEffects
- customEffects
- igniteEffect
- glowLoopEffect
- humLoopEffect
- swingEffect
- criticalEffect
- parryEffect
- descriptionLines
- uiStats
- uiHeaderColor
- uiAccentColor
- iconTint
- iconSprite
- zlMinLevel
- zlGatherBonusPct

==================================
[JP] 自動生成サンプル
==================================
初回起動時、次のファイルが生成されます:
oxide/data/CustomItemLoader/lightsaber.json

最小サンプル例:

{
  "muteAllSounds": true,
  "shortname": "lightsaber",
  "parent": "longsword",
  "stackSize": 1,
  "category": "Weapon",
  "displayName": "Lightsaber",
  "skinId": -1469578201
}

互換キー parent / displayName / skinId は、
読み込み時に parentShortname / defaultName / defaultSkinId へ内部変換されます。

==================================
[EN] Auto-generated Sample Item
==================================

On first server start, the plugin writes:
oxide/data/CustomItemLoader/lightsaber.json

Minimal sample:

{
  "muteAllSounds": true,
  "shortname": "lightsaber",
  "parent": "longsword",
  "stackSize": 1,
  "category": "Weapon",
  "displayName": "Lightsaber",
  "skinId": -1469578201
}

Legacy keys parent, displayName, and skinId are mapped internally to
parentShortname, defaultName, and defaultSkinId.

==================================
[JP] 拡張サンプル
==================================

lightsaber_blue.json のような別JSONを追加し、
defaultName、defaultSkinId、色、FX、各種連携項目を差し替えることで、
他のカスタムアイテムのテンプレートとして流用できます。

追加手順:
1. oxide/data/CustomItemLoader/ に JSON を保存
2. 数秒以内に自動リロード、または oxide.reload CustomItemLoader
3. /cil give <shortname> で配布し、挙動とFXを確認
4. 
==================================
[EN] Extended Example Usage
==================================

You can create another file such as lightsaber_blue.json and reuse it as a template
by changing defaultName, defaultSkinId, colors, FX, and integration fields.

Basic workflow:
1. Save a JSON file under oxide/data/CustomItemLoader/
2. Wait for auto-reload or run oxide.reload CustomItemLoader
3. Use /cil give <shortname> to test the item and its FX

