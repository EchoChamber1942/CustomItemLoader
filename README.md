# CustomItemLoader (CIL)

**Bilingual README / 英和README**

CustomItemLoader (CIL) is a custom item definition loader for Rust/uMod.  
CustomItemLoader（CIL）は、Rust/uMod 用のカスタムアイテム定義ローダーです。

It allows you to define custom items in JSON, load them into the server, grant them in-game, verify definitions, manage FX, and integrate with other plugins.  
JSON ファイルで独自アイテムを定義し、サーバーへ読み込み、ゲーム内付与、定義検証、FX管理、他プラグイン連携を行えます。

> **Note / 補足**  
> This plugin focuses on defining, loading, validating, and integrating custom items for Rust/uMod servers.  
> このプラグインは、Rust/uMod サーバー向けにカスタムアイテムの定義、読み込み、検証、連携を行うことを主目的としています。

---

## Overview / 概要

### English
**Main purposes:**
- Define custom weapons, gear, and special items
- Manage items through JSON files
- Extend visual FX and UI presentation
- Integrate with external plugins
- Auto-generate starter files and reload data when files change

### 日本語
**主な用途:**
- カスタム武器・装備・特殊アイテムの定義
- JSONベースでの管理
- エフェクト（FX）やUI表示の拡張
- 外部プラグインとの連携
- 自動サンプル生成とデータ監視による再読み込み

---

## Integrations / Recommended Plugins  
## 連携プラグイン / 推奨連携

### 1. CustomItemDefinitions *(recommended / 推奨)*
**EN:** CIL can push loaded item definitions into a central registry. Other plugins such as shops, loot systems, UI tools, and skill plugins can query item IDs, shortnames, rarity, and more through a stable API.  
**JP:** CILが読み込んだアイテム定義を中央レジストリへ登録できます。他のプラグイン（ショップ、ルートシステム、UIツール、スキル系など）から、item ID / shortname / rarity などを安定したAPIで参照できます。

### 2. EpicLoot *(optional / 任意)*
**EN:** Exports rarity, perks, and set data so custom items can participate in EpicLoot progression and loot tables.  
**JP:** rarity、perks、set 情報をエクスポートし、EpicLoot の進行やルートテーブルへ参加させられます。

### 3. ItemPerks *(optional / 任意)*
**EN:** Registers perk data for custom items directly from JSON using `perks` / `exportPerks`, without changing plugin code.  
**JP:** JSON定義の `perks` / `exportPerks` をもとに、コード変更なしでカスタムアイテムのパーク登録が可能です。

### 4. ZLevelsRemastered *(optional / 任意)*
**EN:** Supports minimum level requirements and gather bonuses through `zlMinLevel` and `zlGatherBonusPct`.  
**JP:** `zlMinLevel` と `zlGatherBonusPct` を使って、最低レベル制限や追加採集ボーナスを適用できます。

### 5. CustomItemFxPreloader *(optional / 任意)*
**EN:** Reports used FX paths through a safe hook layer so another plugin can preload, cache, or analyze the effects.  
**JP:** CILで使用されたFXパスを安全なフック層で通知し、外部側で事前ロード、キャッシュ、解析を行えます。

---

## Permissions / 権限

### English
This plugin uses the Oxide permission system.

**Grant**
```txt
oxide.grant <user or group> <name or steam id> <permission>
```

**Revoke**
```txt
oxide.revoke <user or group> <name or steam id> <permission>
```

**Defined permissions**
- `customitemloader.give`  
  Allows use of `/cil give` to spawn defined custom items
- `customitemloader.forcejump`  
  Allows use of `/forcejump` while a lightsaber is equipped

### 日本語
このプラグインは Oxide の権限システムを使用します。

**付与**
```txt
oxide.grant <user or group> <name or steam id> <permission>
```

**剥奪**
```txt
oxide.revoke <user or group> <name or steam id> <permission>
```

**定義済み権限**
- `customitemloader.give`  
  `/cil give` を使って定義済みカスタムアイテムを付与可能
- `customitemloader.forcejump`  
  ライトセーバー装備時の `/forcejump` を使用可能

---

## Chat Commands / チャットコマンド

| Command | English | 日本語 |
|---|---|---|
| `/cil` | Shows help and the sub-command list | ヘルプ表示とサブコマンド一覧 |
| `/cilist` | Alias of `/cil` | `/cil` の別名 |
| `/cil list` | Lists all loaded custom item definitions | 読み込み済みカスタムアイテム一覧を表示 |
| `/cil info <shortname>` | Shows detailed information for the selected custom item | 指定アイテムの詳細情報を表示 |
| `/cil give <shortname> [amount]` | Gives the item to the caller | 呼び出し元にカスタムアイテムを付与 |
| `/cil verify` | Runs a verification pass on all item definitions | 全定義の検証を実行 |
| `/cil effects` | Shows a summary of effect registration and missing/invalid FX | FX登録状況や不足・無効FXの概要を表示 |
| `/cil fxlist [all]` | Lists known FX names from `fxlist.txt` | `fxlist.txt` を元に既知FX名を表示 |
| `/cil jsoncheck [file]` | Validates JSON files and reports line/column errors | JSONファイルの構文検証と行・列エラー表示 |
| `/cil config` | Reloads config and all custom item definitions | 設定と全カスタムアイテム定義を再読み込み |
| `/cil log [filter]` | Shows the internal log buffer | 内部ログバッファ表示 |
| `/cil log clear` | Clears the internal log buffer | 内部ログバッファ消去 |
| `/cil log save` | Saves the internal log buffer | 内部ログバッファ保存 |
| `/cil debug` | Toggles debug mode | デバッグモード切替 |
| `/cilinfo` | Shows UI/data for the player's currently equipped item | 現在装備中アイテムの情報UI/データ表示 |
| `/cilfxsave` | Saves current prefab category / FX info to data files | prefab category / FX 情報をデータファイルへ保存 |
| `/cilfxverify` | Runs FX verification with optional filters and intervals | フィルタや間隔付きでFX検証を実行 |
| `/ciltestfx <prefabPath> [count] [interval]` | Repeatedly plays a test effect for debugging | 指定FXを繰り返し再生してデバッグ |
| `/forcejump` | Performs a configurable forward/upward Force Jump | 前方＋上方向へ Force Jump を実行 |

---

## Configuration / 設定

### English
This plugin stores its settings in the config directory and allows reloading through the chat command system.

**Config file**
```txt
oxide/config/CustomItemLoader.json
```

**Operational notes**
- Item definitions are stored in the data folder, not in the config file
- The plugin can reload config and item definitions with `/cil config`
- JSON validation is available through `/cil jsoncheck [file]`
- Debug output can be toggled with `/cil debug`

### 日本語
このプラグインの設定は config ディレクトリに保存され、チャットコマンドから再読み込みできます。

**設定ファイル**
```txt
oxide/config/CustomItemLoader.json
```

**運用メモ**
- アイテム定義本体は config ではなく data フォルダに保存されます
- `/cil config` で設定とアイテム定義を再読み込みできます
- `/cil jsoncheck [file]` で JSON 構文検証を実行できます
- `/cil debug` でデバッグ出力を切り替えできます

---

## Data Folder & Auto-generated Files  
## データ保存先と自動生成ファイル

### English
**Data path**
```txt
oxide/data/CustomItemLoader/
```

**On first run, the plugin automatically:**
- Creates the data folder
- Generates a sample item file: `lightsaber.json`
- Creates `prefab_categories.json` with safe FX examples
- Builds `fxlist.txt` from known FX prefab paths
- Loads effect verification data from `CustomItemFxPreloader` if available

After that, you can edit or replace the files freely.  
The data folder is watched, and saved JSON files are reloaded automatically after a short debounce.

### 日本語
**データ保存先**
```txt
oxide/data/CustomItemLoader/
```

**初回実行時に自動で行う内容:**
- データフォルダ作成
- サンプルアイテム `lightsaber.json` 生成
- 安全なFX例を含む `prefab_categories.json` 生成
- 既知のFX prefab path をまとめた `fxlist.txt` 生成
- `CustomItemFxPreloader` がある場合は検証データを読み込み

これらのファイル作成後は自由に編集可能です。  
データフォルダは監視されており、JSON保存時に短い待機後、自動再読み込みされます。

---

## Item JSON Format / Item JSON 形式

### English
Each `.json` file inside `oxide/data/CustomItemLoader/` defines one custom item.

**Core fields**
- `shortname`  
  Unique custom item ID used by `/cil give` and other plugins
- `parentShortname`  
  Base Rust item shortname to clone from  
  Legacy key `parent` is still accepted
- `maxStackSize`  
  Maximum stack size *(default: 1)*
- `category`  
  Optional category text *(default: Weapon)*
- `defaultName`  
  Display name shown to players  
  Legacy key `displayName` is still accepted
- `defaultSkinId`  
  Workshop skin ID applied to the item  
  Legacy key `skinId` is still accepted

**Useful optional fields**
- `description`
- `rarity`
- `muteAllSounds`
- `passiveEffects`
- `perks`
- `exportPerks`
- `loots`
- `setId`
- `setBonuses`
- `visualEffects`
- `customEffects`
- `igniteEffect`
- `glowLoopEffect`
- `humLoopEffect`
- `swingEffect`
- `criticalEffect`
- `parryEffect`
- `descriptionLines`
- `uiStats`
- `uiHeaderColor`
- `uiAccentColor`
- `iconTint`
- `iconSprite`
- `zlMinLevel`
- `zlGatherBonusPct`

### 日本語
`oxide/data/CustomItemLoader/` 内の各 `.json` ファイルが1つのカスタムアイテムを定義します。

**主要フィールド**
- `shortname`  
  カスタムアイテム固有ID。`/cil give` や他プラグイン参照に使用
- `parentShortname`  
  ベースとなるRustアイテム shortname  
  旧キー `parent` も互換対応
- `maxStackSize`  
  最大スタック数（既定: 1）
- `category`  
  任意カテゴリ文字列（既定: Weapon）
- `defaultName`  
  表示名  
  旧キー `displayName` も互換対応
- `defaultSkinId`  
  適用するWorkshop skin ID  
  旧キー `skinId` も互換対応

**よく使う任意フィールド**
- `description`
- `rarity`
- `muteAllSounds`
- `passiveEffects`
- `perks`
- `exportPerks`
- `loots`
- `setId`
- `setBonuses`
- `visualEffects`
- `customEffects`
- `igniteEffect`
- `glowLoopEffect`
- `humLoopEffect`
- `swingEffect`
- `criticalEffect`
- `parryEffect`
- `descriptionLines`
- `uiStats`
- `uiHeaderColor`
- `uiAccentColor`
- `iconTint`
- `iconSprite`
- `zlMinLevel`
- `zlGatherBonusPct`

---

## Auto-generated Sample Item / 自動生成サンプル

### English
On first server start, the plugin writes:
```txt
oxide/data/CustomItemLoader/lightsaber.json
```

**Minimal sample**
```json
{
  "muteAllSounds": true,
  "shortname": "lightsaber",
  "parent": "longsword",
  "stackSize": 1,
  "category": "Weapon",
  "displayName": "Lightsaber",
  "skinId": -1469578201
}
```

Legacy keys `parent`, `displayName`, and `skinId` are mapped internally to `parentShortname`, `defaultName`, and `defaultSkinId`.

### 日本語
初回起動時、次のファイルが生成されます:
```txt
oxide/data/CustomItemLoader/lightsaber.json
```

**最小サンプル**
```json
{
  "muteAllSounds": true,
  "shortname": "lightsaber",
  "parent": "longsword",
  "stackSize": 1,
  "category": "Weapon",
  "displayName": "Lightsaber",
  "skinId": -1469578201
}
```

互換キー `parent` / `displayName` / `skinId` は、読み込み時に `parentShortname` / `defaultName` / `defaultSkinId` へ内部変換されます。

---

## Extended Example Usage / 拡張サンプルの使い方

### English
You can create another file such as `lightsaber_blue.json` and reuse it as a template by changing `defaultName`, `defaultSkinId`, colors, FX, and integration fields.

**Basic workflow**
1. Save a JSON file under `oxide/data/CustomItemLoader/`
2. Wait for auto-reload or run `oxide.reload CustomItemLoader`
3. Use `/cil give <shortname>` to test the item and its FX

### 日本語
`lightsaber_blue.json` のような別JSONを追加し、`defaultName`、`defaultSkinId`、色、FX、各種連携項目を差し替えることで、他のカスタムアイテムのテンプレートとして流用できます。

**基本手順**
1. `oxide/data/CustomItemLoader/` に JSON を保存
2. 数秒以内の自動リロードを待つ、または `oxide.reload CustomItemLoader` を実行
3. `/cil give <shortname>` で配布し、挙動とFXを確認

---

## Notes / 補足

### English
- The plugin can operate on its own, but becomes more powerful when combined with integration plugins.
- It supports legacy key compatibility for older JSON item definitions.
- The data folder watcher helps reduce reload friction during iterative item editing.

### 日本語
- このプラグインは単体でも動作しますが、連携プラグインと組み合わせることでより強力になります。
- 旧形式のJSONアイテム定義に対する互換キーもサポートしています。
- データフォルダ監視により、繰り返し編集時の再読み込み負担を軽減できます。

---

## Summary / まとめ

**EN:** CustomItemLoader is a flexible bilingual-friendly custom item framework for Rust/uMod servers, centered on JSON-driven item definitions, validation, FX support, and plugin integrations.  
**JP:** CustomItemLoader は、JSON駆動のアイテム定義、検証、FX対応、プラグイン連携を中核とした、Rust/uMod サーバー向けの柔軟なカスタムアイテム基盤です。
