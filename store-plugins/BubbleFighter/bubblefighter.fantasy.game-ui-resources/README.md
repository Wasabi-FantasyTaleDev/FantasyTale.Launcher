# Game Interface Graphics

[简体中文](README.zh.md)

A data-only package (`kind = "assets"`) with the game's own interface sprites, fonts and the editor appearance table
that the shared game UI library draws with. Packages that draw native-looking windows in the overlay, such as Ban Pick,
declare `requires = ["bubblefighter.fantasy.game-ui-resources"]` and use it as their `BfUiProvider` resource root:

```js
const root = overlayBus.packageUrl('bubblefighter.fantasy.game-ui-resources', 'ui.json').replace(/ui\.json$/, '');
```

## The data folder is not in the repository

The sprites and fonts come from the game clients and must never be committed (`store-plugins/**/data/` is ignored).
`tools/game-ui/pack-resources.py` turns a prepared resource root into the package data: theme sprites as lossless
WebP (every pixel checked; the theme `manifest.json` and `ui.json` record `imageFormat: "webp"`), fonts as WOFF2 with
`fonts/fonts.json` mapping original file names to stored ones.

```text
ui.json                      editor appearances per theme
v1/, v2/, v3/                theme manifest.json and sprites (v3: 2017 and later clients such as kr-1256)
fonts/                       WOFF2 fonts and fonts.json
sounds/                      interface sounds and their manifest.json
```

## Filling it at packaging time

```sh
python -X utf8 tools/game-ui/pack-resources.py <prepared resource root> <generated folder>
python tools/publish-store/stage_assets_package.py \
  --package store-plugins/BubbleFighter/bubblefighter.fantasy.game-ui-resources \
  --pack <generated folder> --output <new staging folder>
```

The staging script refuses links and unexpected entries, checks every theme manifest and that `fonts.json` names
existing files.
